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
                float2 centeredUv = input.uv - 0.5;
                float edgeMask = 1.0 - smoothstep(0.38, 0.5, length(centeredUv));

                float2 scroll = _ScrollVelocity.xy * _Time.y;
                float2 primaryUv = (input.uv * _TextureScale) + scroll;
                float2 secondaryUv = (input.uv.yx * (_TextureScale * 1.73)) - (scroll.yx * 0.67);
                float2 primary = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, primaryUv).rg - 0.5;
                float2 secondary = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, secondaryUv).gr - 0.5;
                float wave = sin((input.uv.y * 31.0) + (_Time.y * 2.4));
                float2 distortion = primary + (secondary * 0.55) + float2(wave * 0.12, wave * 0.04);
                distortion *= _DistortionStrength * _ShellStrengthMultiplier * edgeMask;

                return float4(distortion, 1.0, _DistortionBlur * edgeMask);
            }
            ENDHLSL
        }

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

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings VertFallback(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float4 FragFallback(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0.0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
