Shader "Hidden/DuneVector/Map Geoglyph Mask"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
        _Color ("Line Color", Color) = (1, 1, 1, 1)
        _HaloColor ("Halo Color", Color) = (0.01, 0.008, 0.005, 0.9)
        _HaloWidthPixels ("Halo Width Pixels", Range(0, 8)) = 4
        _Threshold ("Mask Threshold", Range(0, 1)) = 0.5
        _Softness ("Edge Softness", Range(0.0001, 0.25)) = 0.025
        _RotationSinCos ("Rotation Sin Cos", Vector) = (0, 1, 0, 0)
        _OutputToSourceScale ("Output To Source Scale", Vector) = (1, 1, 0, 0)
        [NoScaleOffset] _SurfaceTexture ("Surface Texture", 2D) = "white" {}
        _SurfaceTextureEnabled ("Surface Texture Enabled", Float) = 0
        _SurfaceTextureTransform ("Surface Texture Transform", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _SurfaceTexture;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _HaloColor;
            float _HaloWidthPixels;
            float _Threshold;
            float _Softness;
            float2 _RotationSinCos;
            float2 _OutputToSourceScale;
            float _SurfaceTextureEnabled;
            float4 _SurfaceTextureTransform;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float SampleMask(float2 sourceUv)
            {
                float inside =
                    step(0.0, sourceUv.x) *
                    step(sourceUv.x, 1.0) *
                    step(0.0, sourceUv.y) *
                    step(sourceUv.y, 1.0);
                return tex2D(_MainTex, saturate(sourceUv)).r * inside;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 outputPosition =
                    (input.uv - 0.5) * _OutputToSourceScale;
                float sine = _RotationSinCos.x;
                float cosine = _RotationSinCos.y;
                float2 sourceUv = float2(
                    (cosine * outputPosition.x) + (sine * outputPosition.y),
                    (-sine * outputPosition.x) + (cosine * outputPosition.y)) + 0.5;
                float mask = SampleMask(sourceUv);
                float lineCoverage = smoothstep(
                    _Threshold - _Softness,
                    _Threshold + _Softness,
                    mask);

                float2 pixelX = ddx(sourceUv) * _HaloWidthPixels;
                float2 pixelY = ddy(sourceUv) * _HaloWidthPixels;
                float2 diagonalA = (pixelX + pixelY) * 0.70710678;
                float2 diagonalB = (pixelX - pixelY) * 0.70710678;
                float dilatedMask = mask;
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + pixelX));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - pixelX));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + pixelY));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - pixelY));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + diagonalA));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - diagonalA));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + diagonalB));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - diagonalB));

                float2 halfPixelX = pixelX * 0.5;
                float2 halfPixelY = pixelY * 0.5;
                float2 halfDiagonalA = diagonalA * 0.5;
                float2 halfDiagonalB = diagonalB * 0.5;
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + halfPixelX));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - halfPixelX));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + halfPixelY));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - halfPixelY));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + halfDiagonalA));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - halfDiagonalA));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv + halfDiagonalB));
                dilatedMask = max(dilatedMask, SampleMask(sourceUv - halfDiagonalB));

                float dilatedCoverage = smoothstep(
                    _Threshold - _Softness,
                    _Threshold + _Softness,
                    dilatedMask);
                float lineAlpha = _Color.a * lineCoverage;
                float haloAlpha =
                    _HaloColor.a *
                    saturate(dilatedCoverage - lineCoverage);
                float outputAlpha = lineAlpha + (haloAlpha * (1.0 - lineAlpha));
                float2 surfaceUv =
                    (sourceUv * _SurfaceTextureTransform.xy) +
                    _SurfaceTextureTransform.zw;
                float3 surfaceColor = lerp(
                    float3(1.0, 1.0, 1.0),
                    tex2D(_SurfaceTexture, surfaceUv).rgb,
                    saturate(_SurfaceTextureEnabled));
                float3 outputColor =
                    ((_Color.rgb * surfaceColor * lineAlpha) +
                     (_HaloColor.rgb * surfaceColor * haloAlpha * (1.0 - lineAlpha))) /
                    max(outputAlpha, 0.0001);
                return fixed4(outputColor, outputAlpha);
            }
            ENDCG
        }
    }
}
