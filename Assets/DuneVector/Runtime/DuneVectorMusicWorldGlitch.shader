Shader "DuneVector/URP Music World Glitch"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "MusicWorldGlitch"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _DVMusicGlitchParameters;
            float4 _DVMusicGlitchShape;
            float4 _DVMusicGlitchSafety;
            half4 _DVMusicGlitchTint;
            half _DVMusicFullscreenHueIntensity;

            half Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half envelope = saturate(_DVMusicGlitchParameters.x);
                float rowCount = max(4.0, _DVMusicGlitchParameters.z);
                float requestedBands = clamp(_DVMusicGlitchShape.x, 1.0, rowCount);
                float phase = floor(_DVMusicGlitchParameters.y);
                float rowCoordinate = uv.y * rowCount;
                float row = floor(rowCoordinate);
                half rowNoise = Hash11(row + phase * 31.17);
                half bandNoise = Hash11(row + phase * 73.91 + 11.7);
                float primaryRow = floor(Hash11(phase * 17.13 + 5.7) * rowCount);
                half primaryBand = 1.0h - step(0.5h, abs(row - primaryRow));
                half extraBandChance = saturate((requestedBands - 1.0) / max(1.0, rowCount - 1.0));
                half selectedBand = max(primaryBand, step(1.0h - extraBandChance, bandNoise));
                float rowEdgeDistance = min(frac(rowCoordinate), 1.0 - frac(rowCoordinate));
                half rowEdge = smoothstep(
                    0.0h,
                    max(fwidth(rowCoordinate) * 1.5h, 0.0001h),
                    rowEdgeDistance);
                half glitchMask = selectedBand * rowEdge * envelope;
                float shiftDirection = rowNoise < 0.5h ? -1.0 : 1.0;
                float shift = shiftDirection
                    * _DVMusicGlitchParameters.w
                    * glitchMask;

                float2 centerDistance = abs(uv - 0.5);
                half protectedCenter = (1.0 - smoothstep(
                    _DVMusicGlitchSafety.x,
                    _DVMusicGlitchSafety.x + _DVMusicGlitchSafety.z,
                    centerDistance.x))
                    * (1.0 - smoothstep(
                        _DVMusicGlitchSafety.y,
                        _DVMusicGlitchSafety.y + _DVMusicGlitchSafety.z,
                        centerDistance.y));
                half safety = lerp(1.0h, _DVMusicGlitchSafety.w, protectedCenter);
                shift *= safety;
                glitchMask *= safety;

                half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(shift, 0.0)).r;
                half green = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(shift * 0.25, 0.0)).g;
                half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(shift, 0.0)).b;
                half3 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 split = half3(red, green, blue);
                half flash = rowNoise * glitchMask;
                half3 color = lerp(source, split, glitchMask);
                color = lerp(color, _DVMusicGlitchTint.rgb, flash * _DVMusicGlitchTint.a);
                color = lerp(
                    color,
                    _DVMusicGlitchTint.rgb,
                    saturate(_DVMusicFullscreenHueIntensity) * _DVMusicGlitchTint.a);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
