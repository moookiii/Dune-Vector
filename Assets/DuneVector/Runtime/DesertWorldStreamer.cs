using System;
using System.Collections.Generic;
using System.IO;
using KinematicCharacterController;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class DesertWorldStreamer : MonoBehaviour
    {
        private const string FlightRingProgressSaveFileName = "DuneVectorFlightRingProgress.dat";
        private static readonly int VolumetricCloudsWorldOriginOffsetId =
            Shader.PropertyToID("_VolumetricCloudsWorldOriginOffset");
        private static readonly int MassiveCloudsWorldOriginOffsetId =
            Shader.PropertyToID("_MassiveCloudsWorldOriginOffset");

        [Serializable]
        private sealed class FlightRingProgressSaveData
        {
            public int Version = 1;
            public string[] ActivatedRingIdentities = new string[0];
        }

        private sealed class ContractGroundExploderSpawn
        {
            public Vector2Int ChunkCoordinate;
            public Transform Root;
            public GroundExploderEnemy Enemy;
        }

        [Header("World")]
        public int WorldSeed = 19770503;
        [Min(24f)] public float ChunkSize = 80f;
        [Range(8, 96)] public int ChunkResolution = 32;
        [Range(1, 14)] public int ActiveRadius = 3;
        [Range(1, 9)] public int PreloadRadius = 3;
        [Range(0f, 6f)] public float ContentPreloadPredictionSeconds = 3f;
        [Range(2, 12)] public int UnloadRadius = 4;
        [Min(0.05f)] public float RefreshInterval = 0.18f;
        [Range(1, 48)] public int ChunksGeneratedPerFrame = 1;
        [Range(0.25f, 8f)] public float GenerationTimeBudgetMilliseconds = 1.25f;
        public bool EnableCameraFrustumTerrainStreaming = true;
        [Min(0f)] public float CameraFrustumMinimumAltitude = 18f;
        [Min(0.01f)] public float CameraFrustumFullDistanceAltitude = 140f;
        [Min(1f)] public float CameraFrustumMinimumDistance = 480f;
        [Min(1f)] public float CameraFrustumMaximumDistance = 1200f;
        [Range(0, 3)] public int CameraFrustumPaddingChunks = 1;
        [Range(0, 4)] public int CameraFrustumUnloadPaddingChunks = 1;
        [Min(0f)] public float CameraFrustumTerrainHeightPadding = 24f;
        [Range(16, 512)] public int MaximumCameraFrustumTerrainChunks = 192;
        [Range(0f, 5f)] public float CollisionPredictionSeconds = 2.5f;
        [Range(0, 2)] public int CollisionPreloadRadius = 1;
        [Range(1, 4)] public int CollisionActiveRadius = 2;
        [Range(1, 4)] public int SimulationRadius = 3;
        [Range(8, 64)] public int CollisionMeshResolution = 24;
        public bool EnableTerrainDistanceLod = true;
        [Range(1, 8)] public int TerrainFullDetailChunkRadius = 3;
        [Range(4, 48)] public int DistantTerrainResolution = 12;
        [Min(50f)] public float FloatingOriginThreshold = 520f;

        [Header("Dunes")]
        public DuneFieldSettings Dunes = new DuneFieldSettings();

        [Header("Clouds")]
        public CloudTuning Clouds;

        [Header("Desert Shrubs")]
        public DesertShrubTuning Shrubs;
        public LandmarkSystemTuning Landmarks;
        public CactusTuning Cacti;
        public PyramidTuning Obelisks;
        public PyramidTuning DarkPyramids;
        public PyramidTuning Pyramid2;
        public GeoglyphSystemTuning Geoglyphs;

        [Header("Spawning - expected count per chunk")]
        [Min(0f)] public float PyramidDensity = 0.22f;
        [Min(0.1f)] public float PyramidMinimumScale = 2f;
        [Min(0.1f)] public float PyramidMaximumScale = 4.4f;
        [Range(0f, 89f)] public float PyramidMaximumPlacementSlope = 24f;
        [Min(0f)] public float PyramidMinimumBurialDepth = 0.75f;
        [Min(0f)] public float PyramidMaximumBurialDepth = 1.25f;

        [Header("Ring Sizes")]
        public RingTuning Rings = new RingTuning();

        [Header("Ground Explosive Enemies")]
        public GroundExploderTuning GroundExploders = new GroundExploderTuning();

        [Header("Debug")]
        public bool DrawChunkBounds;

        public DuneHeightField HeightField { get; private set; }
        public int EnemySpawnSeed { get; private set; }
        public int ActiveChunkCount => _chunks.Count;
        public int GeneratedChunkCount { get; private set; }
        public int UnloadedChunkCount { get; private set; }
        public int PeakActiveChunkCount { get; private set; }
        public int RebaseCount { get; private set; }
        public int ActivatedFlightRingCount => _activatedFlightRingIdentities.Count;
        public int UpperFlightRingRequiredPasses => Mathf.Max(1, Rings.UpperFlightRingRequiredPasses);
        public bool IsUpperFlightRingUnlocked => ActivatedFlightRingCount >= UpperFlightRingRequiredPasses;
        public Vector2Int CurrentLogicalChunk { get; private set; }
        public double OriginOffsetX { get; private set; }
        public double OriginOffsetZ { get; private set; }
        public LogicalPosition LogicalPlayerPosition
        {
            get
            {
                if (_motor == null)
                {
                    return new LogicalPosition(OriginOffsetX, OriginOffsetZ);
                }
                return new LogicalPosition(OriginOffsetX + _motor.TransientPosition.x, OriginOffsetZ + _motor.TransientPosition.z);
            }
        }

        public static readonly Vector2 StartingLogicalPosition = new Vector2(10f, 8f);

        /// <summary>
        /// Pushes a desert deployment point out of every ring, portal, and cactus it lands on.
        /// Used by both contract insertions and free roam deployments.
        /// </summary>
        public LogicalPosition ResolvePlayerSpawnAwayFromObstacles(
            LogicalPosition desiredPosition,
            Vector3 preferredDirection)
        {
            float clearance = Rings != null
                ? Mathf.Max(0f, Rings.MinimumDroneSpawnSeparation)
                : 0f;
            float cactusClearance = Cacti != null
                ? Mathf.Max(0f, Cacti.DroneSpawnClearance)
                : 0f;
            if (TraversalRing.ActiveRings.Count == 0 && cactusClearance <= 0f)
            {
                return desiredPosition;
            }

            Vector2 candidate = new Vector2((float)desiredPosition.X, (float)desiredPosition.Z);
            Vector2 fallbackDirection = new Vector2(preferredDirection.x, preferredDirection.z);
            fallbackDirection = fallbackDirection.sqrMagnitude > Mathf.Epsilon
                ? fallbackDirection.normalized
                : Vector2.right;
            bool adjusted = false;
            int maximumPasses = TraversalRing.ActiveRings.Count + 1;
            for (int pass = 0; pass < maximumPasses; pass++)
            {
                bool moved = false;
                foreach (TraversalRing ring in TraversalRing.ActiveRings)
                {
                    if (ring == null || !ring.isActiveAndEnabled)
                    {
                        continue;
                    }

                    Vector3 ringWorldPosition = ring.transform.position;
                    Vector2 ringLogicalPosition = new Vector2(
                        (float)(OriginOffsetX + ringWorldPosition.x),
                        (float)(OriginOffsetZ + ringWorldPosition.z));
                    Vector2 delta = candidate - ringLogicalPosition;
                    float ringClearance = Mathf.Max(clearance, ring.InnerRadius);
                    if (delta.sqrMagnitude >= ringClearance * ringClearance)
                    {
                        continue;
                    }

                    Vector2 direction = delta.sqrMagnitude > Mathf.Epsilon
                        ? delta.normalized
                        : fallbackDirection;
                    candidate = ringLogicalPosition + (direction * ringClearance);
                    moved = true;
                    adjusted = true;
                }

                if (!moved)
                {
                    break;
                }
            }

            // Cacti are solid meshes the drone would materialise inside, and they are placed
            // deterministically per chunk, so this resolves even against chunks that have not
            // streamed in yet at the moment of deployment.
            for (int pass = 0; pass < MaximumCactusSpawnPushPasses; pass++)
            {
                if (!TryFindCactusNear(candidate, cactusClearance, out Vector2 cactusPosition))
                {
                    break;
                }

                Vector2 delta = candidate - cactusPosition;
                Vector2 direction = delta.sqrMagnitude > Mathf.Epsilon
                    ? delta.normalized
                    : fallbackDirection;
                candidate = cactusPosition + (direction * cactusClearance);
                adjusted = true;
            }

            return adjusted
                ? new LogicalPosition(candidate.x, candidate.y)
                : desiredPosition;
        }

        /// <summary>
        /// Finds the nearest cactus within <paramref name="clearance"/> of a logical point by
        /// replaying the deterministic cactus placement for every chunk the disc touches.
        /// </summary>
        private bool TryFindCactusNear(Vector2 logicalPoint, float clearance, out Vector2 cactusPosition)
        {
            cactusPosition = Vector2.zero;
            if (clearance <= 0f || HeightField == null || ChunkSize <= 0f)
            {
                return false;
            }

            int chunkReach = Mathf.CeilToInt(clearance / ChunkSize);
            Vector2Int centerChunk = new Vector2Int(
                Mathf.FloorToInt(logicalPoint.x / ChunkSize),
                Mathf.FloorToInt(logicalPoint.y / ChunkSize));
            float closestSquared = clearance * clearance;
            bool found = false;
            for (int z = -chunkReach; z <= chunkReach; z++)
            {
                for (int x = -chunkReach; x <= chunkReach; x++)
                {
                    Vector2Int coordinate = new Vector2Int(centerChunk.x + x, centerChunk.y + z);
                    DesertChunk.CollectCactusPlacements(
                        coordinate,
                        ChunkSize,
                        HeightField,
                        WorldSeed,
                        Cacti,
                        _cactusPlacementQueryBuffer);
                    for (int i = 0; i < _cactusPlacementQueryBuffer.Count; i++)
                    {
                        Vector2 logicalCactus = new Vector2(
                            (coordinate.x * ChunkSize) + _cactusPlacementQueryBuffer[i].Local.x,
                            (coordinate.y * ChunkSize) + _cactusPlacementQueryBuffer[i].Local.y);
                        float distanceSquared = (logicalCactus - logicalPoint).sqrMagnitude;
                        if (distanceSquared >= closestSquared)
                        {
                            continue;
                        }

                        closestSquared = distanceSquared;
                        cactusPosition = logicalCactus;
                        found = true;
                    }
                }
            }

            return found;
        }

        // Iteration cap for nudging a deployment point out of overlapping cacti; each pass moves
        // the point clear of the nearest one, and a handful is enough to settle inside a cluster.
        private const int MaximumCactusSpawnPushPasses = 8;

        private readonly List<CactusPlacement> _cactusPlacementQueryBuffer = new List<CactusPlacement>();

        public event Action<Vector3> WorldShifted;

        private readonly Dictionary<Vector2Int, DesertChunk> _chunks = new Dictionary<Vector2Int, DesertChunk>();
        private readonly Queue<Vector2Int> _generationQueue = new Queue<Vector2Int>();
        private readonly HashSet<Vector2Int> _queuedCoordinates = new HashSet<Vector2Int>();
        private readonly Queue<Vector2Int> _contentCompletionQueue = new Queue<Vector2Int>();
        private readonly Queue<Vector2Int> _collisionQueue = new Queue<Vector2Int>();
        private readonly HashSet<Vector2Int> _queuedCollisionCoordinates = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _candidateCoordinates = new List<Vector2Int>();
        private readonly List<Vector2Int> _frustumCandidateCoordinates = new List<Vector2Int>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> _desiredContentCoordinates = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _desiredVisualCoordinates = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _retainedVisualCoordinates = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _cameraPriorityCoordinates = new HashSet<Vector2Int>();
        private readonly Plane[] _streamingFrustumPlanes = new Plane[6];

        /// <summary>
        /// Unpadded logical height range per chunk coordinate. The height field is static, so these
        /// samples never change and are kept rather than re-sampled every streaming pass. Logical
        /// heights are origin-independent, so a floating-origin shift does not invalidate them.
        /// </summary>
        private const int ChunkHeightCacheCapacity = 32768;

        private readonly Dictionary<Vector2Int, Vector2> _chunkHeightRangeCache =
            new Dictionary<Vector2Int, Vector2>();
        private readonly HashSet<string> _activatedFlightRingIdentities = new HashSet<string>();
        private readonly List<ContractGroundExploderSpawn> _contractGroundExploders = new List<ContractGroundExploderSpawn>();
        private Vector2Int _candidateSortCenter;
        private Vector2 _frustumSortCenter;
        private Vector2Int _predictedCollisionChunk;
        private bool _hasPredictedCollisionChunk;

        private DuneVectorMaterials _materials;
        private Transform _chunkRoot;
        private DroneCharacterController _player;
        private DroneHealth _playerHealth;
        private DroneGoldWallet _goldWallet;
        private DuneVectorGoldHUD _goldHUD;
        private KinematicCharacterMotor _motor;
        private DroneCameraController _camera;
        private Vector2Int _lastScheduledChunk = new Vector2Int(int.MinValue, int.MinValue);
        private float _streamingRefreshTimer;
        private int _coinRingSeed;
        private string _flightRingProgressSavePath;
        private bool _initialized;

        internal static class Markers
        {
            public static readonly ProfilerMarker StreamingUpdate =
                new ProfilerMarker("DuneVector.Streaming.Update");
            public static readonly ProfilerMarker CollisionChunk =
                new ProfilerMarker("DuneVector.Streaming.CollisionChunk");
            public static readonly ProfilerMarker VisualChunk =
                new ProfilerMarker("DuneVector.Streaming.VisualChunk");
            public static readonly ProfilerMarker CompleteChunk =
                new ProfilerMarker("DuneVector.Streaming.CompleteChunk");
            public static readonly ProfilerMarker TerrainMesh =
                new ProfilerMarker("DuneVector.Streaming.TerrainMesh");
            public static readonly ProfilerMarker ColliderAssignment =
                new ProfilerMarker("DuneVector.Streaming.ColliderAssignment");
            public static readonly ProfilerMarker ChunkContent =
                new ProfilerMarker("DuneVector.Streaming.ChunkContent");
        }

        public void Initialize(DuneVectorMaterials materials)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            DuneVectorWorldOccupancy.Clear();
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _materials.SetTerrainLogicalOrigin(OriginOffsetX, OriginOffsetZ);
            SetVolumetricCloudsLogicalOrigin();
            _coinRingSeed = Guid.NewGuid().GetHashCode();
            EnemySpawnSeed = Guid.NewGuid().GetHashCode();
            Rings ??= new RingTuning();
            _flightRingProgressSavePath = Path.Combine(
                Application.persistentDataPath,
                FlightRingProgressSaveFileName);
            if (!DuneTrainingRuntime.Enabled)
            {
                LoadFlightRingProgress();
            }
            GroundExploders ??= new GroundExploderTuning();
            Dunes.WorldSeed = WorldSeed;
            HeightField = new DuneHeightField(Dunes);
            _chunkHeightRangeCache.Clear();

            GameObject root = new GameObject("Streamed Desert Chunks");
            _chunkRoot = root.transform;
            _chunkRoot.SetParent(transform, false);
            DuneVectorStreamedSimulation simulation = gameObject.AddComponent<DuneVectorStreamedSimulation>();
            simulation.Initialize(this);

            DuneVectorUpperFlightRingHUD progressHud = gameObject.AddComponent<DuneVectorUpperFlightRingHUD>();
            progressHud.Initialize(this, Rings);

            Vector2Int initialChunk = LogicalToChunk(StartingLogicalPosition.x, StartingLogicalPosition.y);
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    GenerateChunkFullyImmediate(initialChunk + new Vector2Int(x, z));
                }
            }
            CurrentLogicalChunk = initialChunk;
        }

        public void BindPlayer(DroneCharacterController player, DroneCameraController camera, DroneHealth playerHealth = null)
        {
            _player = player;
            _playerHealth = playerHealth;
            if (player != null)
            {
                _goldWallet = player.GetComponent<DroneGoldWallet>();
                if (_goldWallet == null)
                {
                    _goldWallet = player.gameObject.AddComponent<DroneGoldWallet>();
                }
                _goldWallet.Initialize(Rings.StartingGold);
                if (_goldHUD == null)
                {
                    _goldHUD = gameObject.AddComponent<DuneVectorGoldHUD>();
                }
                _goldHUD.Initialize(_goldWallet, Rings);
            }
            _motor = player != null ? player.Motor : null;
            _camera = camera;
            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.BindPlayer(player, playerHealth);
            }
            ScheduleStreaming(force: true);
        }

        public float SampleHeightAtLocal(float localX, float localZ)
        {
            return (float)HeightField.SampleHeight(OriginOffsetX + localX, OriginOffsetZ + localZ);
        }

        public Vector3 SampleNormalAtLocal(float localX, float localZ)
        {
            return HeightField.SampleNormal(OriginOffsetX + localX, OriginOffsetZ + localZ);
        }

        public Vector3 LogicalToLocal(double logicalX, double height, double logicalZ)
        {
            return new Vector3((float)(logicalX - OriginOffsetX), (float)height, (float)(logicalZ - OriginOffsetZ));
        }

        public bool IsVisualTerrainReady(LogicalPosition logicalPosition)
        {
            Vector2Int coordinate = LogicalToChunk(logicalPosition.X, logicalPosition.Z);
            return _chunks.TryGetValue(coordinate, out DesertChunk chunk) && chunk.IsTerrainVisible;
        }

        public void SetContractGroundExploders(int count, float minimumDistance, float maximumDistance, int seed)
        {
            ClearContractGroundExploders();
            if (DuneTrainingRuntime.ControlledPreHazardStage || count <= 0 || GroundExploders == null || !GroundExploders.Enabled ||
                _player == null || _playerHealth == null || HeightField == null || _chunkRoot == null)
            {
                return;
            }

            float explosionRadius = GroundExploders.EvaluateExplosionRadius(DuneVectorContractRisk.CurrentRisk);
            float minimum = DuneVectorEnemySpawnClearance.ApplyMinimumDistance(
                Mathf.Max(GroundExploders.DetectionRadius + explosionRadius, minimumDistance));
            float maximum = Mathf.Max(minimum, maximumDistance);
            LogicalPosition playerLogical = LogicalPlayerPosition;
            int spawned = 0;
            int attempts = Mathf.Max(count * 12, count);
            for (int attempt = 0; attempt < attempts && spawned < count; attempt++)
            {
                float angle = DuneVectorMath.HashRange(seed, attempt, EnemySpawnSeed, 1249, 0f, Mathf.PI * 2f);
                float distance = DuneVectorMath.HashRange(seed, attempt, EnemySpawnSeed, 1259, minimum, maximum);
                double logicalX = playerLogical.X + (Math.Cos(angle) * distance);
                double logicalZ = playerLogical.Z + (Math.Sin(angle) * distance);
                Vector2Int coordinate = LogicalToChunk(logicalX, logicalZ);
                double chunkLogicalX = coordinate.x * (double)ChunkSize;
                double chunkLogicalZ = coordinate.y * (double)ChunkSize;
                Vector2 local = new Vector2(
                    (float)(logicalX - chunkLogicalX),
                    (float)(logicalZ - chunkLogicalZ));
                if (local.x < 5f || local.x > ChunkSize - 5f || local.y < 5f || local.y > ChunkSize - 5f)
                {
                    continue;
                }
                Vector3 normal = HeightField.SampleNormal(logicalX, logicalZ);
                if (Vector3.Angle(normal, Vector3.up) > GroundExploders.MaximumGroundSlope)
                {
                    continue;
                }

                GameObject rootObject = new GameObject($"High-Value Ground Threat [{coordinate.x}, {coordinate.y}]");
                Transform root = rootObject.transform;
                root.SetParent(_chunkRoot, false);
                RepositionContractGroundRoot(root, coordinate);

                GameObject enemyObject = new GameObject($"High-Value Ground Exploder {spawned + 1:00}");
                enemyObject.transform.SetParent(root, false);
                enemyObject.transform.localPosition = new Vector3(
                    local.x,
                    (float)HeightField.SampleHeight(logicalX, logicalZ),
                    local.y);
                float yaw = DuneVectorMath.HashRange(seed, attempt, EnemySpawnSeed, 1277, 0f, 360f);
                enemyObject.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                GroundExploderEnemy enemy = enemyObject.AddComponent<GroundExploderEnemy>();
                enemy.Initialize(
                    _player,
                    _playerHealth,
                    HeightField,
                    chunkLogicalX,
                    chunkLogicalZ,
                    ChunkSize,
                    _materials,
                    GroundExploders,
                    unchecked((int)DuneVectorMath.Hash(seed, attempt, EnemySpawnSeed, 1289)));
                _contractGroundExploders.Add(new ContractGroundExploderSpawn
                {
                    ChunkCoordinate = coordinate,
                    Root = root,
                    Enemy = enemy,
                });
                spawned++;
            }
        }

        /// <summary>
        /// Removes already-streamed ground enemies that sit inside the protected radius around the
        /// drone's desert deployment point. Chunks streamed afterwards skip the radius while placing.
        /// </summary>
        public void RemoveEnemiesInsidePlayerSpawnClearance()
        {
            if (!DuneVectorEnemySpawnClearance.HasSpawnPoint)
            {
                return;
            }

            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.RemoveGroundExplodersInsidePlayerSpawnClearance();
            }

            for (int i = _contractGroundExploders.Count - 1; i >= 0; i--)
            {
                ContractGroundExploderSpawn spawn = _contractGroundExploders[i];
                if (spawn.Enemy == null)
                {
                    continue;
                }

                Vector3 local = spawn.Enemy.transform.localPosition;
                double logicalX = (spawn.ChunkCoordinate.x * (double)ChunkSize) + local.x;
                double logicalZ = (spawn.ChunkCoordinate.y * (double)ChunkSize) + local.z;
                if (!DuneVectorEnemySpawnClearance.IsBlocked(logicalX, logicalZ))
                {
                    continue;
                }

                if (spawn.Root != null)
                {
                    Destroy(spawn.Root.gameObject);
                }
                _contractGroundExploders.RemoveAt(i);
            }
        }

        public void ClearContractGroundExploders()
        {
            for (int i = 0; i < _contractGroundExploders.Count; i++)
            {
                ContractGroundExploderSpawn spawn = _contractGroundExploders[i];
                if (spawn.Root != null)
                {
                    Destroy(spawn.Root.gameObject);
                }
            }
            _contractGroundExploders.Clear();
        }

        private void Update()
        {
            if (!_initialized || _motor == null)
            {
                return;
            }

            using (Markers.StreamingUpdate.Auto())
            {
                float deltaTime = Time.deltaTime;
                _streamingRefreshTimer -= deltaTime;
                Vector2Int playerChunk = GetPlayerLogicalChunk();
                if (_streamingRefreshTimer <= 0f || playerChunk != _lastScheduledChunk)
                {
                    _streamingRefreshTimer = Mathf.Max(0.05f, RefreshInterval);
                    ScheduleStreaming(force: playerChunk != _lastScheduledChunk);
                }

                ProcessQueuedGeneration(playerChunk);
            }
        }

        private void ProcessQueuedGeneration(Vector2Int playerChunk)
        {
            int generated = 0;
            double startTime = Time.realtimeSinceStartupAsDouble;
            double budgetSeconds = Mathf.Max(0.25f, GenerationTimeBudgetMilliseconds) * 0.001;
            while (generated < Mathf.Max(1, ChunksGeneratedPerFrame))
            {
                bool didWork = false;
                while (_collisionQueue.Count > 0)
                {
                    Vector2Int collisionCoordinate = _collisionQueue.Dequeue();
                    _queuedCollisionCoordinates.Remove(collisionCoordinate);
                    if (ChebyshevDistance(collisionCoordinate, playerChunk) > Mathf.Max(UnloadRadius, PreloadRadius + 2))
                    {
                        continue;
                    }

                    // An already-loaded chunk may still be visual-only, so it is upgraded rather
                    // than skipped. Chunks that already have a collider cost nothing here.
                    if (_chunks.TryGetValue(collisionCoordinate, out DesertChunk existingChunk) &&
                        existingChunk.IsCollisionReady)
                    {
                        continue;
                    }
                    GenerateChunkImmediate(collisionCoordinate, false);
                    generated++;
                    didWork = true;
                    break;
                }

                if (!didWork)
                {
                    while (_contentCompletionQueue.Count > 0)
                    {
                        Vector2Int coordinate = _contentCompletionQueue.Dequeue();
                        if (!_desiredContentCoordinates.Contains(coordinate) ||
                            !_chunks.TryGetValue(coordinate, out DesertChunk stagedChunk) ||
                            stagedChunk.IsContentReady)
                        {
                            continue;
                        }

                        AdvanceChunkContent(stagedChunk);
                        if (stagedChunk.IsContentReady && stagedChunk.IsVisualReady)
                        {
                            stagedChunk.SetPresentationActive(true);
                        }
                        generated++;
                        didWork = true;
                        break;
                    }
                }

                if (!didWork)
                {
                    while (_generationQueue.Count > 0)
                    {
                        Vector2Int coordinate = _generationQueue.Dequeue();
                        _queuedCoordinates.Remove(coordinate);
                        if (!_desiredVisualCoordinates.Contains(coordinate))
                        {
                            continue;
                        }

                        bool needsContent = _desiredContentCoordinates.Contains(coordinate);
                        if (_chunks.TryGetValue(coordinate, out DesertChunk queuedChunk) &&
                            (needsContent
                                ? queuedChunk.IsContentReady && queuedChunk.IsVisualReady
                                : queuedChunk.IsTerrainVisible && queuedChunk.IsVisualReady))
                        {
                            continue;
                        }

                        // Demoting a loaded chunk to a coarser tier is orders of magnitude cheaper
                        // than generating one, so it is governed by the time budget rather than
                        // the per-frame chunk count. Otherwise a whole ring of demotions drains
                        // one chunk per frame and the triangle saving takes seconds to appear.
                        bool reducedDetailRebuild = false;
                        if (needsContent)
                        {
                            // The chunk stages mesh and content work across queue passes while its
                            // root remains hidden, then reveals both together. Collision remains a
                            // separate near-player upgrade instead of burdening every preload chunk.
                            GenerateChunkImmediate(coordinate, true, false);
                        }
                        else if (!_chunks.TryGetValue(coordinate, out DesertChunk visualChunk))
                        {
                            GenerateChunkImmediate(coordinate, false, false);
                        }
                        else
                        {
                            reducedDetailRebuild = visualChunk.IsPendingReducedDetail;
                            visualChunk.BuildVisualTerrain();
                            visualChunk.SetPresentationActive(true);
                        }

                        if (_chunks.TryGetValue(coordinate, out DesertChunk advancedChunk) &&
                            !(needsContent
                                ? advancedChunk.IsContentReady && advancedChunk.IsVisualReady
                                : advancedChunk.IsTerrainVisible && advancedChunk.IsVisualReady) &&
                            _desiredVisualCoordinates.Contains(coordinate) &&
                            _queuedCoordinates.Add(coordinate))
                        {
                            _generationQueue.Enqueue(coordinate);
                        }
                        if (!reducedDetailRebuild)
                        {
                            generated++;
                        }
                        didWork = true;
                        break;
                    }
                }

                if (!didWork || Time.realtimeSinceStartupAsDouble - startTime >= budgetSeconds)
                {
                    break;
                }
            }
        }

        internal void TickStreamedObjects(float deltaTime)
        {
            if (!_initialized || deltaTime <= 0f)
            {
                return;
            }

            Vector2Int playerChunk = GetPlayerLogicalChunk();
            foreach (DesertChunk chunk in _chunks.Values)
            {
                if (ChebyshevDistance(chunk.Coordinate, playerChunk) <= SimulationRadius)
                {
                    chunk.Tick(deltaTime);
                }
            }
            PruneContractGroundExploders();
            for (int i = 0; i < _contractGroundExploders.Count; i++)
            {
                _contractGroundExploders[i].Enemy.Tick(deltaTime);
            }
        }

        internal void FixedTickStreamedObjects(float fixedDeltaTime)
        {
            if (!_initialized)
            {
                return;
            }

            Vector2Int playerChunk = GetPlayerLogicalChunk();
            foreach (DesertChunk chunk in _chunks.Values)
            {
                if (ChebyshevDistance(chunk.Coordinate, playerChunk) <= SimulationRadius)
                {
                    chunk.FixedTick(fixedDeltaTime);
                }
            }
            PruneContractGroundExploders();
            for (int i = 0; i < _contractGroundExploders.Count; i++)
            {
                _contractGroundExploders[i].Enemy.FixedTick(fixedDeltaTime);
            }
        }

        internal void LateTickStreamedObjects(float deltaTime)
        {
            if (!_initialized || (DuneTrainingRuntime.Enabled && !DuneTrainingRuntime.VisualEvaluation))
            {
                return;
            }

            Camera viewCamera = _camera != null ? _camera.Camera : null;
            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.LateTick(deltaTime, viewCamera);
            }
        }

        private void LateUpdate()
        {
            RebaseAroundPlayerIfNeeded();
        }

        /// <summary>
        /// Rebases the floating origin around the player's current motor position when it has
        /// crossed the authored threshold. Teleports can call this synchronously so the player,
        /// world, and follow camera all enter the destination origin frame in the same update.
        /// </summary>
        public bool RebaseAroundPlayerIfNeeded()
        {
            if (_motor == null)
            {
                return false;
            }

            Vector3 localPosition = _motor.TransientPosition;
            if ((localPosition.x * localPosition.x) + (localPosition.z * localPosition.z) < FloatingOriginThreshold * FloatingOriginThreshold)
            {
                return false;
            }

            float shiftX = Mathf.Round(localPosition.x / ChunkSize) * ChunkSize;
            float shiftZ = Mathf.Round(localPosition.z / ChunkSize) * ChunkSize;
            if (Mathf.Abs(shiftX) < 0.01f && Mathf.Abs(shiftZ) < 0.01f)
            {
                return false;
            }

            RebaseNow(new Vector3(shiftX, 0f, shiftZ));
            return true;
        }

        public void RebaseNow(Vector3 localShift)
        {
            if (_motor == null || (Mathf.Abs(localShift.x) < 0.001f && Mathf.Abs(localShift.z) < 0.001f))
            {
                return;
            }

            Vector3 motorPosition = _motor.TransientPosition;
            OriginOffsetX += localShift.x;
            OriginOffsetZ += localShift.z;
            _materials.SetTerrainLogicalOrigin(OriginOffsetX, OriginOffsetZ);
            SetVolumetricCloudsLogicalOrigin();

            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.Reposition(OriginOffsetX, OriginOffsetZ, ChunkSize);
            }
            for (int i = 0; i < _contractGroundExploders.Count; i++)
            {
                ContractGroundExploderSpawn spawn = _contractGroundExploders[i];
                if (spawn.Root != null)
                {
                    RepositionContractGroundRoot(spawn.Root, spawn.ChunkCoordinate);
                    spawn.Enemy?.SynchronizeAfterParentReposition();
                }
            }

            Vector3 worldShift = -localShift;
            _motor.SetPosition(motorPosition + worldShift, true);
            if (_camera != null)
            {
                _camera.ApplyWorldShift(worldShift);
            }
            _player?.HandleWorldShift(worldShift);
            DuneVectorSpatialInstancing.NotifyAllTransformsChanged();
            WorldShifted?.Invoke(worldShift);
            RebaseCount++;
        }

        private void SetVolumetricCloudsLogicalOrigin()
        {
            Vector4 logicalOriginOffset = new Vector4((float)OriginOffsetX, (float)OriginOffsetZ, 0f, 0f);
            Shader.SetGlobalVector(
                VolumetricCloudsWorldOriginOffsetId,
                logicalOriginOffset);
            Shader.SetGlobalVector(
                MassiveCloudsWorldOriginOffsetId,
                logicalOriginOffset);
        }

        private void OnDestroy()
        {
            Shader.SetGlobalVector(VolumetricCloudsWorldOriginOffsetId, Vector4.zero);
            Shader.SetGlobalVector(MassiveCloudsWorldOriginOffsetId, Vector4.zero);
        }

        private void RepositionContractGroundRoot(Transform root, Vector2Int coordinate)
        {
            double logicalX = coordinate.x * (double)ChunkSize;
            double logicalZ = coordinate.y * (double)ChunkSize;
            root.localPosition = new Vector3(
                (float)(logicalX - OriginOffsetX),
                0f,
                (float)(logicalZ - OriginOffsetZ));
        }

        private void PruneContractGroundExploders()
        {
            for (int i = _contractGroundExploders.Count - 1; i >= 0; i--)
            {
                ContractGroundExploderSpawn spawn = _contractGroundExploders[i];
                if (spawn.Enemy != null)
                {
                    continue;
                }
                if (spawn.Root != null)
                {
                    Destroy(spawn.Root.gameObject);
                }
                _contractGroundExploders.RemoveAt(i);
            }
        }

        private void ScheduleStreaming(bool force)
        {
            // A long traversal would otherwise grow the height cache without bound. One streaming
            // pass touches roughly a couple thousand coordinates, so dropping it wholesale here
            // costs a single re-sample pass and keeps the footprint flat.
            if (_chunkHeightRangeCache.Count > ChunkHeightCacheCapacity)
            {
                _chunkHeightRangeCache.Clear();
            }

            Vector2Int playerChunk = GetPlayerLogicalChunk();
            CurrentLogicalChunk = playerChunk;
            bool centerChanged = force || playerChunk != _lastScheduledChunk;
            if (centerChanged)
            {
                _lastScheduledChunk = playerChunk;
                EnsureCollisionNeighborhood(playerChunk);
            }
            QueuePredictedCollisionNeighborhood();
            RefreshChunkActivity(playerChunk);
            RefreshTerrainLod(playerChunk);

            _desiredContentCoordinates.Clear();
            _desiredVisualCoordinates.Clear();
            _retainedVisualCoordinates.Clear();
            _cameraPriorityCoordinates.Clear();
            _candidateCoordinates.Clear();
            int activeRadius = Mathf.Max(1, ActiveRadius);
            AppendSquareCoordinates(
                playerChunk,
                activeRadius,
                _desiredContentCoordinates,
                _desiredVisualCoordinates);

            LogicalPosition playerLogical = LogicalPlayerPosition;
            Vector3 velocity = _motor != null ? _motor.Velocity : Vector3.zero;
            float predictionSeconds = Mathf.Max(0f, ContentPreloadPredictionSeconds);
            Vector2Int predictedContentChunk = LogicalToChunk(
                playerLogical.X + (velocity.x * predictionSeconds),
                playerLogical.Z + (velocity.z * predictionSeconds));
            int preloadRadius = Mathf.Max(1, PreloadRadius);
            AppendSquareCoordinates(
                predictedContentChunk,
                preloadRadius,
                _desiredContentCoordinates,
                _desiredVisualCoordinates);

            int unloadRadius = Mathf.Max(activeRadius, UnloadRadius);
            for (int z = -unloadRadius; z <= unloadRadius; z++)
            {
                for (int x = -unloadRadius; x <= unloadRadius; x++)
                {
                    _retainedVisualCoordinates.Add(playerChunk + new Vector2Int(x, z));
                }
            }
            _retainedVisualCoordinates.UnionWith(_desiredVisualCoordinates);

            Camera viewCamera = _camera != null ? _camera.Camera : null;
            if (TryGetCameraFrustumDistance(viewCamera, out float frustumDistance))
            {
                AppendCameraFrustumCoordinates(
                    viewCamera,
                    frustumDistance,
                    CameraFrustumPaddingChunks,
                    MaximumCameraFrustumTerrainChunks,
                    _desiredVisualCoordinates);

                foreach (Vector2Int coordinate in _desiredVisualCoordinates)
                {
                    if (GeometryUtility.TestPlanesAABB(
                        _streamingFrustumPlanes,
                        CalculateChunkTerrainBounds(coordinate, 0f)))
                    {
                        _cameraPriorityCoordinates.Add(coordinate);
                    }
                }

                int retainedFrustumBudget = Mathf.Max(16, MaximumCameraFrustumTerrainChunks);
                foreach (Vector2Int coordinate in _desiredVisualCoordinates)
                {
                    if (!_retainedVisualCoordinates.Contains(coordinate))
                    {
                        retainedFrustumBudget--;
                    }
                }
                _retainedVisualCoordinates.UnionWith(_desiredVisualCoordinates);
                AppendCameraFrustumCoordinates(
                    viewCamera,
                    frustumDistance + (Mathf.Max(0, CameraFrustumUnloadPaddingChunks) * ChunkSize),
                    CameraFrustumPaddingChunks + CameraFrustumUnloadPaddingChunks,
                    Mathf.Max(0, retainedFrustumBudget),
                    _retainedVisualCoordinates);
            }

            _generationQueue.Clear();
            _queuedCoordinates.Clear();
            foreach (Vector2Int coordinate in _desiredVisualCoordinates)
            {
                bool needsContent = _desiredContentCoordinates.Contains(coordinate);
                bool needsWork = !_chunks.TryGetValue(coordinate, out DesertChunk chunk) ||
                    (needsContent
                        ? !chunk.IsContentReady || !chunk.IsVisualReady
                        : !chunk.IsTerrainVisible || !chunk.IsVisualReady);
                if (needsWork)
                {
                    _candidateCoordinates.Add(coordinate);
                }
            }
            _candidateSortCenter = playerChunk;
            _candidateCoordinates.Sort(CompareCandidateCoordinates);
            for (int i = 0; i < _candidateCoordinates.Count; i++)
            {
                _generationQueue.Enqueue(_candidateCoordinates[i]);
                _queuedCoordinates.Add(_candidateCoordinates[i]);
            }

            _removalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, DesertChunk> entry in _chunks)
            {
                if (!_retainedVisualCoordinates.Contains(entry.Key))
                {
                    _removalBuffer.Add(entry.Key);
                }
            }
            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                Vector2Int coordinate = _removalBuffer[i];
                _chunks[coordinate].Dispose();
                _chunks.Remove(coordinate);
                UnloadedChunkCount++;
            }
            RefreshChunkActivity(playerChunk);
        }

        private static void AppendSquareCoordinates(
            Vector2Int center,
            int radius,
            HashSet<Vector2Int> contentDestination,
            HashSet<Vector2Int> visualDestination)
        {
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int coordinate = center + new Vector2Int(x, z);
                    contentDestination.Add(coordinate);
                    visualDestination.Add(coordinate);
                }
            }
        }

        private bool TryGetCameraFrustumDistance(Camera viewCamera, out float distance)
        {
            distance = 0f;
            if (!EnableCameraFrustumTerrainStreaming || viewCamera == null || HeightField == null)
            {
                return false;
            }

            Vector3 cameraPosition = viewCamera.transform.position;
            double logicalX = OriginOffsetX + cameraPosition.x;
            double logicalZ = OriginOffsetZ + cameraPosition.z;
            float terrainHeight = (float)HeightField.SampleHeight(logicalX, logicalZ);
            float altitude = cameraPosition.y - terrainHeight;
            if (altitude < Mathf.Max(0f, CameraFrustumMinimumAltitude))
            {
                return false;
            }

            float altitudeRange = Mathf.Max(
                0.01f,
                CameraFrustumFullDistanceAltitude - CameraFrustumMinimumAltitude);
            float altitudeProgress = Mathf.Clamp01(
                (altitude - CameraFrustumMinimumAltitude) / altitudeRange);
            float minimumDistance = Mathf.Max(ChunkSize, CameraFrustumMinimumDistance);
            float maximumDistance = Mathf.Max(minimumDistance, CameraFrustumMaximumDistance);
            distance = Mathf.Lerp(minimumDistance, maximumDistance, altitudeProgress);
            return true;
        }

        private void AppendCameraFrustumCoordinates(
            Camera viewCamera,
            float maximumDistance,
            int paddingChunks,
            int maximumChunks,
            HashSet<Vector2Int> destination)
        {
            GeometryUtility.CalculateFrustumPlanes(viewCamera, _streamingFrustumPlanes);
            _streamingFrustumPlanes[5] = new Plane(
                -viewCamera.transform.forward,
                viewCamera.transform.position + (viewCamera.transform.forward * maximumDistance));

            Vector3 cameraPosition = viewCamera.transform.position;
            Vector2Int cameraChunk = LogicalToChunk(
                OriginOffsetX + cameraPosition.x,
                OriginOffsetZ + cameraPosition.z);
            int searchRadius = Mathf.CeilToInt(maximumDistance / ChunkSize) + Mathf.Max(0, paddingChunks) + 1;
            float horizontalPadding = Mathf.Max(0, paddingChunks) * ChunkSize;
            _frustumCandidateCoordinates.Clear();
            _frustumSortCenter = new Vector2(
                cameraPosition.x,
                cameraPosition.z);

            for (int z = -searchRadius; z <= searchRadius; z++)
            {
                for (int x = -searchRadius; x <= searchRadius; x++)
                {
                    Vector2Int coordinate = cameraChunk + new Vector2Int(x, z);
                    if (destination.Contains(coordinate))
                    {
                        continue;
                    }

                    Bounds terrainBounds = CalculateChunkTerrainBounds(coordinate, horizontalPadding);
                    if (GeometryUtility.TestPlanesAABB(_streamingFrustumPlanes, terrainBounds))
                    {
                        _frustumCandidateCoordinates.Add(coordinate);
                    }
                }
            }

            _frustumCandidateCoordinates.Sort(CompareFrustumCandidateCoordinates);
            int selectedCount = Mathf.Min(
                Mathf.Max(0, maximumChunks),
                _frustumCandidateCoordinates.Count);
            for (int i = 0; i < selectedCount; i++)
            {
                destination.Add(_frustumCandidateCoordinates[i]);
            }
        }

        private Bounds CalculateChunkTerrainBounds(Vector2Int coordinate, float horizontalPadding)
        {
            double logicalMinX = coordinate.x * (double)ChunkSize;
            double logicalMinZ = coordinate.y * (double)ChunkSize;
            double logicalMaxX = logicalMinX + ChunkSize;
            double logicalMaxZ = logicalMinZ + ChunkSize;
            double logicalCenterX = logicalMinX + (ChunkSize * 0.5);
            double logicalCenterZ = logicalMinZ + (ChunkSize * 0.5);

            if (!_chunkHeightRangeCache.TryGetValue(coordinate, out Vector2 heightRange))
            {
                float height0 = (float)HeightField.SampleHeight(logicalMinX, logicalMinZ);
                float height1 = (float)HeightField.SampleHeight(logicalMaxX, logicalMinZ);
                float height2 = (float)HeightField.SampleHeight(logicalMinX, logicalMaxZ);
                float height3 = (float)HeightField.SampleHeight(logicalMaxX, logicalMaxZ);
                float height4 = (float)HeightField.SampleHeight(logicalCenterX, logicalCenterZ);
                heightRange = new Vector2(
                    Mathf.Min(height0, height1, height2, height3, height4),
                    Mathf.Max(height0, height1, height2, height3, height4));
                _chunkHeightRangeCache[coordinate] = heightRange;
            }

            float heightPadding = Mathf.Max(0f, CameraFrustumTerrainHeightPadding);
            float minimumHeight = heightRange.x - heightPadding;
            float maximumHeight = heightRange.y + heightPadding;
            Vector3 center = new Vector3(
                (float)(logicalCenterX - OriginOffsetX),
                (minimumHeight + maximumHeight) * 0.5f,
                (float)(logicalCenterZ - OriginOffsetZ));
            Vector3 size = new Vector3(
                ChunkSize + (horizontalPadding * 2f),
                Mathf.Max(0.01f, maximumHeight - minimumHeight),
                ChunkSize + (horizontalPadding * 2f));
            return new Bounds(center, size);
        }

        /// <summary>
        /// Only the chunk the player actually stands on has to exist this frame, or the player
        /// falls through. The eight neighbours go through the collision queue so they are built
        /// under the generation time budget instead of costing nine terrain meshes in the single
        /// frame the player crosses a chunk boundary.
        /// </summary>
        private void EnsureCollisionNeighborhood(Vector2Int playerChunk)
        {
            GenerateChunkImmediate(playerChunk, false);
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && z == 0)
                    {
                        continue;
                    }

                    Vector2Int coordinate = playerChunk + new Vector2Int(x, z);
                    if (_queuedCollisionCoordinates.Add(coordinate))
                    {
                        _collisionQueue.Enqueue(coordinate);
                    }
                }
            }
        }

        private void QueuePredictedCollisionNeighborhood()
        {
            if (_motor == null)
            {
                return;
            }
            Vector3 velocity = _motor.Velocity;
            LogicalPosition player = LogicalPlayerPosition;
            Vector2Int predictedChunk = LogicalToChunk(
                player.X + (velocity.x * Mathf.Max(0f, CollisionPredictionSeconds)),
                player.Z + (velocity.z * Mathf.Max(0f, CollisionPredictionSeconds)));
            _predictedCollisionChunk = predictedChunk;
            _hasPredictedCollisionChunk = true;
            int radius = Mathf.Clamp(CollisionPreloadRadius, 0, 2);
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int coordinate = predictedChunk + new Vector2Int(x, z);
                    bool needsCollision = !_chunks.TryGetValue(coordinate, out DesertChunk chunk) ||
                        !chunk.IsCollisionReady;
                    if (needsCollision && _queuedCollisionCoordinates.Add(coordinate))
                    {
                        _collisionQueue.Enqueue(coordinate);
                    }
                }
            }
        }

        /// <summary>
        /// Full mesh resolution inside the full-detail radius, the distant resolution beyond it.
        /// The distant value is snapped down to a whole divisor of the full resolution so the two
        /// tiers share every vertex along their boundary and can be stitched exactly.
        /// </summary>
        private int GetVisualResolutionForChunk(Vector2Int coordinate, Vector2Int playerChunk)
        {
            if (!EnableTerrainDistanceLod)
            {
                return ChunkResolution;
            }

            if (ChebyshevDistance(coordinate, playerChunk) <= Mathf.Max(1, TerrainFullDetailChunkRadius))
            {
                return ChunkResolution;
            }

            return GetSnappedDistantResolution();
        }

        private int GetSnappedDistantResolution()
        {
            int full = Mathf.Max(8, ChunkResolution);
            int requested = Mathf.Clamp(DistantTerrainResolution, 4, full);
            for (int candidate = requested; candidate >= 4; candidate--)
            {
                if (full % candidate == 0)
                {
                    return candidate;
                }
            }
            return full;
        }

        /// <summary>
        /// Pushes each loaded chunk its own resolution plus the step ratio of each neighbour, so a
        /// chunk bordering a coarser one knows which of its edges to collapse.
        /// </summary>
        private void RefreshTerrainLod(Vector2Int playerChunk)
        {
            foreach (DesertChunk chunk in _chunks.Values)
            {
                Vector2Int coordinate = chunk.Coordinate;
                int resolution = GetVisualResolutionForChunk(coordinate, playerChunk);
                chunk.RequestVisualLod(
                    resolution,
                    BuildEdgeSteps(coordinate, playerChunk));
            }
        }

        private DesertChunk.TerrainEdgeSteps BuildEdgeSteps(Vector2Int coordinate, Vector2Int playerChunk)
        {
            int resolution = GetVisualResolutionForChunk(coordinate, playerChunk);
            return new DesertChunk.TerrainEdgeSteps(
                GetEdgeStep(resolution, coordinate + new Vector2Int(-1, 0), playerChunk),
                GetEdgeStep(resolution, coordinate + new Vector2Int(1, 0), playerChunk),
                GetEdgeStep(resolution, coordinate + new Vector2Int(0, -1), playerChunk),
                GetEdgeStep(resolution, coordinate + new Vector2Int(0, 1), playerChunk));
        }

        /// <summary>
        /// How many of this chunk's edge steps the neighbour spans with a single one. Neighbours at
        /// equal or finer detail leave the edge untouched.
        /// </summary>
        private int GetEdgeStep(int resolution, Vector2Int neighbour, Vector2Int playerChunk)
        {
            int neighbourResolution = GetVisualResolutionForChunk(neighbour, playerChunk);
            if (neighbourResolution >= resolution || neighbourResolution <= 0)
            {
                return 1;
            }
            return Mathf.Max(1, resolution / neighbourResolution);
        }

        private void RefreshChunkActivity(Vector2Int playerChunk)
        {
            int collisionRadius = Mathf.Max(1, CollisionActiveRadius);
            int shadowRadius = Mathf.Max(1, ActiveRadius);
            foreach (DesertChunk chunk in _chunks.Values)
            {
                bool nearPlayer = ChebyshevDistance(chunk.Coordinate, playerChunk) <= collisionRadius;
                bool nearPrediction = _hasPredictedCollisionChunk &&
                    ChebyshevDistance(chunk.Coordinate, _predictedCollisionChunk) <= CollisionPreloadRadius;
                chunk.SetCollisionActive(nearPlayer || nearPrediction);
                chunk.SetShadowCastingActive(
                    ChebyshevDistance(chunk.Coordinate, playerChunk) <= shadowRadius);
            }
        }

        private void GenerateChunkImmediate(
            Vector2Int coordinate,
            bool completeContent = true,
            bool requireCollision = true)
        {
            if (_chunks.TryGetValue(coordinate, out DesertChunk existing))
            {
                if (requireCollision)
                {
                    existing.EnsureCollisionReady();
                }
                if (completeContent && !existing.IsContentReady)
                {
                    AdvanceChunkContent(existing);
                }
                if (completeContent && existing.IsContentReady && existing.IsVisualReady)
                {
                    existing.SetPresentationActive(true);
                }
                return;
            }

            Vector2Int playerChunkForLod = GetPlayerLogicalChunk();
            DesertChunk chunk;
            using ((requireCollision ? Markers.CollisionChunk : Markers.VisualChunk).Auto())
            {
                chunk = new DesertChunk(
                    coordinate,
                    _chunkRoot,
                    OriginOffsetX,
                    OriginOffsetZ,
                    ChunkSize,
                    ChunkResolution,
                    CollisionMeshResolution,
                    HeightField,
                    _materials,
                    requireCollision,
                    GetVisualResolutionForChunk(coordinate, playerChunkForLod),
                    BuildEdgeSteps(coordinate, playerChunkForLod));
            }
            _chunks.Add(coordinate, chunk);
            chunk.EnsureClouds(
                _materials,
                Clouds,
                GetCloudDensityPerChunk(),
                WorldSeed);
            if (completeContent)
            {
                // Keep a staged content chunk invisible until the next queue pass completes its
                // authored content. This avoids one large mesh+content spike while ensuring the
                // terrain and everything belonging to it appear together.
                chunk.SetPresentationActive(false);
                _contentCompletionQueue.Enqueue(coordinate);
            }
            GeneratedChunkCount++;
            PeakActiveChunkCount = Mathf.Max(PeakActiveChunkCount, _chunks.Count);
        }

        private void GenerateChunkFullyImmediate(Vector2Int coordinate)
        {
            GenerateChunkImmediate(coordinate, false);
            if (_chunks.TryGetValue(coordinate, out DesertChunk chunk))
            {
                chunk.BuildVisualTerrain();
                CompleteChunkContent(chunk);
            }
        }

        private void AdvanceChunkContent(DesertChunk chunk)
        {
            if (!chunk.IsVisualReady)
            {
                chunk.BuildVisualTerrain();
                return;
            }
            CompleteChunkContent(chunk);
        }

        private void CompleteChunkContent(DesertChunk chunk)
        {
            using (Markers.CompleteChunk.Auto())
            {
                chunk.CompleteContent(
                    _materials,
                    Clouds,
                    GetCloudDensityPerChunk(),
                    _player,
                    _playerHealth,
                    WorldSeed,
                    _coinRingSeed,
                    EnemySpawnSeed,
                    Cacti,
                    PyramidDensity,
                    PyramidMinimumScale,
                    PyramidMaximumScale,
                    PyramidMaximumPlacementSlope,
                    PyramidMinimumBurialDepth,
                    PyramidMaximumBurialDepth,
                    Obelisks,
                    DarkPyramids,
                    Pyramid2,
                    Rings.GroundRingDensityPerChunk,
                    Rings.AerialRingDensityPerChunk,
                    Rings,
                    GroundExploders,
                    Shrubs,
                    Landmarks,
                    Geoglyphs,
                    HandleTraversalRingActivated);
                if (IsUpperFlightRingUnlocked)
                {
                    chunk.SpawnUpperFlightLayers(_player != null ? _player.FlightTimeNormalized : 0f);
                }
                chunk.SetCoinRingsUnlocked(IsUpperFlightRingUnlocked);
            }
        }

        private float GetCloudDensityPerChunk()
        {
            if (Clouds == null || !Clouds.Enabled || Clouds.ClusterCount <= 0)
            {
                return 0f;
            }

            int diameter = (Mathf.Max(1, PreloadRadius) * 2) + 1;
            Clouds.EnsureInitialized();
            CloudArrangementTuning arrangement = Clouds.GetActiveArrangement();
            return (Clouds.ClusterCount * Mathf.Max(0f, arrangement.ClusterCountMultiplier))
                / (diameter * diameter);
        }

        private void HandleTraversalRingActivated(TraversalRing ring)
        {
            if (ring == null
                || ring.RingType != TraversalRingType.Flight
                || string.IsNullOrEmpty(ring.ProceduralIdentity)
                || IsUpperFlightRingUnlocked
                || !_activatedFlightRingIdentities.Add(ring.ProceduralIdentity))
            {
                return;
            }

            SaveFlightRingProgress();
            int requiredPasses = Mathf.Max(1, Rings.UpperFlightRingRequiredPasses);
            if (_activatedFlightRingIdentities.Count >= requiredPasses)
            {
                SpawnUpperFlightLayersForLoadedChunks();
            }
        }

        private void LoadFlightRingProgress()
        {
            _activatedFlightRingIdentities.Clear();
            if (!File.Exists(_flightRingProgressSavePath))
            {
                SaveFlightRingProgress();
                return;
            }

            try
            {
                FlightRingProgressSaveData data = JsonUtility.FromJson<FlightRingProgressSaveData>(
                    File.ReadAllText(_flightRingProgressSavePath));
                if (data?.ActivatedRingIdentities == null)
                {
                    return;
                }

                for (int i = 0; i < data.ActivatedRingIdentities.Length; i++)
                {
                    string identity = data.ActivatedRingIdentities[i];
                    if (!string.IsNullOrEmpty(identity))
                    {
                        _activatedFlightRingIdentities.Add(identity);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not load flight ring progress from '{_flightRingProgressSavePath}': {exception.Message}",
                    this);
            }
        }

        private void SaveFlightRingProgress()
        {
            if (DuneTrainingRuntime.Enabled || string.IsNullOrEmpty(_flightRingProgressSavePath))
            {
                return;
            }

            try
            {
                string[] identities = new string[_activatedFlightRingIdentities.Count];
                _activatedFlightRingIdentities.CopyTo(identities);
                Array.Sort(identities, StringComparer.Ordinal);
                FlightRingProgressSaveData data = new FlightRingProgressSaveData
                {
                    ActivatedRingIdentities = identities,
                };
                File.WriteAllText(_flightRingProgressSavePath, JsonUtility.ToJson(data));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not save flight ring progress to '{_flightRingProgressSavePath}': {exception.Message}",
                    this);
            }
        }

        private void SpawnUpperFlightLayersForLoadedChunks()
        {
            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.SpawnUpperFlightLayers(_player != null ? _player.FlightTimeNormalized : 0f);
                chunk.SetCoinRingsUnlocked(true);
            }
        }

        private Vector2Int GetPlayerLogicalChunk()
        {
            LogicalPosition logical = LogicalPlayerPosition;
            return LogicalToChunk(logical.X, logical.Z);
        }

        private Vector2Int LogicalToChunk(double logicalX, double logicalZ)
        {
            return new Vector2Int(
                (int)Math.Floor(logicalX / ChunkSize),
                (int)Math.Floor(logicalZ / ChunkSize));
        }

        private static int ChebyshevDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        private int CompareCandidateCoordinates(Vector2Int left, Vector2Int right)
        {
            // Terrain must win over secondary chunk content. A content-radius chunk can already
            // have a usable dune mesh while it is still waiting for landmarks, rings, shrubs, and
            // enemies. Sorting that content pass ahead of missing frustum terrain leaves visible
            // holes in aerial flight until every nearby chunk has completed all of its content.
            // Keep the authored content coverage, but fill/rebuild the desert surface first.
            bool leftNeedsVisualTerrain = NeedsVisualTerrain(left);
            bool rightNeedsVisualTerrain = NeedsVisualTerrain(right);
            if (leftNeedsVisualTerrain != rightNeedsVisualTerrain)
            {
                return leftNeedsVisualTerrain ? -1 : 1;
            }

            bool leftCameraPriority = _cameraPriorityCoordinates.Contains(left);
            bool rightCameraPriority = _cameraPriorityCoordinates.Contains(right);
            if (leftCameraPriority != rightCameraPriority)
            {
                return leftCameraPriority ? -1 : 1;
            }

            bool leftNeedsContent = _desiredContentCoordinates.Contains(left);
            bool rightNeedsContent = _desiredContentCoordinates.Contains(right);
            if (leftNeedsContent != rightNeedsContent)
            {
                return leftNeedsContent ? -1 : 1;
            }
            return ChebyshevDistance(left, _candidateSortCenter)
                .CompareTo(ChebyshevDistance(right, _candidateSortCenter));
        }

        private bool NeedsVisualTerrain(Vector2Int coordinate)
        {
            return !_chunks.TryGetValue(coordinate, out DesertChunk chunk) ||
                !chunk.IsTerrainVisible ||
                !chunk.IsVisualReady;
        }

        private int CompareFrustumCandidateCoordinates(Vector2Int left, Vector2Int right)
        {
            return ChunkDistanceSquared(left, _frustumSortCenter)
                .CompareTo(ChunkDistanceSquared(right, _frustumSortCenter));
        }

        private float ChunkDistanceSquared(Vector2Int coordinate, Vector2 logicalPosition)
        {
            float centerX = (float)(((coordinate.x + 0.5) * ChunkSize) - OriginOffsetX);
            float centerZ = (float)(((coordinate.y + 0.5) * ChunkSize) - OriginOffsetZ);
            float deltaX = centerX - logicalPosition.x;
            float deltaZ = centerZ - logicalPosition.y;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private void OnDrawGizmos()
        {
            if (!DrawChunkBounds || _chunks == null)
            {
                return;
            }

            Gizmos.color = new Color(0f, 0.75f, 1f, 0.4f);
            foreach (DesertChunk chunk in _chunks.Values)
            {
                Vector3 center = chunk.Root.position + new Vector3(ChunkSize * 0.5f, 0f, ChunkSize * 0.5f);
                Gizmos.DrawWireCube(center, new Vector3(ChunkSize, 0.25f, ChunkSize));
            }
        }
    }

    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    internal sealed class DuneVectorStreamedSimulation : MonoBehaviour
    {
        private DesertWorldStreamer _world;

        public void Initialize(DesertWorldStreamer world)
        {
            _world = world;
        }

        private void Update()
        {
            _world?.TickStreamedObjects(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            _world?.FixedTickStreamedObjects(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            _world?.LateTickStreamedObjects(Time.deltaTime);
        }
    }

    /// <summary>
    /// One deterministic cactus slot inside a chunk, in chunk-local XZ metres.
    /// </summary>
    internal struct CactusPlacement
    {
        public Vector2 Local;
        public float Height;
        public float Thickness;
        public float Yaw;
        public int Arms;
        public int InstanceSeed;
    }

    internal sealed class DesertChunk : IDisposable
    {
        private sealed class TerrainBuildBuffers
        {
            public readonly Vector3[] Vertices;
            public readonly Vector3[] Normals;
            public readonly Vector2[] Uvs;
            public readonly Vector4[] Tangents;
            public readonly float[] PaddedHeights;
            public readonly int[] EdgeNormalSteps;

            public TerrainBuildBuffers(int resolution)
            {
                int row = resolution + 1;
                int paddedRow = row + 2;
                Vertices = new Vector3[row * row];
                Normals = new Vector3[row * row];
                Uvs = new Vector2[row * row];
                Tangents = new Vector4[row * row];
                PaddedHeights = new float[paddedRow * paddedRow];
                EdgeNormalSteps = new int[row * row];
            }
        }

        private static readonly Dictionary<int, int[]> TriangleIndicesByResolution = new Dictionary<int, int[]>();
        private static readonly Dictionary<int, TerrainBuildBuffers> BuildBuffersByResolution =
            new Dictionary<int, TerrainBuildBuffers>();

        public Vector2Int Coordinate { get; }
        public Transform Root { get; }

        private readonly List<TraversalRing> _rings = new List<TraversalRing>();
        private readonly List<GroundExploderEnemy> _groundExploders = new List<GroundExploderEnemy>();
        private readonly List<int> _occupancyHandles = new List<int>();
        private Mesh _terrainMesh;
        private Mesh _collisionMesh;
        private MeshCollider _terrainCollider;

        public bool IsCollisionReady => _terrainCollider != null;
        private readonly MeshFilter _terrainFilter;
        private readonly MeshRenderer _terrainRenderer;
        private readonly int _visualResolution;
        private int _desiredVisualResolution;
        private TerrainEdgeSteps _desiredEdgeSteps = TerrainEdgeSteps.Matched;
        private int _builtVisualResolution = -1;
        private TerrainEdgeSteps _builtEdgeSteps = TerrainEdgeSteps.Matched;
        private readonly int _collisionResolution;
        private readonly float _chunkSize;
        private readonly DuneHeightField _heightField;
        private int _worldSeed;
        private RingTuning _ringTuning;
        private DuneVectorCloudField _cloudField;
        private DesertShrubField _shrubs;
        public bool IsVisualReady { get; private set; }
        public bool IsTerrainVisible => _terrainMesh != null && Root.gameObject.activeSelf;

        /// <summary>
        /// True when the queued rebuild is a demotion to a coarser tier, which costs a fraction of
        /// a full-detail build.
        /// </summary>
        public bool IsPendingReducedDetail =>
            !IsVisualReady && _desiredVisualResolution < _visualResolution;
        public bool IsContentReady { get; private set; }

        public DesertChunk(
            Vector2Int coordinate,
            Transform parent,
            double originOffsetX,
            double originOffsetZ,
            float chunkSize,
            int resolution,
            int collisionResolution,
            DuneHeightField heightField,
            DuneVectorMaterials materials,
            bool createCollision,
            int visualLodResolution,
            TerrainEdgeSteps edgeSteps)
        {
            Coordinate = coordinate;
            _chunkSize = chunkSize;
            _heightField = heightField;
            _visualResolution = Mathf.Max(8, resolution);
            _desiredVisualResolution = Mathf.Clamp(visualLodResolution, 4, _visualResolution);
            _desiredEdgeSteps = edgeSteps;
            GameObject rootObject = new GameObject($"Desert Chunk [{coordinate.x}, {coordinate.y}]");
            Root = rootObject.transform;
            Root.SetParent(parent, false);
            Reposition(originOffsetX, originOffsetZ, chunkSize);

            _collisionResolution = Mathf.Clamp(collisionResolution, 8, _visualResolution);
            using (DesertWorldStreamer.Markers.TerrainMesh.Auto())
            {
                if (createCollision)
                {
                    _collisionMesh = BuildTerrainMesh(
                        coordinate,
                        chunkSize,
                        _collisionResolution,
                        heightField,
                        TerrainEdgeSteps.Matched);
                    _terrainMesh = _collisionMesh;
                    // The collision mesh doubles as the visual until the real LOD is built, which
                    // is only skippable when it already matches what the visual pass would produce.
                    IsVisualReady = _desiredVisualResolution == _collisionResolution &&
                        _desiredEdgeSteps.Equals(TerrainEdgeSteps.Matched);
                }
                else
                {
                    _terrainMesh = BuildTerrainMesh(
                        coordinate,
                        chunkSize,
                        _desiredVisualResolution,
                        heightField,
                        _desiredEdgeSteps);
                    IsVisualReady = true;
                }

                if (IsVisualReady)
                {
                    _builtVisualResolution = _desiredVisualResolution;
                    _builtEdgeSteps = _desiredEdgeSteps;
                }
            }
            _terrainFilter = rootObject.AddComponent<MeshFilter>();
            _terrainFilter.sharedMesh = _terrainMesh;
            _terrainRenderer = rootObject.AddComponent<MeshRenderer>();
            _terrainRenderer.sharedMaterials = materials.GetTerrainMaterials(coordinate, chunkSize);
            _terrainRenderer.shadowCastingMode = createCollision
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
            _terrainRenderer.receiveShadows = true;

            if (createCollision)
            {
                AssignTerrainCollider();
            }
        }

        public void EnsureCollisionReady()
        {
            if (_terrainCollider != null)
            {
                return;
            }
            // A stitched or reduced-LOD visual mesh is not a faithful ground surface, so it is only
            // reused for collision when it is still the untouched full-detail or collision-grade
            // build. Otherwise collision gets its own unstitched mesh.
            bool terrainMeshIsCollisionGrade =
                _builtVisualResolution < 0 ||
                (_builtEdgeSteps.Equals(TerrainEdgeSteps.Matched) &&
                    (_builtVisualResolution == _collisionResolution ||
                        _builtVisualResolution == _visualResolution));
            if (_terrainMesh != null && terrainMeshIsCollisionGrade)
            {
                _collisionMesh = _terrainMesh;
                AssignTerrainCollider();
                return;
            }
            using (DesertWorldStreamer.Markers.TerrainMesh.Auto())
            {
                _collisionMesh = BuildTerrainMesh(
                    Coordinate,
                    _chunkSize,
                    _collisionResolution,
                    _heightField,
                    TerrainEdgeSteps.Matched);
            }
            AssignTerrainCollider();
        }

        public void SetPresentationActive(bool active)
        {
            if (Root.gameObject.activeSelf != active)
            {
                Root.gameObject.SetActive(active);
            }
        }

        private void AssignTerrainCollider()
        {
            using (DesertWorldStreamer.Markers.ColliderAssignment.Auto())
            {
                _terrainCollider = Root.gameObject.AddComponent<MeshCollider>();
                _terrainCollider.sharedMesh = _collisionMesh;
                // Ground-bound enemies probe for solid meshes to steer around; the
                // dune surface they ride on must never read as one of those meshes.
                if (Root.GetComponent<DesertTerrainSurface>() == null)
                {
                    Root.gameObject.AddComponent<DesertTerrainSurface>();
                }
            }
        }

        public void CompleteContent(
            DuneVectorMaterials materials,
            CloudTuning cloudTuning,
            float cloudDensity,
            DroneCharacterController player,
            DroneHealth playerHealth,
            int worldSeed,
            int coinRingSeed,
            int enemySpawnSeed,
            CactusTuning cactusTuning,
            float pyramidDensity,
            float pyramidMinimumScale,
            float pyramidMaximumScale,
            float pyramidMaximumPlacementSlope,
            float pyramidMinimumBurialDepth,
            float pyramidMaximumBurialDepth,
            PyramidTuning obeliskTuning,
            PyramidTuning darkPyramidTuning,
            PyramidTuning pyramid2Tuning,
            float groundRingDensity,
            float aerialRingDensity,
            RingTuning ringTuning,
            GroundExploderTuning groundExploderTuning,
            DesertShrubTuning shrubTuning,
            LandmarkSystemTuning landmarkTuning,
            GeoglyphSystemTuning geoglyphTuning,
            Action<TraversalRing> ringActivated)
        {
            if (IsContentReady)
            {
                return;
            }
            IsContentReady = true;
            _worldSeed = worldSeed;
            _ringTuning = ringTuning;
            using (DesertWorldStreamer.Markers.ChunkContent.Auto())
            {
                EnsureClouds(materials, cloudTuning, cloudDensity, worldSeed);
                SpawnContent(
                    Coordinate,
                    _chunkSize,
                    _heightField,
                    materials,
                    player,
                    playerHealth,
                    worldSeed,
                    coinRingSeed,
                    enemySpawnSeed,
                    cactusTuning,
                    pyramidDensity,
                    pyramidMinimumScale,
                    pyramidMaximumScale,
                    pyramidMaximumPlacementSlope,
                    pyramidMinimumBurialDepth,
                    pyramidMaximumBurialDepth,
                    obeliskTuning,
                    darkPyramidTuning,
                    pyramid2Tuning,
                    groundRingDensity,
                    aerialRingDensity,
                    ringTuning,
                    groundExploderTuning,
                    shrubTuning,
                    landmarkTuning,
                    geoglyphTuning,
                    ringActivated);
            }
        }

        public void EnsureClouds(
            DuneVectorMaterials materials,
            CloudTuning tuning,
            float density,
            int worldSeed)
        {
            if (_cloudField != null || materials == null)
            {
                return;
            }

            SpawnClouds(
                Coordinate,
                _chunkSize,
                materials.Cloud,
                materials.CloudUnderbelly,
                worldSeed,
                tuning,
                density);
        }

        /// <summary>
        /// Records the resolution and neighbour edge steps this chunk should be built at. A change
        /// clears the ready flag so the streamer rebuilds it through the normal budgeted queue
        /// rather than stalling the frame.
        /// </summary>
        public void RequestVisualLod(int resolution, TerrainEdgeSteps edgeSteps)
        {
            int clamped = Mathf.Clamp(resolution, 4, _visualResolution);
            if (clamped == _desiredVisualResolution && edgeSteps.Equals(_desiredEdgeSteps))
            {
                return;
            }

            _desiredVisualResolution = clamped;
            _desiredEdgeSteps = edgeSteps;
            if (_builtVisualResolution != clamped || !_builtEdgeSteps.Equals(edgeSteps))
            {
                IsVisualReady = false;
            }
        }

        public void BuildVisualTerrain()
        {
            if (IsVisualReady)
            {
                return;
            }
            Mesh previousTerrainMesh = _terrainMesh;
            using (DesertWorldStreamer.Markers.TerrainMesh.Auto())
            {
                _terrainMesh = BuildTerrainMesh(
                    Coordinate,
                    _chunkSize,
                    _desiredVisualResolution,
                    _heightField,
                    _desiredEdgeSteps);
            }
            _builtVisualResolution = _desiredVisualResolution;
            _builtEdgeSteps = _desiredEdgeSteps;
            _terrainFilter.sharedMesh = _terrainMesh;
            if (previousTerrainMesh != null && previousTerrainMesh != _collisionMesh)
            {
                UnityEngine.Object.Destroy(previousTerrainMesh);
            }
            IsVisualReady = true;
        }

        private void SpawnClouds(
            Vector2Int coordinate,
            float chunkSize,
            Material sunlitMaterial,
            Material underbellyMaterial,
            int worldSeed,
            CloudTuning tuning,
            float density)
        {
            if (tuning == null || !tuning.Enabled || density <= 0f)
            {
                return;
            }

            tuning.EnsureInitialized();
            CloudArrangementTuning arrangement = tuning.GetActiveArrangement();
            int regionSize = Mathf.Max(1, arrangement.CompositionRegionSizeInChunks);
            int regionX = Mathf.FloorToInt(coordinate.x / (float)regionSize);
            int regionZ = Mathf.FloorToInt(coordinate.y / (float)regionSize);
            float regionRoll = DuneVectorMath.Hash01(
                regionX,
                regionZ,
                worldSeed,
                tuning.RandomSeedOffset ^ 4049);
            float densityMultiplier = regionRoll < Mathf.Clamp01(arrangement.NegativeSpaceRegionChance)
                ? Mathf.Max(0f, arrangement.NegativeSpaceDensityMultiplier)
                : Mathf.Max(0f, arrangement.CloudRegionDensityMultiplier);
            int clusterCount = CountFromDensity(
                density * densityMultiplier,
                coordinate,
                worldSeed,
                tuning.RandomSeedOffset);
            if (clusterCount <= 0)
            {
                return;
            }

            GameObject cloudObject = new GameObject("Chunk Clouds");
            cloudObject.transform.SetParent(Root, false);
            _cloudField = cloudObject.AddComponent<DuneVectorCloudField>();
            int randomSeed = unchecked(
                worldSeed
                ^ tuning.RandomSeedOffset
                ^ (coordinate.x * 73856093)
                ^ (coordinate.y * 19349663));
            _cloudField.Initialize(
                sunlitMaterial,
                underbellyMaterial,
                clusterCount,
                chunkSize,
                tuning,
                arrangement,
                randomSeed);
        }

        public void BindPlayer(DroneCharacterController player, DroneHealth playerHealth)
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].BindTargets(player, playerHealth);
                }
            }
            for (int i = 0; i < _groundExploders.Count; i++)
            {
                if (_groundExploders[i] != null)
                {
                    _groundExploders[i].BindTargets(player, playerHealth);
                }
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].Tick(deltaTime);
                }
            }
            for (int i = 0; i < _groundExploders.Count; i++)
            {
                if (_groundExploders[i] != null)
                {
                    _groundExploders[i].Tick(deltaTime);
                }
            }
        }

        public void FixedTick(float fixedDeltaTime)
        {
            for (int i = 0; i < _groundExploders.Count; i++)
            {
                if (_groundExploders[i] != null)
                {
                    _groundExploders[i].FixedTick(fixedDeltaTime);
                }
            }
        }

        public void RemoveGroundExplodersInsidePlayerSpawnClearance()
        {
            double originX = Coordinate.x * (double)_chunkSize;
            double originZ = Coordinate.y * (double)_chunkSize;
            for (int i = _groundExploders.Count - 1; i >= 0; i--)
            {
                GroundExploderEnemy enemy = _groundExploders[i];
                if (enemy == null)
                {
                    _groundExploders.RemoveAt(i);
                    continue;
                }

                Vector3 local = enemy.transform.localPosition;
                if (!DuneVectorEnemySpawnClearance.IsBlocked(originX + local.x, originZ + local.z))
                {
                    continue;
                }

                UnityEngine.Object.Destroy(enemy.gameObject);
                _groundExploders.RemoveAt(i);
            }
        }

        public void LateTick(float deltaTime, Camera viewCamera)
        {
            _cloudField?.Tick(deltaTime);
            _shrubs?.Draw(viewCamera);
            for (int i = 0; i < _rings.Count; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].LateTick(deltaTime, viewCamera);
                }
            }
        }

        public void SpawnUpperFlightLayers(float flightMeterNormalized)
        {
            float amountMultiplier = _ringTuning.GetUpperFlightRingAmountMultiplier(flightMeterNormalized);
            for (int i = 0; i < _rings.Count; i++)
            {
                TraversalRing ring = _rings[i];
                if (ring != null && ring.RingType == TraversalRingType.Flight)
                {
                    int seedOffset = _ringTuning.UpperFlightRingSeedOffset + (i * 53);
                    float amountRoll = DuneVectorMath.Hash01(
                        Coordinate.x,
                        Coordinate.y,
                        _worldSeed,
                        seedOffset);
                    if (amountRoll >= amountMultiplier)
                    {
                        continue;
                    }
                    float margin = Mathf.Min(
                        _chunkSize * 0.5f,
                        Mathf.Max(0f, _ringTuning.UpperFlightRingRadius));
                    float maximumCoordinate = Mathf.Max(margin, _chunkSize - margin);
                    float localX = DuneVectorMath.HashRange(
                        Coordinate.x,
                        Coordinate.y,
                        _worldSeed,
                        seedOffset + 1,
                        margin,
                        maximumCoordinate);
                    float localZ = DuneVectorMath.HashRange(
                        Coordinate.x,
                        Coordinate.y,
                        _worldSeed,
                        seedOffset + 7,
                        margin,
                        maximumCoordinate);
                    double logicalX = (Coordinate.x * (double)_chunkSize) + localX;
                    double logicalZ = (Coordinate.y * (double)_chunkSize) + localZ;
                    float terrainHeight = (float)_heightField.SampleHeight(logicalX, logicalZ);
                    float minimumHeight = Mathf.Max(0f, _ringTuning.UpperFlightRingMinimumHeight);
                    float maximumHeight = Mathf.Max(minimumHeight, _ringTuning.UpperFlightRingMaximumHeight);
                    float height = DuneVectorMath.HashRange(
                        Coordinate.x,
                        Coordinate.y,
                        _worldSeed,
                        seedOffset + 13,
                        minimumHeight,
                        maximumHeight);
                    float yaw = DuneVectorMath.HashRange(
                        Coordinate.x,
                        Coordinate.y,
                        _worldSeed,
                        seedOffset + 19,
                        0f,
                        360f);
                    float minimumLift = Mathf.Max(0f, _ringTuning.UpperFlightModeMinimumHeightOffset);
                    float maximumLift = Mathf.Max(minimumLift, _ringTuning.UpperFlightModeMaximumHeightOffset);
                    float flightModeHeightOffset = DuneVectorMath.HashRange(
                        Coordinate.x,
                        Coordinate.y,
                        _worldSeed,
                        seedOffset + 29,
                        minimumLift,
                        maximumLift);
                    ring.SpawnUpperFlightLayer(
                        new Vector3(localX, terrainHeight + height, localZ),
                        Quaternion.Euler(0f, yaw, 0f),
                        flightModeHeightOffset);
                }
            }
        }

        public void Reposition(double originOffsetX, double originOffsetZ, float chunkSize)
        {
            double logicalX = Coordinate.x * (double)chunkSize;
            double logicalZ = Coordinate.y * (double)chunkSize;
            Root.localPosition = new Vector3((float)(logicalX - originOffsetX), 0f, (float)(logicalZ - originOffsetZ));
            for (int i = 0; i < _groundExploders.Count; i++)
            {
                _groundExploders[i]?.SynchronizeAfterParentReposition();
            }
            _shrubs?.RebuildWorldMatrices();
        }

        public void SetCollisionActive(bool active)
        {
            if (_terrainCollider != null && _terrainCollider.enabled != active)
            {
                _terrainCollider.enabled = active;
            }
        }

        public void SetShadowCastingActive(bool active)
        {
            if (_terrainRenderer != null)
            {
                ShadowCastingMode desiredMode = active
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                if (_terrainRenderer.shadowCastingMode != desiredMode)
                {
                    _terrainRenderer.shadowCastingMode = desiredMode;
                }
            }
        }

        public void SetCoinRingsUnlocked(bool unlocked)
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                TraversalRing ring = _rings[i];
                if (ring != null && ring.RingType == TraversalRingType.Coin)
                {
                    ring.SetAvailable(unlocked);
                }
            }
        }

        public void Dispose()
        {
            DuneVectorWorldOccupancy.ReleaseAll(_occupancyHandles);
            _shrubs?.Dispose();
            _shrubs = null;
            if (Root != null)
            {
                UnityEngine.Object.Destroy(Root.gameObject);
            }
            if (_terrainMesh != null)
            {
                UnityEngine.Object.Destroy(_terrainMesh);
            }
            if (_collisionMesh != null && _collisionMesh != _terrainMesh)
            {
                UnityEngine.Object.Destroy(_collisionMesh);
            }
        }

        /// <summary>
        /// Per-edge step ratio of the neighbouring chunk's mesh. One means the neighbour is at the
        /// same resolution or finer and the edge is left alone; k greater than one means the
        /// neighbour only has a vertex every k steps along that edge.
        /// </summary>
        internal readonly struct TerrainEdgeSteps : IEquatable<TerrainEdgeSteps>
        {
            public readonly int NegativeX;
            public readonly int PositiveX;
            public readonly int NegativeZ;
            public readonly int PositiveZ;

            public TerrainEdgeSteps(int negativeX, int positiveX, int negativeZ, int positiveZ)
            {
                NegativeX = Mathf.Max(1, negativeX);
                PositiveX = Mathf.Max(1, positiveX);
                NegativeZ = Mathf.Max(1, negativeZ);
                PositiveZ = Mathf.Max(1, positiveZ);
            }

            public static TerrainEdgeSteps Matched => new TerrainEdgeSteps(1, 1, 1, 1);

            public bool Equals(TerrainEdgeSteps other) =>
                NegativeX == other.NegativeX &&
                PositiveX == other.PositiveX &&
                NegativeZ == other.NegativeZ &&
                PositiveZ == other.PositiveZ;

            public override bool Equals(object obj) => obj is TerrainEdgeSteps other && Equals(other);

            public override int GetHashCode() =>
                (((NegativeX * 397) ^ PositiveX) * 397 ^ NegativeZ) * 397 ^ PositiveZ;
        }

        /// <summary>
        /// Collapses this chunk's surplus edge vertices onto the straight line its coarser
        /// neighbour draws between their shared vertices. Both sides then describe the identical
        /// edge, so the seam closes exactly rather than being hidden behind a skirt.
        /// </summary>
        private static void StitchEdges(
            Vector3[] vertices,
            Vector3[] normals,
            int resolution,
            TerrainEdgeSteps edges)
        {
            int row = resolution + 1;
            StitchEdge(vertices, normals, edges.NegativeX, resolution, 0, row);
            StitchEdge(vertices, normals, edges.PositiveX, resolution, resolution, row);
            StitchEdge(vertices, normals, edges.NegativeZ, resolution, 0, 1);
            StitchEdge(vertices, normals, edges.PositiveZ, resolution, resolution * row, 1);
        }

        /// <summary>
        /// Rebuilds the surface normal at every vertex this chunk shares with a coarser neighbour
        /// using that neighbour's wider sample spacing. The heights already match exactly on those
        /// vertices, but a normal taken from this chunk's tighter finite difference does not match
        /// the one the coarse side takes from its own, which lights the shared row differently on
        /// each side and draws a hard streak along the boundary. Sampling at the neighbour's
        /// spacing makes both sides compute the identical normal.
        /// </summary>
        private static void ApplyCoarseNeighbourNormals(
            Vector3[] normals,
            int[] edgeNormalSteps,
            int resolution,
            TerrainEdgeSteps edges,
            DuneHeightField heightField,
            double logicalOriginX,
            double logicalOriginZ,
            float step)
        {
            int row = resolution + 1;
            Array.Clear(edgeNormalSteps, 0, row * row);
            bool any = MarkCoarseEdge(edgeNormalSteps, edges.NegativeX, resolution, 0, row);
            any |= MarkCoarseEdge(edgeNormalSteps, edges.PositiveX, resolution, resolution, row);
            any |= MarkCoarseEdge(edgeNormalSteps, edges.NegativeZ, resolution, 0, 1);
            any |= MarkCoarseEdge(edgeNormalSteps, edges.PositiveZ, resolution, resolution * row, 1);
            if (!any)
            {
                return;
            }

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int index = (z * row) + x;
                    int coarseStep = edgeNormalSteps[index];
                    if (coarseStep <= 1)
                    {
                        continue;
                    }

                    double logicalX = logicalOriginX + (x * step);
                    double logicalZ = logicalOriginZ + (z * step);
                    double span = coarseStep * (double)step;
                    float left = (float)heightField.SampleHeight(logicalX - span, logicalZ);
                    float right = (float)heightField.SampleHeight(logicalX + span, logicalZ);
                    float back = (float)heightField.SampleHeight(logicalX, logicalZ - span);
                    float forward = (float)heightField.SampleHeight(logicalX, logicalZ + span);
                    Vector3 tangentX = new Vector3((float)span * 2f, right - left, 0f);
                    Vector3 tangentZ = new Vector3(0f, forward - back, (float)span * 2f);
                    normals[index] = Vector3.Cross(tangentZ, tangentX).normalized;
                }
            }
        }

        /// <summary>
        /// Tags the vertices this chunk shares with one coarser neighbour. A corner touched by two
        /// coarse edges takes the wider of the two spacings so it stays deterministic.
        /// </summary>
        private static bool MarkCoarseEdge(
            int[] edgeNormalSteps,
            int step,
            int resolution,
            int baseIndex,
            int stride)
        {
            if (step <= 1 || resolution % step != 0)
            {
                return false;
            }

            for (int i = 0; i <= resolution; i += step)
            {
                int index = baseIndex + (i * stride);
                edgeNormalSteps[index] = Mathf.Max(edgeNormalSteps[index], step);
            }
            return true;
        }

        private static void StitchEdge(
            Vector3[] vertices,
            Vector3[] normals,
            int step,
            int resolution,
            int baseIndex,
            int stride)
        {
            if (step <= 1 || resolution % step != 0)
            {
                return;
            }

            for (int i = 1; i < resolution; i++)
            {
                int offset = i % step;
                if (offset == 0)
                {
                    continue;
                }

                // The bracketing vertices are multiples of the step, so they are shared with the
                // coarse neighbour and are never themselves moved by this pass.
                int lower = baseIndex + ((i - offset) * stride);
                int upper = baseIndex + ((i - offset + step) * stride);
                int target = baseIndex + (i * stride);
                float blend = offset / (float)step;

                Vector3 position = vertices[target];
                position.y = Mathf.Lerp(vertices[lower].y, vertices[upper].y, blend);
                vertices[target] = position;
                normals[target] = Vector3.Slerp(normals[lower], normals[upper], blend);
            }
        }

        private static Mesh BuildTerrainMesh(
            Vector2Int coordinate,
            float chunkSize,
            int resolution,
            DuneHeightField heightField,
            TerrainEdgeSteps edges)
        {
            // Floored at the same minimum the LOD selection uses, so a distant chunk is never
            // silently built at a resolution the caller did not compute its edge steps against.
            resolution = Mathf.Max(4, resolution);
            int row = resolution + 1;
            int vertexCount = row * row;
            TerrainBuildBuffers buffers = GetBuildBuffers(resolution);
            Vector3[] vertices = buffers.Vertices;
            Vector3[] normals = buffers.Normals;
            Vector2[] uvs = buffers.Uvs;
            int[] triangles = GetTriangleIndices(resolution);
            double logicalOriginX = coordinate.x * (double)chunkSize;
            double logicalOriginZ = coordinate.y * (double)chunkSize;
            float step = chunkSize / resolution;
            int paddedRow = row + 2;
            float[] paddedHeights = buffers.PaddedHeights;
            float minimumHeight = float.PositiveInfinity;
            float maximumHeight = float.NegativeInfinity;

            int paddedVertex = 0;
            for (int z = -1; z <= resolution + 1; z++)
            {
                for (int x = -1; x <= resolution + 1; x++)
                {
                    paddedHeights[paddedVertex++] = (float)heightField.SampleHeight(
                        logicalOriginX + (x * step),
                        logicalOriginZ + (z * step));
                }
            }

            int vertex = 0;
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    double logicalX = logicalOriginX + (x * step);
                    double logicalZ = logicalOriginZ + (z * step);
                    int paddedIndex = ((z + 1) * paddedRow) + x + 1;
                    float height = paddedHeights[paddedIndex];
                    float left = paddedHeights[paddedIndex - 1];
                    float right = paddedHeights[paddedIndex + 1];
                    float back = paddedHeights[paddedIndex - paddedRow];
                    float forward = paddedHeights[paddedIndex + paddedRow];
                    vertices[vertex] = new Vector3(x * step, height, z * step);
                    Vector3 tangentX = new Vector3(step * 2f, right - left, 0f);
                    Vector3 tangentZ = new Vector3(0f, forward - back, step * 2f);
                    normals[vertex] = Vector3.Cross(tangentZ, tangentX).normalized;
                    uvs[vertex] = new Vector2((float)logicalX, (float)logicalZ);
                    minimumHeight = Mathf.Min(minimumHeight, height);
                    maximumHeight = Mathf.Max(maximumHeight, height);
                    vertex++;
                }
            }

            ApplyCoarseNeighbourNormals(
                normals,
                buffers.EdgeNormalSteps,
                resolution,
                edges,
                heightField,
                logicalOriginX,
                logicalOriginZ,
                step);
            StitchEdges(vertices, normals, resolution, edges);

            // The dune UVs run straight along world X and Z, so the tangent is world X
            // orthogonalized against the surface normal. That is exact here and far cheaper
            // than Mesh.RecalculateTangents on every streamed chunk.
            Vector4[] tangents = buffers.Tangents;
            for (int index = 0; index < vertexCount; index++)
            {
                Vector3 normal = normals[index];
                Vector3 tangent = Vector3.right - (normal * normal.x);
                float magnitude = tangent.magnitude;
                tangent = magnitude > 1e-5f ? tangent / magnitude : Vector3.right;
                tangents[index] = new Vector4(tangent.x, tangent.y, tangent.z, -1f);
            }

            Mesh mesh = new Mesh { name = $"Dune Terrain [{coordinate.x}, {coordinate.y}]" };
            if (vertexCount > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.tangents = tangents;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(
                new Vector3(chunkSize * 0.5f, (minimumHeight + maximumHeight) * 0.5f, chunkSize * 0.5f),
                new Vector3(chunkSize, maximumHeight - minimumHeight, chunkSize));
            return mesh;
        }

        private static TerrainBuildBuffers GetBuildBuffers(int resolution)
        {
            if (!BuildBuffersByResolution.TryGetValue(resolution, out TerrainBuildBuffers buffers))
            {
                buffers = new TerrainBuildBuffers(resolution);
                BuildBuffersByResolution.Add(resolution, buffers);
            }
            return buffers;
        }

        private static int[] GetTriangleIndices(int resolution)
        {
            if (TriangleIndicesByResolution.TryGetValue(resolution, out int[] triangles))
            {
                return triangles;
            }

            int row = resolution + 1;
            triangles = new int[resolution * resolution * 6];
            int triangle = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int a = (z * row) + x;
                    int b = a + row;
                    triangles[triangle++] = a;
                    triangles[triangle++] = b;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b + 1;
                }
            }
            TriangleIndicesByResolution.Add(resolution, triangles);
            return triangles;
        }

        private void SpawnContent(
            Vector2Int coordinate,
            float chunkSize,
            DuneHeightField heightField,
            DuneVectorMaterials materials,
            DroneCharacterController player,
            DroneHealth playerHealth,
            int worldSeed,
            int coinRingSeed,
            int enemySpawnSeed,
            CactusTuning cactusTuning,
            float pyramidDensity,
            float pyramidMinimumScale,
            float pyramidMaximumScale,
            float pyramidMaximumPlacementSlope,
            float pyramidMinimumBurialDepth,
            float pyramidMaximumBurialDepth,
            PyramidTuning obeliskTuning,
            PyramidTuning darkPyramidTuning,
            PyramidTuning pyramid2Tuning,
            float groundRingDensity,
            float aerialRingDensity,
            RingTuning ringTuning,
            GroundExploderTuning groundExploderTuning,
            DesertShrubTuning shrubTuning,
            LandmarkSystemTuning landmarkTuning,
            GeoglyphSystemTuning geoglyphTuning,
            Action<TraversalRing> ringActivated)
        {
            List<Vector2> ringExclusions = new List<Vector2>();
            List<Vector2> sceneryExclusions = new List<Vector2>();
            double originX = coordinate.x * (double)chunkSize;
            double originZ = coordinate.y * (double)chunkSize;

            if (coordinate == Vector2Int.zero)
            {
                CreateRing(new Vector2(10f, 29f), Vector3.forward, TraversalRingType.GroundBoost, ringTuning.GroundRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "starter-boost", ringActivated);
                CreateRing(new Vector2(10f, 57f), Vector3.forward, TraversalRingType.Flight, ringTuning.FlightRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "starter-flight", ringActivated);
            }
            else
            {
                float flightMeterNormalized = player != null ? player.FlightTimeNormalized : 0f;
                float flightRingAmountMultiplier = ringTuning.GetFlightRingAmountMultiplier(flightMeterNormalized);
                float totalFlightRingDensity = aerialRingDensity * flightRingAmountMultiplier;
                float baseFlightRingDensity = Mathf.Min(aerialRingDensity, totalFlightRingDensity);
                // Flight rings are placed before any ground ring so that a chunk packed with
                // boost rings can no longer consume every slot the separation test allows.
                // The route pair still spawns together, but the ground half of the pair waits
                // its turn with the rest of the ground rings.
                float aerialChance = DuneVectorMath.Hash01(coordinate.x, coordinate.y, worldSeed, 701);
                bool hasRoutePair = aerialChance < aerialRingDensity;
                float routeAngle = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 709, 0f, 360f);
                Vector3 routeForward = Quaternion.Euler(0f, routeAngle, 0f) * Vector3.forward;
                Vector2 routeCenter = new Vector2(
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 719, 24f, chunkSize - 24f),
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 727, 24f, chunkSize - 24f));
                Vector2 routeForward2 = new Vector2(routeForward.x, routeForward.z);
                Vector2 routeBoostPosition = routeCenter - (routeForward2 * 11f);
                Vector2 routeFlightPosition = routeCenter + (routeForward2 * 11f);

                if (hasRoutePair && aerialChance < baseFlightRingDensity)
                {
                    CreateRing(routeFlightPosition, routeForward, TraversalRingType.Flight, ringTuning.FlightRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "route-flight", ringActivated);
                }

                float additionalFlightDensity = Mathf.Max(
                    0f,
                    totalFlightRingDensity - aerialRingDensity);
                int additionalFlightRingCount = CountFromDensity(
                    additionalFlightDensity,
                    coordinate,
                    worldSeed,
                    761);
                for (int flightRingIndex = 0; flightRingIndex < additionalFlightRingCount; flightRingIndex++)
                {
                    int saltOffset = flightRingIndex * 12;
                    float angle = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        769 + saltOffset,
                        0f,
                        360f);
                    Vector3 forward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector2 position = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 773 + saltOffset, 24f, chunkSize - 24f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 779 + saltOffset, 24f, chunkSize - 24f));
                    CreateRing(
                        position,
                        forward,
                        TraversalRingType.Flight,
                        ringTuning.FlightRingRadius,
                        originX,
                        originZ,
                        heightField,
                        materials,
                        player,
                        playerHealth,
                        ringExclusions,
                        worldSeed,
                        ringTuning,
                        $"extra-flight-{flightRingIndex}",
                        ringActivated);
                }

                if (hasRoutePair)
                {
                    CreateRing(routeBoostPosition, routeForward, TraversalRingType.GroundBoost, ringTuning.GroundRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "route-boost", ringActivated);
                }

                // Ground boost rings roll independently of the flight rings now, so a chunk can
                // carry both instead of the aerial roll deciding which kind the chunk gets.
                float groundBoostDensity = groundRingDensity
                    * Mathf.Max(0f, ringTuning.GroundBoostRingAmountMultiplier);
                int groundBoostRingCount = CountFromDensity(
                    groundBoostDensity,
                    coordinate,
                    worldSeed,
                    733);
                for (int groundBoostRingIndex = 0; groundBoostRingIndex < groundBoostRingCount; groundBoostRingIndex++)
                {
                    int saltOffset = groundBoostRingIndex * 12;
                    float angle = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        739 + saltOffset,
                        0f,
                        360f);
                    Vector3 forward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector2 position = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 743 + saltOffset, 16f, chunkSize - 16f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 751 + saltOffset, 16f, chunkSize - 16f));
                    CreateRing(
                        position,
                        forward,
                        TraversalRingType.GroundBoost,
                        ringTuning.GroundRingRadius,
                        originX,
                        originZ,
                        heightField,
                        materials,
                        player,
                        playerHealth,
                        ringExclusions,
                        worldSeed,
                        ringTuning,
                        $"boost-{groundBoostRingIndex}",
                        ringActivated);
                }
            }

            for (int collectibleIndex = 0; coordinate != Vector2Int.zero && collectibleIndex < 2; collectibleIndex++)
            {
                TraversalRingType collectibleType = collectibleIndex == 0
                    ? TraversalRingType.Health
                    : TraversalRingType.Coin;
                float density = collectibleType == TraversalRingType.Health
                    ? ringTuning.HealthRingDensityPerChunk
                    : ringTuning.CoinRingDensityPerChunk;
                float radius = collectibleType == TraversalRingType.Health
                    ? ringTuning.HealthRingRadius
                    : ringTuning.CoinRingRadius;
                int spawnSalt = collectibleType == TraversalRingType.Health ? 757 : 787;
                int collectibleSeed = collectibleType == TraversalRingType.Coin
                    ? coinRingSeed
                    : worldSeed;
                if (DuneVectorMath.Hash01(coordinate.x, coordinate.y, collectibleSeed, spawnSalt) >= density)
                {
                    continue;
                }

                Vector2 collectiblePosition = new Vector2(
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, collectibleSeed, spawnSalt + 4, 16f, chunkSize - 16f),
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, collectibleSeed, spawnSalt + 12, 16f, chunkSize - 16f));
                // Health rings are recovery pickups, so they must not lose their spawn roll
                // merely because another traversal ring happens to be nearby. Coin rings keep
                // the normal local spacing so their distribution remains unchanged.
                if (collectibleType != TraversalRingType.Health
                    && IsNearAny(collectiblePosition, ringExclusions, radius * 2f))
                {
                    continue;
                }

                float angle = DuneVectorMath.HashRange(
                    coordinate.x,
                    coordinate.y,
                    collectibleSeed,
                    spawnSalt + 16,
                    0f,
                    360f);
                Vector3 collectibleForward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                CreateRing(
                    collectiblePosition,
                    collectibleForward,
                    collectibleType,
                    radius,
                    originX,
                    originZ,
                    heightField,
                    materials,
                    player,
                    playerHealth,
                    ringExclusions,
                    collectibleSeed,
                    ringTuning,
                    collectibleType == TraversalRingType.Health ? "health" : "coin",
                    ringActivated);
            }

            CactusTuning cacti = cactusTuning ?? new CactusTuning();
            List<CactusPlacement> cactusPlacements = new List<CactusPlacement>();
            CollectCactusPlacements(coordinate, chunkSize, heightField, worldSeed, cacti, cactusPlacements);
            for (int i = 0; i < cactusPlacements.Count; i++)
            {
                CactusPlacement placement = cactusPlacements[i];
                Vector2 local = placement.Local;
                if (IsNearAny(local, ringExclusions, 9f))
                {
                    continue;
                }

                double logicalX = originX + local.x;
                double logicalZ = originZ + local.y;
                float y = (float)heightField.SampleHeight(logicalX, logicalZ) - Mathf.Max(0f, cacti.BurialDepth);
                DuneVectorVisuals.CreateCactus(
                    Root,
                    new Vector3(local.x, y, local.y),
                    placement.Height,
                    placement.Thickness,
                    placement.Yaw,
                    placement.Arms,
                    placement.InstanceSeed,
                    cacti,
                    materials.Cactus,
                    materials.CactusBlossom);
                sceneryExclusions.Add(local);
            }

            int pyramidCount = CountFromDensity(pyramidDensity, coordinate, worldSeed, 907);
            for (int i = 0; i < pyramidCount; i++)
            {
                Vector2 local = new Vector2(
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 911 + (i * 17), 12f, chunkSize - 12f),
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 919 + (i * 17), 12f, chunkSize - 12f));
                if (IsNearAny(local, ringExclusions, 13f))
                {
                    continue;
                }

                double logicalX = originX + local.x;
                double logicalZ = originZ + local.y;
                if (Vector3.Angle(heightField.SampleNormal(logicalX, logicalZ), Vector3.up) > pyramidMaximumPlacementSlope)
                {
                    continue;
                }

                float minimumScale = Mathf.Max(0.1f, pyramidMinimumScale);
                float maximumScale = Mathf.Max(minimumScale, pyramidMaximumScale);
                float scale = DuneVectorMath.HashRange(
                    coordinate.x,
                    coordinate.y,
                    worldSeed,
                    929 + (i * 17),
                    minimumScale,
                    maximumScale);
                if (geoglyphTuning != null && geoglyphTuning.OverlapsArtworkFootprint(
                        logicalX,
                        logicalZ,
                        scale))
                {
                    continue;
                }
                if (!TryReserveStructureFootprint(
                        logicalX,
                        logicalZ,
                        CalculateStructureFootprintRadius(scale),
                        landmarkTuning))
                {
                    continue;
                }
                float yaw = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 937 + (i * 17), 0f, 360f);
                float minimumBurial = Mathf.Max(0f, pyramidMinimumBurialDepth);
                float maximumBurial = Mathf.Max(minimumBurial, pyramidMaximumBurialDepth);
                float burial = DuneVectorMath.HashRange(
                    coordinate.x,
                    coordinate.y,
                    worldSeed,
                    941 + (i * 17),
                    minimumBurial,
                    maximumBurial);
                float footprintFloor = SampleLowestPyramidFootprintHeight(
                    heightField,
                    logicalX,
                    logicalZ,
                    scale,
                    yaw);
                float y = footprintFloor - burial;
                float hue = DuneVectorMath.HashRange(
                    coordinate.x,
                    coordinate.y,
                    worldSeed,
                    1009 + (i * 17),
                    0f,
                    1f);
                DuneVectorVisuals.CreatePyramid(
                    Root,
                    new Vector3(local.x, y, local.y),
                    scale,
                    yaw,
                    materials.PyramidPrefab,
                    materials.Sandstone,
                    materials.PyramidLodTuning,
                    hue);
                sceneryExclusions.Add(local);
            }

            if (darkPyramidTuning != null && materials.DarkPyramidPrefab != null)
            {
                int darkPyramidCount = CountFromDensity(
                    darkPyramidTuning.DensityPerChunk,
                    coordinate,
                    worldSeed,
                    1289);
                for (int i = 0; i < darkPyramidCount; i++)
                {
                    Vector2 local = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1291 + (i * 17), 12f, chunkSize - 12f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1297 + (i * 17), 12f, chunkSize - 12f));
                    if (IsNearAny(local, ringExclusions, 13f) || IsNearAny(local, sceneryExclusions, 13f))
                    {
                        continue;
                    }

                    double logicalX = originX + local.x;
                    double logicalZ = originZ + local.y;
                    if (Vector3.Angle(heightField.SampleNormal(logicalX, logicalZ), Vector3.up) >
                        darkPyramidTuning.MaximumPlacementSlope)
                    {
                        continue;
                    }

                    float minimumScale = Mathf.Max(0.1f, darkPyramidTuning.MinimumScale);
                    float maximumScale = Mathf.Max(minimumScale, darkPyramidTuning.MaximumScale);
                    float scale = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        1301 + (i * 17),
                        minimumScale,
                        maximumScale);
                    if (geoglyphTuning != null && geoglyphTuning.OverlapsArtworkFootprint(
                            logicalX,
                            logicalZ,
                            scale))
                    {
                        continue;
                    }
                    if (!TryReserveStructureFootprint(
                            logicalX,
                            logicalZ,
                            CalculateStructureFootprintRadius(scale),
                            landmarkTuning))
                    {
                        continue;
                    }
                    float yaw = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1303 + (i * 17), 0f, 360f);
                    float minimumBurial = Mathf.Max(0f, darkPyramidTuning.MinimumBurialDepth);
                    float maximumBurial = Mathf.Max(minimumBurial, darkPyramidTuning.MaximumBurialDepth);
                    float burial = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        1307 + (i * 17),
                        minimumBurial,
                        maximumBurial);
                    float footprintFloor = SampleLowestPyramidFootprintHeight(
                        heightField,
                        logicalX,
                        logicalZ,
                        scale,
                        yaw);
                    float y = footprintFloor - burial;
                    float hue = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        1319 + (i * 17),
                        0f,
                        1f);
                    DuneVectorVisuals.CreatePyramid(
                        Root,
                        new Vector3(local.x, y, local.y),
                        scale,
                        yaw,
                        materials.DarkPyramidPrefab,
                        materials.Sandstone,
                        materials.DarkPyramidLodTuning,
                        hue,
                        "Small Dark Pyramid",
                        DuneVectorCompendiumSubjectIds.DarkPyramid);
                    sceneryExclusions.Add(local);
                }
            }

            if (pyramid2Tuning != null && materials.Pyramid2Prefab != null)
            {
                int pyramid2Count = CountFromDensity(
                    pyramid2Tuning.DensityPerChunk,
                    coordinate,
                    worldSeed,
                    1361);
                for (int i = 0; i < pyramid2Count; i++)
                {
                    Vector2 local = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1367 + (i * 17), 12f, chunkSize - 12f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1373 + (i * 17), 12f, chunkSize - 12f));
                    if (IsNearAny(local, ringExclusions, 13f) || IsNearAny(local, sceneryExclusions, 13f))
                    {
                        continue;
                    }

                    double logicalX = originX + local.x;
                    double logicalZ = originZ + local.y;
                    if (Vector3.Angle(heightField.SampleNormal(logicalX, logicalZ), Vector3.up) >
                        pyramid2Tuning.MaximumPlacementSlope)
                    {
                        continue;
                    }

                    float minimumScale = Mathf.Max(0.1f, pyramid2Tuning.MinimumScale);
                    float maximumScale = Mathf.Max(minimumScale, pyramid2Tuning.MaximumScale);
                    float scale = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        1381 + (i * 17),
                        minimumScale,
                        maximumScale);
                    if (geoglyphTuning != null && geoglyphTuning.OverlapsArtworkFootprint(
                            logicalX,
                            logicalZ,
                            scale))
                    {
                        continue;
                    }
                    if (!TryReserveStructureFootprint(
                            logicalX,
                            logicalZ,
                            CalculateStructureFootprintRadius(scale),
                            landmarkTuning))
                    {
                        continue;
                    }
                    float yaw = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1399 + (i * 17), 0f, 360f);
                    float minimumBurial = Mathf.Max(0f, pyramid2Tuning.MinimumBurialDepth);
                    float maximumBurial = Mathf.Max(minimumBurial, pyramid2Tuning.MaximumBurialDepth);
                    float burial = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        1409 + (i * 17),
                        minimumBurial,
                        maximumBurial);
                    float footprintFloor = SampleLowestPyramidFootprintHeight(
                        heightField,
                        logicalX,
                        logicalZ,
                        scale,
                        yaw);
                    float y = footprintFloor - burial;
                    float hue = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        1423 + (i * 17),
                        0f,
                        1f);
                    DuneVectorVisuals.CreatePyramid(
                        Root,
                        new Vector3(local.x, y, local.y),
                        scale,
                        yaw,
                        materials.Pyramid2Prefab,
                        materials.Sandstone,
                        materials.Pyramid2LodTuning,
                        hue,
                        "Pyramid 2",
                        DuneVectorCompendiumSubjectIds.Pyramid2);
                    sceneryExclusions.Add(local);
                }
            }

            if (obeliskTuning != null && materials.ObeliskPrefab != null)
            {
                int obeliskCount = CountFromDensity(obeliskTuning.DensityPerChunk, coordinate, worldSeed, 947);
                for (int i = 0; i < obeliskCount; i++)
                {
                    Vector2 local = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 953 + (i * 17), 12f, chunkSize - 12f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 967 + (i * 17), 12f, chunkSize - 12f));
                    if (IsNearAny(local, ringExclusions, 13f) || IsNearAny(local, sceneryExclusions, 13f))
                    {
                        continue;
                    }

                    double logicalX = originX + local.x;
                    double logicalZ = originZ + local.y;
                    if (Vector3.Angle(heightField.SampleNormal(logicalX, logicalZ), Vector3.up) > obeliskTuning.MaximumPlacementSlope)
                    {
                        continue;
                    }

                    float minimumScale = Mathf.Max(0.1f, obeliskTuning.MinimumScale);
                    float maximumScale = Mathf.Max(minimumScale, obeliskTuning.MaximumScale);
                    float scale = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        971 + (i * 17),
                        minimumScale,
                        maximumScale);
                    if (geoglyphTuning != null && geoglyphTuning.OverlapsArtworkFootprint(
                            logicalX,
                            logicalZ,
                            scale))
                    {
                        continue;
                    }
                    if (!TryReserveStructureFootprint(
                            logicalX,
                            logicalZ,
                            CalculateStructureFootprintRadius(scale),
                            landmarkTuning))
                    {
                        continue;
                    }
                    float yaw = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 977 + (i * 17), 0f, 360f);
                    float minimumBurial = Mathf.Max(0f, obeliskTuning.MinimumBurialDepth);
                    float maximumBurial = Mathf.Max(minimumBurial, obeliskTuning.MaximumBurialDepth);
                    float burial = DuneVectorMath.HashRange(
                        coordinate.x,
                        coordinate.y,
                        worldSeed,
                        983 + (i * 17),
                        minimumBurial,
                        maximumBurial);
                    float footprintFloor = SampleLowestPyramidFootprintHeight(
                        heightField,
                        logicalX,
                        logicalZ,
                        scale,
                        yaw);
                    float y = footprintFloor - burial;
                    DuneVectorVisuals.CreateObelisk(
                        Root,
                        new Vector3(local.x, y, local.y),
                        scale,
                        yaw,
                        materials.ObeliskPrefab,
                        materials.ObeliskLodTuning);
                    sceneryExclusions.Add(local);
                }
            }

            SpawnGroundExploders(
                coordinate,
                chunkSize,
                heightField,
                materials,
                player,
                playerHealth,
                enemySpawnSeed,
                originX,
                originZ,
                ringExclusions,
                sceneryExclusions,
                groundExploderTuning);

            _shrubs = new DesertShrubField(
                coordinate,
                Root,
                chunkSize,
                heightField,
                worldSeed,
                shrubTuning,
                landmarkTuning,
                ringExclusions,
                sceneryExclusions);
        }

        private static float SampleLowestPyramidFootprintHeight(
            DuneHeightField heightField,
            double centerX,
            double centerZ,
            float halfExtent,
            float yaw)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            float lowestHeight = float.PositiveInfinity;

            // A regular footprint grid catches dune troughs beneath the pyramid instead
            // of relying only on its center, which can sit much higher than its corners.
            const int intervals = 4;
            for (int z = 0; z <= intervals; z++)
            {
                float normalizedZ = Mathf.Lerp(-1f, 1f, z / (float)intervals);
                for (int x = 0; x <= intervals; x++)
                {
                    float normalizedX = Mathf.Lerp(-1f, 1f, x / (float)intervals);
                    Vector3 offset = rotation * new Vector3(normalizedX * halfExtent, 0f, normalizedZ * halfExtent);
                    float height = (float)heightField.SampleHeight(centerX + offset.x, centerZ + offset.z);
                    lowestHeight = Mathf.Min(lowestHeight, height);
                }
            }

            return lowestHeight;
        }

        private void SpawnGroundExploders(
            Vector2Int coordinate,
            float chunkSize,
            DuneHeightField heightField,
            DuneVectorMaterials materials,
            DroneCharacterController player,
            DroneHealth playerHealth,
            int enemySpawnSeed,
            double originX,
            double originZ,
            List<Vector2> exclusions,
            List<Vector2> sceneryExclusions,
            GroundExploderTuning settings)
        {
            if (DuneTrainingRuntime.ControlledPreHazardStage || settings == null || !settings.Enabled ||
                settings.DensityPerChunk <= 0f || coordinate == Vector2Int.zero)
            {
                return;
            }

            int count = CountFromDensity(settings.DensityPerChunk, coordinate, enemySpawnSeed, 1201);
            count = Mathf.CeilToInt(count * DuneVectorContractRisk.EnemySpawnMultiplier);
            for (int i = 0; i < count; i++)
            {
                Vector2 local = new Vector2(
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, enemySpawnSeed, 1211 + (i * 23), 10f, chunkSize - 10f),
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, enemySpawnSeed, 1217 + (i * 23), 10f, chunkSize - 10f));
                if (IsNearAny(local, exclusions, Mathf.Max(10f, settings.DetectionRadius * 0.7f)))
                {
                    continue;
                }

                double logicalX = originX + local.x;
                double logicalZ = originZ + local.y;
                if (DuneVectorEnemySpawnClearance.IsBlocked(logicalX, logicalZ))
                {
                    continue;
                }

                Vector3 normal = heightField.SampleNormal(logicalX, logicalZ);
                if (Vector3.Angle(normal, Vector3.up) > settings.MaximumGroundSlope)
                {
                    continue;
                }

                float yaw = DuneVectorMath.HashRange(
                    coordinate.x,
                    coordinate.y,
                    enemySpawnSeed,
                    1223 + (i * 23),
                    0f,
                    360f);
                float height = (float)heightField.SampleHeight(logicalX, logicalZ);
                GameObject enemyObject = new GameObject($"Ground Exploder {i + 1:00}");
                enemyObject.transform.SetParent(Root, false);
                enemyObject.transform.localPosition = new Vector3(local.x, height, local.y);
                enemyObject.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                int identity = unchecked((int)DuneVectorMath.Hash(
                    coordinate.x,
                    coordinate.y,
                    enemySpawnSeed,
                    1231 + (i * 23)));
                GroundExploderEnemy enemy = enemyObject.AddComponent<GroundExploderEnemy>();
                enemy.Initialize(
                    player,
                    playerHealth,
                    heightField,
                    originX,
                    originZ,
                    chunkSize,
                    materials,
                    settings,
                    identity);
                _groundExploders.Add(enemy);
                sceneryExclusions.Add(local);
            }
        }

        private void CreateRing(
            Vector2 local,
            Vector3 forward,
            TraversalRingType type,
            float radius,
            double originX,
            double originZ,
            DuneHeightField heightField,
            DuneVectorMaterials materials,
            DroneCharacterController player,
            DroneHealth playerHealth,
            List<Vector2> exclusions,
            int worldSeed,
            RingTuning ringTuning,
            string identitySuffix,
            Action<TraversalRing> ringActivated)
        {
            bool usesSharedRingSpacing = type != TraversalRingType.Health;
            float minimumRingSeparation = Mathf.Max(0f, ringTuning.MinimumRingSeparation);
            if (usesSharedRingSpacing && IsNearAny(local, exclusions, minimumRingSeparation))
            {
                return;
            }

            double logicalX = originX + local.x;
            double logicalZ = originZ + local.y;
            double hubOffsetX = logicalX - DesertWorldStreamer.StartingLogicalPosition.x;
            double hubOffsetZ = logicalZ - DesertWorldStreamer.StartingLogicalPosition.y;
            double hubExclusionRadius = Math.Max(0d, ringTuning.HubExclusionRadius);
            if ((hubOffsetX * hubOffsetX) + (hubOffsetZ * hubOffsetZ)
                < hubExclusionRadius * hubExclusionRadius)
            {
                return;
            }

            // A portal spins to face the camera, so the space it needs is a sphere of its
            // visual radius rather than the thin disc it happens to be showing right now.
            // Nothing solid may sit inside that sphere in any direction the portal can face.
            float clearanceRadius = CalculatePortalClearanceRadius(radius, ringTuning);
            if (DuneVectorWorldOccupancy.Overlaps(
                    logicalX,
                    logicalZ,
                    clearanceRadius,
                    WorldOccupancyKind.Structure))
            {
                return;
            }

            // Portals stream in per chunk, so the per-chunk exclusion list cannot
            // see a neighbour placed just across a chunk border. Half the minimum
            // separation registered on both portals makes the overlap test measure the
            // full center-to-center distance.
            float portalSpacingRadius = usesSharedRingSpacing
                ? Mathf.Max(0f, ringTuning.MinimumPortalSeparation) * 0.5f
                : 0f;
            if (portalSpacingRadius > 0f && DuneVectorWorldOccupancy.Overlaps(
                    logicalX,
                    logicalZ,
                    portalSpacingRadius,
                    WorldOccupancyKind.PortalSpacing))
            {
                return;
            }

            // Ground and flight portals additionally hold a wider distance from their own
            // kind, so a run of same-type portals never bunches up even where the shared
            // spacing above would allow it.
            WorldOccupancyKind sameTypeSpacingKind = type == TraversalRingType.GroundBoost
                ? WorldOccupancyKind.GroundPortalSpacing
                : WorldOccupancyKind.FlightPortalSpacing;
            float sameTypeSpacingRadius = type switch
            {
                TraversalRingType.GroundBoost =>
                    Mathf.Max(0f, ringTuning.MinimumGroundPortalSeparation) * 0.5f,
                TraversalRingType.Flight or TraversalRingType.UpperFlight =>
                    Mathf.Max(0f, ringTuning.MinimumFlightPortalSeparation) * 0.5f,
                _ => 0f,
            };
            if (sameTypeSpacingRadius > 0f && DuneVectorWorldOccupancy.Overlaps(
                    logicalX,
                    logicalZ,
                    sameTypeSpacingRadius,
                    sameTypeSpacingKind))
            {
                return;
            }

            DuneVectorLandmarkDirector landmarks = DuneVectorBootstrap.Instance != null
                ? DuneVectorBootstrap.Instance.LandmarkDirector
                : null;
            if (landmarks != null && landmarks.OverlapsLandmarkFootprint(
                    logicalX,
                    logicalZ,
                    clearanceRadius))
            {
                return;
            }

            float terrainHeight = (float)heightField.SampleHeight(logicalX, logicalZ);
            float minimumHeight = type switch
            {
                TraversalRingType.GroundBoost => ringTuning.GroundRingMinimumHeight,
                TraversalRingType.Flight => ringTuning.FlightRingMinimumHeight,
                TraversalRingType.Health => ringTuning.HealthRingMinimumHeight,
                _ => ringTuning.UpperFlightRingMinimumHeight,
            };
            float maximumHeight = type switch
            {
                TraversalRingType.GroundBoost => ringTuning.GroundRingMaximumHeight,
                TraversalRingType.Flight => ringTuning.FlightRingMaximumHeight,
                TraversalRingType.Health => ringTuning.HealthRingMaximumHeight,
                _ => ringTuning.UpperFlightRingMaximumHeight,
            };
            maximumHeight = Mathf.Max(minimumHeight, maximumHeight);
            int heightSalt = unchecked(
                (Mathf.RoundToInt(local.x * 10f) * 73856093)
                ^ (Mathf.RoundToInt(local.y * 10f) * 19349663)
                ^ ((int)type * 83492791));
            float heightOffset = DuneVectorMath.HashRange(
                Coordinate.x,
                Coordinate.y,
                worldSeed,
                heightSalt,
                minimumHeight,
                maximumHeight);
            Vector3 candidatePosition = Root.TransformPoint(
                new Vector3(local.x, terrainHeight + heightOffset, local.y));
            if (IsNearDrone(
                candidatePosition,
                player,
                Mathf.Max(0f, ringTuning.MinimumDroneSpawnSeparation)))
            {
                return;
            }

            GameObject ringObject = new GameObject("Traversal Ring");
            ringObject.transform.SetParent(Root, false);
            ringObject.transform.localPosition = new Vector3(local.x, terrainHeight + heightOffset, local.y);
            Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            ringObject.transform.localRotation = Quaternion.LookRotation(planarForward, Vector3.up);

            TraversalRing ring = ringObject.AddComponent<TraversalRing>();
            string identity = $"{Coordinate.x}:{Coordinate.y}:{identitySuffix}";
            ring.Initialize(
                type,
                player,
                playerHealth,
                materials,
                radius,
                ringTuning,
                identity);
            if (type == TraversalRingType.Health)
            {
                HealthRingReward reward = ringObject.AddComponent<HealthRingReward>();
                reward.Initialize(playerHealth, ringTuning.HealthRestored);
                ring.SetCollectibleReward(reward);
            }
            else if (type == TraversalRingType.Coin)
            {
                CoinRingReward reward = ringObject.AddComponent<CoinRingReward>();
                reward.Initialize(
                    player != null ? player.GetComponent<DroneGoldWallet>() : null,
                    ringTuning.GoldReward,
                    ringTuning.CoinRingMinimumGoldMultiplier,
                    ringTuning.CoinRingMaximumGoldMultiplier);
                ring.SetCollectibleReward(reward);
            }
            ring.BoostRingActiveScale = ringTuning.BoostRingActiveScale;
            ring.FlightModeScale = ringTuning.FlightModeScale;
            ring.FlightModeScaleSharpness = ringTuning.ScaleSharpness;
            ring.ClockwiseRotationSpeed = type switch
            {
                TraversalRingType.Health => ringTuning.HealthRingRotationSpeed,
                TraversalRingType.Coin => ringTuning.CoinRingRotationSpeed,
                _ => ringTuning.ClockwiseRotationSpeed,
            };
            if (ringActivated != null)
            {
                ring.Activated += ringActivated;
            }
            if (type == TraversalRingType.Flight || type == TraversalRingType.Health)
            {
                float minimumLift = Mathf.Max(0f, ringTuning.FlightModeMinimumHeightOffset);
                float maximumLift = Mathf.Max(minimumLift, ringTuning.FlightModeMaximumHeightOffset);
                ring.FlightModeHeightOffset = DuneVectorMath.HashRange(
                    Coordinate.x,
                    Coordinate.y,
                    worldSeed,
                    heightSalt ^ 486187739,
                    minimumLift,
                    maximumLift);
                ring.FlightModeHeightSharpness = ringTuning.FlightModeHeightSharpness;
                ring.FlightModeGroundResetHeightSharpness = ringTuning.FlightModeGroundResetHeightSharpness;
                ring.ApplyInitialFlightModePresentation(ringTuning.FlightRingSpawnScale);
            }
            _rings.Add(ring);
            exclusions.Add(local);
            _occupancyHandles.Add(DuneVectorWorldOccupancy.Register(
                logicalX,
                logicalZ,
                clearanceRadius,
                WorldOccupancyKind.Portal));
            if (portalSpacingRadius > 0f)
            {
                _occupancyHandles.Add(DuneVectorWorldOccupancy.Register(
                    logicalX,
                    logicalZ,
                    portalSpacingRadius,
                    WorldOccupancyKind.PortalSpacing));
            }
            if (sameTypeSpacingRadius > 0f)
            {
                _occupancyHandles.Add(DuneVectorWorldOccupancy.Register(
                    logicalX,
                    logicalZ,
                    sameTypeSpacingRadius,
                    sameTypeSpacingKind));
            }
        }

        private static float CalculatePortalClearanceRadius(float radius, RingTuning ringTuning)
        {
            float visualRadius = DuneVectorVisuals.CalculatePortalVisualRadius(radius, ringTuning);
            return (visualRadius * Mathf.Max(1f, ringTuning.PortalStructureClearanceMultiplier))
                + Mathf.Max(0f, ringTuning.PortalStructureClearancePadding);
        }

        /// <summary>
        /// Rebuilds the deterministic cactus placements for a chunk without instantiating anything.
        /// Chunk generation consumes this to spawn the meshes, and the deployment placement query
        /// consumes it to keep the drone off a cactus even when that chunk has not streamed in yet.
        /// Ring exclusions are deliberately not applied here so the query stays conservative.
        /// </summary>
        internal static void CollectCactusPlacements(
            Vector2Int coordinate,
            float chunkSize,
            DuneHeightField heightField,
            int worldSeed,
            CactusTuning cactusTuning,
            List<CactusPlacement> results)
        {
            results.Clear();
            if (heightField == null)
            {
                return;
            }

            CactusTuning cacti = cactusTuning ?? new CactusTuning();
            double originX = coordinate.x * (double)chunkSize;
            double originZ = coordinate.y * (double)chunkSize;
            float regionNoise = (float)DuneVectorMath.ValueNoise((coordinate.x + 0.5) * 0.42, (coordinate.y + 0.5) * 0.42, worldSeed, 811);
            int cactusCount = regionNoise < -0.36f ? 0 : CountFromDensity(cacti.DensityPerChunk * Mathf.Lerp(0.35f, 1.25f, (regionNoise + 1f) * 0.5f), coordinate, worldSeed, 821);
            Vector2 clusterCenter = new Vector2(
                DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 823, 8f, chunkSize - 8f),
                DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 827, 8f, chunkSize - 8f));

            for (int i = 0; i < cactusCount; i++)
            {
                Vector2 randomPosition = new Vector2(
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 839 + (i * 13), 5f, chunkSize - 5f),
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 843 + (i * 13), 5f, chunkSize - 5f));
                float clusterMix = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 849 + (i * 13), 0.1f, 0.72f);
                Vector2 local = Vector2.Lerp(randomPosition, clusterCenter, clusterMix);

                double logicalX = originX + local.x;
                double logicalZ = originZ + local.y;
                Vector3 normal = heightField.SampleNormal(logicalX, logicalZ);
                if (Vector3.Angle(normal, Vector3.up) > cacti.MaximumPlacementSlope)
                {
                    continue;
                }

                float minimumHeight = Mathf.Max(0.1f, cacti.MinimumHeight);
                float maximumHeight = Mathf.Max(minimumHeight, cacti.MaximumHeight);
                float minimumThickness = Mathf.Max(0.05f, cacti.MinimumThickness);
                float maximumThickness = Mathf.Max(minimumThickness, cacti.MaximumThickness);
                int minimumArms = Mathf.Clamp(cacti.MinimumArmCount, 0, 5);
                int maximumArms = Mathf.Clamp(Mathf.Max(minimumArms, cacti.MaximumArmCount), minimumArms, 5);
                int arms = minimumArms + Mathf.FloorToInt(
                    DuneVectorMath.Hash01(coordinate.x, coordinate.y, worldSeed, 877 + (i * 13))
                    * ((maximumArms - minimumArms) + 1));

                results.Add(new CactusPlacement
                {
                    Local = local,
                    Height = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 853 + (i * 13), minimumHeight, maximumHeight),
                    Thickness = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 857 + (i * 13), minimumThickness, maximumThickness),
                    Yaw = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 863 + (i * 13), 0f, 360f),
                    Arms = Mathf.Min(maximumArms, arms),
                    InstanceSeed = unchecked((coordinate.x * 73856093) ^ (coordinate.y * 19349663) ^ (i * 83492791) ^ worldSeed),
                });
            }
        }

        // Pyramids and obelisks are authored around a half-extent, so the circle that
        // covers their rotated footprint is that half-extent across the diagonal.
        private static float CalculateStructureFootprintRadius(float scale)
        {
            return Mathf.Max(0f, scale) * 1.4142136f;
        }

        private bool TryReserveStructureFootprint(
            double logicalX,
            double logicalZ,
            float footprintRadius,
            LandmarkSystemTuning landmarkTuning)
        {
            if (DuneVectorWorldOccupancy.Overlaps(
                    logicalX,
                    logicalZ,
                    footprintRadius,
                    WorldOccupancyKind.Portal))
            {
                return false;
            }

            // Landmarks stream from their own placement grid and never register an
            // occupancy footprint, so a pyramid or obelisk has to test their exclusion
            // discs directly or it will spawn straight through the landmark mesh.
            DuneVectorLandmarkDirector landmarks = DuneVectorBootstrap.Instance != null
                ? DuneVectorBootstrap.Instance.LandmarkDirector
                : null;
            if (landmarks != null && landmarks.OverlapsLandmarkFootprint(
                    logicalX,
                    logicalZ,
                    footprintRadius + (landmarkTuning != null
                        ? Mathf.Max(0f, landmarkTuning.StructureClearance)
                        : 0f)))
            {
                return false;
            }

            _occupancyHandles.Add(DuneVectorWorldOccupancy.Register(
                logicalX,
                logicalZ,
                footprintRadius,
                WorldOccupancyKind.Structure));
            return true;
        }

        private static int CountFromDensity(float density, Vector2Int coordinate, int seed, int salt)
        {
            int count = Mathf.FloorToInt(Mathf.Max(0f, density));
            float fraction = Mathf.Max(0f, density) - count;
            if (DuneVectorMath.Hash01(coordinate.x, coordinate.y, seed, salt) < fraction)
            {
                count++;
            }
            return count;
        }

        private static bool IsNearAny(Vector2 position, List<Vector2> exclusions, float distance)
        {
            float distanceSquared = distance * distance;
            for (int i = 0; i < exclusions.Count; i++)
            {
                if ((position - exclusions[i]).sqrMagnitude < distanceSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNearDrone(
            Vector3 position,
            DroneCharacterController player,
            float horizontalDistance)
        {
            if (horizontalDistance <= 0f)
            {
                return false;
            }

            float distanceSquared = horizontalDistance * horizontalDistance;
            if (player != null && HorizontalSqrDistance(position, player.WorldCenter) < distanceSquared)
            {
                return true;
            }

            foreach (DynamicCourierAgent courier in DynamicCourierAgent.ActiveAgents)
            {
                if (courier != null
                    && courier.IsAlive
                    && HorizontalSqrDistance(position, courier.transform.position) < distanceSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float deltaX = a.x - b.x;
            float deltaZ = a.z - b.z;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }
    }
}
