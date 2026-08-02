Shader "DuneVector/URP Y2K Cloud Layer"
{
    Properties
    {
        [HideInInspector] _CloudColor("Cloud Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _CloudHighlight("Cloud Highlight", Color) = (1, 1, 1, 1)
        [HideInInspector] _CloudPearl("Cloud Pearl", Color) = (1, 1, 1, 1)
        [HideInInspector] _CloudOpacity("Cloud Opacity", Float) = 0
        [HideInInspector] _CloudAltitude("Cloud Altitude", Float) = 0.28
        [HideInInspector] _CloudThickness("Cloud Thickness", Float) = 0.2
        [HideInInspector] _CloudScale("Cloud Scale", Float) = 3.8
        [HideInInspector] _CloudSoftness("Cloud Softness", Float) = 0.075
        [HideInInspector] _CloudHighlightStrength("Cloud Highlight Strength", Float) = 0.62
        [HideInInspector] _CloudPearlStrength("Cloud Pearl Strength", Float) = 0.24
        [HideInInspector] _CloudDriftSpeed("Cloud Drift Speed", Float) = 0.012
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _CloudColor;
            float4 _CloudHighlight;
            float4 _CloudPearl;
            float _CloudOpacity;
            float _CloudAltitude;
            float _CloudThickness;
            float _CloudScale;
            float _CloudSoftness;
            float _CloudHighlightStrength;
            float _CloudPearlStrength;
            float _CloudDriftSpeed;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                float3 blend = local * local * (3.0 - 2.0 * local);

                float n000 = Hash31(cell + float3(0.0, 0.0, 0.0));
                float n100 = Hash31(cell + float3(1.0, 0.0, 0.0));
                float n010 = Hash31(cell + float3(0.0, 1.0, 0.0));
                float n110 = Hash31(cell + float3(1.0, 1.0, 0.0));
                float n001 = Hash31(cell + float3(0.0, 0.0, 1.0));
                float n101 = Hash31(cell + float3(1.0, 0.0, 1.0));
                float n011 = Hash31(cell + float3(0.0, 1.0, 1.0));
                float n111 = Hash31(cell + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, blend.x);
                float nx10 = lerp(n010, n110, blend.x);
                float nx01 = lerp(n001, n101, blend.x);
                float nx11 = lerp(n011, n111, blend.x);
                return lerp(lerp(nx00, nx10, blend.y), lerp(nx01, nx11, blend.y), blend.z);
            }

            float CloudNoise(float3 p)
            {
                float value = Noise3(p) * 0.58;
                value += Noise3(p * 1.93 + 7.17) * 0.29;
                value += Noise3(p * 3.71 - 4.26) * 0.13;
                return value;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 skyDirection = normalize(input.directionWS);
                float skyUp = skyDirection.y;
                float upperHemisphere = smoothstep(-0.005, 0.025, skyUp);
                float timeOffset = _TimeParameters.x * _CloudDriftSpeed;
                float3 cloudPosition = skyDirection * _CloudScale;
                cloudPosition += float3(timeOffset, timeOffset * 0.19, -timeOffset * 0.63);
                float broadNoise = CloudNoise(cloudPosition);
                float cloudBandDistance = abs(skyUp - _CloudAltitude) / max(_CloudThickness, 0.001);
                float cloudField = broadNoise - cloudBandDistance * 0.22;
                float cloudMask = smoothstep(
                    0.5 - _CloudSoftness,
                    0.5 + _CloudSoftness,
                    cloudField) * upperHemisphere * _CloudOpacity;

                float glossNoise = Noise3(cloudPosition * 2.21 + 12.8);
                float upperGloss = saturate(
                    (skyUp - (_CloudAltitude - _CloudThickness)) /
                    max(_CloudThickness * 1.7, 0.001));
                float gloss = pow(saturate(glossNoise * upperGloss), 2.4)
                    * _CloudHighlightStrength;
                float azimuth = atan2(skyDirection.x, skyDirection.z);
                float pearlSweep = pow(saturate(
                    0.5 + 0.5 * sin(azimuth * 2.0 + broadNoise * 5.0 + 0.8)), 5.0);
                float3 cloudColor = lerp(
                    _CloudColor.rgb,
                    _CloudHighlight.rgb,
                    saturate(gloss));
                cloudColor += _CloudPearl.rgb * (pearlSweep * _CloudPearlStrength);
                return float4(cloudColor, saturate(cloudMask));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
