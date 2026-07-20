using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum DuneLandmarkType
    {
        DesertRelayStation,
        CrashedCarrier,
        RaiderBeacon,
        AncientSpire,
        SandExcavationSite,
    }

    public enum DuneLandmarkRarity
    {
        Common,
        Standard,
        Rare,
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
                    contractOffset = new Vector3(0f, settings.ContractSocketHeight, -13f * settings.RelayScale);
                    break;
                case DuneLandmarkType.CrashedCarrier:
                    contractOffset = new Vector3(18f * settings.CarrierScale, settings.ContractSocketHeight, -30f * settings.CarrierScale);
                    break;
                case DuneLandmarkType.RaiderBeacon:
                    contractOffset = new Vector3(13f * settings.BeaconScale, settings.ContractSocketHeight, 0f);
                    break;
                case DuneLandmarkType.AncientSpire:
                    contractOffset = new Vector3(24f * settings.SpireScale, settings.ContractSocketHeight + 3f, 0f);
                    break;
                default:
                    contractOffset = new Vector3(0f, settings.ContractSocketHeight, 19f * settings.ExcavationScale);
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

    [DefaultExecutionOrder(1040)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorLandmarkDirector : MonoBehaviour
    {
        private readonly Dictionary<Vector2Int, DuneVectorLandmarkInstance> _streamed =
            new Dictionary<Vector2Int, DuneVectorLandmarkInstance>();
        private readonly List<DuneVectorLandmarkInstance> _pinned = new List<DuneVectorLandmarkInstance>();
        private readonly List<Vector2Int> _removeBuffer = new List<Vector2Int>();

        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private LandmarkSystemTuning _settings;
        private Transform _root;
        private float _refreshTimer;
        private Vector2Int _lastCenter = new Vector2Int(int.MinValue, int.MinValue);

        public IReadOnlyCollection<DuneVectorLandmarkInstance> StreamedLandmarks => _streamed.Values;
        public IReadOnlyList<DuneVectorLandmarkInstance> ContractLandmarks => _pinned;

        public void Initialize(
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            LandmarkSystemTuning settings)
        {
            _world = world;
            _materials = materials;
            _settings = settings;
            GameObject rootObject = new GameObject("Authored Procedural Landmarks");
            _root = rootObject.transform;
            _root.SetParent(transform, false);
            _world.WorldShifted += HandleWorldShift;
            Refresh(force: true);
        }

        public DuneVectorLandmarkInstance CreateContractLandmark(
            DuneLandmarkType type,
            LogicalPosition logicalPosition,
            int variantSeed)
        {
            DuneLandmarkRarity rarity = type == DuneLandmarkType.AncientSpire
                ? DuneLandmarkRarity.Rare
                : DuneLandmarkRarity.Standard;
            DuneVectorLandmarkInstance landmark = BuildLandmark(type, rarity, logicalPosition, variantSeed, true);
            _pinned.Add(landmark);
            return landmark;
        }

        public void ClearContractLandmarks()
        {
            for (int i = 0; i < _pinned.Count; i++)
            {
                if (_pinned[i] != null)
                {
                    Destroy(_pinned[i].gameObject);
                }
            }
            _pinned.Clear();
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
                if (Mathf.Max(Mathf.Abs(pair.Key.x - center.x), Mathf.Abs(pair.Key.y - center.y)) > radius + 1)
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
            float roll = DuneVectorMath.Hash01(cell.x, cell.y, _world.WorldSeed, 7103);
            float rareThreshold = _settings.RareCellChance;
            float standardThreshold = rareThreshold + _settings.StandardCellChance;
            float commonThreshold = standardThreshold + _settings.CommonCellChance;
            if (roll > commonThreshold)
            {
                _streamed[cell] = null;
                return;
            }

            DuneLandmarkRarity rarity = roll <= rareThreshold
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
                _streamed[cell] = null;
                return;
            }
            float slope = Vector3.Angle(_world.HeightField.SampleNormal(logical.X, logical.Z), Vector3.up);
            if (slope > _settings.MaximumPlacementSlope)
            {
                logical = new LogicalPosition(
                    ((cell.x + 0.5) * _settings.PlacementCellSize),
                    ((cell.y + 0.5) * _settings.PlacementCellSize));
                slope = Vector3.Angle(_world.HeightField.SampleNormal(logical.X, logical.Z), Vector3.up);
            }
            if (slope > _settings.MaximumPlacementSlope || ViolatesSpacing(logical, rarity))
            {
                _streamed[cell] = null;
                return;
            }

            _streamed[cell] = BuildLandmark(type, rarity, logical, HashCell(cell), false);
        }

        private bool ViolatesSpacing(LogicalPosition logical, DuneLandmarkRarity rarity)
        {
            float required = rarity == DuneLandmarkRarity.Rare
                ? _settings.RareMinimumSpacing
                : rarity == DuneLandmarkRarity.Standard
                    ? _settings.StandardMinimumSpacing
                    : 0f;
            if (required <= 0f)
            {
                return false;
            }

            double requiredSquared = required * required;
            foreach (DuneVectorLandmarkInstance existing in _streamed.Values)
            {
                if (existing == null || existing.Rarity == DuneLandmarkRarity.Common)
                {
                    continue;
                }
                double dx = existing.LogicalPosition.X - logical.X;
                double dz = existing.LogicalPosition.Z - logical.Z;
                if ((dx * dx) + (dz * dz) < requiredSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private DuneLandmarkType ChooseType(Vector2Int cell, DuneLandmarkRarity rarity)
        {
            if (rarity == DuneLandmarkRarity.Rare)
            {
                return DuneLandmarkType.AncientSpire;
            }
            int choice = Mathf.FloorToInt(DuneVectorMath.Hash01(cell.x, cell.y, _world.WorldSeed, 7109) * 4f);
            switch (Mathf.Clamp(choice, 0, 3))
            {
                case 0: return DuneLandmarkType.DesertRelayStation;
                case 1: return DuneLandmarkType.CrashedCarrier;
                case 2: return DuneLandmarkType.RaiderBeacon;
                default: return DuneLandmarkType.SandExcavationSite;
            }
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
            bool pinned)
        {
            GameObject landmarkObject = new GameObject($"{type} {(pinned ? "Contract" : rarity.ToString())}");
            landmarkObject.transform.SetParent(_root, false);
            double height = _world.HeightField.SampleHeight(logical.X, logical.Z);
            landmarkObject.transform.position = _world.LogicalToLocal(logical.X, height, logical.Z);
            landmarkObject.transform.rotation = Quaternion.Euler(0f, Mathf.Repeat(variantSeed * 0.137f, 360f), 0f);

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
            }
            instance.PositionDeliverySocketAboveVisuals(_settings.DeliveryRingClearance);
            return instance;
        }

        private void BuildRelay(Transform root, int seed, DuneVectorLandmarkAnimator animator)
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
            float scale = _settings.CarrierScale;
            float length = _settings.CarrierLength;
            root.localRotation *= Quaternion.Euler(8f, 0f, -13f);
            Part(PrimitiveType.Cube, "Carrier Hull", root, new Vector3(0f, 4f, 0f), new Vector3(12f, 6f, length) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Cube, "Carrier Nose", root, new Vector3(0f, 3.8f, length * 0.54f) * scale,
                new Vector3(8f, 3.8f, length * 0.16f) * scale, Quaternion.Euler(12f, 0f, 0f), _materials.DroneBody);
            Part(PrimitiveType.Cube, "Carrier Tail Spine", root, new Vector3(0f, 7f, -length * 0.44f) * scale,
                new Vector3(2.2f, 8f, length * 0.18f) * scale, Quaternion.Euler(-8f, 0f, 0f), _materials.DroneDark);
            Part(PrimitiveType.Sphere, "Fractured Cockpit Canopy", root, new Vector3(0f, 6.2f, length * 0.42f) * scale,
                new Vector3(5.8f, 2.2f, 7.2f) * scale * _settings.CarrierCockpitScale,
                Quaternion.Euler(9f, 0f, 0f), _materials.DroneAccent, false);
            int engineCount = Mathf.Max(1, _settings.CarrierEngineCount);
            float engineStart = -((engineCount - 1) * _settings.CarrierEngineRadius * 1.35f);
            for (int i = 0; i < engineCount; i++)
            {
                float engineX = engineStart + (i * _settings.CarrierEngineRadius * 2.7f);
                Vector3 enginePosition = new Vector3(engineX, 4.1f, -length * 0.53f) * scale;
                Part(PrimitiveType.Cylinder, $"Carrier Engine Housing {i + 1}", root, enginePosition,
                    new Vector3(_settings.CarrierEngineRadius, _settings.CarrierEngineDepth * 0.5f, _settings.CarrierEngineRadius) * scale,
                    Quaternion.Euler(90f, 0f, 0f), _materials.DroneBody);
                Part(PrimitiveType.Cylinder, $"Carrier Engine Core {i + 1}", root,
                    enginePosition + (Vector3.back * (_settings.CarrierEngineDepth * 0.54f * scale)),
                    new Vector3(_settings.CarrierEngineRadius * 0.58f, 0.12f, _settings.CarrierEngineRadius * 0.58f) * scale,
                    Quaternion.Euler(90f, 0f, 0f), _materials.DroneAccent, false);
            }
            int hullRibCount = Mathf.Max(3, _settings.CarrierHullRibCount);
            for (int i = 0; i < hullRibCount; i++)
            {
                float alongHull = Mathf.Lerp(-length * 0.34f, length * 0.34f, i / (float)(hullRibCount - 1));
                Part(PrimitiveType.Cube, $"Carrier Hull Rib {i + 1}", root,
                    new Vector3(0f, 7.05f, alongHull) * scale,
                    new Vector3(12.8f, _settings.CarrierHullRibThickness, 0.48f) * scale,
                    Quaternion.Euler(0f, 0f, i % 2 == 0 ? 2f : -2f), _materials.DroneBody, false);
            }
            Transform exposedCore = Part(PrimitiveType.Sphere, "Exposed Carrier Power Core", root,
                new Vector3(-5.4f, 4.8f, -length * 0.05f) * scale,
                Vector3.one * (1.65f * scale), Quaternion.identity, _materials.EnemyCore, false);
            animator.RegisterPulse(exposedCore, _settings.BeaconPulseAmount, _settings.BeaconPulseSpeed);
            Part(PrimitiveType.Cube, "Broken Left Wing", root, new Vector3(-16f, 5f, 3f) * scale, new Vector3(24f, 1.6f, 10f) * scale, Quaternion.Euler(0f, -12f, 7f), _materials.Sandstone);
            Part(PrimitiveType.Cube, "Broken Right Wing", root, new Vector3(14f, 3f, -8f) * scale, new Vector3(17f, 1.4f, 8f) * scale, Quaternion.Euler(0f, 18f, -11f), _materials.Sandstone);
            int wreckageCount = Mathf.Max(2, _settings.CarrierWreckageCount);
            for (int i = 0; i < wreckageCount; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                Part(PrimitiveType.Cube, $"Scattered Cargo {i + 1}", root, new Vector3(side * (10f + (i * 2f)), 1.2f, -18f + (i * 8f)) * scale, new Vector3(3.6f, 2.4f, 3f) * scale, Quaternion.Euler(0f, seed + (i * 31f), 0f), _materials.Package);
            }
            int variant = PositiveVariant(seed);
            for (int i = 0; i < variant; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Part(PrimitiveType.Cube, $"Detached Hull Rib {i + 1}", root,
                    new Vector3(side * (20f + (i * 2f)), 2f + i, -10f + (i * 11f)) * scale,
                    new Vector3(1.2f, 6f, 12f) * scale,
                    Quaternion.Euler(5f * i, seed + (i * 37f), 18f * side), _materials.DroneBody);
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
                _materials.DroneAccent,
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
                    Quaternion.Euler(0f, i * 13f, 0f), _materials.Sandstone);
                if (i < layerCount - 1)
                {
                    float nextWidth = Mathf.Lerp(18f, 3f, (i + 1f) / layerCount);
                    Part(PrimitiveType.Cube, $"Spire Energy Seam {i + 1}", root,
                        new Vector3(0f, (i + 1f) * layerHeight, 0f) * scale,
                        new Vector3(nextWidth * 0.86f, _settings.SpireSeamHeight, nextWidth * 0.86f) * scale,
                        Quaternion.Euler(0f, (i + 1f) * 13f, 0f), _materials.DroneAccent, false);
                }
            }
            Transform relic = Part(PrimitiveType.Sphere, "Floating Spire Relic", root, new Vector3(0f, height + 12f, 0f) * scale, Vector3.one * 5f * scale, Quaternion.identity, _materials.DroneAccent, false);
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
                    Quaternion.Euler(i * 17f, angle, i * 31f), _materials.DroneDark, false);
            }
            animator.RegisterSpin(shardOrbit, Vector3.up, -_settings.SpireRelicRotationSpeed);
            int monolithCount = Mathf.Max(3, _settings.SpireMonolithCount);
            for (int i = 0; i < monolithCount; i++)
            {
                float angle = (360f / monolithCount) * i;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, height * 0.58f, 15f);
                Part(PrimitiveType.Cylinder, $"Flight Monolith {i + 1}", root, offset * scale, new Vector3(1.2f, 8f, 1.2f) * scale, Quaternion.identity, _materials.DroneDark);
            }
            int variant = PositiveVariant(seed);
            for (int i = 0; i < variant; i++)
            {
                float angle = (360f / Mathf.Max(1, variant)) * i + 45f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Buried Spire Fin {i + 1}", root,
                    (direction * (16f + (i * 3f)) + Vector3.up * 2f) * scale,
                    new Vector3(2f, 9f, 7f) * scale,
                    Quaternion.Euler(0f, angle, 18f), _materials.Sandstone);
            }
        }

        private void BuildExcavation(Transform root, int seed, DuneVectorLandmarkAnimator animator)
        {
            float scale = _settings.ExcavationScale;
            float craneHeight = _settings.ExcavationCraneHeight;
            int terraceCount = Mathf.Max(1, _settings.ExcavationPitTerraceCount);
            for (int i = 0; i < terraceCount; i++)
            {
                float inset = i * _settings.ExcavationTerraceStep;
                float terraceWidth = Mathf.Max(4f, _settings.ExcavationPitWidth - (inset * 2f));
                float terraceLength = Mathf.Max(4f, _settings.ExcavationPitLength - (inset * 2f));
                RectangularFrame(
                    root,
                    $"Excavation Terrace {i + 1}",
                    new Vector3(0f, 0.3f - (i * 0.42f), 0f) * scale,
                    terraceWidth * scale,
                    terraceLength * scale,
                    Mathf.Max(0.2f, _settings.ExcavationTerraceStep * 0.34f) * scale,
                    0.55f * scale,
                    i == 0 ? _materials.DroneDark : _materials.Sandstone);
            }
            Part(PrimitiveType.Cube, "Buried Structure", root, new Vector3(0f, -1.5f, 0f) * scale, new Vector3(28f, 8f, 24f) * scale, Quaternion.Euler(0f, 12f, 6f), _materials.Sandstone);
            Part(PrimitiveType.Cube, "Crane Mast", root, new Vector3(-15f, craneHeight * 0.5f, -8f) * scale, new Vector3(2f, craneHeight, 2f) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Cube, "Crane Boom", root, new Vector3(-3f, craneHeight, -8f) * scale, new Vector3(26f, 1.5f, 1.5f) * scale, Quaternion.Euler(0f, 0f, -5f), _materials.DroneDark);
            int trussCount = Mathf.Max(2, _settings.ExcavationCraneTrussCount);
            for (int i = 0; i < trussCount; i++)
            {
                float t = trussCount == 1 ? 0.5f : i / (float)(trussCount - 1);
                float x = Mathf.Lerp(-15f, 9f, t);
                Part(PrimitiveType.Cube, $"Crane Truss Upright {i + 1}", root,
                    new Vector3(x, craneHeight - 1.45f, -8f) * scale,
                    new Vector3(0.28f, 3.2f, 1.9f) * scale,
                    Quaternion.Euler(0f, 0f, -5f), _materials.DroneBody, false);
            }
            Part(PrimitiveType.Cylinder, "Crane Cable", root, new Vector3(8f, craneHeight * 0.65f, -8f) * scale, new Vector3(0.18f, craneHeight * 0.35f, 0.18f) * scale, Quaternion.identity, _materials.DroneDark, false);
            int scaffoldCount = Mathf.Max(2, _settings.ExcavationScaffoldCount);
            for (int i = 0; i < scaffoldCount; i++)
            {
                float scaffoldX = Mathf.Lerp(-10f, 11f, scaffoldCount == 1 ? 0.5f : i / (float)(scaffoldCount - 1));
                Part(PrimitiveType.Cube, $"Scaffold {i + 1}", root,
                    new Vector3(scaffoldX, 5f + (i % 2) * 3f, 12f) * scale,
                    new Vector3(5f, 0.8f, 10f) * scale, Quaternion.identity, _materials.DroneBody);
            }
            int cargoStackCount = Mathf.Max(1, _settings.ExcavationCargoStackCount);
            for (int i = 0; i < cargoStackCount; i++)
            {
                float row = i / 3;
                float column = i % 3;
                Part(PrimitiveType.Cube, $"Excavation Supply Crate {i + 1}", root,
                    new Vector3(10f + (column * 2.5f), 1.1f + ((i % 2) * 0.45f), -3f + (row * 3f)) * scale,
                    new Vector3(2.1f, 2.2f, 2.1f) * scale,
                    Quaternion.Euler(0f, seed + (i * 23f), 0f), _materials.Package);
            }
            int lightCount = Mathf.Max(2, _settings.ExcavationWorkLightCount);
            for (int i = 0; i < lightCount; i++)
            {
                float angle = (360f / lightCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Transform workLight = Part(PrimitiveType.Sphere, $"Excavation Work Light {i + 1}", root,
                    (direction * 13f + Vector3.up * (3f + (i % 2) * 3f)) * scale,
                    Vector3.one * 0.7f * scale, Quaternion.identity, _materials.DroneAccent, false);
                animator.RegisterPulse(workLight, _settings.BeaconPulseAmount, _settings.ExcavationWorkLightPulseSpeed, i);
            }
            int variant = PositiveVariant(seed);
            if (variant >= 2)
            {
                Part(PrimitiveType.Cube, "Secondary Crane Mast", root,
                    new Vector3(14f, craneHeight * 0.34f, 9f) * scale,
                    new Vector3(1.2f, craneHeight * 0.68f, 1.2f) * scale,
                    Quaternion.Euler(0f, 25f, 0f), _materials.DroneDark);
                Part(PrimitiveType.Cube, "Secondary Crane Boom", root,
                    new Vector3(7f, craneHeight * 0.68f, 9f) * scale,
                    new Vector3(15f, 0.8f, 0.8f) * scale,
                    Quaternion.Euler(0f, 25f, 4f), _materials.DroneDark);
            }
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
            for (int i = 0; i < _pinned.Count; i++)
            {
                _pinned[i]?.ApplyWorldShift(shift);
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
