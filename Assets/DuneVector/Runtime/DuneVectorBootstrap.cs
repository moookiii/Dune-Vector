using KinematicCharacterController;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DuneVector
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorBootstrap : MonoBehaviour
    {
        [Header("Runtime Configuration")]
        [Tooltip("Reusable asset containing every gameplay and world-generation tuning value.")]
        public DuneVectorRuntimeSettings RuntimeSettings;

        public DroneTuning PlayerTuning => RuntimeSettings.PlayerTuning;
        public DroneVisualTuning DroneVisuals => RuntimeSettings.DroneVisuals;
        public FlightSwooshTuning FlightSwooshes => RuntimeSettings.FlightSwooshes;
        public BoostRingTrailTuning BoostRingTrail => RuntimeSettings.BoostRingTrail;
        public WindFieldSystemTuning WindFieldSettings => RuntimeSettings.WindFields;
        public DustDevilTuning DustDevilSettings => RuntimeSettings.DustDevils;
        public CloudTuning Clouds => RuntimeSettings.Clouds;
        public DesertWeatherTuning WeatherSettings => RuntimeSettings.Weather;
        public EnvironmentalHazardTuning EnvironmentalHazardSettings => RuntimeSettings.EnvironmentalHazards;
        public AudioTuning AudioSettings => RuntimeSettings.Audio;
        public DeliveryTuning Deliveries => RuntimeSettings.Deliveries;
        public CourierContractTuning Contracts => RuntimeSettings.Contracts;
        public DeliveryMessageTuning DeliveryMessages => RuntimeSettings.DeliveryMessages;
        public WorldHubTuning WorldHubSettings => RuntimeSettings.WorldHub;
        public LandmarkSystemTuning LandmarkSettings => RuntimeSettings.Landmarks;
        public RouteEncounterTuning RouteEncounterSettings => RuntimeSettings.RouteEncounters;
        public DynamicCourierTuning DynamicCourierSettings => RuntimeSettings.DynamicCouriers;
        public DesertAtlasTuning DesertAtlasSettings => RuntimeSettings.DesertAtlas;
        public PyramidTuning Pyramids => RuntimeSettings.Pyramids;
        public PyramidTuning Obelisks => RuntimeSettings.Obelisks;
        public CactusTuning Cacti => RuntimeSettings.Cacti;
        public DesertShrubTuning DesertShrubs => RuntimeSettings.DesertShrubs;
        public WorldStreamingTuning WorldStreaming => RuntimeSettings.WorldStreaming;
        public PlayerHealthTuning HealthSettings => RuntimeSettings.HealthSettings;
        public GameOverScreenTuning GameOverScreenSettings => RuntimeSettings.GameOverScreen;
        public MapHudTuning MapHudSettings => RuntimeSettings.MapHud;
        public EnergyLauncherTuning EnergyLauncherSettings => RuntimeSettings.EnergyLauncher;
        public FlyingEnemyTuning FlyingEnemies => RuntimeSettings.FlyingEnemies;
        public StormPyramidTuning StormPyramids => RuntimeSettings.StormPyramids;
        public PlayerStrikeOrbTuning PlayerStrikeOrbs => RuntimeSettings.PlayerStrikeOrbs;
        public VesperKiteTuning VesperKites => RuntimeSettings.VesperKites;
        public GroundExploderTuning GroundExploders => RuntimeSettings.GroundExploders;
        public RingTuning Rings => RuntimeSettings.Rings;
        public DuneFieldSettings DuneGeneration
        {
            get => RuntimeSettings.DuneGeneration;
            private set => RuntimeSettings.DuneGeneration = value;
        }
        public int DuneMeshResolution
        {
            get => RuntimeSettings.DuneMeshResolution;
            private set => RuntimeSettings.DuneMeshResolution = value;
        }
        public float DuneChunkSize
        {
            get => RuntimeSettings.DuneChunkSize;
            private set => RuntimeSettings.DuneChunkSize = value;
        }
        public DuneGenerationPreset SelectedDunePreset
        {
            get => RuntimeSettings.SelectedDunePreset;
            set => RuntimeSettings.SelectedDunePreset = value;
        }

        public static DuneVectorBootstrap Instance { get; private set; }

        public DesertWorldStreamer World { get; private set; }
        public DroneCharacterController Drone { get; private set; }
        public DroneCameraController DroneCamera { get; private set; }
        public DronePlayer Player { get; private set; }
        public DuneVectorDebugHUD DebugHUD { get; private set; }
        public DuneVectorMapHUD MapHUD { get; private set; }
        public DuneVectorDeliveryLoop DeliveryLoop { get; private set; }
        public DuneVectorLandmarkDirector LandmarkDirector { get; private set; }
        public DuneVectorProceduralBuildingDirector BuildingDirector { get; private set; }
        public DuneVectorCourierGame CourierGame { get; private set; }
        public DuneVectorRouteEncounterDirector RouteEncounterDirector { get; private set; }
        public DuneVectorDynamicCourierDirector DynamicCourierDirector { get; private set; }
        public DuneVectorDesertAtlas DesertAtlas { get; private set; }
        public DroneHealth DroneHealth { get; private set; }
        public DroneTargetDetector TargetDetector { get; private set; }
        public DroneLockOnController LockOnController { get; private set; }
        public DroneEnergyLauncher EnergyLauncher { get; private set; }
        public DroneLockOnHUD LockOnHUD { get; private set; }
        public DuneVectorEnemyDirector EnemyDirector { get; private set; }
        public DuneVectorStormPyramidDirector StormPyramidDirector { get; private set; }
        public DuneVectorVesperKiteDirector VesperKiteDirector { get; private set; }
        public DuneVectorWeatherController WeatherSystem { get; private set; }
        public DuneVectorEnvironmentalHazardSystem EnvironmentalHazardSystem { get; private set; }
        public DuneVectorWindFieldSystem WindFieldSystem { get; private set; }
        public DuneVectorDustDevilSystem DustDevilSystem { get; private set; }
        public DuneVectorGameOverController GameOverController { get; private set; }
        public DuneVectorAudioManager AudioManager { get; private set; }
        public DuneVectorMusicReactiveSky MusicReactiveSky { get; private set; }
        public DuneVectorPauseMenu PauseMenu { get; private set; }
        public DuneVectorPhotographySystem Photography { get; private set; }
        public DroneGoldWallet GoldWallet { get; private set; }
        public DronePermanentUpgradeSystem PermanentUpgrades { get; private set; }

        private DuneVectorMaterials _materials;
        private VolumeProfile _runtimeVolumeProfile;
        private DuneVectorUrpFogState _environmentFog;
        private DuneVectorY2KSky _environmentSky;
        private Bloom _environmentBloom;
        private bool _ownsRuntimeSettings;

        public void ApplyDunePreset(DuneGenerationPreset preset)
        {
            int preservedSeed = DuneGeneration != null ? DuneGeneration.WorldSeed : 19770503;
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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Time.timeScale = 1f;

            if (RuntimeSettings == null)
            {
                RuntimeSettings = ScriptableObject.CreateInstance<DuneVectorRuntimeSettings>();
                RuntimeSettings.name = "Temporary Dune Vector Runtime Settings";
                _ownsRuntimeSettings = true;
                Debug.LogWarning(
                    "Dune Vector Bootstrap has no Runtime Settings asset assigned. " +
                    "Temporary defaults will be used for this session.",
                    this);
            }
            RuntimeSettings.EnsureInitialized();
            ApplyRetroCrtScanlines();

            QualitySettings.vSyncCount = Mathf.Clamp(RuntimeSettings.Performance.VSyncCount, 0, 4);
            Application.targetFrameRate = Mathf.Clamp(RuntimeSettings.Performance.TargetFrameRate, -1, 360);
            if (Debug.isDebugBuild)
            {
                Application.runInBackground = RuntimeSettings.Performance.RunDevelopmentBuildsInBackground;
            }
            _materials = new DuneVectorMaterials(
                RuntimeSettings,
                Rings,
                Deliveries,
                Clouds,
                DynamicCourierSettings,
                Cacti,
                DesertShrubs,
                DroneVisuals,
                RuntimeSettings.Geoglyphs,
                RuntimeSettings.Landmarks,
                PlayerStrikeOrbs,
                Pyramids,
                Obelisks,
                FlyingEnemies,
                VesperKites);
            _materials.ConfigureStormPyramid(StormPyramids);
            _materials.ConfigurePlayerStrikeOrb(PlayerStrikeOrbs);

            DuneVectorSpatialInstancing spatialInstancing = gameObject.AddComponent<DuneVectorSpatialInstancing>();
            spatialInstancing.Initialize(RuntimeSettings.SpatialGpuInstancing);

            BuildEnvironment();
            BuildWorld();
            BuildDroneAndCamera();
            BuildProceduralBuildings();
            BuildWindFields();
            BuildAudio();
            BuildMusicReactiveSky();
            BuildWeather();
            BuildInterface();
            BuildEnemyGameplay();
            BuildDeliveryGameplay();
            BuildDustDevils();
            BuildDynamicCourierGameplay();
            BuildDroneWeapon();
            BuildEnvironmentalHazards();

            DuneVectorRendererFrustumCuller rendererCuller = gameObject.AddComponent<DuneVectorRendererFrustumCuller>();
            rendererCuller.Initialize(DroneCamera.Camera, RuntimeSettings.RendererFrustumCulling);

#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool("DuneVector.ValidationRequested", false))
            {
                gameObject.AddComponent<DuneVectorPlayModeValidator>();
            }
#endif
        }

        private void ApplyRetroCrtScanlines()
        {
            RetroCrtScanlineTuning scanlines = RuntimeSettings.RetroCrtScanlines;
            if (scanlines?.Material == null)
            {
                Debug.LogWarning(
                    "Dune Vector Retro CRT scanlines have no fullscreen material assigned in Runtime Settings.",
                    this);
                return;
            }

            scanlines.Material.SetFloat(
                "_ScanlineHeight",
                Mathf.Max(1f, scanlines.ScanlineHeight));
            scanlines.Material.SetFloat(
                "_ScanlineStrength",
                scanlines.Enabled ? Mathf.Clamp01(scanlines.ScanlineStrength) : 0f);
        }

        private void BuildWorld()
        {
            GameObject worldObject = new GameObject("Endless Procedural Desert");
            worldObject.transform.SetParent(transform, false);
            World = worldObject.AddComponent<DesertWorldStreamer>();
            World.Rings = Rings;
            World.WorldSeed = DuneGeneration.WorldSeed;
            World.Dunes = DuneGeneration;
            World.Clouds = Clouds;
            World.ChunkResolution = DuneMeshResolution;
            World.ChunkSize = DuneChunkSize;
            World.ActiveRadius = WorldStreaming.ActiveRadius;
            World.PreloadRadius = WorldStreaming.PreloadRadius;
            World.UnloadRadius = WorldStreaming.UnloadRadius;
            World.RefreshInterval = WorldStreaming.RefreshInterval;
            World.ChunksGeneratedPerFrame = WorldStreaming.ChunksGeneratedPerFrame;
            World.GenerationTimeBudgetMilliseconds = WorldStreaming.GenerationTimeBudgetMilliseconds;
            World.EnableCameraFrustumTerrainStreaming = WorldStreaming.EnableCameraFrustumTerrainStreaming;
            World.CameraFrustumMinimumAltitude = WorldStreaming.CameraFrustumMinimumAltitude;
            World.CameraFrustumFullDistanceAltitude = WorldStreaming.CameraFrustumFullDistanceAltitude;
            World.CameraFrustumMinimumDistance = WorldStreaming.CameraFrustumMinimumDistance;
            World.CameraFrustumMaximumDistance = WorldStreaming.CameraFrustumMaximumDistance;
            World.CameraFrustumPaddingChunks = WorldStreaming.CameraFrustumPaddingChunks;
            World.CameraFrustumUnloadPaddingChunks = WorldStreaming.CameraFrustumUnloadPaddingChunks;
            World.CameraFrustumTerrainHeightPadding = WorldStreaming.CameraFrustumTerrainHeightPadding;
            World.MaximumCameraFrustumTerrainChunks = WorldStreaming.MaximumCameraFrustumTerrainChunks;
            World.CollisionPredictionSeconds = WorldStreaming.CollisionPredictionSeconds;
            World.CollisionPreloadRadius = WorldStreaming.CollisionPreloadRadius;
            World.CollisionActiveRadius = WorldStreaming.CollisionActiveRadius;
            World.SimulationRadius = WorldStreaming.SimulationRadius;
            World.CollisionMeshResolution = WorldStreaming.CollisionMeshResolution;
            World.FloatingOriginThreshold = WorldStreaming.FloatingOriginThreshold;
            World.Cacti = Cacti;
            World.PyramidDensity = Pyramids.DensityPerChunk;
            World.PyramidMinimumScale = Pyramids.MinimumScale;
            World.PyramidMaximumScale = Pyramids.MaximumScale;
            World.PyramidMaximumPlacementSlope = Pyramids.MaximumPlacementSlope;
            World.PyramidMinimumBurialDepth = Pyramids.MinimumBurialDepth;
            World.PyramidMaximumBurialDepth = Pyramids.MaximumBurialDepth;
            World.Obelisks = Obelisks;
            World.Geoglyphs = RuntimeSettings.Geoglyphs;
            World.Shrubs = DesertShrubs;
            World.Landmarks = Contracts.Enabled && WorldHubSettings.Enabled ? LandmarkSettings : null;
            World.GroundExploders = GroundExploders;
            World.Initialize(_materials);
        }

        private void BuildDroneAndCamera()
        {
            Vector2 start = DesertWorldStreamer.StartingLogicalPosition;
            float startHeight = (float)World.HeightField.SampleHeight(start.x, start.y);

            GameObject droneObject = new GameObject("DroneRoot - KCC");
            droneObject.transform.SetParent(transform, false);
            droneObject.transform.position = new Vector3(start.x, startHeight + 0.08f, start.y);

            KinematicCharacterMotor motor = droneObject.AddComponent<KinematicCharacterMotor>();
            motor.SetCapsuleDimensions(0.86f, 1.8f, 0.9f);
            motor.MaxStableSlopeAngle = 57f;
            motor.GroundDetectionExtraDistance = 0.16f;
            motor.MaxStepHeight = 0.45f;
            motor.LedgeAndDenivelationHandling = true;
            motor.MaxStableDenivelationAngle = 72f;
            motor.InteractiveRigidbodyHandling = false;
            motor.MaxMovementIterations = 8;
            motor.MaxDecollisionIterations = 3;
            motor.CheckMovementInitialOverlaps = true;

            Drone = droneObject.AddComponent<DroneCharacterController>();
            Drone.Motor = motor;
            PlayerTuning.ApplyTo(Drone);
            DroneHealth = droneObject.AddComponent<DroneHealth>();
            DroneHealth.Initialize(
                HealthSettings.MaximumHealth,
                HealthSettings.DamageInvulnerability,
                HealthSettings.DebugInfiniteHealth);
            Transform visualRoot = DuneVectorVisuals.CreateDroneVisual(
                droneObject.transform,
                _materials,
                CourierDroneFaction.Player,
                DroneVisuals);
            visualRoot.localPosition = Vector3.up * PlayerTuning.GroundVisualHeight;

            GameObject cameraTargetObject = new GameObject("CameraTarget");
            cameraTargetObject.transform.SetParent(droneObject.transform, false);
            cameraTargetObject.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            Drone.ConfigurePresentation(visualRoot, cameraTargetObject.transform, World);

            DroneStaminaSystem stamina = droneObject.AddComponent<DroneStaminaSystem>();
            stamina.Initialize(PlayerTuning.StaminaBoost);
            DroneBoostSpeedModifier boostSpeedModifier = droneObject.AddComponent<DroneBoostSpeedModifier>();
            boostSpeedModifier.Initialize(PlayerTuning.StaminaBoost);
            Drone.BindStaminaBoost(stamina, boostSpeedModifier);

            GameObject cameraObject = new GameObject("Dune Vector Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.nearClipPlane = PlayerTuning.CameraNearClipPlane;
            camera.farClipPlane = Mathf.Max(PlayerTuning.CameraNearClipPlane, PlayerTuning.CameraFarClipPlane);
            cameraObject.AddComponent<StudioListener>();
            UniversalAdditionalCameraData cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            }
            cameraData.renderPostProcessing = true;
            cameraData.requiresColorTexture = true;
            cameraData.requiresDepthTexture = true;
            ConfigureCameraAntiAliasing(cameraData, PlayerTuning);

            LensFlareComponentSRP lensFlare = cameraObject.AddComponent<LensFlareComponentSRP>();
            lensFlare.lensFlareData = RuntimeSettings.RuntimeCameraLensFlare;

            DroneCamera = cameraObject.AddComponent<DroneCameraController>();
            DroneCamera.Camera = camera;
            DroneCamera.SpeedSource = Drone;
            PlayerTuning.ApplyTo(DroneCamera);
            DroneCamera.IgnoredColliders.Add(motor.Capsule);
            DroneCamera.SetFollowTransform(cameraTargetObject.transform);

            DroneBoostRingTrail boostRingTrail = droneObject.AddComponent<DroneBoostRingTrail>();
            boostRingTrail.Initialize(
                Drone,
                camera,
                _materials.BoostRing,
                Rings,
                BoostRingTrail);

            DroneFlightSwooshRenderer flightSwooshes = cameraObject.AddComponent<DroneFlightSwooshRenderer>();
            flightSwooshes.Initialize(Drone, camera, FlightSwooshes);

            GameObject playerObject = new GameObject("Player Input and Camera Driver");
            playerObject.transform.SetParent(transform, false);
            DroneInput input = playerObject.AddComponent<DroneInput>();
            Player = playerObject.AddComponent<DronePlayer>();
            Player.Character = Drone;
            Player.CharacterCamera = DroneCamera;
            Player.InputSource = input;
            Player.Health = DroneHealth;
            Player.Stamina = stamina;

            World.BindPlayer(Drone, DroneCamera, DroneHealth);
            GoldWallet = Drone.GetComponent<DroneGoldWallet>();
            PermanentUpgrades = droneObject.AddComponent<DronePermanentUpgradeSystem>();
            PermanentUpgrades.Initialize(
                RuntimeSettings,
                GoldWallet,
                Drone,
                DroneHealth,
                stamina,
                boostSpeedModifier);
        }

        private static void ConfigureCameraAntiAliasing(UniversalAdditionalCameraData cameraData, DroneTuning settings)
        {
            if (cameraData == null || settings == null)
            {
                return;
            }

            cameraData.antialiasing = settings.CameraAntiAliasingMode switch
            {
                DuneVectorCameraAntiAliasingMode.TemporalAntiAliasing =>
                    AntialiasingMode.TemporalAntiAliasing,
                DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing =>
                    AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                _ => AntialiasingMode.None,
            };
            cameraData.antialiasingQuality = settings.SmaaQuality switch
            {
                DuneVectorSmaaQuality.Low => AntialiasingQuality.Low,
                DuneVectorSmaaQuality.Medium => AntialiasingQuality.Medium,
                _ => AntialiasingQuality.High,
            };

            int msaaSampleCount = (int)settings.CameraMsaaSampleCount;
            if (UniversalRenderPipeline.asset != null)
            {
                UniversalRenderPipeline.asset.msaaSampleCount = msaaSampleCount;
            }

            Camera camera = cameraData.GetComponent<Camera>();
            if (camera != null)
            {
                camera.allowMSAA = settings.CameraAntiAliasingMode ==
                    DuneVectorCameraAntiAliasingMode.SubpixelMorphologicalAntiAliasing
                    && msaaSampleCount > 1;
            }
        }

        private void BuildAudio()
        {
            if (DuneVectorAudioManager.Instance != null)
            {
                AudioManager = DuneVectorAudioManager.Instance;
                AudioManager.Initialize(
                    AudioSettings,
                    RuntimeSettings.MusicReactiveSky,
                    DroneHealth,
                    Drone,
                    PlayerTuning.CameraAntiAliasingMode);
                return;
            }

            GameObject audioObject = new GameObject("FMOD Audio and Background Music");
            audioObject.transform.SetParent(transform, false);
            AudioManager = audioObject.AddComponent<DuneVectorAudioManager>();
            AudioManager.Initialize(
                AudioSettings,
                RuntimeSettings.MusicReactiveSky,
                DroneHealth,
                Drone,
                PlayerTuning.CameraAntiAliasingMode);
        }

        private void BuildMusicReactiveSky()
        {
            if (!RuntimeSettings.MusicReactiveSky.Enabled)
            {
                return;
            }

            GameObject reactiveSkyObject = new GameObject("Music Reactive Resonance Front");
            reactiveSkyObject.transform.SetParent(transform, false);
            MusicReactiveSky = reactiveSkyObject.AddComponent<DuneVectorMusicReactiveSky>();
            MusicReactiveSky.Initialize(
                AudioManager,
                _environmentSky,
                _environmentBloom,
                DroneCamera.Camera,
                RuntimeSettings.MusicReactiveSky);
            DuneVectorMusicReactiveConductor conductor = reactiveSkyObject.AddComponent<DuneVectorMusicReactiveConductor>();
            conductor.Initialize(AudioManager, MusicReactiveSky, RuntimeSettings.MusicReactiveSky);
        }

        private void BuildEnvironment()
        {
            DesertWeatherAtmosphereTuning atmosphere = WeatherSettings.Atmosphere;
            GameObject sunObject = new GameObject("Desert Sun");
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.78f, 0.58f);
            sun.shadows = atmosphere.SunShadowType;
            sun.shadowResolution = atmosphere.SunShadowResolution;
            sun.GetUniversalAdditionalLightData().softShadowQuality = atmosphere.SunSoftShadowQuality;
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = atmosphere.SunIntensity;
            sun.shadowStrength = Mathf.Clamp01(atmosphere.SunShadowDimmer);

            GameObject volumeObject = new GameObject("URP Desert Environment");
            volumeObject.transform.SetParent(transform, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;

            _runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeVolumeProfile.name = "Runtime Desert URP Profile";
            volume.sharedProfile = _runtimeVolumeProfile;

            _environmentSky = _runtimeVolumeProfile.Add<DuneVectorY2KSky>(true);
            _environmentSky.Top.Override(atmosphere.ClearSkyTop);
            _environmentSky.Middle.Override(atmosphere.ClearSkyMiddle);
            _environmentSky.Bottom.Override(atmosphere.ClearSkyBottom);
            _environmentSky.GradientDiffusion.Override(atmosphere.SkyGradientDiffusion);
            _environmentSky.Multiplier.Override(atmosphere.SkyMultiplier);
            _environmentSky.HorizonGlowColor.Override(atmosphere.ClearHorizonGlowColor);
            _environmentSky.HorizonGlowSize.Override(atmosphere.HorizonGlowSize);
            _environmentSky.HorizonGlowIntensity.Override(atmosphere.ClearHorizonGlowIntensity);
            _environmentSky.CloudColor.Override(atmosphere.ClearSkyCloudColor);
            _environmentSky.CloudHighlight.Override(atmosphere.ClearSkyCloudHighlight);
            _environmentSky.CloudPearl.Override(atmosphere.ClearSkyCloudPearl);
            _environmentSky.CloudOpacity.Override(atmosphere.ClearSkyCloudOpacity);
            _environmentSky.CloudAltitude.Override(atmosphere.SkyCloudAltitude);
            _environmentSky.CloudThickness.Override(atmosphere.SkyCloudThickness);
            _environmentSky.CloudScale.Override(atmosphere.SkyCloudScale);
            _environmentSky.CloudSoftness.Override(atmosphere.SkyCloudSoftness);
            _environmentSky.CloudHighlightStrength.Override(atmosphere.SkyCloudHighlightStrength);
            _environmentSky.CloudPearlStrength.Override(atmosphere.SkyCloudPearlStrength);
            _environmentSky.CloudDriftSpeed.Override(atmosphere.SkyCloudDriftSpeed);
            _environmentSky.StructureColor.Override(atmosphere.DigitalStructureColor);
            _environmentSky.StructureOpacity.Override(atmosphere.ClearDigitalStructureOpacity);
            _environmentSky.ArcAltitude.Override(atmosphere.DigitalArcAltitude);
            _environmentSky.ArcCurvature.Override(atmosphere.DigitalArcCurvature);
            _environmentSky.ArcThickness.Override(atmosphere.DigitalArcThickness);
            _environmentSky.ArcFrequency.Override(atmosphere.DigitalArcFrequency);
            _environmentSky.RingAltitude.Override(atmosphere.DigitalRingAltitude);
            _environmentSky.RingSpacing.Override(atmosphere.DigitalRingSpacing);
            _environmentSky.RingThickness.Override(atmosphere.DigitalRingThickness);
            _environmentSky.GridOpacity.Override(atmosphere.DigitalGridOpacity);
            _environmentSky.GridScale.Override(atmosphere.DigitalGridScale);
            _environmentSky.GridHeight.Override(atmosphere.DigitalGridHeight);
            _environmentSky.GridLineThickness.Override(atmosphere.DigitalGridLineThickness);

            _environmentFog = new DuneVectorUrpFogState();
            _environmentFog.color.Override(atmosphere.ClearFogColor);
            _environmentFog.startDistance.Override(atmosphere.ClearFogStartDistance);
            _environmentFog.meanFreePath.Override(atmosphere.ClearVisibilityDistance);
            _environmentFog.baseHeight.Override(atmosphere.FogBaseHeight);
            _environmentFog.maximumHeight.Override(atmosphere.ClearFogHeight);
            _environmentFog.maxFogDistance.Override(atmosphere.ClearMaximumFogDistance);
            _environmentFog.enableVolumetricFog.Override(false);

            _environmentBloom = FindGlobalBloom(volume);

            PauseMenuVisualTuning videoSettings = AudioSettings.PauseMenu;
            FilmGrain filmGrain = _runtimeVolumeProfile.Add<FilmGrain>(true);
            filmGrain.intensity.Override(videoSettings.VideoFilmGrainIntensity);
            filmGrain.response.Override(videoSettings.VideoFilmGrainResponse);

            DuneVectorUrpEnvironmentDriver environmentDriver = volumeObject.AddComponent<DuneVectorUrpEnvironmentDriver>();
            environmentDriver.Initialize(_environmentSky, _environmentFog);

        }

        private void BuildProceduralBuildings()
        {
            if (!RuntimeSettings.Buildings.Enabled)
            {
                return;
            }

            GameObject buildingObject = new GameObject("Procedural Building Director");
            buildingObject.transform.SetParent(transform, false);
            BuildingDirector = buildingObject.AddComponent<DuneVectorProceduralBuildingDirector>();
            BuildingDirector.Initialize(
                World,
                RuntimeSettings.Buildings,
                RuntimeSettings.Geoglyphs,
                LandmarkDirector);
        }

        private static Bloom FindGlobalBloom(Volume runtimeEnvironmentVolume)
        {
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Volume volume in volumes)
            {
                if (volume == null || volume == runtimeEnvironmentVolume || !volume.isGlobal || volume.sharedProfile == null)
                {
                    continue;
                }

                if (volume.sharedProfile.TryGet(out Bloom bloom))
                {
                    return bloom;
                }
            }

            Debug.LogWarning("Dune Vector global volume is missing Bloom; music-reactive bloom will be unavailable.");
            return null;
        }

        private void BuildWeather()
        {
            if (!WeatherSettings.Enabled)
            {
                return;
            }

            GameObject weatherObject = new GameObject("Dynamic Desert Weather");
            weatherObject.transform.SetParent(transform, false);
            WeatherSystem = weatherObject.AddComponent<DuneVectorWeatherController>();
            WeatherSystem.Initialize(
                Drone,
                DroneCamera.Camera,
                World,
                _environmentFog,
                _environmentSky,
                WeatherSettings);
        }

        private void BuildWindFields()
        {
            if (!WindFieldSettings.Enabled || WindFieldSettings.Fields.Count == 0)
            {
                return;
            }

            GameObject windObject = new GameObject("World-space Wind Fields");
            windObject.transform.SetParent(transform, false);
            WindFieldSystem = windObject.AddComponent<DuneVectorWindFieldSystem>();
            WindFieldSystem.Initialize(Drone, DroneCamera.Camera, World, WindFieldSettings);
        }

        private void BuildDustDevils()
        {
            if (!DustDevilSettings.Enabled)
            {
                return;
            }

            GameObject dustDevilObject = new GameObject("Procedural Sand Funnels and Dust Devils");
            dustDevilObject.transform.SetParent(transform, false);
            DustDevilSystem = dustDevilObject.AddComponent<DuneVectorDustDevilSystem>();
            DustDevilSystem.Initialize(
                Drone,
                Player,
                DroneCamera.Camera,
                World,
                CourierGame,
                DustDevilSettings);
        }

        private void BuildInterface()
        {
            DebugHUD = gameObject.AddComponent<DuneVectorDebugHUD>();
            DebugHUD.Drone = Drone;
            DebugHUD.CameraController = DroneCamera;
            DebugHUD.World = World;
            DebugHUD.Health = DroneHealth;
            DebugHUD.Initialize(RuntimeSettings.BottomHud);

            DroneHealthHUD healthHUD = gameObject.AddComponent<DroneHealthHUD>();
            healthHUD.Initialize(DroneHealth, RuntimeSettings.BottomHud);
            DroneStaminaHUD staminaHUD = gameObject.AddComponent<DroneStaminaHUD>();
            staminaHUD.Initialize(Drone, DroneCamera.Camera, Player.Stamina, PlayerTuning.StaminaBoost);
            DuneVectorCompassHUD compassHUD = gameObject.AddComponent<DuneVectorCompassHUD>();
            compassHUD.Initialize(DroneCamera.Camera, RuntimeSettings.CompassHud);
            MapHUD = gameObject.AddComponent<DuneVectorMapHUD>();
            MapHUD.Initialize(
                Drone,
                World,
                RuntimeSettings.BottomHud,
                RuntimeSettings.MapHud,
                RuntimeSettings.Geoglyphs,
                PermanentUpgrades);
            GameOverController = gameObject.AddComponent<DuneVectorGameOverController>();
            GameOverController.Initialize(
                DroneHealth,
                GameOverScreenSettings,
                PlayerStrikeOrbs,
                VesperKites);
            PauseMenu = gameObject.AddComponent<DuneVectorPauseMenu>();
            PauseMenu.Initialize(
                Player,
                DroneHealth,
                AudioManager,
                GoldWallet,
                PermanentUpgrades,
                PlayerTuning,
                AudioSettings.PauseMenu,
                RuntimeSettings.PermanentUpgrades.ShopVisuals,
                RuntimeSettings.RetroCrtScanlines);
            Photography = gameObject.AddComponent<DuneVectorPhotographySystem>();
            Photography.Initialize(
                Player,
                DroneCamera,
                World,
                RuntimeSettings.Geoglyphs,
                RuntimeSettings.DesertAtlas,
                RuntimeSettings.Photography);
            PauseMenu.BindPhotography(Photography);
        }

        private void BuildDeliveryGameplay()
        {
            if (!Deliveries.Enabled)
            {
                return;
            }

            if (Contracts.Enabled && WorldHubSettings.Enabled)
            {
                GameObject landmarkObject = new GameObject("Procedural Landmark Director");
                landmarkObject.transform.SetParent(transform, false);
                LandmarkDirector = landmarkObject.AddComponent<DuneVectorLandmarkDirector>();
                LandmarkDirector.Initialize(
                    World,
                    _materials,
                    LandmarkSettings,
                    RuntimeSettings.Geoglyphs);

                GameObject courierObject = new GameObject("Courier Hub and Contract Game");
                courierObject.transform.SetParent(transform, false);
                CourierGame = courierObject.AddComponent<DuneVectorCourierGame>();
                CourierGame.Initialize(
                    Player,
                    Drone,
                    DroneHealth,
                    World,
                    DroneCamera.Camera,
                    _materials,
                    GoldWallet,
                    PermanentUpgrades,
                    LandmarkDirector,
                    Deliveries,
                    WindFieldSystem,
                    Contracts,
                    DeliveryMessages,
                    WorldHubSettings,
                    DesertAtlasSettings,
                    RuntimeSettings.CompassHud,
                    EnemyDirector,
                    StormPyramidDirector,
                    VesperKiteDirector);
                DesertAtlas = CourierGame.DesertAtlas;
                PermanentUpgrades.BindAtlasGlyphMaterial(DesertAtlas, _materials);

                GameObject encounterObject = new GameObject("Route Encounter Formation Director");
                encounterObject.transform.SetParent(transform, false);
                RouteEncounterDirector = encounterObject.AddComponent<DuneVectorRouteEncounterDirector>();
                RouteEncounterDirector.Initialize(
                    Drone,
                    DroneHealth,
                    World,
                    _materials,
                    GoldWallet,
                    RouteEncounterSettings,
                    CourierGame);
                CourierGame.BindEncounterDirector(RouteEncounterDirector);
                PauseMenu?.BindCourierGame(CourierGame);
                GameOverController?.BindCourierGame(CourierGame);
                return;
            }

            GameObject deliveryObject = new GameObject("Pickup and Delivery Jobs");
            deliveryObject.transform.SetParent(transform, false);
            DeliveryLoop = deliveryObject.AddComponent<DuneVectorDeliveryLoop>();
            DeliveryLoop.Initialize(Player.Character, World, DroneCamera.Camera, _materials, Deliveries);
        }

        private void BuildEnemyGameplay()
        {
            if (FlyingEnemies.Enabled)
            {
                GameObject enemyObject = new GameObject("Flying Enemy Director");
                enemyObject.transform.SetParent(transform, false);
                EnemyDirector = enemyObject.AddComponent<DuneVectorEnemyDirector>();
                EnemyDirector.Initialize(Drone, DroneHealth, World, _materials, FlyingEnemies);
            }

            if (StormPyramids.Enabled || PlayerStrikeOrbs.Enabled)
            {
                GameObject stormObject = new GameObject("Storm Lightning Enemy Director");
                stormObject.transform.SetParent(transform, false);
                StormPyramidDirector = stormObject.AddComponent<DuneVectorStormPyramidDirector>();
                StormPyramidDirector.Initialize(
                    Drone,
                    DroneHealth,
                    World,
                    _materials,
                    StormPyramids,
                    GroundExploders,
                    PlayerStrikeOrbs);
            }

            if (VesperKites.Enabled)
            {
                GameObject vesperObject = new GameObject("Vesper Kite Director");
                vesperObject.transform.SetParent(transform, false);
                VesperKiteDirector =
                    vesperObject.AddComponent<DuneVectorVesperKiteDirector>();
                VesperKiteDirector.Initialize(
                    Drone,
                    DroneHealth,
                    World,
                    _materials,
                    VesperKites);
            }
        }

        private void BuildDynamicCourierGameplay()
        {
            if (!DynamicCourierSettings.Enabled)
            {
                return;
            }

            GameObject courierWorldObject = new GameObject("Dynamic Courier Rival and Convoy Director");
            courierWorldObject.transform.SetParent(transform, false);
            DynamicCourierDirector = courierWorldObject.AddComponent<DuneVectorDynamicCourierDirector>();
            DynamicCourierDirector.Initialize(
                Drone,
                DroneHealth,
                World,
                DroneCamera.Camera,
                _materials,
                GoldWallet,
                DynamicCourierSettings,
                CourierGame);
        }

        private void BuildDroneWeapon()
        {
            if (!EnergyLauncherSettings.Enabled)
            {
                return;
            }

            GameObject weaponObject = new GameObject("Drone Lock-On Energy Launcher");
            weaponObject.transform.SetParent(transform, false);

            TargetDetector = weaponObject.AddComponent<DroneTargetDetector>();
            TargetDetector.Initialize(DroneCamera.Camera, EnergyLauncherSettings, CourierGame);
            LockOnController = weaponObject.AddComponent<DroneLockOnController>();
            LockOnController.Initialize(TargetDetector, EnergyLauncherSettings, PermanentUpgrades);
            AudioManager?.BindLockOnController(LockOnController);
            EnergyLauncher = weaponObject.AddComponent<DroneEnergyLauncher>();
            EnergyLauncher.Initialize(
                Drone,
                DroneCamera.Camera,
                World,
                AudioManager,
                LockOnController,
                EnergyLauncherSettings,
                PermanentUpgrades);
            LockOnHUD = weaponObject.AddComponent<DroneLockOnHUD>();
            LockOnHUD.Initialize(Drone, DroneCamera.Camera, LockOnController, EnergyLauncherSettings);
            Player.EnergyLauncher = EnergyLauncher;
        }

        private void BuildEnvironmentalHazards()
        {
            if (EnvironmentalHazardSettings == null)
            {
                return;
            }

            GameObject hazardObject = new GameObject("Environmental Hazard Simulation");
            hazardObject.transform.SetParent(transform, false);
            EnvironmentalHazardSystem = hazardObject.AddComponent<DuneVectorEnvironmentalHazardSystem>();
            EnvironmentalHazardSystem.Initialize(
                Drone,
                DroneHealth,
                Player.Stamina,
                EnergyLauncher,
                World,
                WeatherSystem,
                CourierGame,
                EnvironmentalHazardSettings,
                _materials,
                StormPyramids);
            CourierGame?.BindEnvironmentalHazardSystem(EnvironmentalHazardSystem);
            DeliveryLoop?.BindEnvironmentalHazardSystem(EnvironmentalHazardSystem);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            _materials?.Dispose();
            if (_runtimeVolumeProfile != null)
            {
                Destroy(_runtimeVolumeProfile);
            }
            if (_ownsRuntimeSettings && RuntimeSettings != null)
            {
                Destroy(RuntimeSettings);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorDebugHUD : MonoBehaviour
    {
        public DroneCharacterController Drone;
        public DroneCameraController CameraController;
        public DesertWorldStreamer World;
        public DroneHealth Health;
        public bool ShowDebugInformation;

        private BottomHudTuning _bottomHudSettings;
        private GUIStyle _bodyStyle;

        public void Initialize(BottomHudTuning bottomHudSettings)
        {
            _bottomHudSettings = bottomHudSettings;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                ShowDebugInformation = !ShowDebugInformation;
            }
        }

        private void EnsureStyles()
        {
            if (_bodyStyle != null)
            {
                return;
            }

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
            };
        }

        private void OnGUI()
        {
            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (Drone == null || CameraController == null || World == null)
            {
                return;
            }
            EnsureStyles();

            if (_bottomHudSettings != null)
            {
                float speed01 = Mathf.Clamp01(Drone.Speed / Mathf.Max(1f, Drone.CurrentSpeedometerMaximum));
                bool isFlying = Drone.CurrentMode == DroneTraversalMode.Flight;
                DuneVectorBottomHud.DrawMeterPanel(
                    DuneVectorBottomHud.GetPanelRect(_bottomHudSettings, DuneVectorBottomHudPanel.Speed),
                    isFlying
                        ? _bottomHudSettings.FlightSpeedLabel
                        : _bottomHudSettings.GroundSpeedLabel,
                    $"{Drone.Speed:0.0} {_bottomHudSettings.SpeedUnit}",
                    speed01,
                    isFlying
                        ? _bottomHudSettings.FlightSpeedColor
                        : _bottomHudSettings.GroundSpeedColor,
                    _bottomHudSettings);

                float flight01 = Drone.FlightTimeNormalized;
                DuneVectorBottomHud.DrawMeterPanel(
                    DuneVectorBottomHud.GetPanelRect(_bottomHudSettings, DuneVectorBottomHudPanel.FlightReserve),
                    _bottomHudSettings.FlightTimeLabel,
                    $"{Drone.FlightTimeRemaining:0.0} {_bottomHudSettings.FlightTimeUnit}",
                    flight01,
                    Color.Lerp(
                        _bottomHudSettings.FlightReserveLowColor,
                        _bottomHudSettings.FlightReserveFullColor,
                        flight01),
                    _bottomHudSettings);
            }

            if (!ShowDebugInformation)
            {
                return;
            }

            LogicalPosition logical = World.LogicalPlayerPosition;
            string healthState = Health != null && Health.HasInfiniteHealth ? "INFINITE" : "NORMAL";
            string telemetry =
                $"DRONE  Health: {healthState}\n" +
                $"Mode: {Drone.CurrentMode}   Stable grounded: {Drone.IsStableGrounded}\n" +
                $"Velocity: {Drone.Motor.Velocity}   Speed: {Drone.Speed:0.00}\n" +
                $"Boost: {Drone.IsBoosting}   Flight remaining: {Drone.FlightTimeRemaining:0.0}s\n" +
                $"Wind: {Drone.CurrentWindType}  influence {Drone.CurrentWindInfluence:0.00}  force {Drone.CurrentWindForce}\n" +
                $"Logical position: {logical}\n" +
                $"CAMERA  Sharpness: {CameraController.FollowingSharpness:0.00}   Error: {CameraController.FollowingError:0.00} m\n" +
                $"WORLD\n" +
                $"Chunk: {World.CurrentLogicalChunk}   Active: {World.ActiveChunkCount}\n" +
                $"Generated: {World.GeneratedChunkCount}   Unloaded: {World.UnloadedChunkCount}\n" +
                $"Origin: ({World.OriginOffsetX:0}, {World.OriginOffsetZ:0})   Rebases: {World.RebaseCount}";
            float debugPanelWidth = Mathf.Min(390f, Mathf.Max(1f, Screen.width - 20f));
            float debugContentWidth = Mathf.Max(1f, debugPanelWidth - 24f);
            float debugContentHeight = _bodyStyle.CalcHeight(new GUIContent(telemetry), debugContentWidth);
            float debugPanelHeight = Mathf.Min(debugContentHeight + 16f, Mathf.Max(1f, Screen.height - 20f));
            Rect debugPanel = new Rect(10f, 10f, debugPanelWidth, debugPanelHeight);
            GUI.Box(debugPanel, GUIContent.none);
            GUI.Label(
                new Rect(debugPanel.x + 12f, debugPanel.y + 8f, debugContentWidth, debugPanelHeight - 16f),
                telemetry,
                _bodyStyle);
        }
    }
}
