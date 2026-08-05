Shader "DuneVector/URP Music World Glitch"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "MusicWorldGlitch"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _DVMusicGlitchParameters;
            float4 _DVMusicGlitchSafety;
            half4 _DVMusicGlitchTint;

            half Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half intensity = saturate(_DVMusicGlitchParameters.x);
                float sliceCount = max(1.0, _DVMusicGlitchParameters.z);
                float slice = floor(uv.y * sliceCount);
                half noise = Hash11(slice + _DVMusicGlitchParameters.y * 19.19);
                float shift = (noise * 2.0 - 1.0) * intensity * _DVMusicGlitchParameters.w;

                float2 centerDistance = abs(uv - 0.5);
                half protectedCenter = (1.0 - smoothstep(
                    _DVMusicGlitchSafety.x,
                    _DVMusicGlitchSafety.x + _DVMusicGlitchSafety.z,
                    centerDistance.x))
                    * (1.0 - smoothstep(
                        _DVMusicGlitchSafety.y,
                        _DVMusicGlitchSafety.y + _DVMusicGlitchSafety.z,
                        centerDistance.y));
                half safety = lerp(1.0h, _DVMusicGlitchSafety.w, protectedCenter);
                shift *= safety;
                intensity *= safety;

                half red = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(shift, 0.0)).r;
                half green = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                half blue = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(shift, 0.0)).b;
                half3 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 split = half3(red, green, blue);
                half flash = intensity * noise;
                half3 color = lerp(source, split, intensity);
                color = lerp(color, _DVMusicGlitchTint.rgb, flash * _DVMusicGlitchTint.a);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
