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
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals  = mesh.normals;
            Vector2[] uvs      = mesh.uv;
            int[]     indices  = mesh.triangles;

            if (normals == null || normals.Length != vertices.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

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

            return new BakeContext(mesh, textureResolution, vertexBuffer, normalBuffer, uvBuffer, indexBuffer);
        }
    }
}
