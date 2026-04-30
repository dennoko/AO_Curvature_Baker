using System;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public class BakeContext : IDisposable
    {
        public Mesh SourceMesh { get; }
        public int TextureResolution { get; }
        public int VertexCount { get; }
        public int TriangleCount { get; }

        // GPU buffers: float3 positions, float3 normals, float2 UVs, int triangle indices
        public ComputeBuffer VertexBuffer { get; }
        public ComputeBuffer NormalBuffer { get; }
        public ComputeBuffer UVBuffer { get; }
        public ComputeBuffer IndexBuffer { get; }

        public BakeContext(
            Mesh mesh,
            int resolution,
            ComputeBuffer vertexBuffer,
            ComputeBuffer normalBuffer,
            ComputeBuffer uvBuffer,
            ComputeBuffer indexBuffer)
        {
            SourceMesh = mesh;
            TextureResolution = resolution;
            VertexCount = mesh.vertexCount;
            TriangleCount = mesh.triangles.Length / 3;
            VertexBuffer = vertexBuffer;
            NormalBuffer = normalBuffer;
            UVBuffer = uvBuffer;
            IndexBuffer = indexBuffer;
        }

        public void Dispose()
        {
            VertexBuffer?.Release();
            NormalBuffer?.Release();
            UVBuffer?.Release();
            IndexBuffer?.Release();
        }
    }
}
