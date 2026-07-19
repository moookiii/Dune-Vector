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
            ContractSocket = CreateSocket("Contract Socket", contractOffset);
            EncounterSocket = CreateSocket("Encounter Socket", Vector3.up * settings.EncounterSocketHeight);
            LootSocket = CreateSocket("Loot Socket", new Vector3(4f, 2f, -3f));
            FlightPathSocket = CreateSocket("Flight Path Socket", Vector3.up * settings.FlightSocketHeight);
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
            switch (type)
            {
                case DuneLandmarkType.DesertRelayStation:
                    BuildRelay(landmarkObject.transform, variantSeed);
                    break;
                case DuneLandmarkType.CrashedCarrier:
                    BuildCarrier(landmarkObject.transform, variantSeed);
                    break;
                case DuneLandmarkType.RaiderBeacon:
                    BuildBeacon(landmarkObject.transform, variantSeed);
                    break;
                case DuneLandmarkType.AncientSpire:
                    BuildSpire(landmarkObject.transform, variantSeed);
                    break;
                case DuneLandmarkType.SandExcavationSite:
                    BuildExcavation(landmarkObject.transform, variantSeed);
                    break;
            }
            return instance;
        }

        private void BuildRelay(Transform root, int seed)
        {
            float scale = _settings.RelayScale;
            Part(PrimitiveType.Cube, "Relay Platform", root, new Vector3(0f, 0.6f, 0f), new Vector3(22f, 1.2f, 16f) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Cube, "Relay Building", root, new Vector3(0f, 3.5f, 0f), new Vector3(11f, 5.8f, 8f) * scale, Quaternion.identity, _materials.Sandstone);
            Part(PrimitiveType.Cylinder, "Long Range Antenna", root, new Vector3(0f, _settings.RelayAntennaHeight * 0.5f, 0f), new Vector3(0.62f, _settings.RelayAntennaHeight * 0.5f, 0.62f) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Sphere, "Antenna Beacon", root, new Vector3(0f, _settings.RelayAntennaHeight, 0f) * scale, Vector3.one * 1.3f * scale, Quaternion.identity, _materials.DroneAccent);
            Part(PrimitiveType.Sphere, "Dish", root, new Vector3(0f, _settings.RelayAntennaHeight * 0.62f, 0f) * scale, new Vector3(6f, 0.8f, 6f) * scale, Quaternion.Euler(18f, 0f, 0f), _materials.DroneBody);
            Part(PrimitiveType.Cylinder, "Fuel Tank A", root, new Vector3(-7f, 2f, 5f) * scale, new Vector3(1.6f, 2.4f, 1.6f) * scale, Quaternion.Euler(0f, 0f, 90f), _materials.DroneDark);
            Part(PrimitiveType.Cylinder, "Fuel Tank B", root, new Vector3(7f, 2f, 5f) * scale, new Vector3(1.6f, 2.4f, 1.6f) * scale, Quaternion.Euler(0f, 0f, 90f), _materials.DroneDark);
            if ((seed & 1) == 0)
            {
                Part(PrimitiveType.Cylinder, "Secondary Antenna", root, new Vector3(6f, 11f, -3f) * scale, new Vector3(0.35f, 11f, 0.35f) * scale, Quaternion.identity, _materials.DroneDark);
            }
        }

        private void BuildCarrier(Transform root, int seed)
        {
            float scale = _settings.CarrierScale;
            float length = _settings.CarrierLength;
            root.localRotation *= Quaternion.Euler(8f, 0f, -13f);
            Part(PrimitiveType.Cube, "Carrier Hull", root, new Vector3(0f, 4f, 0f), new Vector3(12f, 6f, length) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Cube, "Broken Left Wing", root, new Vector3(-16f, 5f, 3f) * scale, new Vector3(24f, 1.6f, 10f) * scale, Quaternion.Euler(0f, -12f, 7f), _materials.Sandstone);
            Part(PrimitiveType.Cube, "Broken Right Wing", root, new Vector3(14f, 3f, -8f) * scale, new Vector3(17f, 1.4f, 8f) * scale, Quaternion.Euler(0f, 18f, -11f), _materials.Sandstone);
            for (int i = 0; i < 5; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                Part(PrimitiveType.Cube, $"Scattered Cargo {i + 1}", root, new Vector3(side * (10f + (i * 2f)), 1.2f, -18f + (i * 8f)) * scale, new Vector3(3.6f, 2.4f, 3f) * scale, Quaternion.Euler(0f, seed + (i * 31f), 0f), _materials.Package);
            }
        }

        private void BuildBeacon(Transform root, int seed)
        {
            float scale = _settings.BeaconScale;
            float height = _settings.BeaconHeight;
            Part(PrimitiveType.Cylinder, "Beacon Tower", root, new Vector3(0f, height * 0.5f, 0f) * scale, new Vector3(2.2f, height * 0.5f, 2.2f) * scale, Quaternion.identity, _materials.EnemyBody);
            Part(PrimitiveType.Sphere, "Beacon Energy", root, new Vector3(0f, height, 0f) * scale, Vector3.one * 4.8f * scale, Quaternion.identity, _materials.EnemyCore);
            for (int i = 0; i < 3; i++)
            {
                float angle = (i * 120f) + Mathf.Repeat(seed * 0.1f, 50f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Part(PrimitiveType.Cube, $"Generator {i + 1}", root, (direction * 10f + Vector3.up * 1.6f) * scale, new Vector3(4.5f, 3.2f, 3.4f) * scale, Quaternion.Euler(0f, angle, 0f), _materials.DroneDark);
                Part(PrimitiveType.Cylinder, $"Floating Antenna {i + 1}", root, (direction * 7f + Vector3.up * (height * 0.62f)) * scale, new Vector3(0.5f, 4f, 0.5f) * scale, Quaternion.Euler(90f, angle, 0f), _materials.EnemyCore, false);
            }
        }

        private void BuildSpire(Transform root, int seed)
        {
            float scale = _settings.SpireScale;
            float height = _settings.SpireHeight;
            for (int i = 0; i < 7; i++)
            {
                float layer01 = i / 7f;
                float width = Mathf.Lerp(18f, 3f, layer01);
                Part(PrimitiveType.Cube, $"Spire Layer {i + 1}", root, new Vector3(0f, ((i + 0.5f) * height / 7f), 0f) * scale, new Vector3(width, (height / 7f) * 0.94f, width) * scale, Quaternion.Euler(0f, i * 13f, 0f), _materials.Sandstone);
            }
            Part(PrimitiveType.Sphere, "Floating Spire Relic", root, new Vector3(0f, height + 12f, 0f) * scale, Vector3.one * 5f * scale, Quaternion.identity, _materials.DroneAccent, false);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, height * 0.58f, 15f);
                Part(PrimitiveType.Cylinder, $"Flight Monolith {i + 1}", root, offset * scale, new Vector3(1.2f, 8f, 1.2f) * scale, Quaternion.identity, _materials.DroneDark);
            }
        }

        private void BuildExcavation(Transform root, int seed)
        {
            float scale = _settings.ExcavationScale;
            float craneHeight = _settings.ExcavationCraneHeight;
            Part(PrimitiveType.Cube, "Buried Structure", root, new Vector3(0f, -1.5f, 0f) * scale, new Vector3(28f, 8f, 24f) * scale, Quaternion.Euler(0f, 12f, 6f), _materials.Sandstone);
            Part(PrimitiveType.Cube, "Crane Mast", root, new Vector3(-15f, craneHeight * 0.5f, -8f) * scale, new Vector3(2f, craneHeight, 2f) * scale, Quaternion.identity, _materials.DroneDark);
            Part(PrimitiveType.Cube, "Crane Boom", root, new Vector3(-3f, craneHeight, -8f) * scale, new Vector3(26f, 1.5f, 1.5f) * scale, Quaternion.Euler(0f, 0f, -5f), _materials.DroneDark);
            Part(PrimitiveType.Cylinder, "Crane Cable", root, new Vector3(8f, craneHeight * 0.65f, -8f) * scale, new Vector3(0.18f, craneHeight * 0.35f, 0.18f) * scale, Quaternion.identity, _materials.DroneDark, false);
            for (int i = 0; i < 4; i++)
            {
                Part(PrimitiveType.Cube, $"Scaffold {i + 1}", root, new Vector3(-10f + (i * 7f), 5f + (i % 2) * 3f, 12f) * scale, new Vector3(5f, 0.8f, 10f) * scale, Quaternion.identity, _materials.DroneBody);
            }
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
