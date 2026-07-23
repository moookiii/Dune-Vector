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
        _OrbitLineCount("Orbit Line Count", Float) = 5
        _OrbitAngularWaves("Orbit Angular Waves", Float) = 2
        _OrbitSpeed("Orbit Speed", Float) = 0.55
        _OrbitLineWidth("Orbit Line Width", Range(0.005, 0.25)) = 0.09
        _OrbitWarp("Orbit Warp", Range(0, 0.2)) = 0.045
        _CoreGlowFill("Core Glow Fill", Range(0, 1)) = 0.08
        _CoreEdgeFeather("Core Edge Feather", Range(0.01, 0.5)) = 0.16
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
                float _OrbitLineCount;
                float _OrbitAngularWaves;
                float _OrbitSpeed;
                float _OrbitLineWidth;
                float _OrbitWarp;
                float _CoreGlowFill;
                float _CoreEdgeFeather;
                float _PulseSpeed;
                float _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float pulse = 1.0 + (sin((_Time.y * _PulseSpeed) + 1.7) * _PulseAmount);
                float alpha = _Opacity * _DistanceFade;

                if (_CoreMode > 0.5 && _CoreMode < 1.5)
                {
                    float2 centered = (input.uv * 2.0) - 1.0;
                    float radius = length(centered);
                    clip(1.0 - radius);

                    float angle = atan2(centered.y, centered.x);
                    float orbitTime = _Time.y * _OrbitSpeed;
                    float angularPhase = (angle * _OrbitAngularWaves) + orbitTime;
                    float bentRadius = radius +
                        (sin(angularPhase + (radius * 8.0)) * _OrbitWarp);
                    float orbitCoordinate = (bentRadius - 0.08) * _OrbitLineCount;
                    float orbitDistance = abs(frac(orbitCoordinate + 0.5) - 0.5) * 2.0;
                    float orbitLines = 1.0 - smoothstep(
                        _OrbitLineWidth,
                        _OrbitLineWidth * 2.2,
                        orbitDistance);

                    float orbitIndex = floor(orbitCoordinate + 0.5);
                    float arcVariation = sin(
                        (angle * (2.0 + fmod(abs(orbitIndex), 3.0))) -
                        (orbitTime * 0.35) +
                        (orbitIndex * 2.17));
                    float arcBrightness = lerp(
                        0.38,
                        1.0,
                        smoothstep(-0.65, 0.35, arcVariation));
                    float edgeFade = 1.0 - smoothstep(
                        1.0 - _CoreEdgeFeather,
                        1.0,
                        radius);
                    float centerFade = smoothstep(0.08, 0.2, radius);
                    float lineEnergy = orbitLines * arcBrightness;
                    alpha *= saturate(_CoreGlowFill + lineEnergy) * edgeFade * centerFade;
                }
                else
                {
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
                    _PortalColor.rgb * pulse * _BloomIntensity,
                    alpha * _PortalColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
