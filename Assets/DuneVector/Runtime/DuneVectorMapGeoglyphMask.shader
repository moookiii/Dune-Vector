Shader "Hidden/DuneVector/Map Geoglyph Mask"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
        _Color ("Line Color", Color) = (1, 1, 1, 1)
        _Threshold ("Mask Threshold", Range(0, 1)) = 0.5
        _Softness ("Edge Softness", Range(0.0001, 0.25)) = 0.025
        _RotationSinCos ("Rotation Sin Cos", Vector) = (0, 1, 0, 0)
        _OutputToSourceScale ("Output To Source Scale", Vector) = (1, 1, 0, 0)
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
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Threshold;
            float _Softness;
            float2 _RotationSinCos;
            float2 _OutputToSourceScale;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
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
                float inside =
                    step(0.0, sourceUv.x) *
                    step(sourceUv.x, 1.0) *
                    step(0.0, sourceUv.y) *
                    step(sourceUv.y, 1.0);
                float mask = tex2D(_MainTex, saturate(sourceUv)).r * inside;
                float alpha = smoothstep(
                    _Threshold - _Softness,
                    _Threshold + _Softness,
                    mask);
                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
