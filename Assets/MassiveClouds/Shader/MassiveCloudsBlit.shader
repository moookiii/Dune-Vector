Shader "MassiveCloudsBlit"
{
	Properties
	{
	    [HideInInspector]
		_MainTex   ("Texture", 2D) = "white" {}
        [Toggle]
        _HORIZONTAL         ("Horizontal?", Float)              = 0
        [Toggle]
        _RelativeHeight     ("RelativeHeight?", Float)          = 0
		_Thickness          ("Thickness", Range(0, 10000))      = 50
		_FromHeight         ("FromHeight", Range(0, 5000))      = 1
		_MaxDistance        ("MaxDistance", Range(0, 60000))    = 5000
	}

	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex Vert
			#pragma fragment MassiveCloudsFragment
            #pragma shader_feature _HORIZONTAL_ON

            #include "MassiveCloudsCommon.cginc"

            sampler2D _ResultTexture;
            half4     _ResultTexture_ST;
            float4    _ResultTexture_TexelSize;

            v2f Vert(appdata v)
            {
                v2f o;
                uint vertexID = v.vertexID;
                
                float2 posUV = float2((vertexID << 1) & 2, vertexID & 2);
                float4 posCS = float4(posUV * 2.0 - 1.0, 0.0, 1.0);
                float2 uv = posUV.xy;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif
                o.vertex = posCS;
                o.uv = float4(uv / _RTHandleScale.xy, 0, 1);
            
                return o;
            }
            
            float4 MassiveCloudsFragment(v2f i) : SV_Target
            {
#if defined(USING_STEREO_MATRICES)
                half4 texCol = tex2Dproj(_ResultTexture, UnityStereoScreenSpaceUVAdjust(i.uv, _MainTex_ST));
#else 
                half4 texCol = tex2Dproj(_ResultTexture, i.uv);
#endif
                return texCol;
            }
			ENDCG
		}
	}
}
