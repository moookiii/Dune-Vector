Shader "DuneVector/HDRP World Geoglyph Overlay"
{
    Properties
    {
        [NoScaleOffset] _DVGeoglyphMask0("Geoglyph Mask 0", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask1("Geoglyph Mask 1", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask2("Geoglyph Mask 2", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask3("Geoglyph Mask 3", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask4("Geoglyph Mask 4", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask5("Geoglyph Mask 5", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask6("Geoglyph Mask 6", 2D) = "black" {}
        [NoScaleOffset] _DVGeoglyphMask7("Geoglyph Mask 7", 2D) = "black" {}
        [HideInInspector][NoScaleOffset] _DVGeoglyphSurfaceTexture("Geoglyph Surface Texture", 2D) = "white" {}
        [HideInInspector] _DVGeoglyphSurfaceTextureEnabled("Geoglyph Surface Texture Enabled", Float) = 0
        [HideInInspector] _DVGeoglyphSurfaceTextureTransform("Geoglyph Surface Texture Transform", Vector) = (1, 1, 0, 0)
        [HideInInspector][HDR] _DVGeoglyphBloomEmissionColor("Geoglyph Bloom Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            #define DV_MAX_GEOGLYPHS 8

            TEXTURE2D(_DVGeoglyphMask0); SAMPLER(sampler_DVGeoglyphMask0);
            TEXTURE2D(_DVGeoglyphMask1); SAMPLER(sampler_DVGeoglyphMask1);
            TEXTURE2D(_DVGeoglyphMask2); SAMPLER(sampler_DVGeoglyphMask2);
            TEXTURE2D(_DVGeoglyphMask3); SAMPLER(sampler_DVGeoglyphMask3);
            TEXTURE2D(_DVGeoglyphMask4); SAMPLER(sampler_DVGeoglyphMask4);
            TEXTURE2D(_DVGeoglyphMask5); SAMPLER(sampler_DVGeoglyphMask5);
            TEXTURE2D(_DVGeoglyphMask6); SAMPLER(sampler_DVGeoglyphMask6);
            TEXTURE2D(_DVGeoglyphMask7); SAMPLER(sampler_DVGeoglyphMask7);
            TEXTURE2D(_DVGeoglyphSurfaceTexture); SAMPLER(sampler_DVGeoglyphSurfaceTexture);

            CBUFFER_START(UnityPerMaterial)
                int _DVGeoglyphCount;
                float4 _DVGeoglyphOriginOffset;
                float4 _DVGeoglyphTransform[DV_MAX_GEOGLYPHS];
                float4 _DVGeoglyphRotation[DV_MAX_GEOGLYPHS];
                float4 _DVGeoglyphMaskSettings[DV_MAX_GEOGLYPHS];
                float4 _DVGeoglyphSlope[DV_MAX_GEOGLYPHS];
                float4 _DVGeoglyphLineColor[DV_MAX_GEOGLYPHS];
                float4 _DVGeoglyphSurfaceTextureTransform;
                float _DVGeoglyphSurfaceTextureEnabled;
                float4 _DVGeoglyphBloomEmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionAWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                output.positionAWS = GetAbsolutePositionWS(positionRWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float SampleArtworkMask(int index, float2 uv)
            {
                if (index == 0) return SAMPLE_TEXTURE2D(_DVGeoglyphMask0, sampler_DVGeoglyphMask0, uv).r;
                if (index == 1) return SAMPLE_TEXTURE2D(_DVGeoglyphMask1, sampler_DVGeoglyphMask1, uv).r;
                if (index == 2) return SAMPLE_TEXTURE2D(_DVGeoglyphMask2, sampler_DVGeoglyphMask2, uv).r;
                if (index == 3) return SAMPLE_TEXTURE2D(_DVGeoglyphMask3, sampler_DVGeoglyphMask3, uv).r;
                if (index == 4) return SAMPLE_TEXTURE2D(_DVGeoglyphMask4, sampler_DVGeoglyphMask4, uv).r;
                if (index == 5) return SAMPLE_TEXTURE2D(_DVGeoglyphMask5, sampler_DVGeoglyphMask5, uv).r;
                if (index == 6) return SAMPLE_TEXTURE2D(_DVGeoglyphMask6, sampler_DVGeoglyphMask6, uv).r;
                return SAMPLE_TEXTURE2D(_DVGeoglyphMask7, sampler_DVGeoglyphMask7, uv).r;
            }

            float2 ApplySlopeCorrection(float2 logicalXZ, float worldY, float3 normalWS, float4 slope)
            {
                float normalY = saturate(abs(normalWS.y));
                float slopeBlend = saturate((slope.y - normalY) / max(slope.y, 0.0001));
                float2 gradeDirection = normalWS.xz / max(normalY, 0.15);
                float2 correction = gradeDirection * (worldY - slope.w) * slope.x * slopeBlend;
                float correctionLength = length(correction);
                if (correctionLength > slope.z && correctionLength > 0.0001)
                {
                    correction *= slope.z / correctionLength;
                }
                return logicalXZ + correction;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 logicalXZ = input.positionAWS.xz + _DVGeoglyphOriginOffset.xy;
                float3 normalWS = normalize(input.normalWS);
                float3 accumulatedPremultipliedColor = 0.0;
                float accumulatedAlpha = 0.0;

                [loop]
                for (int i = 0; i < _DVGeoglyphCount && i < DV_MAX_GEOGLYPHS; i++)
                {
                    float4 transform = _DVGeoglyphTransform[i];
                    float4 rotation = _DVGeoglyphRotation[i];
                    float2 correctedXZ = ApplySlopeCorrection(logicalXZ, input.positionAWS.y, normalWS, _DVGeoglyphSlope[i]);
                    float2 delta = correctedXZ - transform.xy;
                    float2 artworkSpace = float2(
                        (rotation.x * delta.x) + (rotation.y * delta.y),
                        (-rotation.y * delta.x) + (rotation.x * delta.y));
                    float2 uv = (artworkSpace * transform.zw) + 0.5;

                    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                    if (inside <= 0.0)
                    {
                        continue;
                    }

                    float maskSample = SampleArtworkMask(i, uv);
                    float edgeWidth = _DVGeoglyphMaskSettings[i].y + fwidth(maskSample);
                    float lineMask = smoothstep(
                        _DVGeoglyphMaskSettings[i].x - edgeWidth,
                        _DVGeoglyphMaskSettings[i].x + edgeWidth,
                        maskSample);
                    float layerAlpha = saturate(lineMask * rotation.z * _DVGeoglyphLineColor[i].a);
                    float remainingAlpha = 1.0 - accumulatedAlpha;
                    float2 surfaceUV =
                        (uv * _DVGeoglyphSurfaceTextureTransform.xy) +
                        _DVGeoglyphSurfaceTextureTransform.zw;
                    float3 surfaceColor = SAMPLE_TEXTURE2D(
                        _DVGeoglyphSurfaceTexture,
                        sampler_DVGeoglyphSurfaceTexture,
                        surfaceUV).rgb;
                    surfaceColor = lerp(
                        float3(1.0, 1.0, 1.0),
                        surfaceColor,
                        saturate(_DVGeoglyphSurfaceTextureEnabled));
                    float3 bloomEmission = max(0.0, _DVGeoglyphBloomEmissionColor.rgb);
                    float3 luminousLineColor =
                        (_DVGeoglyphLineColor[i].rgb * surfaceColor) +
                        bloomEmission;
                    accumulatedPremultipliedColor += luminousLineColor * layerAlpha * remainingAlpha;
                    accumulatedAlpha += layerAlpha * remainingAlpha;
                }

                clip(accumulatedAlpha - 0.0001);
                return float4(accumulatedPremultipliedColor / max(accumulatedAlpha, 0.0001), accumulatedAlpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
