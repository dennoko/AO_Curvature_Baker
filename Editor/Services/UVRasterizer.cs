using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Hardware-accelerated UV-space G-buffer rasterizer using CommandBuffer.DrawProcedural.
    /// Replaces O(Resolution^2 * TriangleCount) brute-force compute rasterization with
    /// sub-millisecond hardware rasterization supporting up to 4096x4096 natively.
    /// </summary>
    internal static class UVRasterizer
    {
        private const string RasterShaderRelativePath =
            "Shaders/UVRasterizer.shader";

        private static Shader _cachedShader;

        private static Shader GetShader()
        {
            if (_cachedShader == null)
            {
                _cachedShader = FastAOBakerPaths.LoadAsset<Shader>(RasterShaderRelativePath);
                if (_cachedShader == null)
                    throw new InvalidOperationException($"UVRasterizer shader not found: {FastAOBakerPaths.ResolveAssetPath(RasterShaderRelativePath)}");
            }
            return _cachedShader;
        }

        /// <summary>
        /// Rasterizes world-space position (+ global triangle ID), smooth normal, and flat geometric normal
        /// into 3 MRTs for AO ray tracing.
        /// </summary>
        public static void RasterizeForAO(
            BakeContext context,
            RenderTexture positionRT,
            RenderTexture normalRT,
            RenderTexture geomNormalRT)
        {
            var shader = GetShader();
            var mat    = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var cmd    = new CommandBuffer { name = "FastAOBaker_RasterizeUV_AO" };
            var prev   = RenderTexture.active;

            try
            {
                mat.SetBuffer("_Vertices", context.VertexBuffer);
                mat.SetBuffer("_Normals",  context.NormalBuffer);
                mat.SetBuffer("_UVs",      context.UVBuffer);
                mat.SetBuffer("_Indices",  context.IndexBuffer);
                mat.SetInt("_SubMeshTriOffset", context.SubMeshTriangleOffset);
                mat.SetFloat("_WindingSign",    context.WindingSign);

                int res = context.TextureResolution;
                var mrt = new RenderTargetIdentifier[] { positionRT, normalRT, geomNormalRT };
                // renderIntoTexture must be false: GL.GetGPUProjectionMatrix with true vertically flips the Y
                // axis on Direct3D/Metal platforms for camera render targets, but since this is direct UV-to-clip
                // rasterization whose final result is read back via Texture2D.ReadPixels (which already assumes
                // bottom-left origin), flipping Y in projection results in upside-down textures.
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(
                    Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), false);

                cmd.SetRenderTarget(mrt, mrt[0]);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetViewport(new Rect(0, 0, res, res));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, gpuProj);
                cmd.DrawProcedural(
                    Matrix4x4.identity, mat, 0,
                    MeshTopology.Triangles, context.TriangleCount * 3);

                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                RenderTexture.active = prev;
                cmd.Release();
                UnityEngine.Object.DestroyImmediate(mat);
            }
        }

        /// <summary>
        /// Rasterizes world-space position and welded smooth normal into 2 MRTs for curvature computation.
        /// </summary>
        public static void RasterizeForCurvature(
            BakeContext context,
            ComputeBuffer smoothNormalBuffer,
            RenderTexture positionRT,
            RenderTexture normalRT)
        {
            var shader = GetShader();
            var mat    = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var cmd    = new CommandBuffer { name = "FastAOBaker_RasterizeUV_Curvature" };
            var prev   = RenderTexture.active;

            try
            {
                mat.SetBuffer("_Vertices", context.VertexBuffer);
                mat.SetBuffer("_Normals",  smoothNormalBuffer);
                mat.SetBuffer("_UVs",      context.UVBuffer);
                mat.SetBuffer("_Indices",  context.IndexBuffer);

                int res = context.TextureResolution;
                var mrt = new RenderTargetIdentifier[] { positionRT, normalRT };
                // renderIntoTexture must be false: prevents inverted Y on Direct3D/Metal for ReadPixels export.
                Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(
                    Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), false);

                cmd.SetRenderTarget(mrt, mrt[0]);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetViewport(new Rect(0, 0, res, res));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, gpuProj);
                cmd.DrawProcedural(
                    Matrix4x4.identity, mat, 1,
                    MeshTopology.Triangles, context.TriangleCount * 3);

                Graphics.ExecuteCommandBuffer(cmd);
            }
            finally
            {
                RenderTexture.active = prev;
                cmd.Release();
                UnityEngine.Object.DestroyImmediate(mat);
            }
        }
    }
}
