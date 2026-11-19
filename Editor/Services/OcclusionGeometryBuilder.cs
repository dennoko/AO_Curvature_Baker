using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Merges target and/or occluder meshes in world space, deduplicating accidental overlaps,
    /// correcting negative-scale winding, and building the SAH BVH asynchronously off the main thread.
    /// </summary>
    public class OcclusionGeometryBuilder
    {
        public async Task<OcclusionGeometry> BuildAsync(
            Mesh targetMesh,
            Transform targetTransform,
            IReadOnlyList<(Mesh mesh, Transform transform)> occluders,
            bool includeSelf,
            bool includeMutual,
            CancellationToken cancellationToken = default)
        {
            var sourceTris = new List<BVHBuilder.SourceTri>();
            int targetTriCount = 0;

            // 1. Target mesh (only included when self-occlusion is enabled)
            if (includeSelf && targetMesh != null && targetTransform != null)
            {
                AppendMeshTriangles(
                    targetMesh, targetTransform.localToWorldMatrix,
                    sourceTris, assignSequentialTriId: true);
                targetTriCount = sourceTris.Count;
            }

            // 2. Occluder meshes (only included when mutual occlusion is enabled)
            if (includeMutual && occluders != null && occluders.Count > 0)
            {
                for (int i = 0; i < occluders.Count; i++)
                {
                    var (occMesh, occTransform) = occluders[i];
                    if (occMesh == null || occTransform == null) continue;

                    // Deduplicate if the user accidentally added the same target object to Occluders
                    if (occTransform == targetTransform && occMesh == targetMesh)
                        continue;

                    AppendMeshTriangles(
                        occMesh, occTransform.localToWorldMatrix,
                        sourceTris, assignSequentialTriId: false);
                }
            }

            int totalTriCount = sourceTris.Count;
            if (totalTriCount == 0)
                return new OcclusionGeometry(null, null, 0, 0);

            // Build SAH BVH and leaf-ordered packed triangles on a background thread
            var (bvhNodes, packedTris) = await BVHBuilder.BuildAsync(sourceTris, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var bvhNodeBuffer = new ComputeBuffer(bvhNodes.Length, 32); // BVHNodeGPU = 32 bytes
            bvhNodeBuffer.SetData(bvhNodes);

            var packedTriBuffer = new ComputeBuffer(packedTris.Length, 48); // PackedTriGPU = 48 bytes
            packedTriBuffer.SetData(packedTris);

            return new OcclusionGeometry(bvhNodeBuffer, packedTriBuffer, totalTriCount, targetTriCount);
        }

        private static void AppendMeshTriangles(
            Mesh mesh,
            Matrix4x4 matrix,
            List<BVHBuilder.SourceTri> output,
            bool assignSequentialTriId)
        {
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return;

            bool isIdentity = matrix == Matrix4x4.identity;
            bool flipWinding = matrix.determinant < 0f;

            // Transform vertices once per mesh
            Vector3[] worldVerts;
            if (isIdentity)
            {
                worldVerts = verts;
            }
            else
            {
                worldVerts = new Vector3[verts.Length];
                for (int v = 0; v < verts.Length; v++)
                    worldVerts[v] = matrix.MultiplyPoint3x4(verts[v]);
            }

            int sequentialId = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                    continue;

                int[] subIndices = mesh.GetTriangles(sub);
                int triCount = subIndices.Length / 3;
                output.Capacity = System.Math.Max(output.Capacity, output.Count + triCount);

                for (int t = 0; t < subIndices.Length; t += 3)
                {
                    int i0 = subIndices[t];
                    int i1 = flipWinding ? subIndices[t + 2] : subIndices[t + 1];
                    int i2 = flipWinding ? subIndices[t + 1] : subIndices[t + 2];

                    output.Add(new BVHBuilder.SourceTri
                    {
                        P0    = worldVerts[i0],
                        P1    = worldVerts[i1],
                        P2    = worldVerts[i2],
                        TriId = assignSequentialTriId ? sequentialId : -1
                    });
                    sequentialId++;
                }
            }
        }
    }
}
