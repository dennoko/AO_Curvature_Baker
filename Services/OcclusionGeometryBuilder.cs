using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Merges multiple meshes into a single set of GPU buffers for BVH-accelerated ray tracing.
    /// All geometry is kept in the target mesh's local space so it matches the G-buffer
    /// positions produced by RasterizeUV.
    /// </summary>
    public class OcclusionGeometryBuilder
    {
        public OcclusionGeometry Build(
            Mesh targetMesh,
            Transform targetTransform,
            IReadOnlyList<(Mesh mesh, Transform transform)> occluders)
        {
            bool hasOccluders = occluders != null && occluders.Count > 0;

            // Collect all vertices and indices into merged lists
            var allVertices = new List<Vector3>();
            var allIndices = new List<int>();

            // Target mesh always stays in its local space — must match the G-buffer
            // positions written by RasterizeUV (which uses mesh.vertices without transform).
            AppendMeshWithMatrix(targetMesh, Matrix4x4.identity, allVertices, allIndices);

            // Occluders are transformed into the target's local space so that all
            // geometry shares the same coordinate frame as the G-buffer.
            if (hasOccluders)
            {
                Matrix4x4 worldToTarget = targetTransform.worldToLocalMatrix;
                for (int i = 0; i < occluders.Count; i++)
                {
                    Matrix4x4 occluderToTarget =
                        worldToTarget * occluders[i].transform.localToWorldMatrix;
                    AppendMeshWithMatrix(occluders[i].mesh, occluderToTarget,
                                        allVertices, allIndices);
                }
            }

            if (allVertices.Count == 0 || allIndices.Count == 0)
                throw new System.InvalidOperationException(
                    "No valid geometry found for occlusion testing.");

            int triCount = allIndices.Count / 3;

            // Build GPU buffers
            var vertexBuffer = new ComputeBuffer(allVertices.Count, 3 * sizeof(float));
            vertexBuffer.SetData(allVertices.ToArray());

            var indexBuffer = new ComputeBuffer(allIndices.Count, sizeof(int));
            indexBuffer.SetData(allIndices.ToArray());

            // Build BVH over merged geometry
            var (bvhNodes, primIndices) = BVHBuilder.Build(
                allVertices.ToArray(), allIndices.ToArray());

            var bvhNodeBuffer = new ComputeBuffer(bvhNodes.Length, 32); // BVHNodeGPU = 32 bytes
            bvhNodeBuffer.SetData(bvhNodes);

            var primIndexBuffer = new ComputeBuffer(primIndices.Length, sizeof(int));
            primIndexBuffer.SetData(primIndices);

            return new OcclusionGeometry(
                vertexBuffer, indexBuffer, bvhNodeBuffer, primIndexBuffer, triCount);
        }

        private static void AppendMeshWithMatrix(
            Mesh mesh, Matrix4x4 matrix,
            List<Vector3> allVertices, List<int> allIndices)
        {
            if (mesh == null) return;

            Vector3[] verts = mesh.vertices;
            int baseVertex = allVertices.Count;

            bool isIdentity = matrix == Matrix4x4.identity;
            for (int v = 0; v < verts.Length; v++)
                allVertices.Add(isIdentity ? verts[v] : matrix.MultiplyPoint3x4(verts[v]));

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                    continue;

                int[] subIndices = mesh.GetTriangles(sub);
                for (int i = 0; i < subIndices.Length; i++)
                    allIndices.Add(subIndices[i] + baseVertex);
            }
        }
    }
}
