using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

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
        private const int HueSalt = 17351;

        private const int UntintedHueIndex = -1;

        private static readonly ProfilerMarker BuildSourcesMarker =
            new ProfilerMarker("ProceduralBuildings.BuildSources");
        private static readonly ProfilerMarker BuildCellMarker =
            new ProfilerMarker("ProceduralBuildings.BuildCell");
        private static readonly ProfilerMarker SubmitBatchesMarker =
            new ProfilerMarker("ProceduralBuildings.SubmitBatches");

        private readonly Dictionary<Vector2Int, BuildingCell> _loadedCells =
            new Dictionary<Vector2Int, BuildingCell>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();
        private readonly Dictionary<GameObject, float> _prefabFootprintRadii =
            new Dictionary<GameObject, float>();
        private readonly Dictionary<GameObject, List<BuildingRendererSource>> _prefabSources =
            new Dictionary<GameObject, List<BuildingRendererSource>>();

        // Quantising to the authored palette keeps buildings sharing a hue on the
        // same material, so they still batch instead of becoming one draw each.
        private readonly Dictionary<(int SourceMaterial, int HueIndex), Material> _tintedMaterials =
            new Dictionary<(int, int), Material>();

        private DesertWorldStreamer _world;
        private DuneVectorLandmarkDirector _landmarks;
        private ProceduralBuildingSystemTuning _settings;
        private GeoglyphSystemTuning _geoglyphs;
        private GameObject[] _prefabs = Array.Empty<GameObject>();
        private Transform _buildingRoot;
        private float _refreshTimer;
        private int _hueSettingsHash;
        private int _playerDeploymentOccupancyHandle = DuneVectorWorldOccupancy.InvalidHandle;

        private const int MaximumDeploymentResolutionPasses = 8;

        public void Initialize(
            DesertWorldStreamer world,
            ProceduralBuildingSystemTuning settings,
            GeoglyphSystemTuning geoglyphs,
            DuneVectorLandmarkDirector landmarks)
        {
            _world = world;
            _settings = settings;
            _geoglyphs = geoglyphs;
            _landmarks = landmarks;
            _hueSettingsHash = ComputeHueSettingsHash();
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

            SubmitBatches();
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShifted;
            }

            DuneVectorWorldOccupancy.Release(_playerDeploymentOccupancyHandle);
            _playerDeploymentOccupancyHandle = DuneVectorWorldOccupancy.InvalidHandle;

            DestroyTintedMaterials();
        }

        public LogicalPosition ResolvePlayerDeployment(
            LogicalPosition desiredPosition,
            Vector3 preferredDirection)
        {
            float clearance = _settings != null
                ? Mathf.Max(0f, _settings.PlayerDeploymentClearance)
                : 0f;
            if (clearance <= 0f)
            {
                return desiredPosition;
            }

            Vector2 candidate = new Vector2((float)desiredPosition.X, (float)desiredPosition.Z);
            Vector2 fallbackDirection = new Vector2(preferredDirection.x, preferredDirection.z);
            fallbackDirection = fallbackDirection.sqrMagnitude > Mathf.Epsilon
                ? fallbackDirection.normalized
                : Vector2.right;
            bool adjusted = false;
            for (int pass = 0; pass < MaximumDeploymentResolutionPasses; pass++)
            {
                if (!DuneVectorWorldOccupancy.TryGetNearestOverlap(
                        candidate.x,
                        candidate.y,
                        clearance,
                        WorldOccupancyKind.Structure,
                        out double structureX,
                        out double structureZ,
                        out float structureRadius))
                {
                    break;
                }

                Vector2 structurePosition = new Vector2((float)structureX, (float)structureZ);
                Vector2 delta = candidate - structurePosition;
                Vector2 direction = delta.sqrMagnitude > Mathf.Epsilon
                    ? delta.normalized
                    : fallbackDirection;
                candidate = structurePosition +
                    (direction * (structureRadius + clearance));
                adjusted = true;
            }

            return adjusted
                ? new LogicalPosition(candidate.x, candidate.y)
                : desiredPosition;
        }

        public void ReservePlayerDeployment(LogicalPosition position)
        {
            DuneVectorWorldOccupancy.Release(_playerDeploymentOccupancyHandle);
            _playerDeploymentOccupancyHandle = DuneVectorWorldOccupancy.InvalidHandle;
            float clearance = _settings != null
                ? Mathf.Max(0f, _settings.PlayerDeploymentClearance)
                : 0f;
            if (clearance <= 0f)
            {
                return;
            }

            _playerDeploymentOccupancyHandle = DuneVectorWorldOccupancy.Register(
                position.X,
                position.Z,
                clearance,
                WorldOccupancyKind.PlayerDeployment);
        }

        private void Refresh()
        {
            _refreshTimer = Mathf.Max(0.1f, _settings.RefreshInterval);

            int hueSettingsHash = ComputeHueSettingsHash();
            if (hueSettingsHash != _hueSettingsHash)
            {
                // Cells retain their selected palette index and render buckets retain
                // their generated material. Rebuild both when live WORLD tuning changes.
                ClearLoadedCells();
                DestroyTintedMaterials();
                _hueSettingsHash = hueSettingsHash;
            }

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
                        LogicalPosition cellCenter = new LogicalPosition(
                            (cell.x + 0.5) * cellSize,
                            (cell.y + 0.5) * cellSize);
                        if (!_world.IsVisualTerrainReady(cellCenter))
                        {
                            continue;
                        }
                        _loadedCells.Add(cell, CreateCell(cell, cellSize));
                    }
                }
            }

            int retentionRadius = radius + 1;
            _removalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, BuildingCell> entry in _loadedCells)
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
                DestroyCell(_loadedCells[cell]);
                _loadedCells.Remove(cell);
            }
        }

        // Each placement cell owns its own instance arrays and its own tight world
        // bounds, so a distant cell never drags the whole world into the frustum.
        private BuildingCell CreateCell(Vector2Int cell, float cellSize)
        {
            int buildingCount = CalculateBuildingCount(cell);
            if (buildingCount <= 0)
            {
                return null;
            }

            BuildCellMarker.Begin();
            BuildingCell buildingCell = new BuildingCell();
            int spawned = 0;
            for (int slot = 0; slot < buildingCount; slot++)
            {
                if (TrySpawnBuilding(buildingCell, cell, slot, cellSize))
                {
                    spawned++;
                }
            }
            BuildCellMarker.End();

            if (spawned == 0)
            {
                DestroyCell(buildingCell);
                return null;
            }

            return buildingCell;
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

        private bool TrySpawnBuilding(
            BuildingCell buildingCell, Vector2Int cell, int slot, float cellSize)
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

                if (OverlapsLandmark(logicalX, logicalZ, prefab))
                {
                    continue;
                }

                float footprintRadius = GetPrefabFootprintRadius(prefab);
                if (DuneVectorWorldOccupancy.Overlaps(
                        logicalX,
                        logicalZ,
                        footprintRadius,
                        WorldOccupancyKind.PlayerDeployment))
                {
                    continue;
                }

                if (DuneVectorWorldOccupancy.Overlaps(
                        logicalX,
                        logicalZ,
                        footprintRadius + Mathf.Max(0f, _settings.PortalClearance),
                        WorldOccupancyKind.Portal))
                {
                    continue;
                }

                Vector3 normal = _world.HeightField.SampleNormal(logicalX, logicalZ);
                float slope = Vector3.Angle(normal, Vector3.up);
                if (slope > Mathf.Clamp(_settings.MaximumPlacementSlope, 0f, 50f))
                {
                    continue;
                }

                float height = (float)_world.HeightField.SampleHeight(logicalX, logicalZ);
                Vector3 position = _world.LogicalToLocal(logicalX, height, logicalZ);
                Quaternion rotation = Quaternion.Euler(0f,
                    DuneVectorMath.HashRange(cell.x, cell.y, _world.WorldSeed,
                        RotationSalt + saltOffset, 0f, 360f), 0f);

                List<BuildingRendererSource> sources = GetRendererSources(prefab);
                if (sources.Count == 0)
                {
                    continue;
                }

                // The placement anchor and the prefab root are two separate transforms in
                // the authored hierarchy, so the instance matrix has to keep both.
                Matrix4x4 rootMatrix =
                    Matrix4x4.TRS(position, rotation, Vector3.one) *
                    GetPrefabRootMatrix(prefab);
                rootMatrix = GroundToDunes(rootMatrix, sources);

                int hueIndex = SelectHueIndex(cell, saltOffset);
                AppendBuilding(buildingCell, prefab, sources, rootMatrix, hueIndex);
                buildingCell.TerrainAnchors.Add(new LogicalPosition(logicalX, logicalZ));
                buildingCell.OccupancyHandles.Add(DuneVectorWorldOccupancy.Register(
                    logicalX,
                    logicalZ,
                    footprintRadius,
                    WorldOccupancyKind.Structure));
                return true;
            }

            return false;
        }

        private void AppendBuilding(
            BuildingCell buildingCell,
            GameObject prefab,
            List<BuildingRendererSource> sources,
            Matrix4x4 rootMatrix,
            int hueIndex)
        {
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                BuildingRendererSource source = sources[sourceIndex];
                Matrix4x4 objectToWorld = rootMatrix * source.LocalToPrefabRoot;

                if (_settings.GpuInstancingEnabled)
                {
                    int submeshCount = Mathf.Min(source.Mesh.subMeshCount, source.Materials.Length);
                    for (int submesh = 0; submesh < submeshCount; submesh++)
                    {
                        Material material = GetRenderMaterial(source.Materials[submesh], hueIndex);
                        if (material == null)
                        {
                            continue;
                        }

                        RenderKey key = new RenderKey(
                            source.Mesh,
                            material,
                            submesh,
                            source.Layer,
                            source.RenderingLayerMask,
                            source.ShadowCastingMode,
                            source.ReceiveShadows);

                        if (!buildingCell.Buckets.TryGetValue(key, out List<Matrix4x4> instances))
                        {
                            instances = new List<Matrix4x4>(32);
                            buildingCell.Buckets.Add(key, instances);
                        }
                        instances.Add(objectToWorld);
                    }
                }

                buildingCell.Encapsulate(TransformBounds(objectToWorld, source.LocalBounds));

                if (_settings.GenerateMeshColliders)
                {
                    CreateCollider(buildingCell, source, objectToWorld);
                }
            }

            if (!_settings.GpuInstancingEnabled || _settings.GpuInstancingDebugCompare)
            {
                CreateComparisonInstance(buildingCell, prefab, rootMatrix, hueIndex);
            }
        }

        // GPU instancing cannot carry physics, so colliders keep living as GameObjects.
        private void CreateCollider(
            BuildingCell buildingCell, BuildingRendererSource source, Matrix4x4 objectToWorld)
        {
            Transform colliderRoot = buildingCell.GetColliderRoot(_buildingRoot);
            GameObject colliderObject = new GameObject($"{source.Mesh.name} Collider");
            colliderObject.transform.SetParent(colliderRoot, false);
            ApplyLocalMatrix(colliderObject.transform, objectToWorld);
            MeshCollider collider = colliderObject.AddComponent<MeshCollider>();
            collider.sharedMesh = source.Mesh;
        }

        private void CreateComparisonInstance(
            BuildingCell buildingCell, GameObject prefab, Matrix4x4 rootMatrix, int hueIndex)
        {
            Transform compareRoot = buildingCell.GetComparisonRoot(_buildingRoot);
            GameObject instance = Instantiate(prefab, compareRoot, false);
            instance.name = prefab.name;

            Matrix4x4 offsetMatrix = _settings.GpuInstancingEnabled
                ? Matrix4x4.Translate(_settings.GpuInstancingDebugCompareOffset) * rootMatrix
                : rootMatrix;
            ApplyLocalMatrix(instance.transform, offsetMatrix);
            ApplyHueVariation(instance, hueIndex);

            if (_settings.GenerateMeshColliders && _settings.GpuInstancingEnabled)
            {
                // The instanced path already spawned colliders for this building.
                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Destroy(colliders[i]);
                }
            }
            else if (_settings.GenerateMeshColliders)
            {
                AddMissingMeshColliders(instance);
            }
        }

        private void ApplyLocalMatrix(Transform target, Matrix4x4 objectToWorld)
        {
            Matrix4x4 local = _buildingRoot.worldToLocalMatrix * objectToWorld;
            target.localPosition = local.GetColumn(3);
            target.localRotation = local.rotation;
            target.localScale = local.lossyScale;
        }

        private void SubmitBatches()
        {
            if (!_settings.GpuInstancingEnabled || _loadedCells.Count == 0)
            {
                return;
            }

            SubmitBatchesMarker.Begin();
            int maxPerDraw = Mathf.Clamp(_settings.MaxInstancesPerDraw, 1, 1023);
            LightProbeUsage lightProbeUsage = _settings.InstancedLightProbes
                ? LightProbeUsage.BlendProbes
                : LightProbeUsage.Off;
            ReflectionProbeUsage reflectionProbeUsage = _settings.InstancedReflectionProbes
                ? ReflectionProbeUsage.BlendProbes
                : ReflectionProbeUsage.Off;

            foreach (BuildingCell buildingCell in _loadedCells.Values)
            {
                if (buildingCell == null || !buildingCell.HasBounds)
                {
                    continue;
                }

                bool terrainReady = IsBuildingCellTerrainReady(buildingCell);
                buildingCell.SetAuxiliaryPresentationActive(terrainReady);
                if (!terrainReady)
                {
                    continue;
                }

                foreach (KeyValuePair<RenderKey, List<Matrix4x4>> bucket in buildingCell.Buckets)
                {
                    List<Matrix4x4> instances = bucket.Value;
                    if (instances.Count == 0)
                    {
                        continue;
                    }

                    RenderKey key = bucket.Key;
                    RenderParams renderParams = new RenderParams(key.Material)
                    {
                        camera = null,
                        layer = key.Layer,
                        worldBounds = buildingCell.WorldBounds,
                        renderingLayerMask = key.RenderingLayerMask,
                        shadowCastingMode = key.ShadowCastingMode,
                        receiveShadows = key.ReceiveShadows,
                        lightProbeUsage = lightProbeUsage,
                        reflectionProbeUsage = reflectionProbeUsage,
                    };

                    for (int start = 0; start < instances.Count; start += maxPerDraw)
                    {
                        int count = Mathf.Min(maxPerDraw, instances.Count - start);
                        Graphics.RenderMeshInstanced(
                            in renderParams, key.Mesh, key.SubmeshIndex, instances, count, start);
                    }
                }
            }
            SubmitBatchesMarker.End();
        }

        private bool IsBuildingCellTerrainReady(BuildingCell buildingCell)
        {
            for (int i = 0; i < buildingCell.TerrainAnchors.Count; i++)
            {
                if (!_world.IsVisualTerrainReady(buildingCell.TerrainAnchors[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private List<BuildingRendererSource> GetRendererSources(GameObject prefab)
        {
            if (_prefabSources.TryGetValue(prefab, out List<BuildingRendererSource> cached))
            {
                return cached;
            }

            BuildSourcesMarker.Begin();
            List<BuildingRendererSource> sources = new List<BuildingRendererSource>();
            Transform prefabRoot = prefab.transform;
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                MeshRenderer renderer = filter != null
                    ? filter.GetComponent<MeshRenderer>()
                    : null;
                if (renderer == null || filter.sharedMesh == null)
                {
                    continue;
                }

                sources.Add(new BuildingRendererSource(
                    filter.sharedMesh,
                    renderer.sharedMaterials,
                    prefabRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                    filter.sharedMesh.bounds,
                    renderer.shadowCastingMode,
                    renderer.receiveShadows,
                    renderer.gameObject.layer,
                    renderer.renderingLayerMask));
            }

            _prefabSources[prefab] = sources;
            BuildSourcesMarker.End();
            return sources;
        }

        private int SelectHueIndex(Vector2Int cell, int saltOffset)
        {
            if (!_settings.HueVariationEnabled ||
                _settings.HueVariationStrength <= 0f ||
                _settings.HueTints == null ||
                _settings.HueTints.Length == 0)
            {
                return UntintedHueIndex;
            }

            return Mathf.Clamp(
                Mathf.FloorToInt(DuneVectorMath.Hash01(
                    cell.x, cell.y, _world.WorldSeed, HueSalt + saltOffset) *
                    _settings.HueTints.Length),
                0,
                _settings.HueTints.Length - 1);
        }

        private int ComputeHueSettingsHash()
        {
            unchecked
            {
                int hash = _settings.HueVariationEnabled ? 1 : 0;
                hash = (hash * 397) ^ _settings.HueVariationStrength.GetHashCode();

                Color[] hueTints = _settings.HueTints;
                int tintCount = hueTints != null ? hueTints.Length : 0;
                hash = (hash * 397) ^ tintCount;
                for (int i = 0; i < tintCount; i++)
                {
                    hash = (hash * 397) ^ hueTints[i].GetHashCode();
                }

                return hash;
            }
        }

        private void ApplyHueVariation(GameObject instance, int hueIndex)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Material[] sourceMaterials = renderer.sharedMaterials;
                Material[] tinted = new Material[sourceMaterials.Length];
                for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
                {
                    tinted[materialIndex] =
                        GetRenderMaterial(sourceMaterials[materialIndex], hueIndex);
                }
                renderer.sharedMaterials = tinted;
            }
        }

        // Every building material is routed through this cache so instancing stays on
        // and buildings sharing a hue keep sharing a single material.
        private Material GetRenderMaterial(Material source, int hueIndex)
        {
            if (source == null)
            {
                return null;
            }

            if (hueIndex == UntintedHueIndex && source.enableInstancing)
            {
                return source;
            }

            (int, int) key = (source.GetInstanceID(), hueIndex);
            if (_tintedMaterials.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            Material variant = new Material(source) { name = $"{source.name} (Hue {hueIndex})" };
            variant.enableInstancing = true;

            if (hueIndex != UntintedHueIndex)
            {
                Color tint = Color.Lerp(
                    Color.white,
                    _settings.HueTints[hueIndex],
                    Mathf.Clamp01(_settings.HueVariationStrength));

                string colorProperty =
                    variant.HasProperty("_BaseColor") ? "_BaseColor" :
                    variant.HasProperty("_Color") ? "_Color" : null;
                if (colorProperty != null)
                {
                    Color baseColor = variant.GetColor(colorProperty);
                    variant.SetColor(colorProperty, new Color(
                        baseColor.r * tint.r,
                        baseColor.g * tint.g,
                        baseColor.b * tint.b,
                        baseColor.a));
                }
            }
            else
            {
                variant.name = $"{source.name} (Instanced)";
            }

            _tintedMaterials[key] = variant;
            return variant;
        }

        private bool OverlapsGeoglyph(double logicalX, double logicalZ)
        {
            return _geoglyphs != null && _geoglyphs.OverlapsArtworkFootprint(
                logicalX,
                logicalZ,
                _settings.GeoglyphClearance);
        }

        private bool OverlapsLandmark(double logicalX, double logicalZ, GameObject prefab)
        {
            if (_landmarks == null)
            {
                return false;
            }

            return _landmarks.OverlapsLandmarkFootprint(
                logicalX,
                logicalZ,
                GetPrefabFootprintRadius(prefab) + _settings.LandmarkClearance);
        }

        private float GetPrefabFootprintRadius(GameObject prefab)
        {
            if (_prefabFootprintRadii.TryGetValue(prefab, out float cachedRadius))
            {
                return cachedRadius;
            }

            float radius = 0f;
            Matrix4x4 prefabRootMatrix = GetPrefabRootMatrix(prefab);
            List<BuildingRendererSource> sources = GetRendererSources(prefab);
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                BuildingRendererSource source = sources[sourceIndex];
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 rootCorner = source.LocalToPrefabRoot.MultiplyPoint3x4(
                        BoundsCorner(source.LocalBounds, corner));
                    Vector3 placementCorner = prefabRootMatrix.MultiplyPoint3x4(rootCorner);
                    radius = Mathf.Max(
                        radius,
                        new Vector2(placementCorner.x, placementCorner.z).magnitude);
                }
            }

            _prefabFootprintRadii[prefab] = radius;
            return radius;
        }

        // The prefab root transform is part of every rendered instance. Footprint checks
        // must apply it too or an authored root scale/offset can extend the visible building
        // beyond the radius reserved around its placement anchor.
        private static Matrix4x4 GetPrefabRootMatrix(GameObject prefab)
        {
            Transform prefabTransform = prefab.transform;
            return Matrix4x4.TRS(
                prefabTransform.localPosition,
                prefabTransform.localRotation,
                prefabTransform.localScale);
        }

        // Sinks the building until its lowest rendered corner sits on the lowest terrain
        // sample under its rotated footprint, matching the pre-instancing behaviour.
        private Matrix4x4 GroundToDunes(Matrix4x4 rootMatrix, List<BuildingRendererSource> sources)
        {
            bool hasBounds = false;
            Vector2 minimum = Vector2.zero;
            Vector2 maximum = Vector2.zero;
            float lowestRenderedHeight = float.PositiveInfinity;

            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                BuildingRendererSource source = sources[sourceIndex];
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 localCorner = BoundsCorner(source.LocalBounds, corner);
                    Vector3 rootCorner = source.LocalToPrefabRoot.MultiplyPoint3x4(localCorner);
                    Vector3 worldCorner = rootMatrix.MultiplyPoint3x4(rootCorner);
                    lowestRenderedHeight = Mathf.Min(lowestRenderedHeight, worldCorner.y);

                    Vector2 horizontal = new Vector2(rootCorner.x, rootCorner.z);
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
                return rootMatrix;
            }

            int samplesPerAxis = Mathf.Clamp(_settings.GroundingSamplesPerAxis, 2, 9);
            float lowestTerrainHeight = float.PositiveInfinity;
            for (int z = 0; z < samplesPerAxis; z++)
            {
                float z01 = z / (float)(samplesPerAxis - 1);
                for (int x = 0; x < samplesPerAxis; x++)
                {
                    float x01 = x / (float)(samplesPerAxis - 1);
                    Vector3 worldSample = rootMatrix.MultiplyPoint3x4(new Vector3(
                        Mathf.Lerp(minimum.x, maximum.x, x01),
                        0f,
                        Mathf.Lerp(minimum.y, maximum.y, z01)));
                    lowestTerrainHeight = Mathf.Min(lowestTerrainHeight,
                        _world.SampleHeightAtLocal(worldSample.x, worldSample.z));
                }
            }

            if (!float.IsFinite(lowestTerrainHeight) || !float.IsFinite(lowestRenderedHeight))
            {
                return rootMatrix;
            }

            float sink = lowestTerrainHeight - lowestRenderedHeight -
                Mathf.Max(0f, _settings.GroundOffsetDown);
            return Matrix4x4.Translate(Vector3.up * sink) * rootMatrix;
        }

        private static Vector3 BoundsCorner(Bounds bounds, int corner)
        {
            return bounds.center + Vector3.Scale(
                bounds.extents,
                new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;

            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

            extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
            extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
            extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

            return new Bounds(center, extents * 2f);
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

        // Instance matrices are absolute world space, so a floating-origin shift has to
        // move them alongside the collider and comparison GameObjects.
        private void HandleWorldShifted(Vector3 shift)
        {
            if (_buildingRoot != null)
            {
                _buildingRoot.position += shift;
            }

            foreach (BuildingCell buildingCell in _loadedCells.Values)
            {
                buildingCell?.Translate(shift);
            }
        }

        private void ClearLoadedCells()
        {
            foreach (BuildingCell buildingCell in _loadedCells.Values)
            {
                DestroyCell(buildingCell);
            }
            _loadedCells.Clear();
        }

        private void DestroyTintedMaterials()
        {
            foreach (Material tinted in _tintedMaterials.Values)
            {
                if (tinted != null)
                {
                    Destroy(tinted);
                }
            }
            _tintedMaterials.Clear();
        }

        private void DestroyCell(BuildingCell buildingCell)
        {
            if (buildingCell == null)
            {
                return;
            }

            if (buildingCell.ColliderRoot != null)
            {
                Destroy(buildingCell.ColliderRoot);
            }
            if (buildingCell.ComparisonRoot != null)
            {
                Destroy(buildingCell.ComparisonRoot);
            }
            DuneVectorWorldOccupancy.ReleaseAll(buildingCell.OccupancyHandles);
            buildingCell.Buckets.Clear();
        }

        private readonly struct BuildingRendererSource
        {
            public readonly Mesh Mesh;
            public readonly Material[] Materials;
            public readonly Matrix4x4 LocalToPrefabRoot;
            public readonly Bounds LocalBounds;
            public readonly ShadowCastingMode ShadowCastingMode;
            public readonly bool ReceiveShadows;
            public readonly int Layer;
            public readonly uint RenderingLayerMask;

            public BuildingRendererSource(
                Mesh mesh,
                Material[] materials,
                Matrix4x4 localToPrefabRoot,
                Bounds localBounds,
                ShadowCastingMode shadowCastingMode,
                bool receiveShadows,
                int layer,
                uint renderingLayerMask)
            {
                Mesh = mesh;
                Materials = materials;
                LocalToPrefabRoot = localToPrefabRoot;
                LocalBounds = localBounds;
                ShadowCastingMode = shadowCastingMode;
                ReceiveShadows = receiveShadows;
                Layer = layer;
                RenderingLayerMask = renderingLayerMask;
            }
        }

        private readonly struct RenderKey : IEquatable<RenderKey>
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly int SubmeshIndex;
            public readonly int Layer;
            public readonly uint RenderingLayerMask;
            public readonly ShadowCastingMode ShadowCastingMode;
            public readonly bool ReceiveShadows;

            public RenderKey(
                Mesh mesh,
                Material material,
                int submeshIndex,
                int layer,
                uint renderingLayerMask,
                ShadowCastingMode shadowCastingMode,
                bool receiveShadows)
            {
                Mesh = mesh;
                Material = material;
                SubmeshIndex = submeshIndex;
                Layer = layer;
                RenderingLayerMask = renderingLayerMask;
                ShadowCastingMode = shadowCastingMode;
                ReceiveShadows = receiveShadows;
            }

            public bool Equals(RenderKey other)
            {
                return Mesh == other.Mesh
                    && Material == other.Material
                    && SubmeshIndex == other.SubmeshIndex
                    && Layer == other.Layer
                    && RenderingLayerMask == other.RenderingLayerMask
                    && ShadowCastingMode == other.ShadowCastingMode
                    && ReceiveShadows == other.ReceiveShadows;
            }

            public override bool Equals(object obj)
            {
                return obj is RenderKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Mesh != null ? Mesh.GetInstanceID() : 0;
                    hash = (hash * 397) ^ (Material != null ? Material.GetInstanceID() : 0);
                    hash = (hash * 397) ^ SubmeshIndex;
                    hash = (hash * 397) ^ Layer;
                    hash = (hash * 397) ^ (int)RenderingLayerMask;
                    hash = (hash * 397) ^ (int)ShadowCastingMode;
                    hash = (hash * 397) ^ (ReceiveShadows ? 1 : 0);
                    return hash;
                }
            }
        }

        private sealed class BuildingCell
        {
            public readonly Dictionary<RenderKey, List<Matrix4x4>> Buckets =
                new Dictionary<RenderKey, List<Matrix4x4>>();

            public readonly List<int> OccupancyHandles = new List<int>();
            public readonly List<LogicalPosition> TerrainAnchors = new List<LogicalPosition>();

            public Bounds WorldBounds;
            public bool HasBounds;
            public GameObject ColliderRoot;
            public GameObject ComparisonRoot;

            public void SetAuxiliaryPresentationActive(bool active)
            {
                if (ColliderRoot != null && ColliderRoot.activeSelf != active)
                {
                    ColliderRoot.SetActive(active);
                }
                if (ComparisonRoot != null && ComparisonRoot.activeSelf != active)
                {
                    ComparisonRoot.SetActive(active);
                }
            }

            public void Encapsulate(Bounds bounds)
            {
                if (!HasBounds)
                {
                    WorldBounds = bounds;
                    HasBounds = true;
                    return;
                }

                WorldBounds.Encapsulate(bounds);
            }

            public void Translate(Vector3 shift)
            {
                foreach (List<Matrix4x4> instances in Buckets.Values)
                {
                    for (int i = 0; i < instances.Count; i++)
                    {
                        Matrix4x4 matrix = instances[i];
                        matrix.m03 += shift.x;
                        matrix.m13 += shift.y;
                        matrix.m23 += shift.z;
                        instances[i] = matrix;
                    }
                }

                if (HasBounds)
                {
                    WorldBounds.center += shift;
                }
            }

            public Transform GetColliderRoot(Transform parent)
            {
                if (ColliderRoot == null)
                {
                    ColliderRoot = new GameObject("Building Colliders");
                    ColliderRoot.transform.SetParent(parent, false);
                }
                return ColliderRoot.transform;
            }

            public Transform GetComparisonRoot(Transform parent)
            {
                if (ComparisonRoot == null)
                {
                    ComparisonRoot = new GameObject("Building Prefabs");
                    ComparisonRoot.transform.SetParent(parent, false);
                }
                return ComparisonRoot.transform;
            }
        }
    }
}
