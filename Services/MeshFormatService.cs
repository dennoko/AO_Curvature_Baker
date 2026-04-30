using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DennokoWorks.Tool.AOBaker
{
    public class MeshFormatService
    {
        // Extracts mesh data into ComputeBuffers ready for GPU dispatch.
        // All geometry remains in local (mesh) space — the world transform is
        // applied in the caller if mutual-occlusion across multiple objects is needed.
        public BakeContext BuildContext(Mesh mesh, int textureResolution)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            if (!mesh.isReadable)
                throw new InvalidOperationException(
                    $"Mesh '{mesh.name}' is not readable. Enable 'Read/Write Enabled' in the mesh import settings.");

            Vector3[] vertices = mesh.vertices;

            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no vertices.");

            // Collect triangle indices from all sub-meshes, filtering out non-Triangle topologies
            // (e.g. Lines, Points) that would corrupt the index buffer stride.
            int[] indices = CollectTriangleIndices(mesh);

            if (indices.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no triangle sub-meshes.");

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

            // UVs — try channel 0 first, then fall back to channels 1–7
            Vector2[] uvs = FindValidUVChannel(mesh, vertices.Length);

            if (uvs == null)
                throw new InvalidOperationException(
                    $"Mesh '{mesh.name}' has no valid UV channel. At least one UV set is required for AO baking.");

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
        /// Searches UV channels 0–7 for a valid UV set matching the vertex count.
        /// Returns the first valid channel found, or null if none exist.
        /// </summary>
        private static Vector2[] FindValidUVChannel(Mesh mesh, int vertexCount)
        {
            // mesh.uv, mesh.uv2, ..., mesh.uv8 correspond to channels 0–7
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
