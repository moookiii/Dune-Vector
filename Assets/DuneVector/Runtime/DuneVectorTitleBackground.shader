Shader "Hidden/DuneVector/Title Background"
{
    Properties
    {
        _MainTex ("Video Frame", 2D) = "black" {}
        _Saturation ("Saturation At Full Grade", Range(0, 1)) = 0.45
        _Brightness ("Brightness At Full Grade", Range(0, 1)) = 0.72
        _GradeStartV ("Grade Start V", Range(0, 1)) = 0.45
        _GradeFullV ("Grade Full V", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Saturation;
            float _Brightness;
            float _GradeStartV;
            float _GradeFullV;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);

                // Texture v runs upward, so the band starts high on the frame and reaches full
                // strength lower down. The caller hands both bounds over already converted out of
                // screen space, which is where the designer authors them.
                float span = _GradeStartV - _GradeFullV;
                float amount = saturate((_GradeStartV - i.uv.y) / max(1e-5, span));

                float3 source = float3(color.rgb);
                float luminance = dot(source, float3(0.2126, 0.7152, 0.0722));
                float3 graded = lerp(source, float3(luminance, luminance, luminance), (1.0 - _Saturation) * amount);
                graded *= lerp(1.0, _Brightness, amount);

                return fixed4(graded, color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
