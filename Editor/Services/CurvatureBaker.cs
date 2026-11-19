using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace DennokoWorks.Tool.AOBaker
{
    public class CurvatureBaker : ICurvatureBaker
    {
        private const string CurvatureShaderRelativePath =
            "Shaders/Compute/Curvature.compute";

        private static ComputeShader _cachedShader;

        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Get(key), args);

        private static ComputeShader GetShader()
        {
            if (_cachedShader == null)
                _cachedShader = FastAOBakerPaths.LoadAsset<ComputeShader>(CurvatureShaderRelativePath);
            if (_cachedShader == null)
                throw new InvalidOperationException($"Compute shader not found: {FastAOBakerPaths.ResolveAssetPath(CurvatureShaderRelativePath)}");
            return _cachedShader;
        }

        public async Task<RenderTexture> ComputeCurvatureAsync(
            BakeContext context,
            CurvatureSettings settings,
            IProgress<(float progress, string message)> progress,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var shader = GetShader();

            if (context.TriangleCount == 0)
                throw new InvalidOperationException("Mesh has no triangles to bake.");

            int curvatureKernel = shader.FindKernel("ComputeCurvature");
            if (curvatureKernel < 0)
                throw new InvalidOperationException("Kernel 'ComputeCurvature' not found in Curvature.compute.");

            int res = context.TextureResolution;
            int tg  = Mathf.CeilToInt(res / 8f);

            RenderTexture positionRT  = null;
            RenderTexture normalRT    = null;
            RenderTexture curvatureRT = null;
            ComputeBuffer smoothNormalBuffer = null;

            try
            {
                positionRT  = CreateRT(res, RenderTextureFormat.ARGBFloat);
                normalRT    = CreateRT(res, RenderTextureFormat.ARGBHalf);
                curvatureRT = CreateRT(res, RenderTextureFormat.ARGBHalf);

                // Position-welded smooth normals transformed into world space so dn/du and dp/du
                // are in the exact same coordinate frame even when the object is rotated or scaled.
                progress?.Report((0.05f, L("Msg_WeldedNormals")));
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                var smoothNormals = ComputeWeldedWorldSmoothNormals(
                    context.SourceMesh, context.WorldNormalMatrix);
                smoothNormalBuffer = new ComputeBuffer(smoothNormals.Length, 3 * sizeof(float));
                smoothNormalBuffer.SetData(smoothNormals);

                // --- Pass 1: Hardware MRT UV Rasterization (< 1ms) ---
                progress?.Report((0.20f, L("Msg_RasterizingUV")));
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                UVRasterizer.RasterizeForCurvature(context, smoothNormalBuffer, positionRT, normalRT);

                // --- Pass 2: Compute Curvature ---
                progress?.Report((0.55f, L("Msg_ComputingCurvature")));
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                shader.SetTexture(curvatureKernel, "_PositionMap",     positionRT);
                shader.SetTexture(curvatureKernel, "_NormalMap",       normalRT);
                shader.SetTexture(curvatureKernel, "_CurvatureOutput", curvatureRT);
                shader.SetInt("_Resolution",                           res);
                shader.SetInt("_CurvatureMode",                        (int)settings.Mode);
                shader.SetFloat("_Strength",                           settings.Strength);
                shader.SetFloat("_Bias",                               settings.Bias);
                shader.Dispatch(curvatureKernel, tg, tg, 1);

                progress?.Report((0.97f, L("Msg_FinalizingCurvature")));
                await Task.Yield();

                var result = curvatureRT;
                curvatureRT = null; // transfer ownership to caller
                return result;
            }
            finally
            {
                smoothNormalBuffer?.Release();
                positionRT?.Release();
                normalRT?.Release();
                curvatureRT?.Release();
            }
        }

        // Computes area-weighted smooth normals by welding vertices within a 1e-4 quantized grid,
        // then transforms them to world space via the inverse-transpose normal matrix.
        private static Vector3[] ComputeWeldedWorldSmoothNormals(Mesh mesh, Matrix4x4 worldNormalMatrix)
        {
            var vertices    = mesh.vertices;
            int vcount      = vertices.Length;
            var accumulated = new Vector3[vcount];

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                int[] idx = mesh.GetTriangles(sub);
                for (int t = 0; t < idx.Length; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    Vector3 faceNormal = Vector3.Cross(
                        vertices[i1] - vertices[i0],
                        vertices[i2] - vertices[i0]);
                    accumulated[i0] += faceNormal;
                    accumulated[i1] += faceNormal;
                    accumulated[i2] += faceNormal;
                }
            }

            // Quantized spatial welding (tolerates minor floating-point discrepancies across seams)
            const float QuantizeScale = 10000f;
            var positionToNormal = new Dictionary<Vector3Int, Vector3>(vcount);
            for (int i = 0; i < vcount; i++)
            {
                Vector3 v = vertices[i];
                var key = new Vector3Int(
                    Mathf.RoundToInt(v.x * QuantizeScale),
                    Mathf.RoundToInt(v.y * QuantizeScale),
                    Mathf.RoundToInt(v.z * QuantizeScale));

                positionToNormal.TryGetValue(key, out var existing);
                positionToNormal[key] = existing + accumulated[i];
            }

            bool isIdentity = worldNormalMatrix == Matrix4x4.identity;
            var result = new Vector3[vcount];
            for (int i = 0; i < vcount; i++)
            {
                Vector3 v = vertices[i];
                var key = new Vector3Int(
                    Mathf.RoundToInt(v.x * QuantizeScale),
                    Mathf.RoundToInt(v.y * QuantizeScale),
                    Mathf.RoundToInt(v.z * QuantizeScale));

                Vector3 localN = positionToNormal[key];
                Vector3 worldN = isIdentity ? localN : worldNormalMatrix.MultiplyVector(localN);
                float mag = worldN.magnitude;
                result[i] = mag > 1e-8f ? (worldN / mag) : Vector3.up;
            }
            return result;
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
