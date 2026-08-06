#ifndef MASSIVE_CLOUDS_DEPTH_SAMPLER_INCLUDED
#define MASSIVE_CLOUDS_DEPTH_SAMPLER_INCLUDED

#include "UnityCG.cginc"

float SampleCameraDepth(float4 uv)
{
#if defined(USING_STEREO_MATRICES)
    return Linear01Depth(UNITY_SAMPLE_DEPTH(tex2Dproj(_CameraDepthTexture, UnityStereoScreenSpaceUVAdjust(uv, _CameraDepthTexture_ST))));
#else
    return Linear01Depth(UNITY_SAMPLE_DEPTH(tex2Dproj(_CameraDepthTexture, uv)));
#endif
}

#endif