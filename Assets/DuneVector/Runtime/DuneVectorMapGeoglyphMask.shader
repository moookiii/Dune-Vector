Shader "Hidden/DuneVector/Map Geoglyph Mask"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "black" {}
        _Color ("Line Color", Color) = (1, 1, 1, 1)
        _Threshold ("Mask Threshold", Range(0, 1)) = 0.5
        _Softness ("Edge Softness", Range(0.0001, 0.25)) = 0.025
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
        Blend SrcAlpha OneMinusSrcAlpha

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

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float mask = tex2D(_MainTex, input.uv).r;
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
