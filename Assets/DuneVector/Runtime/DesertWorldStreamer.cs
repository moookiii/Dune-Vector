using System;
using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class DesertWorldStreamer : MonoBehaviour
    {
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
        [Range(2, 12)] public int UnloadRadius = 4;
        [Range(1, 4)] public int ChunksGeneratedPerFrame = 1;
        [Min(50f)] public float FloatingOriginThreshold = 520f;

        [Header("Dunes")]
        public DuneFieldSettings Dunes = new DuneFieldSettings();

        [Header("Clouds")]
        public CloudTuning Clouds;

        [Header("Desert Shrubs")]
        public DesertShrubTuning Shrubs;
        public LandmarkSystemTuning Landmarks;
        public CactusTuning Cacti;

        [Header("Spawning - expected count per chunk")]
        [Min(0f)] public float PyramidDensity = 0.22f;
        [Min(0.1f)] public float PyramidMinimumScale = 2f;
        [Min(0.1f)] public float PyramidMaximumScale = 4.4f;
        [Range(0f, 89f)] public float PyramidMaximumPlacementSlope = 24f;
        [Min(0f)] public float PyramidMinimumBurialDepth = 0.75f;
        [Min(0f)] public float PyramidMaximumBurialDepth = 1.25f;
        [Range(0f, 2f)] public float GroundRingDensity = 0.48f;
        [Range(0f, 1f)] public float AerialRingDensity = 0.14f;

        [Header("Ring Sizes")]
        public RingTuning Rings = new RingTuning();

        [Header("Ground Explosive Enemies")]
        public GroundExploderTuning GroundExploders = new GroundExploderTuning();

        [Header("Debug")]
        public bool DrawChunkBounds;

        public DuneHeightField HeightField { get; private set; }
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

        public event Action<Vector3> WorldShifted;

        private readonly Dictionary<Vector2Int, DesertChunk> _chunks = new Dictionary<Vector2Int, DesertChunk>();
        private readonly Queue<Vector2Int> _generationQueue = new Queue<Vector2Int>();
        private readonly HashSet<Vector2Int> _queuedCoordinates = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _candidateCoordinates = new List<Vector2Int>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();
        private readonly HashSet<string> _activatedFlightRingIdentities = new HashSet<string>();
        private readonly List<ContractGroundExploderSpawn> _contractGroundExploders = new List<ContractGroundExploderSpawn>();
        private Vector2Int _candidateSortCenter;

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
        private bool _initialized;

        public void Initialize(DuneVectorMaterials materials)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _materials.SetGeoglyphLogicalOrigin(OriginOffsetX, OriginOffsetZ);
            _coinRingSeed = Guid.NewGuid().GetHashCode();
            Rings ??= new RingTuning();
            GroundExploders ??= new GroundExploderTuning();
            Dunes.WorldSeed = WorldSeed;
            HeightField = new DuneHeightField(Dunes);

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
                    GenerateChunkImmediate(initialChunk + new Vector2Int(x, z));
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

        public void SetContractGroundExploders(int count, float minimumDistance, float maximumDistance, int seed)
        {
            ClearContractGroundExploders();
            if (count <= 0 || GroundExploders == null || !GroundExploders.Enabled ||
                _player == null || _playerHealth == null || HeightField == null || _chunkRoot == null)
            {
                return;
            }

            float explosionRadius = GroundExploders.EvaluateExplosionRadius(DuneVectorContractRisk.CurrentRisk);
            float minimum = Mathf.Max(GroundExploders.DetectionRadius + explosionRadius, minimumDistance);
            float maximum = Mathf.Max(minimum, maximumDistance);
            LogicalPosition playerLogical = LogicalPlayerPosition;
            int spawned = 0;
            int attempts = Mathf.Max(count * 12, count);
            for (int attempt = 0; attempt < attempts && spawned < count; attempt++)
            {
                float angle = DuneVectorMath.HashRange(seed, attempt, WorldSeed, 1249, 0f, Mathf.PI * 2f);
                float distance = DuneVectorMath.HashRange(seed, attempt, WorldSeed, 1259, minimum, maximum);
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
                float yaw = DuneVectorMath.HashRange(seed, attempt, WorldSeed, 1277, 0f, 360f);
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
                    unchecked((int)DuneVectorMath.Hash(seed, attempt, WorldSeed, 1289)));
                _contractGroundExploders.Add(new ContractGroundExploderSpawn
                {
                    ChunkCoordinate = coordinate,
                    Root = root,
                    Enemy = enemy,
                });
                spawned++;
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

            float deltaTime = Time.deltaTime;
            _streamingRefreshTimer -= deltaTime;
            Vector2Int playerChunk = GetPlayerLogicalChunk();
            if (_streamingRefreshTimer <= 0f || playerChunk != _lastScheduledChunk)
            {
                _streamingRefreshTimer = 0.18f;
                ScheduleStreaming(force: playerChunk != _lastScheduledChunk);
            }

            int generated = 0;
            while (generated < ChunksGeneratedPerFrame && _generationQueue.Count > 0)
            {
                Vector2Int coordinate = _generationQueue.Dequeue();
                _queuedCoordinates.Remove(coordinate);
                int distance = ChebyshevDistance(coordinate, playerChunk);
                if (_chunks.ContainsKey(coordinate) || distance > PreloadRadius + 1)
                {
                    continue;
                }

                GenerateChunkImmediate(coordinate);
                generated++;
            }
        }

        internal void TickStreamedObjects(float deltaTime)
        {
            if (!_initialized)
            {
                return;
            }

            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.Tick(deltaTime);
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

            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.FixedTick(fixedDeltaTime);
            }
            PruneContractGroundExploders();
            for (int i = 0; i < _contractGroundExploders.Count; i++)
            {
                _contractGroundExploders[i].Enemy.FixedTick(fixedDeltaTime);
            }
        }

        internal void LateTickStreamedObjects(float deltaTime)
        {
            if (!_initialized)
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
            if (_motor == null)
            {
                return;
            }

            Vector3 localPosition = _motor.TransientPosition;
            if ((localPosition.x * localPosition.x) + (localPosition.z * localPosition.z) < FloatingOriginThreshold * FloatingOriginThreshold)
            {
                return;
            }

            float shiftX = Mathf.Round(localPosition.x / ChunkSize) * ChunkSize;
            float shiftZ = Mathf.Round(localPosition.z / ChunkSize) * ChunkSize;
            if (Mathf.Abs(shiftX) < 0.01f && Mathf.Abs(shiftZ) < 0.01f)
            {
                return;
            }
            RebaseNow(new Vector3(shiftX, 0f, shiftZ));
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
            _materials.SetGeoglyphLogicalOrigin(OriginOffsetX, OriginOffsetZ);

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
            Vector2Int playerChunk = GetPlayerLogicalChunk();
            CurrentLogicalChunk = playerChunk;
            if (!force && playerChunk == _lastScheduledChunk)
            {
                return;
            }
            _lastScheduledChunk = playerChunk;

            // The normal generation queue is intentionally throttled, but terrain collision cannot be.
            // A fast flight can cross a chunk boundary before the queued chunk receives a MeshCollider,
            // so keep the player's chunk and every chunk touching it available synchronously.
            EnsureCollisionNeighborhood(playerChunk);

            if (force)
            {
                _generationQueue.Clear();
                _queuedCoordinates.Clear();
            }

            _candidateCoordinates.Clear();
            int radius = Mathf.Min(
                Mathf.Max(ActiveRadius, PreloadRadius),
                Mathf.Max(1, PreloadRadius) + 1);
            for (int z = -radius; z <= radius; z++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    Vector2Int coordinate = playerChunk + new Vector2Int(x, z);
                    if (!_chunks.ContainsKey(coordinate) && !_queuedCoordinates.Contains(coordinate))
                    {
                        _candidateCoordinates.Add(coordinate);
                    }
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
                if (ChebyshevDistance(entry.Key, playerChunk) > UnloadRadius)
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
        }

        private void EnsureCollisionNeighborhood(Vector2Int playerChunk)
        {
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    GenerateChunkImmediate(playerChunk + new Vector2Int(x, z));
                }
            }
        }

        private void GenerateChunkImmediate(Vector2Int coordinate)
        {
            if (_chunks.ContainsKey(coordinate))
            {
                return;
            }

            DesertChunk chunk = new DesertChunk(
                coordinate,
                _chunkRoot,
                OriginOffsetX,
                OriginOffsetZ,
                ChunkSize,
                ChunkResolution,
                HeightField,
                _materials,
                Clouds,
                GetCloudDensityPerChunk(),
                _player,
                _playerHealth,
                WorldSeed,
                _coinRingSeed,
                Cacti,
                PyramidDensity,
                PyramidMinimumScale,
                PyramidMaximumScale,
                PyramidMaximumPlacementSlope,
                PyramidMinimumBurialDepth,
                PyramidMaximumBurialDepth,
                GroundRingDensity,
                AerialRingDensity,
                Rings,
                GroundExploders,
                Shrubs,
                Landmarks,
                HandleTraversalRingActivated);
            _chunks.Add(coordinate, chunk);
            if (IsUpperFlightRingUnlocked)
            {
                chunk.SpawnUpperFlightLayers();
            }
            GeneratedChunkCount++;
            PeakActiveChunkCount = Mathf.Max(PeakActiveChunkCount, _chunks.Count);
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
                || !_activatedFlightRingIdentities.Add(ring.ProceduralIdentity))
            {
                return;
            }

            int requiredPasses = Mathf.Max(1, Rings.UpperFlightRingRequiredPasses);
            if (_activatedFlightRingIdentities.Count >= requiredPasses)
            {
                SpawnUpperFlightLayersForLoadedChunks();
            }
        }

        private void SpawnUpperFlightLayersForLoadedChunks()
        {
            foreach (DesertChunk chunk in _chunks.Values)
            {
                chunk.SpawnUpperFlightLayers();
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
            return ChebyshevDistance(left, _candidateSortCenter)
                .CompareTo(ChebyshevDistance(right, _candidateSortCenter));
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

    internal sealed class DesertChunk : IDisposable
    {
        private static readonly Dictionary<int, int[]> TriangleIndicesByResolution = new Dictionary<int, int[]>();

        public Vector2Int Coordinate { get; }
        public Transform Root { get; }

        private readonly List<TraversalRing> _rings = new List<TraversalRing>();
        private readonly List<GroundExploderEnemy> _groundExploders = new List<GroundExploderEnemy>();
        private readonly Mesh _terrainMesh;
        private readonly float _chunkSize;
        private readonly DuneHeightField _heightField;
        private readonly int _worldSeed;
        private readonly RingTuning _ringTuning;
        private DuneVectorCloudField _cloudField;
        private DesertShrubField _shrubs;

        public DesertChunk(
            Vector2Int coordinate,
            Transform parent,
            double originOffsetX,
            double originOffsetZ,
            float chunkSize,
            int resolution,
            DuneHeightField heightField,
            DuneVectorMaterials materials,
            CloudTuning cloudTuning,
            float cloudDensity,
            DroneCharacterController player,
            DroneHealth playerHealth,
            int worldSeed,
            int coinRingSeed,
            CactusTuning cactusTuning,
            float pyramidDensity,
            float pyramidMinimumScale,
            float pyramidMaximumScale,
            float pyramidMaximumPlacementSlope,
            float pyramidMinimumBurialDepth,
            float pyramidMaximumBurialDepth,
            float groundRingDensity,
            float aerialRingDensity,
            RingTuning ringTuning,
            GroundExploderTuning groundExploderTuning,
            DesertShrubTuning shrubTuning,
            LandmarkSystemTuning landmarkTuning,
            Action<TraversalRing> ringActivated)
        {
            Coordinate = coordinate;
            _chunkSize = chunkSize;
            _heightField = heightField;
            _worldSeed = worldSeed;
            _ringTuning = ringTuning;
            GameObject rootObject = new GameObject($"Desert Chunk [{coordinate.x}, {coordinate.y}]");
            Root = rootObject.transform;
            Root.SetParent(parent, false);
            Reposition(originOffsetX, originOffsetZ, chunkSize);

            _terrainMesh = BuildTerrainMesh(coordinate, chunkSize, resolution, heightField);
            MeshFilter filter = rootObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _terrainMesh;
            MeshRenderer renderer = rootObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.GetTerrainMaterials(coordinate, chunkSize);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            MeshCollider collider = rootObject.AddComponent<MeshCollider>();
            collider.sharedMesh = _terrainMesh;
            SpawnClouds(
                coordinate,
                chunkSize,
                materials.Cloud,
                materials.CloudUnderbelly,
                worldSeed,
                cloudTuning,
                cloudDensity);
            SpawnContent(
                coordinate,
                chunkSize,
                heightField,
                materials,
                player,
                playerHealth,
                worldSeed,
                coinRingSeed,
                cactusTuning,
                pyramidDensity,
                pyramidMinimumScale,
                pyramidMaximumScale,
                pyramidMaximumPlacementSlope,
                pyramidMinimumBurialDepth,
                pyramidMaximumBurialDepth,
                groundRingDensity,
                aerialRingDensity,
                ringTuning,
                groundExploderTuning,
                shrubTuning,
                landmarkTuning,
                ringActivated);
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

        public void LateTick(float deltaTime, Camera viewCamera)
        {
            _cloudField?.Tick(deltaTime);
            _shrubs?.Draw(viewCamera);
            for (int i = 0; i < _rings.Count; i++)
            {
                if (_rings[i] != null)
                {
                    _rings[i].LateTick(viewCamera);
                }
            }
        }

        public void SpawnUpperFlightLayers()
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                TraversalRing ring = _rings[i];
                if (ring != null && ring.RingType == TraversalRingType.Flight)
                {
                    int seedOffset = _ringTuning.UpperFlightRingSeedOffset + (i * 53);
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
            _shrubs?.RebuildWorldMatrices();
        }

        public void Dispose()
        {
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
        }

        private static Mesh BuildTerrainMesh(Vector2Int coordinate, float chunkSize, int resolution, DuneHeightField heightField)
        {
            resolution = Mathf.Max(8, resolution);
            int row = resolution + 1;
            int vertexCount = row * row;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = GetTriangleIndices(resolution);
            double logicalOriginX = coordinate.x * (double)chunkSize;
            double logicalOriginZ = coordinate.y * (double)chunkSize;
            float step = chunkSize / resolution;
            float minimumHeight = float.PositiveInfinity;
            float maximumHeight = float.NegativeInfinity;

            int vertex = 0;
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    double logicalX = logicalOriginX + (x * step);
                    double logicalZ = logicalOriginZ + (z * step);
                    float height = (float)heightField.SampleHeight(logicalX, logicalZ);
                    vertices[vertex] = new Vector3(x * step, height, z * step);
                    normals[vertex] = heightField.SampleNormal(logicalX, logicalZ);
                    uvs[vertex] = new Vector2((float)logicalX, (float)logicalZ);
                    minimumHeight = Mathf.Min(minimumHeight, height);
                    maximumHeight = Mathf.Max(maximumHeight, height);
                    vertex++;
                }
            }

            Mesh mesh = new Mesh { name = $"Dune Terrain [{coordinate.x}, {coordinate.y}]" };
            if (vertexCount > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(
                new Vector3(chunkSize * 0.5f, (minimumHeight + maximumHeight) * 0.5f, chunkSize * 0.5f),
                new Vector3(chunkSize, maximumHeight - minimumHeight, chunkSize));
            return mesh;
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
            CactusTuning cactusTuning,
            float pyramidDensity,
            float pyramidMinimumScale,
            float pyramidMaximumScale,
            float pyramidMaximumPlacementSlope,
            float pyramidMinimumBurialDepth,
            float pyramidMaximumBurialDepth,
            float groundRingDensity,
            float aerialRingDensity,
            RingTuning ringTuning,
            GroundExploderTuning groundExploderTuning,
            DesertShrubTuning shrubTuning,
            LandmarkSystemTuning landmarkTuning,
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
                float aerialChance = DuneVectorMath.Hash01(coordinate.x, coordinate.y, worldSeed, 701);
                if (aerialChance < aerialRingDensity)
                {
                    float angle = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 709, 0f, 360f);
                    Vector3 forward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector2 center = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 719, 24f, chunkSize - 24f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 727, 24f, chunkSize - 24f));
                    Vector2 forward2 = new Vector2(forward.x, forward.z);
                    Vector2 boostPosition = center - (forward2 * 11f);
                    Vector2 flightPosition = center + (forward2 * 11f);
                    CreateRing(boostPosition, forward, TraversalRingType.GroundBoost, ringTuning.GroundRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "route-boost", ringActivated);
                    CreateRing(flightPosition, forward, TraversalRingType.Flight, ringTuning.FlightRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "route-flight", ringActivated);
                }
                else if (DuneVectorMath.Hash01(coordinate.x, coordinate.y, worldSeed, 733) < groundRingDensity)
                {
                    float angle = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 739, 0f, 360f);
                    Vector3 forward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                    Vector2 position = new Vector2(
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 743, 16f, chunkSize - 16f),
                        DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 751, 16f, chunkSize - 16f));
                    CreateRing(position, forward, TraversalRingType.GroundBoost, ringTuning.GroundRingRadius, originX, originZ, heightField, materials, player, playerHealth, ringExclusions, worldSeed, ringTuning, "boost", ringActivated);
                }

                float flightMeterNormalized = player != null ? player.FlightTimeNormalized : 0f;
                float flightRingAmountMultiplier = ringTuning.GetFlightRingAmountMultiplier(flightMeterNormalized);
                float additionalFlightDensity = aerialRingDensity
                    * Mathf.Max(0f, flightRingAmountMultiplier - 1f);
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
                if (IsNearAny(collectiblePosition, ringExclusions, radius * 2f))
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
                if (IsNearAny(local, ringExclusions, 9f))
                {
                    continue;
                }

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
                float height = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 853 + (i * 13), minimumHeight, maximumHeight);
                float thickness = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 857 + (i * 13), minimumThickness, maximumThickness);
                float yaw = DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 863 + (i * 13), 0f, 360f);
                int minimumArms = Mathf.Clamp(cacti.MinimumArmCount, 0, 5);
                int maximumArms = Mathf.Clamp(Mathf.Max(minimumArms, cacti.MaximumArmCount), minimumArms, 5);
                int arms = minimumArms + Mathf.FloorToInt(
                    DuneVectorMath.Hash01(coordinate.x, coordinate.y, worldSeed, 877 + (i * 13))
                    * ((maximumArms - minimumArms) + 1));
                arms = Mathf.Min(maximumArms, arms);
                float y = (float)heightField.SampleHeight(logicalX, logicalZ) - Mathf.Max(0f, cacti.BurialDepth);
                int instanceSeed = unchecked((coordinate.x * 73856093) ^ (coordinate.y * 19349663) ^ (i * 83492791) ^ worldSeed);
                DuneVectorVisuals.CreateCactus(
                    Root,
                    new Vector3(local.x, y, local.y),
                    height,
                    thickness,
                    yaw,
                    arms,
                    instanceSeed,
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
                DuneVectorVisuals.CreatePyramid(Root, new Vector3(local.x, y, local.y), scale, yaw, materials.Sandstone);
                sceneryExclusions.Add(local);
            }

            SpawnGroundExploders(
                coordinate,
                chunkSize,
                heightField,
                materials,
                player,
                playerHealth,
                worldSeed,
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
                materials.Shrubs,
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
            int worldSeed,
            double originX,
            double originZ,
            List<Vector2> exclusions,
            List<Vector2> sceneryExclusions,
            GroundExploderTuning settings)
        {
            if (settings == null || !settings.Enabled || settings.DensityPerChunk <= 0f || coordinate == Vector2Int.zero)
            {
                return;
            }

            int count = CountFromDensity(settings.DensityPerChunk, coordinate, worldSeed, 1201);
            count = Mathf.CeilToInt(count * DuneVectorContractRisk.EnemySpawnMultiplier);
            for (int i = 0; i < count; i++)
            {
                Vector2 local = new Vector2(
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1211 + (i * 23), 10f, chunkSize - 10f),
                    DuneVectorMath.HashRange(coordinate.x, coordinate.y, worldSeed, 1217 + (i * 23), 10f, chunkSize - 10f));
                if (IsNearAny(local, exclusions, Mathf.Max(10f, settings.DetectionRadius * 0.7f)))
                {
                    continue;
                }

                double logicalX = originX + local.x;
                double logicalZ = originZ + local.y;
                Vector3 normal = heightField.SampleNormal(logicalX, logicalZ);
                if (Vector3.Angle(normal, Vector3.up) > settings.MaximumGroundSlope)
                {
                    continue;
                }

                float yaw = DuneVectorMath.HashRange(
                    coordinate.x,
                    coordinate.y,
                    worldSeed,
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
                    worldSeed,
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
            float minimumRingSeparation = Mathf.Max(0f, ringTuning.MinimumRingSeparation);
            if (IsNearAny(local, exclusions, minimumRingSeparation))
            {
                return;
            }

            double logicalX = originX + local.x;
            double logicalZ = originZ + local.y;
            float terrainHeight = (float)heightField.SampleHeight(logicalX, logicalZ);
            float minimumHeight = type switch
            {
                TraversalRingType.GroundBoost => ringTuning.GroundRingMinimumHeight,
                TraversalRingType.Flight => ringTuning.FlightRingMinimumHeight,
                TraversalRingType.Health => ringTuning.HealthRingMinimumHeight,
                _ => ringTuning.CoinRingMinimumHeight,
            };
            float maximumHeight = type switch
            {
                TraversalRingType.GroundBoost => ringTuning.GroundRingMaximumHeight,
                TraversalRingType.Flight => ringTuning.FlightRingMaximumHeight,
                TraversalRingType.Health => ringTuning.HealthRingMaximumHeight,
                _ => ringTuning.CoinRingMaximumHeight,
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
                reward.Initialize(player != null ? player.GetComponent<DroneGoldWallet>() : null, ringTuning.GoldReward);
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
            if (type == TraversalRingType.Flight)
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
            }
            _rings.Add(ring);
            exclusions.Add(local);
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
    }
}
