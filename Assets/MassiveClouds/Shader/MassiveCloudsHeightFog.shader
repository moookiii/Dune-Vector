Shader "MassiveCloudsHeightFog"
{
	Properties
	{
	    [HideInInspector]
		_MainTex            ("Texture", 2D)                     = "white" {}
		_FogColor           ("FogColor", Color)                 = (1, 1, 1, 1)
		_GroundHeight       ("GroundHeight", Range(-1000, 1000)) = 0
		_HeightFogFromDistance  ("HeightFogFromDistance", Range(0, 10000)) = 0
		_HeightFogRange     ("HeightFogRange", Range(0.001, 1000)) = 0
		_FarHeightFogRange     ("FarHeightFogRange", Range(0.001, 1000)) = 0
		_HeightFogDensity     ("HeightFogDensity", Range(0, 1)) = 1
    }

	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex MassiveCloudsVert
			#pragma fragment MassiveCloudsFragment
            #pragma shader_feature _HEIGHTFOG_ON
 
			#include "MassiveCloudsCommon.cginc"
			#include "MassiveCloudsHeightFog.cginc"

			ENDCG
		}
	}
}
