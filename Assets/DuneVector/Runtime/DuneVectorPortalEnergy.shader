Shader "DuneVector/HDRP Portal Energy"
{
    Properties
    {
        [HDR] _PortalColor("Portal Color", Color) = (4, 1.5, 0.05, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.8
        _BloomIntensity("Bloom Intensity", Float) = 2
        _CoreMode("Core Mode", Float) = 0
        _DistanceFade("Distance Fade", Range(0, 1)) = 1
        _LineEdgeSoftness("Line Edge Softness", Range(0.01, 0.49)) = 0.14
        _TravelPulseCount("Travel Pulse Count", Float) = 3
        _TravelPulseSpeed("Travel Pulse Speed", Float) = 0.22
        _TravelPulseWidth("Travel Pulse Width", Range(0.01, 0.8)) = 0.16
        _TravelPulseBrightness("Travel Pulse Brightness", Float) = 2.4
        _TravelPulseRingPhaseOffset("Travel Pulse Ring Phase Offset", Range(0, 1)) = 0.17
        _OuterRimBrightness("Outer Rim Brightness", Float) = 1.25
        _StructuralLineBrightness("Structural Line Brightness", Float) = 0.82
        _RuneBrightness("Rune Brightness", Float) = 0.72
        _InnerRingBrightness("Inner Ring Brightness", Float) = 0.58
        _FeatureBrightness("Feature Brightness", Float) = 1
        _ActivationBloomBoost("Activation Bloom Boost", Float) = 1
        _PulseSpeed("Pulse Speed", Float) = 1.35
        _PulseAmount("Pulse Amount", Range(0, 0.5)) = 0.12
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
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Blend SrcAlpha One
            Cull Off
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PortalColor;
                float _Opacity;
                float _BloomIntensity;
                float _CoreMode;
                float _DistanceFade;
                float _LineEdgeSoftness;
                float _TravelPulseCount;
                float _TravelPulseSpeed;
                float _TravelPulseWidth;
                float _TravelPulseBrightness;
                float _TravelPulseRingPhaseOffset;
                float _OuterRimBrightness;
                float _StructuralLineBrightness;
                float _RuneBrightness;
                float _InnerRingBrightness;
                float _FeatureBrightness;
                float _ActivationBloomBoost;
                float _PulseSpeed;
                float _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 portalPosition : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.portalPosition = input.positionOS.xy;
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float pulse = 1.0 + (sin((_Time.y * _PulseSpeed) + 1.7) * _PulseAmount);
                float alpha = _Opacity * _DistanceFade;
                float travelBrightness = 1.0;
                float featureBrightness = _FeatureBrightness;
                float3 particleTint = float3(1.0, 1.0, 1.0);

                if (_CoreMode > 2.5)
                {
                    float2 centered = (input.uv * 2.0) - 1.0;
                    float particleRadius = length(centered);
                    clip(1.0 - particleRadius);
                    float particleFade = 1.0 - smoothstep(0.15, 1.0, particleRadius);
                    alpha *= particleFade * input.color.a;
                    particleTint = input.color.rgb;
                }
                else
                {
                    float circularLine = step(1.5, input.uv.x);
                    float outerRim = step(2.5, input.uv.x);
                    float innerRing = circularLine * (1.0 - outerRim);
                    float rune = 1.0 - step(-1.5, input.uv.x);
                    float runeBrightnessScale = max(0.0, 1.0 + ((-input.uv.x) - 2.0));
                    featureBrightness *= _StructuralLineBrightness;
                    featureBrightness = lerp(
                        featureBrightness,
                        _RuneBrightness * runeBrightnessScale,
                        rune);
                    featureBrightness = lerp(featureBrightness, _InnerRingBrightness, innerRing);
                    featureBrightness = lerp(featureBrightness, _OuterRimBrightness, outerRim);
                    float portalAngle = atan2(input.portalPosition.y, input.portalPosition.x);
                    float portalAngle01 = (portalAngle / 6.2831853) + 0.5;
                    float travelPulseCount = max(1.0, _TravelPulseCount);
                    float travelPhase = frac(
                        ((portalAngle01 - (_Time.y * _TravelPulseSpeed)) * travelPulseCount) +
                        (length(input.portalPosition) * _TravelPulseRingPhaseOffset));
                    float travelDistance = abs(travelPhase - 0.5) * 2.0;
                    float travelPulse = 1.0 - smoothstep(
                        _TravelPulseWidth,
                        min(1.0, _TravelPulseWidth * 1.8),
                        travelDistance);
                    travelBrightness += circularLine * travelPulse * _TravelPulseBrightness;

                    float distanceFromStrokeCenter = abs((input.uv.y * 2.0) - 1.0);
                    if (_CoreMode > 1.5)
                    {
                        float halo = saturate(1.0 - distanceFromStrokeCenter);
                        alpha *= halo * halo;
                    }
                    else
                    {
                        alpha *= 1.0 - smoothstep(
                            1.0 - _LineEdgeSoftness,
                            1.0,
                            distanceFromStrokeCenter);
                    }
                }

                return float4(
                    _PortalColor.rgb * pulse * _BloomIntensity * travelBrightness *
                        featureBrightness * _ActivationBloomBoost * particleTint,
                    alpha * _PortalColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
