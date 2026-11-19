using System;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Holds the BVH and leaf-ordered packed triangle buffer for ray-tracing occlusion queries.
    /// All geometry is in world space with consistent outward winding.
    /// </summary>
    public class OcclusionGeometry : IDisposable
    {
        public ComputeBuffer BVHNodeBuffer { get; }
        public ComputeBuffer PackedTriBuffer { get; }
        public int TriangleCount { get; }
        public int TargetTriangleCount { get; }
        public bool IsEmpty => TriangleCount == 0 || BVHNodeBuffer == null || PackedTriBuffer == null;

        public OcclusionGeometry(
            ComputeBuffer bvhNodeBuffer,
            ComputeBuffer packedTriBuffer,
            int triangleCount,
            int targetTriangleCount)
        {
            BVHNodeBuffer       = bvhNodeBuffer;
            PackedTriBuffer     = packedTriBuffer;
            TriangleCount       = triangleCount;
            TargetTriangleCount = targetTriangleCount;
        }

        public void Dispose()
        {
            BVHNodeBuffer?.Release();
            PackedTriBuffer?.Release();
        }
    }
}
