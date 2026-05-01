using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class BakeOrchestrator : IBakeOrchestrator
    {
        private const int    SamplingResolution      = 1024;
        private const string FallbackOutputFolder    = "Assets/GeneratedTextures";
        private const string PostProcessShaderPath   =
            "Assets/Editor/AO_Curvature_Baker/Shaders/Compute/PostProcess.compute";

        private readonly MeshFormatService        _meshFormat       = new MeshFormatService();
        private readonly OcclusionGeometryBuilder _occlusionBuilder = new OcclusionGeometryBuilder();
        private readonly IAOBaker                 _aoBaker          = new AOBaker();
        private readonly ICurvatureBaker          _curvatureBaker   = new CurvatureBaker();

        public async Task ExecuteBakePipelineAsync(BakeState state)
        {
            try
            {
                if (state.TargetMeshes.Count == 0)
                {
                    BakeStore.Dispatch(new BakeErrorAction("No target meshes registered."));
                    return;
                }

                bool bakeAO        = true; // AO is always baked when the pipeline runs
                bool bakeCurvature = state.CurvatureSettings.BakeEnabled;

                var outputSettings = state.OutputSettings;
                int outputResolution = outputSettings.OutputResolution;

                // Collect occluder meshes (extract Mesh + Transform pairs)
                var occluders            = CollectOccluderMeshes(state.OccluderMeshes);
                var bakedOccluderMeshes  = new List<Mesh>(); // Track baked meshes for cleanup

                float perMesh = 1f / state.TargetMeshes.Count;

                for (int i = 0; i < state.TargetMeshes.Count; i++)
                {
                    var    go        = state.TargetMeshes[i];
                    float  baseRatio = i * perMesh;
                    string label     = $"[{i + 1}/{state.TargetMeshes.Count}]";

                    Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.02f,
                        $"{label} Preparing '{go.name}'...");

                    // Resolve output folder for this mesh
                    string outputFolder = ResolveOutputFolder(go, outputSettings.OutputFolder);
                    EnsureOutputFolder(outputFolder);

                    bool isBakedMesh = false;
                    var mesh = ExtractMesh(go, out isBakedMesh);
                    if (mesh == null)
                    {
                        BakeStore.Dispatch(new BakeErrorAction(
                            $"No mesh found on '{go.name}'. Attach a MeshFilter or SkinnedMeshRenderer."));
                        continue;
                    }

                    BakeContext       context          = null;
                    OcclusionGeometry occlusionGeometry = null;
                    RenderTexture     aoResult         = null;
                    RenderTexture     curvatureResult  = null;
                    try
                    {
                        context = _meshFormat.BuildContext(mesh, SamplingResolution);

                        // Build occlusion geometry: target mesh + occluder meshes
                        Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.05f,
                            $"{label} Building occlusion geometry...");

                        Transform targetTransform = go.transform;
                        occlusionGeometry = _occlusionBuilder.Build(
                            mesh, targetTransform, occluders);

                        // ---- AO bake ----
                        if (bakeAO)
                        {
                            float aoBase = bakeCurvature ? 0.5f : 1.0f;
                            var innerProgress = new Progress<(float p, string msg)>(t =>
                            {
                                float mapped = baseRatio + perMesh * (t.p * aoBase);
                                BakeStore.Dispatch(new UpdateProgressAction(
                                    BakeStatus.Baking, mapped, $"{label} [AO] {t.msg}"));
                            });

                            aoResult = await _aoBaker.ComputeAOAsync(
                                context, occlusionGeometry, state.AOSettings, innerProgress);

                            // Post-processing: dilation, blur, shadow colour
                            var processed = ApplyPostProcess(aoResult, outputSettings, SamplingResolution);
                            if (!ReferenceEquals(processed, aoResult))
                            {
                                aoResult.Release();
                                aoResult = processed;
                            }

                            Dispatch(BakeStatus.Baking, baseRatio + perMesh * (aoBase * 0.97f),
                                $"{label} Saving AO texture for '{go.name}'...");
                            await Task.Yield();

                            SaveTexture(aoResult, go.name, "BakedAO", outputFolder,
                                outputResolution, outputSettings.OverwriteExisting);
                        }

                        // ---- Curvature bake ----
                        if (bakeCurvature)
                        {
                            float curvStart = bakeAO ? 0.5f : 0.0f;
                            var curvProgress = new Progress<(float p, string msg)>(t =>
                            {
                                float mapped = baseRatio + perMesh * (curvStart + t.p * (1.0f - curvStart));
                                BakeStore.Dispatch(new UpdateProgressAction(
                                    BakeStatus.Baking, mapped, $"{label} [Curvature] {t.msg}"));
                            });

                            curvatureResult = await _curvatureBaker.ComputeCurvatureAsync(
                                context, state.CurvatureSettings, curvProgress);

                            Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.97f,
                                $"{label} Saving curvature texture for '{go.name}'...");
                            await Task.Yield();

                            SaveTexture(curvatureResult, go.name, "BakedCurvature", outputFolder,
                                outputResolution, outputSettings.OverwriteExisting);
                        }
                    }
                    catch (Exception meshEx)
                    {
                        BakeStore.Dispatch(new BakeErrorAction(
                            $"{label} Skipped '{go.name}': {meshEx.Message}"));
                    }
                    finally
                    {
                        context?.Dispose();
                        occlusionGeometry?.Dispose();
                        aoResult?.Release();
                        curvatureResult?.Release();
                        if (isBakedMesh && mesh != null)
                            UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }

                // Cleanup baked occluder meshes
                foreach (var bakedMesh in bakedOccluderMeshes)
                {
                    if (bakedMesh != null)
                        UnityEngine.Object.DestroyImmediate(bakedMesh);
                }

                AssetDatabase.Refresh();
                BakeStore.Dispatch(new BakeCompletedAction());
            }
            catch (Exception ex)
            {
                BakeStore.Dispatch(new BakeErrorAction(ex.Message));
            }
        }

        /// <summary>
        /// Resolves the output folder for a given target mesh.
        /// If the user specified an output folder, use it.
        /// Otherwise, try to find the main texture on the target mesh's material,
        /// and create a BakedAO/ subfolder at that texture's path.
        /// Falls back to the default folder.
        /// </summary>
        private static string ResolveOutputFolder(GameObject go, string userFolder)
        {
            if (!string.IsNullOrEmpty(userFolder))
                return userFolder;

            // Try to find the main texture path from the mesh renderer
            Renderer renderer = go.GetComponentInChildren<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Texture mainTex = renderer.sharedMaterial.mainTexture;
                if (mainTex != null)
                {
                    string texPath = AssetDatabase.GetAssetPath(mainTex);
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        string texDir = Path.GetDirectoryName(texPath).Replace('\\', '/');
                        return $"{texDir}/BakedAO";
                    }
                }
            }

            return FallbackOutputFolder;
        }

        /// <summary>
        /// Collects Mesh + Transform pairs from occluder GameObjects.
        /// SkinnedMeshRenderer meshes are baked to capture current pose.
        /// </summary>
        private static List<(Mesh mesh, Transform transform)> CollectOccluderMeshes(
            IReadOnlyList<GameObject> occluderObjects)
        {
            var result = new List<(Mesh mesh, Transform transform)>();
            if (occluderObjects == null) return result;

            foreach (var go in occluderObjects)
            {
                if (go == null) continue;

                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    result.Add((mf.sharedMesh, mf.transform));
                    continue;
                }

                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null)
                {
                    var source = smr.sharedMesh;
                    var baked = new Mesh { name = source.name + "_occluder_baked" };
                    smr.BakeMesh(baked);
                    result.Add((baked, smr.transform));
                }
            }

            return result;
        }

        private static Mesh ExtractMesh(GameObject go, out bool isBakedMesh)
        {
            isBakedMesh = false;

            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                var source = smr.sharedMesh;
                var baked = new Mesh { name = source.name + "_baked" };
                smr.BakeMesh(baked);

                // BakeMesh only writes skinned vertex positions — copy UVs from the source mesh.
                for (int ch = 0; ch < 8; ch++)
                {
                    var uvList = new List<Vector2>();
                    source.GetUVs(ch, uvList);
                    if (uvList.Count > 0)
                        baked.SetUVs(ch, uvList);
                }

                isBakedMesh = true;
                return baked;
            }
            return null;
        }

        /// <summary>
        /// Runs dilation, Gaussian blur, and shadow colour tint on the AO RenderTexture.
        /// Returns a new RT when any step runs, otherwise returns the original unchanged.
        /// The caller must release the original RT when the returned value differs.
        /// </summary>
        private static RenderTexture ApplyPostProcess(
            RenderTexture src, OutputSettings settings, int resolution)
        {
            bool doDilate = settings.DilationPixels > 0;
            bool doBlur   = settings.GaussianBlurEnabled && settings.GaussianBlurPasses > 0;
            bool doColor  = settings.ShadowColor != Color.black;

            if (!doDilate && !doBlur && !doColor)
                return src;

            var ppShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(PostProcessShaderPath);
            if (ppShader == null)
            {
                Debug.LogWarning($"[AO Baker] PostProcess shader not found at {PostProcessShaderPath}. Skipping post-processing.");
                return src;
            }

            int tg = Mathf.CeilToInt(resolution / 8f);

            RenderTexture rtA = CreateFloatRT(resolution);
            RenderTexture rtB = CreateFloatRT(resolution);

            // Copy source into rtA — use CopyTexture to ensure alpha (background mask) is preserved bit-exactly.
            Graphics.CopyTexture(src, rtA);

            // 1. Dilation
            if (doDilate)
            {
                int k = ppShader.FindKernel("Dilate");
                ppShader.SetInt("_Resolution",      resolution);
                ppShader.SetInt("_DilationRadius",  settings.DilationPixels);
                ppShader.SetTexture(k, "_Input",  rtA);
                ppShader.SetTexture(k, "_Output", rtB);
                ppShader.Dispatch(k, tg, tg, 1);
                (rtA, rtB) = (rtB, rtA);
            }

            // 2. Gaussian blur (N passes)
            if (doBlur)
            {
                int k = ppShader.FindKernel("GaussianBlur");
                ppShader.SetInt("_Resolution", resolution);
                for (int pass = 0; pass < settings.GaussianBlurPasses; pass++)
                {
                    ppShader.SetTexture(k, "_Input",  rtA);
                    ppShader.SetTexture(k, "_Output", rtB);
                    ppShader.Dispatch(k, tg, tg, 1);
                    (rtA, rtB) = (rtB, rtA);
                }
            }

            // 3. Shadow colour + alpha flatten (always run so alpha=0 background becomes opaque white)
            {
                int k = ppShader.FindKernel("ApplyShadowColor");
                Color sc = settings.ShadowColor;
                ppShader.SetVector("_ShadowColor", new Vector4(sc.r, sc.g, sc.b, sc.a));
                ppShader.SetInt("_Resolution", resolution);
                ppShader.SetTexture(k, "_Input",  rtA);
                ppShader.SetTexture(k, "_Output", rtB);
                ppShader.Dispatch(k, tg, tg, 1);
                (rtA, rtB) = (rtB, rtA);
            }

            rtB.Release();
            return rtA; // rtA holds the final result
        }

        private static RenderTexture CreateFloatRT(int res)
        {
            var rt = new RenderTexture(res, res, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// Saves a baked RenderTexture to disk with resolution scaling, duplicate name handling,
        /// and TextureImporter configuration.
        /// </summary>
        private static void SaveTexture(
            RenderTexture rt, string meshName, string prefix,
            string outputFolder, int outputResolution, bool overwrite)
        {
            // Read the float RT into an ARGB32 texture at sampling resolution
            var readTex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readTex.Apply();
            RenderTexture.active = prev;

            // Ensure all pixels are fully opaque (background pixels may have alpha=0 from
            // the AO shader; ApplyShadowColor normally handles this, but guard here too).
            var pixels = readTex.GetPixels32();
            bool needsAlphaFix = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a != 255) { needsAlphaFix = true; break; }
            }
            if (needsAlphaFix)
            {
                for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255;
                readTex.SetPixels32(pixels);
                readTex.Apply();
            }

            // Scale to output resolution if different from sampling resolution
            Texture2D outputTex = readTex;
            if (outputResolution != rt.width || outputResolution != rt.height)
            {
                outputTex = ResizeTexture(readTex, outputResolution, outputResolution);
                UnityEngine.Object.DestroyImmediate(readTex);
            }

            string safeName  = string.Join("_", meshName.Split(Path.GetInvalidFileNameChars()));
            string baseName  = $"{prefix}_{safeName}";
            string assetPath = $"{outputFolder}/{baseName}.png";

            // Handle duplicate filenames
            if (!overwrite)
            {
                assetPath = GetUniqueAssetPath(assetPath);
            }

            string fullPath = Path.Combine(
                Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));

            File.WriteAllBytes(fullPath, outputTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(outputTex);

            AssetDatabase.ImportAsset(assetPath);

            // Configure TextureImporter maxSize to match the output resolution
            ConfigureTextureImporter(assetPath, outputResolution);
        }

        /// <summary>
        /// Resizes a Texture2D to the target width/height using a temporary RenderTexture blit.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            var scaleRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            scaleRT.filterMode = FilterMode.Bilinear;

            var prevActive = RenderTexture.active;
            RenderTexture.active = scaleRT;

            Graphics.Blit(source, scaleRT);

            var result = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false, true);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(scaleRT);

            return result;
        }

        /// <summary>
        /// If the file already exists, appends " 1", " 2", etc. to match Unity's default behaviour.
        /// </summary>
        private static string GetUniqueAssetPath(string assetPath)
        {
            string fullPath = Path.Combine(
                Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
                return assetPath;

            string dir  = Path.GetDirectoryName(assetPath);
            string name = Path.GetFileNameWithoutExtension(assetPath);
            string ext  = Path.GetExtension(assetPath);

            int counter = 1;
            string candidate;
            do
            {
                candidate = $"{dir}/{name} {counter}{ext}";
                string candidateFull = Path.Combine(
                    Directory.GetCurrentDirectory(), candidate.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(candidateFull))
                    break;
                counter++;
            } while (true);

            return candidate;
        }

        /// <summary>
        /// Sets the TextureImporter maxSize to the given output resolution.
        /// </summary>
        private static void ConfigureTextureImporter(string assetPath, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = false;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        private static void EnsureOutputFolder(string outputFolder)
        {
            string fullPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                outputFolder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        private static void Dispatch(BakeStatus status, float progress, string message) =>
            BakeStore.Dispatch(new UpdateProgressAction(status, progress, message));
    }
}
