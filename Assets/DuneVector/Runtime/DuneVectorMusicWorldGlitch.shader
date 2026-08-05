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
            float4 _DVMusicGlitchSafety;
            half4 _DVMusicGlitchTint;
            float4 _DVMusicLightningOverlayResponse;
            float4 _DVMusicLightningOverlayGeometry;
            float4 _DVMusicLightningOverlayShape;
            float4 _DVMusicLightningOverlayPlacement;
            half4 _DVMusicLightningOverlayColor;

            half Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
            }

            half SoftLine(float distanceToLine, float thickness)
            {
                float antialias = max(fwidth(distanceToLine), thickness * 0.35);
                return 1.0h - smoothstep(thickness, thickness + antialias, distanceToLine);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half intensity = saturate(_DVMusicGlitchParameters.x);
                float sliceCount = max(1.0, _DVMusicGlitchParameters.z);
                float slice = floor(uv.y * sliceCount);
                half noise = Hash11(slice + _DVMusicGlitchParameters.y * 19.19);
                float shift = (noise * 2.0 - 1.0) * intensity * _DVMusicGlitchParameters.w;

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
                intensity *= safety;

                half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(shift, 0.0)).r;
                half green = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(shift, 0.0)).b;
                half3 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 split = half3(red, green, blue);
                half flash = intensity * noise;
                half3 color = lerp(source, split, intensity);
                color = lerp(color, _DVMusicGlitchTint.rgb, flash * _DVMusicGlitchTint.a);

                #if UNITY_REVERSED_Z
                    float farDepth = 0.0;
                #else
                    float farDepth = 1.0;
                #endif
                float3 farPositionWS = ComputeWorldSpacePosition(uv, farDepth, UNITY_MATRIX_I_VP);
                float skyUp = normalize(farPositionWS - _WorldSpaceCameraPos).y;
                float horizonDepth = max(0.0001, _DVMusicLightningOverlayShape.w);
                half belowHorizon = (1.0h - smoothstep(0.0, 0.015, skyUp))
                    * smoothstep(-horizonDepth, -horizonDepth * 0.35, skyUp);
                half lightningResponse = saturate(
                    _DVMusicLightningOverlayResponse.x
                    + _DVMusicLightningOverlayResponse.y * _DVMusicLightningOverlayResponse.z);
                float lightningTick = floor(_TimeParameters.x * _DVMusicLightningOverlayGeometry.w);
                float slotCount = max(1.0, round(_DVMusicLightningOverlayGeometry.x));
                float strikeCount = clamp(round(_DVMusicLightningOverlayShape.x), 1.0, 4.0);
                float azimuthSpan = max(0.001, _DVMusicLightningOverlayPlacement.x);
                float screenWidth = _DVMusicLightningOverlayGeometry.y / (azimuthSpan * 2.0);
                half lightningShape = 0.0h;
                [unroll]
                for (int strikeIndex = 0; strikeIndex < 4; strikeIndex++)
                {
                    half strikeEnabled = 1.0h - step(strikeCount, (float)strikeIndex);
                    half strikeChoice = Hash11(lightningTick * 4.17 + strikeIndex * 7.13 + 9.31);
                    float strikeX = (floor(strikeChoice * slotCount) + 0.5) / slotCount;
                    float jaggedOffset = sin(
                        skyUp * 91.0
                        + lightningTick * 1.73
                        + strikeIndex * 8.31) * _DVMusicLightningOverlayGeometry.z * 0.035;
                    float distanceToCore = abs(uv.x - strikeX - jaggedOffset);
                    half core = SoftLine(distanceToCore, screenWidth);
                    half halo = SoftLine(distanceToCore, screenWidth * _DVMusicLightningOverlayShape.y)
                        * _DVMusicLightningOverlayShape.z;
                    float nodePhase = abs(frac(
                        skyUp * _DVMusicLightningOverlayPlacement.z + strikeChoice) - 0.5);
                    half node = SoftLine(
                        max(distanceToCore, nodePhase * screenWidth * 12.0),
                        screenWidth * 1.8) * _DVMusicLightningOverlayPlacement.y;
                    lightningShape += (core + halo + node) * strikeEnabled;
                }
                color += _DVMusicLightningOverlayColor.rgb
                    * (lightningShape
                        * belowHorizon
                        * lightningResponse
                        * _DVMusicLightningOverlayResponse.w);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
