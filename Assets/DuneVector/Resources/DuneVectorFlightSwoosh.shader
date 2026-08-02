Shader "DuneVector/URP Flight Swoosh"
{
    Properties
    {
        _EdgeSoftness("Edge Softness", Range(0.01, 0.49)) = 0.28
        _TipSoftness("Tip Softness", Range(0.01, 0.49)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _EdgeSoftness;
                float _TipSoftness;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(DuneVectorSwooshPerInstance)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SwooshColor)
            UNITY_INSTANCING_BUFFER_END(DuneVectorSwooshPerInstance)

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
                float edgeDistance = 1.0 - abs((input.uv.x * 2.0) - 1.0);
                float edgeFade = smoothstep(0.0, _EdgeSoftness, edgeDistance);
                float leadingTip = smoothstep(0.0, _TipSoftness, input.uv.y);
                float trailingTip = smoothstep(0.0, _TipSoftness, 1.0 - input.uv.y);
                float4 color = UNITY_ACCESS_INSTANCED_PROP(DuneVectorSwooshPerInstance, _SwooshColor);
                color.a *= edgeFade * leadingTip * trailingTip;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
