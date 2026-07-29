using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [Serializable]
    public sealed class DustDevilTuning
    {
        public bool Enabled = true;

        [Header("Procedural Distribution")]
        [Min(40f)] public float SpawnCellSize = 240f;
        [Range(0f, 1f)] public float SpawnChancePerCell = 0.36f;
        [Range(1, 6)] public int ActiveCellRadius = 2;
        [Min(0.05f)] public float StreamingRefreshInterval = 0.45f;
        [Min(0f)] public float StartingAreaExclusionRadius = 115f;
        [Range(0f, 89f)] public float MaximumGroundSlope = 30f;
        public int RandomSeedOffset = 8849;

        [Header("Funnel Dimensions")]
        [Min(10f)] public float ColumnHeight = 125f;
        [Min(0.5f)] public float BaseRadius = 5.5f;
        [Min(1f)] public float TopRadius = 22f;
        [Min(1f)] public float InteractionRadius = 15f;
        [Min(0.1f)] public float CoreRadius = 4.5f;
        [Min(0f)] public float VerticalInteractionPadding = 3f;

        [Header("Travel")]
        [Tooltip("Ground speed of each dust devil across the desert. Set to zero to keep funnels stationary.")]
        [Min(0f)] public float TravelSpeed = 8f;
        [Tooltip("Maximum rate at which a dust devil's travel heading wanders from side to side.")]
        [Min(0f)] public float TravelTurnSpeed = 9f;
        [Tooltip("Cycles per second of the smooth heading wander used while travelling.")]
        [Min(0f)] public float TravelWanderFrequency = 0.08f;

        [Header("Traversal Forces")]
        [Min(0f)] public float OuterUpwardAcceleration = 20f;
        [Min(0f)] public float CoreUpwardAcceleration = 72f;
        [Min(0f)] public float TangentialAcceleration = 46f;
        [Min(0f)] public float InwardAcceleration = 30f;
        [Min(0f)] public float TrajectorySpinDegreesPerSecond = 115f;
        [Tooltip("How quickly the drone spins with the funnel during control loss. Outside control loss, funnel influence scales this rate.")]
        [Min(0f)] public float DroneSpinDegreesPerSecond = 600f;
        [Range(0f, 1f)] public float GroundLaunchInfluenceThreshold = 0.2f;
        [Tooltip("One-time minimum upward speed applied when the drone crosses into the funnel's launch influence, even if it misses the core.")]
        [Min(0f)] public float MinimumEntryLaunchSpeed = 52f;
        [Range(0f, 1f)] public float CoreLaunchInfluenceThreshold = 0.62f;
        [Min(0f)] public float CoreMinimumLaunchSpeed = 78f;
        [Min(0f)] public float MaximumUpwardSpeed = 92f;
        [Min(1f)] public float LaunchFlightSpeedMultiplier = 1.65f;
        [Min(0f)] public float LaunchUpwardWeight = 1f;
        [Min(0f)] public float LaunchForwardWeight = 0.42f;
        [Min(0f)] public float LaunchTangentialWeight = 0.18f;

        [Header("Control Disruption")]
        [Tooltip("Minimum funnel influence required to trigger the temporary airbrake and input lock.")]
        [Range(0f, 1f)] public float ControlLossInfluenceThreshold = 0.2f;
        [Tooltip("Seconds that player input remains locked while the airbrake is applied after entering a funnel.")]
        [Min(0f)] public float ControlLossDuration = 2f;
        [Tooltip("Seconds of full-speed drone spin before it begins fading to zero over the remaining control-loss duration.")]
        [Min(0f)] public float ControlLossSpinFadeDelay = 1f;

        [Header("Fragile Cargo Hazard")]
        [Min(0f)] public float FragileCargoDamagePerSecond = 13f;
        [Min(0.05f)] public float CargoDamageInterval = 0.5f;
        [Range(0f, 1f)] public float CargoDamageInfluenceThreshold = 0.25f;

        [Header("Distant Funnel Ribbon")]
        [Range(1, 8)] public int RibbonCount = 3;
        [Range(8, 128)] public int RibbonSegments = 64;
        [Min(0f)] public float RibbonTurns = 5.5f;
        [Min(0.05f)] public float RibbonWidth = 4.8f;
        [Min(0f)] public float RibbonRadiusVariation = 0.12f;
        [Min(0f)] public float RibbonRadiusVariationWaves = 3f;
        [Range(0.01f, 0.49f)] public float RibbonEndFadeFraction = 0.2f;
        public float FunnelRotationSpeed = 42f;
        [ColorUsage(false)] public Color RibbonColor = new Color(0.58f, 0.34f, 0.15f, 0.34f);

        [Header("Column Particles")]
        [Range(8, 1024)] public int ColumnParticleBudget = 420;
        [Min(0f)] public float ColumnEmissionRate = 115f;
        public Vector2 ColumnParticleLifetime = new Vector2(3.8f, 7f);
        public Vector2 ColumnParticleSize = new Vector2(1.4f, 4.8f);
        [Min(0f)] public float ParticleUpwardSpeed = 17f;
        public float ParticleOrbitalSpeed = 2.8f;
        [Min(0f)] public float ParticleNoiseStrength = 3.4f;
        [Min(0.001f)] public float ParticleNoiseFrequency = 0.16f;
        [Range(0f, 1f)] public float ColumnRadiusThickness = 1f;
        [ColorUsage(false)] public Color ColumnParticleColor = new Color(0.68f, 0.45f, 0.23f, 0.5f);

        [Header("Ground Sand Skirt")]
        [Range(8, 512)] public int GroundParticleBudget = 180;
        [Min(0f)] public float GroundEmissionRate = 62f;
        public Vector2 GroundParticleLifetime = new Vector2(1.1f, 2.5f);
        public Vector2 GroundParticleSize = new Vector2(0.5f, 1.8f);
        [Min(0f)] public float GroundSpraySpeed = 13f;
        [Range(0f, 1f)] public float GroundRadiusThickness = 0.45f;
        [Min(0f)] public float GroundVelocityStretch = 0.18f;
        [Min(0f)] public float GroundStreakLength = 2.2f;
        [ColorUsage(false)] public Color GroundParticleColor = new Color(0.78f, 0.51f, 0.24f, 0.62f);

        [Header("Particle Fade")]
        [Range(0.01f, 0.49f)] public float ParticleFadeInFraction = 0.12f;
        [Range(0.51f, 0.99f)] public float ParticleFadeOutStartFraction = 0.74f;
        [Range(0f, 1f)] public float ParticleMidlifeAlpha = 0.78f;

        [Header("Distance LOD")]
        [Min(0f)] public float FullDetailDistance = 260f;
        [Min(0f)] public float CullDistance = 850f;
        [Range(0f, 1f)] public float DistantEmissionMultiplier = 0.28f;
    }

    public readonly struct DustDevilSample
    {
        public readonly Vector3 Acceleration;
        public readonly Vector3 Tangent;
        public readonly float Influence;
        public readonly float CoreInfluence;
        public readonly float SpinSign;
        public readonly int SourceId;

        public DustDevilSample(
            Vector3 acceleration,
            Vector3 tangent,
            float influence,
            float coreInfluence,
            float spinSign,
            int sourceId)
        {
            Acceleration = acceleration;
            Tangent = tangent;
            Influence = influence;
            CoreInfluence = coreInfluence;
            SpinSign = spinSign;
            SourceId = sourceId;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorDustDevilSystem : MonoBehaviour
    {
        private sealed class RuntimeDustDevil
        {
            public Vector2Int Cell;
            public LogicalPosition LogicalPosition;
            public int Identity;
            public float SpinSign;
            public Transform Root;
            public Transform Funnel;
            public ParticleSystem ColumnParticles;
            public ParticleSystem GroundParticles;
            public Mesh RibbonMesh;
            public Vector3 Center;
            public float TravelHeading;
            public float TravelAge;
            public float TravelPhase;
        }

        private readonly Dictionary<Vector2Int, RuntimeDustDevil> _instances =
            new Dictionary<Vector2Int, RuntimeDustDevil>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();
        private DroneCharacterController _player;
        private DronePlayer _playerInput;
        private Camera _camera;
        private DesertWorldStreamer _world;
        private DuneVectorCourierGame _courierGame;
        private DustDevilTuning _settings;
        private Material _particleMaterial;
        private Material _ribbonMaterial;
        private float _streamingTimer;
        private float _cargoDamageTimer;
        private int _controlLossSourceId = int.MinValue;
        private Vector2Int _lastPlayerCell = new Vector2Int(int.MinValue, int.MinValue);

        public DustDevilSample CurrentPlayerSample { get; private set; }
        public int ActiveDustDevilCount => _instances.Count;
        public bool IsControlDisruptionActive => _playerInput != null
            && _playerInput.IsHazardControlLocked;
        public float ControlDisruptionSpinSign { get; private set; } = 1f;
        public float ControlDisruptionSpinMultiplier
        {
            get
            {
                if (!IsControlDisruptionActive)
                {
                    return 0f;
                }

                float duration = Mathf.Max(0f, _settings.ControlLossDuration);
                float fadeStart = Mathf.Clamp(
                    _settings.ControlLossSpinFadeDelay,
                    0f,
                    duration);
                float elapsed = Mathf.Max(
                    0f,
                    duration - _playerInput.HazardControlLockTimeRemaining);
                float fadeDuration = duration - fadeStart;
                return elapsed <= fadeStart
                    ? 1f
                    : fadeDuration > 0f
                        ? 1f - Mathf.Clamp01((elapsed - fadeStart) / fadeDuration)
                        : 0f;
            }
        }

        public void Initialize(
            DroneCharacterController player,
            DronePlayer playerInput,
            Camera viewCamera,
            DesertWorldStreamer world,
            DuneVectorCourierGame courierGame,
            DustDevilTuning settings)
        {
            _player = player;
            _playerInput = playerInput;
            _camera = viewCamera;
            _world = world;
            _courierGame = courierGame;
            _settings = settings;
            _particleMaterial = CreateTransparentMaterial("Dust Devil Particle Material", Color.white);
            _ribbonMaterial = CreateTransparentMaterial("Dust Devil Funnel Material", settings.RibbonColor);
            _world.WorldShifted += HandleWorldShift;
            RefreshStreaming(true);
            _player.BindDustDevils(this, settings);
        }

        public DustDevilSample Sample(Vector3 worldPosition)
        {
            RuntimeDustDevil strongest = null;
            Vector3 strongestOffset = Vector3.zero;
            float strongestInfluence = 0f;
            float strongestCoreInfluence = 0f;

            foreach (RuntimeDustDevil devil in _instances.Values)
            {
                Vector3 offset = worldPosition - devil.Center;
                float height = Mathf.Max(0.01f, _settings.ColumnHeight);
                if (offset.y < -_settings.VerticalInteractionPadding
                    || offset.y > height + _settings.VerticalInteractionPadding)
                {
                    continue;
                }

                float height01 = Mathf.Clamp01(offset.y / height);
                float expandingRadius = Mathf.Lerp(
                    _settings.InteractionRadius,
                    Mathf.Max(_settings.InteractionRadius, _settings.TopRadius),
                    height01);
                float planarDistance = new Vector2(offset.x, offset.z).magnitude;
                if (planarDistance >= expandingRadius)
                {
                    continue;
                }

                float influence = Mathf.SmoothStep(0f, 1f, 1f - (planarDistance / expandingRadius));
                if (influence <= strongestInfluence)
                {
                    continue;
                }

                strongest = devil;
                strongestOffset = offset;
                strongestInfluence = influence;
                strongestCoreInfluence = Mathf.SmoothStep(
                    0f,
                    1f,
                    1f - Mathf.Clamp01(planarDistance / Mathf.Max(0.01f, _settings.CoreRadius)));
            }

            if (strongest == null)
            {
                return new DustDevilSample(Vector3.zero, Vector3.zero, 0f, 0f, 1f, 0);
            }

            Vector3 radial = Vector3.ProjectOnPlane(strongestOffset, Vector3.up);
            if (radial.sqrMagnitude < 0.001f)
            {
                radial = Vector3.forward;
            }
            radial.Normalize();
            Vector3 tangent = new Vector3(-radial.z, 0f, radial.x) * strongest.SpinSign;
            float upwardAcceleration = Mathf.Lerp(
                _settings.OuterUpwardAcceleration,
                _settings.CoreUpwardAcceleration,
                strongestCoreInfluence);
            Vector3 acceleration = (
                (Vector3.up * upwardAcceleration)
                + (tangent * _settings.TangentialAcceleration)
                - (radial * _settings.InwardAcceleration)) * strongestInfluence;
            return new DustDevilSample(
                acceleration,
                tangent,
                strongestInfluence,
                strongestCoreInfluence,
                strongest.SpinSign,
                strongest.Identity);
        }

        private void Update()
        {
            if (_player == null || _world == null)
            {
                return;
            }

            _streamingTimer -= Time.deltaTime;
            Vector2Int playerCell = LogicalToCell(_world.LogicalPlayerPosition);
            if (_streamingTimer <= 0f || playerCell != _lastPlayerCell)
            {
                RefreshStreaming(playerCell != _lastPlayerCell);
            }

            TickTravel(Time.deltaTime);
            CurrentPlayerSample = Sample(_player.WorldCenter);
            TickControlDisruption();
            TickCargoHazard(Time.deltaTime);
            TickVisuals(Time.deltaTime);
        }

        private void TickControlDisruption()
        {
            if (_playerInput == null
                || CurrentPlayerSample.Influence <= 0f
                || CurrentPlayerSample.Influence < _settings.ControlLossInfluenceThreshold)
            {
                _controlLossSourceId = int.MinValue;
                return;
            }

            if (_controlLossSourceId == CurrentPlayerSample.SourceId)
            {
                return;
            }

            _controlLossSourceId = CurrentPlayerSample.SourceId;
            ControlDisruptionSpinSign = CurrentPlayerSample.SpinSign;
            _playerInput.ApplyHazardAirBrake(_settings.ControlLossDuration);
        }

        private void TickTravel(float deltaTime)
        {
            float speed = Mathf.Max(0f, _settings.TravelSpeed);
            float step = Mathf.Max(0f, deltaTime);
            if (speed <= 0f || step <= 0f)
            {
                return;
            }

            float wanderFrequency = Mathf.Max(0f, _settings.TravelWanderFrequency);
            float turnSpeed = Mathf.Max(0f, _settings.TravelTurnSpeed);
            foreach (RuntimeDustDevil devil in _instances.Values)
            {
                devil.TravelAge += step;
                float wander = Mathf.Sin(
                    devil.TravelPhase + (devil.TravelAge * wanderFrequency * Mathf.PI * 2f));
                devil.TravelHeading += wander * turnSpeed * step;

                float headingRadians = devil.TravelHeading * Mathf.Deg2Rad;
                double distance = speed * step;
                devil.LogicalPosition = new LogicalPosition(
                    devil.LogicalPosition.X + (Math.Cos(headingRadians) * distance),
                    devil.LogicalPosition.Z + (Math.Sin(headingRadians) * distance));
                Reposition(devil, false);
            }
        }

        private void TickCargoHazard(float deltaTime)
        {
            if (_courierGame == null
                || CurrentPlayerSample.Influence < _settings.CargoDamageInfluenceThreshold)
            {
                _cargoDamageTimer = 0f;
                return;
            }

            _cargoDamageTimer -= Mathf.Max(0f, deltaTime);
            if (_cargoDamageTimer > 0f)
            {
                return;
            }

            float interval = Mathf.Max(0.05f, _settings.CargoDamageInterval);
            _cargoDamageTimer = interval;
            _courierGame.DamageFragileCargo(
                _settings.FragileCargoDamagePerSecond * interval * CurrentPlayerSample.Influence);
        }

        private void TickVisuals(float deltaTime)
        {
            Vector3 viewer = _camera != null ? _camera.transform.position : _player.WorldCenter;
            float fullDistance = Mathf.Max(0f, _settings.FullDetailDistance);
            float cullDistance = Mathf.Max(fullDistance, _settings.CullDistance);
            foreach (RuntimeDustDevil devil in _instances.Values)
            {
                float distance = Vector3.Distance(viewer, devil.Center);
                bool visible = distance <= cullDistance;
                if (devil.Root.gameObject.activeSelf != visible)
                {
                    devil.Root.gameObject.SetActive(visible);
                }
                if (!visible)
                {
                    continue;
                }

                devil.Funnel.Rotate(
                    0f,
                    _settings.FunnelRotationSpeed * devil.SpinSign * Mathf.Max(0f, deltaTime),
                    0f,
                    Space.Self);
                float detail01 = 1f - Mathf.InverseLerp(fullDistance, cullDistance, distance);
                float emissionLod = Mathf.Lerp(_settings.DistantEmissionMultiplier, 1f, detail01);
                SetEmission(devil.ColumnParticles, _settings.ColumnEmissionRate * emissionLod);
                SetEmission(devil.GroundParticles, _settings.GroundEmissionRate * emissionLod);
            }
        }

        private void RefreshStreaming(bool force)
        {
            _streamingTimer = Mathf.Max(0.05f, _settings.StreamingRefreshInterval);
            Vector2Int playerCell = LogicalToCell(_world.LogicalPlayerPosition);
            if (!force && playerCell == _lastPlayerCell)
            {
                return;
            }
            _lastPlayerCell = playerCell;

            int radius = Mathf.Max(1, _settings.ActiveCellRadius);
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int cell = playerCell + new Vector2Int(x, z);
                    if (!_instances.ContainsKey(cell) && ShouldSpawn(cell))
                    {
                        CreateDustDevil(cell);
                    }
                }
            }

            _removalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, RuntimeDustDevil> entry in _instances)
            {
                if (Mathf.Max(
                    Mathf.Abs(entry.Key.x - playerCell.x),
                    Mathf.Abs(entry.Key.y - playerCell.y)) > radius)
                {
                    _removalBuffer.Add(entry.Key);
                }
            }
            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                RemoveDustDevil(_removalBuffer[i]);
            }
        }

        private bool ShouldSpawn(Vector2Int cell)
        {
            if (DuneVectorMath.Hash01(
                cell.x,
                cell.y,
                _world.WorldSeed,
                _settings.RandomSeedOffset) > _settings.SpawnChancePerCell)
            {
                return false;
            }

            LogicalPosition position = GetLogicalPosition(cell);
            double startX = position.X - DesertWorldStreamer.StartingLogicalPosition.x;
            double startZ = position.Z - DesertWorldStreamer.StartingLogicalPosition.y;
            double exclusion = _settings.StartingAreaExclusionRadius;
            if ((startX * startX) + (startZ * startZ) < exclusion * exclusion)
            {
                return false;
            }

            Vector3 normal = _world.HeightField.SampleNormal(position.X, position.Z);
            return Vector3.Angle(normal, Vector3.up) <= _settings.MaximumGroundSlope;
        }

        private void CreateDustDevil(Vector2Int cell)
        {
            LogicalPosition logicalPosition = GetLogicalPosition(cell);
            GameObject rootObject = new GameObject($"Dust Devil [{cell.x}, {cell.y}]");
            rootObject.transform.SetParent(transform, false);
            GameObject funnelObject = new GameObject("Distant Helical Funnel");
            funnelObject.transform.SetParent(rootObject.transform, false);

            Mesh ribbonMesh = CreateRibbonMesh(cell);
            MeshFilter meshFilter = funnelObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = ribbonMesh;
            MeshRenderer meshRenderer = funnelObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _ribbonMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            float spinSign = DuneVectorMath.Hash01(
                cell.x,
                cell.y,
                _world.WorldSeed,
                _settings.RandomSeedOffset + 1) < 0.5f ? -1f : 1f;
            RuntimeDustDevil devil = new RuntimeDustDevil
            {
                Cell = cell,
                LogicalPosition = logicalPosition,
                Identity = unchecked((int)DuneVectorMath.Hash(
                    cell.x,
                    cell.y,
                    _world.WorldSeed,
                    _settings.RandomSeedOffset + 2)),
                SpinSign = spinSign,
                Root = rootObject.transform,
                Funnel = funnelObject.transform,
                RibbonMesh = ribbonMesh,
                TravelHeading = DuneVectorMath.HashRange(
                    cell.x,
                    cell.y,
                    _world.WorldSeed,
                    _settings.RandomSeedOffset + 6,
                    0f,
                    360f),
                TravelPhase = DuneVectorMath.HashRange(
                    cell.x,
                    cell.y,
                    _world.WorldSeed,
                    _settings.RandomSeedOffset + 7,
                    0f,
                    Mathf.PI * 2f),
            };
            devil.ColumnParticles = CreateColumnParticles(rootObject.transform, spinSign);
            devil.GroundParticles = CreateGroundParticles(rootObject.transform, spinSign);
            _instances.Add(cell, devil);
            Reposition(devil, true);
        }

        private ParticleSystem CreateColumnParticles(Transform parent, float spinSign)
        {
            GameObject particleObject = new GameObject("Rising Spiral Dust");
            particleObject.transform.SetParent(parent, false);
            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, _settings.ColumnParticleBudget);
            main.startLifetime = OrderedCurve(_settings.ColumnParticleLifetime);
            main.startSize = OrderedCurve(_settings.ColumnParticleSize);
            main.startSpeed = _settings.ParticleUpwardSpeed;
            main.startColor = _settings.ColumnParticleColor;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.ConeVolume;
            shape.radius = _settings.BaseRadius;
            shape.radiusThickness = _settings.ColumnRadiusThickness;
            shape.angle = Mathf.Atan2(
                Mathf.Max(0f, _settings.TopRadius - _settings.BaseRadius),
                Mathf.Max(0.01f, _settings.ColumnHeight)) * Mathf.Rad2Deg;
            shape.length = _settings.ColumnHeight;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.orbitalY = _settings.ParticleOrbitalSpeed * spinSign;

            ConfigureParticleFadeAndNoise(system);
            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetEmission(system, _settings.ColumnEmissionRate);
            system.Play(true);
            return system;
        }

        private ParticleSystem CreateGroundParticles(Transform parent, float spinSign)
        {
            GameObject particleObject = new GameObject("Ground Sand Skirt");
            particleObject.transform.SetParent(parent, false);
            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, _settings.GroundParticleBudget);
            main.startLifetime = OrderedCurve(_settings.GroundParticleLifetime);
            main.startSize = OrderedCurve(_settings.GroundParticleSize);
            main.startSpeed = _settings.GroundSpraySpeed;
            main.startColor = _settings.GroundParticleColor;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _settings.InteractionRadius;
            shape.radiusThickness = _settings.GroundRadiusThickness;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.orbitalY = _settings.ParticleOrbitalSpeed * spinSign;

            ConfigureParticleFadeAndNoise(system);
            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = _settings.GroundVelocityStretch;
            renderer.lengthScale = _settings.GroundStreakLength;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetEmission(system, _settings.GroundEmissionRate);
            system.Play(true);
            return system;
        }

        private void ConfigureParticleFadeAndNoise(ParticleSystem system)
        {
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.ParticleNoiseStrength;
            noise.frequency = _settings.ParticleNoiseFrequency;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, _settings.ParticleFadeInFraction),
                    new GradientAlphaKey(_settings.ParticleMidlifeAlpha, _settings.ParticleFadeOutStartFraction),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = fade;
        }

        private Mesh CreateRibbonMesh(Vector2Int cell)
        {
            int ribbonCount = Mathf.Max(1, _settings.RibbonCount);
            int segments = Mathf.Max(8, _settings.RibbonSegments);
            int vertexCount = ribbonCount * (segments + 1) * 2;
            int indexCount = ribbonCount * segments * 6;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[indexCount];
            int vertex = 0;
            int index = 0;
            float phaseOffset = DuneVectorMath.HashRange(
                cell.x,
                cell.y,
                _world.WorldSeed,
                _settings.RandomSeedOffset + 3,
                0f,
                Mathf.PI * 2f);

            for (int ribbon = 0; ribbon < ribbonCount; ribbon++)
            {
                int ribbonStart = vertex;
                float ribbonPhase = phaseOffset + ((Mathf.PI * 2f * ribbon) / ribbonCount);
                for (int segment = 0; segment <= segments; segment++)
                {
                    float height01 = segment / (float)segments;
                    float angle = ribbonPhase + (height01 * _settings.RibbonTurns * Mathf.PI * 2f);
                    float variation = 1f + (Mathf.Sin(
                        (height01 * Mathf.PI * 2f * _settings.RibbonRadiusVariationWaves) + ribbonPhase)
                        * _settings.RibbonRadiusVariation);
                    float radius = Mathf.Lerp(_settings.BaseRadius, _settings.TopRadius, height01) * variation;
                    Vector3 center = new Vector3(
                        Mathf.Cos(angle) * radius,
                        height01 * _settings.ColumnHeight,
                        Mathf.Sin(angle) * radius);
                    float halfWidth = _settings.RibbonWidth * 0.5f;
                    vertices[vertex] = center + (Vector3.down * halfWidth);
                    vertices[vertex + 1] = center + (Vector3.up * halfWidth);
                    uv[vertex] = new Vector2(height01, 0f);
                    uv[vertex + 1] = new Vector2(height01, 1f);
                    float fadeFraction = Mathf.Max(0.01f, _settings.RibbonEndFadeFraction);
                    float endFade = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Min(height01 / fadeFraction, (1f - height01) / fadeFraction));
                    colors[vertex] = new Color(1f, 1f, 1f, endFade);
                    colors[vertex + 1] = colors[vertex];
                    vertex += 2;

                    if (segment >= segments)
                    {
                        continue;
                    }
                    int current = ribbonStart + (segment * 2);
                    triangles[index++] = current;
                    triangles[index++] = current + 1;
                    triangles[index++] = current + 2;
                    triangles[index++] = current + 1;
                    triangles[index++] = current + 3;
                    triangles[index++] = current + 2;
                }
            }

            Mesh mesh = new Mesh { name = $"Dust Devil Funnel [{cell.x}, {cell.y}]" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private LogicalPosition GetLogicalPosition(Vector2Int cell)
        {
            float size = Mathf.Max(1f, _settings.SpawnCellSize);
            float margin = Mathf.Min(size * 0.45f, Mathf.Max(_settings.TopRadius, _settings.InteractionRadius));
            float minimum = margin;
            float maximum = Mathf.Max(minimum, size - margin);
            float localX = DuneVectorMath.HashRange(
                cell.x,
                cell.y,
                _world.WorldSeed,
                _settings.RandomSeedOffset + 4,
                minimum,
                maximum);
            float localZ = DuneVectorMath.HashRange(
                cell.x,
                cell.y,
                _world.WorldSeed,
                _settings.RandomSeedOffset + 5,
                minimum,
                maximum);
            return new LogicalPosition((cell.x * (double)size) + localX, (cell.y * (double)size) + localZ);
        }

        private Vector2Int LogicalToCell(LogicalPosition logical)
        {
            double size = Math.Max(1.0, _settings.SpawnCellSize);
            return new Vector2Int(
                (int)Math.Floor(logical.X / size),
                (int)Math.Floor(logical.Z / size));
        }

        private void Reposition(RuntimeDustDevil devil, bool clearParticles)
        {
            double height = _world.HeightField.SampleHeight(
                devil.LogicalPosition.X,
                devil.LogicalPosition.Z);
            devil.Center = _world.LogicalToLocal(
                devil.LogicalPosition.X,
                height,
                devil.LogicalPosition.Z);
            devil.Root.position = devil.Center;
            if (clearParticles)
            {
                devil.ColumnParticles.Clear(true);
                devil.GroundParticles.Clear(true);
            }
        }

        private void HandleWorldShift(Vector3 shift)
        {
            foreach (RuntimeDustDevil devil in _instances.Values)
            {
                Reposition(devil, true);
            }
        }

        private void RemoveDustDevil(Vector2Int cell)
        {
            if (!_instances.TryGetValue(cell, out RuntimeDustDevil devil))
            {
                return;
            }
            _instances.Remove(cell);
            if (devil.Root != null)
            {
                Destroy(devil.Root.gameObject);
            }
            if (devil.RibbonMesh != null)
            {
                Destroy(devil.RibbonMesh);
            }
        }

        private static ParticleSystem.MinMaxCurve OrderedCurve(Vector2 range)
        {
            return new ParticleSystem.MinMaxCurve(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        private static Material CreateTransparentMaterial(string materialName, Color tint)
        {
            Shader shader = Shader.Find("DuneVector/HDRP Weather Particle");
            if (shader == null)
            {
                shader = Shader.Find("HDRP/Unlit");
            }
            Material material = new Material(shader) { name = materialName };
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", tint);
            if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
            if (material.HasProperty("_BlendMode")) material.SetFloat("_BlendMode", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ALPHA");
            return material;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            _removalBuffer.Clear();
            foreach (Vector2Int cell in _instances.Keys)
            {
                _removalBuffer.Add(cell);
            }
            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                RemoveDustDevil(_removalBuffer[i]);
            }
            if (_particleMaterial != null)
            {
                Destroy(_particleMaterial);
            }
            if (_ribbonMaterial != null)
            {
                Destroy(_ribbonMaterial);
            }
        }
    }
}
