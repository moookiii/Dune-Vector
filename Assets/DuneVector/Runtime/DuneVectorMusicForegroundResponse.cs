using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicForegroundResponse : MonoBehaviour, IMusicReactiveSink
    {
        private static readonly int PulseOriginsId = Shader.PropertyToID("_DVMusicPulseOrigins");
        private static readonly int PulseParametersId = Shader.PropertyToID("_DVMusicPulseParameters");
        private static readonly int ContinuousId = Shader.PropertyToID("_DVMusicContinuous");
        private static readonly int PulseColorId = Shader.PropertyToID("_DVMusicPulseColor");
        private static readonly ProfilerMarker ShaderGlobalsMarker = new ProfilerMarker("MusicVisualizer.ShaderGlobals");
        private static readonly ProfilerMarker VfxDispatchMarker = new ProfilerMarker("MusicVisualizer.VFXDispatch");

        private struct RoadPulse
        {
            public Vector3 Origin;
            public float Age;
            public float Strength;
            public float Delay;
            public bool Active;
        }

        private Camera _camera;
        private Transform _drone;
        private MusicReactiveSkyTuning _settings;
        private RoadPulse[] _roadPulses;
        private readonly Vector4[] _pulseOrigins = new Vector4[4];
        private readonly Vector4[] _pulseParameters = new Vector4[4];
        private int _roadPulseCursor;
        private int _droppedRoadPulses;
        private Light _reactionLight;
        private float _reactionLightAge;
        private float _reactionLightStrength;
        private ParticleSystem _streaks;
        private ParticleSystem _centerOutStreaks;
        private ParticleSystem.Particle[] _centerOutParticleBuffer;
        private Material _streakMaterial;
        private Color[] _streakPalette;
        private Renderer[] _droneVisualRenderers;
        private Vector3 _droneVisualLocalCenter;
        private bool _hasDroneVisualLocalCenter;
        private TrailRenderer[] _droneTrails;
        private float[] _baseTrailWidths;
        private Color[] _baseTrailStartColors;
        private Color[] _baseTrailEndColors;
        private float _droneKickEnvelope;
        private float _droneThrusterEnvelope;
        private float _continuousBass;

        public int ActiveRoadPulseCount => CountActiveRoadPulses();
        public int DroppedRoadPulseCount => _droppedRoadPulses;
        public bool ReactionLightActive => _reactionLight != null && _reactionLight.enabled;
        public int LiveStreakCount => (_streaks != null ? _streaks.particleCount : 0)
            + (_centerOutStreaks != null ? _centerOutStreaks.particleCount : 0);

        public void Initialize(
            Camera camera,
            Transform drone,
            Material streakMaterial,
            MusicReactiveSkyTuning settings)
        {
            _camera = camera;
            _drone = drone;
            _settings = settings;
            _roadPulses = new RoadPulse[Mathf.Clamp(settings.MaximumRoadPulseCount, 1, _pulseOrigins.Length)];
            _droneVisualRenderers = _drone != null
                ? _drone.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
            CacheDroneVisualLocalCenter();
            CacheDroneTrails();
            BuildReactionLight();
            BuildStreakSystem(streakMaterial);
            Camera.onPreCull += HandleCameraPreCull;
            ResetShaderGlobals();
        }

        private void CacheDroneVisualLocalCenter()
        {
            if (_drone == null)
            {
                return;
            }
            Bounds visualBounds = default;
            bool hasVisualBounds = false;
            for (int i = 0; i < _droneVisualRenderers.Length; i++)
            {
                Renderer renderer = _droneVisualRenderers[i];
                if (renderer == null
                    || !renderer.gameObject.activeInHierarchy
                    || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
                {
                    continue;
                }
                if (hasVisualBounds)
                {
                    visualBounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    visualBounds = renderer.bounds;
                    hasVisualBounds = true;
                }
            }
            _hasDroneVisualLocalCenter = hasVisualBounds;
            _droneVisualLocalCenter = hasVisualBounds
                ? _drone.InverseTransformPoint(visualBounds.center)
                : Vector3.zero;
        }

        private void CacheDroneTrails()
        {
            _droneTrails = _drone != null
                ? _drone.GetComponentsInChildren<TrailRenderer>(true)
                : new TrailRenderer[0];
            _baseTrailWidths = new float[_droneTrails.Length];
            _baseTrailStartColors = new Color[_droneTrails.Length];
            _baseTrailEndColors = new Color[_droneTrails.Length];
            for (int i = 0; i < _droneTrails.Length; i++)
            {
                _baseTrailWidths[i] = _droneTrails[i].widthMultiplier;
                _baseTrailStartColors[i] = _droneTrails[i].startColor;
                _baseTrailEndColors[i] = _droneTrails[i].endColor;
            }
        }

        private void BuildReactionLight()
        {
            GameObject lightObject = new GameObject("Music Structure Reaction Light");
            lightObject.transform.SetParent(transform, false);
            _reactionLight = lightObject.AddComponent<Light>();
            _reactionLight.type = LightType.Point;
            _reactionLight.color = _settings.StructureLightColor;
            _reactionLight.range = _settings.StructureLightRange;
            _reactionLight.intensity = 0f;
            _reactionLight.shadows = LightShadows.None;
            _reactionLight.enabled = false;
        }

        private void BuildStreakSystem(Material sharedMaterial)
        {
            _streakMaterial = new Material(sharedMaterial)
            {
                name = "Music Visualizer - Screen-Space Flare Lines",
                enableInstancing = true,
            };
            _streakMaterial.SetFloat("_ShapeMode", 1f);
            _streakMaterial.SetFloat("_StreakEmission", _settings.ForegroundStreakPaletteEmission);
            _streakMaterial.SetFloat("_StreakTipSharpness", _settings.ForegroundStreakTipSharpness);
            _streakMaterial.SetFloat("_StreakMinimumWidth", _settings.ForegroundStreakMinimumWidth);
            _streakMaterial.SetFloat("_StreakCoreWidth", _settings.ForegroundStreakCoreWidth);
            _streakMaterial.SetFloat("_StreakCoreBrightness", _settings.ForegroundStreakCoreBrightness);
            _streakMaterial.SetFloat("_StreakHaloBrightness", _settings.ForegroundStreakHaloBrightness);
            _streakMaterial.SetFloat("_StreakEndFade", _settings.ForegroundStreakEndFade);
            _streaks = BuildStreakParticleSystem("Music Screen-Space Flare Lines");
            _centerOutStreaks = BuildStreakParticleSystem("Music Drone-Anchored Flare Lines");
            if (_settings.CenterOutCompensateAnchorMotion)
            {
                int capacity = _settings.CenterOutParticlePoolCapacity > 0
                    ? _settings.CenterOutParticlePoolCapacity
                    : _settings.ForegroundStreakParticleBudget;
                _centerOutParticleBuffer = new ParticleSystem.Particle[Mathf.Max(1, capacity)];
            }

            int paletteCount = Mathf.Clamp(_settings.ForegroundStreakPaletteColorCount, 2, 64);
            _streakPalette = new Color[paletteCount];
            for (int i = 0; i < paletteCount; i++)
            {
                Color color = Color.HSVToRGB(
                    i / (float)paletteCount,
                    _settings.ForegroundStreakPaletteSaturation,
                    _settings.ForegroundStreakPaletteValue,
                    true);
                color.a = _settings.ForegroundStreakColor.a;
                _streakPalette[i] = color;
            }
            UpdateCenterOutAnchor();
        }

        private ParticleSystem BuildStreakParticleSystem(string objectName)
        {
            GameObject streakObject = new GameObject(objectName);
            streakObject.transform.SetParent(_camera.transform, false);
            ParticleSystem streaks = streakObject.AddComponent<ParticleSystem>();
            streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = streaks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = _settings.CenterOutParticlePoolCapacity > 0
                ? _settings.CenterOutParticlePoolCapacity
                : _settings.ForegroundStreakParticleBudget;
            main.startLifetime = _settings.ForegroundStreakLifetime;
            main.startSpeed = 0f;
            main.startSize = _settings.ForegroundStreakSize;
            main.startColor = _settings.ForegroundStreakColor;

            ParticleSystem.EmissionModule emission = streaks.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = streaks.shape;
            shape.enabled = false;

            ParticleSystemRenderer renderer = streakObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = _settings.ForegroundStreakVelocityScale;
            renderer.lengthScale = _settings.ForegroundStreakLengthScale;
            renderer.sharedMaterial = _streakMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return streaks;
        }

        public void ApplyContinuous(in MusicReactiveRuntimeState state)
        {
            _continuousBass = state.Bass;
            if ((state.Permissions & MusicVisualEffectGroups.Structures) == 0
                && _reactionLight != null)
            {
                _reactionLightAge = 0f;
                _reactionLightStrength = 0f;
                _reactionLight.intensity = 0f;
                _reactionLight.enabled = false;
            }
            using (ShaderGlobalsMarker.Auto())
            {
                Shader.SetGlobalVector(ContinuousId, new Vector4(state.Bass, state.Mid, state.High, state.Energy));
                Shader.SetGlobalColor(PulseColorId, _settings.RoadPulseColor * _settings.RoadPulseEmissionIntensity);
            }
        }

        public void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            using (VfxDispatchMarker.Auto())
            {
                if ((command.AllowedEffects & MusicVisualEffectGroups.Road) != 0
                    && (command.Type == MusicVisualCueType.MinorKick
                        || command.Type == MusicVisualCueType.MajorKick
                        || command.Type == MusicVisualCueType.ReactorDischarge))
                {
                    float roadStrength = command.IsAuthored && command.RoadResponse > 0f
                        ? command.RoadResponse
                        : _settings.MinorKickRoadRippleResponse > 0f
                            ? _settings.MinorKickRoadRippleResponse * state.Multipliers.RoadResponse
                            : command.Strength;
                    EmitRoadPulse(roadStrength, 0f);
                    if (command.IsAuthored && command.SecondaryRoadResponse > 0f)
                    {
                        EmitRoadPulse(command.SecondaryRoadResponse, command.SecondaryRoadDelaySeconds);
                    }
                }
                if ((command.AllowedEffects & MusicVisualEffectGroups.Structures) != 0
                    && (command.Type == MusicVisualCueType.MinorKick
                        || command.Type == MusicVisualCueType.MajorKick
                        || command.Type == MusicVisualCueType.ReactorDischarge))
                {
                    _reactionLightAge = 0f;
                    float structureStrength = command.IsAuthored && command.StructureResponse > 0f
                        ? command.StructureResponse
                        : command.Strength;
                    _reactionLightStrength = Mathf.Max(_reactionLightStrength, structureStrength);
                    _reactionLight.enabled = true;
                }
                if ((command.AllowedEffects & MusicVisualEffectGroups.Drone) != 0
                    && (command.Type == MusicVisualCueType.MinorKick
                        || command.Type == MusicVisualCueType.MajorKick
                        || command.Type == MusicVisualCueType.ReactorDischarge))
                {
                    float trailBoost = command.IsAuthored && command.DroneTrailWidthBoost > 0f
                        ? command.DroneTrailWidthBoost
                        : command.Strength * _settings.DroneTrailKickMultiplier;
                    _droneKickEnvelope = Mathf.Max(_droneKickEnvelope, trailBoost);
                    float thrusterBoost = command.IsAuthored
                        ? command.DroneThrusterBoost
                        : command.Strength * _settings.OrdinaryDroneThrusterBoost;
                    _droneThrusterEnvelope = Mathf.Max(_droneThrusterEnvelope, thrusterBoost);
                }
                if ((command.AllowedEffects & MusicVisualEffectGroups.Streaks) != 0
                    && (command.Type == MusicVisualCueType.MinorKick
                        || command.Type == MusicVisualCueType.MajorKick
                        || command.Type == MusicVisualCueType.MinorSnare
                        || command.Type == MusicVisualCueType.AccentSnare
                        || command.Type == MusicVisualCueType.TrebleTick
                        || command.Type == MusicVisualCueType.TrebleBurst
                        || command.Type == MusicVisualCueType.ReactorDischarge))
                {
                    EmitStreaks(in command, state.Energy);
                }
            }
        }

        private void EmitRoadPulse(float strength, float delay)
        {
            int selected = -1;
            for (int offset = 0; offset < _roadPulses.Length; offset++)
            {
                int index = (_roadPulseCursor + offset) % _roadPulses.Length;
                if (!_roadPulses[index].Active)
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0)
            {
                _droppedRoadPulses++;
                return;
            }
            _roadPulses[selected] = new RoadPulse
            {
                Origin = _drone != null ? _drone.position : _camera.transform.position,
                Strength = Mathf.Clamp01(strength),
                Delay = Mathf.Max(0f, delay),
                Active = true,
            };
            _roadPulseCursor = (selected + 1) % _roadPulses.Length;
        }

        private void EmitStreaks(in MusicVisualDispatchCommand command, float musicEnergy)
        {
            if (_streaks == null || _centerOutStreaks == null)
            {
                return;
            }
            uint seed = command.DeterministicSeed;
            bool centerOut = command.IsAuthored && command.ScreenFlareLineCount > 0;
            ParticleSystem targetStreaks = centerOut ? _centerOutStreaks : _streaks;
            if (centerOut)
            {
                UpdateCenterOutAnchor();
            }
            int capacity = _settings.CenterOutParticlePoolCapacity > 0
                ? _settings.CenterOutParticlePoolCapacity
                : _settings.ForegroundStreakParticleBudget;
            int available = Mathf.Max(0, capacity - targetStreaks.particleCount);
            float punch = musicEnergy <= _settings.ForegroundStreakSlowEnergyThreshold
                ? Mathf.Max(1f, _settings.ForegroundStreakSlowPunchMultiplier)
                : 1f;
            int requested;
            if (centerOut)
            {
                requested = Mathf.CeilToInt(
                    (command.ScreenFlareLineCount + command.ScreenFlareHeldLineCount)
                    * Mathf.Max(1f, _settings.CenterOutBurstCountMultiplier));
            }
            else if (command.IsAuthored && command.TrebleParticleCount > 0)
            {
                requested = command.TrebleParticleCount;
            }
            else if (_settings.MinorTrebleEventCountRange.y > 0)
            {
                requested = Mathf.RoundToInt(Mathf.Lerp(
                    _settings.MinorTrebleEventCountRange.x,
                    _settings.MinorTrebleEventCountRange.y,
                    Next01(ref seed)));
            }
            else
            {
                requested = Mathf.CeilToInt(
                    _settings.ForegroundStreakBurstCount * Mathf.Clamp01(command.Strength) * punch);
            }
            int count = Mathf.Min(available, requested);
            int movingLineCount = centerOut
                ? Mathf.CeilToInt(command.ScreenFlareLineCount
                    * Mathf.Max(1f, _settings.CenterOutBurstCountMultiplier))
                : 0;
            float forward = Mathf.Max(0.01f, _settings.ForegroundStreakForwardOffset);
            float halfHeight = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * forward;
            float halfWidth = halfHeight * _camera.aspect;
            float viewportScale = Mathf.Min(halfWidth, halfHeight) * 2f;
            for (int i = 0; i < count; i++)
            {
                Vector2 direction = Vector2.zero;
                float x;
                float y;
                Vector3 velocity;
                bool fineLine = false;
                if (centerOut)
                {
                    bool heldLine = i >= movingLineCount;
                    fineLine = Next01(ref seed) < _settings.CenterOutFineLineFraction;
                    direction = ResolveCenterOutDirection(in command, fineLine, i, ref seed);
                    float authoredRadius = command.ScreenFlareInitialViewportRadius;
                    float radius = (authoredRadius > 0f
                        ? authoredRadius
                        : _settings.CenterOutInitialViewportRadius) * viewportScale;
                    x = direction.x * radius;
                    y = direction.y * radius;
                    float speed = _settings.CenterOutRadialSpeed
                        * Mathf.Lerp(0.82f, 1.18f, Next01(ref seed))
                        * (fineLine ? _settings.CenterOutFineLineSpeedMultiplier : 1f)
                        * (heldLine
                            ? (command.ScreenFlareHeldSpeedScale > 0f
                                ? command.ScreenFlareHeldSpeedScale
                                : 1f)
                            : (command.ScreenFlareSpeedScale > 0f
                                ? command.ScreenFlareSpeedScale
                                : 1f));
                    velocity = new Vector3(
                        direction.x * speed,
                        direction.y * speed,
                        -speed * _settings.CenterOutTowardCameraSpeedFraction);
                }
                else
                {
                    float sideSelector = Next01(ref seed) < 0.5f ? -1f : 1f;
                    x = sideSelector * Mathf.Lerp(
                        _settings.ForegroundStreakPeripheralWidth * 0.55f,
                        _settings.ForegroundStreakPeripheralWidth,
                        Next01(ref seed));
                    y = Mathf.Lerp(
                        -_settings.ForegroundStreakPeripheralHeight,
                        _settings.ForegroundStreakPeripheralHeight,
                        Next01(ref seed));
                    velocity = new Vector3(-x * 0.08f, -y * 0.05f, -_settings.ForegroundStreakSpeed * punch);
                }
                Color color = ResolveSongColor(ref seed);
                if (!centerOut && command.TrebleBrightness > 0f)
                {
                    float alpha = color.a;
                    color *= command.TrebleBrightness;
                    color.a = alpha;
                }
                Vector2 lifetimeRange = command.ScreenFlareLineLifetimeSeconds;
                if (centerOut
                    && i >= movingLineCount
                    && command.ScreenFlareHeldLineLifetimeSeconds.y > 0f)
                {
                    lifetimeRange = command.ScreenFlareHeldLineLifetimeSeconds;
                }
                float lifetime = centerOut && lifetimeRange.y > 0f
                    ? Mathf.Lerp(lifetimeRange.x, lifetimeRange.y, Next01(ref seed))
                    : _settings.ForegroundStreakLifetime;
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(x, y, _settings.ForegroundStreakForwardOffset),
                    velocity = velocity,
                    startColor = color,
                    startLifetime = lifetime,
                    startSize = centerOut
                        ? _settings.ForegroundStreakSize * (fineLine
                            ? _settings.CenterOutFineLineWidthMultiplier
                            : _settings.CenterOutBroadRayWidthMultiplier)
                            * (i >= movingLineCount && command.ScreenFlareHeldWidthScale > 0f
                                ? command.ScreenFlareHeldWidthScale
                                : (command.ScreenFlareWidthScale > 0f
                                    ? command.ScreenFlareWidthScale
                                    : 1f))
                        : _settings.ForegroundStreakSize,
                };
                targetStreaks.Emit(emit, 1);
            }
        }

        private void UpdateCenterOutAnchor()
        {
            if (_centerOutStreaks == null || _camera == null || _drone == null)
            {
                return;
            }
            float forward = Mathf.Max(0.01f, _settings.ForegroundStreakForwardOffset);
            float halfHeight = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * forward;
            float halfWidth = halfHeight * _camera.aspect;
            Vector3 droneViewport = _camera.WorldToViewportPoint(GetDroneVisualCenter());
            if (droneViewport.z <= 0f)
            {
                droneViewport = new Vector3(0.5f, 0.5f, 1f);
            }
            Vector3 anchorLocalPosition = new Vector3(
                (droneViewport.x - 0.5f) * halfWidth * 2f,
                (droneViewport.y - 0.5f) * halfHeight * 2f,
                0f);
            CompensateCenterOutParticles(anchorLocalPosition - _centerOutStreaks.transform.localPosition);
            _centerOutStreaks.transform.localPosition = anchorLocalPosition;
        }

        private void CompensateCenterOutParticles(Vector3 anchorDelta)
        {
            if (!_settings.CenterOutCompensateAnchorMotion
                || _centerOutParticleBuffer == null
                || anchorDelta.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }
            int particleCount = _centerOutStreaks.GetParticles(_centerOutParticleBuffer);
            for (int i = 0; i < particleCount; i++)
            {
                ParticleSystem.Particle particle = _centerOutParticleBuffer[i];
                particle.position -= anchorDelta;
                _centerOutParticleBuffer[i] = particle;
            }
            if (particleCount > 0)
            {
                _centerOutStreaks.SetParticles(_centerOutParticleBuffer, particleCount);
            }
        }

        private void LateUpdate()
        {
            UpdateCenterOutAnchor();
        }

        private void HandleCameraPreCull(Camera renderingCamera)
        {
            if (renderingCamera == _camera)
            {
                UpdateCenterOutAnchor();
            }
        }

        private Vector2 ResolveCenterOutDirection(
            in MusicVisualDispatchCommand command,
            bool fineLine,
            int emissionIndex,
            ref uint seed)
        {
            float spread = fineLine
                ? _settings.CenterOutDirectionalVariation
                : Mathf.Min(1f, _settings.CenterOutDirectionalVariation * 1.75f);
            float signedPrimary = command.ScreenFlareEmitMirroredPair
                ? (emissionIndex % 2 == 0 ? -1f : 1f)
                : (Next01(ref seed) < 0.5f ? -1f : 1f);
            float variation = Mathf.Lerp(-spread, spread, Next01(ref seed));
            switch (command.ScreenFlareDirectionMode)
            {
                case MusicScreenFlareDirectionMode.Vertical:
                    return new Vector2(variation, signedPrimary).normalized;
                case MusicScreenFlareDirectionMode.Horizontal:
                    return new Vector2(signedPrimary, variation).normalized;
                case MusicScreenFlareDirectionMode.Diagonal:
                    float signedSecondary = command.ScreenFlareEmitMirroredPair
                        ? signedPrimary * ((command.DeterministicSeed & 1u) == 0u ? -1f : 1f)
                        : (Next01(ref seed) < 0.5f ? -1f : 1f);
                    float diagonalVariation = variation * 0.45f;
                    return new Vector2(
                        signedPrimary + diagonalVariation,
                        signedSecondary - diagonalVariation).normalized;
                default:
                    float rightChance = Mathf.Clamp01(
                        0.5f + command.ScreenFlareHorizontalBias * 0.5f);
                    float side = command.ScreenFlareEmitMirroredPair
                        ? signedPrimary
                        : (Next01(ref seed) < rightChance ? 1f : -1f);
                    return new Vector2(side, variation).normalized;
            }
        }

        private Vector3 GetDroneVisualCenter()
        {
            return _drone != null && _hasDroneVisualLocalCenter
                ? _drone.TransformPoint(_droneVisualLocalCenter)
                : (_drone != null ? _drone.position : Vector3.zero);
        }

        private Color ResolveSongColor(ref uint seed)
        {
            if (_settings.CenterOutUseColorWheelPalette
                || _settings.CenterOutCyanColor.maxColorComponent <= 0f)
            {
                int paletteIndex = Mathf.Min(
                    _streakPalette.Length - 1,
                    Mathf.FloorToInt(Next01(ref seed) * _streakPalette.Length));
                return _streakPalette[paletteIndex];
            }
            float selector = Next01(ref seed);
            float total = Mathf.Max(
                Mathf.Epsilon,
                _settings.CenterOutCyanWeight
                    + _settings.CenterOutMagentaWeight
                    + _settings.CenterOutWarmWhiteWeight
                    + _settings.CenterOutAccentWeight);
            float cyanEnd = _settings.CenterOutCyanWeight / total;
            float magentaEnd = cyanEnd + _settings.CenterOutMagentaWeight / total;
            float warmWhiteEnd = magentaEnd + _settings.CenterOutWarmWhiteWeight / total;
            if (selector < cyanEnd) return _settings.CenterOutCyanColor;
            if (selector < magentaEnd) return _settings.CenterOutMagentaColor;
            if (selector < warmWhiteEnd) return _settings.CenterOutWarmWhiteColor;
            return _settings.CenterOutAccentColor;
        }

        private static float Next01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216f;
        }

        private void Update()
        {
            if (_settings == null)
            {
                return;
            }
            float deltaTime = Time.unscaledDeltaTime;
            UpdateRoadPulses(deltaTime);
            UpdateReactionLight(deltaTime);
            UpdateDroneTrails(deltaTime);
        }

        private void UpdateRoadPulses(float deltaTime)
        {
            for (int i = 0; i < _roadPulses.Length; i++)
            {
                RoadPulse pulse = _roadPulses[i];
                if (!pulse.Active)
                {
                    continue;
                }
                if (pulse.Delay > 0f)
                {
                    pulse.Delay -= deltaTime;
                    _roadPulses[i] = pulse;
                    continue;
                }
                pulse.Age += deltaTime;
                if (pulse.Age >= _settings.RoadPulseDurationSeconds)
                {
                    pulse.Active = false;
                }
                _roadPulses[i] = pulse;
            }

            for (int shaderIndex = 0; shaderIndex < _pulseOrigins.Length; shaderIndex++)
            {
                if (shaderIndex < _roadPulses.Length
                    && _roadPulses[shaderIndex].Active
                    && _roadPulses[shaderIndex].Delay <= 0f)
                {
                    RoadPulse pulse = _roadPulses[shaderIndex];
                    float normalizedAge = Mathf.Clamp01(pulse.Age / _settings.RoadPulseDurationSeconds);
                    float fade = 1f - normalizedAge;
                    _pulseOrigins[shaderIndex] = new Vector4(
                        pulse.Origin.x,
                        pulse.Origin.z,
                        pulse.Age * _settings.RoadPulseTravelSpeed,
                        pulse.Strength * fade);
                    _pulseParameters[shaderIndex] = new Vector4(_settings.RoadPulseWidth, 0f, 0f, 0f);
                }
                else
                {
                    _pulseOrigins[shaderIndex] = Vector4.zero;
                    _pulseParameters[shaderIndex] = Vector4.zero;
                }
            }
            using (ShaderGlobalsMarker.Auto())
            {
                Shader.SetGlobalVectorArray(PulseOriginsId, _pulseOrigins);
                Shader.SetGlobalVectorArray(PulseParametersId, _pulseParameters);
            }
        }

        private void UpdateReactionLight(float deltaTime)
        {
            if (_reactionLight == null || !_reactionLight.enabled)
            {
                return;
            }
            _reactionLightAge += deltaTime;
            float duration = Mathf.Max(0.01f, _settings.StructureLightDurationSeconds);
            float fade = 1f - Mathf.Clamp01(_reactionLightAge / duration);
            _reactionLight.intensity = _settings.StructureLightMaximumIntensity * _reactionLightStrength * fade * fade;
            _reactionLight.range = _settings.StructureLightRange;
            _reactionLight.color = _settings.StructureLightColor;
            Transform anchor = _drone != null ? _drone : _camera.transform;
            _reactionLight.transform.position = anchor.position + _camera.transform.forward * _settings.StructureLightForwardOffset;
            if (fade <= 0f)
            {
                _reactionLight.enabled = false;
                _reactionLightStrength = 0f;
            }
        }

        private void UpdateDroneTrails(float deltaTime)
        {
            float duration = Mathf.Max(0.01f, _settings.DroneKickResponseDurationSeconds);
            _droneKickEnvelope = Mathf.MoveTowards(_droneKickEnvelope, 0f, deltaTime / duration);
            _droneThrusterEnvelope = Mathf.MoveTowards(_droneThrusterEnvelope, 0f, deltaTime / duration);
            float multiplier = 1f
                + _continuousBass * _settings.DroneTrailBassMultiplier
                + _droneKickEnvelope;
            for (int i = 0; i < _droneTrails.Length; i++)
            {
                if (_droneTrails[i] != null)
                {
                    _droneTrails[i].widthMultiplier = _baseTrailWidths[i] * multiplier;
                    _droneTrails[i].startColor = _baseTrailStartColors[i] * (1f + _droneThrusterEnvelope);
                    _droneTrails[i].endColor = _baseTrailEndColors[i] * (1f + _droneThrusterEnvelope);
                }
            }
        }

        public void ResetMusicResponse()
        {
            if (_roadPulses != null)
            {
                for (int i = 0; i < _roadPulses.Length; i++)
                {
                    _roadPulses[i] = default;
                }
            }
            _droneKickEnvelope = 0f;
            _droneThrusterEnvelope = 0f;
            _continuousBass = 0f;
            if (_reactionLight != null)
            {
                _reactionLight.enabled = false;
                _reactionLight.intensity = 0f;
            }
            if (_streaks != null)
            {
                _streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (_centerOutStreaks != null)
            {
                _centerOutStreaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            int trailCount = _droneTrails != null ? _droneTrails.Length : 0;
            for (int i = 0; i < trailCount; i++)
            {
                if (_droneTrails[i] != null)
                {
                    _droneTrails[i].widthMultiplier = _baseTrailWidths[i];
                    _droneTrails[i].startColor = _baseTrailStartColors[i];
                    _droneTrails[i].endColor = _baseTrailEndColors[i];
                }
            }
            ResetShaderGlobals();
        }

        public void ClearFlashingResponse()
        {
            if (_streaks != null)
            {
                _streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (_centerOutStreaks != null)
            {
                _centerOutStreaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ResetShaderGlobals()
        {
            for (int i = 0; i < _pulseOrigins.Length; i++)
            {
                _pulseOrigins[i] = Vector4.zero;
                _pulseParameters[i] = Vector4.zero;
            }
            Shader.SetGlobalVectorArray(PulseOriginsId, _pulseOrigins);
            Shader.SetGlobalVectorArray(PulseParametersId, _pulseParameters);
            Shader.SetGlobalVector(ContinuousId, Vector4.zero);
            Shader.SetGlobalColor(PulseColorId, Color.clear);
        }

        private int CountActiveRoadPulses()
        {
            int count = 0;
            if (_roadPulses == null)
            {
                return count;
            }
            for (int i = 0; i < _roadPulses.Length; i++)
            {
                if (_roadPulses[i].Active)
                {
                    count++;
                }
            }
            return count;
        }

        private void OnDisable()
        {
            ResetMusicResponse();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= HandleCameraPreCull;
            if (_streakMaterial != null)
            {
                Destroy(_streakMaterial);
                _streakMaterial = null;
            }
        }
    }
}
