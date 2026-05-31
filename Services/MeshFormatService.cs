using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DennokoWorks.Tool.AOBaker
{
    public class MeshFormatService
    {
        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Get(key), args);

        // Extracts mesh data into ComputeBuffers ready for GPU dispatch.
        // All geometry is transformed to world space using the targetTransform.
        // All sub-meshes are merged into a single index buffer.
        public BakeContext BuildContext(Mesh mesh, Transform targetTransform, int textureResolution, int uvChannel = -1)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            int[] indices = CollectTriangleIndices(mesh);
            if (indices.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no triangle sub-meshes.");

            return BuildContextCore(mesh, targetTransform, textureResolution, indices, uvChannel);
        }

        // Extracts mesh data for a single sub-mesh into ComputeBuffers ready for GPU dispatch.
        // Only the specified sub-mesh's triangles are included in the index buffer;
        // vertices, normals, and UVs still span the full mesh so indices remain valid.
        public BakeContext BuildContextForSubMesh(
            Mesh mesh, Transform targetTransform, int textureResolution, int subMeshIndex, int uvChannel = -1)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            if (subMeshIndex < 0 || subMeshIndex >= mesh.subMeshCount)
                throw new ArgumentOutOfRangeException(nameof(subMeshIndex),
                    $"Sub-mesh index {subMeshIndex} is out of range for mesh '{mesh.name}' (subMeshCount={mesh.subMeshCount}).");
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                throw new InvalidOperationException(
                    $"Sub-mesh {subMeshIndex} of '{mesh.name}' is not triangle topology and cannot be baked.");

            int[] indices = mesh.GetTriangles(subMeshIndex);
            if (indices.Length == 0)
                throw new InvalidOperationException(
                    $"Sub-mesh {subMeshIndex} of '{mesh.name}' has no triangles.");

            return BuildContextCore(mesh, targetTransform, textureResolution, indices, uvChannel);
        }

        // Shared implementation: builds GPU buffers from caller-supplied indices.
        private BakeContext BuildContextCore(
            Mesh mesh, Transform targetTransform, int textureResolution, int[] indices, int uvChannel)
        {
            if (!mesh.isReadable)
                throw new InvalidOperationException(L("Error_NotReadable", mesh.name));

            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no vertices.");

            // Apply world transform
            Matrix4x4 localToWorld = targetTransform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = localToWorld.MultiplyPoint3x4(vertices[i]);

            // Normals — recalculate if missing or mismatched
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            // Fallback if normals are still missing after recalculation
            // (can happen with degenerate or non-manifold geometry)
            if (normals == null || normals.Length != vertices.Length)
                normals = new Vector3[vertices.Length];
            else
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = targetTransform.TransformDirection(normals[i]).normalized;
            }

            // UVs — use the specified channel, or auto-detect starting from channel 0
            Vector2[] uvs = FindValidUVChannel(mesh, vertices.Length, uvChannel);
            if (uvs == null)
                throw new InvalidOperationException(L("Error_NoUVChannel", mesh.name));

            var vertexBuffer = new ComputeBuffer(vertices.Length, 3 * sizeof(float));
            vertexBuffer.SetData(vertices);

            var normalBuffer = new ComputeBuffer(normals.Length, 3 * sizeof(float));
            normalBuffer.SetData(normals);

            var uvBuffer = new ComputeBuffer(uvs.Length, 2 * sizeof(float));
            uvBuffer.SetData(uvs);

            var indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));
            indexBuffer.SetData(indices);

            return new BakeContext(mesh, textureResolution, indices.Length / 3,
                vertexBuffer, normalBuffer, uvBuffer, indexBuffer);
        }

        /// <summary>
        /// Collects triangle indices from all sub-meshes that have MeshTopology.Triangles.
        /// Sub-meshes with other topologies (Lines, Points, etc.) are safely skipped.
        /// </summary>
        private static int[] CollectTriangleIndices(Mesh mesh)
        {
            var allIndices = new List<int>();

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                    continue;

                int[] subIndices = mesh.GetTriangles(sub);
                allIndices.AddRange(subIndices);
            }

            return allIndices.ToArray();
        }

        /// <summary>
        /// Returns UV data for baking. If preferredChannel is 0–7, that channel is tried first;
        /// if it is empty or invalid, falls back to auto-detection (channels 0–7 in order).
        /// Returns null if no valid channel exists.
        /// </summary>
        private static Vector2[] FindValidUVChannel(Mesh mesh, int vertexCount, int preferredChannel)
        {
            if (preferredChannel >= 0 && preferredChannel < 8)
            {
                var uvList = new List<Vector2>();
                mesh.GetUVs(preferredChannel, uvList);
                if (uvList.Count == vertexCount)
                    return uvList.ToArray();

                Debug.LogWarning(
                    $"[AO Baker] UV{preferredChannel} is missing or invalid on '{mesh.name}'. Falling back to auto-detect.");
            }

            for (int channel = 0; channel < 8; channel++)
            {
                var uvList = new List<Vector2>();
                mesh.GetUVs(channel, uvList);
                if (uvList.Count == vertexCount)
                    return uvList.ToArray();
            }

            return null;
        }
    }
}
