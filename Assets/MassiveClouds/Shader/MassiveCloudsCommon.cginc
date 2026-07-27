#ifndef MASSIVE_CLOUDS_COMMON_INCLUDED
#define MASSIVE_CLOUDS_COMMON_INCLUDED

sampler2D _MainTex;
half4     _MainTex_ST;
float4    _MainTex_TexelSize;

struct appdata
{
    float4 vertex : POSITION;
    float4 uv : TEXCOORD0;
    uint vertexID : SV_VertexID;
};

struct v2f
{
    float4 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
};

float4 _RTHandleScale;        // { w / RTHandle.maxWidth, h / RTHandle.maxHeight } : xy = currFrame, zw = prevFrame
float4 _RTHandleScaleHistory; // Same as above but the RTHandle handle size is that of the history buffer

v2f MassiveCloudsVert(appdata v)
{
    v2f o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv;
    #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0) {
            o.uv.y = 1 - o.uv.y;
        }
    #endif
    return o;
}

#endif