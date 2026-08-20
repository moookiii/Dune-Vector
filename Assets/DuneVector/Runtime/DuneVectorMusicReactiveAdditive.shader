Shader "DuneVector/URP Music Reactive Additive"
{
    Properties
    {
        [HideInInspector] _EdgeSoftness("Edge Softness", Range(0.001, 0.5)) = 0.18
        [HideInInspector] _ShapeMode("Shape Mode", Float) = 0
        [HideInInspector] _StreakEmission("Streak Emission", Float) = 1
        [HideInInspector] _StreakTipSharpness("Streak Tip Sharpness", Float) = 1
        [HideInInspector] _StreakMinimumWidth("Streak Minimum Width", Float) = 0.06
        [HideInInspector] _StreakCoreWidth("Streak Core Width", Float) = 0.24
        [HideInInspector] _StreakCoreBrightness("Streak Core Brightness", Float) = 1.8
        [HideInInspector] _StreakHaloBrightness("Streak Halo Brightness", Float) = 0.42
        [HideInInspector] _StreakEndFade("Streak End Fade", Float) = 0.08
        [HideInInspector] _StreakSpikeProfile("Streak Spike Profile", Float) = 0
        [HideInInspector] _StreakSpikeBaseWidth("Streak Spike Base Width", Float) = 1
        [HideInInspector] _StreakSpikeTipWidth("Streak Spike Tip Width", Float) = 0.08
        [HideInInspector] _StreakSpikeTaper("Streak Spike Taper", Float) = 1.6
        [HideInInspector] _StreakSpikeFlip("Streak Spike Flip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "MusicReactiveAdditive"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _EdgeSoftness;
                half _ShapeMode;
                half _StreakEmission;
                half _StreakTipSharpness;
                half _StreakMinimumWidth;
                half _StreakCoreWidth;
                half _StreakCoreBrightness;
                half _StreakHaloBrightness;
                half _StreakEndFade;
                half _StreakSpikeProfile;
                half _StreakSpikeBaseWidth;
                half _StreakSpikeTipWidth;
                half _StreakSpikeTaper;
                half _StreakSpikeFlip;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_ShapeMode > 0.5h)
                {
                    half longitudinal = saturate(input.uv.x);
                    half triangularProfile = 1.0h - abs(longitudinal * 2.0h - 1.0h);
                    half taperedProfile = lerp(
                        0.35h,
                        1.0h,
                        pow(saturate(triangularProfile), _StreakTipSharpness));
                    half spikeAxis = _StreakSpikeFlip > 0.5h
                        ? 1.0h - longitudinal
                        : longitudinal;
                    half spikeProfile = lerp(
                        _StreakSpikeBaseWidth,
                        _StreakSpikeTipWidth,
                        pow(saturate(spikeAxis), max(_StreakSpikeTaper, 0.01h)));
                    half silhouetteWidth = _StreakSpikeProfile > 0.5h
                        ? spikeProfile
                        : _StreakMinimumWidth * taperedProfile;
                    half transverse = abs(input.uv.y * 2.0h - 1.0h);
                    half antialias = max(fwidth(transverse), 0.001h);
                    half body = 1.0h - smoothstep(
                        silhouetteWidth - antialias,
                        silhouetteWidth + antialias,
                        transverse);
                    half coreWidth = min(
                        silhouetteWidth,
                        max(_StreakMinimumWidth, silhouetteWidth * _StreakCoreWidth));
                    half core = 1.0h - smoothstep(
                        coreWidth - antialias,
                        coreWidth + antialias,
                        transverse);
                    half endFade = smoothstep(0.0h, _StreakEndFade, longitudinal)
                        * (1.0h - smoothstep(1.0h - _StreakEndFade, 1.0h, longitudinal));
                    half brightness = _StreakHaloBrightness + core * _StreakCoreBrightness;
                    return half4(
                        input.color.rgb * (_StreakEmission * brightness),
                        input.color.a * body * endFade);
                }

                half edgeDistance = min(input.uv.y, 1.0h - input.uv.y);
                half edge = smoothstep(0.0h, max(_EdgeSoftness, 0.001h), edgeDistance);
                return half4(input.color.rgb, input.color.a * edge);
            }
            ENDHLSL
        }
    }
}
