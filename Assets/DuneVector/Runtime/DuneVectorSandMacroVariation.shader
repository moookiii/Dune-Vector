Shader "DuneVector/URP Sand Macro Variation"
{
    Properties
    {
        [MainTexture] _BaseMap("Dune Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.14
        _Metallic("Metallic", Range(0, 1)) = 0

        [HideInInspector] _DVSandVariationEnabled("Variation Enabled", Float) = 1
        [HideInInspector] _DVSandLightColor("Light Sand", Color) = (1, 0.72, 0.32, 1)
        [HideInInspector] _DVSandMidColor("Mid Sand", Color) = (0.93, 0.48, 0.16, 1)
        [HideInInspector] _DVSandDarkColor("Dark Sand", Color) = (0.48, 0.2, 0.09, 1)
        [HideInInspector] _DVSandMacroPatternSize("Macro Pattern Size", Float) = 500
        [HideInInspector] _DVSandSecondaryPatternSize("Secondary Pattern Size", Float) = 100
        [HideInInspector] _DVSandMacroNoiseOffset("Macro Offset", Vector) = (1200, -800, 0, 0)
        [HideInInspector] _DVSandBrightnessNoiseOffset("Brightness Offset", Vector) = (-370, 910, 0, 0)
        [HideInInspector] _DVSandSaturationNoiseOffset("Saturation Offset", Vector) = (1420, 480, 0, 0)
        [HideInInspector] _DVSandLogicalOriginOffset("Logical Origin Offset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DVSandDarkThreshold("Dark Threshold", Range(0, 1)) = 0.38
        [HideInInspector] _DVSandLightThreshold("Light Threshold", Range(0, 1)) = 0.62
        [HideInInspector] _DVSandTransitionSoftness("Transition Softness", Range(0.01, 0.25)) = 0.08
        [HideInInspector] _DVSandMacroBlendStrength("Macro Blend Strength", Range(0, 0.5)) = 0.24
        [HideInInspector] _DVSandBrightnessRange("Brightness Range", Vector) = (0.94, 1.04, 0, 0)
        [HideInInspector] _DVSandSaturationRange("Saturation Range", Vector) = (0.94, 1.04, 0, 0)
        [HideInInspector] _DVSandDarkSaturationMultiplier("Dark Saturation", Range(0.9, 1)) = 0.96
        [HideInInspector] _DVSandSmoothnessVariation("Smoothness Variation", Range(0, 0.05)) = 0.025
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
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DVSandLightColor;
                half4 _DVSandMidColor;
                half4 _DVSandDarkColor;
                float4 _DVSandMacroNoiseOffset;
                float4 _DVSandBrightnessNoiseOffset;
                float4 _DVSandSaturationNoiseOffset;
                float4 _DVSandLogicalOriginOffset;
                float4 _DVSandBrightnessRange;
                float4 _DVSandSaturationRange;
                half _Smoothness;
                half _Metallic;
                half _DVSandVariationEnabled;
                float _DVSandMacroPatternSize;
                float _DVSandSecondaryPatternSize;
                half _DVSandDarkThreshold;
                half _DVSandLightThreshold;
                half _DVSandTransitionSoftness;
                half _DVSandMacroBlendStrength;
                half _DVSandDarkSaturationMultiplier;
                half _DVSandSmoothnessVariation;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 GradientDirection(float2 lattice)
            {
                lattice -= floor(lattice / 289.0) * 289.0;
                float hash = lattice.x;
                hash = (hash * 34.0 + 1.0) * hash;
                hash = (hash + lattice.y) - floor((hash + lattice.y) / 289.0) * 289.0;
                hash = (hash * 34.0 + 1.0) * hash;
                hash -= floor(hash / 289.0) * 289.0;
                hash = frac(hash / 41.0) * 2.0 - 1.0;
                float2 gradient = float2(hash - floor(hash + 0.5), abs(hash) - 0.5);
                return normalize(gradient + 0.00001);
            }

            half GradientNoise(float2 coordinate)
            {
                float2 lattice = floor(coordinate);
                float2 fraction = frac(coordinate);
                float2 blend = fraction * fraction * fraction *
                    (fraction * (fraction * 6.0 - 15.0) + 10.0);

                float lowerLeft = dot(GradientDirection(lattice), fraction);
                float lowerRight = dot(
                    GradientDirection(lattice + float2(1.0, 0.0)),
                    fraction - float2(1.0, 0.0));
                float upperLeft = dot(
                    GradientDirection(lattice + float2(0.0, 1.0)),
                    fraction - float2(0.0, 1.0));
                float upperRight = dot(GradientDirection(lattice + 1.0), fraction - 1.0);

                float gradient = lerp(
                    lerp(lowerLeft, lowerRight, blend.x),
                    lerp(upperLeft, upperRight, blend.x),
                    blend.y);
                return saturate(gradient * 0.5 + 0.5);
            }

            half3 ApplySaturation(half3 color, half saturation)
            {
                half luminance = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                return lerp(luminance.xxx, color, saturation);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 textureSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float2 logicalWorldXZ = input.positionWS.xz + _DVSandLogicalOriginOffset.xy;
                float macroPatternSize = max(_DVSandMacroPatternSize, 0.01);
                float secondaryPatternSize = max(_DVSandSecondaryPatternSize, 0.01);

                half macroNoise = GradientNoise(
                    (logicalWorldXZ + _DVSandMacroNoiseOffset.xy) / macroPatternSize);
                half transitionSoftness = max(_DVSandTransitionSoftness, 0.001h);
                half darkThreshold = min(_DVSandDarkThreshold, _DVSandLightThreshold);
                half lightThreshold = max(_DVSandDarkThreshold, _DVSandLightThreshold);
                half darkToMid = smoothstep(
                    darkThreshold - transitionSoftness,
                    darkThreshold + transitionSoftness,
                    macroNoise);
                half midToLight = smoothstep(
                    lightThreshold - transitionSoftness,
                    lightThreshold + transitionSoftness,
                    macroNoise);
                half darkWeight = 1.0h - darkToMid;

                half3 macroPalette = lerp(_DVSandDarkColor.rgb, _DVSandMidColor.rgb, darkToMid);
                macroPalette = lerp(macroPalette, _DVSandLightColor.rgb, midToLight);
                half3 paletteRatio = macroPalette / max(_DVSandMidColor.rgb, 0.02h);
                half macroStrength = saturate(_DVSandMacroBlendStrength) * _DVSandVariationEnabled;
                half3 albedo = textureSample.rgb * lerp(1.0h.xxx, paletteRatio, macroStrength);

                half brightnessNoise = GradientNoise(
                    (logicalWorldXZ + _DVSandBrightnessNoiseOffset.xy) / secondaryPatternSize);
                half saturationNoise = GradientNoise(
                    (logicalWorldXZ + _DVSandSaturationNoiseOffset.xy) / secondaryPatternSize);
                half brightness = lerp(_DVSandBrightnessRange.x, _DVSandBrightnessRange.y, brightnessNoise);
                half saturation = lerp(_DVSandSaturationRange.x, _DVSandSaturationRange.y, saturationNoise);
                saturation *= lerp(1.0h, _DVSandDarkSaturationMultiplier, darkWeight);
                albedo *= lerp(1.0h, brightness, _DVSandVariationEnabled);
                albedo = lerp(albedo, ApplySaturation(albedo, saturation), _DVSandVariationEnabled);

                float2 smoothnessCoordinate = float2(logicalWorldXZ.y, -logicalWorldXZ.x) +
                    float2(_DVSandSaturationNoiseOffset.y, -_DVSandSaturationNoiseOffset.x);
                half smoothnessNoise = GradientNoise(smoothnessCoordinate / secondaryPatternSize);
                half smoothness = saturate(
                    _Smoothness +
                    ((smoothnessNoise * 2.0h - 1.0h) *
                    _DVSandSmoothnessVariation * _DVSandVariationEnabled));

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half diffuseTerm = saturate(dot(normalWS, mainLight.direction));
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirectionWS);
                half specularPower = exp2(3.0h + smoothness * 8.0h);
                half specularTerm = pow(saturate(dot(normalWS, halfDirection)), specularPower) * smoothness;
                half3 specularColor = lerp(0.04h.xxx, albedo, _Metallic);

                half3 ambient = SampleSH(normalWS) * albedo;
                half3 direct = mainLight.color * attenuation *
                    ((albedo * diffuseTerm) + (specularColor * specularTerm));
                half3 color = ambient + direct;
                color = MixFog(color, input.fogFactor);
                return half4(color, textureSample.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack Off
}
