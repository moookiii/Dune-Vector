using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorProceduralBuildingDirector : MonoBehaviour
    {
        private const int CountSalt = 17311;
        private const int PositionXSalt = 17321;
        private const int PositionZSalt = 17327;
        private const int PrefabSalt = 17333;
        private const int RotationSalt = 17341;

        private readonly Dictionary<Vector2Int, GameObject> _loadedCells =
            new Dictionary<Vector2Int, GameObject>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();

        private DesertWorldStreamer _world;
        private ProceduralBuildingSystemTuning _settings;
        private GeoglyphSystemTuning _geoglyphs;
        private GameObject[] _prefabs = Array.Empty<GameObject>();
        private Transform _buildingRoot;
        private float _refreshTimer;

        public void Initialize(
            DesertWorldStreamer world,
            ProceduralBuildingSystemTuning settings,
            GeoglyphSystemTuning geoglyphs)
        {
            _world = world;
            _settings = settings;
            _geoglyphs = geoglyphs;
            _prefabs = Resources.LoadAll<GameObject>(_settings.ResourceFolder ?? string.Empty);
            Array.Sort(_prefabs, (left, right) =>
                string.Compare(left != null ? left.name : string.Empty,
                    right != null ? right.name : string.Empty, StringComparison.Ordinal));

            GameObject rootObject = new GameObject("Procedural Buildings");
            rootObject.transform.SetParent(transform, false);
            _buildingRoot = rootObject.transform;

            if (_prefabs.Length == 0)
            {
                Debug.LogWarning(
                    $"No procedural building prefabs were found in Resources/{_settings.ResourceFolder}.",
                    this);
                enabled = false;
                return;
            }

            _world.WorldShifted += HandleWorldShifted;
            Refresh();
        }

        private void Update()
        {
            if (_world == null || _settings == null)
            {
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                Refresh();
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShifted;
            }
        }

        private void Refresh()
        {
            _refreshTimer = Mathf.Max(0.1f, _settings.RefreshInterval);
            if (!_settings.Enabled || _settings.AmountMultiplier <= 0f)
            {
                ClearLoadedCells();
                return;
            }

            float cellSize = Mathf.Max(100f, _settings.PlacementCellSize);
            LogicalPosition player = _world.LogicalPlayerPosition;
            Vector2Int center = new Vector2Int(
                Mathf.FloorToInt((float)(player.X / cellSize)),
                Mathf.FloorToInt((float)(player.Z / cellSize)));
            int radius = Mathf.Clamp(_settings.ActiveCellRadius, 1, 5);

            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int cell = center + new Vector2Int(x, z);
                    if (!_loadedCells.ContainsKey(cell))
                    {
                        _loadedCells.Add(cell, CreateCell(cell, cellSize));
                    }
                }
            }

            int retentionRadius = radius + 1;
            _removalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, GameObject> entry in _loadedCells)
            {
                if (Mathf.Abs(entry.Key.x - center.x) > retentionRadius ||
                    Mathf.Abs(entry.Key.y - center.y) > retentionRadius)
                {
                    _removalBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                Vector2Int cell = _removalBuffer[i];
                GameObject cellObject = _loadedCells[cell];
                if (cellObject != null)
                {
                    Destroy(cellObject);
                }
                _loadedCells.Remove(cell);
            }
        }

        private GameObject CreateCell(Vector2Int cell, float cellSize)
        {
            int buildingCount = CalculateBuildingCount(cell);
            if (buildingCount <= 0)
            {
                return null;
            }

            GameObject cellObject = new GameObject($"Building Cell {cell.x}, {cell.y}");
            cellObject.transform.SetParent(_buildingRoot, false);
            int spawned = 0;
            for (int slot = 0; slot < buildingCount; slot++)
            {
                if (TrySpawnBuilding(cellObject.transform, cell, slot, cellSize))
                {
                    spawned++;
                }
            }

            if (spawned == 0)
            {
                Destroy(cellObject);
                return null;
            }

            return cellObject;
        }

        private int CalculateBuildingCount(Vector2Int cell)
        {
            float expected = Mathf.Clamp01(_settings.BaseCellAmount) *
                Mathf.Clamp(_settings.AmountMultiplier, 0f, 4f);
            int count = Mathf.FloorToInt(expected);
            float remainder = expected - count;
            if (DuneVectorMath.Hash01(cell.x, cell.y, _world.WorldSeed, CountSalt) < remainder)
            {
                count++;
            }
            return count;
        }

        private bool TrySpawnBuilding(Transform cellRoot, Vector2Int cell, int slot, float cellSize)
        {
            float inset = cellSize * Mathf.Clamp(_settings.CellInsetFraction, 0.05f, 0.45f);
            int attempts = Mathf.Clamp(_settings.PlacementAttemptsPerBuilding, 1, 8);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int saltOffset = (slot * 53) + (attempt * 997);
                double logicalX = (cell.x * (double)cellSize) + DuneVectorMath.HashRange(
                    cell.x, cell.y, _world.WorldSeed,
                    PositionXSalt + saltOffset, inset, cellSize - inset);
                double logicalZ = (cell.y * (double)cellSize) + DuneVectorMath.HashRange(
                    cell.x, cell.y, _world.WorldSeed,
                    PositionZSalt + saltOffset, inset, cellSize - inset);

                Vector2 hubOffset = new Vector2(
                    (float)(logicalX - DesertWorldStreamer.StartingLogicalPosition.x),
                    (float)(logicalZ - DesertWorldStreamer.StartingLogicalPosition.y));
                if (hubOffset.sqrMagnitude <
                    _settings.HubExclusionRadius * _settings.HubExclusionRadius)
                {
                    continue;
                }

                if (OverlapsGeoglyph(logicalX, logicalZ))
                {
                    continue;
                }

                Vector3 normal = _world.HeightField.SampleNormal(logicalX, logicalZ);
                float slope = Vector3.Angle(normal, Vector3.up);
                if (slope > Mathf.Clamp(_settings.MaximumPlacementSlope, 0f, 50f))
                {
                    continue;
                }

                int prefabIndex = Mathf.Min(
                    _prefabs.Length - 1,
                    Mathf.FloorToInt(DuneVectorMath.Hash01(
                        cell.x, cell.y, _world.WorldSeed,
                        PrefabSalt + saltOffset) * _prefabs.Length));
                GameObject prefab = _prefabs[prefabIndex];
                if (prefab == null)
                {
                    continue;
                }

                float height = (float)_world.HeightField.SampleHeight(logicalX, logicalZ);
                GameObject placement = new GameObject(
                    $"{prefab.name} [{cell.x}, {cell.y}:{slot}]");
                placement.transform.SetParent(cellRoot, false);
                placement.transform.position = _world.LogicalToLocal(logicalX, height, logicalZ);
                placement.transform.rotation = Quaternion.Euler(0f,
                    DuneVectorMath.HashRange(cell.x, cell.y, _world.WorldSeed,
                        RotationSalt + saltOffset, 0f, 360f), 0f);

                GameObject instance = Instantiate(prefab, placement.transform, false);
                instance.name = prefab.name;
                instance.transform.localPosition = prefab.transform.localPosition;
                instance.transform.localRotation = prefab.transform.localRotation;
                instance.transform.localScale = prefab.transform.localScale;

                GroundToDunes(instance.transform);
                if (_settings.GenerateMeshColliders)
                {
                    AddMissingMeshColliders(instance);
                }
                return true;
            }

            return false;
        }

        private bool OverlapsGeoglyph(double logicalX, double logicalZ)
        {
            return _geoglyphs != null && _geoglyphs.OverlapsArtworkFootprint(
                logicalX,
                logicalZ,
                _settings.GeoglyphClearance);
        }

        private void GroundToDunes(Transform prefab)
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
                    Vector3 prefabCorner = prefab.InverseTransformPoint(worldCorner);
                    lowestRenderedHeight = Mathf.Min(lowestRenderedHeight, worldCorner.y);
                    Vector2 horizontal = new Vector2(prefabCorner.x, prefabCorner.z);
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

            int samplesPerAxis = Mathf.Clamp(_settings.GroundingSamplesPerAxis, 2, 9);
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
                    lowestTerrainHeight = Mathf.Min(lowestTerrainHeight,
                        _world.SampleHeightAtLocal(worldSample.x, worldSample.z));
                }
            }

            if (float.IsFinite(lowestTerrainHeight) && float.IsFinite(lowestRenderedHeight))
            {
                prefab.position += Vector3.up * (lowestTerrainHeight - lowestRenderedHeight -
                    Mathf.Max(0f, _settings.GroundOffsetDown));
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

                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        private void HandleWorldShifted(Vector3 shift)
        {
            if (_buildingRoot != null)
            {
                _buildingRoot.position += shift;
            }
        }

        private void ClearLoadedCells()
        {
            foreach (GameObject cellObject in _loadedCells.Values)
            {
                if (cellObject != null)
                {
                    Destroy(cellObject);
                }
            }
            _loadedCells.Clear();
        }
    }
}
