Shader "Hidden/FastAOBaker/UVRasterizer"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        // Pass 0: AO G-Buffer (Position + TriId, Smooth Normal, Geometric Normal)
        Pass
        {
            CGPROGRAM
            #pragma vertex vertAO
            #pragma fragment fragAO
            #pragma target 5.0
            #include "UnityCG.cginc"

            StructuredBuffer<float3> _Vertices;
            StructuredBuffer<float3> _Normals;
            StructuredBuffer<float2> _UVs;
            StructuredBuffer<int>    _Indices;

            int   _SubMeshTriOffset;
            float _WindingSign;

            struct v2fAO
            {
                float4 pos         : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                nointerpolation float3 geomNormal : TEXCOORD2;
                nointerpolation float  triId      : TEXCOORD3;
            };

            struct FragOutAO
            {
                float4 position   : SV_Target0; // xyz = worldPos, w = globalTriId + 1 (>= 1.0 if valid, 0 if bg)
                float4 normal     : SV_Target1; // xyz = smoothNormal * 0.5 + 0.5
                float4 geomNormal : SV_Target2; // xyz = geomNormal * 0.5 + 0.5
            };

            v2fAO vertAO(uint vertexID : SV_VertexID)
            {
                v2fAO o;
                uint tri     = vertexID / 3;
                int  vertIdx = _Indices[vertexID];

                float2 uv  = _UVs[vertIdx];
                float3 pos = _Vertices[vertIdx];
                float3 nrm = _Normals[vertIdx];

                int i0 = _Indices[tri * 3 + 0];
                int i1 = _Indices[tri * 3 + 1];
                int i2 = _Indices[tri * 3 + 2];
                float3 gN = cross(_Vertices[i1] - _Vertices[i0], _Vertices[i2] - _Vertices[i0]) * _WindingSign;
                float  gLen = length(gN);

                o.pos         = mul(UNITY_MATRIX_VP, float4(uv.x, uv.y, 0.0, 1.0));
                o.worldPos    = pos;
                o.worldNormal = nrm;
                o.geomNormal  = (gLen > 1e-12) ? (gN / gLen) : nrm;
                o.triId       = (float)(_SubMeshTriOffset + (int)tri + 1);
                return o;
            }

            FragOutAO fragAO(v2fAO i)
            {
                FragOutAO o;
                float nLen = length(i.worldNormal);
                float3 sN  = (nLen > 1e-8) ? (i.worldNormal / nLen) : float3(0.0, 1.0, 0.0);
                float gLen = length(i.geomNormal);
                float3 gN  = (gLen > 1e-8) ? (i.geomNormal / gLen) : sN;

                o.position   = float4(i.worldPos, i.triId);
                o.normal     = float4(sN * 0.5 + 0.5, 1.0);
                o.geomNormal = float4(gN * 0.5 + 0.5, 1.0);
                return o;
            }
            ENDCG
        }

        // Pass 1: Curvature G-Buffer (Position + ValidMask, Smooth Normal)
        Pass
        {
            CGPROGRAM
            #pragma vertex vertCurv
            #pragma fragment fragCurv
            #pragma target 5.0
            #include "UnityCG.cginc"

            StructuredBuffer<float3> _Vertices;
            StructuredBuffer<float3> _Normals;
            StructuredBuffer<float2> _UVs;
            StructuredBuffer<int>    _Indices;

            struct v2fCurv
            {
                float4 pos         : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            struct FragOutCurv
            {
                float4 position : SV_Target0; // xyz = worldPos, w = 1.0 if valid
                float4 normal   : SV_Target1; // xyz = smoothNormal * 0.5 + 0.5
            };

            v2fCurv vertCurv(uint vertexID : SV_VertexID)
            {
                v2fCurv o;
                int vertIdx = _Indices[vertexID];
                float2 uv   = _UVs[vertIdx];

                o.pos         = mul(UNITY_MATRIX_VP, float4(uv.x, uv.y, 0.0, 1.0));
                o.worldPos    = _Vertices[vertIdx];
                o.worldNormal = _Normals[vertIdx];
                return o;
            }

            FragOutCurv fragCurv(v2fCurv i)
            {
                FragOutCurv o;
                float nLen = length(i.worldNormal);
                float3 sN  = (nLen > 1e-8) ? (i.worldNormal / nLen) : float3(0.0, 1.0, 0.0);

                o.position = float4(i.worldPos, 1.0);
                o.normal   = float4(sN * 0.5 + 0.5, 1.0);
                return o;
            }
            ENDCG
        }
    }
    Fallback Off
}
