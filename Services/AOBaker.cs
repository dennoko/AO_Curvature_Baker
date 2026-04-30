using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class AOBaker : IAOBaker
    {
        private const string AoShaderPath =
            "Assets/Editor/AO_Curvature_Baker/Shaders/Compute/AOBake.compute";
        private const string DenoiseShaderPath =
            "Assets/Editor/AO_Curvature_Baker/Shaders/Compute/Denoise.compute";

        public async Task<RenderTexture> ComputeAOAsync(
            BakeContext context,
            OcclusionGeometry occlusionGeometry,
            AOSettings settings,
            IProgress<(float progress, string message)> progress)
        {
            var aoShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(AoShaderPath);
            if (aoShader == null)
                throw new InvalidOperationException($"Compute shader not found: {AoShaderPath}");

            if (context.TriangleCount == 0)
                throw new InvalidOperationException("Mesh has no triangles to bake.");

            int rasterKernel = aoShader.FindKernel("RasterizeUV");
            int aoKernel     = aoShader.FindKernel("TraceAO");
            if (rasterKernel < 0)
                throw new InvalidOperationException("Kernel 'RasterizeUV' not found.");
            if (aoKernel < 0)
                throw new InvalidOperationException("Kernel 'TraceAO' not found.");

            int res = context.TextureResolution;

            var positionRT = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var normalRT   = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var aoRT       = CreateRT(res, RenderTextureFormat.ARGBFloat);

            // --- Pass 1: Rasterize UV ---
            progress?.Report((0.05f, "Rasterizing UV space..."));
            await Task.Yield();

            int threadGroupsXY = Mathf.CeilToInt(res / 8f);
            aoShader.SetBuffer(rasterKernel, "_Vertices", context.VertexBuffer);
            aoShader.SetBuffer(rasterKernel, "_Normals",  context.NormalBuffer);
            aoShader.SetBuffer(rasterKernel, "_UVs",      context.UVBuffer);
            aoShader.SetBuffer(rasterKernel, "_Indices",  context.IndexBuffer);
            aoShader.SetInt("_TriangleCount", context.TriangleCount);
            aoShader.SetInt("_Resolution",    res);
            aoShader.SetTexture(rasterKernel, "_PositionMap", positionRT);
            aoShader.SetTexture(rasterKernel, "_NormalMap",   normalRT);
            aoShader.Dispatch(rasterKernel, threadGroupsXY, threadGroupsXY, 1);
            AsyncGPUReadback.WaitAllRequests();

            // --- Pass 2: Bind occlusion geometry and BVH ---
            progress?.Report((0.12f, "Binding occlusion geometry..."));
            await Task.Yield();

            aoShader.SetBuffer(aoKernel, "_OccVertices",  occlusionGeometry.VertexBuffer);
            aoShader.SetBuffer(aoKernel, "_OccIndices",   occlusionGeometry.IndexBuffer);
            aoShader.SetBuffer(aoKernel, "_BVHNodes",     occlusionGeometry.BVHNodeBuffer);
            aoShader.SetBuffer(aoKernel, "_PrimIndices",  occlusionGeometry.PrimIndexBuffer);

            // --- Pass 3: Trace AO (tiled, BVH-accelerated) ---
            aoShader.SetTexture(aoKernel, "_PositionMap", positionRT);
            aoShader.SetTexture(aoKernel, "_NormalMap",   normalRT);
            aoShader.SetTexture(aoKernel, "_AOOutput",    aoRT);
            aoShader.SetInt("_TriangleCount",      context.TriangleCount);
            aoShader.SetInt("_OccTriangleCount",   occlusionGeometry.TriangleCount);
            aoShader.SetInt("_Resolution",         res);
            aoShader.SetInt("_RayCount",           settings.RayCount);
            aoShader.SetFloat("_MaxDistance",      settings.MaxDistance);
            aoShader.SetInt("_UseSelfOcclusion",   settings.UseSelfOcclusion   ? 1 : 0);
            aoShader.SetInt("_UseMutualOcclusion", settings.UseMutualOcclusion ? 1 : 0);

            bool lowRes     = settings.LowResourceMode;
            int tileSize    = lowRes ? 16 : PickTileSize(occlusionGeometry.TriangleCount);
            int tilesPerRow = Mathf.CeilToInt((float)res / tileSize);
            int tgPerTile   = Mathf.CeilToInt(tileSize / 8f);
            int totalTiles  = tilesPerRow * tilesPerRow;
            int tilesDone   = 0;

            for (int ty = 0; ty < tilesPerRow; ty++)
            {
                for (int tx = 0; tx < tilesPerRow; tx++)
                {
                    aoShader.SetInt("_TileOffsetX", tx * tileSize);
                    aoShader.SetInt("_TileOffsetY", ty * tileSize);
                    aoShader.Dispatch(aoKernel, tgPerTile, tgPerTile, 1);
                    tilesDone++;

                    if (lowRes)
                    {
                        AsyncGPUReadback.WaitAllRequests();
                        float tp = (float)tilesDone / totalTiles;
                        progress?.Report((0.20f + 0.60f * tp,
                            $"Tracing AO ({tp:P0})... [low-resource]"));
                        await Task.Yield();
                    }
                }

                if (!lowRes)
                {
                    AsyncGPUReadback.WaitAllRequests();
                    float rp = (float)(ty + 1) / tilesPerRow;
                    progress?.Report((0.20f + 0.60f * rp, $"Tracing AO ({rp:P0})..."));
                    await Task.Yield();
                }
            }



            // --- Pass 4: SVGF Denoising (optional) ---
            RenderTexture finalRT = aoRT;
            if (settings.DenoiseEnabled && settings.DenoiseIterations > 0)
            {
                finalRT = await RunSVGFAsync(
                    aoRT, positionRT, normalRT, res, settings, progress);
                aoRT.Release();
            }

            positionRT.Release();
            normalRT.Release();

            progress?.Report((0.97f, "Finalizing output..."));
            await Task.Yield();

            return finalRT;
        }

        private static async Task<RenderTexture> RunSVGFAsync(
            RenderTexture aoRT,
            RenderTexture positionRT,
            RenderTexture normalRT,
            int res,
            AOSettings settings,
            IProgress<(float progress, string message)> progress)
        {
            var denoiseShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(DenoiseShaderPath);
            if (denoiseShader == null)
                throw new InvalidOperationException($"Denoise shader not found: {DenoiseShaderPath}");

            int kVar    = denoiseShader.FindKernel("EstimateVariance");
            int kFilter = denoiseShader.FindKernel("ATrousFilter");
            if (kVar < 0 || kFilter < 0)
                throw new InvalidOperationException("Denoise kernels not found in Denoise.compute.");

            int tg = Mathf.CeilToInt(res / 8f);

            // Ping-pong buffers
            var aoA   = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var aoB   = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var varA  = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var varB  = CreateRT(res, RenderTextureFormat.ARGBFloat);

            denoiseShader.SetInt("_Resolution", res);
            denoiseShader.SetFloat("_SigmaPos", settings.DenoiseSigmaPos);
            denoiseShader.SetFloat("_SigmaNrm", settings.DenoiseSigmaNrm);
            denoiseShader.SetFloat("_SigmaLum", settings.DenoiseSigmaLum);

            // Variance estimation: aoRT → (aoA, varA)
            denoiseShader.SetTexture(kVar, "_PositionMap", positionRT);
            denoiseShader.SetTexture(kVar, "_NormalMap",   normalRT);
            denoiseShader.SetTexture(kVar, "_AOIn",        aoRT);
            denoiseShader.SetTexture(kVar, "_AOOut",       aoA);
            denoiseShader.SetTexture(kVar, "_VarianceOut", varA);
            denoiseShader.Dispatch(kVar, tg, tg, 1);
            AsyncGPUReadback.WaitAllRequests();

            progress?.Report((0.82f, "Denoising (variance estimation)..."));
            await Task.Yield();

            // A-trous iterations
            RenderTexture srcAO = aoA, srcVar = varA;
            RenderTexture dstAO = aoB, dstVar = varB;

            int iters = Mathf.Clamp(settings.DenoiseIterations, 1, 5);
            for (int i = 0; i < iters; i++)
            {
                int stepWidth = 1 << i;
                denoiseShader.SetInt("_StepWidth", stepWidth);
                denoiseShader.SetTexture(kFilter, "_PositionMap",  positionRT);
                denoiseShader.SetTexture(kFilter, "_NormalMap",    normalRT);
                denoiseShader.SetTexture(kFilter, "_AOIn",         srcAO);
                denoiseShader.SetTexture(kFilter, "_VarianceIn",   srcVar);
                denoiseShader.SetTexture(kFilter, "_AOOut",        dstAO);
                denoiseShader.SetTexture(kFilter, "_VarianceOut",  dstVar);
                denoiseShader.Dispatch(kFilter, tg, tg, 1);
                AsyncGPUReadback.WaitAllRequests();

                float dp = (float)(i + 1) / iters;
                progress?.Report((0.84f + 0.10f * dp, $"Denoising pass {i + 1}/{iters}..."));
                await Task.Yield();

                // Swap ping-pong
                (srcAO, dstAO)   = (dstAO, srcAO);
                (srcVar, dstVar) = (dstVar, srcVar);
            }

            // srcAO now holds the final denoised result
            RenderTexture result = srcAO;
            // Release whichever buffer is not the result
            if (result == aoA) { aoB.Release(); } else { aoA.Release(); }
            varA.Release();
            varB.Release();

            return result;
        }

        private static int PickTileSize(int triangleCount)
        {
            if (triangleCount < 5_000)  return 256;
            if (triangleCount < 20_000) return 128;
            if (triangleCount < 50_000) return 64;
            return 32;
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
