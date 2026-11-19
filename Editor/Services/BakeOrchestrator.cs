using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class BakeOrchestrator : IBakeOrchestrator
    {
        private const string FallbackOutputFolder          = "Assets/GeneratedTextures";
        private const string PostProcessShaderRelativePath =
            "Shaders/Compute/PostProcess.compute";

        private static ComputeShader _cachedPostProcessShader;

        private readonly MeshFormatService        _meshFormat       = new MeshFormatService();
        private readonly OcclusionGeometryBuilder _occlusionBuilder = new OcclusionGeometryBuilder();
        private readonly IAOBaker                 _aoBaker          = new AOBaker();
        private readonly ICurvatureBaker          _curvatureBaker   = new CurvatureBaker();

        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Get(key), args);

        private static ComputeShader GetPostProcessShader()
        {
            if (_cachedPostProcessShader == null)
                _cachedPostProcessShader = FastAOBakerPaths.LoadAsset<ComputeShader>(PostProcessShaderRelativePath);
            return _cachedPostProcessShader;
        }

        public async Task ExecuteBakePipelineAsync(BakeState state, CancellationToken cancellationToken = default)
        {
            var bakedOccluderMeshes = new List<Mesh>();
            var savedAssets         = new List<(string assetPath, int resolution)>();

            try
            {
                if (state.TargetMeshes.Count == 0)
                {
                    BakeStore.Dispatch(new BakeErrorAction(L("Msg_NoTargetMeshes")));
                    return;
                }

                bool bakeAO        = true;
                bool bakeCurvature = state.CurvatureSettings.BakeEnabled;

                var outputSettings   = state.OutputSettings;
                // Bake natively at the requested output resolution (128..4096) — no 1024 upscale blur!
                int outputResolution = outputSettings.OutputResolution;

                var occluders = CollectOccluderMeshes(state.OccluderMeshes, bakedOccluderMeshes);

                float perMesh = 1f / state.TargetMeshes.Count;

                string lastFolder    = null;
                int    successCount  = 0;
                var    errorMessages = new System.Text.StringBuilder();

                for (int i = 0; i < state.TargetMeshes.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var    go        = state.TargetMeshes[i];
                    float  baseRatio = i * perMesh;
                    string meshLabel = $"[{i + 1}/{state.TargetMeshes.Count}]";

                    Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.02f,
                        $"{meshLabel} {L("Msg_Preparing", go.name)}");

                    var mesh = ExtractMesh(go, out Transform meshTransform, out Renderer renderer, out bool isBakedMesh);
                    if (mesh == null)
                    {
                        string msg = $"{meshLabel} {L("Msg_NoMeshFound", go.name)}";
                        Debug.LogError($"[AO Baker] {msg}");
                        errorMessages.AppendLine(msg);
                        BakeStore.Dispatch(new BakeErrorAction(msg));
                        continue;
                    }

                    Material[] materials = renderer != null
                        ? renderer.sharedMaterials
                        : Array.Empty<Material>();
                    int subMeshCount   = mesh.subMeshCount;
                    int effectiveCount = Mathf.Min(materials.Length, subMeshCount);

                    Debug.Log($"[AO Baker] {meshLabel} '{go.name}': " +
                        $"マテリアル {materials.Length} 個, サブメッシュ {subMeshCount} 個を認識 " +
                        $"→ {effectiveCount} 個のテクスチャを出力 ({outputResolution}px, 上書き保存: {outputSettings.OverwriteExisting})");

                    if (materials.Length != subMeshCount)
                        Debug.LogWarning($"[AO Baker] '{go.name}': " +
                            $"マテリアル数 ({materials.Length}) とサブメッシュ数 ({subMeshCount}) が一致しません。" +
                            $"{effectiveCount} 個を処理します。");

                    if (effectiveCount == 0)
                    {
                        string msg = $"{meshLabel} '{go.name}': 処理対象のサブメッシュがありません。スキップします。";
                        Debug.LogWarning($"[AO Baker] {msg}");
                        errorMessages.AppendLine(msg);
                        if (isBakedMesh && mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                        continue;
                    }

                    // OcclusionGeometry and PreparedMeshData are built ONCE per target mesh and
                    // shared across all submeshes (using the actual renderer/proxy Transform).
                    OcclusionGeometry occlusionGeometry = null;
                    PreparedMeshData  preparedMesh      = null;
                    try
                    {
                        Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.05f,
                            $"{meshLabel} {L("Msg_BuildingBVH")}");
                        await Task.Yield();

                        occlusionGeometry = await _occlusionBuilder.BuildAsync(
                            mesh, meshTransform, occluders,
                            state.AOSettings.UseSelfOcclusion,
                            state.AOSettings.UseMutualOcclusion,
                            cancellationToken);

                        preparedMesh = _meshFormat.PrepareMesh(
                            mesh, meshTransform, outputSettings.UVChannel);

                        float perSubMesh = perMesh / effectiveCount;

                        for (int sub = 0; sub < effectiveCount; sub++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            float  subStart = baseRatio + sub * perSubMesh;
                            string subLabel = effectiveCount > 1
                                ? $"{meshLabel}[{sub + 1}/{effectiveCount}]"
                                : meshLabel;

                            if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                            {
                                Debug.LogWarning($"[AO Baker] {subLabel} '{go.name}' サブメッシュ {sub}: " +
                                    "三角形以外のトポロジーのためスキップします。");
                                continue;
                            }

                            Material mat     = sub < materials.Length ? materials[sub] : null;
                            string   matName = mat != null && !string.IsNullOrEmpty(mat.name)
                                ? mat.name
                                : $"sub{sub}";

                            string outputFolder = ResolveMaterialOutputFolder(mat, outputSettings.OutputFolder);
                            EnsureOutputFolder(outputFolder);
                            if (lastFolder == null) lastFolder = outputFolder;

                            string texBaseName = effectiveCount > 1
                                ? $"{go.name}_{matName}"
                                : go.name;

                            BakeContext   subContext      = null;
                            RenderTexture aoResult        = null;
                            RenderTexture curvatureResult = null;
                            try
                            {
                                subContext = _meshFormat.CreateSubMeshContext(
                                    preparedMesh, outputResolution, sub);

                                // ---- AO bake ----
                                if (bakeAO)
                                {
                                    float aoBase = bakeCurvature ? 0.5f : 1.0f;
                                    var innerProgress = new Progress<(float p, string msg)>(t =>
                                    {
                                        float mapped = subStart + perSubMesh * (t.p * aoBase);
                                        BakeStore.Dispatch(new UpdateProgressAction(
                                            BakeStatus.Baking, mapped, $"{subLabel} [AO] {t.msg}"));
                                    });

                                    aoResult = await _aoBaker.ComputeAOAsync(
                                        subContext, occlusionGeometry, state.AOSettings,
                                        innerProgress, cancellationToken);

                                    var processed = ApplyPostProcess(aoResult, outputSettings, outputResolution);
                                    if (!ReferenceEquals(processed, aoResult))
                                    {
                                        aoResult.Release();
                                        aoResult = processed;
                                    }

                                    Dispatch(BakeStatus.Baking, subStart + perSubMesh * (aoBase * 0.97f),
                                        $"{subLabel} {L("Msg_SavingAO", go.name)}");
                                    await Task.Yield();

                                    string aoPath = WriteTexturePng(aoResult, texBaseName, "BakedAO",
                                        outputFolder, outputSettings.OverwriteExisting);
                                    savedAssets.Add((aoPath, outputResolution));
                                    Debug.Log($"[AO Baker] {subLabel} '{go.name}' (マテリアル: {matName}): " +
                                        $"AO テクスチャを保存しました → {aoPath}");
                                    successCount++;
                                }

                                // ---- Curvature bake ----
                                if (bakeCurvature)
                                {
                                    float curvStart2 = bakeAO ? 0.5f : 0.0f;
                                    var curvProgress = new Progress<(float p, string msg)>(t =>
                                    {
                                        float mapped = subStart + perSubMesh * (curvStart2 + t.p * (1.0f - curvStart2));
                                        BakeStore.Dispatch(new UpdateProgressAction(
                                            BakeStatus.Baking, mapped, $"{subLabel} [Curvature] {t.msg}"));
                                    });

                                    curvatureResult = await _curvatureBaker.ComputeCurvatureAsync(
                                        subContext, state.CurvatureSettings, curvProgress, cancellationToken);

                                    Dispatch(BakeStatus.Baking, subStart + perSubMesh * 0.97f,
                                        $"{subLabel} {L("Msg_SavingCurvature", go.name)}");
                                    await Task.Yield();

                                    string curvPath = WriteTexturePng(curvatureResult, texBaseName, "BakedCurvature",
                                        outputFolder, outputSettings.OverwriteExisting);
                                    savedAssets.Add((curvPath, outputResolution));
                                    Debug.Log($"[AO Baker] {subLabel} '{go.name}' (マテリアル: {matName}): " +
                                        $"Curvature テクスチャを保存しました → {curvPath}");
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception subEx)
                            {
                                string msg = $"{subLabel} '{go.name}' (マテリアル: {matName}): {subEx.Message}";
                                Debug.LogError($"[AO Baker] {msg}\n{subEx}");
                                errorMessages.AppendLine(msg);
                            }
                            finally
                            {
                                subContext?.Dispose();
                                aoResult?.Release();
                                curvatureResult?.Release();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception meshEx)
                    {
                        string msg = L("Msg_Skipped", go.name, meshEx.Message);
                        Debug.LogError($"[AO Baker] {msg}\n{meshEx}");
                        errorMessages.AppendLine(msg);
                        BakeStore.Dispatch(new BakeErrorAction(msg));
                    }
                    finally
                    {
                        preparedMesh?.Dispose();
                        occlusionGeometry?.Dispose();
                        if (isBakedMesh && mesh != null)
                            UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }

                // Batch import and configure TextureImporters once (avoids double-import per texture)
                if (savedAssets.Count > 0)
                {
                    ImportAndConfigureSavedTextures(savedAssets);
                }

                // Highlight the first output folder in the Project window
                if (!string.IsNullOrEmpty(lastFolder))
                {
                    var folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lastFolder);
                    if (folderAsset != null)
                    {
                        Selection.activeObject = folderAsset;
                        EditorGUIUtility.PingObject(folderAsset);
                        AssetDatabase.OpenAsset(folderAsset);
                    }
                }

                if (successCount > 0)
                {
                    BakeStore.Dispatch(new BakeCompletedAction());
                }
                else
                {
                    string summary = errorMessages.Length > 0
                        ? errorMessages.ToString().TrimEnd()
                        : L("Msg_NoTargetMeshes");
                    BakeStore.Dispatch(new BakeErrorAction(summary));
                }
            }
            catch (OperationCanceledException)
            {
                if (savedAssets.Count > 0)
                    ImportAndConfigureSavedTextures(savedAssets);

                Debug.Log("[AO Baker] Bake cancelled by user.");
                BakeStore.Dispatch(new BakeCancelledAction());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AO Baker] Fatal error: {ex}");
                BakeStore.Dispatch(new BakeErrorAction(ex.Message));
            }
            finally
            {
                // Always clean up baked occluder meshes (fixes memory leak on SkinnedMeshRenderer occluders)
                foreach (var bakedMesh in bakedOccluderMeshes)
                {
                    if (bakedMesh != null)
                        UnityEngine.Object.DestroyImmediate(bakedMesh);
                }
            }
        }

        private static string ResolveMaterialOutputFolder(Material mat, string userFolder)
        {
            if (!string.IsNullOrEmpty(userFolder))
                return userFolder;

            if (mat != null)
            {
                Texture mainTex = mat.mainTexture;
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

        private static List<(Mesh mesh, Transform transform)> CollectOccluderMeshes(
            IReadOnlyList<GameObject> occluderObjects,
            List<Mesh> bakedMeshesToDispose)
        {
            var result = new List<(Mesh mesh, Transform transform)>();
            if (occluderObjects == null) return result;

            foreach (var go in occluderObjects)
            {
                if (go == null) continue;

                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    var proxyMr = mr != null
                        ? NdmfProxyResolver.GetProxyRenderer(mr) as MeshRenderer
                        : null;
                    var proxyMf = proxyMr?.GetComponent<MeshFilter>();
                    if (proxyMf?.sharedMesh != null)
                        result.Add((proxyMf.sharedMesh, proxyMr.transform));
                    else
                        result.Add((mf.sharedMesh, mf.transform));
                    continue;
                }

                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null)
                {
                    var sourceSmr = NdmfProxyResolver.GetProxyRenderer(smr) as SkinnedMeshRenderer ?? smr;
                    var baked = new Mesh { name = sourceSmr.sharedMesh.name + "_occluder_baked" };
                    sourceSmr.BakeMesh(baked);
                    bakedMeshesToDispose.Add(baked);
                    result.Add((baked, sourceSmr.transform));
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts a Mesh together with the actual Transform and Renderer (including NDMF preview proxies).
        /// </summary>
        private static Mesh ExtractMesh(
            GameObject go,
            out Transform meshTransform,
            out Renderer renderer,
            out bool isBakedMesh)
        {
            meshTransform = go.transform;
            renderer      = null;
            isBakedMesh   = false;

            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var proxyMr = mr != null
                    ? NdmfProxyResolver.GetProxyRenderer(mr) as MeshRenderer
                    : null;
                var proxyMf = proxyMr?.GetComponent<MeshFilter>();
                if (proxyMf?.sharedMesh != null)
                {
                    meshTransform = proxyMr.transform;
                    renderer      = proxyMr;
                    return proxyMf.sharedMesh;
                }

                meshTransform = mf.transform;
                renderer      = mr != null ? (Renderer)mr : go.GetComponentInChildren<Renderer>();
                return mf.sharedMesh;
            }

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                var sourceSmr = NdmfProxyResolver.GetProxyRenderer(smr) as SkinnedMeshRenderer ?? smr;
                var uvSource  = sourceSmr.sharedMesh;

                var baked = new Mesh { name = uvSource.name + "_baked" };
                sourceSmr.BakeMesh(baked);

                // BakeMesh only writes skinned vertex positions — copy UVs from sharedMesh.
                var uvList = new List<Vector2>();
                for (int ch = 0; ch < 8; ch++)
                {
                    uvList.Clear();
                    uvSource.GetUVs(ch, uvList);
                    if (uvList.Count > 0)
                        baked.SetUVs(ch, uvList);
                }

                meshTransform = sourceSmr.transform;
                renderer      = sourceSmr;
                isBakedMesh   = true;
                return baked;
            }

            return null;
        }

        /// <summary>
        /// Runs post-processing in the geometrically correct order:
        /// 1. Separable 1D Gaussian blur (valid UV texels only — no edge bleed from padding)
        /// 2. Jump Flooding Algorithm (JFA) dilation — O(log2(R)) passes instead of O(R^2)
        /// 3. Shadow color tint (linear-consistent) + 1-LSB triangular dithering + opaque alpha
        /// </summary>
        private static RenderTexture ApplyPostProcess(
            RenderTexture src, OutputSettings settings, int resolution)
        {
            var ppShader = GetPostProcessShader();
            if (ppShader == null)
            {
                Debug.LogWarning($"[AO Baker] PostProcess shader not found at {FastAOBakerPaths.ResolveAssetPath(PostProcessShaderRelativePath)}. Skipping post-processing.");
                return src;
            }

            bool doBlur   = settings.GaussianBlurEnabled && settings.GaussianBlurPasses > 0;
            bool doDilate = settings.DilationPixels > 0;

            int tg = Mathf.CeilToInt(resolution / 8f);
            ppShader.SetInt("_Resolution", resolution);

            // Ping-pong ARGBHalf RTs (read directly from src on first pass — avoids extra CopyTexture)
            RenderTexture currentSrc = src;
            RenderTexture rtA        = CreateRT(resolution, RenderTextureFormat.ARGBHalf);
            RenderTexture rtB        = null;

            RenderTexture GetNextTarget()
            {
                if (ReferenceEquals(currentSrc, rtA))
                {
                    if (rtB == null) rtB = CreateRT(resolution, RenderTextureFormat.ARGBHalf);
                    return rtB;
                }
                return rtA;
            }

            try
            {
                // 1. Separable Gaussian Blur (BEFORE dilation so alpha still marks exact UV islands)
                if (doBlur)
                {
                    int kBlurH = ppShader.FindKernel("GaussianBlurH");
                    int kBlurV = ppShader.FindKernel("GaussianBlurV");

                    for (int pass = 0; pass < settings.GaussianBlurPasses; pass++)
                    {
                        var dstH = GetNextTarget();
                        ppShader.SetTexture(kBlurH, "_Input",  currentSrc);
                        ppShader.SetTexture(kBlurH, "_Output", dstH);
                        ppShader.Dispatch(kBlurH, tg, tg, 1);
                        currentSrc = dstH;

                        var dstV = GetNextTarget();
                        ppShader.SetTexture(kBlurV, "_Input",  currentSrc);
                        ppShader.SetTexture(kBlurV, "_Output", dstV);
                        ppShader.Dispatch(kBlurV, tg, tg, 1);
                        currentSrc = dstV;
                    }
                }

                // 2. Jump Flooding Algorithm (JFA) Dilation — O(log2(R)) passes
                if (doDilate)
                {
                    int kInit  = ppShader.FindKernel("JFAInit");
                    int kStep  = ppShader.FindKernel("JFAStep");
                    int kApply = ppShader.FindKernel("JFAApply");

                    // RGFloat preserves exact integer pixel coordinates up to 4096+
                    var seedA = CreateRT(resolution, RenderTextureFormat.RGFloat);
                    var seedB = CreateRT(resolution, RenderTextureFormat.RGFloat);
                    try
                    {
                        ppShader.SetTexture(kInit, "_Input",   currentSrc);
                        ppShader.SetTexture(kInit, "_SeedOut", seedA);
                        ppShader.Dispatch(kInit, tg, tg, 1);

                        int step = 1;
                        while (step < settings.DilationPixels)
                            step <<= 1;

                        RenderTexture seedIn  = seedA;
                        RenderTexture seedOut = seedB;
                        while (step >= 1)
                        {
                            ppShader.SetInt("_JumpStep", step);
                            ppShader.SetTexture(kStep, "_SeedIn",  seedIn);
                            ppShader.SetTexture(kStep, "_SeedOut", seedOut);
                            ppShader.Dispatch(kStep, tg, tg, 1);

                            (seedIn, seedOut) = (seedOut, seedIn);
                            step >>= 1;
                        }

                        var dstDilate = GetNextTarget();
                        ppShader.SetInt("_DilationRadius", settings.DilationPixels);
                        ppShader.SetTexture(kApply, "_Input",  currentSrc);
                        ppShader.SetTexture(kApply, "_SeedIn", seedIn);
                        ppShader.SetTexture(kApply, "_Output", dstDilate);
                        ppShader.Dispatch(kApply, tg, tg, 1);
                        currentSrc = dstDilate;
                    }
                    finally
                    {
                        seedA.Release();
                        seedB.Release();
                    }
                }

                // 3. Shadow color (converted to linear to match sRGBTexture=false output) + 1-LSB dither + alpha=1
                {
                    int kColor = ppShader.FindKernel("ApplyShadowColor");
                    Color sc   = settings.ShadowColor.linear;
                    var dstCol = GetNextTarget();

                    ppShader.SetVector("_ShadowColor", new Vector4(sc.r, sc.g, sc.b, 1f));
                    ppShader.SetTexture(kColor, "_Input",  currentSrc);
                    ppShader.SetTexture(kColor, "_Output", dstCol);
                    ppShader.Dispatch(kColor, tg, tg, 1);
                    currentSrc = dstCol;
                }

                // Release whichever scratch RT is not the final result
                if (rtA != null && !ReferenceEquals(currentSrc, rtA)) rtA.Release();
                if (rtB != null && !ReferenceEquals(currentSrc, rtB)) rtB.Release();
                return currentSrc;
            }
            catch
            {
                rtA?.Release();
                rtB?.Release();
                throw;
            }
        }

        private static RenderTexture CreateRT(int res, RenderTextureFormat format)
        {
            var rt = new RenderTexture(res, res, 0, format, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode        = FilterMode.Bilinear,
                wrapMode          = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// Reads the native-resolution RenderTexture and writes the PNG to disk.
        /// TextureImporter configuration is deferred and batched in ImportAndConfigureSavedTextures.
        /// </summary>
        private static string WriteTexturePng(
            RenderTexture rt, string meshName, string prefix,
            string outputFolder, bool overwrite)
        {
            var readTex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
            var prev    = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                readTex.Apply(false, false);

                string safeName  = string.Join("_", meshName.Split(Path.GetInvalidFileNameChars()));
                string baseName  = $"{prefix}_{safeName}";
                string assetPath = $"{outputFolder}/{baseName}.png";

                if (!overwrite)
                    assetPath = GetUniqueAssetPath(assetPath);

                string fullPath = Path.Combine(
                    Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));

                File.WriteAllBytes(fullPath, readTex.EncodeToPNG());
                return assetPath;
            }
            finally
            {
                RenderTexture.active = prev;
                UnityEngine.Object.DestroyImmediate(readTex);
            }
        }

        /// <summary>
        /// Imports all saved textures and configures their TextureImporters in a single batched edit,
        /// avoiding redundant double-imports per texture.
        /// </summary>
        private static void ImportAndConfigureSavedTextures(List<(string assetPath, int resolution)> savedAssets)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (assetPath, maxSize) in savedAssets)
                {
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer == null) continue;

                    bool dirty = false;
                    if (importer.maxTextureSize != maxSize)
                    {
                        importer.maxTextureSize = maxSize;
                        dirty = true;
                    }
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                    {
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        dirty = true;
                    }
                    if (importer.sRGBTexture)
                    {
                        importer.sRGBTexture = false;
                        dirty = true;
                    }

                    if (dirty)
                    {
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static string GetUniqueAssetPath(string assetPath)
        {
            string fullPath = Path.Combine(
                Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
                return assetPath;

            string dir  = Path.GetDirectoryName(assetPath).Replace('\\', '/');
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
