Shader "DuneVector/Retro CRT Scanlines"
{
    Properties
    {
        _ScanlineHeight ("Scanline Height", Float) = 1
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Retro CRT Scanlines"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _ScanlineHeight;
            float _ScanlineStrength;

            half4 Frag(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;
                half4 color = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv,
                    _BlitMipLevel);

                float bandHeightPixels = max(2.0, round(_ScanlineHeight));
                float darkRows = max(1.0, floor(bandHeightPixels * 0.5));
                float pixelRow = floor(input.positionCS.y);
                float rowInBand = fmod(pixelRow, bandHeightPixels);
                float scanMask = 1.0 - step(darkRows, rowInBand);
                color.rgb *= 1.0h - ((half)scanMask * (half)saturate(_ScanlineStrength));
                return color;
            }
            ENDHLSL
        }
    }

    CustomEditor "DuneVector.Editor.DuneVectorRetroCrtScanlineShaderGUI"
}
