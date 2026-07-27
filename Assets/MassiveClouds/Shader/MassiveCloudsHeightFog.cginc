#ifndef MASSIVE_CLOUDS_HEIGHT_FOG_INCLUDED
#define MASSIVE_CLOUDS_HEIGHT_FOG_INCLUDED

#include "Includes/MassiveCloudsScreenSpace.cginc"
#include "Includes/MassiveCloudsLight.cginc"
#include "Includes/MassiveCloudsFog.cginc"

fixed4 MassiveCloudsFragment(v2f i) : SV_Target
{
    ScreenSpace ss = CreateScreenSpace(i.uv);
    float exposureMultiplier = ExposureMultiplier();

#if defined(USING_STEREO_MATRICES)
    half4 screenCol = tex2Dproj(_MainTex, UnityStereoScreenSpaceUVAdjust(i.uv, _MainTex_ST));
#else 
    half4 screenCol = tex2Dproj(_MainTex, i.uv);
#endif

    #if defined(_HEIGHTFOG_ON)
    HeightFogFragment(screenCol, ss, exposureMultiplier);
    #endif
    screenCol.a = 0;

    return screenCol;
}

#endif