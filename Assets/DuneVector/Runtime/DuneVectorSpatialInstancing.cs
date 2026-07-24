using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    // CPU-submitted backend with persistent spatial cells; future GPU-driven backends share this seam.
    public enum DuneVectorInstanceRenderBackend
    {
        RenderMeshInstanced = 0,
        BatchRendererGroup = 10,
        EntitiesGraphics = 20,
        RenderMeshIndirect = 30,
    }

    [DefaultExecutionOrder(9000)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorSpatialInstancing : MonoBehaviour
    {
        private struct InstanceData
        {
            public Matrix4x4 objectToWorld;
            public uint renderingLayerMask;
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
            public readonly MotionVectorGenerationMode MotionVectorMode;
            public readonly int RendererPriority;

            public RenderKey(Mesh mesh, Material material, int submeshIndex, MeshRenderer renderer)
            {
                Mesh = mesh;
                Material = material;
                SubmeshIndex = submeshIndex;
                Layer = renderer.gameObject.layer;
                RenderingLayerMask = renderer.renderingLayerMask;
                ShadowCastingMode = renderer.shadowCastingMode;
                ReceiveShadows = renderer.receiveShadows;
                MotionVectorMode = renderer.motionVectorGenerationMode;
                RendererPriority = renderer.rendererPriority;
            }

            public bool Equals(RenderKey other)
            {
                return Mesh == other.Mesh && Material == other.Material && SubmeshIndex == other.SubmeshIndex &&
                    Layer == other.Layer && RenderingLayerMask == other.RenderingLayerMask &&
                    ShadowCastingMode == other.ShadowCastingMode && ReceiveShadows == other.ReceiveShadows &&
                    MotionVectorMode == other.MotionVectorMode && RendererPriority == other.RendererPriority;
            }

            public override bool Equals(object obj) => obj is RenderKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Mesh != null ? Mesh.GetEntityId().GetHashCode() : 0;
                    hash = (hash * 397) ^ (Material != null ? Material.GetEntityId().GetHashCode() : 0);
                    hash = (hash * 397) ^ SubmeshIndex;
                    hash = (hash * 397) ^ Layer;
                    hash = (hash * 397) ^ (int)RenderingLayerMask;
                    hash = (hash * 397) ^ (int)ShadowCastingMode;
                    hash = (hash * 397) ^ ReceiveShadows.GetHashCode();
                    hash = (hash * 397) ^ (int)MotionVectorMode;
                    hash = (hash * 397) ^ RendererPriority;
                    return hash;
                }
            }
        }

        private sealed class Source
        {
            public int Handle;
            public Transform Transform;
            public MeshRenderer SourceRenderer;
            public RenderKey Key;
            public Bounds LocalBounds;
            public Matrix4x4 ObjectToWorld;
            public Vector2Int CellKey;
            public bool Dynamic;
            public bool Active;
            public Vector3 DebugOffset;
            public bool UsesDistanceLod;
            public float MinimumLodDistanceSquared;
            public float MaximumLodDistanceSquared;
        }

        private sealed class Cell
        {
            public readonly List<int> SourceHandles = new List<int>(128);
            public readonly List<int> LodSourceHandles = new List<int>(16);
            public readonly Dictionary<RenderKey, Batch> BatchesByKey =
                new Dictionary<RenderKey, Batch>();
            public readonly List<Batch> ActiveBatches = new List<Batch>();
            public readonly Dictionary<RenderKey, Batch> LodBatchesByKey =
                new Dictionary<RenderKey, Batch>();
            public readonly List<Batch> ActiveLodBatches = new List<Batch>();
            public Bounds WorldBounds;
            public bool HasBounds;
            public bool Dirty;
        }

        private sealed class Batch
        {
            public readonly RenderKey Key;
            public readonly List<InstanceData> Instances = new List<InstanceData>(128);
            public RenderParams RenderParams;
            public Bounds WorldBounds;
            public bool HasWorldBounds;

            public Batch(RenderKey key)
            {
                Key = key;
            }
        }

        private static class Markers
        {
            public static readonly ProfilerMarker BuildSources =
                new ProfilerMarker("DuneVectorSpatialInstancing.BuildSources");
            public static readonly ProfilerMarker RefreshTransforms =
                new ProfilerMarker("DuneVectorSpatialInstancing.RefreshTransforms");
            public static readonly ProfilerMarker RebuildDirtyCells =
                new ProfilerMarker("DuneVectorSpatialInstancing.RebuildDirtyCells");
            public static readonly ProfilerMarker SubmitBatches =
                new ProfilerMarker("DuneVectorSpatialInstancing.SubmitBatches");
        }

        private readonly Dictionary<int, Source> _sources = new Dictionary<int, Source>();
        private readonly Dictionary<Vector2Int, Cell> _cells = new Dictionary<Vector2Int, Cell>();
        private readonly List<Cell> _dirtyCells = new List<Cell>();
        private SpatialGpuInstancingTuning _settings;
        private int _nextHandle = 1;
        private bool _forceTransformRefresh;
        private bool _debugComparisonClaimed;
        private bool _lodBatchesDirty = true;
        private bool _hasLodCameraPosition;
        private float _nextLodRefreshTime;
        private Vector3 _lastLodCameraPosition;
        private Camera _lodCamera;

        public static DuneVectorSpatialInstancing Instance { get; private set; }
        public DuneVectorInstanceRenderBackend Backend { get; private set; } =
            DuneVectorInstanceRenderBackend.RenderMeshInstanced;
        public int RegisteredSourceCount => _sources.Count;
        public int SpatialCellCount => _cells.Count;

        public static int MaximumInstancesPerDraw => Instance != null && Instance._settings != null
            ? Mathf.Clamp(Instance._settings.MaximumInstancesPerDraw, 1, 1023)
            : 500;

        public void Initialize(SpatialGpuInstancingTuning settings)
        {
            _settings = settings;
            Backend = DuneVectorInstanceRenderBackend.RenderMeshInstanced;
        }

        public static DuneVectorInstancedVisualGroup Capture(
            GameObject root,
            bool dynamicTransforms,
            PyramidTuning pyramidLodTuning = null)
        {
            if (root == null || Instance == null || Instance._settings == null || !Instance._settings.Enabled)
            {
                if (root != null && pyramidLodTuning != null)
                {
                    DuneVectorInstancedVisualGroup.ShowHighestDetailLodOnly(root);
                }
                return null;
            }

            DuneVectorInstancedVisualGroup group = root.GetComponent<DuneVectorInstancedVisualGroup>();
            if (group == null)
            {
                group = root.AddComponent<DuneVectorInstancedVisualGroup>();
            }
            group.Initialize(dynamicTransforms, pyramidLodTuning);
            return group;
        }

        public static void NotifyAllTransformsChanged()
        {
            if (Instance != null)
            {
                Instance._forceTransformRefresh = true;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        internal bool ClaimDebugComparisonGroup()
        {
            if (_settings == null || !_settings.EnableDebugComparison || _debugComparisonClaimed)
            {
                return false;
            }
            _debugComparisonClaimed = true;
            return true;
        }

        internal void RegisterRenderer(
            MeshRenderer renderer,
            bool dynamicTransforms,
            bool debugComparisonGroup,
            List<int> handles,
            float minimumLodDistance = -1f,
            float maximumLodDistance = -1f)
        {
            if (renderer == null || handles == null)
            {
                return;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return;
            }

            using (Markers.BuildSources.Auto())
            {
                Material[] materials = renderer.sharedMaterials;
                int submeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
                Vector3 debugOffset = debugComparisonGroup ? _settings.DebugComparisonOffset : Vector3.zero;

                for (int submesh = 0; submesh < submeshCount; submesh++)
                {
                    Material material = materials[submesh];
                    if (material == null)
                    {
                        continue;
                    }

                    material.enableInstancing = true;
                    int handle = _nextHandle++;
                    Matrix4x4 objectToWorld = renderer.transform.localToWorldMatrix;
                    if (debugOffset != Vector3.zero)
                    {
                        objectToWorld = Matrix4x4.Translate(debugOffset) * objectToWorld;
                    }
                    Source source = new Source
                    {
                        Handle = handle,
                        Transform = renderer.transform,
                        SourceRenderer = renderer,
                        Key = new RenderKey(mesh, material, submesh, renderer),
                        LocalBounds = mesh.bounds,
                        ObjectToWorld = objectToWorld,
                        CellKey = WorldToCell(objectToWorld.MultiplyPoint3x4(mesh.bounds.center)),
                        Dynamic = dynamicTransforms,
                        Active = renderer.gameObject.activeInHierarchy,
                        DebugOffset = debugOffset,
                        UsesDistanceLod = minimumLodDistance >= 0f && maximumLodDistance >= 0f,
                        MinimumLodDistanceSquared = minimumLodDistance * minimumLodDistance,
                        MaximumLodDistanceSquared = maximumLodDistance * maximumLodDistance,
                    };
                    _sources.Add(handle, source);
                    Cell cell = GetOrCreateCell(source.CellKey);
                    cell.SourceHandles.Add(handle);
                    if (source.UsesDistanceLod)
                    {
                        cell.LodSourceHandles.Add(handle);
                    }
                    handles.Add(handle);
                }

                if (!debugComparisonGroup)
                {
                    renderer.enabled = false;
                    Destroy(renderer);
                    Destroy(filter);
                }
            }
        }

        internal void Unregister(IReadOnlyList<int> handles)
        {
            if (handles == null)
            {
                return;
            }

            for (int i = 0; i < handles.Count; i++)
            {
                int handle = handles[i];
                if (!_sources.TryGetValue(handle, out Source source))
                {
                    continue;
                }

                if (_cells.TryGetValue(source.CellKey, out Cell cell))
                {
                    cell.SourceHandles.Remove(handle);
                    if (source.UsesDistanceLod)
                    {
                        cell.LodSourceHandles.Remove(handle);
                    }
                    MarkCellDirty(cell);
                    if (cell.SourceHandles.Count == 0)
                    {
                        _cells.Remove(source.CellKey);
                    }
                }
                if (source.DebugOffset != Vector3.zero)
                {
                    _debugComparisonClaimed = false;
                }
                _sources.Remove(handle);
            }
        }

        private void LateUpdate()
        {
            if (_settings == null || !_settings.Enabled || Backend != DuneVectorInstanceRenderBackend.RenderMeshInstanced)
            {
                return;
            }

            RefreshTransforms();
            RebuildDirtyCells();
            SubmitBatches();
        }

        private void RefreshTransforms()
        {
            using (Markers.RefreshTransforms.Auto())
            {
                bool refreshAll = _forceTransformRefresh;
                _forceTransformRefresh = false;
                foreach (Source source in _sources.Values)
                {
                    if (!refreshAll && !source.Dynamic)
                    {
                        continue;
                    }

                    bool active = source.Transform != null && source.Transform.gameObject.activeInHierarchy;
                    Matrix4x4 matrix = active ? source.Transform.localToWorldMatrix : source.ObjectToWorld;
                    if (active && source.DebugOffset != Vector3.zero)
                    {
                        matrix = Matrix4x4.Translate(source.DebugOffset) * matrix;
                    }
                    if (active == source.Active && (!active || matrix == source.ObjectToWorld))
                    {
                        continue;
                    }

                    Vector2Int oldCellKey = source.CellKey;
                    source.Active = active;
                    source.ObjectToWorld = matrix;
                    if (active)
                    {
                        source.CellKey = WorldToCell(matrix.MultiplyPoint3x4(source.LocalBounds.center));
                    }

                    if (source.CellKey != oldCellKey)
                    {
                        if (_cells.TryGetValue(oldCellKey, out Cell oldCell))
                        {
                            oldCell.SourceHandles.Remove(source.Handle);
                            if (source.UsesDistanceLod)
                            {
                                oldCell.LodSourceHandles.Remove(source.Handle);
                            }
                            MarkCellDirty(oldCell);
                            if (oldCell.SourceHandles.Count == 0)
                            {
                                _cells.Remove(oldCellKey);
                            }
                        }
                        Cell newCell = GetOrCreateCell(source.CellKey);
                        newCell.SourceHandles.Add(source.Handle);
                        if (source.UsesDistanceLod)
                        {
                            newCell.LodSourceHandles.Add(source.Handle);
                        }
                    }
                    else if (_cells.TryGetValue(source.CellKey, out Cell cell))
                    {
                        MarkCellDirty(cell);
                    }
                }
            }
        }

        private void RebuildDirtyCells()
        {
            using (Markers.RebuildDirtyCells.Auto())
            {
                for (int dirtyIndex = 0; dirtyIndex < _dirtyCells.Count; dirtyIndex++)
                {
                    Cell cell = _dirtyCells[dirtyIndex];

                    for (int i = 0; i < cell.ActiveBatches.Count; i++)
                    {
                        cell.ActiveBatches[i].Instances.Clear();
                    }
                    cell.ActiveBatches.Clear();
                    cell.HasBounds = false;

                    for (int i = 0; i < cell.SourceHandles.Count; i++)
                    {
                        if (!_sources.TryGetValue(cell.SourceHandles[i], out Source source) ||
                            !source.Active ||
                            source.UsesDistanceLod)
                        {
                            continue;
                        }

                        if (!cell.BatchesByKey.TryGetValue(source.Key, out Batch batch))
                        {
                            batch = new Batch(source.Key);
                            cell.BatchesByKey.Add(source.Key, batch);
                        }
                        if (batch.Instances.Count == 0)
                        {
                            cell.ActiveBatches.Add(batch);
                        }
                        batch.Instances.Add(new InstanceData
                        {
                            objectToWorld = source.ObjectToWorld,
                            renderingLayerMask = source.Key.RenderingLayerMask,
                        });

                        Bounds bounds = TransformBounds(source.ObjectToWorld, source.LocalBounds);
                        if (cell.HasBounds)
                        {
                            cell.WorldBounds.Encapsulate(bounds);
                        }
                        else
                        {
                            cell.WorldBounds = bounds;
                            cell.HasBounds = true;
                        }
                    }
                    for (int i = 0; i < cell.ActiveBatches.Count; i++)
                    {
                        Batch batch = cell.ActiveBatches[i];
                        batch.RenderParams = CreateRenderParams(batch.Key, cell.WorldBounds);
                    }
                    cell.Dirty = false;
                }
                _dirtyCells.Clear();
            }
        }

        private void SubmitBatches()
        {
            using (Markers.SubmitBatches.Auto())
            {
                int maximum = MaximumInstancesPerDraw;
                if (_lodCamera == null || !_lodCamera.isActiveAndEnabled)
                {
                    _lodCamera = Camera.main;
                }
                Camera lodCamera = _lodCamera;
                Vector3 lodCameraPosition = lodCamera != null ? lodCamera.transform.position : Vector3.zero;
                bool refreshLods = ShouldRefreshLodBatches(lodCameraPosition, lodCamera != null);
                foreach (Cell cell in _cells.Values)
                {
                    if (cell.HasBounds)
                    {
                        for (int batchIndex = 0; batchIndex < cell.ActiveBatches.Count; batchIndex++)
                        {
                            SubmitBatch(cell.ActiveBatches[batchIndex], maximum);
                        }
                    }

                    if (refreshLods)
                    {
                        PrepareLodBatches(cell, lodCameraPosition, lodCamera != null);
                    }
                    for (int batchIndex = 0; batchIndex < cell.ActiveLodBatches.Count; batchIndex++)
                    {
                        SubmitBatch(cell.ActiveLodBatches[batchIndex], maximum);
                    }
                }
                if (refreshLods)
                {
                    _lodBatchesDirty = false;
                    _hasLodCameraPosition = lodCamera != null;
                    _lastLodCameraPosition = lodCameraPosition;
                    _nextLodRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, _settings.LodRefreshInterval);
                }
            }
        }

        private bool ShouldRefreshLodBatches(Vector3 cameraPosition, bool hasCamera)
        {
            if (_lodBatchesDirty || !_hasLodCameraPosition || !hasCamera)
            {
                return true;
            }
            if (Time.unscaledTime < _nextLodRefreshTime)
            {
                return false;
            }
            float movementThreshold = Mathf.Max(0.1f, _settings.LodCameraMovementThreshold);
            return (cameraPosition - _lastLodCameraPosition).sqrMagnitude >= movementThreshold * movementThreshold;
        }

        private void PrepareLodBatches(Cell cell, Vector3 cameraPosition, bool hasCamera)
        {
            for (int i = 0; i < cell.ActiveLodBatches.Count; i++)
            {
                Batch previousBatch = cell.ActiveLodBatches[i];
                previousBatch.Instances.Clear();
                previousBatch.HasWorldBounds = false;
            }
            cell.ActiveLodBatches.Clear();

            for (int i = 0; i < cell.LodSourceHandles.Count; i++)
            {
                if (!_sources.TryGetValue(cell.LodSourceHandles[i], out Source source) ||
                    !source.Active ||
                    !source.UsesDistanceLod)
                {
                    continue;
                }

                Vector3 center = source.ObjectToWorld.MultiplyPoint3x4(source.LocalBounds.center);
                float distanceSquared = hasCamera ? (center - cameraPosition).sqrMagnitude : 0f;
                if (distanceSquared < source.MinimumLodDistanceSquared ||
                    distanceSquared >= source.MaximumLodDistanceSquared)
                {
                    continue;
                }

                if (!cell.LodBatchesByKey.TryGetValue(source.Key, out Batch batch))
                {
                    batch = new Batch(source.Key);
                    cell.LodBatchesByKey.Add(source.Key, batch);
                }
                if (batch.Instances.Count == 0)
                {
                    cell.ActiveLodBatches.Add(batch);
                }
                batch.Instances.Add(new InstanceData
                {
                    objectToWorld = source.ObjectToWorld,
                    renderingLayerMask = source.Key.RenderingLayerMask,
                });

                Bounds worldBounds = TransformBounds(source.ObjectToWorld, source.LocalBounds);
                if (batch.HasWorldBounds)
                {
                    batch.WorldBounds.Encapsulate(worldBounds);
                }
                else
                {
                    batch.WorldBounds = worldBounds;
                    batch.HasWorldBounds = true;
                }
            }

            for (int i = 0; i < cell.ActiveLodBatches.Count; i++)
            {
                Batch batch = cell.ActiveLodBatches[i];
                batch.RenderParams = CreateRenderParams(batch.Key, batch.WorldBounds);
            }
        }

        private static void SubmitBatch(Batch batch, int maximumInstancesPerDraw)
        {
            List<InstanceData> instances = batch.Instances;
            for (int start = 0; start < instances.Count; start += maximumInstancesPerDraw)
            {
                int count = Mathf.Min(maximumInstancesPerDraw, instances.Count - start);
                Graphics.RenderMeshInstanced(
                    batch.RenderParams,
                    batch.Key.Mesh,
                    batch.Key.SubmeshIndex,
                    instances,
                    count,
                    start);
            }
        }

        private static RenderParams CreateRenderParams(RenderKey key, Bounds worldBounds)
        {
            return new RenderParams(key.Material)
            {
                camera = null,
                layer = key.Layer,
                renderingLayerMask = key.RenderingLayerMask,
                shadowCastingMode = key.ShadowCastingMode,
                receiveShadows = key.ReceiveShadows,
                motionVectorMode = key.MotionVectorMode,
                rendererPriority = key.RendererPriority,
                worldBounds = worldBounds,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };
        }

        private Cell GetOrCreateCell(Vector2Int key)
        {
            if (!_cells.TryGetValue(key, out Cell cell))
            {
                cell = new Cell();
                _cells.Add(key, cell);
            }
            MarkCellDirty(cell);
            return cell;
        }

        private void MarkCellDirty(Cell cell)
        {
            _lodBatchesDirty = true;
            if (cell == null || cell.Dirty)
            {
                return;
            }
            cell.Dirty = true;
            _dirtyCells.Add(cell);
        }

        private Vector2Int WorldToCell(Vector3 position)
        {
            float size = Mathf.Max(8f, _settings.CellSizeMeters);
            return new Vector2Int(Mathf.FloorToInt(position.x / size), Mathf.FloorToInt(position.z / size));
        }

        public static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            extents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, extents * 2f);
        }

        private void OnDestroy()
        {
            foreach (Source source in _sources.Values)
            {
                if (source.SourceRenderer != null)
                {
                    source.SourceRenderer.enabled = true;
                }
            }
            _sources.Clear();
            _cells.Clear();
            _dirtyCells.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorInstancedVisualGroup : MonoBehaviour
    {
        private readonly List<int> _handles = new List<int>();
        private bool _initialized;

        internal void Initialize(bool dynamicTransforms, PyramidTuning pyramidLodTuning)
        {
            if (_initialized || DuneVectorSpatialInstancing.Instance == null)
            {
                return;
            }

            _initialized = true;
            bool debugComparisonGroup = DuneVectorSpatialInstancing.Instance.ClaimDebugComparisonGroup();
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            Dictionary<MeshRenderer, int> lodIndices = BuildLodIndices(renderers);
            SetHighestDetailLodOnly(renderers, lodIndices);
            for (int i = 0; i < renderers.Length; i++)
            {
                float minimumLodDistance = -1f;
                float maximumLodDistance = -1f;
                if (pyramidLodTuning != null &&
                    lodIndices.TryGetValue(renderers[i], out int lodIndex))
                {
                    GetLodDistanceRange(
                        pyramidLodTuning,
                        lodIndex,
                        out minimumLodDistance,
                        out maximumLodDistance);
                }
                DuneVectorSpatialInstancing.Instance.RegisterRenderer(
                    renderers[i],
                    dynamicTransforms,
                    debugComparisonGroup,
                    _handles,
                    minimumLodDistance,
                    maximumLodDistance);
            }
        }

        internal static void ShowHighestDetailLodOnly(GameObject root)
        {
            if (root == null)
            {
                return;
            }
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            SetHighestDetailLodOnly(renderers, BuildLodIndices(renderers));
        }

        private static void SetHighestDetailLodOnly(
            IReadOnlyList<MeshRenderer> renderers,
            IReadOnlyDictionary<MeshRenderer, int> lodIndices)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer != null && lodIndices.TryGetValue(renderer, out int lodIndex))
                {
                    renderer.enabled = lodIndex == 0;
                }
            }
        }

        private static Dictionary<MeshRenderer, int> BuildLodIndices(
            IReadOnlyList<MeshRenderer> renderers)
        {
            Dictionary<MeshRenderer, int> lodNumbers = new Dictionary<MeshRenderer, int>();
            int firstLodNumber = int.MaxValue;
            for (int i = 0; i < renderers.Count; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer != null && TryFindLodNumber(renderer, out int lodNumber))
                {
                    lodNumbers.Add(renderer, lodNumber);
                    firstLodNumber = Mathf.Min(firstLodNumber, lodNumber);
                }
            }

            if (lodNumbers.Count > 0)
            {
                MeshRenderer[] unresolvedRenderers = GetUnresolvedRenderers(renderers, lodNumbers);
                if (unresolvedRenderers.Length > 0)
                {
                    Array.Sort(unresolvedRenderers, CompareRendererDetailDescending);
                    for (int i = 0; i < unresolvedRenderers.Length; i++)
                    {
                        lodNumbers.Add(unresolvedRenderers[i], firstLodNumber + i);
                    }
                }

                Dictionary<MeshRenderer, int> explicitIndices =
                    new Dictionary<MeshRenderer, int>(lodNumbers.Count);
                foreach (KeyValuePair<MeshRenderer, int> pair in lodNumbers)
                {
                    explicitIndices.Add(pair.Key, Mathf.Max(0, pair.Value - firstLodNumber));
                }
                return explicitIndices;
            }

            if (renderers.Count <= 1)
            {
                return new Dictionary<MeshRenderer, int>();
            }

            MeshRenderer[] detailSortedRenderers = new MeshRenderer[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                detailSortedRenderers[i] = renderers[i];
            }
            Array.Sort(detailSortedRenderers, CompareRendererDetailDescending);

            Dictionary<MeshRenderer, int> inferredIndices =
                new Dictionary<MeshRenderer, int>(detailSortedRenderers.Length);
            for (int i = 0; i < detailSortedRenderers.Length; i++)
            {
                if (detailSortedRenderers[i] != null)
                {
                    inferredIndices.Add(detailSortedRenderers[i], i);
                }
            }
            return inferredIndices;
        }

        private static MeshRenderer[] GetUnresolvedRenderers(
            IReadOnlyList<MeshRenderer> renderers,
            IReadOnlyDictionary<MeshRenderer, int> resolvedRenderers)
        {
            List<MeshRenderer> unresolved = new List<MeshRenderer>();
            for (int i = 0; i < renderers.Count; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer != null && !resolvedRenderers.ContainsKey(renderer))
                {
                    unresolved.Add(renderer);
                }
            }
            return unresolved.ToArray();
        }

        private static int CompareRendererDetailDescending(
            MeshRenderer left,
            MeshRenderer right)
        {
            return GetRendererVertexCount(right).CompareTo(GetRendererVertexCount(left));
        }

        private static int GetRendererVertexCount(MeshRenderer renderer)
        {
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : 0;
        }

        private static bool TryFindLodNumber(MeshRenderer renderer, out int lodNumber)
        {
            lodNumber = -1;
            Transform current = renderer != null ? renderer.transform : null;
            while (current != null)
            {
                if (TryParseLodNumber(current.name, out lodNumber))
                {
                    return true;
                }
                current = current.parent;
            }

            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return filter != null &&
                filter.sharedMesh != null &&
                TryParseLodNumber(filter.sharedMesh.name, out lodNumber);
        }

        private static bool TryParseLodNumber(string objectName, out int lodNumber)
        {
            lodNumber = -1;
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            int markerIndex = objectName.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            int digitStart = markerIndex + 4;
            int digitEnd = digitStart;
            while (digitEnd < objectName.Length && char.IsDigit(objectName[digitEnd]))
            {
                digitEnd++;
            }
            return digitEnd > digitStart &&
                int.TryParse(objectName.Substring(digitStart, digitEnd - digitStart), out lodNumber) &&
                lodNumber >= 0;
        }

        private static void GetLodDistanceRange(
            PyramidTuning tuning,
            int lodIndex,
            out float minimumDistance,
            out float maximumDistance)
        {
            float lod1Maximum = Mathf.Max(0.1f, tuning.Lod1MaximumDistance);
            float lod2Maximum = Mathf.Max(lod1Maximum, tuning.Lod2MaximumDistance);
            float lod3Maximum = Mathf.Max(lod2Maximum, tuning.Lod3MaximumDistance);
            float lod4Maximum = Mathf.Max(lod3Maximum, tuning.Lod4MaximumDistance);

            switch (lodIndex)
            {
                case 0:
                    minimumDistance = 0f;
                    maximumDistance = lod1Maximum;
                    break;
                case 1:
                    minimumDistance = lod1Maximum;
                    maximumDistance = lod2Maximum;
                    break;
                case 2:
                    minimumDistance = lod2Maximum;
                    maximumDistance = lod3Maximum;
                    break;
                default:
                    minimumDistance = lod3Maximum;
                    maximumDistance = lod4Maximum;
                    break;
            }
        }

        private void OnDestroy()
        {
            if (DuneVectorSpatialInstancing.Instance != null)
            {
                DuneVectorSpatialInstancing.Instance.Unregister(_handles);
            }
            _handles.Clear();
        }
    }
}
