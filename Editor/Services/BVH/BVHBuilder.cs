using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BVHNodeGPU
    {
        public Vector3 BoundsMin;
        public int     LeftFirst; // leaf: first tri in _PackedTris; internal: left child index
        public Vector3 BoundsMax;
        public int     Count;     // 0 = internal node; >0 = leaf (triangle count)
    }

    /// <summary>
    /// Pre-transformed triangle stored contiguously in BVH leaf order (48 bytes = 3 x float4).
    /// Eliminates _PrimIndices -> _OccIndices -> _OccVertices multi-level pointer chasing on GPU.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PackedTriGPU
    {
        public Vector3 P0;
        public int     TriId; // >= 0 for self-mesh triangles (exact self-hit skip), -1 for occluders
        public Vector3 E1;    // P1 - P0
        public float   Pad1;
        public Vector3 E2;    // P2 - P0
        public float   Pad2;
    }

    public static class BVHBuilder
    {
        private const int   LeafThreshold = 4;
        private const int   MaxLeafSize   = 16;
        private const int   BucketCount   = 12;
        private const int   MaxDepth      = 58; // safely below GPU traversal stack size (64)
        private const float TraversalCost = 1.25f;

        public struct SourceTri
        {
            public Vector3 P0, P1, P2;
            public int     TriId;
        }

        private struct TriInfo
        {
            public Vector3 Min, Max, Centroid;
            public int     SourceIdx;
        }

        private struct Bucket
        {
            public int     Count;
            public Vector3 Min, Max;
        }

        /// <summary>
        /// Builds a flat SAH-optimized BVH and leaf-ordered PackedTriGPU array off the main thread.
        /// </summary>
        public static Task<(BVHNodeGPU[] nodes, PackedTriGPU[] packedTris)> BuildAsync(
            IReadOnlyList<SourceTri> sourceTris,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Build(sourceTris, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Synchronous SAH BVH build with zero per-node heap allocations and O(B) prefix/suffix sweep.
        /// </summary>
        public static (BVHNodeGPU[] nodes, PackedTriGPU[] packedTris) Build(
            IReadOnlyList<SourceTri> sourceTris,
            CancellationToken cancellationToken = default)
        {
            int triCount = sourceTris?.Count ?? 0;
            if (triCount == 0)
                return (Array.Empty<BVHNodeGPU>(), Array.Empty<PackedTriGPU>());

            var infos = new TriInfo[triCount];
            for (int i = 0; i < triCount; i++)
            {
                SourceTri t = sourceTris[i];
                Vector3 mn = Vector3.Min(Vector3.Min(t.P0, t.P1), t.P2);
                Vector3 mx = Vector3.Max(Vector3.Max(t.P0, t.P1), t.P2);
                infos[i] = new TriInfo
                {
                    Min       = mn,
                    Max       = mx,
                    Centroid  = (mn + mx) * 0.5f,
                    SourceIdx = i
                };
            }

            // Maximum possible nodes for a binary tree over triCount leaves is 2 * triCount - 1
            var nodes      = new BVHNodeGPU[Math.Max(1, triCount * 2)];
            var packedTris = new PackedTriGPU[triCount];
            int nodeCount  = 1; // root at index 0
            int triOutIdx  = 0;

            // Reusable scratch buffers for zero-allocation SAH evaluation
            var buckets    = new Bucket[BucketCount];
            var leftCount  = new int[BucketCount - 1];
            var leftArea   = new float[BucketCount - 1];

            BuildNode(
                sourceTris, infos, 0, triCount,
                nodes, ref nodeCount,
                packedTris, ref triOutIdx,
                0, 0,
                buckets, leftCount, leftArea,
                cancellationToken);

            Array.Resize(ref nodes, nodeCount);
            return (nodes, packedTris);
        }

        private static void BuildNode(
            IReadOnlyList<SourceTri> sourceTris,
            TriInfo[] tris, int start, int end,
            BVHNodeGPU[] nodes, ref int nodeCount,
            PackedTriGPU[] packedTris, ref int triOutIdx,
            int nodeIdx, int depth,
            Bucket[] buckets, int[] leftCount, float[] leftArea,
            CancellationToken cancellationToken)
        {
            if ((nodeCount & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            Vector3 mn = V3Max, mx = V3Min;
            for (int i = start; i < end; i++)
            {
                mn = Vector3.Min(mn, tris[i].Min);
                mx = Vector3.Max(mx, tris[i].Max);
            }

            int n = end - start;
            if (n <= LeafThreshold || depth >= MaxDepth)
            {
                MakeLeaf(sourceTris, tris, start, end, mn, mx, nodeIdx, nodes, packedTris, ref triOutIdx);
                return;
            }

            Vector3 cMin = V3Max, cMax = V3Min;
            for (int i = start; i < end; i++)
            {
                cMin = Vector3.Min(cMin, tris[i].Centroid);
                cMax = Vector3.Max(cMax, tris[i].Centroid);
            }

            int   bestAxis  = -1;
            int   bestSplit = -1;
            float bestCost  = float.MaxValue;
            float nodeArea  = SA(mn, mx);
            float invNodeArea = nodeArea > 1e-12f ? (1f / nodeArea) : 0f;

            for (int axis = 0; axis < 3; axis++)
            {
                float span = cMax[axis] - cMin[axis];
                if (span < 1e-6f) continue;

                float scale = BucketCount / span;
                float cMinA = cMin[axis];

                for (int b = 0; b < BucketCount; b++)
                {
                    buckets[b].Count = 0;
                    buckets[b].Min   = V3Max;
                    buckets[b].Max   = V3Min;
                }

                for (int i = start; i < end; i++)
                {
                    int b = (int)((tris[i].Centroid[axis] - cMinA) * scale);
                    if (b < 0) b = 0;
                    else if (b >= BucketCount) b = BucketCount - 1;

                    buckets[b].Count++;
                    buckets[b].Min = Vector3.Min(buckets[b].Min, tris[i].Min);
                    buckets[b].Max = Vector3.Max(buckets[b].Max, tris[i].Max);
                }

                // Prefix sweep (left -> right) in O(BucketCount)
                Vector3 accMin = V3Max, accMax = V3Min;
                int     accCnt = 0;
                for (int split = 0; split < BucketCount - 1; split++)
                {
                    if (buckets[split].Count > 0)
                    {
                        accCnt += buckets[split].Count;
                        accMin  = Vector3.Min(accMin, buckets[split].Min);
                        accMax  = Vector3.Max(accMax, buckets[split].Max);
                    }
                    leftCount[split] = accCnt;
                    leftArea[split]  = accCnt > 0 ? SA(accMin, accMax) : 0f;
                }

                // Suffix sweep (right -> left) in O(BucketCount)
                accMin = V3Max;
                accMax = V3Min;
                accCnt = 0;
                for (int split = BucketCount - 1; split >= 1; split--)
                {
                    if (buckets[split].Count > 0)
                    {
                        accCnt += buckets[split].Count;
                        accMin  = Vector3.Min(accMin, buckets[split].Min);
                        accMax  = Vector3.Max(accMax, buckets[split].Max);
                    }

                    int lC = leftCount[split - 1];
                    int rC = accCnt;
                    if (lC == 0 || rC == 0) continue;

                    float rArea = SA(accMin, accMax);
                    float cost  = TraversalCost + (lC * leftArea[split - 1] + rC * rArea) * invNodeArea;
                    if (cost < bestCost)
                    {
                        bestCost  = cost;
                        bestAxis  = axis;
                        bestSplit = split;
                    }
                }
            }

            // Terminate as leaf if no valid split or SAH split is worse than leaf traversal
            if (bestAxis < 0 || (bestCost >= n && n <= MaxLeafSize))
            {
                MakeLeaf(sourceTris, tris, start, end, mn, mx, nodeIdx, nodes, packedTris, ref triOutIdx);
                return;
            }

            // In-place partition around best split
            float span2 = cMax[bestAxis] - cMin[bestAxis];
            float scale2 = BucketCount / span2;
            float cMin2  = cMin[bestAxis];
            int   pivot  = start;
            for (int i = start; i < end; i++)
            {
                int b = (int)((tris[i].Centroid[bestAxis] - cMin2) * scale2);
                if (b < 0) b = 0;
                else if (b >= BucketCount) b = BucketCount - 1;

                if (b < bestSplit)
                {
                    (tris[pivot], tris[i]) = (tris[i], tris[pivot]);
                    pivot++;
                }
            }
            if (pivot == start || pivot == end)
                pivot = (start + end) >> 1;

            // Pre-allocate adjacent child slots (right = leftIdx + 1)
            int leftIdx = nodeCount;
            nodeCount += 2;
            nodes[nodeIdx] = new BVHNodeGPU
            {
                BoundsMin = mn,
                BoundsMax = mx,
                LeftFirst = leftIdx,
                Count     = 0
            };

            BuildNode(sourceTris, tris, start, pivot, nodes, ref nodeCount, packedTris, ref triOutIdx,
                leftIdx, depth + 1, buckets, leftCount, leftArea, cancellationToken);
            BuildNode(sourceTris, tris, pivot, end, nodes, ref nodeCount, packedTris, ref triOutIdx,
                leftIdx + 1, depth + 1, buckets, leftCount, leftArea, cancellationToken);
        }

        private static void MakeLeaf(
            IReadOnlyList<SourceTri> sourceTris,
            TriInfo[] tris, int start, int end,
            Vector3 mn, Vector3 mx, int nodeIdx,
            BVHNodeGPU[] nodes, PackedTriGPU[] packedTris, ref int triOutIdx)
        {
            int first = triOutIdx;
            for (int i = start; i < end; i++)
            {
                SourceTri s = sourceTris[tris[i].SourceIdx];
                packedTris[triOutIdx++] = new PackedTriGPU
                {
                    P0    = s.P0,
                    TriId = s.TriId,
                    E1    = s.P1 - s.P0,
                    Pad1  = 0f,
                    E2    = s.P2 - s.P0,
                    Pad2  = 0f
                };
            }

            nodes[nodeIdx] = new BVHNodeGPU
            {
                BoundsMin = mn,
                BoundsMax = mx,
                LeftFirst = first,
                Count     = end - start
            };
        }

        private static float SA(Vector3 mn, Vector3 mx)
        {
            if (mn.x > mx.x) return 0f;
            Vector3 d = mx - mn;
            return 2f * (d.x * d.y + d.y * d.z + d.z * d.x);
        }

        private static readonly Vector3 V3Max = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private static readonly Vector3 V3Min = new Vector3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
    }
}
