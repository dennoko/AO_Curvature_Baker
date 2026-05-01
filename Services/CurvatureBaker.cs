using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    public class CurvatureBaker : ICurvatureBaker
    {
        private const string CurvatureShaderPath =
            "Assets/Editor/FastAOBaker/Shaders/Compute/Curvature.compute";

        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Get(key), args);

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

            var positionRT  = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var normalRT    = CreateRT(res, RenderTextureFormat.ARGBFloat);
            var curvatureRT = CreateRT(res, RenderTextureFormat.ARGBFloat);

            // Position-welded smooth normals for the curvature rasterizer.
            // context.NormalBuffer holds the mesh's rendering normals, which may contain
            // hard-edge splits (same 3-D position, different normal on each side of the
            // crease).  In the Weingarten curvature formula, dn/du is computed from the
            // normal map: hard-edge splits produce a large dn/du at every crease, making
            // the curvature map look per-polygon instead of smoothly varying.
            // Welding duplicate positions and averaging their face-normals gives a smooth
            // normal field that reflects the geometric shape, independent of render topology.
            progress?.Report((0.02f, L("Msg_WeldedNormals")));
            await Task.Yield();
            var smoothNormals      = ComputeWeldedSmoothNormals(context.SourceMesh);
            var smoothNormalBuffer = new ComputeBuffer(smoothNormals.Length, 3 * sizeof(float));
            smoothNormalBuffer.SetData(smoothNormals);

            try
            {
                // --- Pass 1: Rasterize UV ---
                progress?.Report((0.05f, L("Msg_RasterizingUV")));
                await Task.Yield();

                shader.SetBuffer(rasterKernel, "_Vertices",     context.VertexBuffer);
                shader.SetBuffer(rasterKernel, "_Normals",      smoothNormalBuffer);
                shader.SetBuffer(rasterKernel, "_UVs",          context.UVBuffer);
                shader.SetBuffer(rasterKernel, "_Indices",      context.IndexBuffer);
                shader.SetInt("_TriangleCount",                 context.TriangleCount);
                shader.SetInt("_Resolution",                    res);
                shader.SetTexture(rasterKernel, "_PositionMap", positionRT);
                shader.SetTexture(rasterKernel, "_NormalMap",   normalRT);
                shader.Dispatch(rasterKernel, tg, tg, 1);
                AsyncGPUReadback.WaitAllRequests();

                // --- Pass 2: Compute Curvature ---
                progress?.Report((0.55f, L("Msg_ComputingCurvature")));
                await Task.Yield();

                shader.SetTexture(curvatureKernel, "_PositionMap",     positionRT);
                shader.SetTexture(curvatureKernel, "_NormalMap",       normalRT);
                shader.SetTexture(curvatureKernel, "_CurvatureOutput", curvatureRT);
                shader.SetInt("_Resolution",                           res);
                shader.SetInt("_CurvatureMode",                        (int)settings.Mode);
                shader.SetFloat("_Strength",                           settings.Strength);
                shader.SetFloat("_Bias",                               settings.Bias);
                shader.Dispatch(curvatureKernel, tg, tg, 1);
                AsyncGPUReadback.WaitAllRequests();
            }
            finally
            {
                smoothNormalBuffer.Release();
            }

            positionRT.Release();
            normalRT.Release();

            progress?.Report((0.97f, L("Msg_FinalizingCurvature")));
            await Task.Yield();

            return curvatureRT;
        }

        // Computes area-weighted smooth normals by welding vertices that share the same
        // 3-D position.  Hard-edge and UV-seam splits in the mesh topology produce
        // duplicate vertices (same position, different normal/UV); this method merges
        // them so the resulting normal field is smooth across creases.  The output is
        // used only for curvature computation, not for rendering.
        private static Vector3[] ComputeWeldedSmoothNormals(Mesh mesh)
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
                    // Cross product is not normalised → length = 2×area, giving
                    // area-weighted accumulation automatically.
                    Vector3 faceNormal = Vector3.Cross(
                        vertices[i1] - vertices[i0],
                        vertices[i2] - vertices[i0]);
                    accumulated[i0] += faceNormal;
                    accumulated[i1] += faceNormal;
                    accumulated[i2] += faceNormal;
                }
            }

            // Pool by exact position value.  Duplicate vertices inserted at the same
            // world position by the DCC tool or Unity importer have identical float
            // bit patterns, so exact equality is sufficient for typical meshes.
            var positionToNormal = new Dictionary<Vector3, Vector3>(vcount);
            for (int i = 0; i < vcount; i++)
            {
                positionToNormal.TryGetValue(vertices[i], out var existing);
                positionToNormal[vertices[i]] = existing + accumulated[i];
            }

            var result = new Vector3[vcount];
            for (int i = 0; i < vcount; i++)
            {
                var n = positionToNormal[vertices[i]].normalized;
                result[i] = n.sqrMagnitude > 0f ? n : Vector3.up;
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
