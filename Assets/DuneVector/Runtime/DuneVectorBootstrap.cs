using KinematicCharacterController;
using FMODUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

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
        public CloudTuning Clouds => RuntimeSettings.Clouds;
        public DesertWeatherTuning WeatherSettings => RuntimeSettings.Weather;
        public AudioTuning AudioSettings => RuntimeSettings.Audio;
        public DeliveryTuning Deliveries => RuntimeSettings.Deliveries;
        public PyramidTuning Pyramids => RuntimeSettings.Pyramids;
        public WorldStreamingTuning WorldStreaming => RuntimeSettings.WorldStreaming;
        public PlayerHealthTuning HealthSettings => RuntimeSettings.HealthSettings;
        public FlyingEnemyTuning FlyingEnemies => RuntimeSettings.FlyingEnemies;
        public StormPyramidTuning StormPyramids => RuntimeSettings.StormPyramids;
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
        public DuneVectorDeliveryLoop DeliveryLoop { get; private set; }
        public DroneHealth DroneHealth { get; private set; }
        public DuneVectorEnemyDirector EnemyDirector { get; private set; }
        public DuneVectorStormPyramidDirector StormPyramidDirector { get; private set; }
        public DuneVectorWeatherController WeatherSystem { get; private set; }
        public DuneVectorGameOverController GameOverController { get; private set; }
        public DuneVectorAudioManager AudioManager { get; private set; }
        public DuneVectorPauseMenu PauseMenu { get; private set; }

        private DuneVectorMaterials _materials;
        private VolumeProfile _runtimeVolumeProfile;
        private Fog _environmentFog;
        private GradientSky _environmentSky;
        private Exposure _environmentExposure;
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

            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            _materials = new DuneVectorMaterials();
            _materials.ConfigureStormPyramid(StormPyramids);

            BuildEnvironment();
            BuildWorld();
            BuildDroneAndCamera();
            BuildAudio();
            BuildWeather();
            BuildInterface();
            BuildDeliveryGameplay();
            BuildEnemyGameplay();

#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool("DuneVector.ValidationRequested", false))
            {
                gameObject.AddComponent<DuneVectorPlayModeValidator>();
            }
