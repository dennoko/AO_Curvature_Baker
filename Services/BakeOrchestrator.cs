using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class BakeOrchestrator : IBakeOrchestrator
    {
        private const int DefaultResolution     = 1024;
        private const string OutputFolder       = "Assets/GeneratedTextures";

        private readonly MeshFormatService        _meshFormat        = new MeshFormatService();
        private readonly OcclusionGeometryBuilder _occlusionBuilder  = new OcclusionGeometryBuilder();
        private readonly IAOBaker                 _aoBaker           = new AOBaker();

        public async Task ExecuteBakePipelineAsync(BakeState state)
        {
            try
            {
                if (state.TargetMeshes.Count == 0)
                {
                    BakeStore.Dispatch(new BakeErrorAction("No target meshes registered."));
                    return;
                }

                EnsureOutputFolder();

                // Collect occluder meshes (extract Mesh + Transform pairs)
                var occluders = CollectOccluderMeshes(state.OccluderMeshes);
                var bakedOccluderMeshes = new List<Mesh>(); // Track baked meshes for cleanup

                float perMesh = 1f / state.TargetMeshes.Count;

                for (int i = 0; i < state.TargetMeshes.Count; i++)
                {
                    var    go        = state.TargetMeshes[i];
                    float  baseRatio = i * perMesh;
                    string label     = $"[{i + 1}/{state.TargetMeshes.Count}]";

                    Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.02f,
                        $"{label} Preparing '{go.name}'...");

                    bool isBakedMesh = false;
                    var mesh = ExtractMesh(go, out isBakedMesh);
                    if (mesh == null)
                    {
                        BakeStore.Dispatch(new BakeErrorAction(
                            $"No mesh found on '{go.name}'. Attach a MeshFilter or SkinnedMeshRenderer."));
                        continue;
                    }

                    BakeContext context = null;
                    OcclusionGeometry occlusionGeometry = null;
                    RenderTexture aoResult = null;
                    try
                    {
                        context = _meshFormat.BuildContext(mesh, DefaultResolution);

                        // Build occlusion geometry: target mesh + occluder meshes
                        Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.05f,
                            $"{label} Building occlusion geometry...");

                        Transform targetTransform = go.transform;
                        occlusionGeometry = _occlusionBuilder.Build(
                            mesh, targetTransform, occluders);

                        var innerProgress = new Progress<(float p, string msg)>(t =>
                        {
                            float mapped = baseRatio + perMesh * t.p;
                            BakeStore.Dispatch(new UpdateProgressAction(
                                BakeStatus.Baking, mapped, $"{label} {t.msg}"));
                        });

                        aoResult = await _aoBaker.ComputeAOAsync(
                            context, occlusionGeometry, state.AOSettings, innerProgress);

                        Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.97f,
                            $"{label} Saving texture for '{go.name}'...");
                        await Task.Yield();

                        SaveTexture(aoResult, go.name);
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

        private static void SaveTexture(RenderTexture rt, string meshName)
        {
            // Read the float RT into an ARGB32 texture, clamp values to [0,1] automatically
            var readTex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readTex.Apply();
            RenderTexture.active = prev;

            string safeName = string.Join("_", meshName.Split(Path.GetInvalidFileNameChars()));
            string assetPath = $"{OutputFolder}/BakedAO_{safeName}.png";
            string fullPath  = Path.Combine(
                Directory.GetCurrentDirectory(), assetPath.Replace('/', Path.DirectorySeparatorChar));

            File.WriteAllBytes(fullPath, readTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(readTex);

            AssetDatabase.ImportAsset(assetPath);
        }

        private static void EnsureOutputFolder()
        {
            string fullPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                OutputFolder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }

        private static void Dispatch(BakeStatus status, float progress, string message) =>
            BakeStore.Dispatch(new UpdateProgressAction(status, progress, message));
    }
}
