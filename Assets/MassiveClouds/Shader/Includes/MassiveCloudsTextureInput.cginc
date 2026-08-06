#ifndef MASSIVE_CLOUDS_TEXTURE_INPUT
#define MASSIVE_CLOUDS_TEXTURE_INPUT

// Depth
sampler2D _CameraDepthTexture;
half4     _CameraDepthTexture_ST;
float4    _CameraDepthTexture_TexelSize;

inline float ExposureMultiplier()
{
    return 1.0;
}

inline float ExposureMultiplier2()
{
    return 1.0;
}
#endif

