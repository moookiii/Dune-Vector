using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum WindFieldType
    {
        Crosswind,
        Headwind,
        Tailwind,
        Updraft,
        Downdraft,
    }

    [Serializable]
    public sealed class WindFieldDefinition
    {
        public string DisplayName;
        public WindFieldType Type;
        [Tooltip("Persistent X/Z position in the desert's logical coordinate space.")]
        public Vector2 LogicalPosition;
        [Min(0f)] public float HeightAboveTerrain;
        [Tooltip("Full width, height, and depth of the softly blended wind region.")]
        public Vector3 Size;
        [Tooltip("World-space airflow direction. It is normalized at runtime.")]
        public Vector3 Direction;
        [Min(0f)] public float Force;
        [Range(0f, 1f)] public float Turbulence;
    }

    [Serializable]
    public sealed class WindFieldSystemTuning
    {
        public bool Enabled = true;
        [Tooltip("Procedural seed combined with the world seed for wind turbulence and particle variation.")]
        public int WindSeed = 4187;

        [Header("Procedural Placement")]
        public bool ProceduralPlacementEnabled = true;
        [Min(40f)] public float SpawnCellSize = 320f;
        [Range(0f, 1f)] public float SpawnChancePerCell = 0.52f;
        [Range(1, 6)] public int ActiveCellRadius = 2;
        [Min(0.05f)] public float StreamingRefreshInterval = 0.5f;
        [Min(0f)] public float StartingAreaExclusionRadius = 120f;
        [Range(0f, 60f)] public float MaximumGroundSlope = 38f;
        [Range(0f, 0.49f)] public float PositionJitterFraction = 0.38f;
        [Min(0.1f)] public float MinimumSizeMultiplier = 0.78f;
        [Min(0.1f)] public float MaximumSizeMultiplier = 1.24f;
        [Min(0f)] public float HeightVariation = 14f;
        [Range(0f, 180f)] public float DirectionYawVariation = 32f;
        [Range(0f, 1f)] public float ForceVariation = 0.22f;
        [Range(0f, 1f)] public float TurbulenceVariation = 0.16f;
        [Range(0.05f, 0.95f)] public float CoreRadius = 0.42f;
        [Min(0f)] public float PlayerForceResponse = 1f;
        [Min(0f)] public float GroundedForceMultiplier = 0.32f;
        [Min(0f)] public float FlightForceMultiplier = 1f;
        [Min(0f)] public float TurbulenceForce = 4.5f;
        [Min(0f)] public float TurbulenceFrequency = 0.17f;
        [Range(0f, 1f)] public float UpdraftLaunchInfluenceThreshold = 0.12f;
        [Min(0f)] public float UpdraftMinimumLaunchSpeed = 8f;

        [Header("World-space Streamlines")]
        [Range(0, 512)] public int StreamlineParticleBudget = 180;
        [Min(0f)] public float StreamlineEmissionRate = 48f;
        [Min(0.1f)] public float MinimumParticleLifetime = 2.4f;
        [Min(0.1f)] public float MaximumParticleLifetime = 5.8f;
        [Min(0.001f)] public float MinimumParticleSize = 0.035f;
        [Min(0.001f)] public float MaximumParticleSize = 0.11f;
        [Min(0f)] public float AirflowVisualSpeedMultiplier = 1.7f;
        [Min(0f)] public float StreamlineLength = 4.8f;
        [Min(0f)] public float ParticleVelocityStretch = 0.085f;
        [Min(0.1f)] public float ParticleEdgeFalloff = 1.7f;
        [Min(0f)] public float VisualTurbulenceStrength = 1.8f;
        [Min(0.001f)] public float VisualTurbulenceFrequency = 0.09f;
        [ColorUsage(false)] public Color StreamlineColor = new Color(0.92f, 0.82f, 0.64f, 0.3f);

        [Header("Surface Sand")]
        [Range(0, 256)] public int SurfaceParticleBudget = 90;
        [Min(0f)] public float SurfaceEmissionRate = 16f;
        [Min(0f)] public float SurfaceLayerHeight = 1.2f;
        [Min(0f)] public float SurfaceWindSpeedMultiplier = 1.25f;
        [Min(0f)] public float SurfaceStreakLength = 2.8f;
        [ColorUsage(false)] public Color SurfaceSandColor = new Color(0.74f, 0.52f, 0.28f, 0.38f);

        [Header("Drone Interaction")]
        [Range(0, 128)] public int InteractionParticleBudget = 64;
        [Min(0f)] public float InteractionEmissionRate = 28f;
        [Min(0f)] public float InteractionRadius = 4.5f;
        [Min(0f)] public float InteractionStreakLength = 6.2f;
        [Min(0f)] public float RelativeVelocityInfluence = 0.7f;

        [Header("Distance LOD")]
        [Min(0f)] public float FullDetailDistance = 220f;
        [Min(0f)] public float CullDistance = 720f;
        [Range(0f, 1f)] public float DistantEmissionMultiplier = 0.22f;

        [Header("Wind Field Templates")]
        [Tooltip("Templates used for procedural type, shape, direction, force, and turbulence variation.")]
        public List<WindFieldDefinition> Fields = new List<WindFieldDefinition>();

        public void EnsureInitialized()
        {
            Fields ??= new List<WindFieldDefinition>();
        }
    }

    public readonly struct WindFieldSample
    {
        public readonly Vector3 Force;
        public readonly float Influence;
        public readonly WindFieldType DominantType;

        public WindFieldSample(Vector3 force, float influence, WindFieldType dominantType)
        {
            Force = force;
            Influence = influence;
            DominantType = dominantType;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorWindFieldSystem : MonoBehaviour
    {
        private sealed class RuntimeField
        {
            public WindFieldDefinition Definition;
            public Vector2Int Cell;
            public bool IsProcedural;
            public Transform Root;
            public ParticleSystem Streamlines;
            public ParticleSystem SurfaceSand;
            public Vector3 Center;
            public Vector3 Direction;
            public Vector3 TurbulenceOffset;
        }

        private DroneCharacterController _player;
        private Camera _camera;
        private DesertWorldStreamer _world;
        private WindFieldSystemTuning _settings;
        private readonly List<RuntimeField> _fields = new List<RuntimeField>();
        private readonly Dictionary<Vector2Int, RuntimeField> _proceduralFields =
            new Dictionary<Vector2Int, RuntimeField>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();
        private Material _particleMaterial;
        private Texture2D _particleTexture;
        private ParticleSystem _playerInteraction;
        private int _combinedWindSeed;
        private int _particleSeedIndex;
        private float _streamingTimer;
        private Vector2Int _lastPlayerCell = new Vector2Int(int.MinValue, int.MinValue);

        public WindFieldSample CurrentPlayerSample { get; private set; }

        public void Initialize(
            DroneCharacterController player,
            Camera viewCamera,
            DesertWorldStreamer world,
            WindFieldSystemTuning settings)
        {
            _player = player;
            _camera = viewCamera;
            _world = world;
            _settings = settings;
            _combinedWindSeed = unchecked(world.WorldSeed ^ settings.WindSeed);
            _particleTexture = CreateSoftParticleTexture(_settings.ParticleEdgeFalloff);
            _particleMaterial = CreateParticleMaterial(_particleTexture);

            if (_settings.ProceduralPlacementEnabled)
            {
                RefreshStreaming(true);
            }
            else
            {
                foreach (WindFieldDefinition definition in settings.Fields)
                {
                    if (IsValidTemplate(definition))
                    {
                        CreateField(definition, default, false);
                    }
                }
            }

            Vector3 interactionSize = Vector3.one * (_settings.InteractionRadius * 2f);
            _playerInteraction = CreateParticleLayer(
                transform,
                "Drone Wind Interaction",
                _settings.InteractionParticleBudget,
                interactionSize,
                _settings.InteractionStreakLength,
                _settings.StreamlineColor,
                Vector3.zero);

            _world.WorldShifted += HandleWorldShift;
            RepositionFields();
            _player.BindWindFields(this, settings);
        }

        public WindFieldSample Sample(Vector3 worldPosition, float time)
        {
            Vector3 totalForce = Vector3.zero;
            float strongestInfluence = 0f;
            WindFieldType dominantType = WindFieldType.Crosswind;

            for (int i = 0; i < _fields.Count; i++)
            {
                RuntimeField field = _fields[i];
                Vector3 halfSize = field.Definition.Size * 0.5f;
                Vector3 offset = worldPosition - field.Center;
                float normalizedDistance = Mathf.Max(
                    Mathf.Abs(offset.x / halfSize.x),
                    Mathf.Abs(offset.y / halfSize.y),
                    Mathf.Abs(offset.z / halfSize.z));
                if (normalizedDistance >= 1f)
                {
                    continue;
                }

                float edge01 = Mathf.InverseLerp(1f, Mathf.Clamp01(_settings.CoreRadius), normalizedDistance);
                float influence = Mathf.SmoothStep(0f, 1f, edge01);
                Vector3 force = field.Direction * field.Definition.Force;
                if (field.Definition.Turbulence > 0f && _settings.TurbulenceForce > 0f)
                {
                    float phase = time * _settings.TurbulenceFrequency;
                    Vector2 logical = field.Definition.LogicalPosition;
                    Vector3 turbulence = new Vector3(
                        Mathf.PerlinNoise(
                            (logical.x * 0.013f) + field.TurbulenceOffset.x,
                            phase + field.TurbulenceOffset.y) - 0.5f,
                        Mathf.PerlinNoise(
                            (logical.y * 0.017f) + field.TurbulenceOffset.y,
                            phase + field.TurbulenceOffset.z) - 0.5f,
                        Mathf.PerlinNoise(
                            phase + field.TurbulenceOffset.z,
                            (logical.x * 0.011f) + field.TurbulenceOffset.x) - 0.5f);
                    force += turbulence * (2f * _settings.TurbulenceForce * field.Definition.Turbulence);
                }
                totalForce += force * influence;
                if (influence > strongestInfluence)
                {
                    strongestInfluence = influence;
                    dominantType = field.Definition.Type;
                }
            }

            return new WindFieldSample(totalForce, strongestInfluence, dominantType);
        }

        private void Update()
        {
            if (_player == null)
            {
                return;
            }
            if (_settings.ProceduralPlacementEnabled)
            {
                _streamingTimer -= Time.deltaTime;
                Vector2Int playerCell = LogicalToCell(_world.LogicalPlayerPosition);
                if (_streamingTimer <= 0f || playerCell != _lastPlayerCell)
                {
                    RefreshStreaming(playerCell != _lastPlayerCell);
                }
            }
            CurrentPlayerSample = Sample(_player.WorldCenter, Time.time);
            UpdatePlayerInteraction();
            UpdateLod();
        }

        private void UpdatePlayerInteraction()
        {
            if (_playerInteraction == null)
            {
                return;
            }
            _playerInteraction.transform.position = _player.WorldCenter;
            SetEmission(
                _playerInteraction,
                _settings.InteractionEmissionRate * CurrentPlayerSample.Influence);
            Vector3 playerVelocity = _player.Motor != null ? _player.Motor.Velocity : Vector3.zero;
            Vector3 relativeAirflow = (CurrentPlayerSample.Force * _settings.AirflowVisualSpeedMultiplier)
                - (playerVelocity * _settings.RelativeVelocityInfluence);
            SetVelocity(_playerInteraction, relativeAirflow);
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
                    if (!_proceduralFields.ContainsKey(cell) && ShouldSpawn(cell))
                    {
                        CreateProceduralField(cell);
                    }
                }
            }

            _removalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, RuntimeField> entry in _proceduralFields)
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
                RemoveProceduralField(_removalBuffer[i]);
            }
        }

        private bool ShouldSpawn(Vector2Int cell)
        {
            if (_settings.Fields == null || _settings.Fields.Count == 0 ||
                DuneVectorMath.Hash01(cell.x, cell.y, _combinedWindSeed, 9203) >
                _settings.SpawnChancePerCell)
            {
                return false;
            }

            Vector2 logical = GetProceduralLogicalPosition(cell);
            Vector2 start = DesertWorldStreamer.StartingLogicalPosition;
            if ((logical - start).sqrMagnitude <
                _settings.StartingAreaExclusionRadius * _settings.StartingAreaExclusionRadius)
            {
                return false;
            }
            Vector3 normal = _world.HeightField.SampleNormal(logical.x, logical.y);
            return Vector3.Angle(normal, Vector3.up) <= _settings.MaximumGroundSlope;
        }

        private void CreateProceduralField(Vector2Int cell)
        {
            List<WindFieldDefinition> templates = _settings.Fields;
            int startIndex = Mathf.FloorToInt(
                DuneVectorMath.Hash01(cell.x, cell.y, _combinedWindSeed, 9221) * templates.Count);
            WindFieldDefinition template = null;
            for (int i = 0; i < templates.Count; i++)
            {
                WindFieldDefinition candidate = templates[(startIndex + i) % templates.Count];
                if (IsValidTemplate(candidate))
                {
                    template = candidate;
                    break;
                }
            }
            if (template == null)
            {
                return;
            }

            Vector2 logicalPosition = GetProceduralLogicalPosition(cell);
            float minimumScale = Mathf.Min(_settings.MinimumSizeMultiplier, _settings.MaximumSizeMultiplier);
            float maximumScale = Mathf.Max(_settings.MinimumSizeMultiplier, _settings.MaximumSizeMultiplier);
            float scale = DuneVectorMath.HashRange(
                cell.x, cell.y, _combinedWindSeed, 9239, minimumScale, maximumScale);
            float heightOffset = DuneVectorMath.HashRange(
                cell.x, cell.y, _combinedWindSeed, 9257,
                -_settings.HeightVariation, _settings.HeightVariation);
            float yaw = DuneVectorMath.HashRange(
                cell.x, cell.y, _combinedWindSeed, 9277,
                -_settings.DirectionYawVariation, _settings.DirectionYawVariation);
            float forceMultiplier = DuneVectorMath.HashRange(
                cell.x, cell.y, _combinedWindSeed, 9283,
                1f - _settings.ForceVariation, 1f + _settings.ForceVariation);
            float turbulenceOffset = DuneVectorMath.HashRange(
                cell.x, cell.y, _combinedWindSeed, 9293,
                -_settings.TurbulenceVariation, _settings.TurbulenceVariation);
            WindFieldDefinition generated = new WindFieldDefinition
            {
                DisplayName = $"{template.DisplayName} [{cell.x}, {cell.y}]",
                Type = template.Type,
                LogicalPosition = logicalPosition,
                HeightAboveTerrain = Mathf.Max(0f, template.HeightAboveTerrain + heightOffset),
                Size = template.Size * scale,
                Direction = Quaternion.Euler(0f, yaw, 0f) * template.Direction,
                Force = Mathf.Max(0f, template.Force * forceMultiplier),
                Turbulence = Mathf.Clamp01(template.Turbulence + turbulenceOffset),
            };
            RuntimeField field = CreateField(generated, cell, true);
            _proceduralFields.Add(cell, field);
            RepositionField(field);
        }

        private void RemoveProceduralField(Vector2Int cell)
        {
            if (!_proceduralFields.TryGetValue(cell, out RuntimeField field))
            {
                return;
            }
            _proceduralFields.Remove(cell);
            _fields.Remove(field);
            if (field.Root != null)
            {
                Destroy(field.Root.gameObject);
            }
        }

        private Vector2Int LogicalToCell(LogicalPosition position)
        {
            float cellSize = Mathf.Max(1f, _settings.SpawnCellSize);
            return new Vector2Int(
                Mathf.FloorToInt((float)(position.X / cellSize)),
                Mathf.FloorToInt((float)(position.Z / cellSize)));
        }

        private Vector2 GetProceduralLogicalPosition(Vector2Int cell)
        {
            float cellSize = Mathf.Max(1f, _settings.SpawnCellSize);
            float jitter = cellSize * Mathf.Clamp(_settings.PositionJitterFraction, 0f, 0.49f);
            return new Vector2(
                ((cell.x + 0.5f) * cellSize) + DuneVectorMath.HashRange(
                    cell.x, cell.y, _combinedWindSeed, 9301, -jitter, jitter),
                ((cell.y + 0.5f) * cellSize) + DuneVectorMath.HashRange(
                    cell.x, cell.y, _combinedWindSeed, 9311, -jitter, jitter));
        }

        private static bool IsValidTemplate(WindFieldDefinition definition)
        {
            return definition != null && definition.Size.x > 0f && definition.Size.y > 0f &&
                definition.Size.z > 0f;
        }

        private RuntimeField CreateField(WindFieldDefinition definition, Vector2Int cell, bool isProcedural)
        {
            GameObject rootObject = new GameObject(string.IsNullOrWhiteSpace(definition.DisplayName)
                ? definition.Type.ToString()
                : definition.DisplayName);
            rootObject.transform.SetParent(transform, false);

            Vector3 direction = definition.Direction.sqrMagnitude > 0.001f
                ? definition.Direction.normalized
                : Vector3.forward;
            RuntimeField field = new RuntimeField
            {
                Definition = definition,
                Cell = cell,
                IsProcedural = isProcedural,
                Root = rootObject.transform,
                Direction = direction,
                TurbulenceOffset = CreateTurbulenceOffset(definition, _fields.Count),
            };
            field.Streamlines = CreateParticleLayer(
                rootObject.transform,
                "Air Streamlines",
                _settings.StreamlineParticleBudget,
                definition.Size,
                _settings.StreamlineLength,
                _settings.StreamlineColor,
                direction * definition.Force * _settings.AirflowVisualSpeedMultiplier);
            ApplyFieldParticleSeed(field.Streamlines, definition, 9401);

            Vector3 surfaceSize = new Vector3(definition.Size.x, _settings.SurfaceLayerHeight, definition.Size.z);
            Vector3 surfaceDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (surfaceDirection.sqrMagnitude < 0.001f)
            {
                surfaceDirection = definition.Type == WindFieldType.Downdraft ? Vector3.right : Vector3.forward;
            }
            field.SurfaceSand = CreateParticleLayer(
                rootObject.transform,
                "Surface Sand",
                _settings.SurfaceParticleBudget,
                surfaceSize,
                _settings.SurfaceStreakLength,
                _settings.SurfaceSandColor,
                surfaceDirection.normalized * definition.Force * _settings.SurfaceWindSpeedMultiplier);
            ApplyFieldParticleSeed(field.SurfaceSand, definition, 9413);
            field.SurfaceSand.transform.localPosition = Vector3.down * Mathf.Max(
                0f,
                (definition.Size.y * 0.5f) - (_settings.SurfaceLayerHeight * 0.5f));
            ConfigureVerticalSurfaceFlow(field.SurfaceSand, definition);
            _fields.Add(field);
            return field;
        }

        private void ApplyFieldParticleSeed(
            ParticleSystem system,
            WindFieldDefinition definition,
            int salt)
        {
            int logicalX = Mathf.RoundToInt(definition.LogicalPosition.x);
            int logicalZ = Mathf.RoundToInt(definition.LogicalPosition.y);
            system.useAutoRandomSeed = false;
            system.randomSeed = DuneVectorMath.Hash(logicalX, logicalZ, _combinedWindSeed, salt);
        }

        private void ConfigureVerticalSurfaceFlow(ParticleSystem system, WindFieldDefinition definition)
        {
            if (definition.Type != WindFieldType.Updraft && definition.Type != WindFieldType.Downdraft)
            {
                return;
            }

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Min(definition.Size.x, definition.Size.z) * 0.5f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.MainModule main = system.main;
            float radialSpeed = definition.Force * _settings.SurfaceWindSpeedMultiplier;
            main.startSpeed = definition.Type == WindFieldType.Updraft ? -radialSpeed : radialSpeed;
            SetVelocity(system, Vector3.zero);
        }

        private ParticleSystem CreateParticleLayer(
            Transform parent,
            string layerName,
            int particleBudget,
            Vector3 size,
            float streakLength,
            Color color,
            Vector3 velocity)
        {
            GameObject layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(parent, false);
            ParticleSystem system = layerObject.AddComponent<ParticleSystem>();
            system.useAutoRandomSeed = false;
            system.randomSeed = DuneVectorMath.Hash(
                _combinedWindSeed,
                _particleSeedIndex++,
                _settings.WindSeed,
                9157);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, particleBudget);
            main.startLifetime = new ParticleSystem.MinMaxCurve(_settings.MinimumParticleLifetime, _settings.MaximumParticleLifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(_settings.MinimumParticleSize, _settings.MaximumParticleSize);
            main.startSpeed = 0f;
            main.startColor = color;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.VelocityOverLifetimeModule velocityModule = system.velocityOverLifetime;
            velocityModule.enabled = true;
            velocityModule.space = ParticleSystemSimulationSpace.World;
            velocityModule.x = velocity.x;
            velocityModule.y = velocity.y;
            velocityModule.z = velocity.z;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.VisualTurbulenceStrength;
            noise.frequency = _settings.VisualTurbulenceFrequency;
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule fadeModule = system.colorOverLifetime;
            fadeModule.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0.82f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });
            fadeModule.color = fade;

            ParticleSystemRenderer renderer = layerObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = _settings.ParticleVelocityStretch;
            renderer.lengthScale = streakLength;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play(true);
            return system;
        }

        private Vector3 CreateTurbulenceOffset(WindFieldDefinition definition, int fieldIndex)
        {
            int logicalX = Mathf.RoundToInt(definition.LogicalPosition.x);
            int logicalZ = Mathf.RoundToInt(definition.LogicalPosition.y);
            return new Vector3(
                DuneVectorMath.HashRange(logicalX, logicalZ, _combinedWindSeed, 9101 + fieldIndex, 0f, 256f),
                DuneVectorMath.HashRange(logicalX, logicalZ, _combinedWindSeed, 9113 + fieldIndex, 0f, 256f),
                DuneVectorMath.HashRange(logicalX, logicalZ, _combinedWindSeed, 9127 + fieldIndex, 0f, 256f));
        }

        private void UpdateLod()
        {
            Vector3 viewer = _camera != null ? _camera.transform.position : _player.WorldCenter;
            float fullDistance = Mathf.Max(0f, _settings.FullDetailDistance);
            float cullDistance = Mathf.Max(fullDistance, _settings.CullDistance);
            for (int i = 0; i < _fields.Count; i++)
            {
                RuntimeField field = _fields[i];
                float distance = Vector3.Distance(viewer, field.Center);
                bool visible = distance <= cullDistance;
                if (field.Root.gameObject.activeSelf != visible)
                {
                    field.Root.gameObject.SetActive(visible);
                }
                if (!visible)
                {
                    continue;
                }
                float detail01 = 1f - Mathf.InverseLerp(fullDistance, cullDistance, distance);
                float lod = Mathf.Lerp(_settings.DistantEmissionMultiplier, 1f, detail01);
                SetEmission(field.Streamlines, _settings.StreamlineEmissionRate * lod);
                SetEmission(field.SurfaceSand, _settings.SurfaceEmissionRate * lod);
            }
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        private static void SetVelocity(ParticleSystem system, Vector3 velocity)
        {
            ParticleSystem.VelocityOverLifetimeModule velocityModule = system.velocityOverLifetime;
            velocityModule.x = velocity.x;
            velocityModule.y = velocity.y;
            velocityModule.z = velocity.z;
        }

        private void RepositionFields()
        {
            for (int i = 0; i < _fields.Count; i++)
            {
                RepositionField(_fields[i]);
            }
        }

        private void RepositionField(RuntimeField field)
        {
            Vector2 logical = field.Definition.LogicalPosition;
            Vector3 local = _world.LogicalToLocal(logical.x, 0f, logical.y);
            float terrainHeight = _world.SampleHeightAtLocal(local.x, local.z);
            field.Center = new Vector3(local.x, terrainHeight + field.Definition.HeightAboveTerrain, local.z);
            field.Root.position = field.Center;
        }

        private void HandleWorldShift(Vector3 shift)
        {
            RepositionFields();
            for (int i = 0; i < _fields.Count; i++)
            {
                _fields[i].Streamlines.Clear(true);
                _fields[i].SurfaceSand.Clear(true);
            }
            _playerInteraction?.Clear(true);
        }

        private static Material CreateParticleMaterial(Texture2D particleTexture)
        {
            Shader shader = Shader.Find("DuneVector/HDRP Weather Particle");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning(
                    "Wind field particle shader is unavailable on the active graphics device. Using an HDRP fallback shader.");
                shader = Shader.Find("HDRP/Unlit");
            }
            if (shader == null || !shader.isSupported)
            {
                shader = Shader.Find("HDRP/Lit");
            }
            Material material = new Material(shader) { name = "Wind Field Streamline Material" };
            material.renderQueue = (int)RenderQueue.Transparent;
            if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
            if (material.HasProperty("_BlendMode")) material.SetFloat("_BlendMode", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", Color.white);
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", Color.white);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", particleTexture);
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", particleTexture);
            if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", particleTexture);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ALPHA");
            return material;
        }

        private static Texture2D CreateSoftParticleTexture(float edgeFalloff)
        {
            const int textureSize = 32;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Wind Streamline Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Color[] pixels = new Color[textureSize * textureSize];
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(
                        ((x + 0.5f) / textureSize) - 0.5f,
                        ((y + 0.5f) / textureSize) - 0.5f);
                    float alpha = Mathf.Pow(
                        Mathf.Clamp01(1f - (point.magnitude * 2f)),
                        Mathf.Max(0.1f, edgeFalloff));
                    pixels[(y * textureSize) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            if (_particleMaterial != null)
            {
                Destroy(_particleMaterial);
            }
            if (_particleTexture != null)
            {
                Destroy(_particleTexture);
            }
        }
    }
}