#endif
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
            World.ChunksGeneratedPerFrame = WorldStreaming.ChunksGeneratedPerFrame;
            World.FloatingOriginThreshold = WorldStreaming.FloatingOriginThreshold;
            World.PyramidDensity = Pyramids.DensityPerChunk;
            World.PyramidMinimumScale = Pyramids.MinimumScale;
            World.PyramidMaximumScale = Pyramids.MaximumScale;
            World.PyramidMaximumPlacementSlope = Pyramids.MaximumPlacementSlope;
            World.PyramidMinimumBurialDepth = Pyramids.MinimumBurialDepth;
            World.PyramidMaximumBurialDepth = Pyramids.MaximumBurialDepth;
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
            DroneHealth.Initialize(HealthSettings.MaximumHealth, HealthSettings.DamageInvulnerability);
            Transform visualRoot = DuneVectorVisuals.CreateDroneVisual(droneObject.transform, _materials);

            GameObject cameraTargetObject = new GameObject("CameraTarget");
            cameraTargetObject.transform.SetParent(droneObject.transform, false);
            cameraTargetObject.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            Drone.ConfigurePresentation(visualRoot, cameraTargetObject.transform, World);

            GameObject cameraObject = new GameObject("Dune Vector Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.allowHDR = true;
            camera.farClipPlane = 900f;
            camera.nearClipPlane = 0.08f;
            cameraObject.AddComponent<StudioListener>();
            if (cameraObject.GetComponent<HDAdditionalCameraData>() == null)
            {
                cameraObject.AddComponent<HDAdditionalCameraData>();
            }

            DroneCamera = cameraObject.AddComponent<DroneCameraController>();
            DroneCamera.Camera = camera;
            DroneCamera.SpeedSource = Drone;
            PlayerTuning.ApplyTo(DroneCamera);
            DroneCamera.IgnoredColliders.Add(motor.Capsule);
            DroneCamera.SetFollowTransform(cameraTargetObject.transform);

            GameObject playerObject = new GameObject("Player Input and Camera Driver");
            playerObject.transform.SetParent(transform, false);
            DroneInput input = playerObject.AddComponent<DroneInput>();
            Player = playerObject.AddComponent<DronePlayer>();
            Player.Character = Drone;
            Player.CharacterCamera = DroneCamera;
            Player.InputSource = input;
            Player.Health = DroneHealth;

            World.BindPlayer(Drone, DroneCamera, DroneHealth);
        }

        private void BuildAudio()
        {
            GameObject audioObject = new GameObject("FMOD Audio and Background Music");
            audioObject.transform.SetParent(transform, false);
            AudioManager = audioObject.AddComponent<DuneVectorAudioManager>();
            AudioManager.Initialize(AudioSettings);
        }

        private void BuildEnvironment()
        {
            GameObject sunObject = new GameObject("Desert Sun");
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.78f, 0.58f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.86f;
            sun.shadowResolution = LightShadowResolution.VeryHigh;
            HDAdditionalLightData sunData = sunObject.GetComponent<HDAdditionalLightData>();
            if (sunData == null)
            {
                sunData = sunObject.AddComponent<HDAdditionalLightData>();
            }
            sunData.SetShadowResolutionOverride(false);
            sunData.SetShadowResolutionLevel(3);
            sun.lightUnit = LightUnit.Lux;
            sun.intensity = 76f;

            GameObject volumeObject = new GameObject("HDRP Desert Environment");
            volumeObject.transform.SetParent(transform, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;

            _runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeVolumeProfile.name = "Runtime Desert HDRP Profile";
            volume.sharedProfile = _runtimeVolumeProfile;

            VisualEnvironment environment = _runtimeVolumeProfile.Add<VisualEnvironment>(true);
            environment.skyType.Override((int)SkyType.Gradient);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            DesertWeatherAtmosphereTuning atmosphere = WeatherSettings.Atmosphere;
            _environmentSky = _runtimeVolumeProfile.Add<GradientSky>(true);
            _environmentSky.top.Override(atmosphere.ClearSkyTop);
            _environmentSky.middle.Override(atmosphere.ClearSkyMiddle);
            _environmentSky.bottom.Override(atmosphere.ClearSkyBottom);
            _environmentSky.gradientDiffusion.Override(atmosphere.SkyGradientDiffusion);
            _environmentSky.multiplier.Override(atmosphere.SkyMultiplier);

            _environmentExposure = _runtimeVolumeProfile.Add<Exposure>(true);
            _environmentExposure.mode.Override(ExposureMode.Fixed);
            _environmentExposure.fixedExposure.Override(atmosphere.ClearExposure);

            _environmentFog = _runtimeVolumeProfile.Add<Fog>(true);
            _environmentFog.enabled.Override(true);
            _environmentFog.colorMode.Override(FogColorMode.SkyColor);
            _environmentFog.meanFreePath.Override(atmosphere.ClearVisibilityDistance);
            _environmentFog.baseHeight.Override(atmosphere.FogBaseHeight);
            _environmentFog.maximumHeight.Override(atmosphere.ClearFogHeight);
            _environmentFog.maxFogDistance.Override(atmosphere.ClearMaximumFogDistance);
            _environmentFog.enableVolumetricFog.Override(false);

            Bloom bloom = _runtimeVolumeProfile.Add<Bloom>(true);
            bloom.intensity.Override(0.12f);
            bloom.threshold.Override(1.2f);
            bloom.scatter.Override(0.58f);

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
                _environmentExposure,
                WeatherSettings);
        }

        private void BuildInterface()
        {
            DebugHUD = gameObject.AddComponent<DuneVectorDebugHUD>();
            DebugHUD.Drone = Drone;
            DebugHUD.CameraController = DroneCamera;
            DebugHUD.World = World;

            DroneHealthHUD healthHUD = gameObject.AddComponent<DroneHealthHUD>();
            healthHUD.Health = DroneHealth;
            GameOverController = gameObject.AddComponent<DuneVectorGameOverController>();
            GameOverController.Initialize(DroneHealth);
            PauseMenu = gameObject.AddComponent<DuneVectorPauseMenu>();
            PauseMenu.Initialize(Player, DroneHealth, AudioManager, AudioSettings.PauseMenu);
        }

        private void BuildDeliveryGameplay()
        {
            if (!Deliveries.Enabled)
            {
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

            if (StormPyramids.Enabled)
            {
                GameObject stormObject = new GameObject("Storm Pyramid Director");
                stormObject.transform.SetParent(transform, false);
                StormPyramidDirector = stormObject.AddComponent<DuneVectorStormPyramidDirector>();
                StormPyramidDirector.Initialize(Drone, DroneHealth, World, _materials, StormPyramids);
            }
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
        public bool ShowDebugInformation;

        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _hintStyle;
        private float _startTime;

        private void Awake()
        {
            _startTime = Time.time;
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
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.84f, 0.96f, 1f) },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
            };
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1f, 0.88f, 0.62f) },
            };
        }

        private void OnGUI()
        {
            if (Drone == null || CameraController == null || World == null)
            {
                return;
            }
            EnsureStyles();

            float elapsed = Time.time - _startTime;
            if (elapsed < 9f)
            {
                float alpha = elapsed < 6.5f ? 1f : Mathf.Clamp01((9f - elapsed) / 2.5f);
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                float panelWidth = Mathf.Min(600f, Mathf.Max(280f, Screen.width - 32f));
                Rect panel = new Rect((Screen.width - panelWidth) * 0.5f, 20f, panelWidth, 104f);
                GUI.Box(panel, GUIContent.none);
                Rect content = new Rect(panel.x + 14f, panel.y, panel.width - 28f, panel.height);
                GUI.Label(new Rect(content.x, panel.y + 8f, content.width, 30f), "DUNE VECTOR", _titleStyle);
                GUI.Label(new Rect(content.x, panel.y + 42f, content.width, 24f), "WASD Move  •  Mouse Look  •  Space Jump  •  F1 Telemetry", _hintStyle);
                GUI.Label(new Rect(content.x, panel.y + 69f, content.width, 22f), "Amber: Boost  •  Cyan: Flight", _hintStyle);
                GUI.color = previous;
            }

            float speed01 = Mathf.Clamp01(Drone.Speed / Mathf.Max(1f, Drone.MaximumFlightSpeed));
            Rect speedPanel = new Rect(24f, Screen.height - 82f, 310f, 48f);
            GUI.Box(speedPanel, GUIContent.none);
            GUI.Label(new Rect(speedPanel.x + 12f, speedPanel.y + 5f, 150f, 20f), $"{Drone.CurrentMode.ToString().ToUpperInvariant()}  {Drone.Speed:0.0} m/s", _bodyStyle);
            Rect bar = new Rect(speedPanel.x + 12f, speedPanel.y + 29f, speedPanel.width - 24f, 8f);
            GUI.Box(bar, GUIContent.none);
            Color oldColor = GUI.color;
            GUI.color = Drone.CurrentMode == DroneTraversalMode.Flight ? new Color(0f, 0.8f, 1f) : new Color(1f, 0.48f, 0.05f);
            GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * speed01, bar.height - 2f), Texture2D.whiteTexture);
            GUI.color = oldColor;

            if (Drone.CurrentMode == DroneTraversalMode.Flight)
            {
                Rect flightPanel = new Rect((Screen.width * 0.5f) - 180f, Screen.height - 86f, 360f, 52f);
                GUI.Box(flightPanel, GUIContent.none);
                GUI.Label(
                    new Rect(flightPanel.x + 12f, flightPanel.y + 4f, flightPanel.width - 24f, 22f),
                    $"FLIGHT  {Drone.FlightTimeRemaining:0.0}s",
                    _hintStyle);
                Rect flightBar = new Rect(flightPanel.x + 12f, flightPanel.y + 31f, flightPanel.width - 24f, 10f);
                GUI.Box(flightBar, GUIContent.none);
                float flight01 = Drone.FlightTimeNormalized;
                GUI.color = Color.Lerp(new Color(1f, 0.2f, 0.08f), new Color(0f, 0.85f, 1f), flight01);
                GUI.DrawTexture(
                    new Rect(flightBar.x + 1f, flightBar.y + 1f, (flightBar.width - 2f) * flight01, flightBar.height - 2f),
                    Texture2D.whiteTexture);
                GUI.color = oldColor;
            }

            if (!ShowDebugInformation)
            {
                return;
            }

            Rect debugPanel = new Rect(20f, 145f, 390f, 236f);
            GUI.Box(debugPanel, GUIContent.none);
            LogicalPosition logical = World.LogicalPlayerPosition;
            string telemetry =
                $"DRONE\n" +
                $"Mode: {Drone.CurrentMode}   Stable grounded: {Drone.IsStableGrounded}\n" +
                $"Velocity: {Drone.Motor.Velocity}   Speed: {Drone.Speed:0.00}\n" +
                $"Boost: {Drone.IsBoosting}   Flight remaining: {Drone.FlightTimeRemaining:0.0}s\n" +
                $"Logical position: {logical}\n\n" +
                $"CAMERA\n" +
                $"FollowingSharpness: {CameraController.FollowingSharpness:0.00}\n" +
                $"Follow error: {CameraController.FollowingError:0.00} m\n\n" +
                $"WORLD\n" +
                $"Chunk: {World.CurrentLogicalChunk}   Active: {World.ActiveChunkCount}\n" +
                $"Generated: {World.GeneratedChunkCount}   Unloaded: {World.UnloadedChunkCount}\n" +
                $"Origin: ({World.OriginOffsetX:0}, {World.OriginOffsetZ:0})   Rebases: {World.RebaseCount}";
            GUI.Label(new Rect(debugPanel.x + 12f, debugPanel.y + 8f, debugPanel.width - 24f, debugPanel.height - 16f), telemetry, _bodyStyle);
        }
    }
}
