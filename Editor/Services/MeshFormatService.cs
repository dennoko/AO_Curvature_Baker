using System;
using System.Collections.Generic;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Shared per-mesh GPU vertex/normal/UV data built once and reused across all submeshes,
    /// avoiding redundant CPU transforms and GPU uploads on multi-material meshes.
    /// </summary>
    public class PreparedMeshData : IDisposable
    {
        public Mesh          SourceMesh        { get; }
        public int           VertexCount       { get; }
        public float         WindingSign       { get; }
        public Matrix4x4     WorldNormalMatrix { get; }
        public ComputeBuffer VertexBuffer      { get; }
        public ComputeBuffer NormalBuffer      { get; }
        public ComputeBuffer UVBuffer          { get; }
        public int[]         SubMeshTriOffsets { get; }

        public PreparedMeshData(
            Mesh mesh,
            float windingSign,
            Matrix4x4 worldNormalMatrix,
            ComputeBuffer vertexBuffer,
            ComputeBuffer normalBuffer,
            ComputeBuffer uvBuffer,
            int[] subMeshTriOffsets)
        {
            SourceMesh        = mesh;
            VertexCount       = mesh.vertexCount;
            WindingSign       = windingSign;
            WorldNormalMatrix = worldNormalMatrix;
            VertexBuffer      = vertexBuffer;
            NormalBuffer      = normalBuffer;
            UVBuffer          = uvBuffer;
            SubMeshTriOffsets = subMeshTriOffsets;
        }

        public void Dispose()
        {
            VertexBuffer?.Release();
            NormalBuffer?.Release();
            UVBuffer?.Release();
        }
    }

    public class MeshFormatService
    {
        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Get(key), args);

        /// <summary>
        /// Transforms vertices/normals to world space (using inverse-transpose for non-uniform/negative scale),
        /// extracts UVs, and uploads shared GPU buffers ONCE for all submeshes of the mesh.
        /// Never mutates the source Mesh asset.
        /// </summary>
        public PreparedMeshData PrepareMesh(Mesh mesh, Transform meshTransform, int uvChannel = -1)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            if (!mesh.isReadable)
                throw new InvalidOperationException(L("Error_NotReadable", mesh.name));

            Vector3[] localVerts = mesh.vertices;
            if (localVerts == null || localVerts.Length == 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no vertices.");

            Matrix4x4 localToWorld = meshTransform != null ? meshTransform.localToWorldMatrix : Matrix4x4.identity;
            float det              = localToWorld.determinant;
            float windingSign      = det < 0f ? -1f : 1f;
            Matrix4x4 normalMatrix = Mathf.Abs(det) > 1e-12f
                ? localToWorld.inverse.transpose
                : Matrix4x4.identity;

            // Transform vertices into world space
            var worldVerts = new Vector3[localVerts.Length];
            bool isIdentity = localToWorld == Matrix4x4.identity;
            if (isIdentity)
            {
                Array.Copy(localVerts, worldVerts, localVerts.Length);
            }
            else
            {
                for (int i = 0; i < localVerts.Length; i++)
                    worldVerts[i] = localToWorld.MultiplyPoint3x4(localVerts[i]);
            }

            // Normals — compute locally on CPU if missing (never mutate source Mesh asset!)
            Vector3[] localNormals = mesh.normals;
            if (localNormals == null || localNormals.Length != localVerts.Length)
            {
                localNormals = ComputeFallbackNormals(mesh, localVerts);
            }

            // Transform normals with inverse-transpose matrix (handles non-uniform and negative scales accurately)
            var worldNormals = new Vector3[localNormals.Length];
            for (int i = 0; i < localNormals.Length; i++)
            {
                Vector3 n = isIdentity ? localNormals[i] : normalMatrix.MultiplyVector(localNormals[i]);
                float mag = n.magnitude;
                worldNormals[i] = mag > 1e-8f ? (n / mag) : Vector3.up;
            }

            // UVs — use specified channel or auto-detect channels 0-7
            Vector2[] uvs = FindValidUVChannel(mesh, localVerts.Length, uvChannel);
            if (uvs == null)
                throw new InvalidOperationException(L("Error_NoUVChannel", mesh.name));

            // Precompute global triangle offsets per submesh (matching OcclusionGeometryBuilder ordering)
            var subOffsets = new int[mesh.subMeshCount];
            int runningOffset = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                subOffsets[sub] = runningOffset;
                if (mesh.GetTopology(sub) == MeshTopology.Triangles)
                    runningOffset += (int)(mesh.GetIndexCount(sub) / 3);
            }

            var vertexBuffer = new ComputeBuffer(worldVerts.Length, 3 * sizeof(float));
            vertexBuffer.SetData(worldVerts);

            var normalBuffer = new ComputeBuffer(worldNormals.Length, 3 * sizeof(float));
            normalBuffer.SetData(worldNormals);

            var uvBuffer = new ComputeBuffer(uvs.Length, 2 * sizeof(float));
            uvBuffer.SetData(uvs);

            return new PreparedMeshData(
                mesh, windingSign, normalMatrix,
                vertexBuffer, normalBuffer, uvBuffer, subOffsets);
        }

        /// <summary>
        /// Creates a lightweight per-submesh BakeContext sharing the vertex/normal/UV buffers from PreparedMeshData.
        /// </summary>
        public BakeContext CreateSubMeshContext(PreparedMeshData prepared, int textureResolution, int subMeshIndex)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));

            Mesh mesh = prepared.SourceMesh;
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

            var indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));
            indexBuffer.SetData(indices);

            return new BakeContext(
                mesh,
                textureResolution,
                indices.Length / 3,
                prepared.VertexBuffer,
                prepared.NormalBuffer,
                prepared.UVBuffer,
                indexBuffer,
                subMeshTriangleOffset: prepared.SubMeshTriOffsets[subMeshIndex],
                windingSign:           prepared.WindingSign,
                worldNormalMatrix:     prepared.WorldNormalMatrix,
                ownsVertexBuffers:     false);
        }

        // Backward-compatible single-call helpers
        public BakeContext BuildContext(Mesh mesh, Transform targetTransform, int textureResolution, int uvChannel = -1)
        {
            var prepared = PrepareMesh(mesh, targetTransform, uvChannel);
            int[] indices = CollectTriangleIndices(mesh);
            if (indices.Length == 0)
            {
                prepared.Dispose();
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no triangle sub-meshes.");
            }

            var indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));
            indexBuffer.SetData(indices);

            return new BakeContext(
                mesh, textureResolution, indices.Length / 3,
                prepared.VertexBuffer, prepared.NormalBuffer, prepared.UVBuffer, indexBuffer,
                subMeshTriangleOffset: 0,
                windingSign:           prepared.WindingSign,
                worldNormalMatrix:     prepared.WorldNormalMatrix,
                ownsVertexBuffers:     true);
        }

        public BakeContext BuildContextForSubMesh(
            Mesh mesh, Transform targetTransform, int textureResolution, int subMeshIndex, int uvChannel = -1)
        {
            var prepared = PrepareMesh(mesh, targetTransform, uvChannel);
            try
            {
                int[] indices = mesh.GetTriangles(subMeshIndex);
                if (indices.Length == 0)
                    throw new InvalidOperationException($"Sub-mesh {subMeshIndex} of '{mesh.name}' has no triangles.");

                var indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));
                indexBuffer.SetData(indices);

                return new BakeContext(
                    mesh, textureResolution, indices.Length / 3,
                    prepared.VertexBuffer, prepared.NormalBuffer, prepared.UVBuffer, indexBuffer,
                    subMeshTriangleOffset: prepared.SubMeshTriOffsets[subMeshIndex],
                    windingSign:           prepared.WindingSign,
                    worldNormalMatrix:     prepared.WorldNormalMatrix,
                    ownsVertexBuffers:     true);
            }
            catch
            {
                prepared.Dispose();
                throw;
            }
        }

        private static Vector3[] ComputeFallbackNormals(Mesh mesh, Vector3[] vertices)
        {
            var normals = new Vector3[vertices.Length];
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                int[] idx = mesh.GetTriangles(sub);
                for (int t = 0; t < idx.Length; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    Vector3 faceN = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                    normals[i0] += faceN;
                    normals[i1] += faceN;
                    normals[i2] += faceN;
                }
            }
            for (int i = 0; i < normals.Length; i++)
            {
                float mag = normals[i].magnitude;
                normals[i] = mag > 1e-8f ? (normals[i] / mag) : Vector3.up;
            }
            return normals;
        }

        private static int[] CollectTriangleIndices(Mesh mesh)
        {
            var allIndices = new List<int>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                    continue;
                allIndices.AddRange(mesh.GetTriangles(sub));
            }
            return allIndices.ToArray();
        }

        private static Vector2[] FindValidUVChannel(Mesh mesh, int vertexCount, int preferredChannel)
        {
            var uvList = new List<Vector2>(vertexCount);
            if (preferredChannel >= 0 && preferredChannel < 8)
            {
                mesh.GetUVs(preferredChannel, uvList);
                if (uvList.Count == vertexCount)
                    return uvList.ToArray();

                Debug.LogWarning(
                    $"[AO Baker] UV{preferredChannel} is missing or invalid on '{mesh.name}'. Falling back to auto-detect.");
            }

            for (int channel = 0; channel < 8; channel++)
            {
                uvList.Clear();
                mesh.GetUVs(channel, uvList);
                if (uvList.Count == vertexCount)
                    return uvList.ToArray();
            }

            return null;
        }
    }
}
