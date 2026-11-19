using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class AOBaker : IAOBaker
    {
        private const string AoShaderRelativePath =
            "Shaders/Compute/AOBake.compute";
        private const string DenoiseShaderRelativePath =
            "Shaders/Compute/Denoise.compute";

        private static ComputeShader _cachedAoShader;
        private static ComputeShader _cachedDenoiseShader;

        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Get(key), args);

        private static ComputeShader GetAoShader()
        {
            if (_cachedAoShader == null)
                _cachedAoShader = FastAOBakerPaths.LoadAsset<ComputeShader>(AoShaderRelativePath);
            if (_cachedAoShader == null)
                throw new InvalidOperationException($"Compute shader not found: {FastAOBakerPaths.ResolveAssetPath(AoShaderRelativePath)}");
            return _cachedAoShader;
        }

        private static ComputeShader GetDenoiseShader()
        {
            if (_cachedDenoiseShader == null)
                _cachedDenoiseShader = FastAOBakerPaths.LoadAsset<ComputeShader>(DenoiseShaderRelativePath);
            if (_cachedDenoiseShader == null)
                throw new InvalidOperationException($"Denoise shader not found: {FastAOBakerPaths.ResolveAssetPath(DenoiseShaderRelativePath)}");
            return _cachedDenoiseShader;
        }

        public async Task<RenderTexture> ComputeAOAsync(
            BakeContext context,
            OcclusionGeometry occlusionGeometry,
            AOSettings settings,
            IProgress<(float progress, string message)> progress,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var aoShader = GetAoShader();

            if (context.TriangleCount == 0)
                throw new InvalidOperationException("Mesh has no triangles to bake.");

            int compactKernel = aoShader.FindKernel("BuildActiveTexels");
            int aoKernel      = aoShader.FindKernel("TraceAO");
            if (compactKernel < 0)
                throw new InvalidOperationException("Kernel 'BuildActiveTexels' not found.");
            if (aoKernel < 0)
                throw new InvalidOperationException("Kernel 'TraceAO' not found.");

            int res = context.TextureResolution;

            // G-Buffer & AO render targets (packed ARGBHalf saves >50% VRAM vs ARGBFloat)
            RenderTexture positionRT   = null;
            RenderTexture normalRT     = null;
            RenderTexture geomNormalRT = null;
            RenderTexture aoRT         = null;
            RenderTexture finalRT      = null;

            ComputeBuffer activeTexelsBuffer = null;
            ComputeBuffer activeCountBuffer  = null;
            ComputeBuffer dummyBvhBuffer     = null;
            ComputeBuffer dummyTriBuffer     = null;

            try
            {
                positionRT   = CreateRT(res, RenderTextureFormat.ARGBFloat);
                normalRT     = CreateRT(res, RenderTextureFormat.ARGBHalf);
                geomNormalRT = CreateRT(res, RenderTextureFormat.ARGBHalf);
                aoRT         = CreateRT(res, RenderTextureFormat.ARGBHalf);

                // --- Pass 1: Hardware MRT UV Rasterization (< 1ms) ---
                progress?.Report((0.05f, L("Msg_RasterizingUV")));
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                UVRasterizer.RasterizeForAO(context, positionRT, normalRT, geomNormalRT);

                // --- Pass 2: Active Texel Compaction (skips all background pixels) ---
                activeTexelsBuffer = new ComputeBuffer(res * res, sizeof(uint));
                activeCountBuffer  = new ComputeBuffer(1, sizeof(uint));
                activeCountBuffer.SetData(new uint[] { 0u });

                int tg2D = Mathf.CeilToInt(res / 8f);
                aoShader.SetInt("_Resolution", res);
                aoShader.SetTexture(compactKernel, "_PositionMap", positionRT);
                aoShader.SetTexture(compactKernel, "_AOOutput",    aoRT);
                aoShader.SetBuffer(compactKernel, "_ActiveTexels", activeTexelsBuffer);
                aoShader.SetBuffer(compactKernel, "_ActiveCount",  activeCountBuffer);
                aoShader.Dispatch(compactKernel, tg2D, tg2D, 1);

                uint activeCount = await ReadSingleUintAsync(activeCountBuffer, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (activeCount == 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[AO Baker] No valid UV texels rasterized in [0, 1] for '{context.SourceMesh.name}'.");
                    finalRT = aoRT;
                    aoRT = null;
                    return finalRT;
                }

                // --- Pass 3: Bind BVH & Packed Triangles ---
                bool hasGeometry = occlusionGeometry != null
                    && !occlusionGeometry.IsEmpty
                    && (settings.UseSelfOcclusion || settings.UseMutualOcclusion);

                ComputeBuffer bvhBuf;
                ComputeBuffer triBuf;
                int occTriCount;

                if (hasGeometry)
                {
                    bvhBuf      = occlusionGeometry.BVHNodeBuffer;
                    triBuf      = occlusionGeometry.PackedTriBuffer;
                    occTriCount = occlusionGeometry.TriangleCount;
                }
                else
                {
                    // Bind 1-element dummy buffers so shader bindings stay valid on all graphics APIs
                    dummyBvhBuffer = new ComputeBuffer(1, 32);
                    dummyTriBuffer = new ComputeBuffer(1, 48);
                    bvhBuf      = dummyBvhBuffer;
                    triBuf      = dummyTriBuffer;
                    occTriCount = 0;
                }

                aoShader.SetBuffer(aoKernel, "_BVHNodes",         bvhBuf);
                aoShader.SetBuffer(aoKernel, "_PackedTris",       triBuf);
                aoShader.SetBuffer(aoKernel, "_ActiveTexelsRead", activeTexelsBuffer);
                aoShader.SetTexture(aoKernel, "_PositionMap",     positionRT);
                aoShader.SetTexture(aoKernel, "_NormalMap",       normalRT);
                aoShader.SetTexture(aoKernel, "_GeomNormalMap",   geomNormalRT);
                aoShader.SetTexture(aoKernel, "_AOOutput",        aoRT);
                aoShader.SetInt("_Resolution",      res);
                aoShader.SetInt("_OccTriangleCount", occTriCount);
                aoShader.SetInt("_RayCount",         settings.RayCount);
                aoShader.SetFloat("_MaxDistance",    settings.MaxDistance);
                aoShader.SetFloat("_Bias",           settings.Bias);
                aoShader.SetInt("_IgnoreBackfaces",  settings.IgnoreBackfaces ? 1 : 0);

                // --- Pass 4: Time-Budgeted Adaptive Chunked TraceAO ---
                bool lowRes         = settings.LowResourceMode;
                double targetMs     = lowRes ? 25.0 : 120.0;
                int minChunk        = lowRes ? 1024 : 4096;
                int maxChunk        = lowRes ? 32768 : 262144;
                int chunkSize       = PickInitialChunkSize(occTriCount, settings.RayCount, lowRes);

                var sw = new Stopwatch();
                uint offset = 0;

                while (offset < activeCount)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    uint count = (uint)Math.Min((long)chunkSize, (long)(activeCount - offset));
                    int  tg1D  = (int)((count + 63u) / 64u);

                    aoShader.SetInt("_TexelOffset", (int)offset);
                    aoShader.SetInt("_TexelCount",  (int)count);

                    sw.Restart();
                    aoShader.Dispatch(aoKernel, tg1D, 1, 1);

                    // True non-blocking GPU completion fence (prevents TDR without freezing Editor)
                    await WaitForGpuFenceAsync(activeCountBuffer, cancellationToken);
                    sw.Stop();

                    offset += count;
                    float ratio = (float)offset / activeCount;
                    progress?.Report((
                        0.15f + 0.65f * ratio,
                        lowRes
                            ? L("Msg_TracingAOLowRes", ratio.ToString("P0"))
                            : L("Msg_TracingAO", ratio.ToString("P0"))));

                    // Adapt chunk size to stay close to target frame/TDR budget
                    double elapsedMs = sw.Elapsed.TotalMilliseconds;
                    if (elapsedMs > targetMs * 1.5)
                        chunkSize = Math.Max(minChunk, chunkSize / 2);
                    else if (elapsedMs < targetMs * 0.5 && elapsedMs > 0.1)
                        chunkSize = Math.Min(maxChunk, chunkSize * 2);
                }

                // Release geometry-normal RT and compaction buffers before denoising
                geomNormalRT.Release();
                geomNormalRT = null;
                activeTexelsBuffer.Release();
                activeTexelsBuffer = null;

                // --- Pass 5: SVGF Edge-Stopping A-Trous Denoising (optional) ---
                if (settings.DenoiseEnabled && settings.DenoiseIterations > 0 && hasGeometry)
                {
                    finalRT = await RunSVGFAsync(
                        aoRT, positionRT, normalRT, activeCountBuffer,
                        res, settings, progress, cancellationToken);
                    // RunSVGFAsync takes ownership of ping-ponging with aoRT and releases the unused RT
                    aoRT = null;
                }
                else
                {
                    finalRT = aoRT;
                    aoRT = null;
                }

                progress?.Report((0.97f, L("Msg_Finalizing")));
                await Task.Yield();

                var result = finalRT;
                finalRT = null; // transfer ownership to caller
                return result;
            }
            finally
            {
                positionRT?.Release();
                normalRT?.Release();
                geomNormalRT?.Release();
                aoRT?.Release();
                finalRT?.Release();
                activeTexelsBuffer?.Release();
                activeCountBuffer?.Release();
                dummyBvhBuffer?.Release();
                dummyTriBuffer?.Release();
            }
        }

        private static async Task<RenderTexture> RunSVGFAsync(
            RenderTexture aoRT,
            RenderTexture positionRT,
            RenderTexture normalRT,
            ComputeBuffer fenceBuffer,
            int res,
            AOSettings settings,
            IProgress<(float progress, string message)> progress,
            CancellationToken cancellationToken)
        {
            var denoiseShader = GetDenoiseShader();
            int kFilter = denoiseShader.FindKernel("ATrousFilter");
            if (kFilter < 0)
                throw new InvalidOperationException("Kernel 'ATrousFilter' not found in Denoise.compute.");

            int tg = Mathf.CeilToInt(res / 8f);

            // Scale position sigma relative to MaxDistance so edge-stopping works consistently
            // from cm-scale avatars to multi-meter architectural scenes.
            float effectiveSigmaPos = settings.DenoiseSigmaPos * Mathf.Max(settings.MaxDistance * 0.1f, 0.005f);

            // Only 1 extra ARGBHalf RT needed (ping-pongs directly with aoRT; variance is packed in .g!)
            var pingPongRT = CreateRT(res, RenderTextureFormat.ARGBHalf);
            RenderTexture srcAO = aoRT;
            RenderTexture dstAO = pingPongRT;

            try
            {
                denoiseShader.SetInt("_Resolution", res);
                denoiseShader.SetFloat("_SigmaPos", effectiveSigmaPos);
                denoiseShader.SetFloat("_SigmaNrm", settings.DenoiseSigmaNrm);
                denoiseShader.SetFloat("_SigmaLum", settings.DenoiseSigmaLum);

                int iters = Mathf.Clamp(settings.DenoiseIterations, 1, 5);
                for (int i = 0; i < iters; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int stepWidth = 1 << i;
                    denoiseShader.SetInt("_StepWidth", stepWidth);
                    denoiseShader.SetTexture(kFilter, "_PositionMap", positionRT);
                    denoiseShader.SetTexture(kFilter, "_NormalMap",   normalRT);
                    denoiseShader.SetTexture(kFilter, "_AOIn",        srcAO);
                    denoiseShader.SetTexture(kFilter, "_AOOut",       dstAO);
                    denoiseShader.Dispatch(kFilter, tg, tg, 1);

                    await WaitForGpuFenceAsync(fenceBuffer, cancellationToken);

                    float dp = (float)(i + 1) / iters;
                    progress?.Report((0.82f + 0.14f * dp, L("Msg_DenoisingPass", i + 1, iters)));

                    (srcAO, dstAO) = (dstAO, srcAO);
                }

                // Release whichever ping-pong buffer is NOT the final result
                dstAO.Release();
                dstAO = null;
                return srcAO;
            }
            catch
            {
                srcAO?.Release();
                dstAO?.Release();
                throw;
            }
        }

        private static int PickInitialChunkSize(int triangleCount, int rayCount, bool lowRes)
        {
            // Estimate relative work per texel
            float triFactor = Mathf.Max(1f, Mathf.Log(Mathf.Max(triangleCount, 100), 2f) / 12f);
            float rayFactor = Mathf.Max(1f, rayCount / 64f);
            float baseSize  = lowRes ? 16384f : 65536f;
            int   chunk     = Mathf.RoundToInt(baseSize / (triFactor * rayFactor));
            return lowRes
                ? Mathf.Clamp(chunk, 1024, 32768)
                : Mathf.Clamp(chunk, 4096, 131072);
        }

        private static Task WaitForGpuFenceAsync(ComputeBuffer fenceBuffer, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            var req = AsyncGPUReadback.Request(fenceBuffer);

            void Poll()
            {
                req.Update();
                if (ct.IsCancellationRequested)
                {
                    EditorApplication.update -= Poll;
                    tcs.TrySetCanceled(ct);
                    return;
                }
                if (req.done)
                {
                    EditorApplication.update -= Poll;
                    tcs.TrySetResult(!req.hasError);
                }
            }

            EditorApplication.update += Poll;
            return tcs.Task;
        }

        private static Task<uint> ReadSingleUintAsync(ComputeBuffer buffer, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<uint>();
            var req = AsyncGPUReadback.Request(buffer);

            void Poll()
            {
                req.Update();
                if (ct.IsCancellationRequested)
                {
                    EditorApplication.update -= Poll;
                    tcs.TrySetCanceled(ct);
                    return;
                }
                if (req.done)
                {
                    EditorApplication.update -= Poll;
                    if (req.hasError)
                    {
                        tcs.TrySetException(new InvalidOperationException("GPU readback failed."));
                    }
                    else
                    {
                        var data = req.GetData<uint>();
                        tcs.TrySetResult(data.Length > 0 ? data[0] : 0u);
                    }
                }
            }

            EditorApplication.update += Poll;
            return tcs.Task;
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
