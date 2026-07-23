Shader "DuneVector/HDRP Portal Energy"
{
    Properties
    {
        [HDR] _PortalColor("Portal Color", Color) = (4, 1.5, 0.05, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.8
        _CoreMode("Core Mode", Float) = 0
        _SwirlArmCount("Swirl Arm Count", Float) = 5
        _SwirlDensity("Swirl Density", Float) = 12
        _SwirlSpeed("Swirl Speed", Float) = 1.1
        _SwirlLineWidth("Swirl Line Width", Range(0.005, 0.25)) = 0.065
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
                float _CoreMode;
                float _SwirlArmCount;
                float _SwirlDensity;
                float _SwirlSpeed;
                float _SwirlLineWidth;
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
                float alpha = _Opacity;

                if (_CoreMode > 0.5)
                {
                    float2 centered = (input.uv * 2.0) - 1.0;
                    float radius = length(centered);
                    clip(1.0 - radius);

                    float angle = atan2(centered.y, centered.x);
                    float rotatingAngle = angle + (_Time.y * _SwirlSpeed);
                    float spiralPhase = ((rotatingAngle * _SwirlArmCount) -
                        (radius * _SwirlDensity)) / 6.2831853;
                    float spiralDistance = abs(frac(spiralPhase + 0.5) - 0.5) * 2.0;
                    float spiral = 1.0 - smoothstep(
                        _SwirlLineWidth,
                        _SwirlLineWidth * 2.2,
                        spiralDistance);

                    float ringPhase = frac((radius * (_SwirlDensity * 0.42)) -
                        (_Time.y * _SwirlSpeed * 0.16));
                    float ringDistance = abs(ringPhase - 0.5) * 2.0;
                    float energyRings = 1.0 - smoothstep(
                        _SwirlLineWidth * 0.65,
                        _SwirlLineWidth * 1.7,
                        ringDistance);
                    float edgeFade = 1.0 - smoothstep(
                        1.0 - _CoreEdgeFeather,
                        1.0,
                        radius);
                    float centerFade = smoothstep(0.035, 0.18, radius);
                    float lineEnergy = saturate(spiral + (energyRings * 0.42));
                    alpha *= saturate(_CoreGlowFill + lineEnergy) * edgeFade * centerFade;
                }

                return float4(_PortalColor.rgb * pulse, alpha * _PortalColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
