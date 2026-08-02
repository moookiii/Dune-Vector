Shader "Hidden/DuneVector/HDRP Y2K Sky"
{
    HLSLINCLUDE

    #pragma vertex Vert
    #pragma editor_sync_compilation
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyUtils.hlsl"

    float4 _SkyTop;
    float4 _SkyMiddle;
    float4 _SkyBottom;
    float _GradientDiffusion;
    float4 _HorizonGlowColor;
    float _HorizonGlowSize;
    float _HorizonGlowIntensity;
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
    float4 _StructureColor;
    float _StructureOpacity;
    float _ArcAltitude;
    float _ArcCurvature;
    float _ArcThickness;
    float _ArcFrequency;
    float _RingAltitude;
    float _RingSpacing;
    float _RingThickness;
    float _GridOpacity;
    float _GridScale;
    float _GridHeight;
    float _GridLineThickness;
    float4 _ReactiveFrontColor;
    float _ReactiveFrontIntensity;
    float _ReactiveFrontCount;
    float _ReactiveFrontTravelSpeed;
    float _ReactiveFrontThickness;
    float _ReactiveFrontCurvature;
    float _ReactiveFrontAltitude;
    float _ReactiveFrontVerticalSpan;
    float _ReactiveBassExpansion;
    float _ReactiveFrontEnergyResponse;
    float _ReactiveFrontBassResponse;
    float _ReactiveFrontPulseResponse;
    float _ReactiveFrontPressureWidth;
    float _ReactiveFrontPressureOpacity;
    float4 _ReactiveAuroraColor;
    float _ReactiveAuroraIntensity;
    float _ReactiveAuroraAltitude;
    float _ReactiveAuroraThickness;
    float _ReactiveAuroraWaviness;
    float _ReactiveAuroraTravelSpeed;
    float _ReactiveAuroraFrequency;
    float _ReactiveAuroraSecondaryIntensity;
    float _ReactiveAuroraShimmerAmount;
    float4 _ReactiveLightningColor;
    float _ReactiveLightningIntensity;
    float _ReactiveLightningSectorCount;
    float _ReactiveLightningWidth;
    float _ReactiveLightningJaggedness;
    float _ReactiveLightningRetargetRate;
    float _ReactiveLightningSustainResponse;
    float _ReactiveLightningBranchIntensity;
    float _ReactiveMusicEnergy;
    float _ReactiveMusicBass;
    float _ReactiveMusicMids;
    float _ReactiveMusicHighs;
    float _ReactiveBassPulse;
    float _ReactiveHighPulse;
    float _RenderForCubemap;
    float _SkyIntensity;

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
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

    float SoftLine(float distanceToLine, float thickness)
    {
        float antialias = max(fwidth(distanceToLine), thickness * 0.35);
        return 1.0 - smoothstep(thickness, thickness + antialias, distanceToLine);
    }

    float3 RenderY2KSky(float3 viewDirWS)
    {
        float3 skyDirection = normalize(float3(viewDirWS.x, -viewDirWS.y, viewDirWS.z));
        float skyUp = skyDirection.y;
        float verticalGradient = viewDirWS.y * _GradientDiffusion;
        float topLerp = saturate(-verticalGradient);
        float bottomLerp = saturate(verticalGradient);
        float3 color = lerp(_SkyMiddle.rgb, _SkyBottom.rgb, bottomLerp);
        color = lerp(color, _SkyTop.rgb, topLerp);

        float horizon = 1.0 - smoothstep(0.0, max(_HorizonGlowSize, 0.0001), abs(skyUp));
        color += _HorizonGlowColor.rgb * (_HorizonGlowIntensity * horizon * horizon);

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
        float upperGloss = saturate((skyUp - (_CloudAltitude - _CloudThickness)) / max(_CloudThickness * 1.7, 0.001));
        float gloss = pow(saturate(glossNoise * upperGloss), 2.4) * _CloudHighlightStrength;
        float azimuth = atan2(skyDirection.x, skyDirection.z);
        float pearlSweep = pow(saturate(0.5 + 0.5 * sin(azimuth * 2.0 + broadNoise * 5.0 + 0.8)), 5.0);
        float3 cloudColor = _CloudColor.rgb;
        cloudColor = lerp(cloudColor, _CloudHighlight.rgb, saturate(gloss));
        cloudColor += _CloudPearl.rgb * (pearlSweep * _CloudPearlStrength);
        color = lerp(color, cloudColor, saturate(cloudMask));

        float screenOnly = 1.0 - saturate(_RenderForCubemap);
        float normalizedAzimuth = azimuth * (1.0 / TWO_PI) + 0.5;
        float repeatedArc = frac(normalizedAzimuth * _ArcFrequency + 0.5) - 0.5;
        float arcTarget = _ArcAltitude + _ArcCurvature * repeatedArc * repeatedArc;
        float arc = SoftLine(abs(skyUp - arcTarget), _ArcThickness);
        float arcWindow = smoothstep(0.08, 0.3, abs(repeatedArc)) * (1.0 - smoothstep(0.43, 0.5, abs(repeatedArc)));
        arc *= arcWindow * upperHemisphere;

        float ringCoordinate = (skyUp - _RingAltitude) / max(_RingSpacing, 0.001);
        float nearestRing = abs(frac(ringCoordinate + 0.5) - 0.5) * _RingSpacing;
        float rings = SoftLine(nearestRing, _RingThickness);
        float ringBand = smoothstep(_RingAltitude - _RingSpacing, _RingAltitude, skyUp)
            * (1.0 - smoothstep(_RingAltitude + _RingSpacing * 2.6, _RingAltitude + _RingSpacing * 3.2, skyUp));
        rings *= ringBand * upperHemisphere;

        float azimuthGrid = abs(frac(normalizedAzimuth * _GridScale + 0.5) - 0.5);
        float heightGrid = abs(frac(skyUp * _GridScale * 1.8 + 0.5) - 0.5);
        float gridLines = max(
            SoftLine(azimuthGrid, _GridLineThickness),
            SoftLine(heightGrid, _GridLineThickness));
        float gridBand = smoothstep(0.0, 0.012, skyUp)
            * (1.0 - smoothstep(_GridHeight * 0.62, _GridHeight, skyUp));
        float gridWindow = smoothstep(0.18, 0.82, 0.5 + 0.5 * sin(azimuth * 2.0 + 1.1));
        float grid = gridLines * gridBand * gridWindow * _GridOpacity;

        float structureMask = saturate(max(arc, rings * 0.72) + grid);
        color += _StructureColor.rgb * (structureMask * _StructureOpacity * screenOnly);

        // The resonance front reads as a moving weather system instead of a conventional equalizer.
        float reactiveTime = _TimeParameters.x;
        float frontHeight = skyUp - _ReactiveFrontAltitude;
        float frontWindow = smoothstep(-0.04, 0.04, frontHeight)
            * (1.0 - smoothstep(
                _ReactiveFrontVerticalSpan * 0.72,
                _ReactiveFrontVerticalSpan,
                frontHeight));
        float frontWarp = (Noise3(skyDirection * 5.0 + reactiveTime * 0.035) - 0.5) * 0.42;
        float seamlessFrontCount = max(1.0, round(_ReactiveFrontCount));
        float frontCoordinate = normalizedAzimuth * seamlessFrontCount
            + frontHeight * _ReactiveFrontCurvature
            + frontWarp
            - reactiveTime * _ReactiveFrontTravelSpeed;
        float frontDistance = abs(frac(frontCoordinate) - 0.5);
        float expandedThickness = _ReactiveFrontThickness
            * (1.0 + _ReactiveMusicBass * _ReactiveBassExpansion);
        float frontCore = SoftLine(frontDistance, expandedThickness);
        float frontPressure = 1.0 - smoothstep(
            expandedThickness,
            expandedThickness * _ReactiveFrontPressureWidth,
            frontDistance);
        float frontResponse = saturate(
            _ReactiveMusicEnergy * _ReactiveFrontEnergyResponse
            + _ReactiveMusicBass * _ReactiveFrontBassResponse
            + _ReactiveBassPulse * _ReactiveFrontPulseResponse);
        float frontMask = (frontCore + frontPressure * _ReactiveFrontPressureOpacity)
            * frontWindow
            * frontResponse
            * screenOnly;
        color += _ReactiveFrontColor.rgb * (frontMask * _ReactiveFrontIntensity);

        // Midrange energy grows two interwoven melodic currents high over the pressure front.
        float seamlessAuroraFrequency = max(1.0, round(_ReactiveAuroraFrequency));
        float auroraPhase = azimuth * seamlessAuroraFrequency
            + reactiveTime * _ReactiveAuroraTravelSpeed;
        float auroraNoise = Noise3(float3(
            skyDirection.x * 3.5,
            skyUp * 4.0 + reactiveTime * 0.045,
            skyDirection.z * 3.5));
        float auroraWave = sin(auroraPhase + auroraNoise * 2.2) * _ReactiveAuroraWaviness;
        float auroraTarget = _ReactiveAuroraAltitude + auroraWave;
        float auroraPrimary = SoftLine(abs(skyUp - auroraTarget), _ReactiveAuroraThickness);
        float auroraSecondaryTarget = _ReactiveAuroraAltitude
            + sin(-auroraPhase + 1.8) * (_ReactiveAuroraWaviness * 0.62)
            + _ReactiveAuroraThickness * 2.4;
        float auroraSecondary = SoftLine(
            abs(skyUp - auroraSecondaryTarget),
            _ReactiveAuroraThickness * 0.62);
        float auroraShimmer = (1.0 - _ReactiveAuroraShimmerAmount)
            + _ReactiveAuroraShimmerAmount * sin(
            normalizedAzimuth * 37.0
            - reactiveTime * (_ReactiveAuroraTravelSpeed * 5.0));
        float auroraMask = (auroraPrimary + auroraSecondary * _ReactiveAuroraSecondaryIntensity)
            * saturate(auroraShimmer)
            * _ReactiveMusicMids
            * screenOnly;
        color += _ReactiveAuroraColor.rgb * (auroraMask * _ReactiveAuroraIntensity);

        // Treble transients reveal a short-lived, retargeting celestial filament.
        float lightningTick = floor(reactiveTime * _ReactiveLightningRetargetRate);
        float strikeAzimuth = Hash31(float3(lightningTick, 4.17, 9.31));
        strikeAzimuth = floor(strikeAzimuth * _ReactiveLightningSectorCount + 0.5)
            / max(_ReactiveLightningSectorCount, 1.0);
        float lightningVertical = smoothstep(0.02, 0.12, skyUp)
            * (1.0 - smoothstep(0.68, 0.9, skyUp));
        float lightningNoise = Noise3(float3(
            skyUp * 27.0,
            lightningTick * 0.173,
            floor(skyUp * 18.0)));
        float lightningOffset = (lightningNoise - 0.5)
            * _ReactiveLightningJaggedness
            * 0.18;
        float wrappedStrikeDistance = abs(frac(
            normalizedAzimuth - strikeAzimuth - lightningOffset + 0.5) - 0.5);
        float lightning = SoftLine(wrappedStrikeDistance, _ReactiveLightningWidth);
        float branchGate = smoothstep(0.18, 0.42, skyUp)
            * (1.0 - smoothstep(0.54, 0.7, skyUp));
        float branchOffset = (skyUp - 0.38) * _ReactiveLightningJaggedness * 0.28;
        float branchDistance = abs(frac(
            normalizedAzimuth - strikeAzimuth - lightningOffset - branchOffset + 0.5) - 0.5);
        float branch = SoftLine(branchDistance, _ReactiveLightningWidth * 0.62) * branchGate;
        float lightningMask = (lightning + branch * _ReactiveLightningBranchIntensity)
            * lightningVertical
            * saturate(_ReactiveHighPulse + _ReactiveMusicHighs * _ReactiveLightningSustainResponse)
            * screenOnly;
        color += _ReactiveLightningColor.rgb * (lightningMask * _ReactiveLightningIntensity);
        return color * _SkyIntensity;
    }

    float4 RenderSky(Varyings input)
    {
        return float4(RenderY2KSky(GetSkyViewDirWS(input.positionCS.xy)), 1.0);
    }

    float4 FragBaking(Varyings input) : SV_Target
    {
        return RenderSky(input);
    }

    float4 FragRender(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float4 color = RenderSky(input);
        color.rgb *= GetCurrentExposureMultiplier();
        return color;
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment FragBaking
            ENDHLSL
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment FragRender
            ENDHLSL
        }
    }

    Fallback Off
}
