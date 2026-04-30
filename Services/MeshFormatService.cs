using System;
using UnityEngine;

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
            Vector3[] normals  = mesh.normals;
            Vector2[] uvs      = mesh.uv;
            int[]     indices  = mesh.triangles;

            if (vertices == null || vertices.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no vertices.");

            if (indices == null || indices.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no triangles.");

            if (normals == null || normals.Length != vertices.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            // Fallback if normals are still missing after recalculation
            // (can happen with degenerate or non-manifold geometry)
            if (normals == null || normals.Length != vertices.Length)
                normals = new Vector3[vertices.Length];

            if (uvs == null || uvs.Length != vertices.Length)
                uvs = new Vector2[vertices.Length];

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
    }
}
