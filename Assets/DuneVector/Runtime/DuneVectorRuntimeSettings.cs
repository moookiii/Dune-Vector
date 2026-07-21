using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public enum DuneGenerationPreset
    {
        ClassicDesert,
        GentleCinematic,
        GrandErg,
        SharpRidges,
        WindCarved,
        RoundedWindDunes,
        WindRibbonDunes,
        GrandWindSwells,
        RollingSandSea,
        FineRipples,
        ExtremeDunes,
    }

    public enum CloudArrangementPreset
    {
        BalancedDesertSky,
        SparseCinematic,
        MonumentalBanks,
        DevelopingColumns,
        HighWisps,
    }

    [System.Serializable]
    public sealed class GeoglyphArtworkPlacement
    {
        [Tooltip("Unique black-and-white artwork mask. White pixels become linework; black pixels remain transparent.")]
        public Texture2D Mask;

        [Tooltip("Authoritative center in the desert's persistent logical X/Z world coordinates.")]
        public Vector2 WorldCenter;

        [Tooltip("Width and length of the artwork footprint in world metres.")]
        public Vector2 WorldSize = new Vector2(640f, 426.67f);

        [Tooltip("Rotation of the artwork footprint around its world-space center.")]
        public float RotationDegrees;

        [Range(0f, 1f)]
        [Tooltip("Opacity of the artwork linework over the lit sand surface.")]
        public float BlendStrength = 0.9f;

        [ColorUsage(false)]
        [Tooltip("Ground pigment shown wherever the mask contains linework.")]
        public Color LineColor = new Color(0.12f, 0.055f, 0.018f, 1f);

        [Header("Mask Definition")]
        [Range(0f, 1f)] public float MaskThreshold = 0.48f;
        [Range(0.0001f, 0.25f)] public float EdgeSoftness = 0.025f;

        [Header("Optional Slope Correction")]
        [Range(0f, 1f)]
        [Tooltip("Blends toward slope-corrected sampling only on sufficiently steep terrain. Zero preserves pure overhead X/Z projection.")]
        public float SlopeCorrectionStrength = 0.16f;

        [Range(0f, 89f)]
        [Tooltip("Terrain slope angle where correction begins to blend in.")]
        public float SlopeCorrectionStartAngle = 42f;

        [Min(0f)]
        [Tooltip("Maximum world-space sampling offset allowed on steep dune faces.")]
        public float MaximumSlopeCorrection = 3f;

        [Tooltip("World height used as the gentle slope-projection reference plane.")]
        public float SlopeReferenceHeight;
    }

    [System.Serializable]
    public sealed class GeoglyphSystemTuning
    {
        public bool Enabled = true;

        [Tooltip("Unique persistent geoglyph landmarks. Entries are never spawned, tiled, randomized, or repeated by chunks.")]
        public List<GeoglyphArtworkPlacement> Placements = new List<GeoglyphArtworkPlacement>();

        public void EnsureInitialized()
        {
            Placements ??= new List<GeoglyphArtworkPlacement>();
        }
    }

    [System.Serializable]
    public sealed class CloudLobeTuning
    {
        public Vector3 Position;
        public Vector3 Scale;
        public Vector3 Rotation;
    }

    [System.Serializable]
    public sealed class CloudArchetypeTuning
    {
        public string DisplayName;
        public Vector2 AltitudeOffsetRange;
        public Vector3 MinimumScale;
        public Vector3 MaximumScale;
        public Vector2 YawRange;
        [Min(0f)] public float PitchRollVariation;
        public CloudLobeTuning[] SunlitLobes;
        public CloudLobeTuning[] UnderbellyLobes;
    }

    [System.Serializable]
    public sealed class CloudArrangementTuning
    {
        public string DisplayName;
        [Min(0f)] public float ClusterCountMultiplier;
        [Range(1, 8)] public int CompositionRegionSizeInChunks;
        [Range(0f, 1f)] public float NegativeSpaceRegionChance;
        [Min(0f)] public float NegativeSpaceDensityMultiplier;
        [Min(0f)] public float CloudRegionDensityMultiplier;
        public float AltitudeOffset;
        public Vector3 ScaleMultiplier;

        [Header("Archetype Mix")]
        [Min(0f)] public float LongStretchedWeight;
        [Min(0f)] public float CompactPuffyWeight;
        [Min(0f)] public float WideLayeredBankWeight;
        [Min(0f)] public float TallDevelopingWeight;
        [Min(0f)] public float SmallDistantWispyWeight;

        public float GetArchetypeWeight(int archetypeIndex)
        {
            return archetypeIndex switch
            {
                0 => LongStretchedWeight,
                1 => CompactPuffyWeight,
                2 => WideLayeredBankWeight,
                3 => TallDevelopingWeight,
                4 => SmallDistantWispyWeight,
                _ => 0f,
            };
        }
    }

    [System.Serializable]
    public sealed class CloudTuning
    {
        public bool Enabled;
        [Tooltip("Approximate total cluster count across the preloaded chunk area.")]
        [Range(4, 30)] public int ClusterCount;
        [Min(20f)] public float Altitude;
        [Min(0f)] public float DriftSpeed;
        [Tooltip("Cloud drift direction on the world X/Z plane. Set both components to zero to stop cloud drift.")]
        public Vector2 DriftDirection;
        [Header("Weather Wind Response")]
        [Tooltip("Additional cloud drift speed contributed by each metre per second of the live desert-weather wind.")]
        [Min(0f)] public float WeatherWindSpeedMultiplier;
        public int RandomSeedOffset;

        [Header("Arrangement Presets")]
        [Tooltip("Selects the authored density, archetype mix, altitude, and scale arrangement used when the world starts.")]
        public CloudArrangementPreset ActiveArrangementPreset;
        public CloudArrangementTuning BalancedDesertSky;
        public CloudArrangementTuning SparseCinematic;
        public CloudArrangementTuning MonumentalBanks;
        public CloudArrangementTuning DevelopingColumns;
        public CloudArrangementTuning HighWisps;

        [Header("Shared Placement")]
        [Min(0f)] public float PlacementInset;
        [Min(0f)] public float MinimumLocalSeparation;
        [Range(1, 16)] public int PlacementAttempts;

        [Header("Appearance")]
        [ColorUsage(false)] public Color SunlitColor;
        [ColorUsage(false)] public Color UnderbellyColor;
        [Range(0f, 1f)] public float MaterialSmoothness;
        [Range(0f, 1f)] public float MaterialMetallic;
        [Range(0, 2)] public int FacetSubdivisions;
        [Range(0.0001f, 0.1f)] public float CullScreenRelativeHeight;

        [Header("Silhouette Roundness")]
        [Tooltip("Blends each cloud cluster's horizontal X/Z scale toward an even oval footprint.")]
        [Range(0f, 1f)] public float ClusterHorizontalRoundness = 0.65f;
        [Tooltip("Expands the narrower horizontal axis of each cloud lobe to prevent thin sausage silhouettes.")]
        [Range(0f, 1f)] public float LobeHorizontalRoundness = 0.72f;
        [Tooltip("Offsets lobes through the cloud's depth so side views remain broad instead of collapsing into a line.")]
        [Range(0f, 0.75f)] public float LobeDepthSpread = 0.28f;

        [Header("Authored Archetype Kit")]
        public CloudArchetypeTuning LongStretched;
        public CloudArchetypeTuning CompactPuffy;
        public CloudArchetypeTuning WideLayeredBank;
        public CloudArchetypeTuning TallDeveloping;
        public CloudArchetypeTuning SmallDistantWispy;

        public void EnsureInitialized()
        {
            BalancedDesertSky ??= new CloudArrangementTuning();
            SparseCinematic ??= new CloudArrangementTuning();
            MonumentalBanks ??= new CloudArrangementTuning();
            DevelopingColumns ??= new CloudArrangementTuning();
            HighWisps ??= new CloudArrangementTuning();
            LongStretched ??= new CloudArchetypeTuning();
            CompactPuffy ??= new CloudArchetypeTuning();
            WideLayeredBank ??= new CloudArchetypeTuning();
            TallDeveloping ??= new CloudArchetypeTuning();
            SmallDistantWispy ??= new CloudArchetypeTuning();
        }

        public CloudArrangementTuning GetActiveArrangement()
        {
            return ActiveArrangementPreset switch
            {
                CloudArrangementPreset.SparseCinematic => SparseCinematic,
                CloudArrangementPreset.MonumentalBanks => MonumentalBanks,
                CloudArrangementPreset.DevelopingColumns => DevelopingColumns,
                CloudArrangementPreset.HighWisps => HighWisps,
                _ => BalancedDesertSky,
            };
        }

        public CloudArchetypeTuning[] GetArchetypes()
        {
            return new[]
            {
                LongStretched,
                CompactPuffy,
                WideLayeredBank,
                TallDeveloping,
                SmallDistantWispy,
            };
        }
    }

    [System.Serializable]
    public sealed class DeliveryTuning
    {
        private const string ObjectiveHexagonResourcePath = "UI/ObjectiveIndicatorDoubleHexagon";
        private const string ObjectiveArrowResourcePath = "UI/ObjectiveIndicatorArrow";

        public bool Enabled = true;
        public bool RandomizeLocationsEachPlay = true;
        public int JobSeedOffset;
        [Min(20f)] public float MinimumPickupDistance = 75f;
        [Min(20f)] public float MaximumPickupDistance = 145f;
        [Min(20f)] public float MinimumDeliveryDistance = 110f;
        [Min(20f)] public float MaximumDeliveryDistance = 210f;
        [Tooltip("Radius used by pickup objective rings.")]
        [Min(1f)] public float ObjectiveRingRadius = 3.2f;
        [Tooltip("Radius used by delivery objective rings.")]
        [Min(1f)] public float DeliveryRingRadius = 15f;
        [Tooltip("Height of pickup rings above the sampled terrain surface, in meters.")]
        [Min(0f)] public float PickupRingHeight = 1f;
        [Tooltip("Height of delivery rings above the sampled terrain surface, in meters.")]
        [Min(0f)] public float ObjectiveRingHeight = 3.4f;
        [Min(0.1f)] public float PackageScale = 0.8f;

        [Header("Package Drop")]
        [Min(0.01f)] public float PackageDropMass = 1f;
        [Tooltip("How much of the drone's velocity the package keeps when released. Set to 0 for an initial velocity of (0, 0, 0), or 1 to preserve all of it.")]
        [InspectorName("Drone Velocity Preserved")]
        [Range(0f, 1f)] public float PackageDropInheritedVelocityMultiplier = 1f;
        public Vector3 PackageDropAngularVelocity = new Vector3(0.7f, 1.5f, 0.4f);
        public Vector3 PackageDropColliderSize = new Vector3(1.2f, 0.82f, 1f);
        [Min(0f)] public float PackageDropGroundContactOffset = 0.03f;

        [Header("Objective Ring Appearance")]
        [ColorUsage(false, true)] public Color PickupRingBaseColor = new Color(0.32f, 0.015f, 0.48f);
        [ColorUsage(false, true)] public Color PickupRingEmissionColor = new Color(2.8f, 0.05f, 4.2f);
        [ColorUsage(false, true)] public Color DeliveryRingBaseColor = new Color(0.015f, 0.42f, 0.12f);
        [ColorUsage(false, true)] public Color DeliveryRingEmissionColor = new Color(0.05f, 3.8f, 0.45f);

        [Header("Objective Indicator HUD")]
        [Min(240f)] public float ObjectiveIndicatorReferenceHeight = 1080f;
        [Range(0.25f, 2f)] public float ObjectiveIndicatorMinimumScale = 0.65f;
        [Range(0.25f, 2f)] public float ObjectiveIndicatorMaximumScale = 1.25f;
        public Texture2D ObjectiveIndicatorHexagonIcon;
        public Texture2D ObjectiveIndicatorArrowIcon;
        [Min(8f)] public float ObjectiveIndicatorHexagonRadius = 27f;
        [Min(4f)] public float ObjectiveIndicatorArrowLength = 22f;
        [Min(4f)] public float ObjectiveIndicatorArrowWidth = 21f;
        [Min(0f)] public float ObjectiveIndicatorArrowGap = 4f;
        [Min(0f)] public float ObjectiveIndicatorTextGap = 13f;
        [Min(12f)] public float ObjectiveIndicatorTextWidth = 300f;
        [Min(12f)] public float ObjectiveIndicatorTextHeight = 44f;
        [Min(8)] public int ObjectiveIndicatorFontSize = 30;
        [Min(0f)] public float ObjectiveIndicatorEdgePadding = 18f;
        [Min(0f)] public float ObjectiveIndicatorViewportHysteresis = 18f;
        [Min(0f)] public float ObjectiveIndicatorPositionSharpness = 14f;
        [Min(0f)] public float ObjectiveIndicatorTransitionSharpness = 18f;
        [Tooltip("Number of icon-only flashes when a pickup or delivery objective begins.")]
        [Min(0)] public int ObjectiveIndicatorStartFlashCount = 3;
        [Tooltip("Seconds the objective icon remains visible during each start flash.")]
        [Min(0.01f)] public float ObjectiveIndicatorStartFlashOnDuration = 0.15f;
        [Tooltip("Seconds the objective icon remains hidden between start flashes.")]
        [Min(0.01f)] public float ObjectiveIndicatorStartFlashOffDuration = 0.12f;
        public Vector2 ObjectiveIndicatorShadowOffset = new Vector2(2f, 3f);
        [ColorUsage(false)] public Color ObjectiveIndicatorColor = new Color(0.96f, 0.98f, 1f, 1f);
        [ColorUsage(false)] public Color ObjectiveIndicatorShadowColor = new Color(0f, 0f, 0f, 0.72f);

        [Header("Completion Message")]
        [ColorUsage(false)] public Color CompletionTextRed = new Color(1f, 0.55f, 0.68f);
        [ColorUsage(false)] public Color CompletionTextGreen = new Color(0.55f, 1f, 0.72f);
        [ColorUsage(false)] public Color CompletionTextBlue = new Color(0.55f, 0.78f, 1f);
        [Min(0f)] public float CompletionTextColorCyclesPerSecond = 0.45f;

        public void EnsureInitialized()
        {
            ObjectiveIndicatorHexagonIcon ??= Resources.Load<Texture2D>(ObjectiveHexagonResourcePath);
            ObjectiveIndicatorArrowIcon ??= Resources.Load<Texture2D>(ObjectiveArrowResourcePath);
        }
    }

    [System.Serializable]
    public sealed class LandmarkContractLocation
    {
        public DuneLandmarkType Type;
        public string DisplayName;
    }

    [System.Serializable]
    public sealed class CourierContractTuning
    {
        [Header("Debug")]
        [Tooltip("Immediately completes accepted contracts at the courier hub without awarding gold.")]
        public bool DebugCompleteContractsInstantlyWithoutPayout;

        [Header("Contract Board")]
        public bool Enabled = true;
        [Range(5, 8)] public int OfferedContractCount = 6;
        public int ContractSeedOffset = 18431;
        [Min(1)] public int DualModifierUnlockDeliveries = 50;
        [Min(1)] public int TripleModifierUnlockDeliveries = 150;
        [Range(0f, 1f)] public float DualModifierChance = 0.42f;
        [Range(0f, 1f)] public float TripleModifierChance = 0.09f;
        [Range(0f, 1f)] public float UnknownContractChance = 0.14f;
        [Min(10f)] public float MinimumRouteDistance = 650f;
        [Min(10f)] public float MaximumRouteDistance = 2600f;
        [Min(10f)] public float MinimumPickupInsertionDistance = 75f;
        [Min(10f)] public float MaximumPickupInsertionDistance = 135f;
        [Min(10f)] public float MinimumRouteOriginDistance = 320f;
        [Min(10f)] public float MaximumRouteOriginDistance = 620f;
        [Min(0)] public int MinimumBaseReward = 260;
        [Min(0)] public int MaximumBaseReward = 1300;
        [Min(0f)] public float DistanceRewardPerMeter = 0.32f;
        [Min(1f)] public float UnknownRewardMultiplier = 1.6f;
        [Min(1f)] public float DualModifierRewardMultiplier = 1.75f;
        [Min(1f)] public float TripleModifierRewardMultiplier = 2.4f;
        [Min(0f)] public float ContractRefreshSeconds = 240f;
        [Tooltip("Designer-authored contract-board location label for each landmark type.")]
        public LandmarkContractLocation[] LandmarkLocations;
        [Tooltip("Landmark archetypes eligible for pickup and delivery contract objectives.")]
        public DuneLandmarkType[] ContractLandmarkTypes;

        [Header("Risk Scaling")]
        [Range(1, 100)] public int MaximumRisk = 20;
        [Min(0f)] public float RiskRewardMultiplierPerTier = 0.12f;
        [Min(1f)] public float RiskEnemyMultiplierAtRankOne = 1.1f;
        [Min(1f)] public float RiskEnemyMultiplierAtMaximumRank = 3f;
        [Min(1)] public int RiskGroundEnemyReferenceCount = 8;

        [Header("Risk Sand Ambusher")]
        [Min(1)] public int SandAmbusherMinimumRisk = 2;
        [Min(0f)] public float SandAmbusherInitialDelay = 2f;
        [Min(0.1f)] public float SandAmbusherBaseInterval = 2.4f;
        [Min(0f)] public float SandAmbusherIntervalReductionPerRisk = 0.55f;
        [Min(0.1f)] public float SandAmbusherMinimumInterval = 0.55f;
        [Min(0f)] public float SandAmbusherMinimumTargetOffset = 0f;
        [Tooltip("Random horizontal offset around the predicted player position.")]
        [Min(0f)] public float SandAmbusherMaximumTargetOffset = 4f;
        [Min(0f)] public float SandAmbusherTargetPredictionTime = 1.7f;
        [Tooltip("Minimum angle above the horizon for a Sand Ambusher aimed at a grounded drone.")]
        [Range(0f, 90f)] public float SandAmbusherGroundedMinimumAttackAngle = 65f;
        [Min(0f)] public float SandAmbusherWarningDuration = 1.15f;
        [Min(0.1f)] public float SandAmbusherBuriedDepth = 8f;
        [Min(0.1f)] public float SandAmbusherAttackSpeed = 48f;
        [Min(0f)] public float SandAmbusherAttackOvershoot = 5f;
        [Min(0.1f)] public float SandAmbusherMaximumAttackDuration = 3f;
        [Min(0.1f)] public float SandAmbusherRetreatSpeed = 32f;
        [Min(0f)] public float SandAmbusherBaseDamage = 18f;
        [Min(0f)] public float SandAmbusherDamagePerRisk = 5f;
        public string SandAmbusherDeathMessage = "Dragged beneath the dunes by a sand ambusher.";
        [Min(0.1f)] public float SandAmbusherCollisionRadius = 2.2f;
        [Min(0.1f)] public float SandAmbusherPlayerCollisionRadius = 3f;
        [Min(0.1f)] public float SandAmbusherHealth = 55f;
        [Range(1, 60)] public int SandAmbusherMaximumActive = 60;

        [Header("Risk Sand Ambusher Creature Visual")]
        public int SandAmbusherVisualSeed = 9317;
        [Range(3, 10)] public int SandAmbusherVisualSegmentCount = 6;
        [Min(0.1f)] public float SandAmbusherSegmentSpacing = 3.8f;
        [Min(0.1f)] public float SandAmbusherUpperSegmentRadius = 1.8f;
        [Min(0.1f)] public float SandAmbusherLowerSegmentRadius = 3.2f;
        [Min(0.1f)] public float SandAmbusherUpperSegmentHeight = 3.2f;
        [Min(0.1f)] public float SandAmbusherLowerSegmentHeight = 4.4f;
        [Range(0f, 0.5f)] public float SandAmbusherSegmentScaleVariation = 0.14f;
        [Range(0f, 45f)] public float SandAmbusherSegmentRotationVariation = 12f;
        [Range(3, 12)] public int SandAmbusherArmorMeshRings = 6;
        [Range(5, 18)] public int SandAmbusherArmorMeshRadialSegments = 10;
        [Range(0f, 0.8f)] public float SandAmbusherArmorIrregularity = 0.22f;
        [Min(0.1f)] public float SandAmbusherJointScale = 1f;
        [Min(0.1f)] public float SandAmbusherJointCompressedScale = 0.58f;
        [Min(0.01f)] public float SandAmbusherJointLengthMultiplier = 0.25f;
        [Range(0f, 1f)] public float SandAmbusherSegmentCompressedSpacing = 0.34f;
        [Min(0f)] public float SandAmbusherSegmentEmergenceDelay = 0.06f;
        [Min(0.01f)] public float SandAmbusherSegmentExtensionDuration = 0.38f;
        [Range(0.1f, 1f)] public float SandAmbusherSegmentEmergenceScale = 0.62f;
        [Min(0f)] public float SandAmbusherFullSwayBlendDuration = 0.9f;
        [Min(0f)] public float SandAmbusherExposedDuration = 2.2f;
        [Min(0f)] public float SandAmbusherIdleSwayAmplitude = 0.42f;
        [Min(0f)] public float SandAmbusherIdleSwayFrequency = 1.15f;
        [Min(0f)] public float SandAmbusherCrossSwayAmplitude = 0.22f;
        [Min(0f)] public float SandAmbusherCrossSwayFrequencyMultiplier = 1.37f;
        [Min(0f)] public float SandAmbusherSwayPhasePerSegment = 0.55f;
        [Range(0f, 1f)] public float SandAmbusherTailSwayFalloff = 0.45f;
        [Min(0f)] public float SandAmbusherSwayRotationMultiplier = 5f;
        [Range(0, 6)] public int SandAmbusherRidgesPerSegment = 3;
        [Range(0f, 2f)] public float SandAmbusherRidgeRadialOffset = 0.78f;
        [Min(0.01f)] public float SandAmbusherRidgeWidth = 0.2f;
        [Min(0.01f)] public float SandAmbusherRidgeHeight = 0.62f;
        [Min(0.01f)] public float SandAmbusherRidgeDepth = 0.12f;
        public float SandAmbusherRidgeTilt = 18f;
        [Min(0f)] public float SandAmbusherRidgeVerticalOffset = 0.25f;
        [Min(0f)] public float SandAmbusherRidgeAngularVariation = 18f;
        [Range(0f, 1f)] public float SandAmbusherMissingRidgeChance = 0.2f;
        [Min(0.1f)] public float SandAmbusherCreaseSandRadius = 0.74f;
        [Min(0.01f)] public float SandAmbusherCreaseSandThickness = 0.055f;
        [Range(8, 48)] public int SandAmbusherCreaseSandMajorSegments = 24;
        [Range(3, 12)] public int SandAmbusherCreaseSandTubeSegments = 6;
        public float SandAmbusherCreaseSandVerticalPosition = 0.34f;
        public float SandAmbusherCreaseSandTilt = 7f;
        [Min(0f)] public float SandAmbusherCrownBaseHeight = 0.38f;
        [Min(0.01f)] public float SandAmbusherCrownCoreWidth = 0.7f;
        [Min(0.01f)] public float SandAmbusherCrownCoreHeight = 0.55f;
        [Min(0.01f)] public float SandAmbusherCrownCoreDepth = 0.7f;
        public float SandAmbusherCrownCoreTilt = -8f;
        [Min(0f)] public float SandAmbusherCrownProngBaseSeparation = 0.2f;
        [Min(0f)] public float SandAmbusherCrownProngSpread = 1.6f;
        [Min(0.1f)] public float SandAmbusherCrownProngHeight = 1.9f;
        [Min(0f)] public float SandAmbusherCrownProngDepthCurve = 0.16f;
        [Min(0.01f)] public float SandAmbusherCrownProngBaseRadius = 0.23f;
        [Min(0.01f)] public float SandAmbusherCrownProngTipRadius = 0.05f;
        [Min(0.1f)] public float SandAmbusherCrownProngTaperPower = 0.7f;
        [Range(3, 16)] public int SandAmbusherCrownProngPathSegments = 7;
        [Range(5, 16)] public int SandAmbusherCrownProngRadialSegments = 7;
        [Min(0f)] public float SandAmbusherProngMotionDegrees = 8f;
        [Min(0f)] public float SandAmbusherProngMotionFrequency = 0.85f;
        [Min(0f)] public float SandAmbusherProngMotionAsymmetry = 1.22f;
        public Color SandAmbusherArmorColor = new Color(0.18f, 0.095f, 0.045f, 1f);
        public Color SandAmbusherArmorEmission = new Color(0.08f, 0.018f, 0.004f, 1f);
        [Range(0f, 1f)] public float SandAmbusherArmorSmoothness = 0.24f;
        [Range(0f, 1f)] public float SandAmbusherArmorMetallic = 0.16f;
        public Color SandAmbusherUndersideColor = new Color(0.045f, 0.028f, 0.022f, 1f);
        [Range(0f, 1f)] public float SandAmbusherUndersideSmoothness = 0.12f;
        [Range(0f, 1f)] public float SandAmbusherUndersideMetallic = 0.05f;
        public Color SandAmbusherRidgeColor = new Color(0.42f, 0.22f, 0.08f, 1f);
        public Color SandAmbusherRidgeEmission = new Color(0.16f, 0.035f, 0.004f, 1f);
        [Range(0f, 1f)] public float SandAmbusherRidgeSmoothness = 0.31f;
        [Range(0f, 1f)] public float SandAmbusherRidgeMetallic = 0.22f;
        public Color SandAmbusherCreaseSandColor = new Color(0.68f, 0.38f, 0.16f, 1f);
        [Range(0f, 1f)] public float SandAmbusherCreaseSandSmoothness = 0.08f;

        [Header("Risk Sand Ambusher Fracture")]
        [ColorUsage(true, true)] public Color SandAmbusherFractureColor = new Color(12f, 14f, 16f, 1f);
        [Tooltip("Overall multiplier for the Sand Ambusher fracture's planar lengths, jitter, and widths.")]
        [Min(0.01f)] public float SandAmbusherFractureOverallScale = 1f;
        [Tooltip("World-space center angle of the allowed fracture direction cone. Zero points along world +X.")]
        [Range(-180f, 180f)] public float SandAmbusherFractureRotation = 90f;
        [Tooltip("Angular size of the fracture direction cone. Zero fixes the direction; 360 allows any rotation.")]
        [Range(0f, 360f)] public float SandAmbusherFractureAllowedRotation = 360f;
        [Min(1f)] public float SandAmbusherFractureMainLength = 38f;
        [Range(3, 48)] public int SandAmbusherFractureMainPointCount = 22;
        [Min(0f)] public float SandAmbusherFractureMainJitter = 2.2f;
        [Min(0.05f)] public float SandAmbusherFractureMainWidth = 1.8f;
        [Range(0, 20)] public int SandAmbusherFractureBranchCount = 10;
        [Min(0.1f)] public float SandAmbusherFractureBranchMinimumLength = 5f;
        [Min(0.1f)] public float SandAmbusherFractureBranchMaximumLength = 15f;
        [Range(2, 24)] public int SandAmbusherFractureBranchPointCount = 8;
        [Min(0f)] public float SandAmbusherFractureBranchJitter = 1.25f;
        [Min(0.01f)] public float SandAmbusherFractureBranchMinimumWidth = 0.38f;
        [Min(0.01f)] public float SandAmbusherFractureBranchMaximumWidth = 0.85f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMinimumOrigin = 0.14f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMaximumOrigin = 0.86f;
        [Range(0f, 2f)] public float SandAmbusherFractureBranchForwardBias = 0.65f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMinimumDelay = 0.18f;
        [Range(0f, 1f)] public float SandAmbusherFractureBranchMaximumDelay = 0.58f;
        [Range(0.01f, 1f)] public float SandAmbusherFractureBranchMinimumSpread = 0.18f;
        [Range(0.01f, 1f)] public float SandAmbusherFractureBranchMaximumSpread = 0.42f;
        [Range(0.01f, 1f)] public float SandAmbusherFracturePrimarySpreadFraction = 0.72f;
        [Range(0f, 1f)] public float SandAmbusherFractureJitterPersistence = 0.56f;
        [Min(0f)] public float SandAmbusherFractureSurfaceOffset = 0.08f;
        [Min(0f)] public float SandAmbusherFractureEdgeNoiseScale = 0.55f;
        [Range(0f, 1f)] public float SandAmbusherFractureEdgeNoiseStrength = 0.45f;
        [Range(0f, 1f)] public float SandAmbusherFractureInitialWidth = 0.06f;
        [Range(0f, 1f)] public float SandAmbusherFracturePreBurstWidth = 0.28f;
        [Min(0f)] public float SandAmbusherFractureMinimumIntensity = 1.4f;
        [Min(0f)] public float SandAmbusherFractureMaximumIntensity = 8f;
        [Min(0f)] public float SandAmbusherFractureBurstIntensity = 24f;
        [Min(0.1f)] public float SandAmbusherFractureIntensityPower = 2.4f;
        [Min(0f)] public float SandAmbusherFractureBurstHoldDuration = 0.09f;
        [Min(0.01f)] public float SandAmbusherFractureFadeDuration = 0.65f;
        public Color SandAmbusherDisturbedSandColor = new Color(0.32f, 0.16f, 0.065f, 0.68f);
        [Range(0f, 1f)] public float SandAmbusherDisturbedSandSmoothness = 0.05f;
        [Min(0.1f)] public float SandAmbusherDisturbedSandRadius = 11f;
        [Range(5, 64)] public int SandAmbusherDisturbedSandVertexCount = 24;
        [Range(0f, 0.8f)] public float SandAmbusherDisturbedSandIrregularity = 0.28f;
        [Min(0f)] public float SandAmbusherDisturbedSandSurfaceOffset = 0.035f;
        [Range(0f, 1f)] public float SandAmbusherDisturbedSandPreBurstAlphaMultiplier = 0.2f;
        [Min(0f)] public float SandAmbusherDisturbedSandHoldDuration = 2.5f;
        [Min(0.01f)] public float SandAmbusherDisturbedSandFadeDuration = 4.5f;

        [Header("Risk Sand Ambusher Sand VFX")]
        [Range(8, 256)] public int SandAmbusherParticleTextureResolution = 64;
        public Color SandAmbusherDustColor = new Color(0.78f, 0.49f, 0.25f, 0.55f);
        [Range(0f, 1f)] public float SandAmbusherParticleFadeInFraction = 0.06f;
        [Range(0f, 1f)] public float SandAmbusherParticleFadeOutFraction = 0.72f;
        [Range(0f, 1f)] public float SandAmbusherPreBreakStartFraction = 0.62f;
        [Min(0f)] public float SandAmbusherPreBreakDustEmissionRate = 35f;
        [Min(0f)] public float SandAmbusherPreBreakDebrisEmissionRate = 6f;
        [Range(1, 12)] public int SandAmbusherDirectionalBurstEmitterCount = 5;
        [Range(0, 256)] public int SandAmbusherDirectionalBurstParticleCount = 18;
        [Range(1, 512)] public int SandAmbusherDirectionalBurstMaximumParticles = 30;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMinimumLifetime = 0.5f;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMaximumLifetime = 1.8f;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMinimumSize = 0.18f;
        [Min(0.01f)] public float SandAmbusherDirectionalBurstMaximumSize = 0.75f;
        [Min(0f)] public float SandAmbusherDirectionalBurstMinimumSpeed = 8f;
        [Min(0f)] public float SandAmbusherDirectionalBurstMaximumSpeed = 18f;
        [Min(0f)] public float SandAmbusherDirectionalBurstGravity = 1.1f;
        [Range(0f, 90f)] public float SandAmbusherDirectionalBurstConeAngle = 18f;
        [Min(0f)] public float SandAmbusherDirectionalBurstEmitterRadius = 2f;
        [Min(0f)] public float SandAmbusherDirectionalBurstUpwardBias = 0.72f;
        [Min(0f)] public float SandAmbusherDirectionalBurstStretch = 2.5f;
        [Min(0f)] public float SandAmbusherDirectionalBurstVelocityScale = 0.15f;
        [Range(0, 512)] public int SandAmbusherDustBurstParticleCount = 100;
        [Range(1, 1024)] public int SandAmbusherDustMaximumParticles = 160;
        [Min(0.01f)] public float SandAmbusherDustMinimumLifetime = 1.5f;
        [Min(0.01f)] public float SandAmbusherDustMaximumLifetime = 4f;
        [Min(0.01f)] public float SandAmbusherDustMinimumSize = 3f;
        [Min(0.01f)] public float SandAmbusherDustMaximumSize = 8f;
        [Min(0f)] public float SandAmbusherDustMinimumSpeed = 1f;
        [Min(0f)] public float SandAmbusherDustMaximumSpeed = 4f;
        public float SandAmbusherDustGravity = 0f;
        [Min(0.01f)] public float SandAmbusherDustEmitterHeight = 0.3f;
        [Min(0.01f)] public float SandAmbusherDustEmitterWidth = 8f;
        [Min(0f)] public float SandAmbusherDustTurbulence = 2f;
        [Min(0f)] public float SandAmbusherDustTurbulenceFrequency = 0.4f;
        [Range(0, 256)] public int SandAmbusherDebrisParticleCount = 28;
        [Range(1, 512)] public int SandAmbusherDebrisMaximumParticles = 40;
        [Min(0.01f)] public float SandAmbusherDebrisMinimumLifetime = 1.2f;
        [Min(0.01f)] public float SandAmbusherDebrisMaximumLifetime = 3f;
        [Min(0.01f)] public float SandAmbusherDebrisMinimumSize = 0.3f;
        [Min(0.01f)] public float SandAmbusherDebrisMaximumSize = 0.9f;
        [Min(0f)] public float SandAmbusherDebrisMinimumSpeed = 7f;
        [Min(0f)] public float SandAmbusherDebrisMaximumSpeed = 16f;
        [Min(0f)] public float SandAmbusherDebrisGravity = 1.3f;
        [Range(0f, 90f)] public float SandAmbusherDebrisConeAngle = 34f;
        [Min(0f)] public float SandAmbusherDebrisEmitterRadius = 2.5f;
        [Range(3, 8)] public int SandAmbusherDebrisMeshRings = 4;
        [Range(5, 12)] public int SandAmbusherDebrisMeshRadialSegments = 6;
        [Range(0f, 0.8f)] public float SandAmbusherDebrisMeshIrregularity = 0.3f;
        [Range(1, 1024)] public int SandAmbusherTrickleMaximumParticles = 180;
        [Min(0f)] public float SandAmbusherTrickleEmissionRate = 32f;
        [Min(0f)] public float SandAmbusherTrickleDuration = 3f;
        [Min(0.01f)] public float SandAmbusherTrickleMinimumLifetime = 0.4f;
        [Min(0.01f)] public float SandAmbusherTrickleMaximumLifetime = 1.2f;
        [Min(0.01f)] public float SandAmbusherTrickleMinimumSize = 0.05f;
        [Min(0.01f)] public float SandAmbusherTrickleMaximumSize = 0.22f;
        [Min(0f)] public float SandAmbusherTrickleMinimumSpeed = 0.2f;
        [Min(0f)] public float SandAmbusherTrickleMaximumSpeed = 1f;
        [Min(0f)] public float SandAmbusherTrickleGravity = 1f;
        [Min(0f)] public float SandAmbusherTrickleStretch = 1.4f;
        [Min(0f)] public float SandAmbusherTrickleVelocityScale = 0.08f;

        [Header("Cargo Modifiers")]
        [Range(0f, 100f)] public float FragileFailureIntegrity = 18f;
        [Min(0f)] public float FragileCargoDamageMultiplier = 1.45f;
        [Min(0f)] public float StandardCargoDamageMultiplier = 0f;
        [Min(0f)] public float HazardousCargoDamageMultiplier = 0.7f;
        [Min(0f)] public float CargoHardImpactSpeed = 18f;
        [Min(0f)] public float CargoHardImpactDamagePerSpeed = 1.4f;
        [Min(0.1f)] public float ExpressExpectedSpeed = 32f;
        [Min(0f)] public float ExpressGraceSeconds = 18f;
        [Range(0.1f, 1f)] public float OversizedSpeedMultiplier = 0.72f;
        [Range(0.1f, 1f)] public float OversizedAccelerationMultiplier = 0.64f;
        [Range(0.1f, 1f)] public float OversizedTurningMultiplier = 0.62f;
        [Min(1f)] public float OversizedVisualScale = 1.75f;
        [Min(0f)] public float UnknownRevealDelay = 5f;
        [Range(0f, 100f)] public float HazardousWarningIntegrity = 72f;
        [Range(0f, 100f)] public float HazardousUnstableIntegrity = 45f;
        [Range(0f, 100f)] public float HazardousCriticalIntegrity = 22f;
        [Min(0.1f)] public float HazardousPulseInterval = 3.2f;
        [Min(0f)] public float HazardousPulseDamage = 6f;
        public string HazardousPulseDeathMessage = "Destroyed by a Hazardous Cargo pulse.";
        [Range(2, 5)] public int MultiDropMinimumStops = 2;
        [Range(2, 5)] public int MultiDropMaximumStops = 3;
        [Range(0f, 1f)] public float IntegrityRewardFloor = 0.25f;

        [Header("Contract Runtime")]
        [Min(0f)] public float CompletionReturnDelay = 3.5f;
        [Min(0f)] public float FailureReturnDelay = 3f;
        [Min(0.1f)] public float ObjectivePackageScale = 0.8f;
        public Vector3 CarriedPackageOffset = new Vector3(0f, -0.62f, -0.28f);
        public Vector3 OversizedPackageOffset = new Vector3(0f, -0.82f, -0.48f);
        [Min(0f)] public float PackageSpinSpeed = 28f;
        [Min(0f)] public float CargoWarningScale = 0.38f;
        [Min(0f)] public float CargoWarningPulseSpeed = 8f;

        [Header("Cargo Presentation")]
        [Min(0f)] public float CargoDamagePulseAmount = 0.07f;
        [Min(0f)] public float CargoWarningHeight = 0.62f;
        [Range(1, 8)] public int CargoWarningLightCount = 4;
        [Min(0f)] public float CargoWarningLightRadius = 0.52f;
        [Min(0f)] public float CargoWarningOrbitSpeed = 140f;
        [Range(0f, 100f)] public float CargoCriticalEffectsThreshold = 32f;
        [Min(0f)] public float CargoCriticalSparkRate = 12f;
        [Min(0f)] public float CargoCriticalSparkLifetime = 0.45f;
        [Min(0f)] public float CargoCriticalSparkSpeed = 1.4f;
        [Min(0f)] public float CargoCriticalSparkSize = 0.08f;

        [Header("Contract HUD")]
        [Min(160f)] public float HudWidth = 330f;
        [Min(60f)] public float HudHeight = 128f;
        [Min(0f)] public float HudLeft = 24f;
        [Min(0f)] public float HudTop = 24f;
        [Min(10)] public int HudTitleFontSize = 17;
        [Min(9)] public int HudBodyFontSize = 13;
        [Min(9)] public int HudStatusFontSize = 14;
        [Min(0f)] public float ObjectiveEdgePadding = 64f;
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.025f, 0.045f, 0.07f, 0.9f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(1f, 0.62f, 0.16f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.9f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color IntegrityHealthyColor = new Color(0.22f, 0.95f, 0.64f, 1f);
        [ColorUsage(false)] public Color IntegrityCriticalColor = new Color(1f, 0.16f, 0.08f, 1f);
    }

    [System.Serializable]
    public sealed class WorldHubTuning
    {
        public bool Enabled = true;
        [Min(0f)] public float PlatformHeightAboveTerrain = 24f;
        [Min(8f)] public float PlatformRadius = 26f;
        [Min(0.5f)] public float PlatformThickness = 2.4f;
        [Min(0f)] public float TerminalForwardOffset = 11f;
        [Min(1f)] public float TerminalInteractionRadius = 6f;
        [Min(0f)] public float ArchiveTerminalBackwardOffset = 11f;
        [Min(1f)] public float ArchiveTerminalInteractionRadius = 6f;
        [Min(0f)] public float FreeRoamTerminalLeftOffset = 11f;
        [Min(1f)] public float FreeRoamTerminalInteractionRadius = 6f;
        [Min(0f)] public float FreeRoamDeploymentDistance = 320f;
        public float FreeRoamDeploymentHeadingDegrees = 90f;
        [Min(0f)] public float UpgradeAreaSideOffset = 13f;
        [Min(0f)] public float PlayerSpawnHeight = 2.2f;
        public bool RestoreHealthOnReturn = true;

        [Header("Physical Terminals")]
        public Vector3 TerminalPedestalLocalPosition = new Vector3(0f, 2f, 0f);
        public Vector3 TerminalPedestalScale = new Vector3(3f, 4f, 2f);
        public Vector3 TerminalScreenLocalPosition = new Vector3(0f, 4.1f, -0.45f);
        public Vector3 TerminalScreenScale = new Vector3(4.4f, 2.4f, 0.25f);
        public float TerminalScreenTilt = -12f;
        public Vector3 TerminalHeaderLocalPosition = new Vector3(0f, 5.7f, 0f);
        public Vector3 TerminalHeaderScale = new Vector3(5.8f, 0.32f, 1.2f);
        [Min(0f)] public float TerminalSignalMastHorizontalOffset = 2.25f;
        public Vector3 TerminalSignalMastLocalPosition = new Vector3(0f, 7.4f, 0.2f);
        public Vector3 TerminalSignalMastScale = new Vector3(0.12f, 1.8f, 0.12f);
        public string ContractTerminalName = "CONTRACT TERMINAL";
        public string ArchiveTerminalName = "MESSAGE ARCHIVE";
        public string FreeRoamTerminalName = "FREE ROAM TERMINAL";
        public string FreeRoamTerminalNearbyPrompt = "PRESS E — DEPLOY TO FREE ROAM";
        public string TerminalNearbyPromptFormat = "PRESS E — OPEN {0}";
        public string TerminalDistancePromptFormat = "{0}  {1:0} m";

        [Header("Hub Containment")]
        public bool ContainmentEnabled = true;
        [Min(0.5f)] public float ContainmentWallHeight = 8f;
        [Min(0.1f)] public float ContainmentWallThickness = 0.8f;
        [Range(8, 64)] public int ContainmentWallSegments = 32;
        [Tooltip("Extra horizontal clearance kept between the drone capsule and the containment wall.")]
        [Min(0f)] public float ContainmentSafetyPadding = 0.15f;

        [Min(0f)] public float DesertInsertionHeight = 8f;
        [Min(0.1f)] public float TeleportBuildDuration = 1.15f;
        [Min(0.1f)] public float TeleportFadeDuration = 0.45f;
        [Min(0.1f)] public float TeleportRebuildDuration = 0.8f;
        [Min(0f)] public float StabilizeSharpness = 6f;
        [Min(0.1f)] public float TeleportEffectRadius = 4.5f;
        [Min(4)] public int TeleportParticleCount = 28;
        [Min(0f)] public float TeleportParticleSpinSpeed = 150f;
        [Min(0f)] public float TeleportParticleLiftSpeed = 6f;

        [Header("Hub Presentation")]
        [Range(8, 48)] public int PlatformEnergySegmentCount = 24;
        [Min(0f)] public float PlatformEnergyRingRadius = 19f;
        [Min(0f)] public float PlatformEnergySegmentLength = 4.2f;
        [Min(0f)] public float PlatformEnergySegmentWidth = 0.32f;
        [Min(0f)] public float PlatformEnergySegmentHeight = 0.12f;
        public float PlatformEnergyRotationSpeed = 7f;
        [Range(3, 12)] public int HubPylonCount = 6;
        [Min(0f)] public float HubPylonRadius = 22f;
        [Min(0f)] public float HubPylonHeight = 11f;
        [Min(0f)] public float HubPylonWidth = 1.1f;
        [Min(0f)] public float HubPylonLean = 16f;
        [Min(0f)] public float HubBeaconPulseSpeed = 2.8f;
        [Min(0f)] public float HubBeaconPulseAmount = 0.16f;
        [Range(1, 6)] public int UpgradePadArmCount = 3;
        [Min(0f)] public float UpgradePadArmLength = 7.5f;
        public float UpgradePadRotationSpeed = -11f;

        [Header("Teleport Presentation")]
        [Min(0f)] public float TeleportParticleMinimumSize = 0.05f;
        [Min(0f)] public float TeleportParticleMaximumSize = 0.16f;
        [Min(0f)] public float TeleportHelixHeight = 6f;
        [Min(0f)] public float TeleportConvergenceRadius = 0.25f;
        [Range(1, 6)] public int TeleportEnergyRingCount = 3;
        [Range(8, 32)] public int TeleportEnergyRingSegments = 16;
        [Min(0f)] public float TeleportEnergyRingSpacing = 1.7f;
        [Min(0f)] public float TeleportEnergyRingSegmentLength = 0.75f;
        [Min(0f)] public float TeleportEnergyRingThickness = 0.045f;
        public float TeleportEnergyRingRotationSpeed = 95f;

        [Header("Terminal UI")]
        [Min(480f)] public float TerminalReferenceWidth = 1600f;
        [Min(320f)] public float TerminalReferenceHeight = 900f;
        [Range(0.5f, 1.5f)] public float TerminalMinimumScale = 0.72f;
        [Range(0.5f, 1.5f)] public float TerminalMaximumScale = 1.08f;
        [Min(480f)] public float TerminalPanelWidth = 1120f;
        [Min(320f)] public float TerminalPanelHeight = 640f;
        [Min(0f)] public float TerminalScreenMargin = 28f;
        [Min(10f)] public float TerminalPadding = 28f;
        [Min(10f)] public float ContractCardGap = 12f;
        [Range(2, 4)] public int TerminalCardColumns = 3;
        [Range(3, 4)] public int TerminalExpandedGridColumns = 4;
        [Range(5, 8)] public int TerminalExpandedGridThreshold = 6;
        [Min(100f)] public float ContractCardHeight = 196f;
        [Min(10)] public int TerminalTitleFontSize = 30;
        [Min(9)] public int TerminalBodyFontSize = 13;
        [Min(9)] public int TerminalButtonFontSize = 13;
        [Min(9)] public int TerminalKickerFontSize = 11;
        [Min(9)] public int TerminalDestinationFontSize = 17;
        [Min(9)] public int TerminalRewardFontSize = 18;
        [Min(9)] public int TerminalMetaFontSize = 12;
        [Min(0f)] public float TerminalHeaderHeight = 122f;
        [Min(0f)] public float TerminalFooterHeight = 38f;
        [Min(0f)] public float TerminalAccentBarHeight = 4f;
        [Min(0f)] public float TerminalCardAccentWidth = 5f;
        [Min(0f)] public float TerminalContractOrderPipSize = 5f;
        [Min(0f)] public float TerminalContractOrderPipGap = 3f;
        [Range(1, 50)] public int TerminalRiskPipsPerRow = 10;
        [Min(0f)] public float TerminalRiskPipRowGap = 3f;
        [Min(0f)] public float TerminalPanelBorderThickness = 2f;
        public Vector2 TerminalPanelShadowOffset = new Vector2(12f, 14f);
        [Min(180f)] public float TerminalTooltipWidth = 360f;
        [Min(0f)] public float TerminalTooltipPadding = 14f;
        public Vector2 TerminalTooltipMouseOffset = new Vector2(18f, 20f);
        [Min(9)] public int TerminalTooltipTitleFontSize = 12;
        [Min(9)] public int TerminalTooltipBodyFontSize = 12;
        [Min(120f)] public float TerminalPromptWidth = 420f;
        [Min(16f)] public float TerminalPromptHeight = 32f;
        public float TerminalPromptVerticalOffset = -58f;
        [ColorUsage(false)] public Color HubMetalColor = new Color(0.055f, 0.09f, 0.12f, 1f);
        [ColorUsage(false, true)] public Color HubEnergyColor = new Color(0.02f, 2.2f, 3.8f, 1f);
        [ColorUsage(false)] public Color TerminalBackdropColor = new Color(0.006f, 0.012f, 0.022f, 0.9f);
        [ColorUsage(false)] public Color TerminalShadowColor = new Color(0f, 0f, 0f, 0.58f);
        [ColorUsage(false)] public Color TerminalBorderColor = new Color(0.18f, 0.3f, 0.38f, 0.9f);
        [ColorUsage(false)] public Color TerminalDividerColor = new Color(0.16f, 0.24f, 0.3f, 0.9f);
        [ColorUsage(false)] public Color TerminalPanelColor = new Color(0.018f, 0.035f, 0.055f, 0.98f);
        [ColorUsage(false)] public Color TerminalCardColor = new Color(0.045f, 0.072f, 0.095f, 1f);
        [ColorUsage(false)] public Color TerminalCardHoverColor = new Color(0.085f, 0.14f, 0.18f, 1f);
        [ColorUsage(false)] public Color TerminalAccentColor = new Color(1f, 0.58f, 0.12f, 1f);
        [ColorUsage(false)] public Color TerminalTextColor = new Color(0.9f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color TerminalMutedTextColor = new Color(0.55f, 0.65f, 0.72f, 1f);
        [ColorUsage(false)] public Color TerminalUnknownColor = new Color(0.72f, 0.38f, 1f, 1f);
        [ColorUsage(false)] public Color TerminalHighValueColor = new Color(1f, 0.73f, 0.16f, 1f);
        [ColorUsage(false)] public Color TerminalMultiDropColor = new Color(0.12f, 0.85f, 0.8f, 1f);
        [ColorUsage(false)] public Color TerminalDangerColor = new Color(1f, 0.28f, 0.18f, 1f);
    }

    [System.Serializable]
    public sealed class LandmarkSystemTuning
    {
        public bool Enabled = true;
        [Min(40f)] public float PlacementCellSize = 190f;
        [Range(1, 5)] public int ActiveCellRadius = 2;
        [Min(0.1f)] public float RefreshInterval = 0.55f;
        [Range(0f, 1f)] public float CommonCellChance = 0.42f;
        [Range(0f, 1f)] public float StandardCellChance = 0.22f;
        [Range(0f, 1f)] public float RareCellChance = 0.035f;
        [Tooltip("Chance that a cell proposes a region-defining landmark before other rarity tiers are evaluated.")]
        [Range(0f, 1f)] public float RegionDefiningCellChance;
        [Min(10f)] public float StandardMinimumSpacing = 310f;
        [Min(10f)] public float RareMinimumSpacing = 950f;
        [Min(0f)] public float SmallMediumLandmarkExclusionRadius;
        [Min(0f)] public float LargeLandmarkExclusionRadius;
        [Min(0f)] public float RegionDefiningExclusionRadius;
        [Tooltip("Large landmark archetypes selected for rare procedural cells.")]
        public DuneLandmarkType[] RareLandmarkTypes;
        [Tooltip("Mega landmark archetypes selected for region-defining procedural cells.")]
        public DuneLandmarkType[] RegionDefiningLandmarkTypes;
        [Min(0f)] public float HubExclusionRadius = 170f;
        [Range(0f, 50f)] public float MaximumPlacementSlope = 19f;
        [Min(0.1f)] public float RelayScale = 1f;
        [Min(0.1f)] public float CarrierScale = 1.15f;
        [Min(0.1f)] public float BeaconScale = 1f;
        [Min(0.1f)] public float SpireScale = 1.3f;
        [Min(0.1f)] public float ExcavationScale = 1.1f;
        [Min(4f)] public float RelayAntennaHeight = 42f;
        [Min(4f)] public float CarrierLength = 54f;
        [Min(4f)] public float BeaconHeight = 38f;
        [Min(8f)] public float SpireHeight = 96f;
        [Min(4f)] public float ExcavationCraneHeight = 34f;
        [Min(0f)] public float ContractSocketHeight = 5f;
        [Tooltip("Additional horizontal distance between a landmark and its pickup package and ring.")]
        [Min(0f)] public float PickupRingLandmarkClearance = 6f;
        [Tooltip("Vertical air gap between the landmark's highest rendered point and its delivery ring.")]
        [Min(0f)] public float DeliveryRingClearance = 8f;
        [Min(0f)] public float EncounterSocketHeight = 22f;
        [Min(0f)] public float FlightSocketHeight = 18f;

        [Header("Landmark Materials")]
        [ColorUsage(false)] public Color LandmarkStoneColor;
        [ColorUsage(false)] public Color LandmarkMetalColor;
        [ColorUsage(false)] public Color LandmarkSecondaryColor;
        [ColorUsage(false)] public Color LandmarkInteriorColor;
        [ColorUsage(false)] public Color LandmarkAccentColor;
        [ColorUsage(false, true)] public Color LandmarkAccentEmission;
        [Range(0f, 1f)] public float LandmarkStoneSmoothness;
        [Range(0f, 1f)] public float LandmarkMetalSmoothness;
        [Range(0f, 1f)] public float LandmarkMetallic;

        [Header("Landmark Contract Sockets")]
        [Tooltip("Objective socket offsets for the five large and region-defining landmark compositions.")]
        public Vector3 OrbitalContractSocketOffset;
        public Vector3 MegagateContractSocketOffset;
        public Vector3 HarvesterContractSocketOffset;
        public Vector3 ArcologyContractSocketOffset;
        public Vector3 SandRingContractSocketOffset;

        [Header("Landmark Presentation")]
        [Range(1, 6)] public int VisualVariantCount = 4;
        [Min(0f)] public float DishRotationSpeed = 9f;
        [Min(0f)] public float BeaconOrbitSpeed = 16f;
        [Min(0f)] public float BeaconPulseSpeed = 2.6f;
        [Min(0f)] public float BeaconPulseAmount = 0.14f;
        public float SpireRelicRotationSpeed = -18f;
        [Min(0f)] public float SpireRelicFloatAmplitude = 1.8f;
        [Min(0f)] public float SpireRelicFloatSpeed = 1.2f;
        [Range(2, 8)] public int SpireShardCount = 5;
        [Range(2, 8)] public int ExcavationWorkLightCount = 4;
        [Min(0f)] public float ExcavationWorkLightPulseSpeed = 3.2f;
        [Range(0.2f, 1f)] public float LandmarkRingSegmentFill = 0.72f;

        [Header("Relay Station Detail")]
        [Range(6, 24)] public int RelayDishRimSegments = 12;
        [Range(2, 9)] public int RelayWindowCount = 5;
        [Min(0f)] public float RelayWindowSpacing = 1.65f;
        [Min(0.05f)] public float RelayWindowSize = 0.58f;
        [Range(3, 8)] public int RelayMastBraceCount = 4;
        [Min(0.1f)] public float RelayMastBraceRadius = 4.2f;
        [Min(0.1f)] public float RelayMastBraceHeight = 13f;
        [Min(0.05f)] public float RelayMastBraceThickness = 0.22f;

        [Header("Crashed Carrier Detail")]
        [Range(1, 6)] public int CarrierEngineCount = 3;
        [Min(0.1f)] public float CarrierEngineRadius = 1.85f;
        [Min(0.1f)] public float CarrierEngineDepth = 3.6f;
        [Range(3, 12)] public int CarrierHullRibCount = 7;
        [Min(0.05f)] public float CarrierHullRibThickness = 0.32f;
        [Range(2, 10)] public int CarrierWreckageCount = 5;
        [Min(0.1f)] public float CarrierCockpitScale = 1f;

        [Header("Raider Beacon Detail")]
        [Range(3, 8)] public int BeaconFoundationArmCount = 3;
        [Range(6, 24)] public int BeaconSignalRingSegments = 14;
        [Min(0.1f)] public float BeaconSignalRingRadius = 7.2f;
        [Min(0.05f)] public float BeaconSignalRingThickness = 0.28f;
        [Range(3, 12)] public int BeaconTowerFinCount = 6;

        [Header("Ancient Spire Detail")]
        [Range(5, 14)] public int SpireLayerCount = 9;
        [Min(0.02f)] public float SpireSeamHeight = 0.16f;
        [Range(3, 8)] public int SpireMonolithCount = 4;
        [Range(6, 24)] public int SpireBaseRingSegments = 12;
        [Min(1f)] public float SpireBaseRingRadius = 18f;
        [Min(0.05f)] public float SpireBaseRingThickness = 0.38f;

        [Header("Excavation Detail")]
        [Range(2, 8)] public int ExcavationScaffoldCount = 4;
        [Range(1, 5)] public int ExcavationPitTerraceCount = 3;
        [Min(4f)] public float ExcavationPitWidth = 32f;
        [Min(4f)] public float ExcavationPitLength = 27f;
        [Min(0.1f)] public float ExcavationTerraceStep = 2.4f;
        [Range(2, 12)] public int ExcavationCraneTrussCount = 7;
        [Range(1, 10)] public int ExcavationCargoStackCount = 5;

        [Header("Fallen Orbital Array Detail")]
        [Min(4f)] public float OrbitalDishRadius;
        [Range(8, 48)] public int OrbitalDishSegmentCount;
        [Range(0, 12)] public int OrbitalDishMissingSegmentCount;
        [Range(0f, 89f)] public float OrbitalDishTiltMinimum;
        [Range(0f, 89f)] public float OrbitalDishTiltMaximum;
        [Min(1f)] public float OrbitalMastHeight;
        [Range(0, 8)] public int OrbitalSolarWingCount;
        [Min(1f)] public float OrbitalSolarWingLength;
        [Range(0, 40)] public int OrbitalDebrisCount;
        [Min(0f)] public float OrbitalDebrisSpread;
        [Min(0f)] public float OrbitalBurialDepth;

        [Header("Desert Megagate Detail")]
        [Range(2, 6)] public int MegagatePylonCount;
        [Min(8f)] public float MegagatePylonHeight;
        [Min(2f)] public float MegagatePylonWidth;
        [Min(4f)] public float MegagateOpeningWidth;
        [Range(0f, 0.9f)] public float MegagateTaper;
        [Range(0, 12)] public int MegagateBridgeFragmentCount;
        [Range(0, 20)] public int MegagateBaseRuinCount;
        [Range(0, 40)] public int MegagateDebrisCount;
        [Min(0f)] public float MegagateBurialDepth;

        [Header("Wind Harvester Graveyard Detail")]
        [Range(1, 30)] public int HarvesterCount;
        [Min(2f)] public float HarvesterRingRadius;
        [Min(0.1f)] public float HarvesterRingThickness;
        [Range(8, 36)] public int HarvesterRingSegmentCount;
        [Min(4f)] public float HarvesterTowerHeight;
        [Min(4f)] public float HarvesterSpacing;
        [Range(0f, 1f)] public float HarvesterBrokenChance;
        [Range(0f, 1f)] public float HarvesterLeanChance;
        [Range(0f, 1f)] public float HarvesterFallenChance;
        [Range(0, 60)] public int HarvesterDebrisCount;
        [Min(8f)] public float HarvesterFieldRadius;

        [Header("Buried Arcology Detail")]
        [Min(8f)] public float ArcologyCoreRadius;
        [Min(8f)] public float ArcologyCoreHeight;
        [Range(0.5f, 0.95f)] public float ArcologyBurialRatio;
        [Range(1, 16)] public int ArcologyRoofClusterCount;
        [Min(8f)] public float ArcologyRoofClusterRadius;
        [Range(0, 20)] public int ArcologyVentTowerCount;
        [Range(0, 24)] public int ArcologyStructuralRibCount;
        [Range(0, 40)] public int ArcologyExposedWindowCount;
        [Range(0, 50)] public int ArcologyDebrisCount;

        [Header("Sand Ring Detail")]
        [Min(4f)] public float SandRingRadius;
        [Range(12, 64)] public int SandRingSegmentCount;
        [Min(0.2f)] public float SandRingThickness;
        [Min(0f)] public float SandRingBurialDepth;
        [Range(0, 20)] public int SandRingMissingSegmentCount;
        [Range(0, 16)] public int SandRingSupportCount;
        [Min(1f)] public float SandRingSupportRadius;
        [Range(0, 50)] public int SandRingDebrisCount;
        [Min(0f)] public float SandRingDebrisSpread;
        [Range(-35f, 35f)] public float SandRingTilt;
    }

    [System.Serializable]
    public sealed class RouteEncounterTuning
    {
        public bool Enabled = true;
        [Min(0.1f)] public float MinimumEncounterInterval = 28f;
        [Min(0.1f)] public float MaximumEncounterInterval = 52f;
        [Min(0.1f)] public float HighValueIntervalMultiplier = 0.52f;
        [Header("High-Value Contracts")]
        [Min(0f)] public float HighValueInitialEncounterDelay = 3f;
        [Min(0f)] public float HighValueMinimumObjectiveDistance = 60f;
        [Range(0, 6)] public int HighValueFormationSizeBonus = 2;
        [Min(0.1f)] public float HighValueEnemySpeedMultiplier = 1.18f;
        [Min(0.1f)] public float HighValueEnemyHealthMultiplier = 1.25f;
        [Min(0f)] public float HighValueDamageMultiplier = 1.35f;
        [Min(0.1f)] public float HighValueShotIntervalMultiplier = 0.78f;
        [Range(0f, 1f)] public float HighValueSecondPassChanceBonus = 0.25f;
        [Header("High-Value World Threats")]
        [Range(0, 12)] public int HighValueGroundEnemyBonus = 4;
        [Min(0f)] public float HighValueGroundEnemyMinimumSpawnDistance = 35f;
        [Min(0f)] public float HighValueGroundEnemyMaximumSpawnDistance = 80f;
        [Range(0, 8)] public int HighValueStormPyramidBonus = 2;
        [Min(20f)] public float MinimumObjectiveDistance = 180f;
        [Min(10f)] public float EncounterVolumeRadius = 90f;
        [Range(1, 5)] public int VolumesPerRouteLeg = 2;
        [Range(2, 10)] public int MinimumFormationSize = 3;
        [Range(2, 12)] public int MaximumFormationSize = 6;
        [Min(10f)] public float SpawnDistance = 125f;
        [Min(1f)] public float FormationSpacing = 16f;
        [Header("Formation Choreography")]
        [Min(0f)] public float FormationCommitDistance = 28f;
        [Min(0.1f)] public float FormationCommitRadius = 5f;
        [Min(0f)] public float FormationAltitudeStagger = 4f;
        [Range(0f, 1f)] public float FormationPlayerAltitudeContribution = 0.35f;
        [Range(0f, 1f)] public float FormationApproachAltitudeBlend = 0.48f;
        [Range(0f, 1f)] public float FormationApproachLateralCompression = 0.65f;
        [Range(0f, 1f)] public float FormationDepthCommitContribution = 0.4f;
        [Min(0f)] public float CrossAttackExitDistanceMultiplier = 0.55f;
        [Range(0f, 1f)] public float VerticalApproachHeightMultiplier = 0.45f;
        [Range(0f, 1f)] public float FormationLowerBreakMultiplier = 0.42f;
        [Range(0f, 1f)] public float FormationObjectiveDirectionWeight = 0.35f;
        [Min(0f)] public float HeadOnWingDepthSpacing = 9f;
        [Min(0f)] public float HeadOnPassLateralSpacing = 5f;
        [Min(0f)] public float CrossAttackLaneSpacing = 11f;
        [Min(0f)] public float PursuitWingDepthSpacing = 7f;
        [Min(0f)] public float PursuitOvertakeDistance = 34f;
        [Range(0.1f, 2f)] public float VerticalFormationWidthMultiplier = 0.7f;
        [Min(0f)] public float FlyThroughFormationDepthSpacing = 24f;
        [Min(0f)] public float FormationBreakVerticalSeparation = 12f;
        [Min(0f)] public float FormationRepositionHeight = 10f;
        [Min(0f)] public float LowAltitude = 9f;
        [Min(0f)] public float MediumAltitude = 24f;
        [Min(0f)] public float HighAltitude = 46f;
        [Min(1f)] public float ApproachSpeed = 48f;
        [Min(1f)] public float AttackPassSpeed = 68f;
        [Min(1f)] public float BreakSpeed = 58f;
        [Min(1f)] public float TurnSharpness = 5.5f;
        [Min(1f)] public float BreakOffDistance = 210f;
        [Min(0f)] public float RepositionDelay = 1.2f;
        [Range(0, 3)] public int MaximumAttackPasses = 2;
        [Min(1f)] public float EnemyHealth = 55f;
        [Min(0)] public int EnemyGoldReward = 12;
        [Min(0.1f)] public float EnemyVisualScale = 1.25f;
        [Min(0f)] public float ContactDamage = 12f;
        public string ContactDeathMessage = "Destroyed by a Formation Enemy collision.";
        [Min(0.1f)] public float ContactRadius = 2.4f;
        [Min(0f)] public float ShotDamage = 7f;
        public string ShotDeathMessage = "Destroyed by a Formation Enemy shot.";
        [Min(0.1f)] public float ShotInterval = 1.1f;
        [Min(0.1f)] public float ShotTelegraphDuration = 0.22f;
        [Min(0.1f)] public float ShotHitRadius = 2.2f;
        [Min(0.05f)] public float ShotVisualDuration = 0.16f;
        [Min(0.01f)] public float ShotStartWidth = 0.1f;
        [Min(0.01f)] public float ShotEndWidth = 0.025f;
        [Range(0f, 1f)] public float SecondPassChance = 0.62f;
        [ColorUsage(false, true)] public Color FormationEmission = new Color(3.8f, 0.16f, 0.05f, 1f);
        [ColorUsage(false, true)] public Color ShotEmission = new Color(4.5f, 0.35f, 0.06f, 1f);

        [Header("Encounter Presentation")]
        [Min(0f)] public float WaveAnnouncementDuration = 2.2f;
        [Min(0f)] public float WaveAnnouncementTop = 142f;
        [Min(10)] public int WaveAnnouncementFontSize = 18;
        [ColorUsage(false)] public Color WaveAnnouncementColor = new Color(1f, 0.35f, 0.12f, 1f);
        [Min(0f)] public float EnemyTrailDuration = 0.42f;
        [Min(0f)] public float EnemyTrailStartWidth = 0.32f;
        [Min(0f)] public float EnemyTrailEndWidth = 0.02f;
        [Min(0f)] public float EnemyTrailMinimumVertexDistance = 0.08f;
        [Min(0f)] public float TelegraphPulseSpeed = 18f;
        [Min(0f)] public float TelegraphMinimumWidthMultiplier = 0.35f;
        [Min(0f)] public float FlyThroughGuideDuration = 5.5f;
        [Range(2, 8)] public int FlyThroughGuideGateCount = 4;
        [Min(0f)] public float FlyThroughGuideGateSpacing = 28f;
        [Min(0f)] public float FlyThroughGuideGateRadius = 6.5f;
        [Min(0f)] public float FlyThroughGuideGateThickness = 0.18f;
        [Min(0f)] public float FlyThroughGuidePulseSpeed = 3.6f;
        [Min(0f)] public float FlyThroughGuidePulseAmount = 0.1f;
    }

    [System.Serializable]
    public sealed class PyramidTuning
    {
        [Min(0f)] public float DensityPerChunk = 0.22f;
        [Min(0.1f)] public float MinimumScale = 2f;
        [Min(0.1f)] public float MaximumScale = 4.4f;
        [Range(0f, 89f)] public float MaximumPlacementSlope = 24f;
        [Min(0f)] public float MinimumBurialDepth = 0.75f;
        [Min(0f)] public float MaximumBurialDepth = 1.25f;
    }

    [System.Serializable]
    public sealed class DesertShrubVariantTuning
    {
        [Tooltip("Designer label used to identify this procedural shrub silhouette.")]
        public string Name = "Shrub";
        [Min(0f)] public float SelectionWeight = 1f;
        [Min(0.1f)] public float Height = 1.15f;
        [Min(0.1f)] public float Width = 1.7f;
        [Range(2, 9)] public int BranchCount = 5;
        [Range(0.1f, 1f)] public float BranchStartHeight = 0.28f;
        [Range(0f, 1f)] public float BranchUpwardBias = 0.38f;
        [ColorUsage(false)] public Color Color = new Color(0.25f, 0.28f, 0.105f, 1f);
        [Range(0f, 1f)] public float Smoothness = 0.12f;
    }

    [System.Serializable]
    public sealed class DesertShrubTuning
    {
        public bool Enabled = true;

        [Header("Distribution")]
        [Tooltip("Target population before slope, spacing, biome, and exclusion rejection.")]
        [Min(0f)] public float DensityPerChunk = 10f;
        [Min(8f)] public float ClusterCellSize = 62f;
        [Range(0f, 1f)] public float ClusterChance = 0.48f;
        [Range(1, 32)] public int MinimumClusterSize = 5;
        [Range(1, 48)] public int MaximumClusterSize = 15;
        [Min(0.1f)] public float ClusterRadius = 15f;
        [Min(0f)] public float MinimumSpacing = 1.7f;

        [Header("Biome Weighting")]
        [Min(1f)] public float BiomeNoiseScale = 230f;
        [Range(-1f, 1f)] public float MinimumBiomeNoise = -0.18f;
        [Range(-1f, 1f)] public float FullDensityBiomeNoise = 0.38f;
        [Range(0.1f, 5f)] public float BiomeWeightPower = 1.65f;
        [Range(0f, 1f)] public float MinimumRegionWeight = 0.08f;

        [Header("Surface Placement")]
        [Range(0f, 89f)] public float MaximumSlope = 27f;
        [Min(0.05f)] public float MinimumScale = 0.72f;
        [Min(0.05f)] public float MaximumScale = 1.35f;
        [Min(0f)] public float MinimumBurialDepth = 0.05f;
        [Min(0f)] public float MaximumBurialDepth = 0.18f;
        [Range(0f, 1f)] public float SurfaceAlignment = 0.42f;

        [Header("Exclusions")]
        [Min(0f)] public float GameplayExclusionRadius = 10f;
        [Min(0f)] public float HubExclusionRadius = 38f;
        [Min(0f)] public float LandmarkExclusionRadius = 42f;
        [Min(0f)] public float SceneryExclusionRadius = 4f;

        [Header("Rendering")]
        [Min(1f)] public float LodDistance = 105f;
        [Min(1f)] public float CullDistance = 215f;
        public bool CastShadows = true;
        public bool ReceiveShadows = true;

        [Header("Variants")]
        public List<DesertShrubVariantTuning> Variants = new List<DesertShrubVariantTuning>
        {
            new DesertShrubVariantTuning(),
        };

        public void EnsureInitialized()
        {
            Variants ??= new List<DesertShrubVariantTuning>();
        }
    }

    [System.Serializable]
    public sealed class WorldStreamingTuning
    {
        [Tooltip("Chunk radius kept active around the player.")]
        [Range(1, 14)] public int ActiveRadius = 3;
        [Tooltip("Chunk radius generated ahead of the player.")]
        [Range(1, 9)] public int PreloadRadius = 3;
        [Tooltip("Chunks beyond this radius are removed.")]
        [Range(2, 12)] public int UnloadRadius = 4;
        [Tooltip("Maximum terrain chunks generated during one frame.")]
        [Range(1, 4)] public int ChunksGeneratedPerFrame = 1;
        [Tooltip("Local distance at which the world recenters around the drone.")]
        [Min(50f)] public float FloatingOriginThreshold = 520f;
    }

    [System.Serializable]
    public sealed class RendererFrustumCullingTuning
    {
        [Tooltip("Disable rendering for scene renderers outside the gameplay camera's padded frustum.")]
        public bool Enabled = true;

        [Tooltip("World-space distance added beyond every frustum plane so camera movement reveals objects before they enter view.")]
        [Min(0f)] public float Padding = 30f;

        [Tooltip("How often newly spawned renderers are added to the culling set. Tracked renderers are culled every frame.")]
        [Min(0.05f)] public float RendererRefreshInterval = 0.5f;
    }

    [System.Serializable]
    public sealed class SpatialGpuInstancingTuning
    {
        [Tooltip("Use spatially bounded Graphics.RenderMeshInstanced batches for supported procedural visuals.")]
        public bool Enabled = true;

        [Tooltip("World-space width and depth of an instance culling cell.")]
        [Min(8f)] public float CellSizeMeters = 32f;

        [Tooltip("Maximum instances submitted by one RenderMeshInstanced call. Kept below Unity's theoretical limit for reliable custom instance data.")]
        [Range(1, 1023)] public int MaximumInstancesPerDraw = 500;

        [Tooltip("Keep one captured source renderer visible and offset its instanced copy for visual transform comparison.")]
        public bool EnableDebugComparison;

        [Tooltip("World-space offset applied to the instanced side of the optional source-versus-instance comparison.")]
        public Vector3 DebugComparisonOffset = new Vector3(2f, 0f, 0f);
    }

    [System.Serializable]
    public sealed class PlayerHealthTuning
    {
        [Header("Debug")]
        [Tooltip("Starts the player with infinite health. This can also be changed at runtime from the F1 telemetry panel.")]
        public bool DebugInfiniteHealth;

        [Header("Health")]
        [Min(1f)] public float MaximumHealth = 100f;
        [Min(0f)] public float DamageInvulnerability = 0.45f;
    }

    public enum CourierDroneFaction
    {
        Player,
        Rival,
        Neutral,
    }

    [System.Serializable]
    public sealed class DroneVisualTuning
    {
        [Header("Materials")]
        [ColorUsage(false)] public Color BodyColor = new Color(0.68f, 0.72f, 0.74f);
        [Range(0f, 1f)] public float BodySmoothness = 0.72f;
        [Range(0f, 1f)] public float BodyMetallic = 0.7f;
        [ColorUsage(false)] public Color FrameColor = new Color(0.018f, 0.025f, 0.033f);
        [Range(0f, 1f)] public float FrameSmoothness = 0.64f;
        [Range(0f, 1f)] public float FrameMetallic = 0.85f;
        [ColorUsage(false)] public Color TrailColor = new Color(0f, 0.06f, 0.08f);
        [ColorUsage(false, true)] public Color TrailEmission = new Color(0f, 0.8f, 1.4f);
        [Range(0f, 1f)] public float TrailSmoothness = 0.6f;
        [Range(0f, 1f)] public float TrailMetallic = 0.1f;

        [Header("Hull")]
        [Min(0f)] public float CourierVisualHeight = 0.92f;
        public Vector3 LowerHullPosition = new Vector3(0f, -0.08f, -0.04f);
        public Vector3 LowerHullScale = new Vector3(1.2f, 0.28f, 1.58f);
        public Vector3 UpperHullPosition = new Vector3(0f, 0.04f, 0.08f);
        public Vector3 UpperHullScale = new Vector3(1.08f, 0.34f, 1.5f);
        public Vector3 CanopyPosition = new Vector3(0f, 0.28f, 0.28f);
        public Vector3 CanopyScale = new Vector3(0.62f, 0.19f, 0.78f);
        public Vector3 NoseSensorPosition = new Vector3(0f, 0.05f, 1.46f);
        public Vector3 NoseSensorScale = new Vector3(0.28f, 0.15f, 0.14f);
        public Vector3 TailLightPosition = new Vector3(0f, 0.08f, -1.5f);
        public Vector3 TailLightScale = new Vector3(0.44f, 0.08f, 0.11f);

        [Header("Swept Wings")]
        [Min(0.05f)] public float WingInnerOffset = 0.38f;
        [Min(0.05f)] public float WingSpan = 1.42f;
        [Min(0.05f)] public float WingRootChord = 1.08f;
        [Min(0.05f)] public float WingTipChord = 0.48f;
        [Min(0f)] public float WingSweep = 0.5f;
        [Min(0.01f)] public float WingThickness = 0.11f;
        public float WingHeight = -0.015f;
        public float WingForwardOffset = 0.04f;
        [Min(0f)] public float WingAccentInset = 0.13f;
        [Min(0f)] public float WingAccentLift = 0.075f;
        [Min(0.005f)] public float WingAccentThickness = 0.014f;

        [Header("Rotors")]
        public Vector3 FrontRotorPosition = new Vector3(1.58f, 0.03f, 0.42f);
        public Vector3 RearRotorPosition = new Vector3(1.38f, 0.03f, -0.52f);
        public Vector3 RotorNacelleScale = new Vector3(0.52f, 0.18f, 0.52f);
        [Min(0.05f)] public float RotorGuardRadius = 0.48f;
        [Min(0.005f)] public float RotorGuardThickness = 0.055f;
        [Min(0.005f)] public float RotorGlowThickness = 0.024f;
        [Min(0f)] public float RotorGuardHeight = 0.14f;
        public Vector3 RotorHubScale = new Vector3(0.13f, 0.09f, 0.13f);
        [Min(0.02f)] public float RotorBladeLength = 0.72f;
        [Min(0.005f)] public float RotorBladeWidth = 0.055f;
        [Min(0.005f)] public float RotorBladeThickness = 0.018f;
        [Min(0f)] public float RotorSpinSpeed = 860f;
        [Range(0f, 0.25f)] public float RotorPulseAmount = 0.045f;
        [Min(0f)] public float RotorPulseSpeed = 4.5f;

        [Header("Trails")]
        public Vector3 TrailPosition = new Vector3(0.5f, -0.08f, -1.2f);
        [Min(0f)] public float TrailDuration = 0.3f;
        [Min(0f)] public float TrailStartWidth = 0.065f;
        [Min(0f)] public float TrailEndWidth;
        [Min(0.001f)] public float TrailMinimumVertexDistance = 0.12f;
    }

    [System.Serializable]
    public sealed class DynamicCourierTuning
    {
        public bool Enabled;

        [Header("Event Scheduling")]
        [Min(0f)] public float InitialEventDelay;
        [Min(0f)] public float MinimumEventInterval;
        [Min(0f)] public float MaximumEventInterval;
        [Min(1f)] public float MinimumSpawnDistance;
        [Min(1f)] public float MaximumSpawnDistance;
        [Min(1f)] public float MinimumRouteDistance;
        [Min(1f)] public float MaximumRouteDistance;
        [Min(1f)] public float OfferedEventDespawnDistance;
        [Min(0f)] public float ResultDisplayDuration;
        [Min(0f)] public float DistressEventWeight;
        [Min(0f)] public float RaceEventWeight;
        [Min(0f)] public float ConvoyEventWeight;

        [Header("Ambient Neutral Deliveries")]
        public bool AmbientNeutralCouriersEnabled;
        [Range(0, 24)] public int AmbientNeutralCourierCount;
        [Min(1f)] public float AmbientMinimumSpawnDistance;
        [Min(1f)] public float AmbientMaximumSpawnDistance;
        [Min(1f)] public float AmbientMinimumRouteDistance;
        [Min(1f)] public float AmbientMaximumRouteDistance;
        [Min(0f)] public float AmbientMinimumCruiseSpeed;
        [Min(0f)] public float AmbientMaximumCruiseSpeed;
        [Min(0f)] public float AmbientMinimumFlightHeight;
        [Min(0f)] public float AmbientMaximumFlightHeight;
        [Min(0f)] public float AmbientMinimumTurnaroundDelay;
        [Min(0f)] public float AmbientMaximumTurnaroundDelay;
        [Min(1f)] public float AmbientDespawnDistance;
        [Min(0.01f)] public float AmbientPackageScale;
        public Vector3 AmbientPackageOffset;

        [Header("Courier Flight")]
        [Min(0f)] public float FlightHeightAboveTerrain;
        [Min(0f)] public float CruiseSpeed;
        [Min(0f)] public float RivalRaceSpeed;
        [Min(0f)] public float TurnSharpness;
        [Min(0.1f)] public float DestinationRadius;
        [Min(0f)] public float HoverAmplitude;
        [Min(0f)] public float HoverFrequency;
        [Min(0.1f)] public float VisualScale;
        [Min(1f)] public float MaximumCourierHealth;

        [Header("Distressed Courier")]
        [Range(0.01f, 1f)] public float DistressedStartingHealthFraction;
        [Range(1, 8)] public int DistressAttackerCount;
        [Min(0)] public int DistressRescueReward;

        [Header("Courier Race")]
        [Min(1f)] public float ChallengeAcceptDistance;
        public UnityEngine.InputSystem.Key ChallengeAcceptKey;
        [Min(0)] public int RaceWinnerReward;

        [Header("Moving Convoy")]
        [Range(0, 6)] public int ConvoyEscortCount;
        [Range(1, 10)] public int ConvoyAttackerCount;
        [Min(0f)] public float ConvoyEscortSpacing;
        [Range(0f, 1f)] public float ConvoyMinimumRewardFraction;
        [Min(0)] public int ConvoyMaximumReward;

        [Header("Event Attackers")]
        [Min(1f)] public float AttackerMaximumHealth;
        [Min(0.1f)] public float AttackerVisualScale;
        [Min(0f)] public float AttackerSpeed;
        [Min(0f)] public float AttackerTurnSharpness;
        [Min(0f)] public float AttackerOrbitRadius;
        [Min(0f)] public float AttackerHeightOffset;
        [Min(0.1f)] public float AttackerShotRange;
        [Min(0.1f)] public float AttackerShotInterval;
        [Min(0f)] public float AttackerShotDamage;
        [Min(0.01f)] public float AttackerCollisionRadius;
        [Min(0f)] public int AttackerGoldReward;
        [Min(0.01f)] public float AttackerShotVisualDuration;
        [Min(0.001f)] public float AttackerShotStartWidth;
        [Min(0.001f)] public float AttackerShotEndWidth;

        [Header("Faction Tops")]
        [ColorUsage(false)] public Color PlayerTopColor;
        [ColorUsage(false, true)] public Color PlayerTopEmission;
        [ColorUsage(false)] public Color RivalTopColor;
        [ColorUsage(false, true)] public Color RivalTopEmission;
        [ColorUsage(false)] public Color NeutralTopColor;
        [ColorUsage(false, true)] public Color NeutralTopEmission;
        [Range(0f, 1f)] public float TopMaterialSmoothness;
        [Range(0f, 1f)] public float TopMaterialMetallic;

        [Header("Event HUD")]
        [Min(100f)] public float HudWidth;
        [Min(60f)] public float HudHeight;
        [Min(0f)] public float HudLeft;
        [Min(0f)] public float HudTop;
        [Min(0f)] public float HudPadding;
        [Min(8)] public int HudTitleFontSize;
        [Min(8)] public int HudBodyFontSize;
        [Min(0f)] public float HudTitleHeight;
        [Min(0f)] public float HudLineHeight;
        [Min(8f)] public float ObjectiveMarkerSize;
        [Min(0f)] public float ObjectiveMarkerEdgePadding;
        [Min(80f)] public float ObjectiveMarkerLabelWidth;
        [Min(12f)] public float ObjectiveMarkerLabelHeight;
        [Min(8)] public int ObjectiveMarkerFontSize;
        [ColorUsage(false)] public Color HudPanelColor;
        [ColorUsage(false)] public Color HudTextColor;
        [ColorUsage(false)] public Color DistressHudColor;
        [ColorUsage(false)] public Color RaceHudColor;
        [ColorUsage(false)] public Color ConvoyHudColor;
        [ColorUsage(false)] public Color SuccessHudColor;
        [ColorUsage(false)] public Color FailureHudColor;
    }

    [System.Serializable]
    public sealed class EnergyLauncherTuning
    {
        public bool Enabled = true;

        [Header("Lock-On Targeting")]
        [Min(1f)] public float LockRange = 180f;
        [Tooltip("Full angle of the view-centered targeting cone. Targets behind the camera are always rejected.")]
        [Range(1f, 179f)] public float LockConeAngle = 34f;
        [Min(0f)] public float AcquisitionTime = 0.55f;
        [Tooltip("Brief time that TARGET DETECTED is shown before acquisition begins.")]
        [Min(0f)] public float TargetDetectedDuration = 0.12f;
        [Tooltip("Grace time before an acquired target outside the cone or range is released.")]
        [Min(0f)] public float TargetLossTolerance = 0.32f;
        [Tooltip("Seconds between candidate scoring passes. Current-target validity is still checked every frame.")]
        [Min(0.01f)] public float TargetScanInterval = 0.05f;
        [Tooltip("How much better a new view-center score must be before replacing the current target.")]
        [Range(0f, 1f)] public float TargetSwitchAdvantage = 0.12f;
        [Tooltip("Small distance contribution to selection score; screen-center alignment remains dominant.")]
        [Range(0f, 0.5f)] public float DistanceScoreWeight = 0.08f;

        [Header("Energy Shot")]
        [Min(1f)] public float ProjectileSpeed = 155f;
        [Tooltip("Maximum homing direction change in degrees per second.")]
        [Min(0f)] public float HomingTurnStrength = 430f;
        [Min(0f)] public float Damage = 45f;
        [Min(0f)] public float FireCooldown = 0.22f;
        [Min(0.05f)] public float ProjectileLifetime = 3f;
        [Min(0.01f)] public float ProjectileHitRadius = 0.32f;
        [Tooltip("Maximum look-ahead time used to lead a moving locked target.")]
        [Min(0f)] public float LeadPredictionTime = 0.65f;
        [Tooltip("Caps measured target velocity used for lead prediction, filtering floating-origin shifts and spikes.")]
        [Min(0f)] public float MaximumLeadSpeed = 140f;
        [Tooltip("View-relative launch offset from the drone center.")]
        public Vector3 MuzzleOffset = new Vector3(0f, -0.1f, 2.4f);

        [Header("Projectile Feedback")]
        [Min(0.01f)] public float ProjectileScale = 0.28f;
        [Min(0.01f)] public float TrailDuration = 0.2f;
        [Min(0.001f)] public float TrailStartWidth = 0.2f;
        [Min(0f)] public float TrailMinimumVertexDistance = 0.08f;
        [Min(0.01f)] public float LaunchFlashScale = 0.85f;
        [Min(0.01f)] public float LaunchFlashDuration = 0.11f;
        [Min(0.01f)] public float ImpactFlashScale = 2.2f;
        [Min(0.01f)] public float ImpactFlashDuration = 0.24f;
        [ColorUsage(false, true)] public Color ProjectileColor = new Color(0.08f, 0.72f, 1f);
        [ColorUsage(false, true)] public Color ProjectileEmission = new Color(2f, 12f, 24f);

        [Header("Targeting HUD")]
        [Min(240f)] public float HudReferenceHeight = 1080f;
        [Min(1f)] public float CenterReticleSize = 22f;
        [Min(0f)] public float CenterReticleGap = 6f;
        [Min(1f)] public float ReticleLineThickness = 2f;
        [Min(1f)] public float TargetDetectedReticleSize = 84f;
        [Min(1f)] public float LockedReticleSize = 44f;
        [Min(1f)] public float TargetBracketLength = 18f;
        [Min(0f)] public float LockedPulseAmount = 4f;
        [Min(0f)] public float ReticlePulseSpeed = 8f;
        [Min(1f)] public float LockedConfirmationSize = 7f;
        [Min(0f)] public float TargetStatusOffset = 28f;
        [Min(0f)] public float TargetDistanceOffset = 46f;
        [Min(40f)] public float HudLabelWidth = 190f;
        [Min(8f)] public float HudLabelHeight = 22f;
        [Min(8)] public int TargetStatusFontSize = 14;
        [Min(8)] public int TargetDistanceFontSize = 12;
        [ColorUsage(false)] public Color CenterReticleColor = new Color(0.72f, 0.92f, 1f, 0.88f);
        [ColorUsage(false)] public Color TargetDetectedColor = new Color(1f, 0.72f, 0.18f, 0.95f);
        [ColorUsage(false)] public Color LockingColor = new Color(0.15f, 0.86f, 1f, 0.98f);
        [ColorUsage(false)] public Color LockedColor = new Color(0.35f, 1f, 0.62f, 1f);
    }

    [System.Serializable]
    public sealed class FlyingEnemyTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 90f;
        [Min(0)] public int GoldReward = 20;
        [Range(1, 12)] public int EnemyCount = 3;
        [Min(10f)] public float MinimumSpawnDistance = 55f;
        [Min(10f)] public float MaximumSpawnDistance = 105f;
        [Min(1f)] public float DetectionRange = 125f;
        [Min(1f)] public float HoverHeight = 20f;
        [Min(0f)] public float HoverAmplitude = 1.1f;
        [Tooltip("Player follow speed at risk 0.")]
        [Min(0f)] public float FollowSpeed = 11f;
        [Tooltip("Player follow speed at the speed risk scaling ceiling.")]
        [Min(0f)] public float FollowSpeedAtRiskCeiling = 33f;
        [Tooltip("Attack dive speed at risk 0.")]
        [Min(0f)] public float AttackSpeed = 66f;
        [Tooltip("Attack dive speed at the speed risk scaling ceiling.")]
        [Min(0f)] public float AttackSpeedAtRiskCeiling = 102f;
        [Tooltip("Risk at which follow and attack speeds reach their ceiling values.")]
        [Min(1)] public int SpeedRiskScalingCeiling = 20;
        [Min(0.1f)] public float AttackCooldown = 3.5f;
        [Min(0.25f)] public float AttackAlignmentDistance = 4f;
        [Min(0f)] public float ImpactDamage = 25f;
        public string ImpactDeathMessage = "Destroyed by a Sky Piecer impact.";
        [Min(0.1f)] public float ImpactRadius = 3.4f;
        [Min(0f)] public float StuckDuration = 2.2f;
        [Min(0f)] public float ReturnSpeed = 13f;
        [Min(20f)] public float RepositionDistance = 240f;
        [Min(0.1f)] public float VisualScale = 1.35f;

        public float EvaluateFollowSpeed(int risk)
        {
            return Mathf.Lerp(FollowSpeed, FollowSpeedAtRiskCeiling, EvaluateSpeedRisk(risk));
        }

        public float EvaluateAttackSpeed(int risk)
        {
            return Mathf.Lerp(AttackSpeed, AttackSpeedAtRiskCeiling, EvaluateSpeedRisk(risk));
        }

        private float EvaluateSpeedRisk(int risk)
        {
            return Mathf.Clamp01(risk / (float)Mathf.Max(1, SpeedRiskScalingCeiling));
        }
    }

    [System.Serializable]
    public sealed class StormPyramidTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 135f;
        [Min(0)] public int GoldReward = 50;

        [Header("Spawning")]
        [Range(1, 10)] public int EnemyCount = 2;
        [Min(20f)] public float MinimumSpawnDistance = 90f;
        [Min(20f)] public float MaximumSpawnDistance = 180f;
        [Min(50f)] public float RepositionDistance = 360f;

        [Header("High-Altitude Patrol")]
        [Tooltip("Height above the terrain used as the center of this enemy's altitude range.")]
        [Min(10f)] public float HoverHeight = 72f;
        [Tooltip("Random amount added above or below Hover Height when each enemy spawns.")]
        [Min(0f)] public float HoverHeightVariance = 16f;
        [Min(0f)] public float PatrolDriftRange = 16f;
        [Min(0f)] public float PatrolDriftSpeed = 3f;

        [Header("Targeting")]
        [Tooltip("Ground strikes show a nearby warning when the impact point is inside this range of the drone.")]
        [Min(1f)] public float DetectionRange = 125f;

        [Header("Lightning Attack")]
        [Tooltip("Delay before beginning another straight-down ground strike after returning to idle at risk 0.")]
        [Min(0f)] public float AttackInterval = 4.5f;
        [Tooltip("Delay between ground strikes at the attack interval risk ceiling.")]
        [Min(0f)] public float AttackIntervalAtRiskCeiling = 0f;
        [Tooltip("Risk at which the storm pyramid reaches Attack Interval At Risk Ceiling.")]
        [Min(1)] public int AttackIntervalRiskCeiling = 20;
        [Tooltip("Charge duration for a straight-down ground strike.")]
        [InspectorName("Ground Strike Charge Time")]
        [Min(0.1f)] public float ChargeTime = 1.15f;
        [Min(0f)] public float Cooldown = 2.4f;
        [Min(0f)] public float LightningDamage = 32f;
        public string LightningDeathMessage = "Struck by Storm Pyramid ground lightning.";
        [Tooltip("Ground strike radius at risk 0.")]
        [Min(0.1f)] public float StrikeRadius = 5f;
        [Tooltip("Ground strike radius at the strike radius risk ceiling.")]
        [Min(0.1f)] public float StrikeRadiusAtRiskCeiling = 20f;
        [Tooltip("Risk at which the ground strike reaches Strike Radius At Risk Ceiling.")]
        [Min(1)] public int StrikeRadiusRiskCeiling = 20;
        [Min(0.05f)] public float LightningVisualDuration = 0.28f;
        [Min(0.01f)] public float ChargeTelegraphWidth = 0.12f;
        [Min(0.01f)] public float LightningWidth = 0.48f;
        [Tooltip("Multiplies only the lightning bolt emission, creating a stronger HDR bloom halo.")]
        [Min(0f)] public float LightningBloomIntensity = 4f;

        [Header("Ground Impact Effect")]
        [Tooltip("Time for the ground shockwave to expand from the strike point to Strike Radius.")]
        [Min(0.01f)] public float GroundImpactExpansionDuration = 0.42f;
        [Tooltip("Time the ground shockwave remains at the full Strike Radius before disappearing.")]
        [Min(0f)] public float GroundImpactHoldDuration = 0.1f;
        [Tooltip("Initial shockwave size as a fraction of Strike Radius.")]
        [Range(0f, 1f)] public float GroundImpactStartScale = 0.08f;
        [Tooltip("Peak size of the central impact flash as a fraction of Strike Radius.")]
        [Min(0f)] public float GroundImpactFlashScaleMultiplier = 0.34f;
        [Tooltip("World-space thickness of the expanding ground shockwave ring.")]
        [Min(0.005f)] public float GroundImpactRingThickness = 0.14f;
        [Tooltip("Raises the shockwave slightly above the terrain to keep it visible on the ground.")]
        [Min(0f)] public float GroundImpactHeightOffset = 0.06f;

        [Header("Attack Warning HUD")]
        [Tooltip("Ground strikes show the attack warning when their impact point is within this distance of the drone. Player-targeted strikes always warn.")]
        [Min(1f)] public float NearbyWarningRange = 55f;
        [Tooltip("Speed of the warning border and marker pulse.")]
        [Min(0f)] public float WarningPulseSpeed = 9f;
        [Tooltip("Scales the warning panel, text, target marker, and screen border together.")]
        [Range(0.6f, 2f)] public float WarningHudScale = 1f;
        [Tooltip("Distance in pixels that the directional strike marker stays inside the screen edge before HUD scaling.")]
        [Min(12f)] public float WarningEdgePadding = 64f;

        [Header("Presentation")]
        [Min(0.1f)] public float VisualScale = 2.2f;
        [Min(0.1f)] public float BodyWidth = 4.8f;
        [Min(0.1f)] public float BodyHeight = 3.8f;
        [Min(0f)] public float BodyCornerCut = 0.38f;
        [Range(1, 8)] public int EnergyBandCount = 3;
        [Range(0f, 1f)] public float EnergyBandStart = 0.2f;
        [Range(0f, 1f)] public float EnergyBandEnd = 0.72f;
        [Min(0.005f)] public float EnergyBandThickness = 0.055f;
        [Min(0.005f)] public float EdgeConduitRadius = 0.035f;
        [Range(3, 8)] public int CrownFinCount = 4;
        [Min(0f)] public float CrownFinRadius = 2.15f;
        public Vector3 CrownFinSize = new Vector3(0.32f, 0.68f, 0.86f);
        [Range(-60f, 60f)] public float CrownFinOutwardTilt = 18f;
        [Min(0.1f)] public float CrownRingRadius = 1.9f;
        [Min(0.01f)] public float CrownRingThickness = 0.1f;
        public float CrownHeight = 0.14f;
        public float CoreHeight = 0.18f;
        public Vector3 CoreScale = new Vector3(0.78f, 0.24f, 0.78f);
        [Min(0.1f)] public float CoreRingRadius = 1.28f;
        [Min(0.01f)] public float CoreRingThickness = 0.055f;
        public float CoreRingHeight = 0.24f;
        [Min(0.1f)] public float ChargeHaloRadius = 1.72f;
        [Min(0.01f)] public float ChargeHaloThickness = 0.075f;
        public float ChargeHaloHeight = 0.46f;
        [Min(0f)] public float LightningOriginTipOffset = 0.08f;
        public float VisualRotationSpeed = 11f;
        public float CounterRotationSpeed = -24f;
        [Min(0f)] public float CorePulseSpeed = 4.5f;
        [Range(0f, 1f)] public float CorePulseAmount = 0.1f;
        [Min(1f)] public float CoreChargeScaleMultiplier = 1.7f;
        [ColorUsage(false)] public Color BodyColor = new Color(0.012f, 0.055f, 0.024f);
        [ColorUsage(false, true)] public Color BodyEmission = new Color(0.025f, 0.22f, 0.075f);
        [ColorUsage(false)] public Color CoreColor = new Color(0.018f, 0.11f, 0.045f);
        [ColorUsage(false, true)] public Color CoreEmission = new Color(0.12f, 1.65f, 0.48f);
        [ColorUsage(false)] public Color LightningColor = new Color(0.55f, 0.86f, 1f);
        [ColorUsage(false, true)] public Color LightningEmission = new Color(7.5f, 12f, 18f);
        [ColorUsage(false)] public Color WarningColor = new Color(0.18f, 0.42f, 0.62f);
        [ColorUsage(false, true)] public Color WarningEmission = new Color(0.45f, 2.8f, 5.8f);

        public float EvaluateStrikeRadius(int risk)
        {
            float riskProgress = Mathf.Clamp01(
                risk / (float)Mathf.Max(1, StrikeRadiusRiskCeiling));
            return Mathf.Lerp(StrikeRadius, StrikeRadiusAtRiskCeiling, riskProgress);
        }
    }

    [System.Serializable]
    public sealed class PlayerStrikeOrbTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 110f;
        [Min(0)] public int GoldReward = 55;

        [Header("Spawning")]
        [Range(1, 10)] public int EnemyCount = 2;
        [Min(20f)] public float MinimumSpawnDistance = 120f;
        [Min(20f)] public float MaximumSpawnDistance = 240f;
        [Min(50f)] public float RepositionDistance = 390f;

        [Header("High-Altitude Patrol")]
        [Min(10f)] public float HoverHeight = 68f;
        [Min(0f)] public float HoverHeightVariance = 14f;
        [Min(0f)] public float PatrolDriftRange = 20f;
        [Min(0f)] public float PatrolDriftSpeed = 5f;

        [Header("Airborne Player Targeting")]
        [Min(1f)] public float DetectionRange = 155f;
        [Tooltip("The drone must be at least this far above the dune surface before this enemy can attack it.")]
        [Min(0f)] public float MinimumTargetHeightAboveGround = 3f;
        [Tooltip("Time spent visibly following the airborne drone before locking the predicted strike point.")]
        [Min(0f)] public float TrackingDuration = 0.55f;
        [Tooltip("Multiplier applied to the exact time remaining until impact when predicting the drone's future position. A value of 1 aims at a constant-velocity intercept.")]
        [Min(0f)] public float PredictionTimeMultiplier = 1f;
        [Tooltip("Maximum distance the predicted strike point can lead ahead of the drone.")]
        [Min(0f)] public float MaximumPredictionDistance = 55f;

        [Header("Player Lightning Strike")]
        [Min(0.1f)] public float AttackInterval = 5.25f;
        [Range(0f, 1f)] public float MinimumInitialAttackDelayMultiplier = 0.35f;
        [Min(0.1f)] public float ChargeTime = 1.15f;
        [Min(0f)] public float Cooldown = 2.5f;
        [Min(0f)] public float LightningDamage = 34f;
        public string LightningDeathMessage = "Struck by Strike Orb lightning.";
        [Min(0.1f)] public float StrikeRadius = 4.25f;
        [Min(0.05f)] public float LightningVisualDuration = 0.32f;
        [Min(0.01f)] public float ChargeTelegraphWidth = 0.14f;
        [Min(0.01f)] public float LightningWidth = 0.52f;
        [Min(0f)] public float ChargePulseSpeed = 12f;
        [Range(0f, 0.5f)] public float ChargePulseAmount = 0.12f;
        [Min(0.01f)] public float ChargeMarkerStartScale = 0.25f;
        [Min(0.01f)] public float ChargeHaloStartScale = 0.35f;
        [Min(0.01f)] public float ChargeHaloEndScale = 1.15f;
        [Min(0f)] public float ImpactFlashScaleMultiplier = 0.34f;
        [Min(0f)] public float MinimumLightningJitter = 0.35f;
        [Min(0f)] public float MaximumLightningJitter = 2.2f;
        [Min(0f)] public float LightningJitterPerMeter = 0.022f;
        [Range(0.1f, 1f)] public float LightningEndWidthMultiplier = 0.65f;

        [Header("Fly-Through Destruction")]
        [Tooltip("Fraction of the visible ring opening that counts as flying through its center.")]
        [Range(0.1f, 1f)] public float FlyThroughRadiusMultiplier = 0.78f;
        [Min(0.05f)] public float FlyThroughExplosionDuration = 0.7f;
        [Min(0.1f)] public float FlyThroughFlashStartScale = 1.5f;
        [Min(0.1f)] public float FlyThroughFlashMaximumScale = 24f;
        [Range(0.05f, 0.95f)] public float FlyThroughFlashPeakTime = 0.28f;
        [Range(1, 8)] public int FlyThroughShockwaveCount = 3;
        [Min(0.01f)] public float FlyThroughShockwaveThickness = 0.16f;
        [Min(0.1f)] public float FlyThroughShockwaveStartRadius = 1.5f;
        [Min(0.1f)] public float FlyThroughShockwaveEndRadius = 27f;
        [Min(0f)] public float FlyThroughExplosionLightIntensity = 85000f;
        [Min(0f)] public float FlyThroughExplosionLightRange = 48f;
        [ColorUsage(false)] public Color FlyThroughExplosionWhiteColor = Color.white;
        [ColorUsage(false, true)] public Color FlyThroughExplosionWhiteEmission = new Color(18f, 22f, 28f);
        [ColorUsage(false)] public Color FlyThroughExplosionBlueColor = new Color(0.16f, 0.62f, 1f);
        [ColorUsage(false, true)] public Color FlyThroughExplosionBlueEmission = new Color(2.5f, 12f, 28f);

        [Header("Presentation")]
        [Min(0.1f)] public float VisualScale = 2.35f;
        [Min(0.1f)] public float RingRadius = 1.55f;
        [Min(0.01f)] public float RingThickness = 0.2f;
        [Min(0.01f)] public float InnerRingThickness = 0.055f;
        [Min(0.05f)] public float OrbitingOrbRadius = 0.3f;
        [Min(0.1f)] public float OrbitRadius = 2.2f;
        public float FirstOrbOrbitSpeed = 58f;
        public float SecondOrbOrbitSpeed = -91f;
        [Range(0f, 360f)] public float FirstOrbStartAngle = 35f;
        [Range(0f, 360f)] public float SecondOrbStartAngle = 215f;
        [Range(-85f, 85f)] public float FirstOrbOrbitTilt = 24f;
        [Range(-85f, 85f)] public float SecondOrbOrbitTilt = -38f;
        [Min(0.1f)] public float ChargeHaloRadius = 1.9f;
        [Min(0.01f)] public float ChargeHaloThickness = 0.075f;
        [Min(0f)] public float RingRotationSpeed = 18f;
        [Min(0f)] public float FacingSharpness = 9f;
        [ColorUsage(false)] public Color BodyColor = new Color(0.018f, 0.028f, 0.07f);
        [ColorUsage(false, true)] public Color BodyEmission = new Color(0.08f, 0.18f, 0.8f);
        [ColorUsage(false)] public Color OrbColor = new Color(0.08f, 0.3f, 0.48f);
        [ColorUsage(false, true)] public Color OrbEmission = new Color(0.35f, 3.5f, 6.8f);
    }

    [System.Serializable]
    public sealed class GroundExploderTuning
    {
        public bool Enabled = true;
        [Min(1f)] public float MaximumHealth = 70f;
        [Min(0)] public int GoldReward = 15;
        [Tooltip("Expected number of ground exploders generated in each streamed desert chunk.")]
        [Min(0f)] public float DensityPerChunk = 0.28f;
        [Header("Patrol")]
        [Min(0f)] public float MovementSpeed = 5.5f;
        [Min(2f)] public float PatrolRadius = 18f;
        [Range(0f, 60f)] public float MaximumGroundSlope = 34f;
        [Header("Proximity Explosion")]
        [Min(0.5f)] public float DetectionRadius = 18f;
        [Min(0.1f)] public float WindUpDuration = 1.25f;
        [Tooltip("Explosion radius at risk 0.")]
        [Min(0.5f)] public float ExplosionRadius = 11f;
        [Tooltip("Explosion radius at the risk scaling ceiling.")]
        [Min(0.5f)] public float ExplosionRadiusAtRiskCeiling = 18.3f;
        [Min(0f)] public float MaximumDamage = 65f;
        public string ExplosionDeathMessage = "Destroyed by a Ground Exploder blast.";
        [Header("Presentation")]
        [Tooltip("Visual scale at risk 0.")]
        [Min(0.1f)] public float VisualScale = 3f;
        [Tooltip("Visual scale at the risk scaling ceiling.")]
        [Min(0.1f)] public float VisualScaleAtRiskCeiling = 5f;
        [Tooltip("Risk at which visual scale and explosion radius reach their ceiling values.")]
        [Min(1)] public int RiskScalingCeiling = 20;

        public float EvaluateExplosionRadius(int risk)
        {
            return Mathf.Lerp(ExplosionRadius, ExplosionRadiusAtRiskCeiling, EvaluateRisk(risk));
        }

        public float EvaluateVisualScale(int risk)
        {
            return Mathf.Lerp(VisualScale, VisualScaleAtRiskCeiling, EvaluateRisk(risk));
        }

        private float EvaluateRisk(int risk)
        {
            return Mathf.Clamp01(risk / (float)Mathf.Max(1, RiskScalingCeiling));
        }
    }

    [System.Serializable]
    public sealed class RingTuning
    {
        [Header("Starting Size")]
        [Min(0.75f)] public float GroundRingRadius = 3.25f;
        [Min(0.75f)] public float FlightRingRadius = 3.55f;

        [Header("Blue Flight Ring Generation")]
        [Tooltip("Multiplier for the expected number of procedurally generated blue flight rings. One preserves the base amount; values above one add blue rings without adding boost rings.")]
        [Min(1f)] public float FlightRingAmountMultiplier = 1f;

        [Header("Boost and Flight Ring Appearance")]
        [ColorUsage(false, true)] public Color BoostRingBaseColor = new Color(0.42f, 0.09f, 0.008f);
        [ColorUsage(false, true)] public Color BoostRingEmissionColor = new Color(3.6f, 0.72f, 0.025f);
        [ColorUsage(false, true)] public Color FlightRingBaseColor = new Color(0.004f, 0.19f, 0.32f);
        [ColorUsage(false, true)] public Color FlightRingEmissionColor = new Color(0f, 2f, 3.6f);

        [Header("Upper Flight Ring Unlock")]
        [Tooltip("Number of distinct blue flight rings the player must cross before the upper-layer ring appears.")]
        [Min(1)] public int UpperFlightRingRequiredPasses = 5;

        [Header("Upper Flight Ring Generation")]
        [Tooltip("Independent procedural salt used for upper-layer positions, altitudes, rotations, and movement.")]
        public int UpperFlightRingSeedOffset = 19031;
        [Min(0.75f)] public float UpperFlightRingRadius = 5f;
        [Min(0f)] public float UpperFlightRingMinimumHeight = 45f;
        [Min(0f)] public float UpperFlightRingMaximumHeight = 70f;

        [Header("Upper Flight Ring Appearance")]
        [ColorUsage(false, true)] public Color UpperFlightRingBaseColor = new Color(0.24f, 0.015f, 0.42f);
        [ColorUsage(false, true)] public Color UpperFlightRingEmissionColor = new Color(4.8f, 0.08f, 8f);
        [Min(1f)] public float UpperFlightRingActiveScale = 3f;
        [Min(0f)] public float UpperFlightRingScaleSharpness = 4.5f;
        [Min(0f)] public float UpperFlightRingRotationSpeed = 56f;

        [Header("Upper Flight Ring Motion and Speed")]
        [Min(0f)] public float UpperFlightModeMinimumHeightOffset;
        [Min(0f)] public float UpperFlightModeMaximumHeightOffset = 18f;
        [Min(0f)] public float UpperFlightModeHeightSharpness = 3f;
        [Tooltip("Multiplier applied to normal and maximum flight speed after crossing an upper-layer ring. A blue ring resets it to one.")]
        [Min(1f)] public float UpperFlightSpeedMultiplier = 1.6f;

        [Header("Upper Flight Ring HUD")]
        public bool ShowUpperFlightRingHud = true;
        public string UpperFlightRingHudTitle = "UPPER FLIGHT LAYER";
        public string UpperFlightRingHudProgressLabel = "BLUE RINGS";
        public string UpperFlightRingHudUnlockedLabel = "UPPER RING UNLOCKED";
        [Min(0f)]
        [Tooltip("Seconds the HUD remains visible after the upper flight layer unlocks. Uses unscaled time.")]
        public float UpperFlightRingHudUnlockedDuration = 5f;
        [Min(240f)] public float UpperFlightRingHudReferenceHeight = 1080f;
        [Range(0.25f, 2f)] public float UpperFlightRingHudMinimumScale = 0.65f;
        [Range(0.25f, 2f)] public float UpperFlightRingHudMaximumScale = 1.25f;
        [Min(0f)] public float UpperFlightRingHudTopMargin = 28f;
        [Min(0f)] public float UpperFlightRingHudRightMargin = 28f;
        [Tooltip("Minimum vertical gap between the gold panel and the upper-flight-layer tracker.")]
        [Min(0f)] public float UpperFlightRingHudGoldGap = 14f;
        [Min(160f)] public float UpperFlightRingHudWidth = 310f;
        [Min(60f)] public float UpperFlightRingHudHeight = 92f;
        [Min(0f)] public float UpperFlightRingHudPadding = 14f;
        [Min(1f)] public float UpperFlightRingHudAccentWidth = 5f;
        [Min(1f)] public float UpperFlightRingHudProgressBarHeight = 8f;
        [Min(8)] public int UpperFlightRingHudTitleFontSize = 13;
        [Min(8)] public int UpperFlightRingHudStatusFontSize = 17;
        [ColorUsage(false)] public Color UpperFlightRingHudPanelColor = new Color(0.025f, 0.07f, 0.11f, 0.9f);
        [ColorUsage(false)] public Color UpperFlightRingHudAccentColor = new Color(0f, 0.82f, 1f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudTrackColor = new Color(0.12f, 0.24f, 0.3f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudTitleColor = new Color(0.55f, 0.78f, 0.86f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudStatusColor = new Color(0.88f, 0.97f, 1f, 1f);
        [ColorUsage(false)] public Color UpperFlightRingHudUnlockedColor = new Color(0.35f, 1f, 0.7f, 1f);

        [Header("Health Rings")]
        [Tooltip("Expected health-ring count per streamed terrain chunk. Values well below one keep pickups scarce.")]
        [Range(0f, 1f)] public float HealthRingDensityPerChunk = 0.035f;
        [Min(0.75f)] public float HealthRingRadius = 4.2f;
        [Min(0f)] public float HealthRingMinimumHeight = 4f;
        [Min(0f)] public float HealthRingMaximumHeight = 10f;
        [Min(0f)] public float HealthRestored = 35f;
        [Tooltip("Target size of the imported heartpiece model at the center of a health ring.")]
        [Min(0.1f)] public float HealthHeartScale = 2.4f;
        public Vector3 HealthHeartOffset;
        public Vector3 HealthHeartEulerAngles;
        [Tooltip("Rotation speed around the health ring's local Y axis after its XZ plane billboards toward the camera.")]
        [Min(0f)] public float HealthRingRotationSpeed = 24f;

        [Header("Health Ring Appearance")]
        [ColorUsage(false, true)] public Color HealthRingBaseColor = new Color(0.48f, 0.015f, 0.055f);
        [ColorUsage(false, true)] public Color HealthRingEmissionColor = new Color(4.8f, 0.06f, 0.24f);
        [ColorUsage(false, true)] public Color HealthHeartBaseColor = new Color(0.8f, 0.035f, 0.09f);
        [ColorUsage(false, true)] public Color HealthHeartEmissionColor = new Color(8f, 0.12f, 0.42f);
        [Range(0f, 1f)] public float HealthMaterialSmoothness = 0.72f;
        [Range(0f, 1f)] public float HealthMaterialMetallic = 0.22f;

        [Header("Health Pickup Feedback")]
        [Min(0.1f)] public float HealthPickupFeedbackDuration = 1.4f;
        [Min(8)] public int HealthPickupFeedbackFontSize = 28;
        [Min(0f)] public float HealthPickupFeedbackTop = 170f;
        [Min(24f)] public float HealthPickupFeedbackHeight = 48f;
        [ColorUsage(false)] public Color HealthPickupFeedbackColor = new Color(1f, 0.32f, 0.5f, 1f);

        [Header("Coin Rings")]
        [Tooltip("Expected coin-ring count per streamed terrain chunk.")]
        [Range(0f, 1f)] public float CoinRingDensityPerChunk = 0.12f;
        [Min(0.75f)] public float CoinRingRadius = 4.2f;
        [Min(0f)] public float CoinRingMinimumHeight = 4f;
        [Min(0f)] public float CoinRingMaximumHeight = 10f;
        [Min(1)] public int GoldReward = 25;
        [Min(0)] public int StartingGold;
        [Tooltip("Target size of the imported coin model at the center of a coin ring.")]
        [Min(0.1f)] public float CoinModelScale = 2.4f;
        public Vector3 CoinModelOffset;
        public Vector3 CoinModelEulerAngles;
        [Min(0f)] public float CoinRingRotationSpeed = 24f;

        [Header("Coin Ring Appearance")]
        [ColorUsage(false, true)] public Color CoinRingBaseColor = new Color(0.64f, 0.3f, 0.015f);
        [ColorUsage(false, true)] public Color CoinRingEmissionColor = new Color(6.5f, 2.2f, 0.05f);
        [ColorUsage(false, true)] public Color CoinBaseColor = new Color(0.95f, 0.58f, 0.04f);
        [ColorUsage(false, true)] public Color CoinEmissionColor = new Color(8f, 3.2f, 0.08f);
        [Range(0f, 1f)] public float CoinMaterialSmoothness = 0.82f;
        [Range(0f, 1f)] public float CoinMaterialMetallic = 0.72f;

        [Header("Gold HUD and Pickup Feedback")]
        [Min(0f)] public float GoldHudRightMargin = 28f;
        [Min(0f)] public float GoldHudTopMargin = 28f;
        [Min(100f)] public float GoldHudWidth = 180f;
        [Min(30f)] public float GoldHudHeight = 48f;
        [Min(8)] public int GoldHudFontSize = 18;
        [Min(0.1f)] public float GoldPickupFeedbackDuration = 1.4f;
        [Min(8)] public int GoldPickupFeedbackFontSize = 28;
        [Min(0f)] public float GoldPickupFeedbackTop = 118f;
        [Min(24f)] public float GoldPickupFeedbackHeight = 48f;
        [ColorUsage(false)] public Color GoldHudPanelColor = new Color(0.08f, 0.045f, 0.01f, 0.9f);
        [ColorUsage(false)] public Color GoldHudTextColor = new Color(1f, 0.75f, 0.2f, 1f);
        [ColorUsage(false)] public Color GoldPickupFeedbackColor = new Color(1f, 0.82f, 0.22f, 1f);

        [Header("Height Above Ground")]
        [Min(0f)] public float GroundRingMinimumHeight = 1.75f;
        [Min(0f)] public float GroundRingMaximumHeight = 3.25f;
        [Min(0f)] public float FlightRingMinimumHeight = 5f;
        [Min(0f)] public float FlightRingMaximumHeight = 8f;

        [Header("Active Size")]
        [Min(1f)] public float BoostRingActiveScale = 1.45f;
        [InspectorName("Flight Ring Active Scale")]
        [Min(1f)] public float FlightModeScale = 1.45f;
        [Min(0f)] public float ScaleSharpness = 4.5f;

        [Header("Rotation")]
        [Tooltip("Clockwise visual rotation speed for both yellow boost rings and blue flight rings, in degrees per second.")]
        [Min(0f)] public float ClockwiseRotationSpeed = 32f;

        [Header("Flight Mode Height Offset")]
        [Min(0f)] public float FlightModeMinimumHeightOffset;
        [Min(0f)] public float FlightModeMaximumHeightOffset;
        [Min(0f)] public float FlightModeHeightSharpness;
    }

    [System.Serializable]
    public sealed class DesertWeatherCycleTuning
    {
        [Header("Storm Frequency")]
        [Tooltip("Clear time before the first storm begins, in seconds.")]
        [Min(0f)] public float InitialClearDuration = 35f;
        [Tooltip("Minimum clear interval between completed storms, in seconds.")]
        [Min(1f)] public float MinimumClearDuration = 90f;
        [Tooltip("Maximum clear interval between completed storms, in seconds.")]
        [Min(1f)] public float MaximumClearDuration = 180f;

        [Header("Storm Progression")]
        [Min(0.1f)] public float DustBuildingDuration = 12f;
        [Tooltip("Storm intensity reached at the end of the initial dust-building phase.")]
        [Range(0f, 1f)] public float DustBuildingIntensity = 0.28f;
        [Min(0.1f)] public float ApproachingStormDuration = 18f;
        [Min(1f)] public float MinimumFullStormDuration = 35f;
        [Min(1f)] public float MaximumFullStormDuration = 60f;
        [Min(0.1f)] public float FadingDuration = 18f;
        [Range(0f, 1f)] public float MaximumStormIntensity = 0.85f;
        public int RandomSeedOffset = 6317;
    }

    [System.Serializable]
    public sealed class DesertWeatherWindTuning
    {
        [Tooltip("Global wind direction on the world X/Z plane.")]
        public Vector2 Direction = new Vector2(1f, 0.18f);
        [Min(0f)] public float ClearWindSpeed = 5.5f;
        [Min(0f)] public float StormWindSpeed = 24f;
        [Min(0f)] public float WindZoneStrengthMultiplier = 0.08f;
        [Min(0f)] public float ClearTurbulence = 0.12f;
        [Min(0f)] public float StormTurbulence = 0.72f;
        [Tooltip("How strongly the drone's velocity changes the apparent speed of nearby sand.")]
        [Range(0f, 1.5f)] public float PlayerVelocityInfluence = 0.65f;
    }

    [System.Serializable]
    public sealed class DesertWeatherAtmosphereTuning
    {
        [Header("Visibility")]
        [Min(10f)] public float ClearVisibilityDistance = 330f;
        [Min(10f)] public float StormVisibilityDistance = 72f;
        [Min(20f)] public float ClearMaximumFogDistance = 780f;
        [Min(20f)] public float StormMaximumFogDistance = 260f;
        public float FogBaseHeight = -12f;
        [Min(1f)] public float ClearFogHeight = 85f;
        [Min(1f)] public float StormFogHeight = 160f;
        [Range(0f, 1f)] public float VolumetricFogThreshold = 0.08f;

        [Header("Y2K Sky Gradient & Exposure")]
        [ColorUsage(false, true)] public Color ClearSkyTop = new Color(0.018f, 0.24f, 1.65f);
        [ColorUsage(false, true)] public Color ClearSkyMiddle = new Color(0.04f, 0.72f, 2.15f);
        [ColorUsage(false, true)] public Color ClearSkyBottom = new Color(0.42f, 2.25f, 3.2f);
        [ColorUsage(false, true)] public Color StormSkyTop = new Color(0.025f, 0.105f, 0.32f);
        [ColorUsage(false, true)] public Color StormSkyMiddle = new Color(0.045f, 0.22f, 0.48f);
        [ColorUsage(false, true)] public Color StormSkyBottom = new Color(0.18f, 0.52f, 0.72f);
        [Min(0f)] public float SkyGradientDiffusion = 1.48f;
        [Min(0f)] public float SkyMultiplier = 0.82f;
        public float ClearExposure = 2f;
        public float StormExposure = 1.35f;

        [Header("Y2K Horizon Glow")]
        [ColorUsage(false, true)] public Color ClearHorizonGlowColor = new Color(0.38f, 2.8f, 4.4f, 1f);
        [ColorUsage(false, true)] public Color StormHorizonGlowColor = new Color(0.08f, 0.42f, 0.68f, 1f);
        [Range(0.01f, 0.5f)] public float HorizonGlowSize = 0.14f;
        [Min(0f)] public float ClearHorizonGlowIntensity = 0.72f;
        [Min(0f)] public float StormHorizonGlowIntensity = 0.18f;

        [Header("Y2K Sky Clouds")]
        [ColorUsage(false, true)] public Color ClearSkyCloudColor = new Color(1.1f, 1.75f, 2.05f, 1f);
        [ColorUsage(false, true)] public Color ClearSkyCloudHighlight = new Color(1.8f, 2.7f, 3.1f, 1f);
        [ColorUsage(false, true)] public Color ClearSkyCloudPearl = new Color(0.62f, 1.8f, 2.4f, 1f);
        [ColorUsage(false, true)] public Color StormSkyCloudColor = new Color(0.18f, 0.36f, 0.52f, 1f);
        [ColorUsage(false, true)] public Color StormSkyCloudHighlight = new Color(0.36f, 0.62f, 0.78f, 1f);
        [ColorUsage(false, true)] public Color StormSkyCloudPearl = new Color(0.16f, 0.42f, 0.64f, 1f);
        [Range(0f, 1f)] public float ClearSkyCloudOpacity = 0.82f;
        [Range(0f, 1f)] public float StormSkyCloudOpacity = 0.24f;
        [Range(0.05f, 0.8f)] public float SkyCloudAltitude = 0.28f;
        [Range(0.03f, 0.5f)] public float SkyCloudThickness = 0.2f;
        [Min(0.1f)] public float SkyCloudScale = 3.8f;
        [Range(0.005f, 0.25f)] public float SkyCloudSoftness = 0.075f;
        [Range(0f, 2f)] public float SkyCloudHighlightStrength = 0.62f;
        [Range(0f, 2f)] public float SkyCloudPearlStrength = 0.24f;
        [Min(0f)] public float SkyCloudDriftSpeed = 0.012f;

        [Header("Y2K Digital Structures")]
        [ColorUsage(false, true)] public Color DigitalStructureColor = new Color(0.32f, 1.9f, 3.1f, 1f);
        [Range(0f, 1f)] public float ClearDigitalStructureOpacity = 0.105f;
        [Range(0f, 1f)] public float StormDigitalStructureOpacity = 0.018f;
        [Range(0.02f, 0.65f)] public float DigitalArcAltitude = 0.2f;
        [Range(0f, 1f)] public float DigitalArcCurvature = 0.32f;
        [Range(0.001f, 0.05f)] public float DigitalArcThickness = 0.006f;
        [Range(1f, 12f)] public float DigitalArcFrequency = 3f;
        [Range(0.02f, 0.6f)] public float DigitalRingAltitude = 0.12f;
        [Range(0.01f, 0.3f)] public float DigitalRingSpacing = 0.075f;
        [Range(0.001f, 0.04f)] public float DigitalRingThickness = 0.0035f;
        [Range(0f, 1f)] public float DigitalGridOpacity = 0.42f;
        [Range(2f, 40f)] public float DigitalGridScale = 14f;
        [Range(0.02f, 0.35f)] public float DigitalGridHeight = 0.11f;
        [Range(0.001f, 0.08f)] public float DigitalGridLineThickness = 0.018f;

        [Header("Bloom Integration")]
        [Min(0f)] public float BloomIntensity = 0.2f;
        [Min(0f)] public float BloomThreshold = 1.05f;
        [Range(0f, 1f)] public float BloomScatter = 0.62f;
    }

    [System.Serializable]
    public sealed class DesertWeatherDustTuning
    {
        [Header("Density & Coverage")]
        [Range(0f, 1f)] public float AmbientDustDensity = 0.18f;
        [Range(0f, 1f)] public float AmbientAirborneSandDensity = 0.75f;
        [Range(0f, 1.5f)] public float StormDustDensity = 0.95f;
        [Min(10f)] public float FieldRadius = 80f;
        [Min(1f)] public float GroundLayerHeight = 6f;
        [Min(1f)] public float AirborneLayerHeight = 32f;
        [Min(4f)] public float CloseLayerRadius = 22f;

        [Header("Particle Shape")]
        [Min(0.01f)] public float MinimumParticleSize = 0.05f;
        [Min(0.01f)] public float MaximumParticleSize = 0.22f;
        [Min(0.1f)] public float MinimumParticleLifetime = 2.2f;
        [Min(0.1f)] public float MaximumParticleLifetime = 5.5f;
        [Min(0f)] public float TurbulenceStrength = 1.8f;
        [Min(0.01f)] public float TurbulenceFrequency = 0.22f;
        [Min(0f)] public float SandStreakLength = 3.8f;
        [Min(0f)] public float ParticleVelocityStretch = 0.06f;
        [Min(0f)] public float CameraVelocityStretch = 0.12f;
        [ColorUsage(false)] public Color AmbientDustColor = new Color(0.9f, 0.61f, 0.3f, 0.26f);
        [ColorUsage(false)] public Color StormDustColor = new Color(0.88f, 0.47f, 0.16f, 0.58f);

        [Header("Layer Motion & Placement")]
        [Min(0f)] public float GroundWindResponse = 0.62f;
        [Min(0f)] public float AirborneWindResponse = 0.9f;
        [Min(0f)] public float CloseWindResponse = 1.12f;
        [Min(0f)] public float ApproachingFrontStartDistance = 1.55f;
        [Min(0f)] public float ApproachingFrontEndDistance = 0.22f;
        [Range(0f, 1.5f)] public float FullStormFrontDensity = 0.55f;

        [Header("Performance Budgets")]
        [Range(32, 1000)] public int GroundParticleBudget = 240;
        [Range(32, 1200)] public int AirborneParticleBudget = 420;
        [Range(32, 1200)] public int ApproachingFrontParticleBudget = 350;
        [Range(32, 800)] public int CloseParticleBudget = 220;
        [Min(0f)] public float GroundEmissionRate = 50f;
        [Min(0f)] public float AirborneEmissionRate = 110f;
        [Min(0f)] public float ApproachingFrontEmissionRate = 90f;
        [Min(0f)] public float CloseEmissionRate = 70f;
    }

    [System.Serializable]
    public sealed class DesertWeatherTuning
    {
        public bool Enabled = true;
        public DesertWeatherCycleTuning Cycle = new DesertWeatherCycleTuning();
        public DesertWeatherWindTuning Wind = new DesertWeatherWindTuning();
        public DesertWeatherAtmosphereTuning Atmosphere = new DesertWeatherAtmosphereTuning();
        public DesertWeatherDustTuning Dust = new DesertWeatherDustTuning();

        public void EnsureInitialized()
        {
            Cycle ??= new DesertWeatherCycleTuning();
            Wind ??= new DesertWeatherWindTuning();
            Atmosphere ??= new DesertWeatherAtmosphereTuning();
            Dust ??= new DesertWeatherDustTuning();
        }
    }

    [System.Serializable]
    public sealed class ElectricalStormVisualTuning
    {
        public bool Enabled = true;

        [Header("Storm Presence & Severity")]
        [Range(0f, 1f)] public float VisualActivationIntensity = 0.08f;
        [Range(0f, 1f)] public float FullVisualIntensity = 0.78f;
        [Min(0f)] public float VisualBlendSharpness = 3.5f;
        [Range(0f, 1f)] public float SevereVisualThreshold = 0.58f;
        [Range(0f, 1f)] public float ExtremeVisualThreshold = 0.88f;

        [Header("Regional Stormfront")]
        public Vector2 StormfrontDirection = new Vector2(-1f, 0.2f);
        [Min(0f)] public float StormfrontFarDistance = 460f;
        [Min(0f)] public float StormfrontNearDistance = 36f;
        [Min(1f)] public float StormfrontWidth = 390f;
        [Min(1f)] public float StormfrontHeight = 165f;
        [Min(1f)] public float StormfrontDepth = 115f;
        [Min(0f)] public float StormfrontBaseHeight = 22f;

        [Header("High-Rank Player Pursuit")]
        [Range(1, 20)] public int PlayerFollowStartRank = 8;
        [Range(1, 20)] public int PlayerFollowEndRank = 20;
        [Min(0.01f)] public float PlayerFollowSpeedAtStartRank = 8f;
        [Min(0.01f)] public float PlayerFollowSpeedAtEndRank = 80f;

        [Header("Supercell Shelf")]
        [Range(4, 48)] public int StormShelfLobeCount = 20;
        [Range(0.1f, 1.5f)] public float StormShelfWidthFraction = 1.08f;
        [Range(0f, 1f)] public float StormShelfHeightFraction = 0.2f;
        [Range(0.1f, 1f)] public float StormShelfDepthFraction = 0.76f;
        [Min(0f)] public float StormShelfVerticalVariation = 7f;
        [Min(1f)] public float StormShelfMinimumLobeWidth = 44f;
        [Min(1f)] public float StormShelfMaximumLobeWidth = 76f;
        [Min(1f)] public float StormShelfMinimumThickness = 18f;
        [Min(1f)] public float StormShelfMaximumThickness = 34f;
        [Min(1f)] public float StormShelfMinimumDepth = 42f;
        [Min(1f)] public float StormShelfMaximumDepth = 82f;
        [Range(0.1f, 1f)] public float StormShelfEdgeScale = 0.62f;
        [Min(0f)] public float StormShelfMotionMultiplier = 0.5f;
        public float StormShelfRotationSpeed = 0.65f;

        [Header("Cumulonimbus Towers")]
        [Range(1, 8)] public int StormTowerCount = 4;
        [Range(2, 10)] public int StormTowerTierCount = 5;
        [Range(-0.5f, 0.5f)] public float PrimaryTowerHorizontalOffset = -0.12f;
        [Min(1f)] public float PrimaryTowerHeight = 185f;
        [Min(1f)] public float PrimaryTowerWidth = 92f;
        [Min(1f)] public float SecondaryTowerMinimumHeight = 92f;
        [Min(1f)] public float SecondaryTowerMaximumHeight = 154f;
        [Min(1f)] public float SecondaryTowerMinimumWidth = 48f;
        [Min(1f)] public float SecondaryTowerMaximumWidth = 76f;
        [Range(0f, 0.5f)] public float StormTowerHorizontalSpread = 0.4f;
        [Range(0f, 0.5f)] public float StormTowerDepthSpread = 0.28f;
        [Range(0.1f, 1f)] public float StormTowerTopScale = 0.48f;
        [Range(0f, 1f)] public float StormTowerTierOffset = 0.24f;
        [Range(0f, 1f)] public float StormTowerDepthVariation = 0.38f;
        [Min(0.1f)] public float StormTowerVerticalOverlap = 1.72f;
        [Min(0.1f)] public float StormTowerMinimumScaleVariation = 0.82f;
        [Min(0.1f)] public float StormTowerMaximumScaleVariation = 1.18f;
        [Min(0.1f)] public float StormTowerMinimumDepthScale = 0.72f;
        [Min(0.1f)] public float StormTowerMaximumDepthScale = 1.08f;
        [Min(0f)] public float StormTowerBottomMotionMultiplier = 0.35f;
        [Min(0f)] public float StormTowerTopMotionMultiplier = 0.75f;
        public float StormTowerRotationSpeed = 0.18f;
        [Range(0f, 1f)] public float StormUpperColorThreshold = 0.55f;

        [Header("Supporting Masses & Scud")]
        [Range(0, 48)] public int StormSupportLobeCount = 18;
        [Range(0f, 1f)] public float StormSupportMinimumHeight = 0.16f;
        [Range(0f, 1f)] public float StormSupportMaximumHeight = 0.68f;
        [Range(0f, 0.5f)] public float StormSupportHorizontalSpread = 0.5f;
        [Range(0f, 0.5f)] public float StormSupportDepthSpread = 0.5f;
        [Min(0f)] public float StormSupportMotionMultiplier = 0.8f;
        public float StormSupportRotationSpeed = -0.12f;
        public Vector3 StormCloudMinimumScale = new Vector3(42f, 24f, 34f);
        public Vector3 StormCloudMaximumScale = new Vector3(92f, 64f, 72f);
        [Range(0, 32)] public int StormScudLobeCount = 12;
        public float StormScudMinimumHeight = -18f;
        public float StormScudMaximumHeight = 18f;
        [Range(0f, 0.5f)] public float StormScudHorizontalSpread = 0.46f;
        [Range(0f, 0.5f)] public float StormScudDepthSpread = 0.5f;
        public Vector3 StormScudMinimumScale = new Vector3(15f, 7f, 12f);
        public Vector3 StormScudMaximumScale = new Vector3(38f, 18f, 30f);
        [Min(0f)] public float StormScudMotionMultiplier = 1.45f;
        public float StormScudRotationSpeed = 1.35f;

        [Header("Cloud Mesh Families & Motion")]
        [Range(1, 8)] public int StormCloudMeshFamilyCount = 4;
        [Range(6, 24)] public int StormCloudLongitudeSegments = 10;
        [Range(4, 16)] public int StormCloudLatitudeSegments = 7;
        [Range(0f, 0.45f)] public float StormCloudSurfaceVariation = 0.16f;
        [Range(0f, 0.35f)] public float StormCloudBroadVariation = 0.1f;
        [Min(0.1f)] public float StormCloudSurfaceFrequency = 3f;
        [Min(0.1f)] public float StormCloudVerticalFrequency = 2f;
        [Range(0f, 45f)] public float StormCloudMaximumTilt = 18f;
        [Range(0f, 1f)] public float StormCloudVerticalDriftRatio = 0.35f;
        [Min(0f)] public float StormCloudRollAmount = 3.5f;
        [Min(0f)] public float StormCloudRollSpeed = 0.16f;
        [Min(0f)] public float StormCloudRockAngle = 1.8f;

        [Header("Cloud Lighting & Value Layers")]
        [ColorUsage(false)] public Color StormCloudTopColor = new Color(0.24f, 0.29f, 0.36f, 1f);
        [ColorUsage(false)] public Color StormCloudMiddleColor = new Color(0.095f, 0.12f, 0.16f, 1f);
        [ColorUsage(false)] public Color StormCloudUndersideColor = new Color(0.032f, 0.045f, 0.065f, 1f);
        [ColorUsage(false)] public Color StormCloudScudColor = new Color(0.065f, 0.085f, 0.11f, 1f);
        [ColorUsage(false, true)] public Color StormCloudFlashEmission = new Color(1.7f, 3.8f, 6.5f, 1f);
        [Range(0f, 1f)] public float StormCloudSmoothness = 0.12f;

        [Header("Internal Lightning Rhythm")]
        [Min(0.1f)] public float InternalFlashMinimumInterval = 1.8f;
        [Min(0.1f)] public float InternalFlashMaximumInterval = 5.5f;
        [Min(0.01f)] public float InternalFlashDuration = 0.24f;
        [Min(0f)] public float InternalFlashEmissionMultiplier = 2.4f;
        [Range(0, 8)] public int InternalFlashLightCount = 3;
        [Min(0f)] public float InternalFlashLightRange = 130f;
        [Min(0f)] public float InternalFlashLightIntensity = 1300f;
        [Range(0f, 1f)] public float InternalLightHorizontalSpread = 0.35f;
        [Range(0f, 1f)] public float InternalLightMinimumHeight = 0.2f;
        [Range(0f, 1f)] public float InternalLightMaximumHeight = 0.8f;
        [Min(0f)] public float InternalFlashMinimumFrequencyMultiplier = 0.65f;
        [Min(0f)] public float InternalFlashMaximumFrequencyMultiplier = 1.7f;
        [ColorUsage(false, true)] public Color InternalFlashLightColor = new Color(0.54f, 0.82f, 1f, 1f);

        [Header("Cloud-to-Cloud Electrical Arcs")]
        [Range(0f, 1f)] public float CloudArcActivationIntensity = 0.22f;
        [Min(0.1f)] public float CloudArcMinimumInterval = 2.4f;
        [Min(0.1f)] public float CloudArcMaximumInterval = 6.8f;
        [Min(0f)] public float CloudArcMinimumLength = 24f;
        [Min(0f)] public float CloudArcMaximumLength = 145f;
        [Range(1, 32)] public int CloudArcSelectionAttempts = 10;
        [Min(0.001f)] public float CloudArcWidth = 0.2f;
        [Min(0.01f)] public float CloudArcDuration = 0.16f;

        [Header("Charged Dust Veil")]
        [Range(0, 800)] public int ChargedDustParticleBudget = 360;
        [Min(0f)] public float ChargedDustEmissionRate = 90f;
        [Min(0.1f)] public float ChargedDustLifetime = 4.5f;
        [Min(0.01f)] public float ChargedDustMinimumSize = 0.05f;
        [Min(0.01f)] public float ChargedDustMaximumSize = 0.18f;
        [Min(1f)] public float ChargedDustRadius = 72f;
        [Min(1f)] public float ChargedDustHeight = 42f;
        public Vector3 ChargedDustVelocity = new Vector3(10f, 0.8f, 2f);
        [Min(0f)] public float ChargedDustTurbulence = 1.15f;
        [Min(0f)] public float ChargedDustLengthScale = 0.45f;
        [Min(0f)] public float ChargedDustVelocityStretch = 0.12f;
        [ColorUsage(false)] public Color ChargedDustColor = new Color(0.48f, 0.58f, 0.66f, 0.24f);

        [Header("Static Motes & Air Streaks")]
        [Range(0, 500)] public int StaticMoteParticleBudget = 180;
        [Min(0f)] public float StaticMoteEmissionRate = 32f;
        [Min(0.1f)] public float StaticMoteLifetime = 1.5f;
        [Min(0.01f)] public float StaticMoteMinimumSize = 0.025f;
        [Min(0.01f)] public float StaticMoteMaximumSize = 0.09f;
        [Min(0f)] public float StaticMoteSpeed = 18f;
        [Min(0f)] public float StaticMoteLength = 2.8f;
        [Min(0f)] public float StaticMoteVelocityStretch = 1f;
        [Min(1f)] public float StaticMoteRadius = 42f;
        [Min(1f)] public float StaticMoteHeight = 28f;
        [Min(1f)] public float ChargeBuildupParticleMultiplier = 2.2f;
        [Range(0f, 1f)] public float ParticleFadeInFraction = 0.18f;
        [Range(0f, 1f)] public float ParticleFadeOutFraction = 0.78f;
        [Range(16, 256)] public int ParticleTextureResolution = 64;
        [ColorUsage(false, true)] public Color StaticMoteColor = new Color(0.45f, 1.6f, 3.2f, 0.82f);

        [Header("Distant Probing Strikes")]
        [Min(0.1f)] public float ProbeMinimumInterval = 4.5f;
        [Min(0.1f)] public float ProbeMaximumInterval = 9f;
        [Min(0f)] public float ProbeMinimumDistance = 85f;
        [Min(0f)] public float ProbeMaximumDistance = 260f;
        [Min(1f)] public float ProbeOriginHeight = 125f;
        [Range(0f, 1f)] public float ProbeActivationIntensity = 0.12f;
        [Min(0f)] public float ProbeWidthMultiplier = 0.7f;
        [Min(0f)] public float ProbeMinimumFrequencyMultiplier = 0.7f;
        [Min(0f)] public float ProbeMaximumFrequencyMultiplier = 1.6f;

        [Header("Readable Strike Telegraph")]
        [Range(8, 96)] public int TargetMarkerSegments = 40;
        [Min(0f)] public float TargetMarkerStartRadius = 1.4f;
        [Min(0f)] public float TargetMarkerEndRadius = 5.2f;
        [Min(0.001f)] public float TargetMarkerWidth = 0.12f;
        [Min(0f)] public float TargetMarkerHeightOffset = 0.14f;
        [Min(0f)] public float AirTargetMarkerRadius = 2.8f;
        [Min(0f)] public float TargetPulseSpeed = 12f;
        [Range(0f, 1f)] public float TargetPulseAmount = 0.16f;
        [Min(1f)] public float ChargeColumnHeight = 68f;
        [Min(0.001f)] public float ChargeColumnStartWidth = 0.025f;
        [Min(0.001f)] public float ChargeColumnEndWidth = 0.22f;
        [Range(0f, 1f)] public float ChargeColumnTipWidthMultiplier = 0.35f;
        [Range(0, 240)] public int ConvergingSparkBudget = 90;
        [Min(0f)] public float ConvergingSparkEmissionRate = 48f;
        [Min(0.1f)] public float ConvergingSparkLifetime = 0.7f;
        [Min(0.01f)] public float ConvergingSparkSize = 0.075f;
        [Min(0f)] public float ConvergingSparkSpeed = 8f;
        [Range(0f, 1f)] public float ConvergingSparkInitialEmissionFraction = 0.35f;
        [ColorUsage(false, true)] public Color TelegraphColor = new Color(0.24f, 2.8f, 6.8f, 1f);

        [Header("Lightning Release")]
        [Range(4, 32)] public int LightningSegments = 15;
        [Min(0.001f)] public float LightningStartWidth = 0.52f;
        [Range(0.1f, 1f)] public float LightningEndWidthMultiplier = 0.42f;
        [Min(0f)] public float LightningMinimumJitter = 0.45f;
        [Min(0f)] public float LightningMaximumJitter = 3.8f;
        [Min(0f)] public float LightningJitterPerMeter = 0.026f;
        [Min(0.01f)] public float LightningVisualDuration = 0.34f;
        [Range(0, 8)] public int LightningBranchCount = 4;
        [Min(0f)] public float LightningBranchLength = 9f;
        [Min(0.001f)] public float LightningBranchWidthMultiplier = 0.46f;
        [ColorUsage(false, true)] public Color LightningColor = new Color(5.8f, 11f, 18f, 1f);
        [Min(0f)] public float ImpactFlashRadius = 5.8f;
        [Min(0.01f)] public float ImpactFlashDuration = 0.38f;

        [Header("Fused Sand Afterglow")]
        [Min(0f)] public float StrikeScarRadius = 3.6f;
        [Min(0f)] public float StrikeScarThickness = 0.09f;
        [Min(0f)] public float StrikeScarHeightOffset = 0.06f;
        [Min(0.1f)] public float StrikeScarLifetime = 28f;
        [ColorUsage(false)] public Color StrikeScarColor = new Color(0.018f, 0.024f, 0.032f, 1f);
        [ColorUsage(false, true)] public Color StrikeScarEmission = new Color(0.12f, 1.2f, 3.4f, 1f);
        [Range(0f, 1f)] public float StrikeScarSmoothness = 0.72f;

        [Header("Near-Field Arc Snaps")]
        [Min(0.1f)] public float NearArcMinimumInterval = 1.4f;
        [Min(0.1f)] public float NearArcMaximumInterval = 3.8f;
        [Min(0f)] public float NearArcMinimumRadius = 2.4f;
        [Min(0f)] public float NearArcMaximumRadius = 8f;
        [Min(0.001f)] public float NearArcWidth = 0.055f;
        [Min(0.01f)] public float NearArcDuration = 0.12f;

        [Header("Landmark Electrical Reactions")]
        public bool LandmarkReactionsEnabled = true;
        [Min(0f)] public float LandmarkReactionRange = 240f;
        [Min(0.1f)] public float LandmarkReactionMinimumInterval = 3.5f;
        [Min(0.1f)] public float LandmarkReactionMaximumInterval = 7.5f;
        [Min(0.001f)] public float LandmarkArcWidth = 0.11f;
        [Min(0.01f)] public float LandmarkArcDuration = 0.2f;

        [Header("Interior Storm Atmosphere")]
        public float InteriorVolumePriority = 210f;
        [Min(0f)] public float InteriorBlendSharpness = 5f;
        public float InteriorPostExposure = -0.52f;
        [Range(-100f, 100f)] public float InteriorSaturation = -28f;
        [Range(-100f, 100f)] public float InteriorContrast = 22f;
        [ColorUsage(false)] public Color InteriorColorFilter = new Color(0.72f, 0.82f, 0.92f, 1f);
        [Min(0f)] public float InteriorBloomIntensity = 0.68f;
        [Min(0f)] public float InteriorBloomThreshold = 0.82f;

        [Header("Electrical HUD")]
        [Min(100f)] public float HudWidth = 304f;
        [Min(40f)] public float HudHeight = 72f;
        [Min(0f)] public float HudLeft = 24f;
        [Min(0f)] public float HudTop = 118f;
        [Tooltip("Minimum vertical gap below other visible left-side HUD panels.")]
        [Min(0f)] public float HudOtherPanelGap = 14f;
        [Min(0f)] public float HudPadding = 12f;
        [Min(1f)] public float HudAccentWidth = 4f;
        [Min(8)] public int HudTitleFontSize = 12;
        [Min(8)] public int HudStatusFontSize = 15;
        [Min(8f)] public float HudTitleRowHeight = 18f;
        [Min(8f)] public float HudStatusRowHeight = 21f;
        [Min(0f)] public float HudTextRowGap = 2f;
        public string HudStormLabel = "ELECTRICAL STORM REGION";
        public string HudIonizationLabel = "IONIZATION SPIKE DETECTED";
        public string HudInterferenceLabel = "DRONE SYSTEM INTERFERENCE";
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.018f, 0.028f, 0.052f, 0.9f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(0.18f, 0.74f, 1f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.78f, 0.9f, 1f, 1f);
        [ColorUsage(false)] public Color HudStaticColor = new Color(0.35f, 0.82f, 1f, 0.14f);
        [Range(0, 16)] public int HudStaticLineCount = 5;
        [Min(0f)] public float HudStaticLineHeight = 1f;
        [Min(0f)] public float HudStaticJitter = 4f;
        [Min(0f)] public float HudStaticSpeed = 18f;
        [Range(0f, 1f)] public float HudApproachStaticMultiplier = 0.35f;
    }

    [System.Serializable]
    public sealed class ElectricalSandstormTuning
    {
        public bool Enabled = true;
        [Range(0f, 1f)] public float MinimumStormIntensity = 0.7f;
        [Tooltip("Horizontal distance beyond the visible stormfront footprint where electrical interference and lightning become active.")]
        [Min(0f)] public float ElectricalEffectRange = 35f;
        [Min(0f)] public float InitialStrikeDelay = 4f;
        [Min(0.1f)] public float ElectricalBuildupDuration = 1.4f;
        [Min(0.1f)] public float TargetTelegraphDuration = 1.8f;
        [Min(0.1f)] public float MinimumStrikeInterval = 5.5f;
        [Min(0.1f)] public float MaximumStrikeInterval = 8.5f;
        [Min(0f)] public float TargetPredictionTime = 0.65f;
        [Min(0f)] public float MaximumPredictionDistance = 28f;
        [Min(0f)] public float AirTargetMinimumHeight = 4f;
        [Min(0.1f)] public float StrikeRadius = 4.5f;
        [Min(0f)] public float StrikeDamage = 24f;
        public string StrikeDeathMessage = "Struck by Electrical Sandstorm lightning.";
        [Min(1f)] public float HazardousCargoDamageMultiplier = 1.35f;
        [Range(0.1f, 1f)] public float HighValueStrikeIntervalMultiplier = 0.82f;
        [Min(1f)] public float WeaponCooldownMultiplier = 1.3f;
        public int RandomSeedOffset = 22483;
        public ElectricalStormVisualTuning Visuals = new ElectricalStormVisualTuning();

        public void EnsureInitialized()
        {
            Visuals ??= new ElectricalStormVisualTuning();
        }
    }

    [System.Serializable]
    public sealed class HeatZoneTuning
    {
        public bool Enabled = true;

        [Header("Regional Heat Zones")]
        [Min(20f)] public float ZoneCellSize = 260f;
        [Range(0f, 1f)] public float ZoneChance = 0.32f;
        [Min(1f)] public float MinimumZoneRadius = 70f;
        [Min(1f)] public float MaximumZoneRadius = 125f;
        [Range(0f, 1f)] public float ZoneEdgeFalloff = 0.35f;
        public int RandomSeedOffset = 30871;

        [Header("Zone Severity")]
        [Range(0f, 1f)] public float SevereZoneChance = 0.28f;
        [Range(0f, 1f)] public float ExtremeZoneChance = 0.07f;
        [Range(0f, 1f)] public float MildSeverity = 0.55f;
        [Range(0f, 1f)] public float SevereSeverity = 0.78f;
        [Range(0f, 1f)] public float ExtremeSeverity = 1f;

        [Header("Drone Temperature")]
        [Min(1f)] public float MaximumTemperature = 100f;
        [Min(0f)] public float ZoneHeatPerSecond = 8f;
        [Min(0f)] public float BoostHeatPerSecond = 5f;
        [Min(0f)] public float WeaponHeatPerShot = 8f;
        [Min(0f)] public float PassiveCoolingPerSecond = 7f;
        [Min(0f)] public float CoolingAltitudeStart = 18f;
        [Min(0f)] public float CoolingAltitudeFull = 65f;
        [Min(1f)] public float HighAltitudeCoolingMultiplier = 2.4f;
        [Min(1f)] public float HotZoneBoostHeatMultiplier = 1.6f;
        [Min(1f)] public float HotZoneWeaponHeatMultiplier = 1.5f;

        [Header("Mechanical Consequences")]
        [Range(0f, 1f)] public float ConsequenceTemperatureThreshold = 0.55f;
        [Min(1f)] public float MaximumBoostDrainMultiplier = 1.45f;
        [Min(1f)] public float MaximumWeaponCooldownMultiplier = 1.6f;

        [Header("Visual Range & Refresh")]
        public bool VisualsEnabled = true;
        [Min(20f)] public float VisualRange = 540f;
        [Range(1, 8)] public int MaximumVisibleZones = 4;
        [Min(0.05f)] public float VisualRefreshInterval = 0.75f;
        [Min(8)] public int CurtainSegments = 48;
        [Min(2)] public int GroundMirageRings = 7;
        [Min(8)] public int GroundMirageSegments = 48;

        [Header("Refractive Air")]
        [Min(1f)] public float ShimmerCurtainHeight = 62f;
        [Range(0.1f, 1.5f)] public float ShimmerCurtainRadiusMultiplier = 1f;
        [Min(0f)] public float GroundMirageHeightOffset = 0.16f;
        [Range(0.05f, 1f)] public float GroundMirageRadiusMultiplier = 0.92f;
        [Min(0f)] public float DistantDistortionStrength = 0.34f;
        [Min(0f)] public float InteriorDistortionStrength = 0.78f;
        [Min(0f)] public float DistortionBlurStrength = 0.18f;
        [Min(0f)] public float DistortionTextureScale = 4.5f;
        public Vector2 DistortionScrollVelocity = new Vector2(0.035f, 0.12f);
        [Range(16, 256)] public int DistortionTextureResolution = 96;
        [Min(0f)] public float MirageSurfaceOpacity = 0.09f;
        [ColorUsage(false, true)] public Color MirageSurfaceColor = new Color(1.15f, 0.96f, 0.62f, 0.09f);

        [Header("Rising Heat Columns")]
        [Range(0, 240)] public int HeatPlumeParticleBudget = 72;
        [Min(0f)] public float HeatPlumeEmissionRate = 7f;
        [Min(0.1f)] public float HeatPlumeMinimumLifetime = 4.5f;
        [Min(0.1f)] public float HeatPlumeMaximumLifetime = 8f;
        [Min(0.01f)] public float HeatPlumeMinimumSize = 4f;
        [Min(0.01f)] public float HeatPlumeMaximumSize = 10f;
        [Min(1f)] public float HeatPlumeMinimumHeightMultiplier = 3.2f;
        [Min(1f)] public float HeatPlumeMaximumHeightMultiplier = 5.8f;
        [Min(0f)] public float HeatPlumeRiseSpeed = 5.5f;
        [Min(0f)] public float HeatPlumeTurbulence = 0.45f;

        [Header("Heat Plume Distortion Mask")]
        [Min(0f)] public float HeatPlumeDistortionStrength = 0.12f;
        [Range(0f, 1f)] public float HeatPlumeDistortionBlur = 0.04f;
        public Vector2 HeatPlumePrimaryTiling = new Vector2(2.4f, 3.2f);
        public Vector2 HeatPlumeSecondaryTiling = new Vector2(4.1f, 2.3f);
        public Vector2 HeatPlumePrimaryVelocity = new Vector2(0.035f, 0.12f);
        public Vector2 HeatPlumeSecondaryVelocity = new Vector2(-0.055f, 0.073f);
        [Range(0f, 1f)] public float HeatPlumeSecondaryStrength = 0.48f;
        [Range(0f, 1f)] public float HeatPlumeHorizontalTurbulence = 0.24f;
        [Range(0.05f, 0.5f)] public float HeatPlumeCoreWidth = 0.28f;
        [Range(0.05f, 0.5f)] public float HeatPlumeTopWidth = 0.4f;
        [Range(0f, 0.5f)] public float HeatPlumeWidthVariation = 0.16f;
        [Min(0f)] public float HeatPlumeWidthFrequency = 5.2f;
        [Range(0.01f, 1f)] public float HeatPlumeSideFeather = 0.42f;
        [Range(0.01f, 1f)] public float HeatPlumeBottomFeather = 0.2f;
        [Range(0.01f, 1f)] public float HeatPlumeTopFeather = 0.34f;
        [Range(0f, 1f)] public float HeatPlumeVerticalDissipationStart = 0.3f;
        [Min(0.01f)] public float HeatPlumeVerticalDissipationPower = 1.4f;
        [Range(0f, 0.5f)] public float HeatPlumeMaximumLean = 0.12f;
        [Min(0f)] public float HeatPlumeMinimumAnimationSpeedMultiplier = 0.78f;
        [Min(0f)] public float HeatPlumeMaximumAnimationSpeedMultiplier = 1.22f;
        [Min(0f)] public float HeatPlumeMinimumStrengthMultiplier = 0.75f;
        [Min(0f)] public float HeatPlumeMaximumStrengthMultiplier = 1.2f;
        [Min(0f)] public float HeatPlumePhaseRange = 17.371f;
        public float HeatPlumePrimaryPhaseOffset = 0.37f;
        public float HeatPlumeSecondaryPhaseOffset = -0.23f;
        [Range(0.001f, 0.25f)] public float HeatPlumeCardEdgeFeather = 0.04f;
        [Range(0f, 1f)] public float HeatPlumeEdgeNoiseBase = 0.72f;
        [Range(0f, 1f)] public float HeatPlumePrimaryEdgeNoise = 0.56f;
        [Range(0f, 1f)] public float HeatPlumeSecondaryEdgeNoise = 0.28f;
        [Range(0f, 1f)] public float HeatPlumeFadeProfileVariation = 0.18f;
        [Range(0f, 1f)] public float HeatPlumeLifetimeFadeInFraction = 0.12f;
        [Range(0f, 1f)] public float HeatPlumeLifetimeFadeOutFraction = 0.72f;
        [Min(0f)] public float HeatPlumeDistanceFadeStart = 160f;
        [Min(0f)] public float HeatPlumeDistanceFadeEnd = 480f;
        [Min(0f)] public float HeatPlumeDetailFadeStart = 120f;
        [Min(0f)] public float HeatPlumeDetailFadeEnd = 320f;
        [Min(0f)] public float HeatPlumeDepthFadeDistance = 2.5f;
        [Min(0f)] public float HeatPlumeMaskClipThreshold = 0.001f;

        [Header("Hot Wind Streaks")]
        [Range(0, 320)] public int HeatStreakParticleBudget = 110;
        [Min(0f)] public float HeatStreakEmissionRate = 16f;
        [Min(0.1f)] public float HeatStreakLifetime = 2.4f;
        [Min(0.01f)] public float HeatStreakSize = 0.07f;
        [Min(0f)] public float HeatStreakLength = 3.2f;
        public Vector2 HeatStreakDirection = new Vector2(1f, 0.22f);
        [Min(0f)] public float HeatStreakSpeed = 18f;
        [Min(0f)] public float HeatStreakHeightFraction = 0.32f;
        [Min(0f)] public float HeatStreakVolumeRadiusMultiplier = 1.6f;
        [Min(0f)] public float HeatStreakVolumeHeightMultiplier = 0.5f;
        [Min(0f)] public float HeatStreakVelocityStretch = 1f;
        [ColorUsage(false)] public Color HeatStreakColor = new Color(1f, 0.9f, 0.65f, 0.16f);

        [Header("Terrain Heat Pockets")]
        [Range(0, 24)] public int HotSpotCount = 9;
        [Min(0f)] public float HotSpotMinimumRadius = 1.6f;
        [Min(0f)] public float HotSpotMaximumRadius = 4.8f;
        [Min(0f)] public float HotSpotHeightOffset = 0.08f;
        [Min(0f)] public float HotSpotPlateThickness = 0.18f;
        [Min(0f)] public float HotSpotGlowScale = 0.62f;
        [Range(0f, 1f)] public float HotSpotMinimumDistanceFraction = 0.14f;
        [Range(0f, 1f)] public float HotSpotMaximumDistanceFraction = 0.82f;
        [Min(0f)] public float HotSpotPlateAspect = 1.45f;
        [Min(0f)] public float HotSpotGlowHeightMultiplier = 0.6f;
        [ColorUsage(false)] public Color HotSpotPlateColor = new Color(0.09f, 0.075f, 0.06f, 1f);
        [ColorUsage(false, true)] public Color HotSpotGlowColor = new Color(3.2f, 1.25f, 0.18f, 1f);
        [Range(0f, 1f)] public float HotSpotSmoothness = 0.22f;

        [Header("Interior Atmosphere")]
        public float InteriorVolumePriority = 200f;
        [Min(0f)] public float InteriorBlendSharpness = 4f;
        public float InteriorPostExposure = 0.28f;
        [Range(-100f, 100f)] public float InteriorSaturation = -16f;
        [Range(-100f, 100f)] public float InteriorContrast = 8f;
        [ColorUsage(false)] public Color InteriorColorFilter = new Color(1f, 0.96f, 0.82f, 1f);
        [Min(0f)] public float InteriorBloomIntensity = 0.34f;
        [Min(0f)] public float InteriorBloomThreshold = 1.08f;

        [Header("Thermal HUD")]
        [Range(0f, 1f)] public float HudVisibilityThreshold = 0.08f;
        [Min(100f)] public float HudWidth = 268f;
        [Min(40f)] public float HudHeight = 82f;
        [Min(0f)] public float HudRight = 24f;
        [Min(0f)] public float HudTop = 118f;
        [Tooltip("Minimum vertical gap below the upper-flight-ring HUD while both panels are visible.")]
        [Min(0f)] public float HudUpperFlightGap = 14f;
        [Min(0f)] public float HudPadding = 12f;
        [Min(1f)] public float HudAccentWidth = 4f;
        [Min(1f)] public float HudBarHeight = 8f;
        [Min(8)] public int HudTitleFontSize = 12;
        [Min(8)] public int HudStatusFontSize = 15;
        [Min(8f)] public float HudTitleRowHeight = 18f;
        [Min(8f)] public float HudStatusRowHeight = 21f;
        [Min(0f)] public float HudTextRowGap = 2f;
        public string HudZoneLabel = "HIGH THERMAL ZONE";
        public string HudRisingLabel = "DRONE HEAT RISING";
        public string HudBoostLabel = "BOOST EFFICIENCY REDUCED";
        [ColorUsage(false)] public Color HudPanelColor = new Color(0.035f, 0.045f, 0.052f, 0.88f);
        [ColorUsage(false)] public Color HudAccentColor = new Color(1f, 0.56f, 0.12f, 1f);
        [ColorUsage(false)] public Color HudTrackColor = new Color(0.13f, 0.15f, 0.16f, 1f);
        [ColorUsage(false)] public Color HudCoolColor = new Color(1f, 0.78f, 0.26f, 1f);
        [ColorUsage(false)] public Color HudHotColor = new Color(1f, 0.19f, 0.06f, 1f);
        [ColorUsage(false)] public Color HudTextColor = new Color(0.96f, 0.94f, 0.86f, 1f);
    }

    [System.Serializable]
    public sealed class EnvironmentalHazardTuning
    {
        public ElectricalSandstormTuning ElectricalSandstorms = new ElectricalSandstormTuning();
        public HeatZoneTuning HeatZones = new HeatZoneTuning();

        public void EnsureInitialized()
        {
            ElectricalSandstorms ??= new ElectricalSandstormTuning();
            ElectricalSandstorms.EnsureInitialized();
            HeatZones ??= new HeatZoneTuning();
        }
    }

    [System.Serializable]
    public sealed class PauseMenuVisualTuning
    {
        [Header("Responsive Layout")]
        [Min(320f)] public float ReferenceWidth = 1920f;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float MinimumScale = 0.7f;
        [Range(0.5f, 2f)] public float MaximumScale = 1.2f;
        [Min(280f)] public float PanelWidth = 540f;
        [Min(340f)] public float PanelHeight = 630f;
        [Min(8f)] public float ScreenMargin = 24f;
        [Min(12f)] public float PanelPadding = 36f;
        [Min(1f)] public float AccentBarHeight = 6f;
        [Min(0f)] public float ShadowOffset = 10f;

        [Header("Mixer Controls")]
        [Min(40f)] public float SliderRowHeight = 76f;
        [Min(2f)] public float SliderTrackHeight = 9f;
        [Min(4f)] public float SliderThumbWidth = 12f;
        [Min(8f)] public float SliderThumbHeight = 22f;
        [Min(24f)] public float ButtonHeight = 44f;
        [Min(0f)] public float ButtonGap = 10f;

        [Header("Typography")]
        [Min(12)] public int TitleFontSize = 36;
        [Min(10)] public int SubtitleFontSize = 13;
        [Min(10)] public int SectionFontSize = 14;
        [Min(10)] public int MixerLabelFontSize = 16;
        [Min(10)] public int ValueFontSize = 15;
        [Min(10)] public int ButtonFontSize = 15;
        [Min(9)] public int HintFontSize = 12;

        [Header("Desert Palette")]
        [ColorUsage(false)] public Color OverlayColor = new Color(0.015f, 0.025f, 0.045f, 0.86f);
        [ColorUsage(false)] public Color ShadowColor = new Color(0f, 0f, 0f, 0.48f);
        [ColorUsage(false)] public Color PanelColor = new Color(0.055f, 0.075f, 0.105f, 0.98f);
        [ColorUsage(false)] public Color PanelBorderColor = new Color(0.92f, 0.5f, 0.16f, 0.82f);
        [ColorUsage(false)] public Color AccentColor = new Color(1f, 0.61f, 0.18f, 1f);
        [ColorUsage(false)] public Color TitleColor = new Color(1f, 0.76f, 0.3f, 1f);
        [ColorUsage(false)] public Color PrimaryTextColor = new Color(0.92f, 0.96f, 1f, 1f);
        [ColorUsage(false)] public Color SecondaryTextColor = new Color(0.57f, 0.67f, 0.76f, 1f);
        [ColorUsage(false)] public Color DividerColor = new Color(0.3f, 0.37f, 0.44f, 0.75f);
        [ColorUsage(false)] public Color SliderTrackColor = new Color(0.12f, 0.16f, 0.21f, 1f);
        [ColorUsage(false)] public Color SliderFillColor = new Color(1f, 0.54f, 0.13f, 1f);
        [ColorUsage(false)] public Color SliderThumbColor = new Color(1f, 0.8f, 0.42f, 1f);
        [ColorUsage(false)] public Color ButtonColor = new Color(0.12f, 0.18f, 0.24f, 1f);
        [ColorUsage(false)] public Color ButtonHoverColor = new Color(0.19f, 0.29f, 0.38f, 1f);
        [ColorUsage(false)] public Color ButtonActiveColor = new Color(0.93f, 0.47f, 0.12f, 1f);
        [ColorUsage(false)] public Color DangerButtonColor = new Color(0.31f, 0.12f, 0.1f, 1f);
        [ColorUsage(false)] public Color DangerButtonHoverColor = new Color(0.5f, 0.17f, 0.12f, 1f);
    }

    [System.Serializable]
    public sealed class AudioTuning
    {
        [Header("FMOD Events")]
        [Tooltip("Looped background-music event played for the full run.")]
        public string BackgroundMusicEvent = "event:/Shadows on the Mesa";
        [Tooltip("One-shot event played whenever the drone successfully loses health.")]
        public string DroneDamageEvent = "event:/Drone_Damage";
        [Tooltip("One-shot event played when the drone successfully launches an energy shot.")]
        public string DroneFireEvent = "event:/Drone_Fire";
        [Tooltip("One-shot event played when a new lock-on target is initially detected.")]
        public string LockOnEvent = "event:/Lock_On";
        [Tooltip("One-shot event played when lock-on acquisition becomes fully locked.")]
        public string LockOnFullEvent = "event:/Lock_On_Full";

        [Header("July Mixer Routing")]
        [Tooltip("FMOD master bus used for pause-menu volume ducking.")]
        public string MasterBusPath = "bus:/";
        [Tooltip("FMOD group bus used by background music.")]
        public string MusicBusPath = "bus:/Music";
        [Tooltip("FMOD group bus reserved for gameplay and interface sound effects.")]
        public string SoundEffectsBusPath = "bus:/SFX";

        [Header("Default Volumes")]
        [Range(0f, 1f)] public float DefaultMusicVolume = 1f;
        [Range(0f, 1f)] public float DefaultSoundEffectsVolume = 1f;
        [Tooltip("Remember pause-menu volume choices between runs.")]
        public bool PersistVolumeSettings = true;

        [Header("Pause Audio Ducking")]
        [Tooltip("Master volume multiplier used while the game is paused.")]
        [Range(0f, 1f)] public float PausedVolumeMultiplier = 0.333333f;
        [Tooltip("Seconds used to fade between full and paused FMOD volume.")]
        [Min(0f)] public float PauseFadeDuration = 0.35f;

        [Header("Pause Menu Presentation")]
        public PauseMenuVisualTuning PauseMenu = new PauseMenuVisualTuning();

        public void EnsureInitialized()
        {
            PauseMenu ??= new PauseMenuVisualTuning();
        }
    }

    [System.Serializable]
    public sealed class StaminaBoostTuning
    {
        [Header("Stamina")]
        [Min(0.01f)] public float MaxStamina = 100f;
        [Min(0f)] public float DrainRate = 25f;
        [Min(0f)] public float RegenDelay = 0.8f;
        [Min(0f)] public float RegenRate = 30f;
        [Tooltip("Stamina restored per second after stamina has bottomed out.")]
        [Min(0f)] public float ExhaustedRegenRate = 15f;

        [Header("Speed Boost")]
        [Min(0f)] public float BoostAcceleration = 2.4f;
        [Min(0f)] public float BoostDeceleration = 3.2f;
        [Min(1f)] public float BoostSpeedMultiplier = 1.5f;
        [Tooltip("Absolute target-speed ceiling while boosting. Set to 0 for no additional ceiling.")]
        [Min(0f)] public float BoostMaximumSpeed = 150f;

        [Header("World-Following Meter")]
        [Tooltip("Screen-space offset from the drone whenever the stamina boost is inactive, including normal forward movement.")]
        public Vector2 MeterScreenOffset = new Vector2(62f, 4f);
        [Tooltip("Screen-space offset from the drone at full stamina boost. The meter follows the boost acceleration and deceleration blend between the two offsets.")]
        public Vector2 MeterMaximumSpeedScreenOffset = new Vector2(62f, 4f);
        [Min(8f)] public float MeterRadius = 28f;
        [Min(1f)] public float MeterThickness = 5f;
        [Tooltip("Non-procedural texture drawn behind the live stamina fill.")]
        public Texture2D MeterBackgroundIcon;
        [Tooltip("Screen-space width and height of the stamina background texture.")]
        [Min(1f)] public float MeterBackgroundIconSize = 76f;
        [Tooltip("Screen-space correction used to align the background texture's ring center with the live fill.")]
        public Vector2 MeterBackgroundIconOffset = Vector2.zero;
        [Tooltip("Tessellation used to keep the continuous ring visually smooth; this does not create visible tick marks.")]
        [Range(32, 256)] public int MeterArcResolution = 128;
        [Range(90f, 360f)] public float MeterArcDegrees = 280f;
        public float MeterArcStartDegrees = 130f;
        [Min(0f)] public float ScreenEdgePadding = 38f;
        [Tooltip("How quickly the stamina bar follows the drone in screen space.")]
        [Min(0f)] public float MeterFollowSharpness = 16f;
        [Min(0f)] public float VisibilityFadeSpeed = 7f;
        [Min(0f)] public float FullIdleFadeDelay = 1.2f;
        [Range(0f, 1f)] public float FullIdleAlpha = 0.12f;
        [Min(0f)] public float RestoredFeedbackDuration = 0.9f;
        [Range(0f, 1f)] public float LowStaminaThreshold = 0.25f;
        [ColorUsage(false)] public Color ReadyColor = new Color(0.35f, 1f, 0.72f, 1f);
        [ColorUsage(false)] public Color BoostingColor = new Color(0.2f, 0.95f, 1f, 1f);
        [ColorUsage(false)] public Color LowColor = new Color(1f, 0.7f, 0.12f, 1f);
        [ColorUsage(false)] public Color EmptyColor = new Color(1f, 0.16f, 0.08f, 1f);
        [ColorUsage(false)] public Color RegeneratingColor = new Color(0.38f, 0.72f, 1f, 1f);
        [ColorUsage(false)] public Color MeterBackgroundColor = new Color(0.015f, 0.035f, 0.05f, 0.72f);
    }

    [System.Serializable]
    public sealed class FlightSwooshTuning
    {
        public bool Enabled = true;

        [Header("Pool & Density")]
        [Range(8, 256)] public int MaximumStreakCount = 96;
        [Tooltip("Maximum streaks spawned per second at full intensity before the boost multiplier is applied.")]
        [Min(0f)] public float Density = 52f;
        [Min(0.01f)] public float DensityCurvePower = 0.8f;
        [Range(0f, 1f)] public float TimingVariation = 0.38f;

        [Header("Speed Response")]
        [Min(0f)] public float SpeedThreshold = 12f;
        [Min(0.01f)] public float MaximumIntensitySpeed = 38f;
        [Min(0f)] public float IntensitySharpness = 8f;
        [Min(0f)] public float BoostMultiplier = 1.35f;

        [Header("Streak Shape")]
        public Vector2 LengthRange = new Vector2(5.5f, 18f);
        public Vector2 WidthRange = new Vector2(0.045f, 0.14f);
        public Vector2 LifetimeRange = new Vector2(0.28f, 0.52f);
        public Vector2 SweepSpeedRange = new Vector2(38f, 96f);
        [Range(0f, 12f)] public float DirectionJitterDegrees = 3.2f;
        [Min(0f)] public float MovementAlignmentSharpness = 18f;

        [Header("Camera-Edge Spawn Area")]
        [Tooltip("Viewport-space radial band around screen center. Values near 0.5 place streaks at the outer view edges.")]
        public Vector2 SpawnRadiusRange = new Vector2(0.3f, 0.54f);
        [Tooltip("World-space distance in front of the player camera where streaks originate.")]
        public Vector2 SpawnDepthRange = new Vector2(7f, 20f);

        [Header("Appearance")]
        [ColorUsage(false, true)] public Color Color = new Color(0.3f, 2.4f, 4.5f, 1f);
        [Range(0f, 1f)] public float Opacity = 0.82f;
        [Range(0f, 1f)] public float BrightnessVariation = 0.18f;
        [Range(0.01f, 0.49f)] public float FadeInFraction = 0.1f;
        [Range(0.01f, 0.49f)] public float FadeOutFraction = 0.32f;
        [Range(0.01f, 0.49f)] public float EdgeSoftness = 0.22f;
        [Range(0.01f, 0.49f)] public float TipSoftness = 0.14f;
    }

    public enum DuneVectorTaaQuality
    {
        Low,
        Medium,
        High,
    }

    public enum DuneVectorTaaSharpenMode
    {
        LowQuality,
        PostSharpen,
        ContrastAdaptiveSharpening,
    }

    [System.Serializable]
    public sealed class DroneTuning
    {
        [Header("Ground Movement")]
        [Tooltip("Vertical distance between the grounded character root and the drone visual.")]
        [Min(0f)] public float GroundVisualHeight = 0.45f;
        [Min(0f)] public float MaxGroundSpeed = 18f;
        [Min(0f)] public float GroundMovementSharpness = 8.5f;
        [Min(0f)] public float GroundBrakingSharpness = 5.5f;
        [Min(0f)] public float GroundSteeringSharpness = 11f;
        [Min(0f)] public float TrailMinimumSpeed = 0.35f;

        [Header("Jump")]
        [Min(0f)] public float JumpSpeed = 13f;

        [Header("Boost Rings")]
        [Min(0f)] public float RingBoostAcceleration = 9.5f;
        [Min(0f)] public float BoostDuration = 2.6f;
        [Min(0f)] public float BoostMaxSpeed = 39f;

        [Header("Shift Stamina Boost")]
        public StaminaBoostTuning StaminaBoost = new StaminaBoostTuning();

        [Header("Ring Entry Burst")]
        [Min(1f)] public float RingBurstSpeedMultiplier = 1.45f;
        [Min(0.05f)] public float RingBurstDuration = 0.7f;
        [Min(0f)] public float RingBurstAcceleration = 28f;

        [Header("Flight")]
        [Min(0f)] public float FlightSpeed = 27f;
        [Min(0f)] public float MaximumFlightSpeed = 38f;
        [Min(0f)] public float FlightAcceleration = 3.8f;
        [Tooltip("Target flight speed while Space is held as an air brake.")]
        [Min(0f)] public float FlightBrakeSpeed = 12f;
        [Tooltip("How quickly held Space pulls flight velocity toward the brake speed.")]
        [Min(0f)] public float FlightBrakeSharpness = 9f;
        [Min(0f)] public float FlightSteeringSharpness = 10f;
        [Min(0f)] public float FlightLevelingSharpness = 5f;
        [Min(0f)] public float FlightYawRate = 125f;
        [Min(0.1f)] public float FlightDuration = 14f;
        [Min(0f)] public float FlightEntryLiftDuration = 0.75f;
        [Min(0f)] public float FlightEntryLiftSpeed = 16f;

        [Header("Camera")]
        [Min(0f)] public float CameraLookSensitivity = 0.085f;
        [Min(0f)] public float CameraRotationSharpness = 30f;
        [Min(0f)] public float CameraFollowSharpness = 4.2f;

        [Header("Camera Temporal Anti-Aliasing (HDRP)")]
        public bool EnableTemporalAntiAliasing = true;
        public DuneVectorTaaQuality TemporalAntiAliasingQuality = DuneVectorTaaQuality.High;
        public DuneVectorTaaSharpenMode TemporalSharpenMode = DuneVectorTaaSharpenMode.PostSharpen;
        [Range(0f, 2f)] public float TemporalSharpenStrength = 0.65f;
        [Range(0f, 1f)] public float TemporalRingingReduction = 0.35f;
        [Range(0f, 1f)] public float TemporalHistorySharpening = 0.25f;
        [Range(0f, 1f)] public float TemporalAntiFlicker = 0.4f;
        [Range(0f, 1f)] public float TemporalMotionVectorRejection = 0.35f;
        public bool TemporalAntiHistoryRinging = true;
        [Range(0.6f, 0.95f)] public float TemporalBaseBlendFactor = 0.8f;
        [Range(0.1f, 1f)] public float TemporalJitterScale = 0.9f;

        public void EnsureInitialized()
        {
            StaminaBoost ??= new StaminaBoostTuning();
        }

        public void ApplyTo(DroneCharacterController drone)
        {
            drone.MaxGroundSpeed = MaxGroundSpeed;
            drone.GroundMovementSharpness = GroundMovementSharpness;
            drone.GroundBrakingSharpness = GroundBrakingSharpness;
            drone.RotationSharpness = GroundSteeringSharpness;
            drone.TrailMinimumSpeed = TrailMinimumSpeed;
            drone.JumpSpeed = JumpSpeed;
            drone.RingBoostAcceleration = RingBoostAcceleration;
            drone.RingBoostDuration = BoostDuration;
            drone.RingBoostMaxSpeed = BoostMaxSpeed;
            drone.RingBurstSpeedMultiplier = RingBurstSpeedMultiplier;
            drone.RingBurstDuration = RingBurstDuration;
            drone.RingBurstAcceleration = RingBurstAcceleration;
            drone.FlightSpeed = FlightSpeed;
            drone.MaximumFlightSpeed = MaximumFlightSpeed;
            drone.FlightAcceleration = FlightAcceleration;
            drone.FlightBrakeSpeed = FlightBrakeSpeed;
            drone.FlightBrakeSharpness = FlightBrakeSharpness;
            drone.FlightSteeringSharpness = FlightSteeringSharpness;
            drone.FlightLevelingSharpness = FlightLevelingSharpness;
            drone.FlightYawRate = FlightYawRate;
            drone.FlightDuration = FlightDuration;
            drone.FlightEntryLiftDuration = FlightEntryLiftDuration;
            drone.FlightEntryLiftSpeed = FlightEntryLiftSpeed;
        }

        public void ApplyTo(DroneCameraController camera)
        {
            camera.LookSensitivity = CameraLookSensitivity;
            camera.RotationSharpness = CameraRotationSharpness;
            camera.FollowingSharpness = CameraFollowSharpness;
        }
    }

    [CreateAssetMenu(fileName = "Dune Vector Runtime Settings", menuName = "Dune Vector/Runtime Settings", order = 0)]
    public sealed class DuneVectorRuntimeSettings : ScriptableObject
    {
        [Tooltip("Movement, flight, boost, and camera controls for the drone.")]
        public DroneTuning PlayerTuning = new DroneTuning();

        [Tooltip("Shared player, rival, and neutral drone model, materials, rotor animation, and trails.")]
        public DroneVisualTuning DroneVisuals = new DroneVisualTuning();

        [Tooltip("Local camera-edge anime motion streaks driven by the player drone's real flight velocity.")]
        public FlightSwooshTuning FlightSwooshes = new FlightSwooshTuning();

        [Tooltip("World-space wind regions, authoritative forces, placement, falloff, and streamline presentation.")]
        public WindFieldSystemTuning WindFields = new WindFieldSystemTuning();

        [Tooltip("Procedural dust-devil spawning, traversal forces, fragile-cargo damage, and distant column presentation.")]
        public DustDevilTuning DustDevils = new DustDevilTuning();

        [Tooltip("Authored stylized cloud archetypes, placement, shading, and motion.")]
        public CloudTuning Clouds = new CloudTuning();

        [Tooltip("Dynamic clear-weather dust, sandstorm timing, wind, HDRP atmosphere, and particle layers.")]
        public DesertWeatherTuning Weather = new DesertWeatherTuning();

        [Tooltip("Electrical sandstorm strikes, regional heat, temperature, cooling, and gameplay consequences.")]
        public EnvironmentalHazardTuning EnvironmentalHazards = new EnvironmentalHazardTuning();

        [Tooltip("FMOD background music, July mixer bus routing, and pause-menu volume defaults.")]
        public AudioTuning Audio = new AudioTuning();

        [Tooltip("Pickup, package, and drop-off job generation.")]
        public DeliveryTuning Deliveries = new DeliveryTuning();

        [Tooltip("Courier contract generation, modifiers, rewards, cargo rules, and HUD.")]
        public CourierContractTuning Contracts = new CourierContractTuning();

        [Tooltip("Authored post-delivery narrative sequence, typewriter timing, and FMOD typing loop.")]
        public DeliveryMessageTuning DeliveryMessages = new DeliveryMessageTuning();

        [Tooltip("World hub geometry, terminal interaction, and teleport presentation.")]
        public WorldHubTuning WorldHub = new WorldHubTuning();

        [Tooltip("Authored procedural landmark placement and archetype dimensions.")]
        public LandmarkSystemTuning Landmarks = new LandmarkSystemTuning();

        [Tooltip("Unique mask-authored ground artworks placed in persistent logical world coordinates.")]
        public GeoglyphSystemTuning Geoglyphs = new GeoglyphSystemTuning();

        [Tooltip("Route-aware open-world enemy formation choreography.")]
        public RouteEncounterTuning RouteEncounters = new RouteEncounterTuning();

        [Tooltip("Ambient rival couriers, rescues, races, moving convoys, rewards, and faction presentation.")]
        public DynamicCourierTuning DynamicCouriers = new DynamicCourierTuning();

        [Tooltip("Procedural pyramid density and size range.")]
        public PyramidTuning Pyramids = new PyramidTuning();

        [Tooltip("Clustered, biome-weighted, instanced desert shrub generation and silhouettes.")]
        public DesertShrubTuning DesertShrubs = new DesertShrubTuning();

        [Tooltip("Chunk loading, unloading, and floating-origin behavior.")]
        public WorldStreamingTuning WorldStreaming = new WorldStreamingTuning();

        [Tooltip("Padded camera-frustum renderer suppression and dynamic-renderer discovery.")]
        public RendererFrustumCullingTuning RendererFrustumCulling = new RendererFrustumCullingTuning();

        [Tooltip("Spatial RenderMeshInstanced batching shared by high-volume procedural visuals.")]
        public SpatialGpuInstancingTuning SpatialGpuInstancing = new SpatialGpuInstancingTuning();

        [Tooltip("Player hull strength and damage protection.")]
        public PlayerHealthTuning HealthSettings = new PlayerHealthTuning();

        [Tooltip("Drone lock-on targeting, energy projectile, cooldown, feedback, and HUD presentation.")]
        public EnergyLauncherTuning EnergyLauncher = new EnergyLauncherTuning();

        [Tooltip("Airborne enemy spawning and combat behavior.")]
        public FlyingEnemyTuning FlyingEnemies = new FlyingEnemyTuning();

        [Tooltip("High-altitude upside-down pyramid lightning turrets.")]
        public StormPyramidTuning StormPyramids = new StormPyramidTuning();

        [Tooltip("High-altitude ring enemies that predict and strike only airborne players.")]
        public PlayerStrikeOrbTuning PlayerStrikeOrbs = new PlayerStrikeOrbTuning();

        [Tooltip("Ground enemy spawning, patrol, and explosion behavior.")]
        public GroundExploderTuning GroundExploders = new GroundExploderTuning();

        [Tooltip("Boost and flight ring sizes, height ranges, and animation.")]
        public RingTuning Rings = new RingTuning();

        [Tooltip("Permanent drone stat definitions, tier curves, gold costs, and upgrade-shop presentation.")]
        public DronePermanentUpgradeTuning PermanentUpgrades = new DronePermanentUpgradeTuning();

        [Tooltip("Layered procedural dune-shape controls.")]
        public DuneFieldSettings DuneGeneration = new DuneFieldSettings();

        [Tooltip("PNG texture used by the streamed dune terrain material.")]
        public Texture2D DuneTexture;

        [Tooltip("World-space width and length, in meters, covered by one repeat of the dune texture.")]
        [Min(0.01f)] public float DuneTextureTileSize = 18f;

        [Tooltip("Vertices along one edge of each generated terrain chunk. Higher values are smoother but cost more.")]
        [Range(8, 96)] public int DuneMeshResolution = 32;

        [Tooltip("World-space width and length of each streamed terrain chunk.")]
        [Min(24f)] public float DuneChunkSize = 80f;

        [HideInInspector] public DuneGenerationPreset SelectedDunePreset = DuneGenerationPreset.ClassicDesert;

        public void EnsureInitialized()
        {
            PlayerTuning ??= new DroneTuning();
            PlayerTuning.EnsureInitialized();
            DroneVisuals ??= new DroneVisualTuning();
            FlightSwooshes ??= new FlightSwooshTuning();
            WindFields ??= new WindFieldSystemTuning();
            WindFields.EnsureInitialized();
            DustDevils ??= new DustDevilTuning();
            Clouds ??= new CloudTuning();
            Clouds.EnsureInitialized();
            Weather ??= new DesertWeatherTuning();
            Weather.EnsureInitialized();
            EnvironmentalHazards ??= new EnvironmentalHazardTuning();
            EnvironmentalHazards.EnsureInitialized();
            Audio ??= new AudioTuning();
            Audio.EnsureInitialized();
            Deliveries ??= new DeliveryTuning();
            Deliveries.EnsureInitialized();
            Contracts ??= new CourierContractTuning();
            DeliveryMessages ??= new DeliveryMessageTuning();
            DeliveryMessages.EnsureInitialized();
            WorldHub ??= new WorldHubTuning();
            Landmarks ??= new LandmarkSystemTuning();
            Geoglyphs ??= new GeoglyphSystemTuning();
            Geoglyphs.EnsureInitialized();
            RouteEncounters ??= new RouteEncounterTuning();
            DynamicCouriers ??= new DynamicCourierTuning();
            Pyramids ??= new PyramidTuning();
            DesertShrubs ??= new DesertShrubTuning();
            DesertShrubs.EnsureInitialized();
            WorldStreaming ??= new WorldStreamingTuning();
            RendererFrustumCulling ??= new RendererFrustumCullingTuning();
            SpatialGpuInstancing ??= new SpatialGpuInstancingTuning();
            HealthSettings ??= new PlayerHealthTuning();
            EnergyLauncher ??= new EnergyLauncherTuning();
            FlyingEnemies ??= new FlyingEnemyTuning();
            StormPyramids ??= new StormPyramidTuning();
            PlayerStrikeOrbs ??= new PlayerStrikeOrbTuning();
            GroundExploders ??= new GroundExploderTuning();
            Rings ??= new RingTuning();
            PermanentUpgrades ??= new DronePermanentUpgradeTuning();
            PermanentUpgrades.EnsureInitialized();
            DuneGeneration ??= new DuneFieldSettings();
        }

        public void ApplyDunePreset(DuneGenerationPreset preset)
        {
            EnsureInitialized();
            int preservedSeed = DuneGeneration.WorldSeed;
            DuneGeneration = new DuneFieldSettings { WorldSeed = preservedSeed };
            SelectedDunePreset = preset;
            DuneChunkSize = 80f;
            DuneMeshResolution = 32;

            switch (preset)
            {
                case DuneGenerationPreset.GentleCinematic:
                    DuneGeneration.MajorScale = 360f;
                    DuneGeneration.MajorAmplitude = 2.4f;
                    DuneGeneration.BroadBowlStrength = 0.22f;
                    DuneGeneration.DuneScale = 72f;
                    DuneGeneration.DuneAmplitude = 2.8f;
                    DuneGeneration.DuneWarp = 0.28f;
                    DuneGeneration.RidgeHarmonicWeight = 0.08f;
                    DuneGeneration.CrestVariationStrength = 0.12f;
                    DuneGeneration.SecondaryScale = 145f;
                    DuneGeneration.SecondaryAmplitude = 1.1f;
                    DuneGeneration.DetailAmplitude = 0.12f;
                    DuneMeshResolution = 28;
                    break;

                case DuneGenerationPreset.GrandErg:
                    DuneGeneration.MajorScale = 520f;
                    DuneGeneration.MajorAmplitude = 7.2f;
                    DuneGeneration.MajorOctaves = 5;
                    DuneGeneration.BroadBowlStrength = 0.42f;
                    DuneGeneration.DuneScale = 96f;
                    DuneGeneration.DuneAmplitude = 10.5f;
                    DuneGeneration.DuneWarp = 0.48f;
                    DuneGeneration.RidgeHarmonicWeight = 0.21f;
                    DuneGeneration.SecondaryScale = 190f;
                    DuneGeneration.SecondaryAmplitude = 3.8f;
                    DuneGeneration.DetailAmplitude = 0.22f;
                    DuneGeneration.HeightMultiplier = 1.12f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.SharpRidges:
                    DuneGeneration.MajorScale = 240f;
                    DuneGeneration.MajorAmplitude = 3.8f;
                    DuneGeneration.DuneScale = 38f;
                    DuneGeneration.DuneAmplitude = 8.4f;
                    DuneGeneration.DuneWarp = 0.38f;
                    DuneGeneration.PrimaryRidgeWeight = 0.78f;
                    DuneGeneration.RidgeHarmonicWeight = 0.34f;
                    DuneGeneration.RidgeHarmonicFrequency = 2.15f;
                    DuneGeneration.CrestVariationStrength = 0.1f;
                    DuneGeneration.SecondaryAmplitude = 1.6f;
                    DuneGeneration.DetailScale = 12f;
                    DuneGeneration.DetailAmplitude = 0.48f;
                    DuneGeneration.NormalSampleDistance = 0.42f;
                    DuneMeshResolution = 48;
                    break;

                case DuneGenerationPreset.WindCarved:
                    DuneGeneration.WindDirection = new Vector2(1f, 0.14f);
                    DuneGeneration.MajorScale = 310f;
                    DuneGeneration.MajorAmplitude = 4.6f;
                    DuneGeneration.DuneScale = 61f;
                    DuneGeneration.DuneAmplitude = 6.4f;
                    DuneGeneration.DuneWarp = 1.35f;
                    DuneGeneration.WarpOctaves = 5;
                    DuneGeneration.CrestVariationStrength = 0.42f;
                    DuneGeneration.SecondaryScale = 132f;
                    DuneGeneration.SecondaryAmplitude = 2.9f;
                    DuneGeneration.DetailScale = 15f;
                    DuneGeneration.DetailAmplitude = 0.3f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.RoundedWindDunes:
                    DuneGeneration.WindDirection = new Vector2(0.96f, 0.28f);
                    DuneGeneration.MajorScale = 340f;
                    DuneGeneration.MajorAmplitude = 4.1f;
                    DuneGeneration.BroadBowlStrength = 0.38f;
                    DuneGeneration.DuneScale = 68f;
                    DuneGeneration.DuneAmplitude = 5.6f;
                    DuneGeneration.DuneWarp = 1.05f;
                    DuneGeneration.WarpOctaves = 4;
                    DuneGeneration.PrimaryRidgeWeight = 0.54f;
                    DuneGeneration.RidgeHarmonicWeight = 0.06f;
                    DuneGeneration.CrestVariationStrength = 0.3f;
                    DuneGeneration.SecondaryScale = 124f;
                    DuneGeneration.SecondaryAmplitude = 3.1f;
                    DuneGeneration.DetailScale = 21f;
                    DuneGeneration.DetailAmplitude = 0.16f;
                    DuneGeneration.NormalSampleDistance = 0.9f;
                    DuneMeshResolution = 36;
                    break;

                case DuneGenerationPreset.WindRibbonDunes:
                    DuneGeneration.WindDirection = new Vector2(0.82f, 0.57f);
                    DuneGeneration.MajorScale = 390f;
                    DuneGeneration.MajorAmplitude = 4.8f;
                    DuneGeneration.BroadBowlStrength = 0.3f;
                    DuneGeneration.DuneScale = 82f;
                    DuneGeneration.DuneAmplitude = 6.3f;
                    DuneGeneration.DuneWarp = 1.48f;
                    DuneGeneration.WarpOctaves = 5;
                    DuneGeneration.PrimaryRidgeWeight = 0.58f;
                    DuneGeneration.RidgeHarmonicWeight = 0.09f;
                    DuneGeneration.RidgeHarmonicFrequency = 1.7f;
                    DuneGeneration.CrestVariationStrength = 0.5f;
                    DuneGeneration.SecondaryScale = 155f;
                    DuneGeneration.SecondaryAmplitude = 3.4f;
                    DuneGeneration.SecondaryOctaves = 4;
                    DuneGeneration.DetailScale = 24f;
                    DuneGeneration.DetailAmplitude = 0.14f;
                    DuneGeneration.NormalSampleDistance = 1f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.GrandWindSwells:
                    DuneGeneration.WindDirection = new Vector2(0.9f, 0.43f);
                    DuneGeneration.MajorScale = 610f;
                    DuneGeneration.MajorAmplitude = 7.8f;
                    DuneGeneration.MajorOctaves = 5;
                    DuneGeneration.BroadBowlStrength = 0.48f;
                    DuneGeneration.DuneScale = 116f;
                    DuneGeneration.DuneAmplitude = 9.2f;
                    DuneGeneration.DuneWarp = 1.12f;
                    DuneGeneration.WarpOctaves = 4;
                    DuneGeneration.PrimaryRidgeWeight = 0.56f;
                    DuneGeneration.RidgeHarmonicWeight = 0.05f;
                    DuneGeneration.CrestVariationStrength = 0.34f;
                    DuneGeneration.SecondaryScale = 225f;
                    DuneGeneration.SecondaryAmplitude = 4.2f;
                    DuneGeneration.DetailScale = 28f;
                    DuneGeneration.DetailAmplitude = 0.12f;
                    DuneGeneration.HeightMultiplier = 1.08f;
                    DuneGeneration.NormalSampleDistance = 1.15f;
                    DuneMeshResolution = 40;
                    break;

                case DuneGenerationPreset.RollingSandSea:
                    DuneGeneration.MajorScale = 215f;
                    DuneGeneration.MajorAmplitude = 6.8f;
                    DuneGeneration.BroadBowlStrength = 0.55f;
                    DuneGeneration.DuneScale = 86f;
                    DuneGeneration.DuneAmplitude = 3.2f;
                    DuneGeneration.DuneWarp = 0.58f;
                    DuneGeneration.RidgeHarmonicWeight = 0.06f;
                    DuneGeneration.CrestVariationStrength = 0.3f;
                    DuneGeneration.SecondaryScale = 72f;
                    DuneGeneration.SecondaryAmplitude = 4.6f;
                    DuneGeneration.SecondaryOctaves = 4;
                    DuneGeneration.DetailAmplitude = 0.18f;
                    DuneMeshResolution = 36;
                    break;

                case DuneGenerationPreset.FineRipples:
                    DuneGeneration.MajorScale = 330f;
                    DuneGeneration.MajorAmplitude = 1.8f;
                    DuneGeneration.BroadBowlStrength = 0.18f;
                    DuneGeneration.DuneScale = 24f;
                    DuneGeneration.DuneAmplitude = 2.9f;
                    DuneGeneration.DuneWarp = 0.82f;
                    DuneGeneration.RidgeHarmonicWeight = 0.24f;
                    DuneGeneration.RidgeHarmonicFrequency = 3f;
                    DuneGeneration.SecondaryScale = 58f;
                    DuneGeneration.SecondaryAmplitude = 1.3f;
                    DuneGeneration.DetailScale = 7.5f;
                    DuneGeneration.DetailAmplitude = 0.72f;
                    DuneGeneration.DetailOctaves = 3;
                    DuneGeneration.NormalSampleDistance = 0.28f;
                    DuneMeshResolution = 64;
                    break;

                case DuneGenerationPreset.ExtremeDunes:
                    DuneGeneration.MajorScale = 155f;
                    DuneGeneration.MajorAmplitude = 10f;
                    DuneGeneration.MajorOctaves = 6;
                    DuneGeneration.BroadBowlStrength = 0.62f;
                    DuneGeneration.DuneScale = 34f;
                    DuneGeneration.DuneAmplitude = 12f;
                    DuneGeneration.DuneWarp = 1.65f;
                    DuneGeneration.WarpOctaves = 5;
                    DuneGeneration.PrimaryRidgeWeight = 0.8f;
                    DuneGeneration.RidgeHarmonicWeight = 0.38f;
                    DuneGeneration.CrestVariationStrength = 0.48f;
                    DuneGeneration.SecondaryScale = 64f;
                    DuneGeneration.SecondaryAmplitude = 6.2f;
                    DuneGeneration.SecondaryOctaves = 5;
                    DuneGeneration.DetailScale = 10f;
                    DuneGeneration.DetailAmplitude = 1.1f;
                    DuneGeneration.HeightMultiplier = 1.25f;
                    DuneGeneration.NormalSampleDistance = 0.35f;
                    DuneMeshResolution = 48;
                    break;
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
        }
    }
}
