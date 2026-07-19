using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuneVector
{
    [DefaultExecutionOrder(915)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorElectricalStormVisualSystem : MonoBehaviour
    {
        private sealed class CloudLobe
        {
            public Transform Transform;
            public Vector3 BasePosition;
            public float Phase;
        }

        private sealed class LightningVisual
        {
            public GameObject Root;
            public LineRenderer Main;
            public readonly List<LineRenderer> Branches = new List<LineRenderer>();
            public Transform ImpactFlash;
            public Vector3 Start;
            public Vector3 End;
            public float Width;
            public float Duration;
            public float Remaining;
            public float Elapsed;
            public float BoltDuration;
            public float Seed;
            public float ImpactRadius;
        }

        private sealed class StrikeScarVisual
        {
            public GameObject Root;
            public Material Material;
            public float Duration;
            public float Remaining;
        }

        private readonly List<CloudLobe> _cloudLobes = new List<CloudLobe>();
        private readonly List<Light> _internalLights = new List<Light>();
        private readonly List<LineRenderer> _targetRings = new List<LineRenderer>();
        private readonly List<LightningVisual> _lightning = new List<LightningVisual>();
        private readonly List<StrikeScarVisual> _strikeScars = new List<StrikeScarVisual>();
        private readonly List<DuneVectorLandmarkInstance> _landmarkCandidates = new List<DuneVectorLandmarkInstance>();

        private DuneVectorEnvironmentalHazardSystem _hazards;
        private DroneCharacterController _drone;
        private DesertWorldStreamer _world;
        private DuneVectorWeatherController _weather;
        private ElectricalStormVisualTuning _settings;
        private System.Random _random;
        private GameObject _stormRoot;
        private Material _cloudMaterial;
        private Material _lightningMaterial;
        private Material _telegraphMaterial;
        private Material _chargedDustMaterial;
        private Material _staticMoteMaterial;
        private Texture2D _softParticleTexture;
        private ParticleSystem _chargedDust;
        private ParticleSystem _staticMotes;
        private ParticleSystem _convergingSparks;
        private LineRenderer _chargeColumn;
        private Volume _interiorVolume;
        private VolumeProfile _interiorProfile;
        private float _visualBlend;
        private float _flashTimer;
        private float _flashRemaining;
        private float _probeTimer;
        private float _nearArcTimer;
        private float _landmarkTimer;
        private bool _targetTelegraphActive;
        private bool _stormWasVisible;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudStatusStyle;

        public void Initialize(
            DuneVectorEnvironmentalHazardSystem hazards,
            DroneCharacterController drone,
            DesertWorldStreamer world,
            DuneVectorWeatherController weather,
            ElectricalStormVisualTuning settings)
        {
            _hazards = hazards;
            _drone = drone;
            _world = world;
            _weather = weather;
            _settings = settings;
            _random = new System.Random(unchecked(world.WorldSeed ^ 77531));
            _softParticleTexture = CreateSoftParticleTexture(Mathf.Max(16, settings.ParticleTextureResolution));
            _cloudMaterial = CreateCloudMaterial();
            _lightningMaterial = CreateEnergyMaterial("Electrical Storm Lightning", settings.LightningColor);
            _telegraphMaterial = CreateEnergyMaterial("Electrical Strike Telegraph", settings.TelegraphColor);
            _chargedDustMaterial = CreateParticleMaterial("Charged Desert Dust", settings.ChargedDustColor);
            _staticMoteMaterial = CreateParticleMaterial("Ionized Static Motes", settings.StaticMoteColor);
            BuildStormfront();
            _chargedDust = CreateChargedDust();
            _staticMotes = CreateStaticMotes();
            BuildTargetTelegraph();
            CreateInteriorVolume();

            _flashTimer = NextInterval(settings.InternalFlashMinimumInterval, settings.InternalFlashMaximumInterval);
            _probeTimer = NextInterval(settings.ProbeMinimumInterval, settings.ProbeMaximumInterval);
            _nearArcTimer = NextInterval(settings.NearArcMinimumInterval, settings.NearArcMaximumInterval);
            _landmarkTimer = NextInterval(settings.LandmarkReactionMinimumInterval, settings.LandmarkReactionMaximumInterval);

            _hazards.StrikePhaseChanged += HandleStrikePhaseChanged;
            _hazards.LightningTargetLocked += HandleLightningTargetLocked;
            _hazards.LightningStruck += HandleLightningStruck;
            _world.WorldShifted += HandleWorldShift;
        }

        private void Update()
        {
            if (_hazards == null || _drone == null || _settings == null)
            {
                return;
            }

            float rawIntensity = _weather != null ? _weather.CurrentStormIntensity : 0f;
            float desiredBlend = rawIntensity <= _settings.VisualActivationIntensity
                ? 0f
                : Mathf.InverseLerp(
                    _settings.VisualActivationIntensity,
                    Mathf.Max(_settings.VisualActivationIntensity + 0.001f, _settings.FullVisualIntensity),
                    rawIntensity);
            _visualBlend = Mathf.Lerp(
                _visualBlend,
                desiredBlend,
                DuneVectorMath.Sharpness(_settings.VisualBlendSharpness, Time.deltaTime));

            UpdateStormfront();
            UpdateInternalFlashes(Time.deltaTime);
            UpdateChargedAir();
            UpdateTargetTelegraph();
            UpdateAmbientElectricalEvents(Time.deltaTime);
            UpdateLightningVisuals(Time.deltaTime);
            UpdateStrikeScars(Time.deltaTime);
            if (_interiorVolume != null)
            {
                _interiorVolume.weight = _visualBlend;
            }
        }

        private void BuildStormfront()
        {
            _stormRoot = new GameObject("Landmark-Scale Electrical Stormfront");
            _stormRoot.transform.SetParent(transform, true);
            int lobeCount = Mathf.Max(4, _settings.StormCloudLobeCount);
            for (int i = 0; i < lobeCount; i++)
            {
                float horizontal = RandomRange(-0.5f, 0.5f);
                float vertical = RandomRange(0f, 1f);
                float depth = RandomRange(-0.5f, 0.5f);
                Vector3 basePosition = new Vector3(
                    horizontal * _settings.StormfrontWidth,
                    vertical * _settings.StormfrontHeight,
                    depth * _settings.StormfrontDepth);
                Vector3 scale = RandomScale(_settings.StormCloudMinimumScale, _settings.StormCloudMaximumScale);
                if (vertical > _settings.SevereVisualThreshold)
                {
                    scale.x *= Mathf.Lerp(1f, _settings.StormCloudAnvilScaleMultiplier, vertical);
                    basePosition.x *= _settings.StormCloudAnvilSpreadMultiplier;
                }

                GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = $"Charged Storm Cloud Lobe {i + 1}";
                lobe.transform.SetParent(_stormRoot.transform, false);
                lobe.transform.localPosition = basePosition;
                lobe.transform.localScale = scale;
                Renderer renderer = lobe.GetComponent<Renderer>();
                renderer.sharedMaterial = _cloudMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                Collider collider = lobe.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                _cloudLobes.Add(new CloudLobe
                {
                    Transform = lobe.transform,
                    BasePosition = basePosition,
                    Phase = RandomRange(0f, Mathf.PI * 2f),
                });
            }

            for (int i = 0; i < Mathf.Max(0, _settings.InternalFlashLightCount); i++)
            {
                GameObject lightObject = new GameObject($"Internal Cloud Flash {i + 1}");
                lightObject.transform.SetParent(_stormRoot.transform, false);
                lightObject.transform.localPosition = new Vector3(
                    RandomRange(-_settings.InternalLightHorizontalSpread, _settings.InternalLightHorizontalSpread) *
                        _settings.StormfrontWidth,
                    RandomRange(
                        _settings.InternalLightMinimumHeight,
                        Mathf.Max(_settings.InternalLightMinimumHeight, _settings.InternalLightMaximumHeight)) *
                        _settings.StormfrontHeight,
                    RandomRange(-_settings.InternalLightHorizontalSpread, _settings.InternalLightHorizontalSpread) *
                        _settings.StormfrontDepth);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = _settings.InternalFlashLightColor;
                light.range = _settings.InternalFlashLightRange;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                _internalLights.Add(light);
            }
        }

        private void UpdateStormfront()
        {
            bool visible = _visualBlend > 0.001f;
            _stormRoot.SetActive(visible);
            if (!visible)
            {
                _stormWasVisible = false;
                return;
            }

            Vector2 configured = _settings.StormfrontDirection.sqrMagnitude > 0.001f
                ? _settings.StormfrontDirection.normalized
                : Vector2.left;
            Vector3 direction = new Vector3(configured.x, 0f, configured.y);
            float distance = Mathf.Lerp(
                _settings.StormfrontFarDistance,
                _settings.StormfrontNearDistance,
                _visualBlend);
            Vector3 horizontalCenter = _drone.WorldCenter + (direction * distance);
            float terrainHeight = _world.SampleHeightAtLocal(horizontalCenter.x, horizontalCenter.z);
            Vector3 desired = new Vector3(
                horizontalCenter.x,
                terrainHeight + _settings.StormfrontBaseHeight,
                horizontalCenter.z);
            _stormRoot.transform.position = _stormWasVisible
                ? Vector3.Lerp(
                    _stormRoot.transform.position,
                    desired,
                    DuneVectorMath.Sharpness(_settings.VisualBlendSharpness, Time.deltaTime))
                : desired;
            _stormWasVisible = true;
            _stormRoot.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);

            for (int i = 0; i < _cloudLobes.Count; i++)
            {
                CloudLobe lobe = _cloudLobes[i];
                lobe.Transform.localPosition = lobe.BasePosition +
                    (Vector3.up * Mathf.Sin((Time.time * _settings.StormCloudRollSpeed) + lobe.Phase) *
                     _settings.StormCloudRollAmount);
            }
        }

        private void UpdateInternalFlashes(float deltaTime)
        {
            if (_visualBlend <= 0f)
            {
                SetInternalFlash(0f);
                return;
            }
            if (_flashRemaining > 0f)
            {
                _flashRemaining = Mathf.Max(0f, _flashRemaining - deltaTime);
                float progress = 1f - (_flashRemaining / Mathf.Max(0.01f, _settings.InternalFlashDuration));
                SetInternalFlash(Mathf.Sin(progress * Mathf.PI) * _visualBlend);
                if (_flashRemaining <= 0f)
                {
                    _flashTimer = NextInterval(
                        _settings.InternalFlashMinimumInterval,
                        _settings.InternalFlashMaximumInterval);
                }
                return;
            }
            SetInternalFlash(0f);
            _flashTimer -= deltaTime * Mathf.Lerp(
                _settings.InternalFlashMinimumFrequencyMultiplier,
                _settings.InternalFlashMaximumFrequencyMultiplier,
                _visualBlend);
            if (_flashTimer <= 0f)
            {
                _flashRemaining = Mathf.Max(0.01f, _settings.InternalFlashDuration);
            }
        }

        private void SetInternalFlash(float strength)
        {
            Color emission = _settings.StormCloudFlashEmission *
                (strength * _settings.InternalFlashEmissionMultiplier);
            if (_cloudMaterial.HasProperty("_EmissiveColor"))
            {
                _cloudMaterial.SetColor("_EmissiveColor", emission);
            }
            for (int i = 0; i < _internalLights.Count; i++)
            {
                _internalLights[i].intensity = _settings.InternalFlashLightIntensity * strength;
            }
        }

        private ParticleSystem CreateChargedDust()
        {
            GameObject dustObject = new GameObject("Charged Dust Veil");
            dustObject.transform.SetParent(transform, true);
            ParticleSystem system = dustObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, _settings.ChargedDustParticleBudget);
            main.startLifetime = Mathf.Max(0.1f, _settings.ChargedDustLifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(
                _settings.ChargedDustMinimumSize,
                Mathf.Max(_settings.ChargedDustMinimumSize, _settings.ChargedDustMaximumSize));
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                _settings.ChargedDustRadius * 2f,
                _settings.ChargedDustHeight,
                _settings.ChargedDustRadius * 2f);
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = _settings.ChargedDustVelocity.x;
            velocity.y = _settings.ChargedDustVelocity.y;
            velocity.z = _settings.ChargedDustVelocity.z;
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.ChargedDustTurbulence;
            ConfigureParticleFade(system);
            ParticleSystemRenderer renderer = dustObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _chargedDustMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = _settings.ChargedDustLengthScale;
            renderer.velocityScale = _settings.ChargedDustVelocityStretch;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }

        private ParticleSystem CreateStaticMotes()
        {
            GameObject moteObject = new GameObject("Ionized Air Motes");
            moteObject.transform.SetParent(transform, true);
            ParticleSystem system = moteObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, _settings.StaticMoteParticleBudget);
            main.startLifetime = Mathf.Max(0.1f, _settings.StaticMoteLifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(
                _settings.StaticMoteMinimumSize,
                Mathf.Max(_settings.StaticMoteMinimumSize, _settings.StaticMoteMaximumSize));
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                _settings.StaticMoteRadius * 2f,
                _settings.StaticMoteHeight,
                _settings.StaticMoteRadius * 2f);
            Vector3 direction = _settings.ChargedDustVelocity.sqrMagnitude > 0.001f
                ? _settings.ChargedDustVelocity.normalized
                : Vector3.right;
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = direction.x * _settings.StaticMoteSpeed;
            velocity.y = direction.y * _settings.StaticMoteSpeed;
            velocity.z = direction.z * _settings.StaticMoteSpeed;
            ConfigureParticleFade(system);
            ParticleSystemRenderer renderer = moteObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _staticMoteMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = _settings.StaticMoteLength;
            renderer.velocityScale = _settings.StaticMoteVelocityStretch;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }

        private void UpdateChargedAir()
        {
            Vector3 center = _drone.WorldCenter;
            _chargedDust.transform.position = center;
            _staticMotes.transform.position = center;
            float buildup = _hazards.StrikePhase == ElectricalStrikePhase.Buildup ||
                _hazards.StrikePhase == ElectricalStrikePhase.TargetTelegraph
                    ? _settings.ChargeBuildupParticleMultiplier
                    : 1f;
            SetEmission(_chargedDust, _settings.ChargedDustEmissionRate * _visualBlend);
            SetEmission(_staticMotes, _settings.StaticMoteEmissionRate * _visualBlend * buildup);
        }

        private void BuildTargetTelegraph()
        {
            GameObject targetRoot = new GameObject("Readable Electrical Strike Telegraph");
            targetRoot.transform.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                LineRenderer ring = CreateLine($"Ionization Target Ring {i + 1}", targetRoot.transform, _telegraphMaterial);
                ring.loop = true;
                ring.startWidth = _settings.TargetMarkerWidth;
                ring.endWidth = _settings.TargetMarkerWidth;
                ring.enabled = false;
                _targetRings.Add(ring);
            }
            _chargeColumn = CreateLine("Vertical Charge Convergence", targetRoot.transform, _telegraphMaterial);
            _chargeColumn.enabled = false;
            _convergingSparks = CreateConvergingSparks(targetRoot.transform);
        }

        private ParticleSystem CreateConvergingSparks(Transform parent)
        {
            GameObject sparkObject = new GameObject("Converging Telegraph Sparks");
            sparkObject.transform.SetParent(parent, false);
            ParticleSystem system = sparkObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, _settings.ConvergingSparkBudget);
            main.startLifetime = Mathf.Max(0.1f, _settings.ConvergingSparkLifetime);
            main.startSize = Mathf.Max(0.01f, _settings.ConvergingSparkSize);
            main.startSpeed = -Mathf.Max(0f, _settings.ConvergingSparkSpeed);
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _settings.TargetMarkerEndRadius;
            shape.radiusThickness = 0f;
            ConfigureParticleFade(system);
            ParticleSystemRenderer renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _staticMoteMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private void UpdateTargetTelegraph()
        {
            if (!_targetTelegraphActive || _hazards.StrikePhase != ElectricalStrikePhase.TargetTelegraph)
            {
                SetTargetTelegraphVisible(false);
                return;
            }

            SetTargetTelegraphVisible(true);
            Vector3 target = _hazards.LightningTarget;
            if (!_hazards.LightningTargetsAir)
            {
                target.y += _settings.TargetMarkerHeightOffset;
            }
            float progress = _hazards.StrikePhaseProgress;
            float pulse = 1f + (Mathf.Sin(Time.time * _settings.TargetPulseSpeed) * _settings.TargetPulseAmount);
            float radius = Mathf.Lerp(
                _settings.TargetMarkerStartRadius,
                _hazards.LightningTargetsAir ? _settings.AirTargetMarkerRadius : _settings.TargetMarkerEndRadius,
                progress) * pulse;
            UpdateCircle(_targetRings[0], target, Vector3.right, Vector3.forward, radius);
            if (_hazards.LightningTargetsAir)
            {
                UpdateCircle(_targetRings[1], target, Vector3.right, Vector3.up, radius);
                UpdateCircle(_targetRings[2], target, Vector3.forward, Vector3.up, radius);
            }
            else
            {
                _targetRings[1].enabled = false;
                _targetRings[2].enabled = false;
            }

            _chargeColumn.positionCount = 2;
            _chargeColumn.SetPosition(0, target + (Vector3.up * _settings.ChargeColumnHeight));
            _chargeColumn.SetPosition(1, target);
            float width = Mathf.Lerp(
                _settings.ChargeColumnStartWidth,
                _settings.ChargeColumnEndWidth,
                progress) * pulse;
            _chargeColumn.startWidth = width;
            _chargeColumn.endWidth = width * _settings.ChargeColumnTipWidthMultiplier;
            _convergingSparks.transform.position = target;
            SetEmission(
                _convergingSparks,
                _settings.ConvergingSparkEmissionRate * Mathf.Lerp(
                    _settings.ConvergingSparkInitialEmissionFraction,
                    1f,
                    progress));
            if (!_convergingSparks.isPlaying)
            {
                _convergingSparks.Play();
            }
        }

        private void UpdateCircle(
            LineRenderer line,
            Vector3 center,
            Vector3 firstAxis,
            Vector3 secondAxis,
            float radius)
        {
            int segments = Mathf.Max(8, _settings.TargetMarkerSegments);
            line.positionCount = segments;
            line.enabled = true;
            for (int i = 0; i < segments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / segments;
                line.SetPosition(
                    i,
                    center + (firstAxis * Mathf.Cos(angle) * radius) +
                    (secondAxis * Mathf.Sin(angle) * radius));
            }
        }

        private void SetTargetTelegraphVisible(bool visible)
        {
            for (int i = 0; i < _targetRings.Count; i++)
            {
                if (!visible) _targetRings[i].enabled = false;
            }
            _chargeColumn.enabled = visible;
            if (!visible)
            {
                SetEmission(_convergingSparks, 0f);
                if (_convergingSparks.isPlaying)
                {
                    _convergingSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void UpdateAmbientElectricalEvents(float deltaTime)
        {
            if (_visualBlend > _settings.ProbeActivationIntensity)
            {
                _probeTimer -= deltaTime * Mathf.Lerp(
                    _settings.ProbeMinimumFrequencyMultiplier,
                    _settings.ProbeMaximumFrequencyMultiplier,
                    _visualBlend);
                if (_probeTimer <= 0f)
                {
                    SpawnDistantProbeStrike();
                    _probeTimer = NextInterval(_settings.ProbeMinimumInterval, _settings.ProbeMaximumInterval);
                }
            }

            if (_hazards.IsElectricalInterferenceActive)
            {
                _nearArcTimer -= deltaTime;
                if (_nearArcTimer <= 0f)
                {
                    SpawnNearFieldArc();
                    _nearArcTimer = NextInterval(_settings.NearArcMinimumInterval, _settings.NearArcMaximumInterval);
                }
                if (_settings.LandmarkReactionsEnabled)
                {
                    _landmarkTimer -= deltaTime;
                    if (_landmarkTimer <= 0f)
                    {
                        SpawnLandmarkReaction();
                        _landmarkTimer = NextInterval(
                            _settings.LandmarkReactionMinimumInterval,
                            _settings.LandmarkReactionMaximumInterval);
                    }
                }
            }
        }

        private void SpawnDistantProbeStrike()
        {
            float angle = RandomRange(0f, Mathf.PI * 2f);
            float distance = RandomRange(
                _settings.ProbeMinimumDistance,
                Mathf.Max(_settings.ProbeMinimumDistance, _settings.ProbeMaximumDistance));
            Vector3 target = _drone.WorldCenter + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);
            target.y = _world.SampleHeightAtLocal(target.x, target.z);
            Vector3 start = target + (Vector3.up * _settings.ProbeOriginHeight);
            SpawnLightning(
                start,
                target,
                _settings.LightningStartWidth * _settings.ProbeWidthMultiplier,
                _settings.LightningVisualDuration,
                true,
                true);
        }

        private void SpawnNearFieldArc()
        {
            Vector3 center = _drone.WorldCenter;
            Vector3 first = RandomUnitSphere();
            Vector3 second = RandomUnitSphere();
            float minimum = Mathf.Max(0f, _settings.NearArcMinimumRadius);
            float maximum = Mathf.Max(minimum, _settings.NearArcMaximumRadius);
            Vector3 start = center + (first * RandomRange(minimum, maximum));
            Vector3 end = center + (second * RandomRange(minimum, maximum));
            SpawnLightning(start, end, _settings.NearArcWidth, _settings.NearArcDuration, false, false);
        }

        private void SpawnLandmarkReaction()
        {
            DuneVectorLandmarkInstance[] landmarks =
                Object.FindObjectsByType<DuneVectorLandmarkInstance>();
            _landmarkCandidates.Clear();
            float rangeSquared = _settings.LandmarkReactionRange * _settings.LandmarkReactionRange;
            for (int i = 0; i < landmarks.Length; i++)
            {
                if ((landmarks[i].transform.position - _drone.WorldCenter).sqrMagnitude <= rangeSquared)
                {
                    _landmarkCandidates.Add(landmarks[i]);
                }
            }
            if (_landmarkCandidates.Count == 0)
            {
                return;
            }

            DuneVectorLandmarkInstance landmark = _landmarkCandidates[_random.Next(_landmarkCandidates.Count)];
            Renderer[] renderers = landmark.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(landmark.transform.position, Vector3.zero);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            Vector3 start = hasBounds
                ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
                : (landmark.EncounterSocket != null ? landmark.EncounterSocket.position : landmark.transform.position);
            Vector3 end = hasBounds ? bounds.center : landmark.transform.position;
            SpawnLightning(
                start,
                end,
                _settings.LandmarkArcWidth,
                _settings.LandmarkArcDuration,
                false,
                false);
        }

        private void HandleStrikePhaseChanged(ElectricalStrikePhase phase)
        {
            if (phase != ElectricalStrikePhase.TargetTelegraph)
            {
                _targetTelegraphActive = false;
            }
        }

        private void HandleLightningTargetLocked(Vector3 target, bool targetsAir)
        {
            _targetTelegraphActive = true;
        }

        private void HandleLightningStruck(Vector3 target, bool hit)
        {
            _targetTelegraphActive = false;
            SetTargetTelegraphVisible(false);
            Vector3 start = target + (Vector3.up * _settings.ProbeOriginHeight);
            SpawnLightning(
                start,
                target,
                _settings.LightningStartWidth,
                _settings.LightningVisualDuration,
                !_hazards.LightningTargetsAir,
                true);
        }

        private void SpawnLightning(
            Vector3 start,
            Vector3 end,
            float width,
            float duration,
            bool createScar,
            bool branches)
        {
            GameObject root = new GameObject("Electrical Storm Lightning Release");
            root.transform.SetParent(transform, true);
            LightningVisual visual = new LightningVisual
            {
                Root = root,
                Main = CreateLine("Blue-White Lightning", root.transform, _lightningMaterial),
                Start = start,
                End = end,
                Width = width,
                Duration = Mathf.Max(Mathf.Max(0.01f, duration), _settings.ImpactFlashDuration),
                Remaining = Mathf.Max(Mathf.Max(0.01f, duration), _settings.ImpactFlashDuration),
                BoltDuration = Mathf.Max(0.01f, duration),
                Seed = RandomRange(0f, 1000f),
                ImpactRadius = _settings.ImpactFlashRadius * Mathf.Clamp01(
                    width / Mathf.Max(0.001f, _settings.LightningStartWidth)),
            };
            visual.Main.startWidth = width;
            visual.Main.endWidth = width * _settings.LightningEndWidthMultiplier;
            int branchCount = branches ? Mathf.Max(0, _settings.LightningBranchCount) : 0;
            for (int i = 0; i < branchCount; i++)
            {
                LineRenderer branch = CreateLine($"Lightning Branch {i + 1}", root.transform, _lightningMaterial);
                branch.startWidth = width * _settings.LightningBranchWidthMultiplier;
                branch.endWidth = 0f;
                visual.Branches.Add(branch);
            }
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Lightning Impact Flash";
            flash.transform.SetParent(root.transform, true);
            flash.transform.position = end;
            flash.transform.localScale = Vector3.zero;
            Renderer flashRenderer = flash.GetComponent<Renderer>();
            flashRenderer.sharedMaterial = _lightningMaterial;
            flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            Collider collider = flash.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            visual.ImpactFlash = flash.transform;
            _lightning.Add(visual);
            UpdateLightningGeometry(visual);
            if (createScar)
            {
                CreateStrikeScar(end);
            }
        }

        private void UpdateLightningVisuals(float deltaTime)
        {
            for (int i = _lightning.Count - 1; i >= 0; i--)
            {
                LightningVisual visual = _lightning[i];
                visual.Remaining -= deltaTime;
                visual.Elapsed += deltaTime;
                if (visual.Remaining <= 0f)
                {
                    Destroy(visual.Root);
                    _lightning.RemoveAt(i);
                    continue;
                }
                bool boltVisible = visual.Elapsed <= visual.BoltDuration;
                visual.Main.enabled = boltVisible;
                for (int branchIndex = 0; branchIndex < visual.Branches.Count; branchIndex++)
                {
                    visual.Branches[branchIndex].enabled = boltVisible;
                }
                if (boltVisible)
                {
                    UpdateLightningGeometry(visual);
                }
                float impactProgress = Mathf.Clamp01(
                    visual.Elapsed / Mathf.Max(0.01f, _settings.ImpactFlashDuration));
                float flashScale = Mathf.Sin(impactProgress * Mathf.PI) * visual.ImpactRadius;
                visual.ImpactFlash.localScale = Vector3.one * flashScale;
            }
        }

        private void UpdateLightningGeometry(LightningVisual visual)
        {
            int segments = Mathf.Max(4, _settings.LightningSegments);
            Vector3[] positions = new Vector3[segments];
            Vector3 direction = visual.End - visual.Start;
            Vector3 axis = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.down;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up;
            Vector3 side = Vector3.Cross(axis, reference).normalized;
            Vector3 secondSide = Vector3.Cross(axis, side).normalized;
            float amplitude = Mathf.Min(
                _settings.LightningMaximumJitter,
                Mathf.Max(_settings.LightningMinimumJitter, direction.magnitude * _settings.LightningJitterPerMeter));
            for (int i = 0; i < segments; i++)
            {
                float along = i / (float)(segments - 1);
                Vector3 point = Vector3.Lerp(visual.Start, visual.End, along);
                if (i > 0 && i < segments - 1)
                {
                    float envelope = Mathf.Sin(along * Mathf.PI);
                    float first = Mathf.Sin((Time.time * 71f) + visual.Seed + (i * 12.7f));
                    float second = Mathf.Sin((Time.time * 89f) + (visual.Seed * 1.7f) + (i * 8.3f));
                    point += ((side * first) + (secondSide * second)) * amplitude * envelope;
                }
                positions[i] = point;
            }
            visual.Main.positionCount = segments;
            visual.Main.SetPositions(positions);

            for (int branchIndex = 0; branchIndex < visual.Branches.Count; branchIndex++)
            {
                LineRenderer branch = visual.Branches[branchIndex];
                int startIndex = Mathf.Clamp(
                    Mathf.RoundToInt((branchIndex + 1f) * (segments - 2f) / (visual.Branches.Count + 1f)),
                    1,
                    segments - 2);
                Vector3 branchStart = positions[startIndex];
                float sign = branchIndex % 2 == 0 ? 1f : -1f;
                Vector3 branchEnd = branchStart +
                    ((side * sign) + (secondSide * Mathf.Sin(visual.Seed + branchIndex))).normalized *
                    _settings.LightningBranchLength;
                int branchSegments = Mathf.Max(4, segments / 3);
                branch.positionCount = branchSegments;
                for (int pointIndex = 0; pointIndex < branchSegments; pointIndex++)
                {
                    float along = pointIndex / (float)(branchSegments - 1);
                    branch.SetPosition(pointIndex, Vector3.Lerp(branchStart, branchEnd, along));
                }
            }
        }

        private void CreateStrikeScar(Vector3 position)
        {
            GameObject scar = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            scar.name = "Fused Sand Lightning Scar";
            scar.transform.SetParent(transform, true);
            scar.transform.position = position + (Vector3.up * _settings.StrikeScarHeightOffset);
            scar.transform.localScale = new Vector3(
                _settings.StrikeScarRadius * 2f,
                _settings.StrikeScarThickness,
                _settings.StrikeScarRadius * 2f);
            Material material = CreateScarMaterial();
            Renderer renderer = scar.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            Collider collider = scar.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _strikeScars.Add(new StrikeScarVisual
            {
                Root = scar,
                Material = material,
                Duration = Mathf.Max(0.1f, _settings.StrikeScarLifetime),
                Remaining = Mathf.Max(0.1f, _settings.StrikeScarLifetime),
            });
        }

        private void UpdateStrikeScars(float deltaTime)
        {
            for (int i = _strikeScars.Count - 1; i >= 0; i--)
            {
                StrikeScarVisual scar = _strikeScars[i];
                scar.Remaining -= deltaTime;
                if (scar.Remaining <= 0f)
                {
                    Destroy(scar.Root);
                    Destroy(scar.Material);
                    _strikeScars.RemoveAt(i);
                    continue;
                }
                float glow = Mathf.Clamp01(scar.Remaining / scar.Duration);
                if (scar.Material.HasProperty("_EmissiveColor"))
                {
                    scar.Material.SetColor("_EmissiveColor", _settings.StrikeScarEmission * glow);
                }
            }
        }

        private void CreateInteriorVolume()
        {
            GameObject volumeObject = new GameObject("Electrical Storm Interior Atmosphere");
            volumeObject.transform.SetParent(transform, false);
            _interiorVolume = volumeObject.AddComponent<Volume>();
            _interiorVolume.isGlobal = true;
            _interiorVolume.priority = _settings.InteriorVolumePriority;
            _interiorVolume.weight = 0f;
            _interiorProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _interiorProfile.name = "Runtime Electrical Storm Atmosphere";
            _interiorVolume.sharedProfile = _interiorProfile;
            ColorAdjustments color = _interiorProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(_settings.InteriorPostExposure);
            color.saturation.Override(_settings.InteriorSaturation);
            color.contrast.Override(_settings.InteriorContrast);
            color.colorFilter.Override(_settings.InteriorColorFilter);
            Bloom bloom = _interiorProfile.Add<Bloom>(true);
            bloom.intensity.Override(_settings.InteriorBloomIntensity);
            bloom.threshold.Override(_settings.InteriorBloomThreshold);
        }

        private Material CreateCloudMaterial()
        {
            Shader shader = Shader.Find("HDRP/Lit");
            Material material = new Material(shader) { name = "Electrical Storm Cloud Mass" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", _settings.StormCloudColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", _settings.StormCloudSmoothness);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", Color.black);
            if (material.HasProperty("_EmissiveExposureWeight")) material.SetFloat("_EmissiveExposureWeight", 0f);
            return material;
        }

        private static Material CreateEnergyMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            Material material = new Material(shader) { name = materialName };
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 1f);
            material.SetColor("_UnlitColor", color);
            material.SetColor("_EmissiveColor", color);
            material.SetFloat("_EmissiveExposureWeight", 0f);
            HDMaterial.ValidateMaterial(material);
            return material;
        }

        private Material CreateParticleMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("DuneVector/HDRP Weather Particle");
            if (shader == null) shader = Shader.Find("HDRP/Unlit");
            Material material = new Material(shader) { name = materialName };
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", _softParticleTexture);
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", color);
            if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", _softParticleTexture);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
            return material;
        }

        private Material CreateScarMaterial()
        {
            Shader shader = Shader.Find("HDRP/Lit");
            Material material = new Material(shader) { name = "Fused Sand Afterglow" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", _settings.StrikeScarColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", _settings.StrikeScarSmoothness);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", _settings.StrikeScarEmission);
            if (material.HasProperty("_EmissiveExposureWeight")) material.SetFloat("_EmissiveExposureWeight", 0f);
            return material;
        }

        private static LineRenderer CreateLine(string name, Transform parent, Material material)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 100;
            return line;
        }

        private void ConfigureParticleFade(ParticleSystem system)
        {
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, _settings.ParticleFadeInFraction),
                    new GradientAlphaKey(1f, _settings.ParticleFadeOutFraction),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = gradient;
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        private static Texture2D CreateSoftParticleTexture(int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Electrical Storm Soft Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 uv = new Vector2(x, y) / (resolution - 1f);
                    float alpha = Mathf.Clamp01(1f - (Vector2.Distance(uv, Vector2.one * 0.5f) * 2f));
                    pixels[(y * resolution) + x] = new Color(1f, 1f, 1f, alpha * alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private float NextInterval(float minimum, float maximum)
        {
            float safeMinimum = Mathf.Max(0.1f, minimum);
            return Mathf.Lerp(safeMinimum, Mathf.Max(safeMinimum, maximum), (float)_random.NextDouble());
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
        }

        private Vector3 RandomScale(Vector3 minimum, Vector3 maximum)
        {
            return new Vector3(
                RandomRange(Mathf.Min(minimum.x, maximum.x), Mathf.Max(minimum.x, maximum.x)),
                RandomRange(Mathf.Min(minimum.y, maximum.y), Mathf.Max(minimum.y, maximum.y)),
                RandomRange(Mathf.Min(minimum.z, maximum.z), Mathf.Max(minimum.z, maximum.z)));
        }

        private Vector3 RandomUnitSphere()
        {
            float vertical = RandomRange(-1f, 1f);
            float angle = RandomRange(0f, Mathf.PI * 2f);
            float horizontal = Mathf.Sqrt(Mathf.Max(0f, 1f - (vertical * vertical)));
            return new Vector3(
                horizontal * Mathf.Cos(angle),
                vertical,
                horizontal * Mathf.Sin(angle));
        }

        private void HandleWorldShift(Vector3 shift)
        {
            _chargedDust.Clear();
            _staticMotes.Clear();
            _convergingSparks.Clear();
            for (int i = 0; i < _lightning.Count; i++)
            {
                _lightning[i].Start += shift;
                _lightning[i].End += shift;
            }
            for (int i = 0; i < _strikeScars.Count; i++)
            {
                _strikeScars[i].Root.transform.position += shift;
            }
        }

        private void OnGUI()
        {
            if (_hazards == null || _visualBlend <= 0.01f || DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            _hudTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Bold,
                fontSize = _settings.HudTitleFontSize,
                normal = { textColor = _settings.HudTextColor },
            };
            _hudStatusStyle ??= new GUIStyle(_hudTitleStyle)
            {
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Normal,
                fontSize = _settings.HudStatusFontSize,
            };
            Rect panel = new Rect(
                _settings.HudLeft,
                _settings.HudTop,
                _settings.HudWidth,
                _settings.HudHeight);
            DrawRect(panel, WithAlpha(_settings.HudPanelColor, _settings.HudPanelColor.a * _visualBlend));
            DrawRect(
                new Rect(panel.x, panel.y, _settings.HudAccentWidth, panel.height),
                WithAlpha(_settings.HudAccentColor, _visualBlend));
            float contentX = panel.x + _settings.HudPadding;
            float contentWidth = panel.width - (_settings.HudPadding * 2f);
            float titleY = panel.y + _settings.HudPadding;
            GUI.Label(
                new Rect(contentX, titleY, contentWidth, _settings.HudTitleRowHeight),
                _settings.HudStormLabel,
                _hudTitleStyle);
            string status = _hazards.StrikePhase == ElectricalStrikePhase.Buildup ||
                _hazards.StrikePhase == ElectricalStrikePhase.TargetTelegraph
                    ? _settings.HudIonizationLabel
                    : _settings.HudInterferenceLabel;
            GUI.Label(
                new Rect(
                    contentX,
                    titleY + _settings.HudTitleRowHeight + _settings.HudTextRowGap,
                    contentWidth,
                    _settings.HudStatusRowHeight),
                status,
                _hudStatusStyle);
            float staticStrength = _hazards.IsElectricalInterferenceActive
                ? _visualBlend
                : _visualBlend * _settings.HudApproachStaticMultiplier;
            for (int i = 0; i < _settings.HudStaticLineCount; i++)
            {
                float phase = Mathf.Repeat(
                    (Time.unscaledTime * _settings.HudStaticSpeed) +
                    (i / (float)Mathf.Max(1, _settings.HudStaticLineCount)),
                    1f);
                float jitter = Mathf.Sin((Time.unscaledTime * _settings.HudStaticSpeed) + (i * 4.17f)) *
                    _settings.HudStaticJitter;
                Rect line = new Rect(
                    panel.x + jitter,
                    panel.y + (phase * panel.height),
                    panel.width,
                    _settings.HudStaticLineHeight);
                DrawRect(line, WithAlpha(_settings.HudStaticColor, _settings.HudStaticColor.a * staticStrength));
            }
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private void OnDestroy()
        {
            if (_hazards != null)
            {
                _hazards.StrikePhaseChanged -= HandleStrikePhaseChanged;
                _hazards.LightningTargetLocked -= HandleLightningTargetLocked;
                _hazards.LightningStruck -= HandleLightningStruck;
            }
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            if (_cloudMaterial != null) Destroy(_cloudMaterial);
            if (_lightningMaterial != null) Destroy(_lightningMaterial);
            if (_telegraphMaterial != null) Destroy(_telegraphMaterial);
            if (_chargedDustMaterial != null) Destroy(_chargedDustMaterial);
            if (_staticMoteMaterial != null) Destroy(_staticMoteMaterial);
            if (_softParticleTexture != null) Destroy(_softParticleTexture);
            if (_interiorProfile != null) Destroy(_interiorProfile);
            for (int i = 0; i < _strikeScars.Count; i++)
            {
                if (_strikeScars[i].Material != null) Destroy(_strikeScars[i].Material);
            }
        }
    }
}
