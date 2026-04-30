using System;
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

        private readonly MeshFormatService _meshFormat = new MeshFormatService();
        private readonly IAOBaker          _aoBaker    = new AOBaker();

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

                float perMesh = 1f / state.TargetMeshes.Count;

                for (int i = 0; i < state.TargetMeshes.Count; i++)
                {
                    var    go        = state.TargetMeshes[i];
                    float  baseRatio = i * perMesh;
                    string label     = $"[{i + 1}/{state.TargetMeshes.Count}]";

                    Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.02f,
                        $"{label} Preparing '{go.name}'...");

                    var mesh = ExtractMesh(go);
                    if (mesh == null)
                    {
                        BakeStore.Dispatch(new BakeErrorAction(
                            $"No mesh found on '{go.name}'. Attach a MeshFilter or SkinnedMeshRenderer."));
                        return;
                    }

                    BakeContext context = null;
                    RenderTexture aoResult = null;
                    try
                    {
                        context = _meshFormat.BuildContext(mesh, DefaultResolution);

                        var innerProgress = new Progress<(float p, string msg)>(t =>
                        {
                            float mapped = baseRatio + perMesh * t.p;
                            BakeStore.Dispatch(new UpdateProgressAction(
                                BakeStatus.Baking, mapped, $"{label} {t.msg}"));
                        });

                        aoResult = await _aoBaker.ComputeAOAsync(context, state.AOSettings, innerProgress);

                        Dispatch(BakeStatus.Baking, baseRatio + perMesh * 0.97f,
                            $"{label} Saving texture for '{go.name}'...");
                        await Task.Yield();

                        SaveTexture(aoResult, go.name);
                    }
                    finally
                    {
                        context?.Dispose();
                        aoResult?.Release();
                    }
                }

                AssetDatabase.Refresh();
                BakeStore.Dispatch(new BakeCompletedAction());
            }
            catch (Exception ex)
            {
                BakeStore.Dispatch(new BakeErrorAction(ex.Message));
            }
        }

        private static Mesh ExtractMesh(GameObject go)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var baked = new Mesh { name = smr.sharedMesh.name + "_baked" };
                smr.BakeMesh(baked);
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
