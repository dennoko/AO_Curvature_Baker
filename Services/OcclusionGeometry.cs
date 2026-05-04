using System;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Holds the merged geometry buffers and BVH for ray-tracing occlusion queries.
    /// All geometry is in a common space (world space when multiple meshes are involved).
    /// </summary>
    public class OcclusionGeometry : IDisposable
    {
        public ComputeBuffer VertexBuffer { get; }
        public ComputeBuffer IndexBuffer { get; }
        public ComputeBuffer BVHNodeBuffer { get; }
        public ComputeBuffer PrimIndexBuffer { get; }
        public int TriangleCount { get; }
        public int TargetTriangleCount { get; }

        public OcclusionGeometry(
            ComputeBuffer vertexBuffer,
            ComputeBuffer indexBuffer,
            ComputeBuffer bvhNodeBuffer,
            ComputeBuffer primIndexBuffer,
            int triangleCount,
            int targetTriangleCount)
        {
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            BVHNodeBuffer = bvhNodeBuffer;
            PrimIndexBuffer = primIndexBuffer;
            TriangleCount = triangleCount;
            TargetTriangleCount = targetTriangleCount;
        }

        public void Dispose()
        {
            VertexBuffer?.Release();
            IndexBuffer?.Release();
            BVHNodeBuffer?.Release();
            PrimIndexBuffer?.Release();
        }
    }
}
