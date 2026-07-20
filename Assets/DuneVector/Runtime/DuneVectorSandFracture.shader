Shader "DuneVector/HDRP Sand Fracture"
{
    Properties
    {
        [HDR] _Color("Fracture Color", Color) = (8, 8, 8, 1)
        _Reveal("Reveal", Range(0, 1)) = 0
        _Width("Width", Range(0, 1)) = 0.05
        _Intensity("Intensity", Float) = 1
        _Fade("Fade", Range(0, 1)) = 1
        _EdgeNoiseScale("Edge Noise Scale", Float) = 1
        _EdgeNoiseStrength("Edge Noise Strength", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
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
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Reveal;
                float _Width;
                float _Intensity;
                float _Fade;
                float _EdgeNoiseScale;
                float _EdgeNoiseStrength;
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
                float3 positionAWS : TEXCOORD1;
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
                output.uv = input.uv;
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float noise = Hash21(floor(input.positionAWS.xz * _EdgeNoiseScale));
                float halfWidth = saturate(_Width * (1.0 + ((noise - 0.5) * _EdgeNoiseStrength)));
                float across = abs((input.uv.y * 2.0) - 1.0);
                float widthMask = 1.0 - smoothstep(halfWidth * 0.82, max(halfWidth, 0.001), across);
                float revealFeather = max(fwidth(input.uv.x) * 2.0, 0.01);
                float revealMask = 1.0 - smoothstep(_Reveal - revealFeather, _Reveal + revealFeather, input.uv.x);
                float alpha = saturate(widthMask * revealMask * _Fade * _Color.a);
                clip(alpha - 0.001);
                return float4(_Color.rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
