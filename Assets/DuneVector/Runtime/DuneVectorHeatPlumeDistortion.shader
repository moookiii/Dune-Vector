Shader "DuneVector/HDRP Heat Plume Distortion"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex("Thermal Noise", 2D) = "gray" {}
        _DistortionStrength("Distortion Strength", Float) = 0.12
        _DistortionBlur("Distortion Blur", Range(0, 1)) = 0.04
        _PrimaryTiling("Primary Tiling", Vector) = (2.4, 3.2, 0, 0)
        _SecondaryTiling("Secondary Tiling", Vector) = (4.1, 2.3, 0, 0)
        _PrimaryVelocity("Primary Velocity", Vector) = (0.035, 0.12, 0, 0)
        _SecondaryVelocity("Secondary Velocity", Vector) = (-0.055, 0.073, 0, 0)
        _SecondaryStrength("Secondary Strength", Range(0, 1)) = 0.48
        _HorizontalTurbulence("Horizontal Turbulence", Range(0, 1)) = 0.24
        _CoreWidth("Core Width", Range(0.05, 0.5)) = 0.28
        _TopWidth("Top Width", Range(0.05, 0.5)) = 0.4
        _WidthVariation("Width Variation", Range(0, 0.5)) = 0.16
        _WidthFrequency("Width Frequency", Float) = 5.2
        _SideFeather("Side Feather", Range(0.01, 1)) = 0.42
        _BottomFeather("Bottom Feather", Range(0.01, 1)) = 0.2
        _TopFeather("Top Feather", Range(0.01, 1)) = 0.34
        _VerticalDissipationStart("Vertical Dissipation Start", Range(0, 1)) = 0.3
        _VerticalDissipationPower("Vertical Dissipation Power", Float) = 1.4
        _Lean("Lean", Range(0, 0.5)) = 0.12
        _MinimumSpeedMultiplier("Minimum Speed Multiplier", Float) = 0.78
        _MaximumSpeedMultiplier("Maximum Speed Multiplier", Float) = 1.22
        _MinimumStrengthMultiplier("Minimum Strength Multiplier", Float) = 0.75
        _MaximumStrengthMultiplier("Maximum Strength Multiplier", Float) = 1.2
        _PhaseRange("Phase Range", Float) = 17.371
        _PrimaryPhaseOffset("Primary Phase Offset", Float) = 0.37
        _SecondaryPhaseOffset("Secondary Phase Offset", Float) = -0.23
        _CardEdgeFeather("Card Edge Feather", Range(0.001, 0.25)) = 0.04
        _EdgeNoiseBase("Edge Noise Base", Range(0, 1)) = 0.72
        _PrimaryEdgeNoise("Primary Edge Noise", Range(0, 1)) = 0.56
        _SecondaryEdgeNoise("Secondary Edge Noise", Range(0, 1)) = 0.28
        _FadeProfileVariation("Fade Profile Variation", Range(0, 1)) = 0.18
        _DistanceFadeStart("Distance Fade Start", Float) = 160
        _DistanceFadeEnd("Distance Fade End", Float) = 480
        _DetailFadeStart("Detail Fade Start", Float) = 120
        _DetailFadeEnd("Detail Fade End", Float) = 320
        _DepthFadeDistance("Depth Fade Distance", Float) = 2.5
        _MaskClipThreshold("Mask Clip Threshold", Float) = 0.001
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "DuneVectorHeatDistortion"
            Tags { "LightMode" = "DistortionVectors" }

            Stencil
            {
                WriteMask 2
                Ref 2
                Comp Always
                Pass Replace
            }

            Blend One One, One One
            ZTest LEqual
            ZWrite Off
            Cull Off

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

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float _DistortionStrength;
                float _DistortionBlur;
                float4 _PrimaryTiling;
                float4 _SecondaryTiling;
                float4 _PrimaryVelocity;
                float4 _SecondaryVelocity;
                float _SecondaryStrength;
                float _HorizontalTurbulence;
                float _CoreWidth;
                float _TopWidth;
                float _WidthVariation;
                float _WidthFrequency;
                float _SideFeather;
                float _BottomFeather;
                float _TopFeather;
                float _VerticalDissipationStart;
                float _VerticalDissipationPower;
                float _Lean;
                float _MinimumSpeedMultiplier;
                float _MaximumSpeedMultiplier;
                float _MinimumStrengthMultiplier;
                float _MaximumStrengthMultiplier;
                float _PhaseRange;
                float _PrimaryPhaseOffset;
                float _SecondaryPhaseOffset;
                float _CardEdgeFeather;
                float _EdgeNoiseBase;
                float _PrimaryEdgeNoise;
                float _SecondaryEdgeNoise;
                float _FadeProfileVariation;
                float _DistanceFadeStart;
                float _DistanceFadeEnd;
                float _DetailFadeStart;
                float _DetailFadeEnd;
                float _DepthFadeDistance;
                float _MaskClipThreshold;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float random : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionRWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 color : COLOR;
                float random : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionRWS);
                output.uv = input.uv;
                output.color = input.color;
                output.random = input.random;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float distanceToCamera = length(input.positionRWS);
                float distanceFade = 1.0 - smoothstep(_DistanceFadeStart, _DistanceFadeEnd, distanceToCamera);
                float detailFade = 1.0 - smoothstep(_DetailFadeStart, _DetailFadeEnd, distanceToCamera);
                float speed = lerp(_MinimumSpeedMultiplier, _MaximumSpeedMultiplier, input.random);
                float phase = input.random * _PhaseRange;
                float strengthVariation = lerp(
                    _MinimumStrengthMultiplier,
                    _MaximumStrengthMultiplier,
                    input.random);

                float2 primaryUv = (input.uv * _PrimaryTiling.xy) +
                    (_PrimaryVelocity.xy * _Time.y * speed) +
                    float2(phase, phase * _PrimaryPhaseOffset);
                float2 secondaryUv = (input.uv * _SecondaryTiling.xy) +
                    (_SecondaryVelocity.xy * _Time.y / max(speed, 0.001)) +
                    float2(phase * _SecondaryPhaseOffset, phase);
                float3 primary = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, primaryUv).rgb;
                float3 secondary = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, secondaryUv).rgb;

                float heightVariation = sin((input.uv.y * _WidthFrequency) +
                    (primary.b * 3.14159) + phase) * _WidthVariation * detailFade;
                float center = 0.5 + ((input.random - 0.5) * _Lean * input.uv.y) +
                    ((secondary.r - 0.5) * _HorizontalTurbulence * detailFade);
                float halfWidth = max(0.02, lerp(_CoreWidth, _TopWidth, input.uv.y) + heightVariation);
                float normalizedSide = abs(input.uv.x - center) / halfWidth;
                float sideMask = 1.0 - smoothstep(1.0 - _SideFeather, 1.0, normalizedSide);
                float cardEdgeMask = smoothstep(0.0, _CardEdgeFeather, input.uv.x) *
                    smoothstep(0.0, _CardEdgeFeather, 1.0 - input.uv.x);
                float fadeVariation = lerp(
                    1.0 - _FadeProfileVariation,
                    1.0 + _FadeProfileVariation,
                    input.random);
                float bottomMask = smoothstep(0.0, _BottomFeather * fadeVariation, input.uv.y);
                float topMask = smoothstep(0.0, _TopFeather * fadeVariation, 1.0 - input.uv.y);
                float dissipationProgress = saturate(
                    (input.uv.y - _VerticalDissipationStart) /
                    max(1.0 - _VerticalDissipationStart, 0.001));
                float verticalDissipation = pow(1.0 - dissipationProgress, _VerticalDissipationPower);
                float turbulentEdge = saturate(_EdgeNoiseBase +
                    ((primary.b - 0.5) * _PrimaryEdgeNoise) +
                    ((secondary.b - 0.5) * _SecondaryEdgeNoise * detailFade));

                uint2 pixelCoord = uint2(input.positionCS.xy);
                float sceneDepth = LinearEyeDepth(LoadCameraDepth(pixelCoord), _ZBufferParams);
                float cardDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float intersectionFade = saturate((sceneDepth - cardDepth) / max(_DepthFadeDistance, 0.001));
                float mask = sideMask * cardEdgeMask * bottomMask * topMask * verticalDissipation * turbulentEdge *
                    input.color.a * distanceFade * intersectionFade;
                clip(mask - _MaskClipThreshold);

                float2 primaryVector = (primary.rg * 2.0) - 1.0;
                float2 secondaryVector = (secondary.rg * 2.0) - 1.0;
                float2 distortion = (primaryVector + (secondaryVector * _SecondaryStrength * detailFade));
                distortion.x += (secondary.g - 0.5) * _HorizontalTurbulence * detailFade;
                distortion *= _DistortionStrength * strengthVariation * mask;

                return float4(distortion, 1.0, _DistortionBlur * mask);
            }
            ENDHLSL
        }

        // HDRP strips DistortionVectors when distortion is disabled in the active
        // frame settings. Keep an invisible forward pass so the shader remains
        // valid instead of rendering the particle billboards with the error shader.
        Pass
        {
            Name "FallbackInvisible"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite Off
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertFallback
            #pragma fragment FragFallback
            #pragma multi_compile_instancing
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct FallbackAttributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FallbackVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            FallbackVaryings VertFallback(FallbackAttributes input)
            {
                FallbackVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float4 FragFallback(FallbackVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0.0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
