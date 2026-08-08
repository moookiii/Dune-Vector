Shader "DuneVector/URP TV Static"
{
    Properties
    {
        [Header(Signal Source)]
        [MainTexture] _MainTex("Signal Texture", 2D) = "black" {}
        [MainColor] _SignalTint("Signal Tint", Color) = (1, 1, 1, 1)
        _SignalStrength("Signal Strength", Range(0, 1)) = 0
        _StaticAmount("Static Amount", Range(0, 1)) = 1

        [Header(Static Grain)]
        _NoiseResolutionX("Noise Resolution X", Float) = 256
        _NoiseResolutionY("Noise Resolution Y", Float) = 144
        _FrameRate("Frame Rate (0 = Continuous)", Float) = 24
        _TimeScale("Time Scale", Float) = 1
        _Seed("Seed", Float) = 0
        _NoiseContrast("Noise Contrast", Range(0, 8)) = 1
        _NoiseBrightness("Noise Brightness", Range(-1, 1)) = 0
        _ColorNoise("Color Noise", Range(0, 1)) = 0
        [HDR] _StaticDarkColor("Static Dark Color", Color) = (0, 0, 0, 1)
        [HDR] _StaticLightColor("Static Light Color", Color) = (1, 1, 1, 1)

        [Header(Scanlines)]
        _ScanlineCount("Scanline Count", Float) = 180
        _ScanlineStrength("Scanline Strength", Range(0, 1)) = 0.3
        _ScanlineSharpness("Scanline Sharpness", Range(0.1, 8)) = 1
        _ScanlineScrollSpeed("Scanline Scroll Speed", Float) = 0

        [Header(Rolling Bar)]
        _RollBarStrength("Roll Bar Strength", Range(0, 1)) = 0.25
        _RollBarHeight("Roll Bar Height", Range(0, 1)) = 0.06
        _RollBarSoftness("Roll Bar Softness", Range(0.001, 1)) = 0.12
        _RollBarSpeed("Roll Bar Speed", Float) = 0.12
        _RollBarStaticBoost("Roll Bar Static Boost", Range(0, 1)) = 0.5
        _RollBarOffset("Roll Bar Displacement", Range(-0.5, 0.5)) = 0.02

        [Header(Tearing)]
        _TearAmount("Tear Amount", Range(0, 0.5)) = 0.05
        _TearDensity("Tear Density", Range(0, 1)) = 0.15
        _TearRows("Tear Rows", Float) = 48
        _TearSpeed("Tear Speed", Float) = 12

        [Header(Chromatic Aberration)]
        _ChromaticAberration("Chromatic Aberration", Range(0, 0.1)) = 0

        [Header(Flicker)]
        _FlickerAmount("Flicker Amount", Range(0, 1)) = 0.1
        _FlickerRate("Flicker Rate", Float) = 18

        [Header(Vignette)]
        _VignetteStrength("Vignette Strength", Range(0, 1)) = 0.35
        _VignetteScale("Vignette Scale", Range(0.01, 3)) = 1
        _VignettePower("Vignette Power", Range(0.1, 8)) = 2.5

        [Header(Output)]
        _EmissionIntensity("Emission Intensity", Float) = 1
        _Opacity("Opacity", Range(0, 1)) = 1
        _AlphaFromStatic("Alpha From Static", Range(0, 1)) = 0

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Z Test", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SignalTint;
                float4 _StaticDarkColor;
                float4 _StaticLightColor;
                float _SignalStrength;
                float _StaticAmount;
                float _NoiseResolutionX;
                float _NoiseResolutionY;
                float _FrameRate;
                float _TimeScale;
                float _Seed;
                float _NoiseContrast;
                float _NoiseBrightness;
                float _ColorNoise;
                float _ScanlineCount;
                float _ScanlineStrength;
                float _ScanlineSharpness;
                float _ScanlineScrollSpeed;
                float _RollBarStrength;
                float _RollBarHeight;
                float _RollBarSoftness;
                float _RollBarSpeed;
                float _RollBarStaticBoost;
                float _RollBarOffset;
                float _TearAmount;
                float _TearDensity;
                float _TearRows;
                float _TearSpeed;
                float _ChromaticAberration;
                float _FlickerAmount;
                float _FlickerRate;
                float _VignetteStrength;
                float _VignetteScale;
                float _VignettePower;
                float _EmissionIntensity;
                float _Opacity;
                float _AlphaFromStatic;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _ZTest;
                float _Cull;
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
                float fogFactor : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Hash11(float p)
            {
                return Hash21(float2(p, p * 1.61803));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 uv = input.uv;
                float timeSeconds = _Time.y * _TimeScale;

                // Quantize the noise clock so the grain steps at a chosen frame rate.
                float frameRate = max(0.0, _FrameRate);
                float noiseTime = frameRate > 0.0
                    ? floor(timeSeconds * frameRate)
                    : timeSeconds * 60.0;
                noiseTime += _Seed;

                // Rolling brightness bar sweeping up the screen.
                float rollDistance = abs(frac(uv.y - (timeSeconds * _RollBarSpeed) + 0.5) - 0.5) * 2.0;
                float rollBar = 1.0 - smoothstep(
                    saturate(_RollBarHeight),
                    saturate(_RollBarHeight) + _RollBarSoftness,
                    rollDistance);

                // Per-row horizontal tearing.
                float tearRows = max(1.0, _TearRows);
                float tearClock = floor(timeSeconds * max(0.0, _TearSpeed));
                float rowIndex = floor(uv.y * tearRows);
                float rowSelect = Hash21(float2(rowIndex, tearClock));
                float tearMask = step(1.0 - saturate(_TearDensity), rowSelect);
                float tearOffset = (Hash21(float2((rowIndex * 1.7) + 3.1, tearClock + 7.3)) - 0.5) *
                    _TearAmount * tearMask;

                float2 displacedUV = uv;
                displacedUV.x += tearOffset + (rollBar * _RollBarOffset);

                // Signal texture with optional chromatic split.
                float2 aberration = float2(_ChromaticAberration, 0.0);
                float4 signalR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, displacedUV + aberration);
                float4 signalG = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, displacedUV);
                float4 signalB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, displacedUV - aberration);
                float4 signal = float4(signalR.r, signalG.g, signalB.b, signalG.a) * _SignalTint;

                // Static grain sampled on a quantized pixel grid.
                float2 grid = float2(max(1.0, _NoiseResolutionX), max(1.0, _NoiseResolutionY));
                float2 cell = floor(displacedUV * grid);
                float luminanceNoise = Hash21(cell + float2(noiseTime, noiseTime * 0.7));
                float3 channelNoise = float3(
                    Hash21(cell + float2(noiseTime, noiseTime * 0.7)),
                    Hash21(cell + float2(noiseTime * 1.3 + 19.0, noiseTime * 0.4 + 5.0)),
                    Hash21(cell + float2(noiseTime * 0.9 + 71.0, noiseTime * 1.1 + 37.0)));
                float3 noise = lerp(luminanceNoise.xxx, channelNoise, saturate(_ColorNoise));
                noise = saturate(((noise - 0.5) * _NoiseContrast) + 0.5 + _NoiseBrightness);

                float3 staticColor = lerp(_StaticDarkColor.rgb, _StaticLightColor.rgb, noise);

                float staticBlend = saturate(
                    _StaticAmount + (rollBar * _RollBarStaticBoost));
                float3 color = lerp(signal.rgb * _SignalStrength, staticColor, staticBlend);

                // Rolling bar brightening.
                color *= 1.0 + (rollBar * _RollBarStrength);

                // Scanlines.
                float scanPhase = (uv.y + (timeSeconds * _ScanlineScrollSpeed)) * max(0.0, _ScanlineCount);
                float scan = (sin(scanPhase * 6.2831853) * 0.5) + 0.5;
                scan = pow(saturate(scan), max(0.1, _ScanlineSharpness));
                color *= lerp(1.0, scan, saturate(_ScanlineStrength));

                // Flicker.
                float flicker = 1.0 + ((Hash11(floor(timeSeconds * max(0.0, _FlickerRate))) - 0.5) *
                    saturate(_FlickerAmount));
                color *= flicker;

                // Vignette.
                float2 centered = (uv * 2.0) - 1.0;
                float vignette = 1.0 - (saturate(pow(saturate(length(centered) * _VignetteScale),
                    max(0.1, _VignettePower))) * saturate(_VignetteStrength));
                color *= vignette;

                color *= _EmissionIntensity * input.color.rgb;

                float staticLuminance = dot(noise, float3(0.299, 0.587, 0.114));
                float alpha = _Opacity * input.color.a * _StaticLightColor.a;
                alpha = lerp(alpha, alpha * staticLuminance, saturate(_AlphaFromStatic));
                alpha = lerp(alpha, alpha * signal.a, saturate(1.0 - staticBlend));

                color = MixFog(color, input.fogFactor);
                return float4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
