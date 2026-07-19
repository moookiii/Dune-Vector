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

    [System.Serializable]
    public sealed class CloudTuning
    {
        public bool Enabled = true;
        [Tooltip("Approximate total cluster count across the preloaded chunk area.")]
        [Range(4, 30)] public int ClusterCount = 14;
        [Min(20f)] public float Altitude = 82f;
        [Min(0f)] public float AltitudeVariation = 22f;
        [Min(0f)] public float DriftSpeed = 2.2f;
        public Vector2 DriftDirection = new Vector2(0.82f, 0.57f);
        public int RandomSeedOffset = 7319;

        [Header("Cluster Shape")]
        [Range(1, 12)] public int MinimumLobes = 4;
        [Range(1, 12)] public int MaximumLobes = 7;
        public Vector3 MinimumLobeOffset = new Vector3(-13f, -2.5f, -7f);
        public Vector3 MaximumLobeOffset = new Vector3(13f, 4.5f, 7f);
        public Vector3 MinimumLobeScale = new Vector3(12f, 4.5f, 8f);
        public Vector3 MaximumLobeScale = new Vector3(24f, 8.5f, 17f);
    }

    [System.Serializable]
    public sealed class DeliveryTuning
    {
        public bool Enabled = true;
        public bool RandomizeLocationsEachPlay = true;
        public int JobSeedOffset;
        [Min(20f)] public float MinimumPickupDistance = 75f;
        [Min(20f)] public float MaximumPickupDistance = 145f;
        [Min(20f)] public float MinimumDeliveryDistance = 110f;
        [Min(20f)] public float MaximumDeliveryDistance = 210f;
        [Min(1f)] public float ObjectiveRingRadius = 3.2f;
        [Min(0f)] public float ObjectiveRingHeight = 3.4f;
        [Min(0.1f)] public float PackageScale = 0.8f;

        [Header("Completion Message")]
        [ColorUsage(false)] public Color CompletionTextRed = new Color(1f, 0.55f, 0.68f);
        [ColorUsage(false)] public Color CompletionTextGreen = new Color(0.55f, 1f, 0.72f);
        [ColorUsage(false)] public Color CompletionTextBlue = new Color(0.55f, 0.78f, 1f);
        [Min(0f)] public float CompletionTextColorCyclesPerSecond = 0.45f;
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
    public sealed class WorldStreamingTuning
    {
        [Tooltip("Chunk radius kept active around the player.")]
        [Range(1, 8)] public int ActiveRadius = 3;
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
    public sealed class PlayerHealthTuning
    {
        [Min(1f)] public float MaximumHealth = 100f;
        [Min(0f)] public float DamageInvulnerability = 0.45f;
    }

    [System.Serializable]
    public sealed class FlyingEnemyTuning
    {
        public bool Enabled = true;
        [Range(1, 12)] public int EnemyCount = 3;
        [Min(10f)] public float MinimumSpawnDistance = 55f;
        [Min(10f)] public float MaximumSpawnDistance = 105f;
        [Min(1f)] public float DetectionRange = 125f;
        [Min(1f)] public float HoverHeight = 20f;
        [Min(0f)] public float HoverAmplitude = 1.1f;
        [Min(0f)] public float FollowSpeed = 11f;
        [Min(0f)] public float AttackSpeed = 38f;
        [Min(0.1f)] public float AttackCooldown = 3.5f;
        [Min(0.25f)] public float AttackAlignmentDistance = 4f;
        [Min(0f)] public float ImpactDamage = 25f;
        [Min(0.1f)] public float ImpactRadius = 3.4f;
        [Min(0f)] public float StuckDuration = 2.2f;
        [Min(0f)] public float ReturnSpeed = 13f;
        [Min(20f)] public float RepositionDistance = 240f;
        [Min(0.1f)] public float VisualScale = 1.35f;
    }

    [System.Serializable]
    public sealed class StormPyramidTuning
    {
        public bool Enabled = true;

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
        [Min(1f)] public float DetectionRange = 125f;
        [Tooltip("Time spent visibly tracking before the attack point is locked.")]
        [Min(0f)] public float TrackingDuration = 0.45f;

        [Header("Lightning Attack")]
        [Tooltip("Delay before beginning a new attack after returning to idle.")]
        [Min(0.1f)] public float AttackInterval = 4.5f;
        [Tooltip("Charge duration for a straight-down ground strike.")]
        [Min(0.1f)] public float ChargeTime = 1.15f;
        [Tooltip("Charge duration for a player-targeted strike that is not straight down.")]
        [Min(0.1f)] public float PlayerStrikeChargeTime = 0.9f;
        [Min(0f)] public float Cooldown = 2.4f;
        [Min(0f)] public float LightningDamage = 32f;
        [Min(0.1f)] public float StrikeRadius = 5.5f;
        [Min(0.05f)] public float LightningVisualDuration = 0.28f;
        [Min(0.01f)] public float ChargeTelegraphWidth = 0.12f;
        [Min(0.01f)] public float LightningWidth = 0.48f;
        [Tooltip("Multiplies only the lightning bolt emission, creating a stronger HDR bloom halo.")]
        [Min(0f)] public float LightningBloomIntensity = 4f;

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
        [ColorUsage(false)] public Color BodyColor = new Color(0.012f, 0.055f, 0.024f);
        [ColorUsage(false, true)] public Color BodyEmission = new Color(0.025f, 0.22f, 0.075f);
        [ColorUsage(false)] public Color CoreColor = new Color(0.018f, 0.11f, 0.045f);
        [ColorUsage(false, true)] public Color CoreEmission = new Color(0.12f, 1.65f, 0.48f);
        [ColorUsage(false)] public Color LightningColor = new Color(0.55f, 0.86f, 1f);
        [ColorUsage(false, true)] public Color LightningEmission = new Color(7.5f, 12f, 18f);
        [ColorUsage(false)] public Color WarningColor = new Color(0.18f, 0.42f, 0.62f);
        [ColorUsage(false, true)] public Color WarningEmission = new Color(0.45f, 2.8f, 5.8f);
    }

    [System.Serializable]
    public sealed class GroundExploderTuning
    {
        public bool Enabled = true;
        [Tooltip("Expected number of ground exploders generated in each streamed desert chunk.")]
        [Min(0f)] public float DensityPerChunk = 0.28f;
        [Header("Patrol")]
        [Min(0f)] public float MovementSpeed = 5.5f;
        [Min(2f)] public float PatrolRadius = 18f;
        [Range(0f, 60f)] public float MaximumGroundSlope = 34f;
        [Header("Proximity Explosion")]
        [Min(0.5f)] public float DetectionRadius = 18f;
        [Min(0.1f)] public float WindUpDuration = 1.25f;
        [Min(0.5f)] public float ExplosionRadius = 11f;
        [Min(0f)] public float MaximumDamage = 65f;
        [Header("Presentation")]
        [Min(0.1f)] public float VisualScale = 1.15f;
    }

    [System.Serializable]
    public sealed class RingTuning
    {
        [Header("Starting Size")]
        [Min(0.75f)] public float GroundRingRadius = 3.25f;
        [Min(0.75f)] public float FlightRingRadius = 3.55f;

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

        [Header("Sky & Exposure")]
        [ColorUsage(false, true)] public Color ClearSkyTop = new Color(0.03f, 0.32f, 1.7f);
        [ColorUsage(false, true)] public Color ClearSkyMiddle = new Color(0.1f, 0.55f, 1.4f);
        [ColorUsage(false, true)] public Color ClearSkyBottom = new Color(2.6f, 1f, 0.25f);
        [ColorUsage(false, true)] public Color StormSkyTop = new Color(0.24f, 0.14f, 0.065f);
        [ColorUsage(false, true)] public Color StormSkyMiddle = new Color(0.54f, 0.29f, 0.1f);
        [ColorUsage(false, true)] public Color StormSkyBottom = new Color(1.15f, 0.54f, 0.15f);
        [Min(0f)] public float SkyGradientDiffusion = 1.35f;
        [Min(0f)] public float SkyMultiplier = 0.85f;
        public float ClearExposure = 2f;
        public float StormExposure = 1.35f;
    }

    [System.Serializable]
    public sealed class DesertWeatherDustTuning
    {
        [Header("Density & Coverage")]
        [Range(0f, 1f)] public float AmbientDustDensity = 0.18f;
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
    public sealed class PauseMenuVisualTuning
    {
        [Header("Responsive Layout")]
        [Min(320f)] public float ReferenceWidth = 1920f;
        [Min(240f)] public float ReferenceHeight = 1080f;
        [Range(0.5f, 2f)] public float MinimumScale = 0.7f;
        [Range(0.5f, 2f)] public float MaximumScale = 1.2f;
        [Min(280f)] public float PanelWidth = 540f;
        [Min(340f)] public float PanelHeight = 500f;
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
    public sealed class DroneTuning
    {
        [Header("Ground Movement")]
        [Min(0f)] public float MaxGroundSpeed = 18f;
        [Min(0f)] public float GroundMovementSharpness = 8.5f;
        [Min(0f)] public float GroundBrakingSharpness = 5.5f;
        [Min(0f)] public float TrailMinimumSpeed = 0.35f;

        [Header("Jump")]
        [Min(0f)] public float JumpSpeed = 13f;

        [Header("Boost Rings")]
        [Min(0f)] public float BoostAcceleration = 9.5f;
        [Min(0f)] public float BoostDuration = 2.6f;
        [Min(0f)] public float BoostMaxSpeed = 39f;

        [Header("Ring Entry Burst")]
        [Min(1f)] public float RingBurstSpeedMultiplier = 1.45f;
        [Min(0.05f)] public float RingBurstDuration = 0.7f;
        [Min(0f)] public float RingBurstAcceleration = 28f;

        [Header("Flight")]
        [Min(0f)] public float FlightSpeed = 27f;
        [Min(0f)] public float MaximumFlightSpeed = 38f;
        [Min(0f)] public float FlightAcceleration = 3.8f;
        [Min(0f)] public float FlightSteeringSharpness = 10f;
        [Min(0f)] public float FlightLevelingSharpness = 5f;
        [Min(0f)] public float FlightYawRate = 125f;
        [Min(0.1f)] public float FlightDuration = 14f;
        [Min(0f)] public float GroundFlightLaunchDelay = 0.5f;
        [Min(0f)] public float FlightEntryLiftDuration = 0.75f;
        [Min(0f)] public float FlightEntryLiftSpeed = 16f;

        [Header("Camera")]
        [Min(0f)] public float CameraLookSensitivity = 0.085f;
        [Min(0f)] public float CameraRotationSharpness = 30f;
        [Min(0f)] public float CameraFollowSharpness = 4.2f;

        public void ApplyTo(DroneCharacterController drone)
        {
            drone.MaxGroundSpeed = MaxGroundSpeed;
            drone.GroundMovementSharpness = GroundMovementSharpness;
            drone.GroundBrakingSharpness = GroundBrakingSharpness;
            drone.TrailMinimumSpeed = TrailMinimumSpeed;
            drone.JumpSpeed = JumpSpeed;
            drone.BoostAcceleration = BoostAcceleration;
            drone.BoostDuration = BoostDuration;
            drone.BoostMaxSpeed = BoostMaxSpeed;
            drone.RingBurstSpeedMultiplier = RingBurstSpeedMultiplier;
            drone.RingBurstDuration = RingBurstDuration;
            drone.RingBurstAcceleration = RingBurstAcceleration;
            drone.FlightSpeed = FlightSpeed;
            drone.MaximumFlightSpeed = MaximumFlightSpeed;
            drone.FlightAcceleration = FlightAcceleration;
            drone.FlightSteeringSharpness = FlightSteeringSharpness;
            drone.FlightLevelingSharpness = FlightLevelingSharpness;
            drone.FlightYawRate = FlightYawRate;
            drone.FlightDuration = FlightDuration;
            drone.GroundFlightLaunchDelay = GroundFlightLaunchDelay;
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

        [Tooltip("Procedural cloud placement and motion.")]
        public CloudTuning Clouds = new CloudTuning();

        [Tooltip("Dynamic clear-weather dust, sandstorm timing, wind, HDRP atmosphere, and particle layers.")]
        public DesertWeatherTuning Weather = new DesertWeatherTuning();

        [Tooltip("FMOD background music, July mixer bus routing, and pause-menu volume defaults.")]
        public AudioTuning Audio = new AudioTuning();

        [Tooltip("Pickup, package, and drop-off job generation.")]
        public DeliveryTuning Deliveries = new DeliveryTuning();

        [Tooltip("Procedural pyramid density and size range.")]
        public PyramidTuning Pyramids = new PyramidTuning();

        [Tooltip("Chunk loading, unloading, and floating-origin behavior.")]
        public WorldStreamingTuning WorldStreaming = new WorldStreamingTuning();

        [Tooltip("Player hull strength and damage protection.")]
        public PlayerHealthTuning HealthSettings = new PlayerHealthTuning();

        [Tooltip("Airborne enemy spawning and combat behavior.")]
        public FlyingEnemyTuning FlyingEnemies = new FlyingEnemyTuning();

        [Tooltip("High-altitude upside-down pyramid lightning turrets.")]
        public StormPyramidTuning StormPyramids = new StormPyramidTuning();

        [Tooltip("Ground enemy spawning, patrol, and explosion behavior.")]
        public GroundExploderTuning GroundExploders = new GroundExploderTuning();

        [Tooltip("Boost and flight ring sizes, height ranges, and animation.")]
        public RingTuning Rings = new RingTuning();

        [Tooltip("Layered procedural dune-shape controls.")]
        public DuneFieldSettings DuneGeneration = new DuneFieldSettings();

        [Tooltip("Vertices along one edge of each generated terrain chunk. Higher values are smoother but cost more.")]
        [Range(8, 96)] public int DuneMeshResolution = 32;

        [Tooltip("World-space width and length of each streamed terrain chunk.")]
        [Min(24f)] public float DuneChunkSize = 80f;

        [HideInInspector] public DuneGenerationPreset SelectedDunePreset = DuneGenerationPreset.ClassicDesert;

        public void EnsureInitialized()
        {
            PlayerTuning ??= new DroneTuning();
            Clouds ??= new CloudTuning();
            Weather ??= new DesertWeatherTuning();
            Weather.EnsureInitialized();
            Audio ??= new AudioTuning();
            Audio.EnsureInitialized();
            Deliveries ??= new DeliveryTuning();
            Pyramids ??= new PyramidTuning();
            WorldStreaming ??= new WorldStreamingTuning();
            HealthSettings ??= new PlayerHealthTuning();
            FlyingEnemies ??= new FlyingEnemyTuning();
            StormPyramids ??= new StormPyramidTuning();
            GroundExploders ??= new GroundExploderTuning();
            Rings ??= new RingTuning();
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
