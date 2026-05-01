using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BVHNodeGPU
    {
        public Vector3 BoundsMin;
        public int     LeftFirst; // leaf: first prim in _PrimIndices; internal: left child index
        public Vector3 BoundsMax;
        public int     Count;     // 0 = internal node; >0 = leaf (triangle count)
    }

    public static class BVHBuilder
    {
        private const int LeafThreshold = 4;
        private const int BucketCount   = 8;

        private struct TriInfo
        {
            public Vector3 Min, Max, Centroid;
            public int     OrigIdx;
        }

        private struct Bucket
        {
            public int     Count;
            public Vector3 Min, Max;
        }

        // Returns flat BVH node array and primitive index array.
        // Prim indices remap into the original triangle list (0..triCount-1).
        public static (BVHNodeGPU[] nodes, int[] primIndices) Build(Vector3[] vertices, int[] indices)
        {
            int triCount = indices.Length / 3;
            var infos    = new TriInfo[triCount];

            for (int i = 0; i < triCount; i++)
            {
                Vector3 p0 = vertices[indices[i * 3]];
                Vector3 p1 = vertices[indices[i * 3 + 1]];
                Vector3 p2 = vertices[indices[i * 3 + 2]];
                Vector3 mn = Vector3.Min(Vector3.Min(p0, p1), p2);
                Vector3 mx = Vector3.Max(Vector3.Max(p0, p1), p2);
                infos[i]   = new TriInfo { Min = mn, Max = mx, Centroid = (mn + mx) * 0.5f, OrigIdx = i };
            }

            var nodes       = new List<BVHNodeGPU>(triCount * 2);
            var primIndices = new List<int>(triCount);
            nodes.Add(default); // root at index 0
            BuildNode(infos, 0, triCount, nodes, primIndices, 0);

            return (nodes.ToArray(), primIndices.ToArray());
        }

        // Fill the pre-allocated node at nodeIdx and recursively build its subtree.
        // Children are pre-allocated as adjacent slots (right child = leftFirst + 1),
        // matching the shader's traversal assumption.
        private static void BuildNode(
            TriInfo[] tris, int start, int end,
            List<BVHNodeGPU> nodes, List<int> prims, int nodeIdx)
        {
            Vector3 mn = V3Max, mx = V3Min;
            for (int i = start; i < end; i++) { mn = Vector3.Min(mn, tris[i].Min); mx = Vector3.Max(mx, tris[i].Max); }

            int n = end - start;

            if (n <= LeafThreshold)
            {
                MakeLeaf(tris, start, end, mn, mx, nodeIdx, nodes, prims);
                return;
            }

            // Centroid bounds
            Vector3 cMin = V3Max, cMax = V3Min;
            for (int i = start; i < end; i++) { cMin = Vector3.Min(cMin, tris[i].Centroid); cMax = Vector3.Max(cMax, tris[i].Centroid); }

            int   bestAxis   = -1;
            int   bestSplit  = -1;
            float bestCost   = float.MaxValue;
            float nodeArea   = SA(mn, mx);

            for (int axis = 0; axis < 3; axis++)
            {
                float span = cMax[axis] - cMin[axis];
                if (span < 1e-6f) continue;

                var buckets = new Bucket[BucketCount];
                for (int b = 0; b < BucketCount; b++) { buckets[b].Min = V3Max; buckets[b].Max = V3Min; }

                for (int i = start; i < end; i++)
                {
                    int b = Mathf.Min(BucketCount - 1,
                        (int)(BucketCount * (tris[i].Centroid[axis] - cMin[axis]) / span));
                    buckets[b].Count++;
                    buckets[b].Min = Vector3.Min(buckets[b].Min, tris[i].Min);
                    buckets[b].Max = Vector3.Max(buckets[b].Max, tris[i].Max);
                }

                for (int split = 1; split < BucketCount; split++)
                {
                    Vector3 lMn = V3Max, lMx = V3Min; int lC = 0;
                    for (int b = 0; b < split; b++)
                        if (buckets[b].Count > 0) { lC += buckets[b].Count; lMn = Vector3.Min(lMn, buckets[b].Min); lMx = Vector3.Max(lMx, buckets[b].Max); }

                    Vector3 rMn = V3Max, rMx = V3Min; int rC = 0;
                    for (int b = split; b < BucketCount; b++)
                        if (buckets[b].Count > 0) { rC += buckets[b].Count; rMn = Vector3.Min(rMn, buckets[b].Min); rMx = Vector3.Max(rMx, buckets[b].Max); }

                    if (lC == 0 || rC == 0) continue;
                    float cost = (lC * SA(lMn, lMx) + rC * SA(rMn, rMx)) / nodeArea;
                    if (cost < bestCost) { bestCost = cost; bestAxis = axis; bestSplit = split; }
                }
            }

            if (bestAxis < 0)
            {
                MakeLeaf(tris, start, end, mn, mx, nodeIdx, nodes, prims);
                return;
            }

            // Partition around best split
            float span2 = cMax[bestAxis] - cMin[bestAxis];
            int   pivot = start;
            for (int i = start; i < end; i++)
            {
                int b = Mathf.Min(BucketCount - 1,
                    (int)(BucketCount * (tris[i].Centroid[bestAxis] - cMin[bestAxis]) / span2));
                if (b < bestSplit) { (tris[pivot], tris[i]) = (tris[i], tris[pivot]); pivot++; }
            }
            if (pivot == start || pivot == end) pivot = (start + end) / 2;

            // Pre-allocate both child slots adjacently BEFORE recursing,
            // so right child is always at leftFirst + 1 (as the shader assumes).
            int leftIdx = nodes.Count;
            nodes.Add(default); // left child slot
            nodes.Add(default); // right child slot (leftIdx + 1)
            nodes[nodeIdx] = new BVHNodeGPU { BoundsMin = mn, BoundsMax = mx, LeftFirst = leftIdx, Count = 0 };

            BuildNode(tris, start, pivot, nodes, prims, leftIdx);
            BuildNode(tris, pivot, end,   nodes, prims, leftIdx + 1);
        }

        private static void MakeLeaf(
            TriInfo[] tris, int start, int end,
            Vector3 mn, Vector3 mx, int nodeIdx,
            List<BVHNodeGPU> nodes, List<int> prims)
        {
            int first = prims.Count;
            for (int i = start; i < end; i++) prims.Add(tris[i].OrigIdx);
            nodes[nodeIdx] = new BVHNodeGPU { BoundsMin = mn, BoundsMax = mx, LeftFirst = first, Count = end - start };
        }

        private static float SA(Vector3 mn, Vector3 mx)
        {
            if (mn.x > mx.x) return 0f;
            Vector3 d = mx - mn;
            return 2f * (d.x * d.y + d.y * d.z + d.z * d.x);
        }

        private static readonly Vector3 V3Max = new Vector3( float.MaxValue,  float.MaxValue,  float.MaxValue);
        private static readonly Vector3 V3Min = new Vector3(-float.MaxValue, -float.MaxValue, -float.MaxValue);
    }
}
