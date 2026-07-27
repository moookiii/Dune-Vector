#ifndef MASSIVE_CLOUDS_TEXTURE_INPUT
#define MASSIVE_CLOUDS_TEXTURE_INPUT

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition-config/Runtime/ShaderConfig.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/TextureXR.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"


// Depth
TEXTURE2D_X(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_ExposureTexture);

inline float ExposureMultiplier()
{
#if SHADEROPTIONS_PRE_EXPOSITION
    return LOAD_TEXTURE2D(_ExposureTexture, int2(0, 0)).x;
#else
    return 1.0;
#endif
}

inline float ExposureMultiplier2()
{
    float m = ExposureMultiplier();
    return m * m;
}

#endif
