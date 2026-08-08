using System;
using System.Collections.Generic;
using UnityEngine;

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
        [Min(0f)] public float DroneSpinDegreesPerSecond = 1200f;
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
        [Tooltip("Horizontal speed below which the drone counts as standing still on launch, so the funnel throws it in a random horizontal direction instead of straight up.")]
        [Min(0f)] public float StationaryLaunchSpeedThreshold = 1.5f;

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

        [Header("Funnel Visual")]
        [Tooltip("Resources path of the tornado prefab instantiated for every dust devil. The prefab's authored scale is preserved.")]
        public string PrefabResourcePath = DuneVectorDustDevilSystem.DefaultPrefabResourcePath;

        [Header("Distance LOD")]
        [Min(0f)] public float CullDistance = 850f;
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
        public const string DefaultPrefabResourcePath = "TornadoPrefab";

        private sealed class RuntimeDustDevil
        {
            public Vector2Int Cell;
            public LogicalPosition LogicalPosition;
            public int Identity;
            public float SpinSign;
            public Transform Root;
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
        private GameObject _funnelPrefab;
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
            _funnelPrefab = LoadFunnelPrefab(settings.PrefabResourcePath);
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
            TickVisuals();
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
                Reposition(devil);
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

        private void TickVisuals()
        {
            Vector3 viewer = _camera != null ? _camera.transform.position : _player.WorldCenter;
            float cullDistance = Mathf.Max(0f, _settings.CullDistance);
            foreach (RuntimeDustDevil devil in _instances.Values)
            {
                bool visible = Vector3.Distance(viewer, devil.Center) <= cullDistance;
                if (devil.Root.gameObject.activeSelf != visible)
                {
                    devil.Root.gameObject.SetActive(visible);
                }
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
            CreateFunnelVisual(rootObject.transform, cell);

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
            _instances.Add(cell, devil);
            Reposition(devil);
        }

        private void CreateFunnelVisual(Transform parent, Vector2Int cell)
        {
            if (_funnelPrefab == null)
            {
                return;
            }

            GameObject visual = Instantiate(_funnelPrefab, parent);
            visual.name = _funnelPrefab.name;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(
                0f,
                DuneVectorMath.HashRange(
                    cell.x,
                    cell.y,
                    _world.WorldSeed,
                    _settings.RandomSeedOffset + 3,
                    0f,
                    360f),
                0f);
            visual.transform.localScale = _funnelPrefab.transform.localScale;
        }

        private static GameObject LoadFunnelPrefab(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                resourcePath = DefaultPrefabResourcePath;
            }

            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"Dust devil funnel prefab '{resourcePath}' was not found in Resources; "
                    + "funnels will remain invisible.");
            }
            return prefab;
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

        private void Reposition(RuntimeDustDevil devil)
        {
            double height = _world.HeightField.SampleHeight(
                devil.LogicalPosition.X,
                devil.LogicalPosition.Z);
            devil.Center = _world.LogicalToLocal(
                devil.LogicalPosition.X,
                height,
                devil.LogicalPosition.Z);
            devil.Root.position = devil.Center;
        }

        private void HandleWorldShift(Vector3 shift)
        {
            foreach (RuntimeDustDevil devil in _instances.Values)
            {
                Reposition(devil);
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
        }
    }
}
