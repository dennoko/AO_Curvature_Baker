using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class CurvatureBaker : ICurvatureBaker
    {
        private const string CurvatureShaderPath =
            "Assets/Editor/AO_Curvature_Baker/Shaders/Compute/Curvature.compute";

        public async Task<RenderTexture> ComputeCurvatureAsync(
            BakeContext context,
            CurvatureSettings settings,
            IProgress<(float progress, string message)> progress)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(CurvatureShaderPath);
            if (shader == null)
                throw new InvalidOperationException($"Compute shader not found: {CurvatureShaderPath}");

            if (context.TriangleCount == 0)
                throw new InvalidOperationException("Mesh has no triangles to bake.");

            int rasterKernel    = shader.FindKernel("RasterizeUV");
            int curvatureKernel = shader.FindKernel("ComputeCurvature");
            if (rasterKernel < 0)
                throw new InvalidOperationException("Kernel 'RasterizeUV' not found in Curvature.compute.");
            if (curvatureKernel < 0)
                throw new InvalidOperationException("Kernel 'ComputeCurvature' not found in Curvature.compute.");

            int res = context.TextureResolution;
            int tg  = Mathf.CeilToInt(res / 8f);

            var positionRT   = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var normalRT     = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var curvatureRT  = CreateRT(res, RenderTextureFormat.ARGBFloat);

            // --- Pass 1: Rasterize UV ---
            progress?.Report((0.05f, "Rasterizing UV space..."));
            await Task.Yield();

            shader.SetBuffer(rasterKernel, "_Vertices",     context.VertexBuffer);
            shader.SetBuffer(rasterKernel, "_Normals",      context.NormalBuffer);
            shader.SetBuffer(rasterKernel, "_UVs",          context.UVBuffer);
            shader.SetBuffer(rasterKernel, "_Indices",      context.IndexBuffer);
            shader.SetInt("_TriangleCount",                 context.TriangleCount);
            shader.SetInt("_Resolution",                    res);
            shader.SetTexture(rasterKernel, "_PositionMap", positionRT);
            shader.SetTexture(rasterKernel, "_NormalMap",   normalRT);
            shader.Dispatch(rasterKernel, tg, tg, 1);
            AsyncGPUReadback.WaitAllRequests();

            // --- Pass 2: Compute Curvature ---
            progress?.Report((0.55f, "Computing curvature..."));
            await Task.Yield();

            shader.SetTexture(curvatureKernel, "_PositionMap",      positionRT);
            shader.SetTexture(curvatureKernel, "_NormalMap",        normalRT);
            shader.SetTexture(curvatureKernel, "_CurvatureOutput",  curvatureRT);
            shader.SetInt("_Resolution",                            res);
            shader.SetInt("_CurvatureMode",                         (int)settings.Mode);
            shader.SetFloat("_Strength",                            settings.Strength);
            shader.SetFloat("_Bias",                                settings.Bias);
            shader.Dispatch(curvatureKernel, tg, tg, 1);
            AsyncGPUReadback.WaitAllRequests();

            positionRT.Release();
            normalRT.Release();

            progress?.Report((0.97f, "Finalizing curvature output..."));
            await Task.Yield();

            return curvatureRT;
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
