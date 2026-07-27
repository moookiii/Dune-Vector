#ifndef MASSIVE_CLOUDS_DEPTH_SAMPLER_INCLUDED
#define MASSIVE_CLOUDS_DEPTH_SAMPLER_INCLUDED

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

float LoadCameraDepth(uint2 pixelCoords)
{
    return LOAD_TEXTURE2D_X_LOD(_CameraDepthTexture, pixelCoords, 0).r;
}

float SampleCameraDepth(float4 uv)
{
    return Linear01Depth(LoadCameraDepth(uint2(uv.xy * _ScreenSize.xy)), _ZBufferParams);
}

#endif