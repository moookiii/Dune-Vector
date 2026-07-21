Shader "DuneVector/HDRP Dune Heat Distortion"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex("Thermal Noise", 2D) = "gray" {}
        _DistortionStrength("Distortion Strength", Float) = 2.2
        _DistortionBlur("Distortion Blur", Range(0, 1)) = 0.08
        _TextureScale("Texture Scale", Float) = 4.5
        _ScrollVelocity("Scroll Velocity", Vector) = (0.035, 0.12, 0, 0)
        _ShellStrengthMultiplier("Shell Strength Multiplier", Float) = 1
        [HideInInspector] _VerticalVeil("Vertical Veil", Float) = 0
        [HideInInspector] _NearFadeStart("Near Fade Start", Float) = 5
        [HideInInspector] _NearFadeEnd("Near Fade End", Float) = 14
        [HDR] _ShimmerColor("Visible Heat Shimmer", Color) = (1.15, 0.9, 0.62, 1)
        _ShimmerOpacity("Visible Heat Shimmer Opacity", Range(0, 0.3)) = 0.08
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
            Name "DuneHeatDistortion"
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
                float _TextureScale;
                float4 _ScrollVelocity;
                float _ShellStrengthMultiplier;
                float _VerticalVeil;
                float _NearFadeStart;
                float _NearFadeEnd;
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
                float viewDepth : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.viewDepth = -TransformWorldToView(TransformObjectToWorld(input.positionOS)).z;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 centeredUv = input.uv - 0.5;
                float radialMask = 1.0 - smoothstep(0.38, 0.5, length(centeredUv));
                float verticalMask = smoothstep(0.0, 0.12, input.uv.y) *
                    smoothstep(0.0, 0.22, 1.0 - input.uv.y);
                float edgeMask = lerp(radialMask, verticalMask, saturate(_VerticalVeil));
                float nearFade = smoothstep(_NearFadeStart, _NearFadeEnd, input.viewDepth);
                edgeMask *= nearFade;

                float2 scroll = _ScrollVelocity.xy * _Time.y;
                float2 primaryUv = (input.uv * _TextureScale) - scroll;
                float2 secondaryUv = (input.uv.yx * (_TextureScale * 1.73)) + (scroll.yx * 0.67);
                float2 primary = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, primaryUv).rg - 0.5;
                float2 secondary = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, secondaryUv).gr - 0.5;
                float wave = sin((input.uv.y * 31.0) - (_Time.y * 2.4));
                float2 distortion = primary + (secondary * 0.55) + float2(wave * 0.12, wave * 0.04);
                distortion *= _DistortionStrength * _ShellStrengthMultiplier * edgeMask;

                return float4(distortion, 1.0, _DistortionBlur * edgeMask);
            }
            ENDHLSL
        }

        Pass
        {
            Name "VisibleHeatShimmer"
            Tags { "LightMode" = "ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertShimmer
            #pragma fragment FragShimmer
            #pragma multi_compile_instancing
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float _DistortionStrength;
                float _DistortionBlur;
                float _TextureScale;
                float4 _ScrollVelocity;
                float _ShellStrengthMultiplier;
                float _VerticalVeil;
                float _NearFadeStart;
                float _NearFadeEnd;
                float4 _ShimmerColor;
                float _ShimmerOpacity;
            CBUFFER_END

            struct ShimmerAttributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShimmerVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float viewDepth : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShimmerVaryings VertShimmer(ShimmerAttributes input)
            {
                ShimmerVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.viewDepth = -TransformWorldToView(TransformObjectToWorld(input.positionOS)).z;
                return output;
            }

            float4 FragShimmer(ShimmerVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 scroll = _ScrollVelocity.xy * _Time.y;
                float3 primary = SAMPLE_TEXTURE2D(
                    _NoiseTex,
                    sampler_NoiseTex,
                    (input.uv * _TextureScale) - scroll).rgb;
                float secondary = SAMPLE_TEXTURE2D(
                    _NoiseTex,
                    sampler_NoiseTex,
                    (input.uv.yx * (_TextureScale * 1.61)) - (scroll.yx * 0.73)).b;
                float sideFade = smoothstep(0.0, 0.18, input.uv.x) *
                    smoothstep(0.0, 0.18, 1.0 - input.uv.x);
                sideFade = lerp(sideFade, 1.0, saturate(_VerticalVeil));
                float verticalFade = smoothstep(0.0, 0.12, input.uv.y) *
                    smoothstep(0.0, 0.24, 1.0 - input.uv.y);
                float pulse = 0.45 + (0.35 * sin((input.uv.y * 24.0) - (_Time.y * 2.1)));
                float shimmer = saturate((primary.b * 0.65) + (secondary * 0.35) + pulse - 0.55);
                float nearFade = smoothstep(_NearFadeStart, _NearFadeEnd, input.viewDepth);
                float alpha = shimmer * sideFade * verticalFade * nearFade * saturate(_VerticalVeil) *
                    _ShimmerOpacity * _ShellStrengthMultiplier;
                clip(alpha - 0.002);
                return float4(_ShimmerColor.rgb, alpha);
            }
            ENDHLSL
        }

    }
    Fallback Off
}
