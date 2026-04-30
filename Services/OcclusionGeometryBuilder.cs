using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Merges multiple meshes into a single set of GPU buffers suitable for
    /// BVH-accelerated ray tracing.  All geometry is transformed into a common
    /// coordinate space (the target mesh's local space when only one mesh is used,
    /// or world space when multiple meshes are involved).
    /// </summary>
    public class OcclusionGeometryBuilder
    {
        /// <summary>
        /// Builds occlusion geometry from target mesh and optional additional occluder meshes.
        /// When occluder meshes are provided, all geometry is transformed to world space.
        /// When no occluders are provided, geometry stays in the target mesh's local space.
        /// </summary>
        public OcclusionGeometry Build(
            Mesh targetMesh,
            Transform targetTransform,
            IReadOnlyList<(Mesh mesh, Transform transform)> occluders)
        {
            bool hasOccluders = occluders != null && occluders.Count > 0;

            // Collect all vertices and indices into merged lists
            var allVertices = new List<Vector3>();
            var allIndices = new List<int>();

            // Add target mesh geometry
            AppendMesh(targetMesh, hasOccluders ? targetTransform : null,
                       allVertices, allIndices);

            // Add occluder meshes
            if (hasOccluders)
            {
                for (int i = 0; i < occluders.Count; i++)
                {
                    AppendMesh(occluders[i].mesh, occluders[i].transform,
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

        /// <summary>
        /// Appends a single mesh's triangle geometry to the merged vertex/index lists.
        /// If transform is provided, vertices are transformed to world space.
        /// Only sub-meshes with MeshTopology.Triangles are included.
        /// </summary>
        private static void AppendMesh(
            Mesh mesh, Transform transform,
            List<Vector3> allVertices, List<int> allIndices)
        {
            if (mesh == null) return;

            Vector3[] verts = mesh.vertices;
            int baseVertex = allVertices.Count;

            // Transform vertices to world space if a transform is given
            if (transform != null)
            {
                var localToWorld = transform.localToWorldMatrix;
                for (int v = 0; v < verts.Length; v++)
                    verts[v] = localToWorld.MultiplyPoint3x4(verts[v]);
            }

            allVertices.AddRange(verts);

            // Collect only triangle sub-meshes, offsetting indices
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
