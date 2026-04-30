using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class AOBaker : IAOBaker
    {
        private const string ShaderAssetPath =
            "Assets/Editor/AO_Curvature_Baker/Shaders/Compute/AOBake.compute";

        public async Task<RenderTexture> ComputeAOAsync(
            BakeContext context,
            AOSettings settings,
            IProgress<(float progress, string message)> progress)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderAssetPath);
            if (shader == null)
                throw new InvalidOperationException($"Compute shader not found: {ShaderAssetPath}");

            int res = context.TextureResolution;
            int threadGroupsXY = Mathf.CeilToInt(res / 8f);

            var positionRT = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var normalRT   = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var aoRT       = CreateRT(res, RenderTextureFormat.ARGBFloat);

            // --- Pass 1: Rasterize mesh UVs into a G-buffer (position + normal maps) ---
            progress?.Report((0.05f, "Rasterizing UV space..."));
            await Task.Yield();

            int rasterKernel = shader.FindKernel("RasterizeUV");
            shader.SetBuffer(rasterKernel, "_Vertices", context.VertexBuffer);
            shader.SetBuffer(rasterKernel, "_Normals",  context.NormalBuffer);
            shader.SetBuffer(rasterKernel, "_UVs",      context.UVBuffer);
            shader.SetBuffer(rasterKernel, "_Indices",  context.IndexBuffer);
            shader.SetInt("_TriangleCount", context.TriangleCount);
            shader.SetInt("_Resolution",    res);
            shader.SetTexture(rasterKernel, "_PositionMap", positionRT);
            shader.SetTexture(rasterKernel, "_NormalMap",   normalRT);
            shader.Dispatch(rasterKernel, threadGroupsXY, threadGroupsXY, 1);

            // --- Pass 2: Trace AO rays from every valid texel ---
            progress?.Report((0.30f, "Tracing AO rays..."));
            await Task.Yield();

            int aoKernel = shader.FindKernel("TraceAO");
            shader.SetBuffer(aoKernel, "_Vertices", context.VertexBuffer);
            shader.SetBuffer(aoKernel, "_Normals",  context.NormalBuffer);
            shader.SetBuffer(aoKernel, "_Indices",  context.IndexBuffer);
            shader.SetTexture(aoKernel, "_PositionMap", positionRT);
            shader.SetTexture(aoKernel, "_NormalMap",   normalRT);
            shader.SetTexture(aoKernel, "_AOOutput",    aoRT);
            shader.SetInt("_TriangleCount",       context.TriangleCount);
            shader.SetInt("_Resolution",          res);
            shader.SetInt("_RayCount",            settings.RayCount);
            shader.SetFloat("_MaxDistance",       settings.MaxDistance);
            shader.SetInt("_UseSelfOcclusion",    settings.UseSelfOcclusion    ? 1 : 0);
            shader.SetInt("_UseMutualOcclusion",  settings.UseMutualOcclusion  ? 1 : 0);
            shader.Dispatch(aoKernel, threadGroupsXY, threadGroupsXY, 1);

            progress?.Report((0.95f, "Finalizing output..."));
            await Task.Yield();

            positionRT.Release();
            normalRT.Release();

            return aoRT;
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
    }
}
