using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum DesertWeatherState
    {
        Clear,
        DustBuilding,
        ApproachingStorm,
        FullSandstorm,
        Fading,
    }

    public readonly struct DesertWeatherSnapshot
    {
        public readonly DesertWeatherState State;
        public readonly float StateProgress;
        public readonly float StormIntensity;
        public readonly Vector3 WindDirection;
        public readonly float WindSpeed;

        public DesertWeatherSnapshot(
            DesertWeatherState state,
            float stateProgress,
            float stormIntensity,
            Vector3 windDirection,
            float windSpeed)
        {
            State = state;
            StateProgress = stateProgress;
            StormIntensity = stormIntensity;
            WindDirection = windDirection;
            WindSpeed = windSpeed;
        }
    }

    public sealed class DesertWeatherStateMachine
    {
        public DesertWeatherState CurrentState { get; private set; }

        private readonly DesertWeatherCycleTuning _cycle;
        private readonly DesertWeatherWindTuning _wind;
        private readonly System.Random _random;
        private float _stateTime;
        private float _stateDuration;
        private bool _completedInitialClear;

        public DesertWeatherStateMachine(
            DesertWeatherCycleTuning cycle,
            DesertWeatherWindTuning wind,
            int worldSeed)
        {
            _cycle = cycle;
            _wind = wind;
            _random = new System.Random(unchecked(worldSeed ^ cycle.RandomSeedOffset));
            if (cycle.StartWithFullSandstorm)
            {
                CurrentState = DesertWeatherState.FullSandstorm;
                _stateDuration = RandomRange(
                    cycle.MinimumFullStormDuration,
                    cycle.MaximumFullStormDuration);
            }
            else
            {
                CurrentState = DesertWeatherState.Clear;
                _stateDuration = Mathf.Max(0.1f, cycle.InitialClearDuration);
            }
        }

        public DesertWeatherSnapshot Tick(float deltaTime)
        {
            _stateTime += Mathf.Max(0f, deltaTime);
            while (_stateTime >= _stateDuration)
            {
                _stateTime -= _stateDuration;
                AdvanceState();
            }

            float progress = Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, _stateDuration));
            float shapedProgress = Mathf.SmoothStep(0f, 1f, progress);
            float buildIntensity = Mathf.Clamp01(_cycle.DustBuildingIntensity);
            float rawIntensity;
            switch (CurrentState)
            {
                case DesertWeatherState.DustBuilding:
                    rawIntensity = Mathf.Lerp(0f, buildIntensity, shapedProgress);
                    break;
                case DesertWeatherState.ApproachingStorm:
                    rawIntensity = Mathf.Lerp(buildIntensity, 1f, shapedProgress);
                    break;
                case DesertWeatherState.FullSandstorm:
                    rawIntensity = 1f;
                    break;
                case DesertWeatherState.Fading:
                    rawIntensity = 1f - shapedProgress;
                    break;
                default:
                    rawIntensity = 0f;
                    break;
            }

            Vector2 configuredDirection = _wind.Direction.sqrMagnitude > 0.0001f
                ? _wind.Direction.normalized
                : Vector2.right;
            Vector3 windDirection = new Vector3(configuredDirection.x, 0f, configuredDirection.y);
            float stormIntensity = rawIntensity * Mathf.Clamp01(_cycle.MaximumStormIntensity);
            float windSpeed = Mathf.Lerp(
                Mathf.Max(0f, _wind.ClearWindSpeed),
                Mathf.Max(0f, _wind.StormWindSpeed),
                stormIntensity);
            return new DesertWeatherSnapshot(
                CurrentState,
                progress,
                stormIntensity,
                windDirection,
                windSpeed);
        }

        private void AdvanceState()
        {
            switch (CurrentState)
            {
                case DesertWeatherState.Clear:
                    _completedInitialClear = true;
                    EnterState(DesertWeatherState.DustBuilding, _cycle.DustBuildingDuration);
                    break;
                case DesertWeatherState.DustBuilding:
                    EnterState(DesertWeatherState.ApproachingStorm, _cycle.ApproachingStormDuration);
                    break;
                case DesertWeatherState.ApproachingStorm:
                    EnterState(
                        DesertWeatherState.FullSandstorm,
                        RandomRange(_cycle.MinimumFullStormDuration, _cycle.MaximumFullStormDuration));
                    break;
                case DesertWeatherState.FullSandstorm:
                    EnterState(DesertWeatherState.Fading, _cycle.FadingDuration);
                    break;
                default:
                    EnterState(
                        DesertWeatherState.Clear,
                        _completedInitialClear
                            ? RandomRange(_cycle.MinimumClearDuration, _cycle.MaximumClearDuration)
                            : _cycle.InitialClearDuration);
                    break;
            }
        }

        private void EnterState(DesertWeatherState state, float duration)
        {
            CurrentState = state;
            _stateDuration = Mathf.Max(0.1f, duration);
        }

        private float RandomRange(float minimum, float maximum)
        {
            float safeMinimum = Mathf.Max(0.1f, minimum);
            float safeMaximum = Mathf.Max(safeMinimum, maximum);
            return Mathf.Lerp(safeMinimum, safeMaximum, (float)_random.NextDouble());
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorWeatherController : MonoBehaviour
    {
        public DesertWeatherState CurrentState { get; private set; }
        public float CurrentStateProgress { get; private set; }
        public float CurrentStormIntensity { get; private set; }
        public Vector3 CurrentWindDirection { get; private set; }
        public float CurrentWindSpeed { get; private set; }

        private DesertWeatherStateMachine _stateMachine;
        private DuneVectorWeatherAtmosphere _atmosphere;
        private DuneVectorWeatherWind _wind;
        private DuneVectorWeatherDustField _dust;

        public void Initialize(
            DroneCharacterController player,
            Camera viewCamera,
            DesertWorldStreamer world,
            DuneVectorUrpFogState fog,
            DuneVectorY2KSky sky,
            DesertWeatherTuning settings)
        {
            settings.EnsureInitialized();
            _stateMachine = new DesertWeatherStateMachine(settings.Cycle, settings.Wind, world.WorldSeed);

            GameObject atmosphereObject = new GameObject("URP Weather Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            _atmosphere = atmosphereObject.AddComponent<DuneVectorWeatherAtmosphere>();
            _atmosphere.Initialize(fog, sky, settings.Atmosphere);

            GameObject windObject = new GameObject("Global Desert Wind");
            windObject.transform.SetParent(transform, false);
            _wind = windObject.AddComponent<DuneVectorWeatherWind>();
            _wind.Initialize(settings.Wind);

            GameObject dustObject = new GameObject("Recycled Dust and Sand Layers");
            dustObject.transform.SetParent(transform, false);
            _dust = dustObject.AddComponent<DuneVectorWeatherDustField>();
            _dust.Initialize(player, viewCamera, world, settings.Wind, settings.Dust);

            ApplySnapshot(_stateMachine.Tick(0f));
        }

        private void Update()
        {
            if (_stateMachine == null)
            {
                return;
            }
            ApplySnapshot(_stateMachine.Tick(Time.deltaTime));
        }

        private void ApplySnapshot(DesertWeatherSnapshot snapshot)
        {
            CurrentState = snapshot.State;
            CurrentStateProgress = snapshot.StateProgress;
            CurrentStormIntensity = snapshot.StormIntensity;
            CurrentWindDirection = snapshot.WindDirection;
            CurrentWindSpeed = snapshot.WindSpeed;
            _atmosphere.Apply(snapshot);
            _wind.Apply(snapshot);
            _dust.Apply(snapshot, Time.deltaTime);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorWeatherAtmosphere : MonoBehaviour
    {
        private DuneVectorUrpFogState _fog;
        private DuneVectorY2KSky _sky;
        private DesertWeatherAtmosphereTuning _settings;

        public void Initialize(
            DuneVectorUrpFogState fog,
            DuneVectorY2KSky sky,
            DesertWeatherAtmosphereTuning settings)
        {
            _fog = fog;
            _sky = sky;
            _settings = settings;
        }

        public void Apply(DesertWeatherSnapshot snapshot)
        {
            if (_settings == null)
            {
                return;
            }

            float intensity = Mathf.SmoothStep(0f, 1f, snapshot.StormIntensity);
            if (_fog != null)
            {
                _fog.color.value = Color.Lerp(
                    _settings.ClearFogColor,
                    _settings.StormFogColor,
                    intensity);
                _fog.startDistance.value = Mathf.Lerp(
                    _settings.ClearFogStartDistance,
                    _settings.StormFogStartDistance,
                    intensity);
                _fog.meanFreePath.value = Mathf.Lerp(
                    Mathf.Max(10f, _settings.ClearVisibilityDistance),
                    Mathf.Max(10f, _settings.StormVisibilityDistance),
                    intensity);
                _fog.maxFogDistance.value = Mathf.Lerp(
                    Mathf.Max(20f, _settings.ClearMaximumFogDistance),
                    Mathf.Max(20f, _settings.StormMaximumFogDistance),
                    intensity);
                _fog.baseHeight.value = _settings.FogBaseHeight;
                _fog.maximumHeight.value = Mathf.Lerp(
                    Mathf.Max(1f, _settings.ClearFogHeight),
                    Mathf.Max(1f, _settings.StormFogHeight),
                    intensity);
                _fog.enableVolumetricFog.value = intensity >= _settings.VolumetricFogThreshold;
            }

            if (_sky != null)
            {
                _sky.Top.value = Color.Lerp(_settings.ClearSkyTop, _settings.StormSkyTop, intensity);
                _sky.Middle.value = Color.Lerp(_settings.ClearSkyMiddle, _settings.StormSkyMiddle, intensity);
                _sky.Bottom.value = Color.Lerp(_settings.ClearSkyBottom, _settings.StormSkyBottom, intensity);
                _sky.HorizonGlowColor.value = Color.Lerp(
                    _settings.ClearHorizonGlowColor,
                    _settings.StormHorizonGlowColor,
                    intensity);
                _sky.HorizonGlowIntensity.value = Mathf.Lerp(
                    _settings.ClearHorizonGlowIntensity,
                    _settings.StormHorizonGlowIntensity,
                    intensity);
                _sky.CloudColor.value = Color.Lerp(
                    _settings.ClearSkyCloudColor,
                    _settings.StormSkyCloudColor,
                    intensity);
                _sky.CloudHighlight.value = Color.Lerp(
                    _settings.ClearSkyCloudHighlight,
                    _settings.StormSkyCloudHighlight,
                    intensity);
                _sky.CloudPearl.value = Color.Lerp(
                    _settings.ClearSkyCloudPearl,
                    _settings.StormSkyCloudPearl,
                    intensity);
                _sky.CloudOpacity.value = Mathf.Lerp(
                    _settings.ClearSkyCloudOpacity,
                    _settings.StormSkyCloudOpacity,
                    intensity);
                _sky.StructureOpacity.value = Mathf.Lerp(
                    _settings.ClearDigitalStructureOpacity,
                    _settings.StormDigitalStructureOpacity,
                    intensity);
            }

        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorWeatherWind : MonoBehaviour
    {
        private DesertWeatherWindTuning _settings;
        private WindZone _windZone;

        public void Initialize(DesertWeatherWindTuning settings)
        {
            _settings = settings;
            _windZone = gameObject.AddComponent<WindZone>();
            _windZone.mode = WindZoneMode.Directional;
            _windZone.radius = 0f;
        }

        public void Apply(DesertWeatherSnapshot snapshot)
        {
            if (_settings == null || _windZone == null)
            {
                return;
            }

            if (snapshot.WindDirection.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(snapshot.WindDirection, Vector3.up);
            }
            float intensity = snapshot.StormIntensity;
            _windZone.windMain = snapshot.WindSpeed * Mathf.Max(0f, _settings.WindZoneStrengthMultiplier);
            _windZone.windTurbulence = Mathf.Lerp(
                Mathf.Max(0f, _settings.ClearTurbulence),
                Mathf.Max(0f, _settings.StormTurbulence),
                intensity);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorWeatherDustField : MonoBehaviour
    {
        private DroneCharacterController _player;
        private Camera _camera;
        private DesertWorldStreamer _world;
        private DesertWeatherWindTuning _windSettings;
        private DesertWeatherDustTuning _settings;
        private ParticleSystem _groundDust;
        private ParticleSystem _airborneSand;
        private ParticleSystem _approachingFront;
        private ParticleSystem _closeSand;
        private Material[] _materials;
        private Texture2D _particleTexture;
        private System.Random _random;
        private float _groundEmissionAccumulator;

        public void Initialize(
            DroneCharacterController player,
            Camera viewCamera,
            DesertWorldStreamer world,
            DesertWeatherWindTuning windSettings,
            DesertWeatherDustTuning settings)
        {
            _player = player;
            _camera = viewCamera;
            _world = world;
            _windSettings = windSettings;
            _settings = settings;
            _random = new System.Random(unchecked(world.WorldSeed ^ 0x5a17d391));
            _particleTexture = CreateSoftParticleTexture();
            _materials = new Material[4];
            for (int i = 0; i < _materials.Length; i++)
            {
                _materials[i] = CreateDustMaterial($"Desert Weather Dust {i + 1}");
            }

            _groundDust = CreateParticleLayer(
                "Ground Surface Dust",
                _settings.GroundParticleBudget,
                _materials[0],
                false,
                1.6f);
            _airborneSand = CreateParticleLayer(
                "Airborne Sand Sheet",
                _settings.AirborneParticleBudget,
                _materials[1],
                true,
                _settings.SandStreakLength * 0.7f);
            _approachingFront = CreateParticleLayer(
                "Approaching Sand Front",
                _settings.ApproachingFrontParticleBudget,
                _materials[2],
                true,
                _settings.SandStreakLength);
            _closeSand = CreateParticleLayer(
                "Camera Proximity Sand",
                _settings.CloseParticleBudget,
                _materials[3],
                true,
                _settings.SandStreakLength * 1.25f);

            ConfigureAutomaticShape(_airborneSand, new Vector3(
                _settings.FieldRadius * 2f,
                _settings.AirborneLayerHeight,
                _settings.FieldRadius * 2f));
            ConfigureAutomaticShape(_approachingFront, new Vector3(
                _settings.FieldRadius * 2.4f,
                _settings.AirborneLayerHeight * 1.4f,
                _settings.FieldRadius * 0.22f));
            ConfigureAutomaticShape(_closeSand, new Vector3(
                _settings.CloseLayerRadius * 2f,
                _settings.CloseLayerRadius * 0.8f,
                _settings.CloseLayerRadius * 1.25f));

            _world.WorldShifted += HandleWorldShift;
        }

        public void Apply(DesertWeatherSnapshot snapshot, float deltaTime)
        {
            if (_player == null || _world == null || _settings == null)
            {
                return;
            }
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            Vector3 playerPosition = _player.WorldCenter;
            float groundHeight = _world.SampleHeightAtLocal(playerPosition.x, playerPosition.z);
            Quaternion windRotation = snapshot.WindDirection.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(snapshot.WindDirection, Vector3.up)
                : Quaternion.identity;
            float radius = Mathf.Max(10f, _settings.FieldRadius);

            _groundDust.transform.SetPositionAndRotation(
                new Vector3(playerPosition.x, groundHeight + (_settings.GroundLayerHeight * 0.35f), playerPosition.z),
                windRotation);
            _airborneSand.transform.SetPositionAndRotation(
                new Vector3(playerPosition.x, groundHeight + (_settings.AirborneLayerHeight * 0.5f), playerPosition.z),
                windRotation);

            float frontDistance = snapshot.State == DesertWeatherState.ApproachingStorm
                ? Mathf.Lerp(
                    radius * _settings.ApproachingFrontStartDistance,
                    radius * _settings.ApproachingFrontEndDistance,
                    Mathf.SmoothStep(0f, 1f, snapshot.StateProgress))
                : radius * _settings.ApproachingFrontEndDistance;
            Vector3 frontCenter = playerPosition - (snapshot.WindDirection * frontDistance);
            frontCenter.y = groundHeight + (_settings.AirborneLayerHeight * 0.58f);
            _approachingFront.transform.SetPositionAndRotation(frontCenter, windRotation);

            Vector3 closeCenter = _camera != null ? _camera.transform.position : playerPosition;
            _closeSand.transform.SetPositionAndRotation(closeCenter, windRotation);

            Vector3 playerVelocity = _player.Motor != null ? _player.Motor.Velocity : Vector3.zero;
            Vector3 relativeWind = (snapshot.WindDirection * snapshot.WindSpeed)
                - (playerVelocity * Mathf.Max(0f, _windSettings.PlayerVelocityInfluence));
            SetLayerVelocity(_groundDust, (relativeWind * _settings.GroundWindResponse) + (Vector3.up * 0.16f), snapshot.StormIntensity);
            SetLayerVelocity(_airborneSand, relativeWind * _settings.AirborneWindResponse, snapshot.StormIntensity);
            SetLayerVelocity(_approachingFront, relativeWind, snapshot.StormIntensity);
            SetLayerVelocity(_closeSand, relativeWind * _settings.CloseWindResponse, snapshot.StormIntensity);

            float ambientDensity = Mathf.Clamp01(_settings.AmbientDustDensity);
            float ambientAirborneDensity = Mathf.Clamp01(_settings.AmbientAirborneSandDensity);
            float clearWeatherBlend = 1f - snapshot.StormIntensity;
            float stormDensity = Mathf.Max(0f, _settings.StormDustDensity) * snapshot.StormIntensity;
            float groundDensity = Mathf.Lerp(ambientDensity, _settings.StormDustDensity, snapshot.StormIntensity);
            float airborneDensity = ambientAirborneDensity
                + (stormDensity * Mathf.SmoothStep(0f, 1f, snapshot.StormIntensity));
            float closeDensity = (ambientAirborneDensity
                    * Mathf.Clamp01(_settings.AmbientAirborneSandProximityDensityMultiplier))
                + (stormDensity * Mathf.Pow(snapshot.StormIntensity, 1.35f));
            float frontDensity = EvaluateFrontDensity(snapshot) * Mathf.Max(0f, _settings.StormDustDensity);
            float ambientSizeMultiplier = Mathf.Lerp(
                1f,
                Mathf.Max(0f, _settings.AmbientAirborneSandSizeMultiplier),
                clearWeatherBlend);
            float ambientOpacityMultiplier = Mathf.Lerp(
                1f,
                Mathf.Max(0f, _settings.AmbientAirborneSandOpacityMultiplier),
                clearWeatherBlend);

            Color dustColor = Color.Lerp(
                _settings.AmbientDustColor,
                _settings.StormDustColor,
                snapshot.StormIntensity);
            EmitGroundDust(groundDensity, dustColor, deltaTime);
            SetAutomaticLayer(
                _airborneSand,
                airborneDensity,
                _settings.AirborneEmissionRate,
                dustColor,
                ambientSizeMultiplier,
                ambientOpacityMultiplier);
            SetAutomaticLayer(_approachingFront, frontDensity, _settings.ApproachingFrontEmissionRate, dustColor);
            SetAutomaticLayer(
                _closeSand,
                closeDensity,
                _settings.CloseEmissionRate,
                dustColor,
                ambientSizeMultiplier,
                ambientOpacityMultiplier);
        }

        private float EvaluateFrontDensity(DesertWeatherSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case DesertWeatherState.DustBuilding:
                    return Mathf.Lerp(0f, 0.18f, snapshot.StateProgress);
                case DesertWeatherState.ApproachingStorm:
                    return Mathf.Lerp(0.22f, 1f, Mathf.SmoothStep(0f, 1f, snapshot.StateProgress));
                case DesertWeatherState.FullSandstorm:
                    return _settings.FullStormFrontDensity * snapshot.StormIntensity;
                case DesertWeatherState.Fading:
                    return 0.3f * snapshot.StormIntensity;
                default:
                    return 0f;
            }
        }

        private void EmitGroundDust(float density, Color color, float deltaTime)
        {
            float rate = Mathf.Max(0f, _settings.GroundEmissionRate) * Mathf.Max(0f, density);
            _groundEmissionAccumulator += rate * Mathf.Max(0f, deltaTime);
            int emissionCount = Mathf.Min(16, Mathf.FloorToInt(_groundEmissionAccumulator));
            _groundEmissionAccumulator -= emissionCount;
            if (emissionCount <= 0)
            {
                return;
            }

            float radius = Mathf.Max(10f, _settings.FieldRadius);
            for (int i = 0; i < emissionCount; i++)
            {
                float angle = RandomRange(0f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt(RandomRange(0f, 1f)) * radius;
                Vector3 center = _player.WorldCenter;
                float x = center.x + (Mathf.Cos(angle) * distance);
                float z = center.z + (Mathf.Sin(angle) * distance);
                float surfaceHeight = _world.SampleHeightAtLocal(x, z);
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, surfaceHeight + RandomRange(0.08f, _settings.GroundLayerHeight), z),
                    startLifetime = RandomRange(_settings.MinimumParticleLifetime, _settings.MaximumParticleLifetime),
                    startSize = RandomRange(_settings.MinimumParticleSize, _settings.MaximumParticleSize * 1.3f),
                    startColor = color,
                    applyShapeToPosition = false,
                };
                _groundDust.Emit(emit, 1);
            }
        }

        private void SetAutomaticLayer(
            ParticleSystem system,
            float density,
            float maximumRate,
            Color color,
            float sizeMultiplier = 1f,
            float opacityMultiplier = 1f)
        {
            float safeDensity = Mathf.Max(0f, density);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Mathf.Max(0f, maximumRate) * safeDensity;
            ParticleSystem.MainModule main = system.main;
            Color particleColor = color;
            particleColor.a *= Mathf.Clamp01(0.25f + safeDensity) * Mathf.Max(0f, opacityMultiplier);
            main.startColor = particleColor;
            float safeSizeMultiplier = Mathf.Max(0f, sizeMultiplier);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.01f, _settings.MinimumParticleSize) * safeSizeMultiplier,
                Mathf.Max(_settings.MinimumParticleSize, _settings.MaximumParticleSize) * safeSizeMultiplier);
        }

        private void SetLayerVelocity(ParticleSystem system, Vector3 velocity, float stormIntensity)
        {
            ParticleSystem.VelocityOverLifetimeModule velocityModule = system.velocityOverLifetime;
            velocityModule.x = velocity.x;
            velocityModule.y = velocity.y;
            velocityModule.z = velocity.z;
            ParticleSystem.NoiseModule noise = system.noise;
            noise.strength = _settings.TurbulenceStrength * Mathf.Lerp(0.25f, 1f, stormIntensity);
            noise.scrollSpeed = Mathf.Lerp(0.15f, 0.7f, stormIntensity);
        }

        private ParticleSystem CreateParticleLayer(
            string layerName,
            int maximumParticles,
            Material material,
            bool automaticEmission,
            float streakLength)
        {
            GameObject layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(transform, false);
            ParticleSystem system = layerObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(32, maximumParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.1f, _settings.MinimumParticleLifetime),
                Mathf.Max(_settings.MinimumParticleLifetime, _settings.MaximumParticleLifetime));
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.01f, _settings.MinimumParticleSize),
                Mathf.Max(_settings.MinimumParticleSize, _settings.MaximumParticleSize));
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = automaticEmission;
            emission.rateOverTime = 0f;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.frequency = Mathf.Max(0.01f, _settings.TurbulenceFrequency);
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.14f),
                    new GradientAlphaKey(0.72f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = fade;

            ParticleSystemRenderer renderer = layerObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = Mathf.Max(0f, _settings.ParticleVelocityStretch);
            renderer.lengthScale = Mathf.Max(0f, streakLength);
            renderer.cameraVelocityScale = Mathf.Max(0f, _settings.CameraVelocityStretch);
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play(true);
            return system;
        }

        private static void ConfigureAutomaticShape(ParticleSystem system, Vector3 size)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
        }

        private Material CreateDustMaterial(string materialName)
        {
            Shader shader = Shader.Find("DuneVector/URP Weather Particle");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            Material material = new Material(shader) { name = materialName };
            material.renderQueue = (int)RenderQueue.Transparent;
            SetMaterialFloat(material, "_Surface", 1f);
            SetMaterialFloat(material, "_Blend", 0f);
            SetMaterialFloat(material, "_ZWrite", 0f);
            SetMaterialFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetMaterialFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetMaterialColor(material, "_BaseColor", Color.white);
            SetMaterialColor(material, "_Tint", Color.white);
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", _particleTexture);
            }
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", _particleTexture);
            }
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ALPHA");
            return material;
        }

        private static Texture2D CreateSoftParticleTexture()
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Soft Sand Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(
                        ((x + 0.5f) / size) - 0.5f,
                        ((y + 0.5f) / size) - 0.5f);
                    float distance = point.magnitude * 2f;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.7f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void SetMaterialFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void SetMaterialColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private float RandomRange(float minimum, float maximum)
        {
            float safeMaximum = Mathf.Max(minimum, maximum);
            return Mathf.Lerp(minimum, safeMaximum, (float)_random.NextDouble());
        }

        private void HandleWorldShift(Vector3 shift)
        {
            _groundDust?.Clear(true);
            _airborneSand?.Clear(true);
            _approachingFront?.Clear(true);
            _closeSand?.Clear(true);
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            if (_materials != null)
            {
                for (int i = 0; i < _materials.Length; i++)
                {
                    if (_materials[i] != null)
                    {
                        Destroy(_materials[i]);
                    }
                }
            }
            if (_particleTexture != null)
            {
                Destroy(_particleTexture);
            }
        }
    }
}
