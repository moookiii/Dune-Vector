using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum DuneLandmarkType
    {
        // Explicit values preserve existing seeded contract/location data as the catalog grows.
        DesertRelayStation = 0,
        CrashedCarrier = 1,
        RaiderBeacon = 2,
        AncientSpire = 3,
        SandExcavationSite = 4,
        FallenOrbitalArray = 5,
        DesertMegagate = 6,
        WindHarvesterGraveyard = 7,
        BuriedArcology = 8,
        SandRing = 9,
    }

    public static class DuneLandmarkNames
    {
        public static string GetDisplayName(DuneLandmarkType type)
        {
            return type switch
            {
                DuneLandmarkType.DesertRelayStation => "RUINS",
                DuneLandmarkType.CrashedCarrier => "DC-10",
                DuneLandmarkType.RaiderBeacon => "RAIDER BEACON",
                DuneLandmarkType.AncientSpire => "ANCIENT SPIRE",
                DuneLandmarkType.SandExcavationSite => "DESERT OBELISK",
                DuneLandmarkType.FallenOrbitalArray => "FALLEN ORBITAL ARRAY",
                DuneLandmarkType.DesertMegagate => "DESERT MEGAGATE",
                DuneLandmarkType.WindHarvesterGraveyard => "WIND HARVESTER GRAVEYARD",
                DuneLandmarkType.BuriedArcology => "DESERT SHOP",
                DuneLandmarkType.SandRing => "SAND RING",
                _ => type.ToString().ToUpperInvariant(),
            };
        }
    }

    public enum DuneLandmarkRarity
    {
        Common,
        Standard,
        Rare,
        RegionDefining,
    }

    public sealed class DuneLandmarkPlacementRecord
    {
        public string PersistentId { get; }
        public Vector2Int Cell { get; }
        public DuneLandmarkType Type { get; }
        public DuneLandmarkRarity Rarity { get; }
        public LogicalPosition LogicalPosition { get; }
        public int VariantSeed { get; }
        public float RotationDegrees { get; }
        public float ExclusionRadius { get; }

        public DuneLandmarkPlacementRecord(string persistentId, Vector2Int cell, DuneLandmarkType type,
            DuneLandmarkRarity rarity, LogicalPosition logicalPosition, int variantSeed,
            float rotationDegrees, float exclusionRadius)
        {
            PersistentId = persistentId;
            Cell = cell;
            Type = type;
            Rarity = rarity;
            LogicalPosition = logicalPosition;
            VariantSeed = variantSeed;
            RotationDegrees = rotationDegrees;
            ExclusionRadius = exclusionRadius;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorLandmarkInstance : MonoBehaviour
    {
        public DuneLandmarkType Type { get; private set; }
        public DuneLandmarkRarity Rarity { get; private set; }
        public LogicalPosition LogicalPosition { get; private set; }
        public Transform ContractSocket { get; private set; }
        public Transform DeliverySocket { get; private set; }
        public Transform EncounterSocket { get; private set; }
        public Transform LootSocket { get; private set; }
        public Transform FlightPathSocket { get; private set; }
        public bool IsPinnedToContract { get; private set; }
        public DuneLandmarkPlacementRecord PlacementRecord { get; private set; }

        public void AssignPlacementRecord(DuneLandmarkPlacementRecord placementRecord)
        {
            PlacementRecord = placementRecord;
        }

        public void SetContractPinned(bool pinned)
        {
            IsPinnedToContract = pinned;
        }

        public void Initialize(
            DuneLandmarkType type,
            DuneLandmarkRarity rarity,
            LogicalPosition logicalPosition,
            bool pinned,
            LandmarkSystemTuning settings)
        {
            Type = type;
            Rarity = rarity;
            LogicalPosition = logicalPosition;
            IsPinnedToContract = pinned;
            Vector3 contractOffset;
            switch (type)
            {
                case DuneLandmarkType.DesertRelayStation:
                    contractOffset = settings.RelayContractSocketOffset;
                    break;
                case DuneLandmarkType.CrashedCarrier:
                    contractOffset = settings.CarrierContractSocketOffset;
                    break;
                case DuneLandmarkType.RaiderBeacon:
                    contractOffset = new Vector3(13f * settings.BeaconScale, settings.ContractSocketHeight, 0f);
                    break;
                case DuneLandmarkType.AncientSpire:
                    contractOffset = new Vector3(24f * settings.SpireScale, settings.ContractSocketHeight + 3f, 0f);
                    break;
                case DuneLandmarkType.FallenOrbitalArray:
                    contractOffset = settings.OrbitalContractSocketOffset;
                    break;
                case DuneLandmarkType.DesertMegagate:
                    contractOffset = settings.MegagateContractSocketOffset;
                    break;
                case DuneLandmarkType.WindHarvesterGraveyard:
                    contractOffset = settings.HarvesterContractSocketOffset;
                    break;
                case DuneLandmarkType.BuriedArcology:
                    contractOffset = settings.ArcologyContractSocketOffset;
                    break;
                case DuneLandmarkType.SandRing:
                    contractOffset = settings.SandRingContractSocketOffset;
                    break;
                default:
                    contractOffset = settings.ExcavationContractSocketOffset;
                    break;
            }
            Vector3 pickupDirection = Vector3.ProjectOnPlane(contractOffset, Vector3.up).normalized;
            contractOffset += pickupDirection * settings.PickupRingLandmarkClearance;
            ContractSocket = CreateSocket("Contract Socket", contractOffset);
            DeliverySocket = CreateSocket("Airborne Delivery Socket", Vector3.zero);
            EncounterSocket = CreateSocket("Encounter Socket", Vector3.up * settings.EncounterSocketHeight);
            LootSocket = CreateSocket("Loot Socket", new Vector3(4f, 2f, -3f));
            FlightPathSocket = CreateSocket("Flight Path Socket", Vector3.up * settings.FlightSocketHeight);
        }

        public void PositionContractSocket(Vector3 localOffset, float landmarkClearance)
        {
            if (ContractSocket == null)
            {
                return;
            }

            Vector3 pickupDirection = Vector3.ProjectOnPlane(localOffset, Vector3.up).normalized;
            ContractSocket.localPosition = localOffset + pickupDirection * Mathf.Max(0f, landmarkClearance);
        }

        public void PositionDeliverySocketAboveVisuals(float clearance)
        {
            if (DeliverySocket == null)
            {
                return;
            }

            float highestLocalPoint = 0f;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                Vector3 worldTop = renderer.bounds.center + (Vector3.up * renderer.bounds.extents.y);
                highestLocalPoint = Mathf.Max(highestLocalPoint, transform.InverseTransformPoint(worldTop).y);
            }

            DeliverySocket.localPosition = Vector3.up * (highestLocalPoint + Mathf.Max(0f, clearance));
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
        }

        private Transform CreateSocket(string socketName, Vector3 localPosition)
        {
            GameObject socketObject = new GameObject(socketName);
            socketObject.transform.SetParent(transform, false);
            socketObject.transform.localPosition = localPosition;
            return socketObject.transform;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorLandmarkAnimator : MonoBehaviour
    {
        private sealed class SpinBinding
        {
            public Transform Target;
            public Vector3 Axis;
            public float DegreesPerSecond;
        }

        private sealed class BobBinding
        {
            public Transform Target;
            public Vector3 BasePosition;
            public float Amplitude;
            public float Speed;
            public float Phase;
        }

        private sealed class PulseBinding
        {
            public Transform Target;
            public Vector3 BaseScale;
            public float Amount;
            public float Speed;
            public float Phase;
        }

        private readonly List<SpinBinding> _spins = new List<SpinBinding>();
        private readonly List<BobBinding> _bobs = new List<BobBinding>();
        private readonly List<PulseBinding> _pulses = new List<PulseBinding>();
        private float _seedPhase;

        public void Initialize(int seed)
        {
            _seedPhase = Mathf.Repeat(seed * 0.0137f, Mathf.PI * 2f);
        }

        public void RegisterSpin(Transform target, Vector3 axis, float degreesPerSecond)
        {
            if (target == null || Mathf.Approximately(degreesPerSecond, 0f)) return;
            _spins.Add(new SpinBinding { Target = target, Axis = axis.normalized, DegreesPerSecond = degreesPerSecond });
        }

        public void RegisterBob(Transform target, float amplitude, float speed, float phaseOffset = 0f)
        {
            if (target == null || amplitude <= 0f || speed <= 0f) return;
            _bobs.Add(new BobBinding
            {
                Target = target,
                BasePosition = target.localPosition,
                Amplitude = amplitude,
                Speed = speed,
                Phase = _seedPhase + phaseOffset,
            });
        }

        public void RegisterPulse(Transform target, float amount, float speed, float phaseOffset = 0f)
        {
            if (target == null || amount <= 0f || speed <= 0f) return;
            _pulses.Add(new PulseBinding
            {
                Target = target,
                BaseScale = target.localScale,
                Amount = amount,
                Speed = speed,
                Phase = _seedPhase + phaseOffset,
            });
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            float time = Time.time;
            for (int i = 0; i < _spins.Count; i++)
            {
                SpinBinding binding = _spins[i];
                if (binding.Target != null)
                {
                    binding.Target.Rotate(binding.Axis, binding.DegreesPerSecond * deltaTime, Space.Self);
                }
            }
            for (int i = 0; i < _bobs.Count; i++)
            {
                BobBinding binding = _bobs[i];
                if (binding.Target != null)
                {
                    binding.Target.localPosition = binding.BasePosition +
                        (Vector3.up * (Mathf.Sin((time * binding.Speed) + binding.Phase) * binding.Amplitude));
                }
            }
            for (int i = 0; i < _pulses.Count; i++)
            {
                PulseBinding binding = _pulses[i];
                if (binding.Target != null)
                {
                    float scale = 1f + (Mathf.Sin((time * binding.Speed) + binding.Phase) * binding.Amount);
                    binding.Target.localScale = binding.BaseScale * scale;
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorGeneratedLandmarkMesh : MonoBehaviour
    {
        private Mesh _mesh;

        public void Initialize(Mesh mesh)
        {
            _mesh = mesh;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }
        }
    }

    [DefaultExecutionOrder(1040)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorLandmarkDirector : MonoBehaviour
    {
        private static readonly int BaseMapTransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTextureTransformId = Shader.PropertyToID("_MainTex_ST");

        private readonly Dictionary<Vector2Int, DuneVectorLandmarkInstance> _streamed =
            new Dictionary<Vector2Int, DuneVectorLandmarkInstance>();
        private readonly Dictionary<Vector2Int, DuneLandmarkPlacementRecord> _placementRecords =
            new Dictionary<Vector2Int, DuneLandmarkPlacementRecord>();
        private readonly List<DuneVectorLandmarkInstance> _pinned = new List<DuneVectorLandmarkInstance>();
        private readonly List<Vector2Int> _removeBuffer = new List<Vector2Int>();

        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private LandmarkSystemTuning _settings;
        private GeoglyphSystemTuning _geoglyphs;
        private Transform _root;
        private float _refreshTimer;
        private Vector2Int _lastCenter = new Vector2Int(int.MinValue, int.MinValue);

        public IReadOnlyCollection<DuneVectorLandmarkInstance> StreamedLandmarks => _streamed.Values;
        public IReadOnlyList<DuneVectorLandmarkInstance> ContractLandmarks => _pinned;
        public IReadOnlyDictionary<Vector2Int, DuneLandmarkPlacementRecord> PlacementRecords => _placementRecords;

        public bool OverlapsLandmarkFootprint(
            double logicalX,
            double logicalZ,
            float additionalClearance)
        {
            if (_world == null || _settings == null || !_settings.Enabled)
            {
                return false;
            }

            float clearance = Mathf.Max(0f, additionalClearance);
            float maximumLandmarkRadius = Mathf.Max(
                GetExclusionRadius(DuneLandmarkType.DesertRelayStation),
                GetExclusionRadius(DuneLandmarkType.AncientSpire),
                GetExclusionRadius(DuneLandmarkType.SandRing));
            float cellSize = Mathf.Max(1f, _settings.PlacementCellSize);
            int searchRadius = Mathf.Max(1, Mathf.CeilToInt(
                (maximumLandmarkRadius + clearance) / cellSize));
            Vector2Int center = LogicalToCell(new LogicalPosition(logicalX, logicalZ));

            for (int z = -searchRadius; z <= searchRadius; z++)
            {
                for (int x = -searchRadius; x <= searchRadius; x++)
                {
                    DuneLandmarkPlacementRecord record = GetOrCreatePlacementRecord(
                        center + new Vector2Int(x, z));
                    if (record == null)
                    {
                        continue;
                    }

                    double deltaX = record.LogicalPosition.X - logicalX;
                    double deltaZ = record.LogicalPosition.Z - logicalZ;
                    double requiredSeparation = record.ExclusionRadius + clearance;
                    if ((deltaX * deltaX) + (deltaZ * deltaZ) <
                        requiredSeparation * requiredSeparation)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Initialize(
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            LandmarkSystemTuning settings,
            GeoglyphSystemTuning geoglyphs)
        {
            _world = world;
            _materials = materials;
            _settings = settings;
            _geoglyphs = geoglyphs;
            GameObject rootObject = new GameObject("Authored Procedural Landmarks");
            _root = rootObject.transform;
            _root.SetParent(transform, false);
            _world.WorldShifted += HandleWorldShift;
            Refresh(force: true);
        }

        public DuneVectorLandmarkInstance PinNearestWorldLandmark(
            DuneLandmarkType type,
            LogicalPosition desiredPosition,
            ISet<string> excludedPersistentIds = null)
        {
            DuneLandmarkPlacementRecord record = ResolveNearestWorldLandmark(
                type, desiredPosition, excludedPersistentIds);
            return PinWorldLandmark(record);
        }

        public DuneLandmarkPlacementRecord ResolveNearestWorldLandmark(
            DuneLandmarkType type,
            LogicalPosition desiredPosition,
            ISet<string> excludedPersistentIds = null)
        {
            DuneLandmarkPlacementRecord record = FindNearestPlacement(
                desiredPosition, type, excludedPersistentIds, requireType: true);
            if (record == null)
            {
                record = FindNearestPlacement(
                    desiredPosition, type, excludedPersistentIds, requireType: false);
            }
            if (record == null)
            {
                Debug.LogError($"No procedural world landmark could be found near contract stop {desiredPosition}.");
            }
            return record;
        }

        public DuneLandmarkPlacementRecord ResolveNearestWorldLandmark(
            DuneLandmarkType type,
            LogicalPosition desiredPosition,
            LogicalPosition legOrigin,
            float minimumLegDistance,
            float maximumLegDistance,
            ISet<string> excludedPersistentIds = null)
        {
            float minimum = Mathf.Max(0f, minimumLegDistance);
            float maximum = Mathf.Max(minimum, maximumLegDistance);
            DuneLandmarkPlacementRecord record = FindNearestPlacementInDistanceBand(
                desiredPosition,
                legOrigin,
                minimum,
                maximum,
                type,
                excludedPersistentIds,
                requireType: true);
            if (record == null)
            {
                record = FindNearestPlacementInDistanceBand(
                    desiredPosition,
                    legOrigin,
                    minimum,
                    maximum,
                    type,
                    excludedPersistentIds,
                    requireType: false);
            }
            if (record == null)
            {
                record = ResolveNearestWorldLandmark(type, desiredPosition, excludedPersistentIds);
                Debug.LogWarning(
                    $"No procedural world landmark was available in the {minimum:0}-{maximum:0}m contract leg band near {desiredPosition}.",
                    this);
            }
            return record;
        }

        public DuneVectorLandmarkInstance PinWorldLandmark(DuneLandmarkPlacementRecord record)
        {
            if (record == null)
            {
                return null;
            }
            if (!_streamed.TryGetValue(record.Cell, out DuneVectorLandmarkInstance landmark) || landmark == null)
            {
                landmark = BuildLandmark(
                    record.Type,
                    record.Rarity,
                    record.LogicalPosition,
                    record.VariantSeed,
                    true,
                    record.RotationDegrees);
                landmark.AssignPlacementRecord(record);
                _streamed[record.Cell] = landmark;
            }
            landmark.SetContractPinned(true);
            if (!_pinned.Contains(landmark))
            {
                _pinned.Add(landmark);
            }
            return landmark;
        }

        public void ClearContractLandmarks()
        {
            for (int i = 0; i < _pinned.Count; i++)
            {
                if (_pinned[i] != null)
                {
                    _pinned[i].SetContractPinned(false);
                }
            }
            _pinned.Clear();
            Refresh(force: true);
        }

        private void Update()
        {
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = Mathf.Max(0.1f, _settings.RefreshInterval);
                Refresh(force: false);
            }
        }

        private void Refresh(bool force)
        {
            if (_world == null || _settings == null || !_settings.Enabled)
            {
                return;
            }

            Vector2Int center = LogicalToCell(_world.LogicalPlayerPosition);
            if (!force && center == _lastCenter)
            {
                return;
            }
            _lastCenter = center;

            int radius = Mathf.Max(1, _settings.ActiveCellRadius);
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int cell = center + new Vector2Int(x, z);
                    if (!_streamed.ContainsKey(cell))
                    {
                        TryGenerateCell(cell);
                    }
                }
            }

            _removeBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, DuneVectorLandmarkInstance> pair in _streamed)
            {
                if ((pair.Value == null || !pair.Value.IsPinnedToContract) &&
                    Mathf.Max(Mathf.Abs(pair.Key.x - center.x), Mathf.Abs(pair.Key.y - center.y)) > radius + 1)
                {
                    _removeBuffer.Add(pair.Key);
                }
            }
            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                Vector2Int cell = _removeBuffer[i];
                if (_streamed.TryGetValue(cell, out DuneVectorLandmarkInstance landmark) && landmark != null)
                {
                    Destroy(landmark.gameObject);
                }
                _streamed.Remove(cell);
            }
        }

        private void TryGenerateCell(Vector2Int cell)
        {
            DuneLandmarkPlacementRecord record = GetOrCreatePlacementRecord(cell);
            if (record == null)
            {
                _streamed[cell] = null;
                return;
            }

            DuneVectorLandmarkInstance landmark = BuildLandmark(record.Type, record.Rarity, record.LogicalPosition,
                record.VariantSeed, false, record.RotationDegrees);
            landmark.AssignPlacementRecord(record);
            _streamed[cell] = landmark;
        }

        private DuneLandmarkPlacementRecord FindNearestPlacement(
            LogicalPosition desiredPosition,
            DuneLandmarkType requestedType,
            ISet<string> excludedPersistentIds,
            bool requireType)
        {
            Vector2Int center = LogicalToCell(desiredPosition);
            int maximumRadius = Mathf.Max(1, _settings.ContractLandmarkSearchRadius);
            for (int radius = 0; radius <= maximumRadius; radius++)
            {
                DuneLandmarkPlacementRecord nearest = null;
                double nearestDistanceSquared = double.MaxValue;
                for (int z = -radius; z <= radius; z++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (radius > 0 && Mathf.Abs(x) != radius && Mathf.Abs(z) != radius)
                        {
                            continue;
                        }

                        DuneLandmarkPlacementRecord candidate =
                            GetOrCreatePlacementRecord(center + new Vector2Int(x, z));
                        if (candidate == null ||
                            (requireType && candidate.Type != requestedType) ||
                            (excludedPersistentIds != null &&
                             excludedPersistentIds.Contains(candidate.PersistentId)))
                        {
                            continue;
                        }

                        double deltaX = candidate.LogicalPosition.X - desiredPosition.X;
                        double deltaZ = candidate.LogicalPosition.Z - desiredPosition.Z;
                        double distanceSquared = (deltaX * deltaX) + (deltaZ * deltaZ);
                        if (distanceSquared < nearestDistanceSquared)
                        {
                            nearest = candidate;
                            nearestDistanceSquared = distanceSquared;
                        }
                    }
                }
                if (nearest != null)
                {
                    return nearest;
                }
            }
            return null;
        }

        private DuneLandmarkPlacementRecord FindNearestPlacementInDistanceBand(
            LogicalPosition desiredPosition,
            LogicalPosition legOrigin,
            float minimumLegDistance,
            float maximumLegDistance,
            DuneLandmarkType requestedType,
            ISet<string> excludedPersistentIds,
            bool requireType)
        {
            Vector2Int center = LogicalToCell(desiredPosition);
            int maximumRadius = Mathf.Max(1, _settings.ContractLandmarkSearchRadius);
            double minimumDistanceSquared = minimumLegDistance * minimumLegDistance;
            double maximumDistanceSquared = maximumLegDistance * maximumLegDistance;
            DuneLandmarkPlacementRecord nearest = null;
            double nearestDesiredDistanceSquared = double.MaxValue;
            for (int z = -maximumRadius; z <= maximumRadius; z++)
            {
                for (int x = -maximumRadius; x <= maximumRadius; x++)
                {
                    DuneLandmarkPlacementRecord candidate =
                        GetOrCreatePlacementRecord(center + new Vector2Int(x, z));
                    if (candidate == null ||
                        (requireType && candidate.Type != requestedType) ||
                        (excludedPersistentIds != null &&
                         excludedPersistentIds.Contains(candidate.PersistentId)))
                    {
                        continue;
                    }

                    double originDeltaX = candidate.LogicalPosition.X - legOrigin.X;
                    double originDeltaZ = candidate.LogicalPosition.Z - legOrigin.Z;
                    double originDistanceSquared =
                        (originDeltaX * originDeltaX) + (originDeltaZ * originDeltaZ);
                    if (originDistanceSquared < minimumDistanceSquared ||
                        originDistanceSquared > maximumDistanceSquared)
                    {
                        continue;
                    }

                    double desiredDeltaX = candidate.LogicalPosition.X - desiredPosition.X;
                    double desiredDeltaZ = candidate.LogicalPosition.Z - desiredPosition.Z;
                    double desiredDistanceSquared =
                        (desiredDeltaX * desiredDeltaX) + (desiredDeltaZ * desiredDeltaZ);
                    if (desiredDistanceSquared < nearestDesiredDistanceSquared)
                    {
                        nearest = candidate;
                        nearestDesiredDistanceSquared = desiredDistanceSquared;
                    }
                }
            }
            return nearest;
        }

        private DuneLandmarkPlacementRecord GetOrCreatePlacementRecord(Vector2Int cell)
        {
            if (_placementRecords.TryGetValue(cell, out DuneLandmarkPlacementRecord cached))
            {
                return cached;
            }

            DuneLandmarkPlacementRecord candidate = CreatePlacementCandidate(cell);
            if (candidate != null && IsBlockedByPreferredNeighbor(candidate))
            {
                candidate = null;
            }
            _placementRecords[cell] = candidate;
            return candidate;
        }

        private DuneLandmarkPlacementRecord CreatePlacementCandidate(Vector2Int cell)
        {
            float roll = DuneVectorMath.Hash01(cell.x, cell.y, _world.WorldSeed, 7103);
            float megaThreshold = _settings.RegionDefiningCellChance;
            float rareThreshold = megaThreshold + _settings.RareCellChance;
            float standardThreshold = rareThreshold + _settings.StandardCellChance;
            float commonThreshold = standardThreshold + _settings.CommonCellChance;
            if (roll > commonThreshold)
            {
                return null;
            }

            DuneLandmarkRarity rarity = roll <= megaThreshold
                ? DuneLandmarkRarity.RegionDefining
                : roll <= rareThreshold
                    ? DuneLandmarkRarity.Rare
                : roll <= standardThreshold
                    ? DuneLandmarkRarity.Standard
                    : DuneLandmarkRarity.Common;
            DuneLandmarkType type = ChooseType(cell, rarity);
            LogicalPosition logical = ChoosePlacement(cell);
            double hubDx = logical.X - DesertWorldStreamer.StartingLogicalPosition.x;
            double hubDz = logical.Z - DesertWorldStreamer.StartingLogicalPosition.y;
            if ((hubDx * hubDx) + (hubDz * hubDz) < _settings.HubExclusionRadius * _settings.HubExclusionRadius)
            {
                return null;
            }
            float slope = Vector3.Angle(_world.HeightField.SampleNormal(logical.X, logical.Z), Vector3.up);
            if (slope > _settings.MaximumPlacementSlope)
            {
                logical = new LogicalPosition(
                    ((cell.x + 0.5) * _settings.PlacementCellSize),
                    ((cell.y + 0.5) * _settings.PlacementCellSize));
                slope = Vector3.Angle(_world.HeightField.SampleNormal(logical.X, logical.Z), Vector3.up);
            }
            if (slope > _settings.MaximumPlacementSlope)
            {
                return null;
            }

            float exclusionRadius = GetExclusionRadius(type);
            if (_geoglyphs != null && _geoglyphs.OverlapsArtworkFootprint(
                    logical.X,
                    logical.Z,
                    exclusionRadius))
            {
                return null;
            }

            int variantSeed = HashCell(cell);
            float rotation = Mathf.Repeat(variantSeed * 0.137f, 360f);
            return new DuneLandmarkPlacementRecord(
                $"DV-LM-{_world.WorldSeed:X8}-{cell.x}-{cell.y}", cell, type, rarity, logical,
                variantSeed, rotation, exclusionRadius);
        }

        private bool IsBlockedByPreferredNeighbor(DuneLandmarkPlacementRecord candidate)
        {
            float maximumRadius = Mathf.Max(
                GetExclusionRadius(DuneLandmarkType.DesertRelayStation),
                GetExclusionRadius(DuneLandmarkType.AncientSpire),
                GetExclusionRadius(DuneLandmarkType.SandRing));
            int searchRadius = Mathf.Max(1, Mathf.CeilToInt(
                (candidate.ExclusionRadius + maximumRadius) / Mathf.Max(1f, _settings.PlacementCellSize)));
            for (int z = -searchRadius; z <= searchRadius; z++)
            {
                for (int x = -searchRadius; x <= searchRadius; x++)
                {
                    if (x == 0 && z == 0)
                    {
                        continue;
                    }
                    DuneLandmarkPlacementRecord neighbor = CreatePlacementCandidate(
                        candidate.Cell + new Vector2Int(x, z));
                    if (neighbor == null)
                    {
                        continue;
                    }
                    double dx = neighbor.LogicalPosition.X - candidate.LogicalPosition.X;
                    double dz = neighbor.LogicalPosition.Z - candidate.LogicalPosition.Z;
                    double separation = candidate.ExclusionRadius + neighbor.ExclusionRadius;
                    if ((dx * dx) + (dz * dz) < separation * separation && IsPreferred(neighbor, candidate))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsPreferred(DuneLandmarkPlacementRecord left, DuneLandmarkPlacementRecord right)
        {
            if (left.Rarity != right.Rarity)
            {
                return left.Rarity > right.Rarity;
            }
            uint leftRank = unchecked((uint)left.VariantSeed);
            uint rightRank = unchecked((uint)right.VariantSeed);
            if (leftRank != rightRank)
            {
                return leftRank < rightRank;
            }
            return left.Cell.x < right.Cell.x || (left.Cell.x == right.Cell.x && left.Cell.y < right.Cell.y);
        }

        private DuneLandmarkType ChooseType(Vector2Int cell, DuneLandmarkRarity rarity)
        {
            if (rarity == DuneLandmarkRarity.RegionDefining)
            {
                return ChooseFromPool(cell, _settings.RegionDefiningLandmarkTypes, 7107,
                    DuneLandmarkType.SandRing);
            }
            if (rarity == DuneLandmarkRarity.Rare)
            {
                return ChooseFromPool(cell, _settings.RareLandmarkTypes, 7108,
                    DuneLandmarkType.AncientSpire);
            }
            float typeRoll = DuneVectorMath.Hash01(cell.x, cell.y, _world.WorldSeed, 7109);
            float carrierChance = Mathf.Clamp01(_settings.CrashedCarrierSelectionChance);
            if (typeRoll < carrierChance)
            {
                return DuneLandmarkType.CrashedCarrier;
            }

            float nonCarrierRoll = carrierChance < 1f
                ? (typeRoll - carrierChance) / (1f - carrierChance)
                : 0f;
            int choice = Mathf.Clamp(Mathf.FloorToInt(nonCarrierRoll * 3f), 0, 2);
            switch (choice)
            {
                case 0: return DuneLandmarkType.DesertRelayStation;
                case 1: return DuneLandmarkType.RaiderBeacon;
                default: return DuneLandmarkType.SandExcavationSite;
            }
        }

        private DuneLandmarkType ChooseFromPool(Vector2Int cell, DuneLandmarkType[] pool, int salt,
            DuneLandmarkType fallback)
        {
            if (pool == null || pool.Length == 0)
            {
                return fallback;
            }
            int choice = Mathf.FloorToInt(
                DuneVectorMath.Hash01(cell.x, cell.y, _world.WorldSeed, salt) * pool.Length);
            return pool[Mathf.Clamp(choice, 0, pool.Length - 1)];
        }

        private LogicalPosition ChoosePlacement(Vector2Int cell)
        {
            float inset = _settings.PlacementCellSize * 0.2f;
            float x = (cell.x * _settings.PlacementCellSize) + DuneVectorMath.HashRange(
                cell.x, cell.y, _world.WorldSeed, 7111, inset, _settings.PlacementCellSize - inset);
            float z = (cell.y * _settings.PlacementCellSize) + DuneVectorMath.HashRange(
                cell.x, cell.y, _world.WorldSeed, 7113, inset, _settings.PlacementCellSize - inset);
            return new LogicalPosition(x, z);
        }

        private DuneVectorLandmarkInstance BuildLandmark(
            DuneLandmarkType type,
            DuneLandmarkRarity rarity,
            LogicalPosition logical,
            int variantSeed,
            bool pinned,
            float? authoredRotation = null)
        {
            GameObject landmarkObject = new GameObject(
                $"{DuneLandmarkNames.GetDisplayName(type)} {(pinned ? "Contract" : rarity.ToString())}");
            landmarkObject.transform.SetParent(_root, false);
            double height = _world.HeightField.SampleHeight(logical.X, logical.Z);
            landmarkObject.transform.position = _world.LogicalToLocal(logical.X, height, logical.Z);
            landmarkObject.transform.rotation = Quaternion.Euler(
                0f, authoredRotation ?? Mathf.Repeat(variantSeed * 0.137f, 360f), 0f);

            DuneVectorLandmarkInstance instance = landmarkObject.AddComponent<DuneVectorLandmarkInstance>();
            instance.Initialize(type, rarity, logical, pinned, _settings);
            DuneVectorLandmarkAnimator animator = landmarkObject.AddComponent<DuneVectorLandmarkAnimator>();
            animator.Initialize(variantSeed);
            switch (type)
            {
                case DuneLandmarkType.DesertRelayStation:
                    BuildRelay(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.CrashedCarrier:
                    BuildCarrier(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.RaiderBeacon:
                    BuildBeacon(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.AncientSpire:
                    BuildSpire(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.SandExcavationSite:
                    BuildExcavation(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.FallenOrbitalArray:
                    BuildFallenOrbitalArray(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.DesertMegagate:
                    BuildDesertMegagate(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.WindHarvesterGraveyard:
                    BuildWindHarvesterGraveyard(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.BuriedArcology:
                    BuildBuriedArcology(landmarkObject.transform, variantSeed, animator);
                    break;
                case DuneLandmarkType.SandRing:
                    BuildSandRing(landmarkObject.transform, variantSeed, animator);
                    break;
            }
            instance.PositionDeliverySocketAboveVisuals(_settings.DeliveryRingClearance);
            Bounds? photographyBounds = null;
            if (type == DuneLandmarkType.DesertMegagate)
            {
                photographyBounds = CreatePhotographyBounds(
                    _settings.MegagatePhotographyBoundsCenter,
                    _settings.MegagatePhotographyBoundsSize);
            }
            else if (type == DuneLandmarkType.SandRing)
            {
                photographyBounds = CreatePhotographyBounds(
                    _settings.SandRingPhotographyBoundsCenter,
                    _settings.SandRingPhotographyBoundsSize);
            }
            DuneVectorPhotographableMarker.Register(
                landmarkObject,
                DuneVectorCompendiumSubjectIds.ForLandmark(type),
                PhotographableSubjectCategory.Landmark,
                photographyBounds);
            return instance;
        }

        private static Bounds? CreatePhotographyBounds(Vector3 center, Vector3 size)
        {
            return size.x > 0f && size.y > 0f && size.z > 0f
                ? new Bounds(center, size)
                : null;
        }

        private DuneLandmarkRarity GetRarity(DuneLandmarkType type)
        {
            switch (type)
            {
                case DuneLandmarkType.WindHarvesterGraveyard:
                case DuneLandmarkType.BuriedArcology:
                case DuneLandmarkType.SandRing:
                    return DuneLandmarkRarity.RegionDefining;
                case DuneLandmarkType.AncientSpire:
                case DuneLandmarkType.FallenOrbitalArray:
                case DuneLandmarkType.DesertMegagate:
                    return DuneLandmarkRarity.Rare;
                default:
                    return DuneLandmarkRarity.Standard;
            }
        }

        private float GetExclusionRadius(DuneLandmarkType type)
        {
            DuneLandmarkRarity rarity = GetRarity(type);
            return rarity == DuneLandmarkRarity.RegionDefining
                ? PositiveOrFallback(_settings.RegionDefiningExclusionRadius, _settings.RareMinimumSpacing * 0.5f)
                : rarity == DuneLandmarkRarity.Rare
                    ? PositiveOrFallback(_settings.LargeLandmarkExclusionRadius, _settings.RareMinimumSpacing * 0.5f)
                    : PositiveOrFallback(_settings.SmallMediumLandmarkExclusionRadius,
                        _settings.StandardMinimumSpacing * 0.5f);
        }

        private static float PositiveOrFallback(float value, float fallback)
        {
            return value > 0f ? value : Mathf.Max(0f, fallback);
        }

        private void BuildRelay(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            GameObject relayPrefab = null;
            if (!string.IsNullOrWhiteSpace(_settings.RelayStationResourcePath))
            {
                relayPrefab = Resources.Load<GameObject>(_settings.RelayStationResourcePath);
            }

            if (relayPrefab == null)
            {
                relayPrefab = _settings.RelayStationPrefab;
            }

            if (relayPrefab == null)
            {
                Debug.LogWarning("Ruins landmark prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = relayPrefab.transform.localScale;
            Quaternion prefabRotation = relayPrefab.transform.localRotation;
            GameObject relay = UnityEngine.Object.Instantiate(relayPrefab, root, false);
            relay.name = relayPrefab.name;
            relay.transform.localPosition = Vector3.zero;
            relay.transform.localRotation = prefabRotation;
            relay.transform.localScale = prefabScale;
            GroundPrefabToDunes(
                relay.transform,
                _settings.RelayGroundingSamplesPerAxis,
                _settings.RelayPrefabGroundOffsetDown,
                _settings.RelayGroundingBurialCoverage);
        }

        private void BuildProceduralRelay(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            float scale = _settings.RelayScale;
            Part(PrimitiveType.Cube, "Relay Platform", root, new Vector3(0f, 0.6f, 0f), new Vector3(22f, 1.2f, 16f) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Cube, "Relay Building", root, new Vector3(0f, 3.5f, 0f), new Vector3(11f, 5.8f, 8f) * scale, Quaternion.identity, _materials.Sandstone);
            Part(PrimitiveType.Cube, "Relay Roof Cap", root, new Vector3(0f, 6.65f, 0f) * scale,
                new Vector3(12.5f, 0.55f, 9.5f) * scale, Quaternion.identity, _materials.DroneBody);
            int relayWindowCount = Mathf.Max(2, _settings.RelayWindowCount);
            float relayWindowStart = -((relayWindowCount - 1) * _settings.RelayWindowSpacing * 0.5f);
            for (int i = 0; i < relayWindowCount; i++)
            {
                Part(PrimitiveType.Cube, $"Relay Navigation Window {i + 1}", root,
                    new Vector3(relayWindowStart + (i * _settings.RelayWindowSpacing), 4.25f, -4.08f) * scale,
                    new Vector3(_settings.RelayWindowSize, _settings.RelayWindowSize * 0.72f, 0.12f) * scale,
                    Quaternion.identity, _materials.DroneAccent, false);
            }
            Part(PrimitiveType.Cylinder, "Long Range Antenna", root, new Vector3(0f, _settings.RelayAntennaHeight * 0.5f, 0f), new Vector3(0.62f, _settings.RelayAntennaHeight * 0.5f, 0.62f) * scale, Quaternion.identity, _materials.DroneDark);
            Transform beacon = Part(PrimitiveType.Sphere, "Antenna Beacon", root, new Vector3(0f, _settings.RelayAntennaHeight, 0f) * scale, Vector3.one * 1.3f * scale, Quaternion.identity, _materials.DroneAccent);
            Transform dishPivot = new GameObject("Dish Tracking Pivot").transform;
            dishPivot.SetParent(root, false);
            dishPivot.localPosition = new Vector3(0f, _settings.RelayAntennaHeight * 0.62f, 0f) * scale;
            Part(PrimitiveType.Sphere, "Dish", dishPivot, Vector3.zero, new Vector3(6f, 0.8f, 6f) * scale, Quaternion.Euler(18f, 0f, 0f), _materials.DroneBody);
            Transform dishRim = new GameObject("Dish Illuminated Rim").transform;
            dishRim.SetParent(dishPivot, false);
            dishRim.localRotation = Quaternion.Euler(18f, 0f, 0f);
            SegmentedRing(dishRim, _settings.RelayDishRimSegments, 3f * scale, 0.18f * scale, _materials.DroneAccent, "Dish Rim");
            animator.RegisterSpin(dishPivot, Vector3.up, _settings.DishRotationSpeed);
            animator.RegisterPulse(beacon, _settings.BeaconPulseAmount, _settings.BeaconPulseSpeed);
            int braceCount = Mathf.Max(3, _settings.RelayMastBraceCount);
            for (int i = 0; i < braceCount; i++)
            {
                float angle = (360f / braceCount) * i;
                Vector3 anchor = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (_settings.RelayMastBraceRadius * scale);
                BeamBetween(root, $"Antenna Brace {i + 1}", anchor, Vector3.up * (_settings.RelayMastBraceHeight * scale),
                    _settings.RelayMastBraceThickness * scale, _materials.DroneBody, false);
            }
            Part(PrimitiveType.Cylinder, "Fuel Tank A", root, new Vector3(-7f, 2f, 5f) * scale, new Vector3(1.6f, 2.4f, 1.6f) * scale, Quaternion.Euler(0f, 0f, 90f), _materials.DroneDark);
            Part(PrimitiveType.Cylinder, "Fuel Tank B", root, new Vector3(7f, 2f, 5f) * scale, new Vector3(1.6f, 2.4f, 1.6f) * scale, Quaternion.Euler(0f, 0f, 90f), _materials.DroneDark);
            if ((seed & 1) == 0)
            {
                Part(PrimitiveType.Cylinder, "Secondary Antenna", root, new Vector3(6f, 11f, -3f) * scale, new Vector3(0.35f, 11f, 0.35f) * scale, Quaternion.identity, _materials.DroneDark);
            }
            int variant = PositiveVariant(seed);
            if (variant == 1 || variant == 3)
            {
                Part(PrimitiveType.Cube, "Solar Wing Left", root, new Vector3(-12f, 2.4f, -2f) * scale,
                    new Vector3(10f, 0.25f, 6f) * scale, Quaternion.Euler(0f, 8f, -9f), _materials.DroneAccent, false);
                Part(PrimitiveType.Cube, "Solar Wing Right", root, new Vector3(12f, 2.4f, -2f) * scale,
                    new Vector3(10f, 0.25f, 6f) * scale, Quaternion.Euler(0f, -8f, 9f), _materials.DroneAccent, false);
            }
            if (variant >= 2)
            {
                for (int i = 0; i < variant; i++)
                {
                    float angle = (360f / variant) * i;
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Part(PrimitiveType.Cylinder, $"Relay Marker {i + 1}", root,
                        (direction * 15f + Vector3.up * 2.5f) * scale,
                        new Vector3(0.22f, 2.5f, 0.22f) * scale,
                        Quaternion.identity, _materials.DroneAccent, false);
                }
            }
        }

        private void BuildCarrier(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            root.localRotation *= Quaternion.Euler(8f, 0f, -13f);
            GameObject carrierPrefab = _settings.CrashedCarrierPrefab;
            if (carrierPrefab == null && !string.IsNullOrWhiteSpace(_settings.CrashedCarrierResourcePath))
            {
                carrierPrefab = Resources.Load<GameObject>(_settings.CrashedCarrierResourcePath);
            }

            if (carrierPrefab == null)
            {
                Debug.LogWarning("Crashed carrier landmark prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = carrierPrefab.transform.localScale;
            GameObject carrier = UnityEngine.Object.Instantiate(carrierPrefab, root, false);
            carrier.name = carrierPrefab.name;
            carrier.transform.localPosition = Vector3.down * _settings.CrashedCarrierGroundSink;
            carrier.transform.localRotation = Quaternion.identity;
            carrier.transform.localScale = prefabScale;
            if (carrier.GetComponentInChildren<Collider>(true) == null)
            {
                BuildCarrierColliders(carrier.transform);
            }
        }

        private void BuildCarrierColliders(Transform carrier)
        {
            LandmarkBoxColliderTuning[] colliderBoxes = _settings.CrashedCarrierColliderBoxes;
            if (colliderBoxes == null)
            {
                return;
            }

            for (int i = 0; i < colliderBoxes.Length; i++)
            {
                LandmarkBoxColliderTuning boxTuning = colliderBoxes[i];
                if (boxTuning == null || boxTuning.Size.x <= 0f || boxTuning.Size.y <= 0f || boxTuning.Size.z <= 0f)
                {
                    continue;
                }

                GameObject colliderObject = new GameObject($"DC-10 Collider {i + 1}");
                colliderObject.layer = carrier.gameObject.layer;
                colliderObject.transform.SetParent(carrier, false);
                colliderObject.transform.localPosition = boxTuning.Center;
                colliderObject.transform.localRotation = Quaternion.Euler(boxTuning.EulerAngles);
                BoxCollider boxCollider = colliderObject.AddComponent<BoxCollider>();
                boxCollider.size = boxTuning.Size;
            }
        }

        private void BuildBeacon(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            float scale = _settings.BeaconScale;
            float height = _settings.BeaconHeight;
            int foundationArmCount = Mathf.Max(3, _settings.BeaconFoundationArmCount);
            for (int i = 0; i < foundationArmCount; i++)
            {
                float angle = (360f / foundationArmCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Beacon Foundation Arm {i + 1}", root,
                    (direction * 6f + Vector3.up * 0.65f) * scale,
                    new Vector3(2.2f, 1.3f, 12f) * scale,
                    Quaternion.Euler(0f, angle, 0f), _materials.EnemyBody);
            }
            Part(PrimitiveType.Cylinder, "Beacon Tower", root, new Vector3(0f, height * 0.5f, 0f) * scale, new Vector3(2.2f, height * 0.5f, 2.2f) * scale, Quaternion.identity, _materials.EnemyBody);
            int towerFinCount = Mathf.Max(3, _settings.BeaconTowerFinCount);
            for (int i = 0; i < towerFinCount; i++)
            {
                float angle = (360f / towerFinCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Beacon Tower Fin {i + 1}", root,
                    (direction * 2.25f + Vector3.up * (height * 0.43f)) * scale,
                    new Vector3(0.32f, height * 0.56f, 3.4f) * scale,
                    Quaternion.Euler(0f, angle, 0f), _materials.DroneDark, false);
            }
            Transform beaconEnergy = Part(PrimitiveType.Sphere, "Beacon Energy", root, new Vector3(0f, height, 0f) * scale, Vector3.one * 4.8f * scale, Quaternion.identity, _materials.EnemyCore);
            Transform orbit = new GameObject("Raider Signal Orbit").transform;
            orbit.SetParent(root, false);
            Transform signalRing = new GameObject("Raider Signal Ring").transform;
            signalRing.SetParent(orbit, false);
            signalRing.localPosition = Vector3.up * (height * 0.72f * scale);
            SegmentedRing(
                signalRing,
                _settings.BeaconSignalRingSegments,
                _settings.BeaconSignalRingRadius * scale,
                _settings.BeaconSignalRingThickness * scale,
                _materials.EnemyCore,
                "Signal Ring");
            for (int i = 0; i < 3; i++)
            {
                float angle = (i * 120f) + Mathf.Repeat(seed * 0.1f, 50f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Generator {i + 1}", root, (direction * 10f + Vector3.up * 1.6f) * scale, new Vector3(4.5f, 3.2f, 3.4f) * scale, Quaternion.Euler(0f, angle, 0f), _materials.DroneDark);
                Part(PrimitiveType.Cylinder, $"Floating Antenna {i + 1}", orbit, (direction * 7f + Vector3.up * (height * 0.62f)) * scale, new Vector3(0.5f, 4f, 0.5f) * scale, Quaternion.Euler(90f, angle, 0f), _materials.EnemyCore, false);
            }
            animator.RegisterSpin(orbit, Vector3.up, _settings.BeaconOrbitSpeed);
            animator.RegisterPulse(beaconEnergy, _settings.BeaconPulseAmount, _settings.BeaconPulseSpeed);
            int variant = PositiveVariant(seed);
            for (int i = 0; i < variant + 2; i++)
            {
                float angle = (360f / (variant + 2f)) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Beacon Crown Blade {i + 1}", orbit,
                    (direction * 5.5f + Vector3.up * height) * scale,
                    new Vector3(0.35f, 0.35f, 5f) * scale,
                    Quaternion.Euler(0f, angle, 0f), _materials.EnemyBody, false);
            }
        }

        private void BuildSpire(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            float scale = _settings.SpireScale;
            float height = _settings.SpireHeight;
            Transform baseRing = new GameObject("Ancient Spire Ground Circuit").transform;
            baseRing.SetParent(root, false);
            baseRing.localPosition = Vector3.up * (0.18f * scale);
            SegmentedRing(
                baseRing,
                _settings.SpireBaseRingSegments,
                _settings.SpireBaseRingRadius * scale,
                _settings.SpireBaseRingThickness * scale,
                _materials.AncientSpireAccent,
                "Ground Circuit");
            int layerCount = Mathf.Max(5, _settings.SpireLayerCount);
            float layerHeight = height / layerCount;
            for (int i = 0; i < layerCount; i++)
            {
                float layer01 = i / (float)layerCount;
                float width = Mathf.Lerp(18f, 3f, layer01);
                Part(PrimitiveType.Cube, $"Spire Layer {i + 1}", root,
                    new Vector3(0f, (i + 0.5f) * layerHeight, 0f) * scale,
                    new Vector3(width, layerHeight * 0.92f, width) * scale,
                    Quaternion.Euler(0f, i * 13f, 0f), _materials.AncientSpireStone);
                if (i < layerCount - 1)
                {
                    float nextWidth = Mathf.Lerp(18f, 3f, (i + 1f) / layerCount);
                    Part(PrimitiveType.Cube, $"Spire Energy Seam {i + 1}", root,
                        new Vector3(0f, (i + 1f) * layerHeight, 0f) * scale,
                        new Vector3(nextWidth * 0.86f, _settings.SpireSeamHeight, nextWidth * 0.86f) * scale,
                        Quaternion.Euler(0f, (i + 1f) * 13f, 0f), _materials.AncientSpireAccent, false);
                }
            }
            Transform relic = Part(PrimitiveType.Sphere, "Floating Spire Relic", root, new Vector3(0f, height + 12f, 0f) * scale, Vector3.one * 5f * scale, Quaternion.identity, _materials.AncientSpireAccent, false);
            animator.RegisterSpin(relic, Vector3.up, _settings.SpireRelicRotationSpeed);
            animator.RegisterBob(relic, _settings.SpireRelicFloatAmplitude * scale, _settings.SpireRelicFloatSpeed);
            animator.RegisterPulse(relic, _settings.BeaconPulseAmount, _settings.BeaconPulseSpeed);
            Transform shardOrbit = new GameObject("Spire Relic Shards").transform;
            shardOrbit.SetParent(root, false);
            int shardCount = Mathf.Max(2, _settings.SpireShardCount);
            for (int i = 0; i < shardCount; i++)
            {
                float angle = (360f / shardCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Relic Shard {i + 1}", shardOrbit,
                    (direction * (8f + ((i % 2) * 2f)) + Vector3.up * (height + 12f + ((i % 3) - 1f) * 2f)) * scale,
                    new Vector3(0.8f, 3.8f, 1.1f) * scale,
                    Quaternion.Euler(i * 17f, angle, i * 31f), _materials.AncientSpireDark, false);
            }
            animator.RegisterSpin(shardOrbit, Vector3.up, -_settings.SpireRelicRotationSpeed);
            int monolithCount = Mathf.Max(3, _settings.SpireMonolithCount);
            for (int i = 0; i < monolithCount; i++)
            {
                float angle = (360f / monolithCount) * i;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, height * 0.58f, 15f);
                Part(PrimitiveType.Cylinder, $"Flight Monolith {i + 1}", root, offset * scale, new Vector3(1.2f, 8f, 1.2f) * scale, Quaternion.identity, _materials.AncientSpireDark);
            }
            int variant = PositiveVariant(seed);
            for (int i = 0; i < variant; i++)
            {
                float angle = (360f / Mathf.Max(1, variant)) * i + 45f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Buried Spire Fin {i + 1}", root,
                    (direction * (16f + (i * 3f)) + Vector3.up * 2f) * scale,
                    new Vector3(2f, 9f, 7f) * scale,
                    Quaternion.Euler(0f, angle, 18f), _materials.AncientSpireStone);
            }
            ApplySpireConcreteTiling(root);
        }

        private void ApplySpireConcreteTiling(Transform root)
        {
            float tileWorldSize = Mathf.Max(0.01f, _settings.SpireConcreteTileWorldSize);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material material = renderer.sharedMaterial;
                if (material != _materials.AncientSpireStone &&
                    material != _materials.AncientSpireAccent &&
                    material != _materials.AncientSpireDark)
                {
                    continue;
                }

                Vector3 localSize = renderer.localBounds.size;
                Vector3 lossyScale = renderer.transform.lossyScale;
                Vector3 renderedSize = new Vector3(
                    localSize.x * Mathf.Abs(lossyScale.x),
                    localSize.y * Mathf.Abs(lossyScale.y),
                    localSize.z * Mathf.Abs(lossyScale.z));
                float horizontalSpan = Mathf.Max(renderedSize.x, renderedSize.z);
                float horizontalRepeats = Mathf.Max(1f, horizontalSpan / tileWorldSize);
                float verticalRepeats = Mathf.Max(1f, renderedSize.y / tileWorldSize);
                Vector4 textureTransform = new Vector4(horizontalRepeats, verticalRepeats, 0f, 0f);
                renderer.GetPropertyBlock(properties);
                properties.SetVector(BaseMapTransformId, textureTransform);
                properties.SetVector(MainTextureTransformId, textureTransform);
                renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void BuildExcavation(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            GameObject excavationPrefab = _settings.ExcavationPrefab;
            if (excavationPrefab == null && !string.IsNullOrWhiteSpace(_settings.ExcavationResourcePath))
            {
                excavationPrefab = Resources.Load<GameObject>(_settings.ExcavationResourcePath);
            }

            if (excavationPrefab == null)
            {
                Debug.LogWarning("Excavation replacement prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = excavationPrefab.transform.localScale;
            Quaternion prefabRotation = excavationPrefab.transform.localRotation;
            GameObject excavation = UnityEngine.Object.Instantiate(excavationPrefab, root, false);
            excavation.name = excavationPrefab.name;
            excavation.transform.localPosition = Vector3.zero;
            excavation.transform.localRotation = prefabRotation;
            excavation.transform.localScale = prefabScale;
            // Match the foundation's local underside to the dune envelope. On level ground this
            // preserves the existing placement; only uneven placements with an exposed underside
            // receive the additional downward correction needed to keep the base in the sand.
            GroundPrefabToDunes(
                excavation.transform,
                _settings.ExcavationGroundingSamplesPerAxis,
                0f,
                lowerEnvelopeCoverage: 1f);
        }

        private void GroundPrefabToDunes(
            Transform prefab,
            int configuredSamplesPerAxis,
            float groundOffsetDown,
            float lowerEnvelopeCoverage = 0f)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Vector2 minimum = Vector2.zero;
            Vector2 maximum = Vector2.zero;
            float lowestRenderedHeight = float.PositiveInfinity;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Bounds localBounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 rendererCorner = localBounds.center + Vector3.Scale(
                        localBounds.extents,
                        new Vector3(
                            (corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    Vector3 worldCorner = renderer.transform.TransformPoint(rendererCorner);
                    Vector3 excavationCorner = prefab.InverseTransformPoint(worldCorner);
                    lowestRenderedHeight = Mathf.Min(lowestRenderedHeight, worldCorner.y);
                    Vector2 horizontal = new Vector2(excavationCorner.x, excavationCorner.z);
                    if (!hasBounds)
                    {
                        minimum = horizontal;
                        maximum = horizontal;
                        hasBounds = true;
                    }
                    else
                    {
                        minimum = Vector2.Min(minimum, horizontal);
                        maximum = Vector2.Max(maximum, horizontal);
                    }
                }
            }

            if (!hasBounds)
            {
                return;
            }

            int samplesPerAxis = Mathf.Max(2, configuredSamplesPerAxis);
            if (lowerEnvelopeCoverage > 0f)
            {
                float[] lowestMeshHeightBySample = new float[samplesPerAxis * samplesPerAxis];
                Vector3[] lowestMeshPointBySample = new Vector3[lowestMeshHeightBySample.Length];
                for (int i = 0; i < lowestMeshHeightBySample.Length; i++)
                {
                    lowestMeshHeightBySample[i] = float.PositiveInfinity;
                }

                MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
                for (int filterIndex = 0; filterIndex < meshFilters.Length; filterIndex++)
                {
                    MeshFilter meshFilter = meshFilters[filterIndex];
                    Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    if (mesh == null || !mesh.isReadable)
                    {
                        continue;
                    }

                    Vector3[] vertices = mesh.vertices;
                    for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        Vector3 worldVertex = meshFilter.transform.TransformPoint(vertices[vertexIndex]);
                        Vector3 prefabVertex = prefab.InverseTransformPoint(worldVertex);
                        float x01 = Mathf.InverseLerp(minimum.x, maximum.x, prefabVertex.x);
                        float z01 = Mathf.InverseLerp(minimum.y, maximum.y, prefabVertex.z);
                        int sampleX = Mathf.Clamp(Mathf.RoundToInt(x01 * (samplesPerAxis - 1)), 0, samplesPerAxis - 1);
                        int sampleZ = Mathf.Clamp(Mathf.RoundToInt(z01 * (samplesPerAxis - 1)), 0, samplesPerAxis - 1);
                        int sampleIndex = sampleZ * samplesPerAxis + sampleX;
                        if (worldVertex.y < lowestMeshHeightBySample[sampleIndex])
                        {
                            lowestMeshHeightBySample[sampleIndex] = worldVertex.y;
                            lowestMeshPointBySample[sampleIndex] = worldVertex;
                        }
                    }
                }

                List<float> matchedGroundingShifts = new List<float>(lowestMeshHeightBySample.Length);
                for (int sampleIndex = 0; sampleIndex < lowestMeshHeightBySample.Length; sampleIndex++)
                {
                    if (!float.IsFinite(lowestMeshHeightBySample[sampleIndex]))
                    {
                        continue;
                    }

                    Vector3 meshPoint = lowestMeshPointBySample[sampleIndex];
                    float terrainHeight = _world.SampleHeightAtLocal(meshPoint.x, meshPoint.z);
                    matchedGroundingShifts.Add(terrainHeight - lowestMeshHeightBySample[sampleIndex]);
                }

                if (matchedGroundingShifts.Count > 0)
                {
                    matchedGroundingShifts.Sort();
                    float coverage = Mathf.Clamp01(lowerEnvelopeCoverage);
                    int selectedIndex = Mathf.Clamp(
                        Mathf.RoundToInt((1f - coverage) * (matchedGroundingShifts.Count - 1)),
                        0,
                        matchedGroundingShifts.Count - 1);
                    prefab.position += Vector3.up * (
                        matchedGroundingShifts[selectedIndex] - groundOffsetDown);
                    return;
                }
            }

            float lowestTerrainHeight = float.PositiveInfinity;
            for (int z = 0; z < samplesPerAxis; z++)
            {
                float z01 = z / (float)(samplesPerAxis - 1);
                for (int x = 0; x < samplesPerAxis; x++)
                {
                    float x01 = x / (float)(samplesPerAxis - 1);
                    Vector3 worldSample = prefab.TransformPoint(new Vector3(
                        Mathf.Lerp(minimum.x, maximum.x, x01),
                        0f,
                        Mathf.Lerp(minimum.y, maximum.y, z01)));
                    lowestTerrainHeight = Mathf.Min(
                        lowestTerrainHeight,
                        _world.SampleHeightAtLocal(worldSample.x, worldSample.z));
                }
            }

            if (float.IsFinite(lowestTerrainHeight) && float.IsFinite(lowestRenderedHeight))
            {
                prefab.position += Vector3.up * (
                    lowestTerrainHeight - lowestRenderedHeight - groundOffsetDown);
            }
        }

        private void BuildFallenOrbitalArray(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            float radius = Mathf.Max(4f, _settings.OrbitalDishRadius);
            float tilt = SeedRange(seed, 0, 7201, _settings.OrbitalDishTiltMinimum,
                Mathf.Max(_settings.OrbitalDishTiltMinimum, _settings.OrbitalDishTiltMaximum));
            Transform impactFrame = new GameObject("Orbital Array Impact Frame").transform;
            impactFrame.SetParent(root, false);
            impactFrame.localPosition = new Vector3(0f, radius * 0.18f - _settings.OrbitalBurialDepth, 0f);
            impactFrame.localRotation = Quaternion.Euler(tilt, 0f, SeedRange(seed, 1, 7203, -9f, 9f));

            MeshPart("Broken Parabolic Dish", impactFrame, Vector3.zero, Quaternion.identity,
                Vector3.one, CreateParabolicDishMesh(radius, _settings.OrbitalDishSegmentCount,
                    _settings.OrbitalDishMissingSegmentCount, seed),
                _materials.LandmarkSecondary, true);
            HorizontalSegmentedRing(impactFrame, "Orbital Dish Rim", radius, radius * 0.055f,
                _settings.OrbitalDishSegmentCount, _settings.OrbitalDishMissingSegmentCount,
                seed, _materials.LandmarkMetal, true);

            Part(PrimitiveType.Cube, "Orbital Equipment Bus", impactFrame,
                new Vector3(0f, -radius * 0.24f, -radius * 0.18f),
                new Vector3(radius * 0.82f, radius * 0.3f, radius * 0.48f),
                Quaternion.Euler(0f, 0f, 4f), _materials.LandmarkSecondary, true);
            Part(PrimitiveType.Cylinder, "Orbital Gimbal Housing", impactFrame,
                new Vector3(0f, -radius * 0.06f, -radius * 0.05f),
                new Vector3(radius * 0.18f, radius * 0.34f, radius * 0.18f),
                Quaternion.Euler(0f, 0f, 90f), _materials.LandmarkInterior, true);
            for (int brace = -1; brace <= 1; brace += 2)
            {
                BeamBetween(impactFrame, $"Dish Gimbal Brace {brace}",
                    new Vector3(brace * radius * 0.32f, -radius * 0.2f, -radius * 0.1f),
                    new Vector3(brace * radius * 0.13f, radius * 0.08f, 0f),
                    radius * 0.045f, _materials.LandmarkInterior, true);
            }

            float mastHeight = Mathf.Max(1f, _settings.OrbitalMastHeight);
            BeamBetween(impactFrame, "Snapped Dish Feed Mast", Vector3.up * (radius * 0.08f),
                new Vector3(radius * 0.06f, mastHeight, radius * 0.08f), radius * 0.035f,
                _materials.LandmarkInterior, true);
            Part(PrimitiveType.Sphere, "Dish Receiver", impactFrame,
                new Vector3(radius * 0.06f, mastHeight, radius * 0.08f),
                Vector3.one * (radius * 0.1f), Quaternion.identity, _materials.LandmarkAccent, false);

            int frameworkCount = Mathf.Max(3, _settings.OrbitalDishSegmentCount / 6);
            for (int i = 0; i < frameworkCount; i++)
            {
                float angle = (360f / frameworkCount) * i;
                Vector3 rim = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (radius * 0.86f);
                BeamBetween(impactFrame, $"Exposed Dish Truss {i + 1}", Vector3.zero, rim,
                    radius * 0.022f, _materials.LandmarkInterior, false);
            }

            int wingCount = Mathf.Max(0, _settings.OrbitalSolarWingCount);
            for (int i = 0; i < wingCount; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float trail = radius + (i * _settings.OrbitalSolarWingLength * 0.7f);
                Vector3 offset = new Vector3(side * radius * SeedRange(seed, i, 7211, 0.65f, 1.35f), 0f, -trail);
                offset.y = TerrainLocalHeight(root, offset) + SeedRange(seed, i, 7213, -0.8f, 1.5f);
                Quaternion wingRotation = Quaternion.Euler(SeedRange(seed, i, 7215, -12f, 18f),
                    SeedRange(seed, i, 7217, -28f, 28f), SeedRange(seed, i, 7219, -18f, 18f));
                Part(PrimitiveType.Cube, $"Scattered Solar Wing {i + 1}", root, offset,
                    new Vector3(_settings.OrbitalSolarWingLength, radius * 0.03f,
                        _settings.OrbitalSolarWingLength * 0.36f),
                    wingRotation, i % 2 == 0 ? _materials.LandmarkMetal : _materials.LandmarkSecondary, true);
                int panelLines = 4;
                for (int panel = 1; panel < panelLines; panel++)
                {
                    Part(PrimitiveType.Cube, $"Solar Wing {i + 1} Divider {panel}", root,
                        offset + (wingRotation * Vector3.right *
                            Mathf.Lerp(-_settings.OrbitalSolarWingLength * 0.5f,
                                _settings.OrbitalSolarWingLength * 0.5f, panel / (float)panelLines)),
                        new Vector3(radius * 0.025f, radius * 0.035f,
                            _settings.OrbitalSolarWingLength * 0.38f),
                        wingRotation,
                        _materials.LandmarkInterior, false);
                }
            }

            BuildDebrisTrail(root, "Orbital Impact Debris", seed, _settings.OrbitalDebrisCount,
                _settings.OrbitalDebrisSpread, radius * 0.12f, Vector3.back,
                _materials.LandmarkInterior, 7221);
        }

        private void BuildDesertMegagate(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            GameObject megagatePrefab = null;
            if (!string.IsNullOrWhiteSpace(_settings.MegagateResourcePath))
            {
                megagatePrefab = Resources.Load<GameObject>(_settings.MegagateResourcePath);
            }
            if (megagatePrefab == null)
            {
                megagatePrefab = _settings.MegagatePrefab;
            }
            if (megagatePrefab == null)
            {
                Debug.LogWarning("Desert Megagate replacement prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = megagatePrefab.transform.localScale;
            Quaternion prefabRotation = megagatePrefab.transform.localRotation;
            GameObject megagate = UnityEngine.Object.Instantiate(megagatePrefab, root, false);
            megagate.name = megagatePrefab.name;
            megagate.transform.localPosition = Vector3.zero;
            megagate.transform.localRotation = prefabRotation;
            megagate.transform.localScale = prefabScale;
            GroundPrefabToDunes(
                megagate.transform,
                _settings.MegagateGroundingSamplesPerAxis,
                _settings.MegagateBurialDepth);
            megagate.transform.localScale = prefabScale;

            if (_settings.MegagateGenerateMeshColliders)
            {
                AddMissingMeshColliders(megagate);
            }
        }

        private static void AddMissingMeshColliders(GameObject instance)
        {
            MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null ||
                    filter.GetComponent<Collider>() != null)
                {
                    continue;
                }

                MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
            }
        }

        private void BuildWindHarvesterGraveyard(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            GameObject harvesterPrefab = null;
            if (!string.IsNullOrWhiteSpace(_settings.HarvesterResourcePath))
            {
                harvesterPrefab = Resources.Load<GameObject>(_settings.HarvesterResourcePath);
            }
            if (harvesterPrefab == null)
            {
                harvesterPrefab = _settings.HarvesterPrefab;
            }
            if (harvesterPrefab == null)
            {
                Debug.LogWarning("Wind harvester graveyard turbine prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = harvesterPrefab.transform.localScale;
            Quaternion prefabRotation = harvesterPrefab.transform.localRotation;
            int count = Mathf.Max(1, _settings.HarvesterCount);
            float fieldRadius = Mathf.Max(_settings.HarvesterSpacing, _settings.HarvesterFieldRadius);
            bool hasFallen = false;
            for (int i = 0; i < count; i++)
            {
                float normalizedRadius = Mathf.Sqrt((i + 0.5f) / count);
                float distance = Mathf.Min(fieldRadius,
                    Mathf.Max(_settings.HarvesterSpacing * 0.5f, normalizedRadius * fieldRadius));
                float angle = (i * 137.50776f) + SeedRange(seed, i, 7301, -18f, 18f);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                offset.y = TerrainLocalHeight(root, offset);
                Transform installation = new GameObject($"Wind Harvester {i + 1:00}").transform;
                installation.SetParent(root, false);
                installation.localPosition = offset;
                installation.localRotation = Quaternion.Euler(0f, SeedRange(seed, i, 7303, 0f, 360f), 0f);

                float state = SeedRange(seed, i, 7305, 0f, 1f);
                bool fallen = state < _settings.HarvesterFallenChance;
                if (!hasFallen && i == count - 1 && _settings.HarvesterFallenChance > 0f)
                {
                    fallen = true;
                }
                bool leaning = !fallen && SeedRange(seed, i, 7307, 0f, 1f) < _settings.HarvesterLeanChance;

                float tilt = 0f;
                if (fallen)
                {
                    hasFallen = true;
                    float minimum = Mathf.Min(
                        _settings.HarvesterPrefabFallenMinimumAngle,
                        _settings.HarvesterPrefabFallenMaximumAngle);
                    float maximum = Mathf.Max(
                        _settings.HarvesterPrefabFallenMinimumAngle,
                        _settings.HarvesterPrefabFallenMaximumAngle);
                    tilt = SeedRange(seed, i, 7311, minimum, maximum);
                }
                else if (leaning)
                {
                    float minimum = Mathf.Min(
                        _settings.HarvesterPrefabLeanMinimumAngle,
                        _settings.HarvesterPrefabLeanMaximumAngle);
                    float maximum = Mathf.Max(
                        _settings.HarvesterPrefabLeanMinimumAngle,
                        _settings.HarvesterPrefabLeanMaximumAngle);
                    tilt = SeedRange(seed, i, 7317, minimum, maximum);
                }
                if (SeedRange(seed, i, 7321, 0f, 1f) < 0.5f)
                {
                    tilt = -tilt;
                }

                GameObject harvester = UnityEngine.Object.Instantiate(harvesterPrefab, installation, false);
                harvester.name = harvesterPrefab.name;
                harvester.transform.localPosition = Vector3.down * (fallen
                    ? _settings.HarvesterPrefabFallenGroundSink
                    : _settings.HarvesterPrefabGroundSink);
                harvester.transform.localRotation = Quaternion.Euler(0f, 0f, tilt) * prefabRotation;
                harvester.transform.localScale = prefabScale;

                Transform wings = harvester.transform.Find(_settings.HarvesterWingsTransformName);
                if (wings != null)
                {
                    float minimum = Mathf.Min(
                        _settings.HarvesterWingsMinimumZRotation,
                        _settings.HarvesterWingsMaximumZRotation);
                    float maximum = Mathf.Max(
                        _settings.HarvesterWingsMinimumZRotation,
                        _settings.HarvesterWingsMaximumZRotation);
                    float wingsRotation = SeedRange(seed, i, 7327, minimum, maximum);
                    wings.localRotation *= Quaternion.Euler(0f, 0f, wingsRotation);
                }

                // Keep the prefab's authored root scale as the final placement operation.
                harvester.transform.localScale = prefabScale;
            }
            BuildDebrisTrail(root, "Harvester Field Debris", seed + 79, _settings.HarvesterDebrisCount,
                fieldRadius, _settings.HarvesterRingThickness * 2.5f, Vector3.forward,
                _materials.LandmarkInterior, 7331);
        }

        private void BuildBuriedArcology(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            GameObject arcologyPrefab = _settings.BuriedArcologyPrefab;
            if (arcologyPrefab == null && !string.IsNullOrWhiteSpace(_settings.BuriedArcologyResourcePath))
            {
                arcologyPrefab = Resources.Load<GameObject>(_settings.BuriedArcologyResourcePath);
            }

            if (arcologyPrefab == null)
            {
                Debug.LogWarning("Buried arcology replacement prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = arcologyPrefab.transform.localScale;
            Quaternion prefabRotation = arcologyPrefab.transform.localRotation;
            GameObject arcology = UnityEngine.Object.Instantiate(arcologyPrefab, root, false);
            arcology.name = arcologyPrefab.name;
            arcology.transform.localPosition = Vector3.zero;
            arcology.transform.localRotation = prefabRotation;
            arcology.transform.localScale = prefabScale;
            GroundPrefabToDunes(arcology.transform, _settings.BuriedArcologyGroundingSamplesPerAxis, 0f);
        }

        private void BuildProceduralBuriedArcology(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            float coreRadius = Mathf.Max(8f, _settings.ArcologyCoreRadius);
            float coreHeight = Mathf.Max(8f, _settings.ArcologyCoreHeight);
            float coreBase = -(coreHeight * _settings.ArcologyBurialRatio);
            MeshPart("Arcology Central Crown", root, new Vector3(0f, coreBase + coreHeight * 0.5f, 0f),
                Quaternion.Euler(0f, 45f, 0f), Vector3.one,
                CreateTaperedPrismMesh(coreRadius * 2f, coreHeight, coreRadius * 2f, 0.72f),
                _materials.LandmarkStone, true);
            Part(PrimitiveType.Cube, "Arcology Crown Aperture", root,
                new Vector3(0f, coreBase + coreHeight + 0.08f, 0f),
                new Vector3(coreRadius * 0.42f, coreHeight * 0.018f, coreRadius * 0.42f),
                Quaternion.Euler(0f, 45f, 0f), _materials.LandmarkInterior, false);

            float exposedCrownHeight = coreHeight * (1f - _settings.ArcologyBurialRatio);
            for (int terrace = 0; terrace < 3; terrace++)
            {
                float terraceScale = coreRadius * (2.55f - terrace * 0.42f);
                Part(PrimitiveType.Cube, $"Arcology Sandline Terrace {terrace + 1}", root,
                    new Vector3(0f, exposedCrownHeight * (0.18f + terrace * 0.22f), 0f),
                    new Vector3(terraceScale, coreHeight * 0.035f, terraceScale),
                    Quaternion.Euler(0f, 45f, 0f),
                    terrace == 1 ? _materials.LandmarkSecondary : _materials.LandmarkStone, true);
            }

            int roofCount = Mathf.Max(1, _settings.ArcologyRoofClusterCount);
            for (int i = 0; i < roofCount; i++)
            {
                float angle = (360f / roofCount) * i + SeedRange(seed, i, 7401, -18f, 18f);
                float distance = SeedRange(seed, i, 7403, coreRadius * 0.9f,
                    _settings.ArcologyRoofClusterRadius);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                float terrain = TerrainLocalHeight(root, offset);
                float clusterRadius = coreRadius * SeedRange(seed, i, 7405, 0.22f, 0.48f);
                float clusterHeight = coreHeight * SeedRange(seed, i, 7407, 0.22f, 0.46f);
                float burial = clusterHeight * SeedRange(seed, i, 7409,
                    _settings.ArcologyBurialRatio, Mathf.Min(0.95f, _settings.ArcologyBurialRatio + 0.1f));
                offset.y = terrain - burial + clusterHeight * 0.5f;
                MeshPart($"Arcology Roof Cluster {i + 1}", root, offset,
                    Quaternion.Euler(SeedRange(seed, i, 7411, -4f, 4f), angle, 0f), Vector3.one,
                    CreateTaperedPrismMesh(clusterRadius * 2f, clusterHeight,
                        clusterRadius * SeedRange(seed, i, 7413, 1.2f, 2f), 0.64f),
                    i % 3 == 0 ? _materials.LandmarkSecondary : _materials.LandmarkStone, true);
            }

            for (int i = 0; i < Mathf.Max(0, _settings.ArcologyStructuralRibCount); i++)
            {
                float angle = (360f / Mathf.Max(1, _settings.ArcologyStructuralRibCount)) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 start = direction * (coreRadius * 0.72f);
                Vector3 end = direction * SeedRange(seed, i, 7421, coreRadius * 1.4f,
                    _settings.ArcologyRoofClusterRadius);
                start.y = TerrainLocalHeight(root, start) + coreHeight * 0.04f;
                end.y = TerrainLocalHeight(root, end) - coreHeight * 0.025f;
                BeamBetween(root, $"Submerging Structural Rib {i + 1}", start, end,
                    coreRadius * 0.045f, _materials.LandmarkMetal, true);
            }

            for (int i = 0; i < Mathf.Max(0, _settings.ArcologyVentTowerCount); i++)
            {
                float angle = SeedRange(seed, i, 7431, 0f, 360f);
                float distance = SeedRange(seed, i, 7433, coreRadius * 0.75f,
                    _settings.ArcologyRoofClusterRadius * 0.85f);
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                float ventHeight = coreHeight * SeedRange(seed, i, 7435, 0.08f, 0.19f);
                offset.y = TerrainLocalHeight(root, offset) + ventHeight * 0.42f;
                Part(PrimitiveType.Cylinder, $"Arcology Vent Tower {i + 1}", root, offset,
                    new Vector3(coreRadius * 0.045f, ventHeight * 0.5f, coreRadius * 0.045f),
                    Quaternion.Euler(SeedRange(seed, i, 7437, -5f, 5f), 0f,
                        SeedRange(seed, i, 7439, -5f, 5f)), _materials.LandmarkMetal, true);
                Part(PrimitiveType.Cylinder, $"Arcology Vent Crown {i + 1}", root,
                    offset + Vector3.up * ventHeight * 0.52f,
                    new Vector3(coreRadius * 0.075f, coreHeight * 0.008f, coreRadius * 0.075f),
                    Quaternion.identity, _materials.LandmarkAccent, false);
            }

            for (int i = 0; i < Mathf.Max(0, _settings.ArcologyExposedWindowCount); i++)
            {
                float angle = SeedRange(seed, i, 7451, 0f, 360f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 offset = direction * SeedRange(seed, i, 7453, coreRadius * 0.55f, coreRadius * 0.78f);
                offset.y = SeedRange(seed, i, 7455, coreHeight * 0.025f,
                    coreHeight * Mathf.Max(0.04f, 1f - _settings.ArcologyBurialRatio));
                Part(PrimitiveType.Cube, $"Half-Buried Arcology Window {i + 1}", root, offset,
                    new Vector3(coreRadius * 0.085f, coreHeight * 0.028f, coreRadius * 0.018f),
                    Quaternion.Euler(0f, angle, 0f),
                    i % 4 == 0 ? _materials.LandmarkAccent : _materials.LandmarkInterior, false);
            }
            int antennaCount = Mathf.Max(1, _settings.ArcologyVentTowerCount / 4);
            for (int i = 0; i < antennaCount; i++)
            {
                float angle = (360f / antennaCount) * i + 25f;
                Vector3 basePoint = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (coreRadius * 0.28f);
                basePoint.y = coreBase + coreHeight;
                Vector3 tip = basePoint + new Vector3(coreRadius * 0.08f, coreHeight * 0.13f, 0f);
                BeamBetween(root, $"Broken Arcology Antenna {i + 1}", basePoint, tip,
                    coreRadius * 0.018f, _materials.LandmarkMetal, false);
            }
            BuildDebrisTrail(root, "Arcology Upper Ruin", seed + 101, _settings.ArcologyDebrisCount,
                _settings.ArcologyRoofClusterRadius, coreRadius * 0.08f, Vector3.right,
                _materials.LandmarkStone, 7461);
        }

        private void BuildSandRing(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            GameObject sandRingPrefab = null;
            if (!string.IsNullOrWhiteSpace(_settings.SandRingResourcePath))
            {
                sandRingPrefab = Resources.Load<GameObject>(_settings.SandRingResourcePath);
            }
            if (sandRingPrefab == null)
            {
                sandRingPrefab = _settings.SandRingPrefab;
            }
            if (sandRingPrefab == null)
            {
                Debug.LogWarning("Sand ring replacement prefab could not be resolved from Dune Vector Runtime Settings.", root);
                return;
            }

            Vector3 prefabScale = sandRingPrefab.transform.localScale;
            Quaternion prefabRotation = sandRingPrefab.transform.localRotation;
            GameObject sandRing = UnityEngine.Object.Instantiate(sandRingPrefab, root, false);
            sandRing.name = sandRingPrefab.name;
            sandRing.transform.localPosition = Vector3.zero;
            sandRing.transform.localRotation = prefabRotation;
            sandRing.transform.localScale = prefabScale;
            GroundPrefabToDunes(
                sandRing.transform,
                _settings.SandRingGroundingSamplesPerAxis,
                _settings.SandRingBurialDepth);

            // Grounding only moves the prefab; restore its authored scale last so it remains exact.
            sandRing.transform.localScale = prefabScale;
        }

        private void BuildCurvedTower(Transform parent, float height, float curve,
            float thickness, float completion)
        {
            int segmentCount = 6;
            int builtSegments = Mathf.Clamp(Mathf.CeilToInt(segmentCount * completion), 1, segmentCount);
            Vector3 previous = Vector3.zero;
            for (int i = 1; i <= builtSegments; i++)
            {
                float t = i / (float)segmentCount;
                Vector3 next = new Vector3(curve * t * t, height * t, 0f);
                BeamBetween(parent, $"Curved Tower Segment {i}", previous, next, thickness,
                    _materials.LandmarkInterior, true);
                previous = next;
            }
        }

        private void BuildDebrisTrail(Transform root, string namePrefix, int seed, int count,
            float spread, float size, Vector3 trailDirection, Material material, int salt,
            bool useWorldScaleBoxUvs = false)
        {
            Vector3 direction = trailDirection.sqrMagnitude > 0.001f ? trailDirection.normalized : Vector3.forward;
            Vector3 lateral = new Vector3(-direction.z, 0f, direction.x);
            for (int i = 0; i < Mathf.Max(0, count); i++)
            {
                float distance = SeedRange(seed, i, salt, spread * 0.08f, spread);
                float lateralOffset = SeedRange(seed, i, salt + 2, -spread * 0.38f, spread * 0.38f);
                Vector3 offset = direction * distance + lateral * lateralOffset;
                offset.y = TerrainLocalHeight(root, offset) + SeedRange(seed, i, salt + 4, -size * 0.4f, size * 0.35f);
                float pieceScale = size * SeedRange(seed, i, salt + 6, 0.45f, 1.35f);
                Vector3 pieceSize = new Vector3(
                    pieceScale,
                    pieceScale * SeedRange(seed, i, salt + 8, 0.35f, 1.2f),
                    pieceScale * SeedRange(seed, i, salt + 10, 0.5f, 1.6f));
                Quaternion rotation = Quaternion.Euler(
                    SeedRange(seed, i, salt + 12, 0f, 360f),
                    SeedRange(seed, i, salt + 14, 0f, 360f),
                    SeedRange(seed, i, salt + 16, 0f, 360f));
                if (useWorldScaleBoxUvs)
                {
                    TexturedBoxPart($"{namePrefix} {i + 1:00}", root, offset,
                        pieceSize, rotation, material, true);
                }
                else
                {
                    Part(i % 4 == 0 ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                        $"{namePrefix} {i + 1:00}", root, offset,
                        pieceSize, rotation, material, true);
                }
            }
        }

        private void VerticalSegmentedRing(Transform parent, string partName, float radius,
            float thickness, int segmentCount, int missingCount, int seed, Material material,
            bool collider, Material secondaryMaterial = null)
        {
            int count = Mathf.Max(3, segmentCount);
            float segmentLength = ((Mathf.PI * 2f * Mathf.Max(0.01f, radius)) / count) *
                _settings.LandmarkRingSegmentFill;
            for (int i = 0; i < count; i++)
            {
                if (IsMissingSegment(i, count, missingCount, seed))
                {
                    continue;
                }
                float angle = (360f / count) * i;
                Vector3 point = Quaternion.Euler(0f, 0f, angle) * Vector3.right * radius;
                Part(PrimitiveType.Cube, $"{partName} {i + 1:00}", parent, point,
                    new Vector3(segmentLength, thickness, thickness),
                    Quaternion.Euler(0f, 0f, angle + 90f),
                    secondaryMaterial != null && i % 4 == 0 ? secondaryMaterial : material, collider);
            }
        }

        private void HorizontalSegmentedRing(Transform parent, string partName, float radius,
            float thickness, int segmentCount, int missingCount, int seed, Material material, bool collider)
        {
            int count = Mathf.Max(3, segmentCount);
            float segmentLength = ((Mathf.PI * 2f * Mathf.Max(0.01f, radius)) / count) *
                _settings.LandmarkRingSegmentFill;
            for (int i = 0; i < count; i++)
            {
                if (IsMissingSegment(i, count, missingCount, seed))
                {
                    continue;
                }
                float angle = (360f / count) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"{partName} {i + 1:00}", parent, direction * radius,
                    new Vector3(thickness, thickness, segmentLength),
                    Quaternion.Euler(0f, angle + 90f, 0f), material, collider);
            }
        }

        private static bool IsMissingSegment(int index, int segmentCount, int missingCount, int seed)
        {
            int targetCount = Mathf.Clamp(missingCount, 0, Mathf.Max(0, segmentCount - 3));
            uint value = DuneVectorMath.Hash(seed, index, 0, 7523);
            int rank = 0;
            for (int other = 0; other < segmentCount; other++)
            {
                if (other == index)
                {
                    continue;
                }
                uint otherValue = DuneVectorMath.Hash(seed, other, 0, 7523);
                if (otherValue < value || (otherValue == value && other < index))
                {
                    rank++;
                }
            }
            return rank < targetCount;
        }

        private float TerrainLocalHeight(Transform root, Vector3 localOffset)
        {
            DuneVectorLandmarkInstance instance = root.GetComponent<DuneVectorLandmarkInstance>();
            if (instance == null || _world == null)
            {
                return 0f;
            }
            Vector3 horizontal = root.rotation * new Vector3(localOffset.x, 0f, localOffset.z);
            double baseHeight = _world.HeightField.SampleHeight(
                instance.LogicalPosition.X, instance.LogicalPosition.Z);
            double sampleHeight = _world.HeightField.SampleHeight(
                instance.LogicalPosition.X + horizontal.x,
                instance.LogicalPosition.Z + horizontal.z);
            return (float)(sampleHeight - baseHeight);
        }

        private float SeedRange(int seed, int index, int salt, float minimum, float maximum)
        {
            return DuneVectorMath.HashRange(seed, index, _world != null ? _world.WorldSeed : 0,
                salt, minimum, maximum);
        }

        private void SegmentedRing(
            Transform parent,
            int segmentCount,
            float radius,
            float thickness,
            Material material,
            string partName)
        {
            int count = Mathf.Max(3, segmentCount);
            float segmentLength = ((Mathf.PI * 2f * Mathf.Max(0.01f, radius)) / count) * _settings.LandmarkRingSegmentFill;
            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(
                    PrimitiveType.Cube,
                    $"{partName} {i + 1:00}",
                    parent,
                    direction * radius,
                    new Vector3(thickness, thickness, segmentLength),
                    Quaternion.Euler(0f, angle + 90f, 0f),
                    material,
                    false);
            }
        }

        private static Transform BeamBetween(
            Transform parent,
            string partName,
            Vector3 start,
            Vector3 end,
            float thickness,
            Material material,
            bool collider)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
            {
                return null;
            }
            return Part(
                PrimitiveType.Cube,
                partName,
                parent,
                (start + end) * 0.5f,
                new Vector3(thickness, length, thickness),
                Quaternion.FromToRotation(Vector3.up, delta / length),
                material,
                collider);
        }

        private static void RectangularFrame(
            Transform parent,
            string partName,
            Vector3 center,
            float width,
            float length,
            float beamWidth,
            float beamHeight,
            Material material)
        {
            float halfWidth = width * 0.5f;
            float halfLength = length * 0.5f;
            Part(PrimitiveType.Cube, $"{partName} North", parent,
                center + (Vector3.forward * halfLength), new Vector3(width, beamHeight, beamWidth),
                Quaternion.identity, material);
            Part(PrimitiveType.Cube, $"{partName} South", parent,
                center + (Vector3.back * halfLength), new Vector3(width, beamHeight, beamWidth),
                Quaternion.identity, material);
            Part(PrimitiveType.Cube, $"{partName} East", parent,
                center + (Vector3.right * halfWidth), new Vector3(beamWidth, beamHeight, length),
                Quaternion.identity, material);
            Part(PrimitiveType.Cube, $"{partName} West", parent,
                center + (Vector3.left * halfWidth), new Vector3(beamWidth, beamHeight, length),
                Quaternion.identity, material);
        }

        private int PositiveVariant(int seed)
        {
            int count = Mathf.Max(1, _settings.VisualVariantCount);
            return (int)((uint)seed % (uint)count);
        }

        private static Transform MeshPart(
            string partName,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Mesh mesh,
            Material material,
            bool collider)
        {
            GameObject part = new GameObject(partName);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            MeshFilter filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            part.AddComponent<DuneVectorGeneratedLandmarkMesh>().Initialize(mesh);
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (collider)
            {
                MeshCollider meshCollider = part.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
            }
            return part.transform;
        }

        private static Mesh CreateParabolicDishMesh(float radius, int segmentCount,
            int missingSegmentCount, int seed)
        {
            int segments = Mathf.Max(8, segmentCount);
            int rings = Mathf.Max(4, segments / 4);
            int layerVertexCount = (rings + 1) * segments;
            Vector3[] vertices = new Vector3[layerVertexCount * 2];
            Vector2[] uv = new Vector2[vertices.Length];
            float depth = radius * 0.32f;
            float shellThickness = radius * 0.035f;
            for (int layer = 0; layer < 2; layer++)
            {
                for (int ring = 0; ring <= rings; ring++)
                {
                    float t = ring / (float)rings;
                    float ringRadius = radius * t;
                    float y = depth * t * t - (layer * shellThickness);
                    for (int segment = 0; segment < segments; segment++)
                    {
                        float angle = (Mathf.PI * 2f * segment) / segments;
                        int index = (layer * layerVertexCount) + (ring * segments) + segment;
                        vertices[index] = new Vector3(Mathf.Cos(angle) * ringRadius, y,
                            Mathf.Sin(angle) * ringRadius);
                        uv[index] = new Vector2(segment / (float)segments, t);
                    }
                }
            }

            List<int> triangles = new List<int>(rings * segments * 12 + segments * 6);
            for (int layer = 0; layer < 2; layer++)
            {
                int layerStart = layer * layerVertexCount;
                for (int ring = 0; ring < rings; ring++)
                {
                    for (int segment = 0; segment < segments; segment++)
                    {
                        if (IsMissingSegment(segment, segments, missingSegmentCount, seed))
                        {
                            continue;
                        }
                        int next = (segment + 1) % segments;
                        int a = layerStart + (ring * segments) + segment;
                        int b = layerStart + (ring * segments) + next;
                        int c = layerStart + ((ring + 1) * segments) + segment;
                        int d = layerStart + ((ring + 1) * segments) + next;
                        if (layer == 0)
                        {
                            triangles.Add(a); triangles.Add(b); triangles.Add(c);
                            triangles.Add(b); triangles.Add(d); triangles.Add(c);
                        }
                        else
                        {
                            triangles.Add(a); triangles.Add(c); triangles.Add(b);
                            triangles.Add(b); triangles.Add(c); triangles.Add(d);
                        }
                    }
                }
            }
            int frontRim = rings * segments;
            int backRim = layerVertexCount + frontRim;
            for (int segment = 0; segment < segments; segment++)
            {
                if (IsMissingSegment(segment, segments, missingSegmentCount, seed))
                {
                    continue;
                }
                int next = (segment + 1) % segments;
                triangles.Add(frontRim + segment);
                triangles.Add(frontRim + next);
                triangles.Add(backRim + segment);
                triangles.Add(frontRim + next);
                triangles.Add(backRim + next);
                triangles.Add(backRim + segment);
            }

            Mesh mesh = new Mesh { name = "Procedural Parabolic Dish" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTaperedPrismMesh(float width, float height, float depth, float taper)
        {
            float bottomX = width * 0.5f;
            float bottomZ = depth * 0.5f;
            float topScale = Mathf.Clamp01(1f - taper);
            float topX = bottomX * topScale;
            float topZ = bottomZ * topScale;
            Vector3 b0 = new Vector3(-bottomX, -height * 0.5f, -bottomZ);
            Vector3 b1 = new Vector3(bottomX, -height * 0.5f, -bottomZ);
            Vector3 b2 = new Vector3(bottomX, -height * 0.5f, bottomZ);
            Vector3 b3 = new Vector3(-bottomX, -height * 0.5f, bottomZ);
            Vector3 t0 = new Vector3(-topX, height * 0.5f, -topZ);
            Vector3 t1 = new Vector3(topX, height * 0.5f, -topZ);
            Vector3 t2 = new Vector3(topX, height * 0.5f, topZ);
            Vector3 t3 = new Vector3(-topX, height * 0.5f, topZ);
            Vector3[] vertices =
            {
                b0, b1, b2, b3,
                t0, t3, t2, t1,
                b0, t0, t1, b1,
                b1, t1, t2, b2,
                b2, t2, t3, b3,
                b3, t3, t0, b0,
            };
            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23,
            };
            Vector2[] uv = CreateBoxFaceUvs(width, height, depth);
            Mesh mesh = new Mesh { name = "Procedural Tapered Landmark Prism" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Transform TexturedBoxPart(
            string partName,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Quaternion localRotation,
            Material material,
            bool collider)
        {
            Mesh mesh = CreateTexturedBoxMesh(size);
            GameObject part = new GameObject(partName);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            MeshFilter filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            part.AddComponent<DuneVectorGeneratedLandmarkMesh>().Initialize(mesh);
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (collider)
            {
                BoxCollider boxCollider = part.AddComponent<BoxCollider>();
                boxCollider.size = size;
            }
            return part.transform;
        }

        private static Mesh CreateTexturedBoxMesh(Vector3 size)
        {
            float halfX = size.x * 0.5f;
            float halfY = size.y * 0.5f;
            float halfZ = size.z * 0.5f;
            Vector3 b0 = new Vector3(-halfX, -halfY, -halfZ);
            Vector3 b1 = new Vector3(halfX, -halfY, -halfZ);
            Vector3 b2 = new Vector3(halfX, -halfY, halfZ);
            Vector3 b3 = new Vector3(-halfX, -halfY, halfZ);
            Vector3 t0 = new Vector3(-halfX, halfY, -halfZ);
            Vector3 t1 = new Vector3(halfX, halfY, -halfZ);
            Vector3 t2 = new Vector3(halfX, halfY, halfZ);
            Vector3 t3 = new Vector3(-halfX, halfY, halfZ);
            Vector3[] vertices =
            {
                b0, b1, b2, b3,
                t0, t3, t2, t1,
                b0, t0, t1, b1,
                b1, t1, t2, b2,
                b2, t2, t3, b3,
                b3, t3, t0, b0,
            };
            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                8, 9, 10, 8, 10, 11,
                12, 13, 14, 12, 14, 15,
                16, 17, 18, 16, 18, 19,
                20, 21, 22, 20, 22, 23,
            };
            Mesh mesh = new Mesh { name = "Procedural World-Scale Textured Box" };
            mesh.vertices = vertices;
            mesh.uv = CreateBoxFaceUvs(size.x, size.y, size.z);
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2[] CreateBoxFaceUvs(float width, float height, float depth)
        {
            Vector2[] uv = new Vector2[24];
            uv[0] = new Vector2(0f, 0f); uv[1] = new Vector2(width, 0f);
            uv[2] = new Vector2(width, depth); uv[3] = new Vector2(0f, depth);
            uv[4] = new Vector2(0f, 0f); uv[5] = new Vector2(0f, depth);
            uv[6] = new Vector2(width, depth); uv[7] = new Vector2(width, 0f);
            uv[8] = new Vector2(0f, 0f); uv[9] = new Vector2(0f, height);
            uv[10] = new Vector2(width, height); uv[11] = new Vector2(width, 0f);
            uv[12] = new Vector2(0f, 0f); uv[13] = new Vector2(0f, height);
            uv[14] = new Vector2(depth, height); uv[15] = new Vector2(depth, 0f);
            uv[16] = new Vector2(0f, 0f); uv[17] = new Vector2(0f, height);
            uv[18] = new Vector2(width, height); uv[19] = new Vector2(width, 0f);
            uv[20] = new Vector2(0f, 0f); uv[21] = new Vector2(0f, height);
            uv[22] = new Vector2(depth, height); uv[23] = new Vector2(depth, 0f);
            return uv;
        }

        private static Transform Part(
            PrimitiveType primitive,
            string partName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material,
            bool collider = true)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null)
            {
                partCollider.enabled = collider;
            }
            return part.transform;
        }

        private Vector2Int LogicalToCell(LogicalPosition logical)
        {
            float size = Mathf.Max(1f, _settings.PlacementCellSize);
            return new Vector2Int(Mathf.FloorToInt((float)(logical.X / size)), Mathf.FloorToInt((float)(logical.Z / size)));
        }

        private int HashCell(Vector2Int cell)
        {
            return unchecked((int)DuneVectorMath.Hash(cell.x, cell.y, _world.WorldSeed, 7121));
        }

        private void HandleWorldShift(Vector3 shift)
        {
            foreach (DuneVectorLandmarkInstance landmark in _streamed.Values)
            {
                landmark?.ApplyWorldShift(shift);
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
        }
    }
}
