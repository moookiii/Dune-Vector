using System;
using System.Collections.Generic;
using System.IO;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DuneVector
{
    [Flags]
    public enum CourierContractModifier
    {
        None = 0,
        Fragile = 1 << 0,
        Express = 1 << 1,
        HighValue = 1 << 2,
        Oversized = 1 << 3,
        Hazardous = 1 << 4,
        Unknown = 1 << 5,
        MultiDrop = 1 << 6,
    }

    public enum CourierRunState
    {
        Hub,
        TeleportingToDesert,
        FindPackage,
        Delivering,
        ContractComplete,
        ContractFailed,
        TeleportingToHub,
    }

    [Serializable]
    public sealed class CourierContract
    {
        public string ContractId;
        public string DestinationName;
        public string CargoName;
        public int Seed;
        public int Difficulty;
        public float RouteDistance;
        public int BaseReward;
        public int OfferedReward;
        public float TimeLimit;
        public int StopCount = 1;
        public float EncounterIntensity = 1f;
        public CourierContractModifier DisplayModifiers;
        public CourierContractModifier GameplayModifiers;

        [NonSerialized] public LogicalPosition PickupPosition;
        [NonSerialized] public readonly List<LogicalPosition> DeliveryPositions = new List<LogicalPosition>();

        public bool Has(CourierContractModifier modifier)
        {
            return (GameplayModifiers & modifier) != 0;
        }

        public string DisplayModifierText => FormatModifiers(DisplayModifiers);

        public static string FormatModifiers(CourierContractModifier modifiers)
        {
            if (modifiers == CourierContractModifier.None)
            {
                return "STANDARD";
            }
            List<string> labels = new List<string>();
            AddLabel(labels, modifiers, CourierContractModifier.Fragile, "FRAGILE");
            AddLabel(labels, modifiers, CourierContractModifier.Express, "EXPRESS");
            AddLabel(labels, modifiers, CourierContractModifier.HighValue, "HIGH-VALUE");
            AddLabel(labels, modifiers, CourierContractModifier.Oversized, "OVERSIZED");
            AddLabel(labels, modifiers, CourierContractModifier.Hazardous, "HAZARDOUS");
            AddLabel(labels, modifiers, CourierContractModifier.Unknown, "UNKNOWN");
            AddLabel(labels, modifiers, CourierContractModifier.MultiDrop, "MULTI-DROP");
            return string.Join(" + ", labels);
        }

        private static void AddLabel(
            List<string> labels,
            CourierContractModifier available,
            CourierContractModifier flag,
            string label)
        {
            if ((available & flag) != 0)
            {
                labels.Add(label);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorCourierProgress : MonoBehaviour
    {
        private const string SaveFileName = "DuneVectorCourierProgress.dat";

        [Serializable]
        private sealed class SaveData
        {
            public int Version = 1;
            public int CompletedDeliveries;
            public int FailedDeliveries;
            public int TotalContractGold;
            public int HighestDifficulty;
        }

        public int CompletedDeliveries { get; private set; }
        public int FailedDeliveries { get; private set; }
        public int TotalContractGold { get; private set; }
        public int HighestDifficulty { get; private set; }
        public event Action Changed;

        private string _savePath;

        public void Initialize()
        {
            _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            Load();
        }

        public void RecordCompletion(int reward, int difficulty)
        {
            CompletedDeliveries++;
            TotalContractGold = TotalContractGold > int.MaxValue - Mathf.Max(0, reward)
                ? int.MaxValue
                : TotalContractGold + Mathf.Max(0, reward);
            HighestDifficulty = Mathf.Max(HighestDifficulty, difficulty);
            Save();
            Changed?.Invoke();
        }

        public void RecordFailure()
        {
            FailedDeliveries++;
            Save();
            Changed?.Invoke();
        }

        private void Load()
        {
            if (!File.Exists(_savePath))
            {
                Save();
                return;
            }
            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(_savePath));
                if (data == null)
                {
                    return;
                }
                CompletedDeliveries = Mathf.Max(0, data.CompletedDeliveries);
                FailedDeliveries = Mathf.Max(0, data.FailedDeliveries);
                TotalContractGold = Mathf.Max(0, data.TotalContractGold);
                HighestDifficulty = Mathf.Max(0, data.HighestDifficulty);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load courier progress from '{_savePath}': {exception.Message}", this);
            }
        }

        private void Save()
        {
            try
            {
                SaveData data = new SaveData
                {
                    CompletedDeliveries = CompletedDeliveries,
                    FailedDeliveries = FailedDeliveries,
                    TotalContractGold = TotalContractGold,
                    HighestDifficulty = HighestDifficulty,
                };
                File.WriteAllText(_savePath, JsonUtility.ToJson(data));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not save courier progress to '{_savePath}': {exception.Message}", this);
            }
        }
    }

    [DefaultExecutionOrder(1120)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorCourierGame : MonoBehaviour
    {
        public CourierRunState State { get; private set; }
        public CourierContract ActiveContract { get; private set; }
        public Transform ActiveObjective { get; private set; }
        public LogicalPosition ActiveObjectiveLogicalPosition { get; private set; }
        public bool IsContractActive => State == CourierRunState.FindPackage || State == CourierRunState.Delivering;
        public bool IsCarryingCargo => State == CourierRunState.Delivering;
        public bool IsTerminalOpen => _terminalOpen;
        public Vector3 HubSpawnPosition => _hubSpawn;
        public static bool IsGameplayHudSuppressed
        {
            get
            {
                DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
                return bootstrap != null && bootstrap.CourierGame != null && bootstrap.CourierGame.IsTerminalOpen;
            }
        }
        public float CargoIntegrity { get; private set; } = 100f;
        public float ExpressTimeRemaining { get; private set; }
        public DuneVectorCourierProgress Progress { get; private set; }
        public IReadOnlyList<CourierContract> AvailableContracts => _offers;

        public event Action<CourierContract> ContractStarted;
        public event Action<CourierContract> ContractEnded;

        private readonly List<CourierContract> _offers = new List<CourierContract>();
        private readonly List<DuneVectorLandmarkInstance> _routeLandmarks = new List<DuneVectorLandmarkInstance>();
        private readonly List<Transform> _teleportParticles = new List<Transform>();
        private readonly List<Transform> _teleportEnergyRings = new List<Transform>();
        private readonly List<Transform> _hubBeacons = new List<Transform>();

        private DronePlayer _playerInput;
        private DroneCharacterController _player;
        private DroneHealth _health;
        private DesertWorldStreamer _world;
        private Camera _camera;
        private DroneCameraController _cameraController;
        private DuneVectorMaterials _materials;
        private DroneGoldWallet _wallet;
        private DuneVectorLandmarkDirector _landmarks;
        private CourierContractTuning _settings;
        private DeliveryTuning _deliverySettings;
        private WorldHubTuning _hubSettings;
        private DuneVectorEnemyDirector _enemyDirector;
        private DuneVectorStormPyramidDirector _stormDirector;
        private DuneVectorRouteEncounterDirector _routeEncounterDirector;

        private Transform _hubRoot;
        private Transform _terminal;
        private Transform _teleportPlatform;
        private Transform _hubEnergyOrbit;
        private Transform _upgradeEnergyOrbit;
        private Vector3 _hubSpawn;
        private Vector3 _desertSpawn;
        private Quaternion _desertRotation;
        private Transform _package;
        private Transform _cargoWarning;
        private ParticleSystem _cargoSparks;
        private JobTraversalRing _objectiveRing;
        private int _deliveryIndex;
        private float _stateTimer;
        private float _hazardPulseTimer;
        private float _unknownRevealTimer;
        private float _offerRefreshTimer;
        private float _teleportTimer;
        private bool _teleportMoved;
        private bool _terminalOpen;
        private bool _unknownRevealed;
        private bool _wasGrounded;
        private float _minimumAirVerticalSpeed;
        private string _statusMessage;
        private float _statusMessageUntil;
        private Vector3 _droneVisualOriginalScale;
        private Material _hubMetalMaterial;
        private Material _hubEnergyMaterial;

        private GUIStyle _terminalTitleStyle;
        private GUIStyle _terminalBodyStyle;
        private GUIStyle _terminalButtonStyle;
        private GUIStyle _terminalPanelStyle;
        private GUIStyle _terminalSubtitleStyle;
        private GUIStyle _terminalKickerStyle;
        private GUIStyle _terminalDestinationStyle;
        private GUIStyle _terminalMetaStyle;
        private GUIStyle _terminalRewardStyle;
        private GUIStyle _terminalActionStyle;
        private GUIStyle _terminalTooltipTitleStyle;
        private GUIStyle _terminalTooltipBodyStyle;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudBodyStyle;
        private GUIStyle _objectiveStyle;
        private GUIStyle _statusStyle;
        private Texture2D _terminalPanelTexture;
        private Texture2D _terminalCardTexture;
        private Texture2D _terminalCardHoverTexture;

        public void Initialize(
            DronePlayer playerInput,
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            Camera camera,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DuneVectorLandmarkDirector landmarks,
            DeliveryTuning deliverySettings,
            CourierContractTuning settings,
            WorldHubTuning hubSettings,
            DuneVectorEnemyDirector enemyDirector,
            DuneVectorStormPyramidDirector stormDirector)
        {
            _playerInput = playerInput;
            _player = player;
            _health = health;
            _world = world;
            _camera = camera;
            _cameraController = camera != null ? camera.GetComponent<DroneCameraController>() : null;
            _materials = materials;
            _wallet = wallet;
            _landmarks = landmarks;
            _deliverySettings = deliverySettings;
            _settings = settings;
            _hubSettings = hubSettings;
            _enemyDirector = enemyDirector;
            _stormDirector = stormDirector;
            Progress = gameObject.AddComponent<DuneVectorCourierProgress>();
            Progress.Initialize();
            _health.Damaged += HandlePlayerDamaged;
            _health.Died += HandlePlayerDied;
            _world.WorldShifted += HandleWorldShift;
            if (_player.DroneVisualRoot != null)
            {
                _droneVisualOriginalScale = _player.DroneVisualRoot.localScale;
            }
            BuildHub();
            GenerateOffers();
            EnterHubImmediate(openTerminal: true);
        }

        public void BindEncounterDirector(DuneVectorRouteEncounterDirector director)
        {
            _routeEncounterDirector = director;
            if (_routeEncounterDirector != null)
            {
                _routeEncounterDirector.enabled = State != CourierRunState.Hub;
            }
        }

        public void RequestReturnToHub(bool recordAbandonment = true)
        {
            if (State == CourierRunState.Hub || State == CourierRunState.TeleportingToHub)
            {
                return;
            }
            if (IsContractActive)
            {
                FailContract("CONTRACT ABANDONED", recordFailure: recordAbandonment, beginReturn: false);
            }
            BeginTeleport(toHub: true);
        }

        public bool AcceptOffer(int offerIndex)
        {
            if (State != CourierRunState.Hub || offerIndex < 0 || offerIndex >= _offers.Count)
            {
                return false;
            }
            AcceptContract(_offers[offerIndex]);
            return true;
        }

        private void Update()
        {
            if (_world == null || _player == null)
            {
                return;
            }

            if (State == CourierRunState.TeleportingToDesert || State == CourierRunState.TeleportingToHub)
            {
                UpdateTeleport();
                return;
            }

            if (State == CourierRunState.Hub)
            {
                UpdateHub();
                return;
            }

            if (State == CourierRunState.FindPackage || State == CourierRunState.Delivering)
            {
                UpdateActiveContract();
            }
            else if (State == CourierRunState.ContractComplete || State == CourierRunState.ContractFailed)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                {
                    BeginTeleport(toHub: true);
                }
            }
        }

        private void UpdateHub()
        {
            AnimateHubPresentation();
            _offerRefreshTimer -= Time.unscaledDeltaTime;
            if (_offerRefreshTimer <= 0f)
            {
                GenerateOffers();
            }

            float terminalDistance = _terminal != null
                ? Vector3.Distance(_player.WorldCenter, _terminal.position)
                : float.PositiveInfinity;
            if (!_terminalOpen && terminalDistance <= _hubSettings.TerminalInteractionRadius &&
                Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                SetTerminalOpen(true);
            }
            else if (_terminalOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetTerminalOpen(false);
            }
        }

        private void UpdateActiveContract()
        {
            if (State == CourierRunState.FindPackage && _package != null)
            {
                _package.Rotate(0f, _settings.PackageSpinSpeed * Time.deltaTime, 0f, Space.World);
            }

            if (State != CourierRunState.Delivering || ActiveContract == null)
            {
                return;
            }
            if (CargoUsesIntegrity())
            {
                UpdateCargoImpactDamage();
            }
            if (State != CourierRunState.Delivering)
            {
                return;
            }
            if (ActiveContract.Has(CourierContractModifier.Express))
            {
                ExpressTimeRemaining = Mathf.Max(0f, ExpressTimeRemaining - Time.deltaTime);
                if (ExpressTimeRemaining <= 0f)
                {
                    FailContract("EXPRESS WINDOW MISSED", recordFailure: true, beginReturn: true);
                    return;
                }
            }
            if (!_unknownRevealed && ActiveContract.DisplayModifiers == CourierContractModifier.Unknown)
            {
                _unknownRevealTimer -= Time.deltaTime;
                if (_unknownRevealTimer <= 0f)
                {
                    _unknownRevealed = true;
                    ShowStatus($"CARGO IDENTIFIED: {CourierContract.FormatModifiers(ActiveContract.GameplayModifiers)}", 4f);
                }
            }
            if (ActiveContract.Has(CourierContractModifier.Hazardous) && CargoIntegrity <= _settings.HazardousUnstableIntegrity)
            {
                _hazardPulseTimer -= Time.deltaTime;
                if (_hazardPulseTimer <= 0f)
                {
                    _hazardPulseTimer = _settings.HazardousPulseInterval;
                    _health.TakeDamage(_settings.HazardousPulseDamage);
                    if (State != CourierRunState.Delivering)
                    {
                        return;
                    }
                    ShowStatus(CargoIntegrity <= _settings.HazardousCriticalIntegrity
                        ? "HAZARDOUS CARGO CRITICAL"
                        : "HAZARDOUS ENERGY DISCHARGE", 1.8f);
                }
            }
            UpdateCargoPresentation();
        }

        private void BuildHub()
        {
            _hubMetalMaterial = CreateHubMaterial(
                _materials.DroneDark,
                "World Hub Metal",
                _hubSettings.HubMetalColor,
                Color.black);
            _hubEnergyMaterial = CreateHubMaterial(
                _materials.DroneAccent,
                "World Hub Energy",
                new Color(_hubSettings.HubEnergyColor.r * 0.12f, _hubSettings.HubEnergyColor.g * 0.12f, _hubSettings.HubEnergyColor.b * 0.12f),
                _hubSettings.HubEnergyColor);
            LogicalPosition hubLogical = new LogicalPosition(
                DesertWorldStreamer.StartingLogicalPosition.x,
                DesertWorldStreamer.StartingLogicalPosition.y);
            float groundHeight = (float)_world.HeightField.SampleHeight(hubLogical.X, hubLogical.Z);
            float platformY = groundHeight + _hubSettings.PlatformHeightAboveTerrain;
            GameObject hubObject = new GameObject("World Hub - Courier Aerie");
            _hubRoot = hubObject.transform;
            _hubRoot.SetParent(transform, false);
            _hubRoot.position = _world.LogicalToLocal(hubLogical.X, platformY, hubLogical.Z);

            HubPart(PrimitiveType.Cylinder, "Main Teleport Platform", _hubRoot, Vector3.zero,
                new Vector3(_hubSettings.PlatformRadius, _hubSettings.PlatformThickness * 0.5f, _hubSettings.PlatformRadius),
                Quaternion.identity, _hubMetalMaterial, false);
            BuildCircleModelCollider(
                _hubRoot,
                "Main Teleport Platform Collider (circle.glb)",
                Vector3.up * (_hubSettings.PlatformThickness * 0.5f),
                _hubSettings.PlatformRadius * 0.5f);
            HubPart(PrimitiveType.Cylinder, "Energy Inlay", _hubRoot,
                new Vector3(0f, (_hubSettings.PlatformThickness * 0.5f) + 0.08f, 0f),
                new Vector3(_hubSettings.PlatformRadius * 0.72f, 0.08f, _hubSettings.PlatformRadius * 0.72f),
                Quaternion.identity, _hubEnergyMaterial, false);

            _hubEnergyOrbit = new GameObject("Rotating Platform Energy Lanes").transform;
            _hubEnergyOrbit.SetParent(_hubRoot, false);
            _hubEnergyOrbit.localPosition = Vector3.up * ((_hubSettings.PlatformThickness * 0.5f) + 0.18f);
            BuildSegmentedRing(
                _hubEnergyOrbit,
                _hubSettings.PlatformEnergySegmentCount,
                _hubSettings.PlatformEnergyRingRadius,
                _hubSettings.PlatformEnergySegmentLength,
                _hubSettings.PlatformEnergySegmentWidth,
                _hubSettings.PlatformEnergySegmentHeight,
                _hubEnergyMaterial,
                "Platform Energy Lane");

            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                HubPart(PrimitiveType.Cube, $"Hub Radial Brace {i + 1}", _hubRoot,
                    (direction * (_hubSettings.PlatformRadius * 0.8f)) + Vector3.up * 1.2f,
                    new Vector3(2f, 2.4f, _hubSettings.PlatformRadius * 0.34f),
                    Quaternion.Euler(0f, angle, 0f), _hubMetalMaterial, true);
            }

            int pylonCount = Mathf.Max(3, _hubSettings.HubPylonCount);
            for (int i = 0; i < pylonCount; i++)
            {
                float angle = (360f / pylonCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                HubPart(
                    PrimitiveType.Cube,
                    $"Courier Aerie Pylon {i + 1}",
                    _hubRoot,
                    (direction * _hubSettings.HubPylonRadius) + (Vector3.up * (_hubSettings.HubPylonHeight * 0.5f)),
                    new Vector3(_hubSettings.HubPylonWidth, _hubSettings.HubPylonHeight, _hubSettings.HubPylonWidth),
                    Quaternion.Euler(_hubSettings.HubPylonLean, angle, 0f),
                    _hubMetalMaterial,
                    true);
                Transform beacon = HubPart(
                    PrimitiveType.Sphere,
                    "Navigation Beacon",
                    _hubRoot,
                    (direction * _hubSettings.HubPylonRadius) + (Vector3.up * _hubSettings.HubPylonHeight),
                    Vector3.one * (_hubSettings.HubPylonWidth * 1.45f),
                    Quaternion.identity,
                    _hubEnergyMaterial,
                    false);
                _hubBeacons.Add(beacon);
            }

            GameObject terminalObject = new GameObject("Physical Contract Terminal");
            _terminal = terminalObject.transform;
            _terminal.SetParent(_hubRoot, false);
            _terminal.localPosition = Vector3.forward * _hubSettings.TerminalForwardOffset;
            HubPart(PrimitiveType.Cube, "Terminal Pedestal", _terminal, Vector3.up * 2f,
                new Vector3(3f, 4f, 2f), Quaternion.identity, _hubMetalMaterial, true);
            HubPart(PrimitiveType.Cube, "Terminal Screen", _terminal, new Vector3(0f, 4.1f, -0.45f),
                new Vector3(4.4f, 2.4f, 0.25f), Quaternion.Euler(-12f, 0f, 0f), _hubEnergyMaterial, false);
            HubPart(PrimitiveType.Cube, "Terminal Header", _terminal, new Vector3(0f, 5.7f, 0f),
                new Vector3(5.8f, 0.32f, 1.2f), Quaternion.identity, _hubMetalMaterial, false);
            for (int i = -1; i <= 1; i += 2)
            {
                HubPart(PrimitiveType.Cylinder, $"Terminal Signal Mast {(i < 0 ? "Left" : "Right")}", _terminal,
                    new Vector3(i * 2.25f, 7.4f, 0.2f), new Vector3(0.12f, 1.8f, 0.12f),
                    Quaternion.identity, _hubEnergyMaterial, false);
            }

            Transform upgradeArea = new GameObject("Drone Upgrade Area").transform;
            upgradeArea.SetParent(_hubRoot, false);
            upgradeArea.localPosition = Vector3.right * _hubSettings.UpgradeAreaSideOffset;
            Transform upgradePad = HubPart(PrimitiveType.Cylinder, "Upgrade Pad", upgradeArea, Vector3.up * 0.5f,
                new Vector3(5f, 0.5f, 5f), Quaternion.identity, _hubMetalMaterial, false);
            BuildCircleModelCollider(
                upgradeArea,
                "Upgrade Pad Collider (circle.glb)",
                Vector3.up,
                upgradePad.localScale.x * 0.5f);
            HubPart(PrimitiveType.Cube, "Upgrade Gantry", upgradeArea, new Vector3(0f, 5f, 2.5f),
                new Vector3(8f, 10f, 1f), Quaternion.identity, _hubMetalMaterial, true);
            _upgradeEnergyOrbit = new GameObject("Upgrade Calibration Arms").transform;
            _upgradeEnergyOrbit.SetParent(upgradeArea, false);
            _upgradeEnergyOrbit.localPosition = Vector3.up * 1.2f;
            int armCount = Mathf.Max(1, _hubSettings.UpgradePadArmCount);
            for (int i = 0; i < armCount; i++)
            {
                float angle = (360f / armCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                HubPart(PrimitiveType.Cube, $"Calibration Arm {i + 1}", _upgradeEnergyOrbit,
                    direction * (_hubSettings.UpgradePadArmLength * 0.5f),
                    new Vector3(0.22f, 0.12f, _hubSettings.UpgradePadArmLength),
                    Quaternion.Euler(0f, angle, 0f), _hubEnergyMaterial, false);
            }

            _teleportPlatform = _hubRoot;
            _hubSpawn = _hubRoot.position + Vector3.up * (_hubSettings.PlayerSpawnHeight + (_hubSettings.PlatformThickness * 0.5f));
        }

        private void AnimateHubPresentation()
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (_hubEnergyOrbit != null)
            {
                _hubEnergyOrbit.Rotate(0f, _hubSettings.PlatformEnergyRotationSpeed * deltaTime, 0f, Space.Self);
            }
            if (_upgradeEnergyOrbit != null)
            {
                _upgradeEnergyOrbit.Rotate(0f, _hubSettings.UpgradePadRotationSpeed * deltaTime, 0f, Space.Self);
            }
            float pulse = 1f + (Mathf.Sin(Time.unscaledTime * _hubSettings.HubBeaconPulseSpeed) * _hubSettings.HubBeaconPulseAmount);
            for (int i = 0; i < _hubBeacons.Count; i++)
            {
                if (_hubBeacons[i] != null)
                {
                    _hubBeacons[i].localScale = Vector3.one * (_hubSettings.HubPylonWidth * 1.45f * pulse);
                }
            }
        }

        private void EnterHubImmediate(bool openTerminal)
        {
            CleanupContractObjects();
            _landmarks?.ClearContractLandmarks();
            ActiveContract = null;
            CargoIntegrity = 100f;
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            if (_hubSettings.RestoreHealthOnReturn && !_health.IsDead)
            {
                _health.RestoreHealth(_health.MaximumHealth);
            }
            _player.Motor.SetPositionAndRotation(_hubSpawn, Quaternion.identity, true);
            _player.ResetTraversalAfterTeleport(Vector3.forward);
            _cameraController?.SnapToTarget();
            SetCombatSystemsActive(false);
            State = CourierRunState.Hub;
            _playerInput.SetInputEnabled(!openTerminal);
            SetTerminalOpen(openTerminal);
        }

        private void GenerateOffers()
        {
            _offers.Clear();
            int completionTier = Progress != null ? Progress.CompletedDeliveries : 0;
            int count = Mathf.Clamp(_settings.OfferedContractCount, 5, 8);
            int batch = Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(1f, _settings.ContractRefreshSeconds));
            System.Random random = new System.Random(unchecked(
                _world.WorldSeed ^ _settings.ContractSeedOffset ^ (completionTier * 486187739) ^ batch));
            for (int i = 0; i < count; i++)
            {
                _offers.Add(CreateOffer(random, i, completionTier));
            }
            _offerRefreshTimer = Mathf.Max(5f, _settings.ContractRefreshSeconds);
        }

        private CourierContract CreateOffer(System.Random random, int index, int completed)
        {
            int seed = random.Next();
            int difficulty = Mathf.Clamp(1 + (completed / 10) + random.Next(0, 3), 1, 20);
            float distance = Mathf.Lerp(_settings.MinimumRouteDistance, _settings.MaximumRouteDistance, (float)random.NextDouble());
            CourierContractModifier gameplay = CourierContractModifier.None;
            CourierContractModifier display = CourierContractModifier.None;
            if (index != 0)
            {
                int modifierCount = 1;
                if (completed >= _settings.TripleModifierUnlockDeliveries && random.NextDouble() <= _settings.TripleModifierChance)
                {
                    modifierCount = 3;
                }
                else if (completed >= _settings.DualModifierUnlockDeliveries && random.NextDouble() <= _settings.DualModifierChance)
                {
                    modifierCount = 2;
                }
                gameplay = ChooseModifiers(random, modifierCount);
                bool unknown = random.NextDouble() <= _settings.UnknownContractChance;
                display = unknown ? CourierContractModifier.Unknown : gameplay;
            }

            int stops = (gameplay & CourierContractModifier.MultiDrop) != 0
                ? random.Next(_settings.MultiDropMinimumStops, _settings.MultiDropMaximumStops + 1)
                : 1;
            float baseReward = Mathf.Lerp(_settings.MinimumBaseReward, _settings.MaximumBaseReward, distance / Mathf.Max(1f, _settings.MaximumRouteDistance));
            baseReward += distance * _settings.DistanceRewardPerMeter;
            int actualModifierCount = CountModifiers(gameplay);
            float multiplier = actualModifierCount >= 3
                ? _settings.TripleModifierRewardMultiplier
                : actualModifierCount == 2
                    ? _settings.DualModifierRewardMultiplier
                    : 1f + (actualModifierCount * 0.28f);
            if (display == CourierContractModifier.Unknown)
            {
                multiplier *= _settings.UnknownRewardMultiplier;
            }

            string[] destinationNames =
            {
                "WESTERN RELAY", "EXCAVATION DELTA", "CARRIER FALL", "RAIDER MERIDIAN",
                "ANCIENT VECTOR", "SANDWORKS NINE", "NORTHERN RELAY", "SPIRE APPROACH",
            };
            return new CourierContract
            {
                ContractId = $"DV-{completed:0000}-{index + 1:00}-{Math.Abs(seed % 10000):0000}",
                DestinationName = destinationNames[(seed & int.MaxValue) % destinationNames.Length],
                CargoName = display == CourierContractModifier.Unknown ? "CLASSIFIED" : CargoNameFor(gameplay),
                Seed = seed,
                Difficulty = difficulty,
                RouteDistance = distance,
                BaseReward = Mathf.RoundToInt(baseReward),
                OfferedReward = Mathf.RoundToInt(baseReward * multiplier),
                TimeLimit = (gameplay & CourierContractModifier.Express) != 0
                    ? (distance / _settings.ExpressExpectedSpeed) + _settings.ExpressGraceSeconds
                    : 0f,
                StopCount = stops,
                EncounterIntensity = (gameplay & CourierContractModifier.HighValue) != 0 ? 1.8f : 1f + (difficulty * 0.04f),
                DisplayModifiers = display,
                GameplayModifiers = gameplay,
            };
        }

        private CourierContractModifier ChooseModifiers(System.Random random, int count)
        {
            CourierContractModifier[] pool =
            {
                CourierContractModifier.Fragile,
                CourierContractModifier.Express,
                CourierContractModifier.HighValue,
                CourierContractModifier.Oversized,
                CourierContractModifier.Hazardous,
                CourierContractModifier.MultiDrop,
            };
            CourierContractModifier result = CourierContractModifier.None;
            while (CountModifiers(result) < count)
            {
                result |= pool[random.Next(0, pool.Length)];
            }
            return result;
        }

        private void AcceptContract(CourierContract contract)
        {
            if (contract == null || State != CourierRunState.Hub)
            {
                return;
            }
            ActiveContract = contract;
            PrepareRoute(contract);
            SetTerminalOpen(false);
            BeginTeleport(toHub: false);
        }

        private void PrepareRoute(CourierContract contract)
        {
            CleanupContractObjects();
            _landmarks.ClearContractLandmarks();
            _routeLandmarks.Clear();
            System.Random random = new System.Random(contract.Seed);
            LogicalPosition hub = new LogicalPosition(
                DesertWorldStreamer.StartingLogicalPosition.x,
                DesertWorldStreamer.StartingLogicalPosition.y);
            double insertionAngle = random.NextDouble() * Math.PI * 2.0;
            double insertionDistance = Mathf.Lerp(
                _settings.MinimumRouteOriginDistance,
                Mathf.Max(_settings.MinimumRouteOriginDistance, _settings.MaximumRouteOriginDistance),
                (float)random.NextDouble());
            LogicalPosition routeOrigin = new LogicalPosition(
                hub.X + (Math.Cos(insertionAngle) * insertionDistance),
                hub.Z + (Math.Sin(insertionAngle) * insertionDistance));
            double routeAngle = random.NextDouble() * Math.PI * 2.0;
            float pickupOffset = Mathf.Lerp(
                _settings.MinimumPickupInsertionDistance,
                _settings.MaximumPickupInsertionDistance,
                (float)random.NextDouble());
            contract.PickupPosition = new LogicalPosition(
                routeOrigin.X + (Math.Cos(routeAngle) * pickupOffset),
                routeOrigin.Z + (Math.Sin(routeAngle) * pickupOffset));
            contract.DeliveryPositions.Clear();
            LogicalPosition previous = contract.PickupPosition;
            float perLeg = contract.RouteDistance / Mathf.Max(1, contract.StopCount);
            for (int i = 0; i < contract.StopCount; i++)
            {
                float directionJitter = Mathf.Lerp(-0.3f, 0.3f, (float)random.NextDouble());
                double angle = routeAngle + directionJitter;
                LogicalPosition destination = new LogicalPosition(
                    previous.X + (Math.Cos(angle) * perLeg),
                    previous.Z + (Math.Sin(angle) * perLeg));
                contract.DeliveryPositions.Add(destination);
                previous = destination;
            }

            DuneLandmarkType pickupType = ChooseLandmarkType(random);
            _routeLandmarks.Add(_landmarks.CreateContractLandmark(pickupType, contract.PickupPosition, contract.Seed));
            for (int i = 0; i < contract.DeliveryPositions.Count; i++)
            {
                _routeLandmarks.Add(_landmarks.CreateContractLandmark(
                    ChooseLandmarkType(random), contract.DeliveryPositions[i], contract.Seed + i + 1));
            }

            Vector3 routeForward = new Vector3((float)Math.Cos(routeAngle), 0f, (float)Math.Sin(routeAngle));
            float insertionHeight = (float)_world.HeightField.SampleHeight(routeOrigin.X, routeOrigin.Z) + _hubSettings.DesertInsertionHeight;
            _desertSpawn = _world.LogicalToLocal(routeOrigin.X, insertionHeight, routeOrigin.Z);
            _desertRotation = Quaternion.LookRotation(routeForward, Vector3.up);
            BuildPickupObjective();
        }

        private void BuildPickupObjective()
        {
            DuneVectorLandmarkInstance landmark = _routeLandmarks[0];
            Vector3 objectivePosition = landmark.ContractSocket.position;
            LogicalPosition objectiveLogical = LocalToLogical(objectivePosition);
            _package = DuneVectorVisuals.CreatePackageVisual(transform, _materials, _settings.ObjectivePackageScale);
            _package.name = $"Contract Cargo {ActiveContract.ContractId}";
            _package.position = objectivePosition;
            _objectiveRing = CreateObjectiveRing(
                "Contract Pickup Ring",
                objectiveLogical,
                objectivePosition.y,
                true,
                HandlePackagePickup);
            ActiveObjective = _package;
            ActiveObjectiveLogicalPosition = objectiveLogical;
        }

        private JobTraversalRing CreateObjectiveRing(
            string objectName,
            LogicalPosition logical,
            double height,
            bool pickup,
            Action crossed)
        {
            GameObject ringObject = new GameObject(objectName);
            ringObject.transform.SetParent(transform, false);
            JobTraversalRing ring = ringObject.AddComponent<JobTraversalRing>();
            ring.Initialize(_player, _camera, _materials, pickup, Mathf.Max(1f, _deliverySettings.ObjectiveRingRadius), crossed);
            ring.LogicalPosition = logical;
            ring.LogicalHeight = height;
            ring.transform.position = _world.LogicalToLocal(logical.X, height, logical.Z);
            return ring;
        }

        private void HandlePackagePickup()
        {
            if (State != CourierRunState.FindPackage || _package == null)
            {
                return;
            }
            if (_objectiveRing != null)
            {
                Destroy(_objectiveRing.gameObject);
                _objectiveRing = null;
            }
            State = CourierRunState.Delivering;
            CargoIntegrity = 100f;
            ExpressTimeRemaining = ActiveContract.TimeLimit;
            _unknownRevealTimer = _settings.UnknownRevealDelay;
            _unknownRevealed = ActiveContract.DisplayModifiers != CourierContractModifier.Unknown;
            _hazardPulseTimer = _settings.HazardousPulseInterval;
            _wasGrounded = _player.Motor.GroundingStatus.IsStableOnGround;
            _minimumAirVerticalSpeed = 0f;
            Transform carryParent = _player.DroneVisualRoot != null ? _player.DroneVisualRoot : _player.transform;
            _package.SetParent(carryParent, false);
            bool oversized = ActiveContract.Has(CourierContractModifier.Oversized);
            _package.localPosition = oversized ? _settings.OversizedPackageOffset : _settings.CarriedPackageOffset;
            _package.localRotation = Quaternion.Euler(0f, 18f, 0f);
            _package.localScale *= oversized ? _settings.OversizedVisualScale : 1f;
            if (oversized)
            {
                _player.SetCargoHandlingModifiers(
                    _settings.OversizedSpeedMultiplier,
                    _settings.OversizedAccelerationMultiplier,
                    _settings.OversizedTurningMultiplier);
            }
            if (ActiveContract.Has(CourierContractModifier.Hazardous) ||
                ActiveContract.Has(CourierContractModifier.Fragile))
            {
                CreateCargoWarningPresentation(ActiveContract.Has(CourierContractModifier.Hazardous)
                    ? _materials.EnemyCore
                    : _materials.GroundEnemyWarning);
            }
            _deliveryIndex = 0;
            BuildDeliveryObjective();
            ContractStarted?.Invoke(ActiveContract);
            _routeEncounterDirector?.BeginContract(ActiveContract);
            ShowStatus("CARGO SECURED — PROCEED TO DESTINATION", 3f);
        }

        private void BuildDeliveryObjective()
        {
            if (_objectiveRing != null)
            {
                Destroy(_objectiveRing.gameObject);
            }
            DuneVectorLandmarkInstance landmark = _routeLandmarks[_deliveryIndex + 1];
            Vector3 objectivePosition = landmark.ContractSocket.position;
            LogicalPosition objectiveLogical = LocalToLogical(objectivePosition);
            _objectiveRing = CreateObjectiveRing(
                $"Delivery Ring {_deliveryIndex + 1}", objectiveLogical, objectivePosition.y, false, HandleDelivery);
            ActiveObjective = _objectiveRing.transform;
            ActiveObjectiveLogicalPosition = objectiveLogical;
        }

        private LogicalPosition LocalToLogical(Vector3 localPosition)
        {
            return new LogicalPosition(
                _world.OriginOffsetX + localPosition.x,
                _world.OriginOffsetZ + localPosition.z);
        }

        private void HandleDelivery()
        {
            if (State != CourierRunState.Delivering)
            {
                return;
            }
            _deliveryIndex++;
            if (_deliveryIndex < ActiveContract.DeliveryPositions.Count)
            {
                ShowStatus($"DROP {_deliveryIndex} COMPLETE — NEXT DESTINATION", 2.5f);
                BuildDeliveryObjective();
                return;
            }
            CompleteContract();
        }

        private void CompleteContract()
        {
            CourierContract completed = ActiveContract;
            float integrityFactor = CargoUsesIntegrity()
                ? Mathf.Lerp(
                    _settings.IntegrityRewardFloor,
                    1f,
                    Mathf.Clamp01(CargoIntegrity / 100f))
                : 1f;
            int reward = Mathf.RoundToInt(completed.OfferedReward * integrityFactor);
            _wallet?.AddGold(reward);
            Progress.RecordCompletion(reward, completed.Difficulty);
            _routeEncounterDirector?.EndContract();
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            CleanupContractObjects();
            State = CourierRunState.ContractComplete;
            _stateTimer = _settings.CompletionReturnDelay;
            ShowStatus($"CONTRACT COMPLETE  +{reward} GOLD", _stateTimer);
            ContractEnded?.Invoke(completed);
            GenerateOffers();
        }

        private void FailContract(string reason, bool recordFailure, bool beginReturn)
        {
            CourierContract failed = ActiveContract;
            if (recordFailure && failed != null)
            {
                Progress.RecordFailure();
            }
            _routeEncounterDirector?.EndContract();
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            CleanupContractObjects();
            State = CourierRunState.ContractFailed;
            _stateTimer = _settings.FailureReturnDelay;
            ShowStatus(reason, _stateTimer);
            if (failed != null)
            {
                ContractEnded?.Invoke(failed);
            }
            if (beginReturn && _stateTimer <= 0f)
            {
                BeginTeleport(toHub: true);
            }
        }

        private void HandlePlayerDamaged(float amount)
        {
            if (State != CourierRunState.Delivering || ActiveContract == null)
            {
                return;
            }
            if (!CargoUsesIntegrity())
            {
                return;
            }
            float multiplier = ActiveContract.Has(CourierContractModifier.Fragile)
                ? _settings.FragileCargoDamageMultiplier
                : ActiveContract.Has(CourierContractModifier.Hazardous)
                    ? _settings.HazardousCargoDamageMultiplier
                    : _settings.StandardCargoDamageMultiplier;
            DamageCargo(amount * multiplier);
        }

        private void HandlePlayerDied()
        {
            if (!IsContractActive || ActiveContract == null)
            {
                return;
            }
            CourierContract failed = ActiveContract;
            Progress.RecordFailure();
            _routeEncounterDirector?.EndContract();
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            CleanupContractObjects();
            State = CourierRunState.ContractFailed;
            _stateTimer = float.PositiveInfinity;
            ContractEnded?.Invoke(failed);
        }

        private void UpdateCargoImpactDamage()
        {
            bool grounded = _player.Motor.GroundingStatus.IsStableOnGround;
            if (!grounded)
            {
                _minimumAirVerticalSpeed = Mathf.Min(_minimumAirVerticalSpeed, _player.Motor.BaseVelocity.y);
            }
            else if (!_wasGrounded)
            {
                float impactSpeed = Mathf.Max(0f, -_minimumAirVerticalSpeed);
                if (impactSpeed > _settings.CargoHardImpactSpeed)
                {
                    DamageCargo((impactSpeed - _settings.CargoHardImpactSpeed) * _settings.CargoHardImpactDamagePerSpeed);
                }
                _minimumAirVerticalSpeed = 0f;
            }
            _wasGrounded = grounded;
        }

        private void DamageCargo(float amount)
        {
            if (amount <= 0f || State != CourierRunState.Delivering || ActiveContract == null)
            {
                return;
            }
            CargoIntegrity = Mathf.Max(0f, CargoIntegrity - amount);
            float failureThreshold = ActiveContract.Has(CourierContractModifier.Fragile)
                ? _settings.FragileFailureIntegrity
                : 0f;
            if (CargoIntegrity <= failureThreshold)
            {
                FailContract("CARGO INTEGRITY LOST", recordFailure: true, beginReturn: true);
            }
            else
            {
                ShowStatus($"CARGO DAMAGED  {CargoIntegrity:0}%", 1.5f);
            }
        }

        private void UpdateCargoPresentation()
        {
            if (_package == null)
            {
                return;
            }
            float critical = 1f - Mathf.Clamp01(CargoIntegrity / 100f);
            float pulse = 1f + (Mathf.Sin(Time.time * (4f + critical * 8f)) * critical * _settings.CargoDamagePulseAmount);
            _package.localScale = Vector3.one * _settings.ObjectivePackageScale *
                (ActiveContract.Has(CourierContractModifier.Oversized) ? _settings.OversizedVisualScale : 1f) * pulse;
            if (_cargoWarning != null)
            {
                float warningAmount = 1f - Mathf.Clamp01(CargoIntegrity / Mathf.Max(1f, _settings.HazardousWarningIntegrity));
                float warningPulse = 0.75f + (Mathf.Sin(Time.time * _settings.CargoWarningPulseSpeed) * 0.25f);
                _cargoWarning.localScale = Vector3.one * (_settings.CargoWarningScale * warningAmount * warningPulse);
                _cargoWarning.Rotate(0f, _settings.CargoWarningOrbitSpeed * Time.deltaTime, 0f, Space.Self);
            }
            if (_cargoSparks != null)
            {
                ParticleSystem.EmissionModule emission = _cargoSparks.emission;
                float severity = 1f - Mathf.Clamp01(CargoIntegrity / Mathf.Max(1f, _settings.CargoCriticalEffectsThreshold));
                emission.rateOverTime = _settings.CargoCriticalSparkRate * severity;
            }
        }

        private void CreateCargoWarningPresentation(Material warningMaterial)
        {
            GameObject warningRoot = new GameObject("Cargo Integrity Warning Array");
            _cargoWarning = warningRoot.transform;
            _cargoWarning.SetParent(_package, false);
            _cargoWarning.localPosition = Vector3.up * _settings.CargoWarningHeight;
            _cargoWarning.localScale = Vector3.zero;
            int lightCount = Mathf.Max(1, _settings.CargoWarningLightCount);
            for (int i = 0; i < lightCount; i++)
            {
                float angle = (360f / lightCount) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                GameObject lightObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lightObject.name = $"Warning Light {i + 1}";
                lightObject.transform.SetParent(_cargoWarning, false);
                lightObject.transform.localPosition = direction * _settings.CargoWarningLightRadius;
                lightObject.transform.localScale = Vector3.one;
                Renderer renderer = lightObject.GetComponent<Renderer>();
                renderer.sharedMaterial = warningMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Collider lightCollider = lightObject.GetComponent<Collider>();
                if (lightCollider != null) Destroy(lightCollider);
            }

            GameObject sparkObject = new GameObject("Cargo Critical Sparks");
            sparkObject.transform.SetParent(_package, false);
            sparkObject.transform.localPosition = Vector3.up * _settings.CargoWarningHeight;
            _cargoSparks = sparkObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _cargoSparks.main;
            main.loop = true;
            main.startLifetime = _settings.CargoCriticalSparkLifetime;
            main.startSpeed = _settings.CargoCriticalSparkSpeed;
            main.startSize = _settings.CargoCriticalSparkSize;
            main.maxParticles = Mathf.Max(8, Mathf.CeilToInt(_settings.CargoCriticalSparkRate * Mathf.Max(1f, _settings.CargoCriticalSparkLifetime) * 2f));
            ParticleSystem.EmissionModule emission = _cargoSparks.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = _cargoSparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _settings.CargoWarningLightRadius;
            ParticleSystemRenderer particleRenderer = _cargoSparks.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = warningMaterial;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private bool CargoUsesIntegrity()
        {
            return ActiveContract != null &&
                (ActiveContract.Has(CourierContractModifier.Fragile) ||
                 ActiveContract.Has(CourierContractModifier.Hazardous));
        }

        private void BeginTeleport(bool toHub)
        {
            SetTerminalOpen(false);
            State = toHub ? CourierRunState.TeleportingToHub : CourierRunState.TeleportingToDesert;
            _teleportTimer = 0f;
            _teleportMoved = false;
            _playerInput.SetInputEnabled(false);
            CreateTeleportParticles();
        }

        private void UpdateTeleport()
        {
            bool toHub = State == CourierRunState.TeleportingToHub;
            float build = _hubSettings.TeleportBuildDuration;
            float fade = _hubSettings.TeleportFadeDuration;
            float rebuild = _hubSettings.TeleportRebuildDuration;
            float total = build + fade + rebuild;
            _teleportTimer += Time.deltaTime;
            if (!_teleportMoved)
            {
                _player.Motor.BaseVelocity = Vector3.Lerp(
                    _player.Motor.BaseVelocity,
                    Vector3.zero,
                    DuneVectorMath.Sharpness(_hubSettings.StabilizeSharpness, Time.deltaTime));
            }
            float vanishAt = build + (fade * 0.5f);
            if (!_teleportMoved && _teleportTimer >= vanishAt)
            {
                _teleportMoved = true;
                Vector3 position = toHub ? _hubSpawn : _desertSpawn;
                Quaternion rotation = toHub ? Quaternion.identity : _desertRotation;
                _player.Motor.SetPositionAndRotation(position, rotation, true);
                _player.ResetTraversalAfterTeleport(rotation * Vector3.forward);
                _cameraController?.SnapToTarget();
                RecenterTeleportParticles(position);
            }

            float visualScale;
            if (_teleportTimer < vanishAt)
            {
                visualScale = 1f - Mathf.Clamp01((_teleportTimer - build) / Mathf.Max(0.01f, fade * 0.5f));
            }
            else
            {
                visualScale = Mathf.Clamp01((_teleportTimer - vanishAt) / Mathf.Max(0.01f, rebuild));
            }
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _droneVisualOriginalScale * visualScale;
            }
            AnimateTeleportParticles(total);

            if (_teleportTimer < total)
            {
                return;
            }
            DestroyTeleportParticles();
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _droneVisualOriginalScale;
            }
            if (toHub)
            {
                EnterHubImmediate(openTerminal: false);
                ShowStatus("RETURNED TO COURIER AERIE", 2.5f);
            }
            else
            {
                State = CourierRunState.FindPackage;
                SetCombatSystemsActive(true);
                _playerInput.SetInputEnabled(true);
                ShowStatus("CONTRACT DEPLOYED — LOCATE CARGO", 3f);
            }
        }

        private void SetCombatSystemsActive(bool active)
        {
            _enemyDirector?.SetGameplayActive(active);
            _stormDirector?.SetGameplayActive(active);
            if (_routeEncounterDirector != null) _routeEncounterDirector.enabled = active;
        }

        private void SetTerminalOpen(bool open)
        {
            _terminalOpen = open;
            if (State != CourierRunState.Hub)
            {
                return;
            }
            _playerInput.SetInputEnabled(!open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        private void CleanupContractObjects()
        {
            if (_objectiveRing != null) Destroy(_objectiveRing.gameObject);
            if (_package != null) Destroy(_package.gameObject);
            _objectiveRing = null;
            _package = null;
            _cargoWarning = null;
            _cargoSparks = null;
            ActiveObjective = null;
        }

        private void CreateTeleportParticles()
        {
            DestroyTeleportParticles();
            int count = Mathf.Max(4, _hubSettings.TeleportParticleCount);
            for (int i = 0; i < count; i++)
            {
                GameObject particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                particle.name = $"Teleport Sand Energy {i + 1:00}";
                particle.transform.SetParent(transform, true);
                particle.transform.localScale = Vector3.one * Mathf.Lerp(
                    _hubSettings.TeleportParticleMinimumSize,
                    _hubSettings.TeleportParticleMaximumSize,
                    (i % 7) / 6f);
                Renderer renderer = particle.GetComponent<Renderer>();
                renderer.sharedMaterial = _hubEnergyMaterial != null ? _hubEnergyMaterial : _materials.DroneAccent;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Collider collider = particle.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                _teleportParticles.Add(particle.transform);
            }

            int ringCount = Mathf.Max(1, _hubSettings.TeleportEnergyRingCount);
            for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
            {
                Transform ring = new GameObject($"Teleport Energy Ring {ringIndex + 1}").transform;
                ring.SetParent(transform, true);
                BuildSegmentedRing(
                    ring,
                    _hubSettings.TeleportEnergyRingSegments,
                    _hubSettings.TeleportEffectRadius,
                    _hubSettings.TeleportEnergyRingSegmentLength,
                    _hubSettings.TeleportEnergyRingThickness,
                    _hubSettings.TeleportEnergyRingThickness,
                    _hubEnergyMaterial != null ? _hubEnergyMaterial : _materials.DroneAccent,
                    "Teleport Arc");
                _teleportEnergyRings.Add(ring);
            }
        }

        private void AnimateTeleportParticles(float total)
        {
            Vector3 center = _player.WorldCenter;
            float progress = Mathf.Clamp01(_teleportTimer / Mathf.Max(0.01f, total));
            for (int i = 0; i < _teleportParticles.Count; i++)
            {
                Transform particle = _teleportParticles[i];
                if (particle == null) continue;
                float phase = (i / (float)_teleportParticles.Count) * Mathf.PI * 2f;
                float angle = phase + (_teleportTimer * _hubSettings.TeleportParticleSpinSpeed * Mathf.Deg2Rad);
                float radius = Mathf.Lerp(_hubSettings.TeleportEffectRadius, _hubSettings.TeleportConvergenceRadius, progress);
                float y = Mathf.Repeat(
                    (i * 0.37f) + (_teleportTimer * _hubSettings.TeleportParticleLiftSpeed),
                    Mathf.Max(0.01f, _hubSettings.TeleportHelixHeight)) - (_hubSettings.TeleportHelixHeight * 0.34f);
                particle.position = center + new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }
            float ringScale = Mathf.Lerp(1f, 0.08f, progress);
            float ringCenter = (_teleportEnergyRings.Count - 1) * 0.5f;
            for (int i = 0; i < _teleportEnergyRings.Count; i++)
            {
                Transform ring = _teleportEnergyRings[i];
                if (ring == null) continue;
                ring.position = center + Vector3.up * ((i - ringCenter) * _hubSettings.TeleportEnergyRingSpacing);
                ring.localScale = Vector3.one * ringScale;
                float direction = i % 2 == 0 ? 1f : -1f;
                ring.Rotate(0f, direction * _hubSettings.TeleportEnergyRingRotationSpeed * Time.deltaTime, 0f, Space.Self);
            }
        }

        private void RecenterTeleportParticles(Vector3 center)
        {
            for (int i = 0; i < _teleportParticles.Count; i++)
            {
                if (_teleportParticles[i] != null)
                {
                    _teleportParticles[i].position = center;
                }
            }
            for (int i = 0; i < _teleportEnergyRings.Count; i++)
            {
                if (_teleportEnergyRings[i] != null)
                {
                    _teleportEnergyRings[i].position = center;
                }
            }
        }

        private void DestroyTeleportParticles()
        {
            for (int i = 0; i < _teleportParticles.Count; i++)
            {
                if (_teleportParticles[i] != null) Destroy(_teleportParticles[i].gameObject);
            }
            _teleportParticles.Clear();
            for (int i = 0; i < _teleportEnergyRings.Count; i++)
            {
                if (_teleportEnergyRings[i] != null) Destroy(_teleportEnergyRings[i].gameObject);
            }
            _teleportEnergyRings.Clear();
        }

        private void HandleWorldShift(Vector3 shift)
        {
            if (_hubRoot != null)
            {
                _hubRoot.position += shift;
                _hubSpawn += shift;
            }
            _desertSpawn += shift;
            _objectiveRing?.ApplyWorldShift(shift);
            if (State == CourierRunState.FindPackage && _package != null)
            {
                _package.position += shift;
            }
        }

        private void ShowStatus(string message, float duration)
        {
            _statusMessage = message;
            _statusMessageUntil = Time.unscaledTime + Mathf.Max(0.1f, duration);
        }

        private void EnsureStyles()
        {
            if (_terminalTitleStyle != null)
            {
                return;
            }
            _terminalTitleStyle = LabelStyle(_hubSettings.TerminalTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _hubSettings.TerminalAccentColor);
            _terminalBodyStyle = LabelStyle(_hubSettings.TerminalBodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft, _hubSettings.TerminalTextColor);
            _terminalSubtitleStyle = LabelStyle(_hubSettings.TerminalMetaFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _hubSettings.TerminalMutedTextColor);
            _terminalKickerStyle = LabelStyle(_hubSettings.TerminalKickerFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _hubSettings.TerminalAccentColor);
            _terminalDestinationStyle = LabelStyle(_hubSettings.TerminalDestinationFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _hubSettings.TerminalTextColor);
            _terminalMetaStyle = LabelStyle(_hubSettings.TerminalMetaFontSize, FontStyle.Normal, TextAnchor.MiddleLeft, _hubSettings.TerminalMutedTextColor);
            _terminalRewardStyle = LabelStyle(_hubSettings.TerminalRewardFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _hubSettings.TerminalHighValueColor);
            _terminalActionStyle = LabelStyle(_hubSettings.TerminalButtonFontSize, FontStyle.Bold, TextAnchor.MiddleRight, _hubSettings.TerminalAccentColor);
            _terminalTooltipTitleStyle = LabelStyle(_hubSettings.TerminalTooltipTitleFontSize, FontStyle.Bold, TextAnchor.UpperLeft, _hubSettings.TerminalAccentColor);
            _terminalTooltipBodyStyle = LabelStyle(_hubSettings.TerminalTooltipBodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft, _hubSettings.TerminalTextColor);
            _terminalPanelTexture = SolidTexture(_hubSettings.TerminalPanelColor, "Courier Terminal Panel");
            _terminalCardTexture = SolidTexture(_hubSettings.TerminalCardColor, "Courier Contract Card");
            _terminalCardHoverTexture = SolidTexture(_hubSettings.TerminalCardHoverColor, "Courier Contract Card Hover");
            _terminalPanelStyle = new GUIStyle(GUI.skin.box);
            _terminalPanelStyle.normal.background = _terminalPanelTexture;
            _terminalPanelStyle.border = new RectOffset(0, 0, 0, 0);
            _terminalButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = _hubSettings.TerminalButtonFontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _terminalButtonStyle.border = new RectOffset(0, 0, 0, 0);
            _terminalButtonStyle.padding = new RectOffset(0, 0, 0, 0);
            _terminalButtonStyle.margin = new RectOffset(0, 0, 0, 0);
            _terminalButtonStyle.normal.background = _terminalCardTexture;
            _terminalButtonStyle.hover.background = _terminalCardHoverTexture;
            _terminalButtonStyle.active.background = _terminalCardHoverTexture;
            _terminalButtonStyle.normal.textColor = _hubSettings.TerminalTextColor;
            _terminalButtonStyle.hover.textColor = _hubSettings.TerminalTextColor;
            _hudTitleStyle = LabelStyle(_settings.HudTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, _settings.HudAccentColor);
            _hudBodyStyle = LabelStyle(_settings.HudBodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft, _settings.HudTextColor);
            _objectiveStyle = LabelStyle(_settings.HudStatusFontSize, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudTextColor);
            _statusStyle = LabelStyle(_settings.HudStatusFontSize + 2, FontStyle.Bold, TextAnchor.MiddleCenter, _settings.HudAccentColor);
        }

        private static GUIStyle LabelStyle(int size, FontStyle fontStyle, TextAnchor anchor, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = true,
                normal = { textColor = color },
            };
        }

        private static Texture2D SolidTexture(Color color, string textureName)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (_terminalOpen && State == CourierRunState.Hub)
            {
                DrawContractTerminal();
            }
            else if (State == CourierRunState.Hub)
            {
                DrawHubHUD();
            }
            else if (IsContractActive)
            {
                DrawContractHUD();
                DrawObjectiveMarker();
            }
            if (State == CourierRunState.TeleportingToDesert || State == CourierRunState.TeleportingToHub)
            {
                DrawTeleportFade();
            }
            if (!_terminalOpen && Time.unscaledTime < _statusMessageUntil)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.18f, Screen.width, 42f), _statusMessage, _statusStyle);
            }
        }

        private void DrawContractTerminal()
        {
            GUI.depth = -1100;
            Color previousBackground = GUI.backgroundColor;
            Matrix4x4 previousMatrix = GUI.matrix;
            DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), _hubSettings.TerminalBackdropColor);

            float minimumScale = Mathf.Min(_hubSettings.TerminalMinimumScale, _hubSettings.TerminalMaximumScale);
            float maximumScale = Mathf.Max(_hubSettings.TerminalMinimumScale, _hubSettings.TerminalMaximumScale);
            float scale = Mathf.Clamp(
                Mathf.Min(
                    Screen.width / Mathf.Max(1f, _hubSettings.TerminalReferenceWidth),
                    Screen.height / Mathf.Max(1f, _hubSettings.TerminalReferenceHeight)),
                minimumScale,
                maximumScale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float virtualWidth = Screen.width / scale;
            float virtualHeight = Screen.height / scale;
            float width = Mathf.Min(_hubSettings.TerminalPanelWidth, virtualWidth - (_hubSettings.TerminalScreenMargin * 2f));
            float height = Mathf.Min(_hubSettings.TerminalPanelHeight, virtualHeight - (_hubSettings.TerminalScreenMargin * 2f));
            Rect panel = new Rect((virtualWidth - width) * 0.5f, (virtualHeight - height) * 0.5f, width, height);
            Rect shadow = new Rect(
                panel.x + _hubSettings.TerminalPanelShadowOffset.x,
                panel.y + _hubSettings.TerminalPanelShadowOffset.y,
                panel.width,
                panel.height);
            DrawSolidRect(shadow, _hubSettings.TerminalShadowColor);
            GUI.Box(panel, GUIContent.none, _terminalPanelStyle);
            DrawBorder(panel, _hubSettings.TerminalBorderColor, _hubSettings.TerminalPanelBorderThickness);
            DrawSolidRect(
                new Rect(panel.x, panel.y, panel.width, _hubSettings.TerminalAccentBarHeight),
                _hubSettings.TerminalAccentColor);

            float padding = _hubSettings.TerminalPadding;
            float contentWidth = panel.width - (padding * 2f);
            GUI.Label(
                new Rect(panel.x + padding, panel.y + 15f, contentWidth, 18f),
                "COURIER AERIE  /  CONTRACT EXCHANGE",
                _terminalSubtitleStyle);
            GUI.Label(
                new Rect(panel.x + padding, panel.y + 34f, contentWidth, 40f),
                "AVAILABLE CONTRACTS",
                _terminalTitleStyle);
            _terminalActionStyle.normal.textColor = _hubSettings.TerminalAccentColor;
            GUI.Label(
                new Rect(panel.xMax - padding - 120f, panel.y + 15f, 120f, 18f),
                "ESC  CLOSE",
                _terminalActionStyle);

            string modifierAccess = Progress.CompletedDeliveries >= _settings.DualModifierUnlockDeliveries
                ? "DUAL CARGO  UNLOCKED"
                : $"DUAL CARGO  {_settings.DualModifierUnlockDeliveries - Progress.CompletedDeliveries} RUNS TO GO";
            GUI.Label(
                new Rect(panel.x + padding, panel.y + 78f, contentWidth, 26f),
                $"COMPLETED  {Progress.CompletedDeliveries:000}      AVAILABLE  {_offers.Count:00}      WALLET  {(_wallet != null ? _wallet.Gold : 0):N0} GOLD      {modifierAccess}",
                _terminalMetaStyle);
            DrawSolidRect(
                new Rect(panel.x + padding, panel.y + _hubSettings.TerminalHeaderHeight - 2f, contentWidth, 1f),
                _hubSettings.TerminalDividerColor);

            int columns = _offers.Count > _hubSettings.TerminalExpandedGridThreshold
                ? _hubSettings.TerminalExpandedGridColumns
                : _hubSettings.TerminalCardColumns;
            columns = Mathf.Clamp(columns, 2, 4);
            columns = Mathf.Min(columns, Mathf.Max(1, _offers.Count));
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(_offers.Count / (float)columns));
            float cardsTop = panel.y + _hubSettings.TerminalHeaderHeight + 12f;
            float cardsBottom = panel.yMax - _hubSettings.TerminalFooterHeight - padding;
            float gap = _hubSettings.ContractCardGap;
            float cardWidth = (panel.width - (padding * 2f) - (gap * (columns - 1))) / columns;
            float cardHeight = Mathf.Min(
                _hubSettings.ContractCardHeight,
                (cardsBottom - cardsTop - (gap * (rowCount - 1))) / rowCount);
            CourierContract selectedOffer = null;
            for (int i = 0; i < _offers.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect card = new Rect(panel.x + padding + (column * (cardWidth + gap)), cardsTop + (row * (cardHeight + gap)), cardWidth, cardHeight);
                CourierContract offer = _offers[i];
                if (DrawContractCard(card, offer, i, _offers.Count))
                {
                    selectedOffer = offer;
                }
            }

            DrawSolidRect(
                new Rect(panel.x + padding, panel.yMax - _hubSettings.TerminalFooterHeight, contentWidth, 1f),
                _hubSettings.TerminalDividerColor);
            GUI.Label(
                new Rect(panel.x + padding, panel.yMax - _hubSettings.TerminalFooterHeight + 7f, contentWidth, 22f),
                "SELECT A CONTRACT TO DEPLOY  /  CONTRACTS REFRESH AUTOMATICALLY",
                _terminalSubtitleStyle);
            DrawContractTypeTooltip(panel, virtualWidth, virtualHeight, scale);

            GUI.matrix = previousMatrix;
            GUI.backgroundColor = previousBackground;
            if (selectedOffer != null)
            {
                AcceptContract(selectedOffer);
            }
        }

        private bool DrawContractCard(Rect card, CourierContract offer, int offerIndex, int offerCount)
        {
            bool accepted = GUI.Button(card, GUIContent.none, _terminalButtonStyle);
            Color modifierColor = GetContractModifierColor(offer.DisplayModifiers);
            DrawSolidRect(
                new Rect(card.x, card.y, _hubSettings.TerminalCardAccentWidth, card.height),
                modifierColor);

            float left = card.x + _hubSettings.TerminalCardAccentWidth + 16f;
            float right = card.xMax - 16f;
            float contentWidth = right - left;
            _terminalKickerStyle.normal.textColor = modifierColor;
            GUI.Label(
                new Rect(left, card.y + 12f, contentWidth, 20f),
                new GUIContent(offer.DisplayModifierText, GetContractTypeTooltip(offer.DisplayModifiers)),
                _terminalKickerStyle);

            int pipCount = Mathf.Max(1, offerCount);
            int activePip = Mathf.Clamp(offerIndex, 0, pipCount - 1);
            float pipSize = _hubSettings.TerminalContractOrderPipSize;
            float pipGap = _hubSettings.TerminalContractOrderPipGap;
            float pipStart = right - ((pipSize * pipCount) + (pipGap * (pipCount - 1)));
            for (int i = 0; i < pipCount; i++)
            {
                DrawSolidRect(
                    new Rect(pipStart + (i * (pipSize + pipGap)), card.y + 19f, pipSize, pipSize),
                    i == activePip ? _hubSettings.TerminalAccentColor : _hubSettings.TerminalDividerColor);
            }

            GUI.Label(new Rect(left, card.y + 40f, contentWidth, 27f), offer.DestinationName, _terminalDestinationStyle);
            GUI.Label(
                new Rect(left, card.y + 70f, contentWidth, 21f),
                $"ROUTE  {offer.RouteDistance / 1000f:0.0} KM      RISK  {offer.Difficulty:00}",
                _terminalMetaStyle);
            DrawSolidRect(
                new Rect(left, card.y + 101f, contentWidth, 1f),
                _hubSettings.TerminalDividerColor);

            _terminalKickerStyle.normal.textColor = _hubSettings.TerminalMutedTextColor;
            GUI.Label(new Rect(left, card.y + 111f, contentWidth, 18f), "CONTRACT PAYOUT", _terminalKickerStyle);
            GUI.Label(new Rect(left, card.y + 130f, contentWidth, 28f), $"{offer.OfferedReward:N0} GOLD", _terminalRewardStyle);

            string details = offer.TimeLimit > 0f ? $"EXPRESS {FormatTime(offer.TimeLimit)}" : "OPEN WINDOW";
            if (offer.StopCount > 1) details += $"   /   {offer.StopCount} STOPS";
            GUI.Label(new Rect(left, card.yMax - 29f, contentWidth * 0.72f, 20f), details, _terminalMetaStyle);
            _terminalActionStyle.normal.textColor = modifierColor;
            GUI.Label(new Rect(left, card.yMax - 29f, contentWidth, 20f), "SELECT", _terminalActionStyle);
            return accepted;
        }

        private void DrawContractTypeTooltip(Rect panel, float virtualWidth, float virtualHeight, float scale)
        {
            string tooltip = GUI.tooltip;
            if (string.IsNullOrWhiteSpace(tooltip))
            {
                return;
            }

            int separator = tooltip.IndexOf('\n');
            string title = separator >= 0 ? tooltip.Substring(0, separator) : tooltip;
            string body = separator >= 0 ? tooltip.Substring(separator + 1) : string.Empty;
            float padding = _hubSettings.TerminalTooltipPadding;
            float width = Mathf.Min(_hubSettings.TerminalTooltipWidth, panel.width - (padding * 2f));
            float textWidth = width - (padding * 2f);
            float titleHeight = _terminalTooltipTitleStyle.CalcHeight(new GUIContent(title), textWidth);
            float bodyHeight = _terminalTooltipBodyStyle.CalcHeight(new GUIContent(body), textWidth);
            float height = padding + titleHeight + bodyHeight + padding;
            Vector2 mouse = Event.current.mousePosition / Mathf.Max(0.01f, scale);
            Vector2 offset = _hubSettings.TerminalTooltipMouseOffset;
            float maximumX = Mathf.Min(virtualWidth, panel.xMax) - width - padding;
            float maximumY = Mathf.Min(virtualHeight, panel.yMax) - height - padding;
            float x = Mathf.Clamp(mouse.x + offset.x, panel.x + padding, maximumX);
            float y = Mathf.Clamp(mouse.y + offset.y, panel.y + padding, maximumY);
            Rect tooltipRect = new Rect(x, y, width, height);

            DrawSolidRect(tooltipRect, _hubSettings.TerminalPanelColor);
            DrawBorder(tooltipRect, _hubSettings.TerminalBorderColor, _hubSettings.TerminalPanelBorderThickness);
            DrawSolidRect(
                new Rect(tooltipRect.x, tooltipRect.y, tooltipRect.width, _hubSettings.TerminalAccentBarHeight),
                _hubSettings.TerminalAccentColor);
            GUI.Label(
                new Rect(tooltipRect.x + padding, tooltipRect.y + padding, textWidth, titleHeight),
                title,
                _terminalTooltipTitleStyle);
            GUI.Label(
                new Rect(tooltipRect.x + padding, tooltipRect.y + padding + titleHeight, textWidth, bodyHeight),
                body,
                _terminalTooltipBodyStyle);
        }

        private static string GetContractTypeTooltip(CourierContractModifier modifiers)
        {
            if (modifiers == CourierContractModifier.None)
            {
                return "STANDARD\nNo special cargo conditions. Complete one pickup and one delivery at your own pace.";
            }
            if ((modifiers & CourierContractModifier.Unknown) != 0)
            {
                return "UNKNOWN\nCargo conditions stay classified until deployment. The uncertainty increases the payout.";
            }

            List<string> descriptions = new List<string>();
            AddContractTypeDescription(descriptions, modifiers, CourierContractModifier.Fragile,
                "FRAGILE — Hard impacts damage cargo integrity and can fail the contract.");
            AddContractTypeDescription(descriptions, modifiers, CourierContractModifier.Express,
                "EXPRESS — Complete the route before the delivery timer expires.");
            AddContractTypeDescription(descriptions, modifiers, CourierContractModifier.HighValue,
                "HIGH-VALUE — Increased payout attracts stronger and more frequent encounters.");
            AddContractTypeDescription(descriptions, modifiers, CourierContractModifier.Oversized,
                "OVERSIZED — Heavy cargo reduces speed, acceleration, and turning response.");
            AddContractTypeDescription(descriptions, modifiers, CourierContractModifier.Hazardous,
                "HAZARDOUS — Unstable cargo periodically discharges and threatens hull integrity.");
            AddContractTypeDescription(descriptions, modifiers, CourierContractModifier.MultiDrop,
                "MULTI-DROP — Carry the package through several delivery stops to finish the route.");
            return $"{CourierContract.FormatModifiers(modifiers)}\n{string.Join("\n", descriptions)}";
        }

        private static void AddContractTypeDescription(
            List<string> descriptions,
            CourierContractModifier modifiers,
            CourierContractModifier required,
            string description)
        {
            if ((modifiers & required) != 0)
            {
                descriptions.Add(description);
            }
        }

        private Color GetContractModifierColor(CourierContractModifier modifiers)
        {
            if ((modifiers & CourierContractModifier.Unknown) != 0) return _hubSettings.TerminalUnknownColor;
            if ((modifiers & (CourierContractModifier.Hazardous | CourierContractModifier.Fragile)) != 0) return _hubSettings.TerminalDangerColor;
            if ((modifiers & CourierContractModifier.HighValue) != 0) return _hubSettings.TerminalHighValueColor;
            if ((modifiers & CourierContractModifier.MultiDrop) != 0) return _hubSettings.TerminalMultiDropColor;
            if ((modifiers & CourierContractModifier.Express) != 0) return _hubSettings.HubEnergyColor;
            return _hubSettings.TerminalAccentColor;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            float border = Mathf.Max(0f, thickness);
            if (border <= 0f) return;
            DrawSolidRect(new Rect(rect.x, rect.y, rect.width, border), color);
            DrawSolidRect(new Rect(rect.x, rect.yMax - border, rect.width, border), color);
            DrawSolidRect(new Rect(rect.x, rect.y, border, rect.height), color);
            DrawSolidRect(new Rect(rect.xMax - border, rect.y, border, rect.height), color);
        }

        private void DrawHubHUD()
        {
            if (_terminal == null)
            {
                return;
            }
            float distance = Vector3.Distance(_player.WorldCenter, _terminal.position);
            string prompt = distance <= _hubSettings.TerminalInteractionRadius
                ? "PRESS E — OPEN CONTRACT TERMINAL"
                : $"CONTRACT TERMINAL  {distance:0} m";
            GUI.Label(new Rect(0f, Screen.height - 150f, Screen.width, 32f), prompt, _objectiveStyle);
            GUI.Label(new Rect(24f, 24f, 360f, 86f),
                $"COURIER AERIE\nDELIVERIES  {Progress.CompletedDeliveries}\nCONTRACT GOLD  {Progress.TotalContractGold:N0}", _hudBodyStyle);
        }

        private void DrawContractHUD()
        {
            Rect panel = new Rect(_settings.HudLeft, _settings.HudTop, _settings.HudWidth, _settings.HudHeight);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = _settings.HudPanelColor;
            GUI.Box(panel, GUIContent.none);
            GUI.backgroundColor = old;
            string modifier = ActiveContract.DisplayModifiers == CourierContractModifier.Unknown && !_unknownRevealed
                ? "UNKNOWN CARGO"
                : CourierContract.FormatModifiers(ActiveContract.GameplayModifiers);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 24f), modifier, _hudTitleStyle);
            string objective = State == CourierRunState.FindPackage
                ? "LOCATE AND FLY THROUGH PICKUP RING"
                : _deliveryIndex + 1 < ActiveContract.StopCount
                    ? $"DELIVER STOP {_deliveryIndex + 1} / {ActiveContract.StopCount}"
                    : "DELIVER CARGO";
            string timer = ActiveContract.Has(CourierContractModifier.Express)
                ? $"\nTIME  {FormatTime(ExpressTimeRemaining)}"
                : string.Empty;
            string integrity = State == CourierRunState.Delivering && CargoUsesIntegrity()
                ? $"\nCARGO INTEGRITY  {CargoIntegrity:0}%"
                : string.Empty;
            GUI.Label(new Rect(panel.x + 12f, panel.y + 34f, panel.width - 24f, panel.height - 40f),
                $"{objective}\nREWARD  {ActiveContract.OfferedReward:N0} GOLD{timer}{integrity}", _hudBodyStyle);
            if (State == CourierRunState.Delivering && CargoUsesIntegrity())
            {
                Rect integrityBar = new Rect(panel.x + 12f, panel.yMax - 11f, panel.width - 24f, 5f);
                GUI.Box(integrityBar, GUIContent.none);
                Color previousColor = GUI.color;
                GUI.color = Color.Lerp(
                    _settings.IntegrityCriticalColor,
                    _settings.IntegrityHealthyColor,
                    CargoIntegrity / 100f);
                GUI.DrawTexture(
                    new Rect(integrityBar.x, integrityBar.y, integrityBar.width * Mathf.Clamp01(CargoIntegrity / 100f), integrityBar.height),
                    Texture2D.whiteTexture);
                GUI.color = previousColor;
            }
        }

        private void DrawObjectiveMarker()
        {
            if (ActiveObjective == null || _camera == null)
            {
                return;
            }
            Vector3 projected = _camera.WorldToScreenPoint(ActiveObjective.position);
            Vector2 point = new Vector2(projected.x, Screen.height - projected.y);
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float padding = _settings.ObjectiveEdgePadding;
            bool onScreen = projected.z > 0f && point.x >= padding && point.x <= Screen.width - padding && point.y >= padding && point.y <= Screen.height - padding;
            Vector2 direction = point - center;
            if (!onScreen)
            {
                if (projected.z <= 0f) direction = -direction;
                if (direction.sqrMagnitude < 0.001f) direction = Vector2.up;
                direction.Normalize();
                float horizontal = (center.x - padding) / Mathf.Max(0.001f, Mathf.Abs(direction.x));
                float vertical = (center.y - padding) / Mathf.Max(0.001f, Mathf.Abs(direction.y));
                point = center + (direction * Mathf.Min(horizontal, vertical));
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
                Matrix4x4 previousMatrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, point);
                GUI.Label(new Rect(point.x - 20f, point.y - 20f, 40f, 40f), "▲", _objectiveStyle);
                GUI.matrix = previousMatrix;
            }
            else
            {
                GUI.Label(new Rect(point.x - 20f, point.y - 20f, 40f, 40f), "◆", _objectiveStyle);
            }
            float distance = Vector3.Distance(_player.WorldCenter, ActiveObjective.position);
            GUI.Label(new Rect(point.x - 110f, point.y + 18f, 220f, 30f),
                $"{(State == CourierRunState.FindPackage ? "PICKUP" : "DELIVER")}  {distance:0} m", _objectiveStyle);
        }

        private void DrawTeleportFade()
        {
            float build = _hubSettings.TeleportBuildDuration;
            float fade = _hubSettings.TeleportFadeDuration;
            float alpha = _teleportTimer < build
                ? 0f
                : _teleportTimer < build + fade
                    ? Mathf.Sin(Mathf.Clamp01((_teleportTimer - build) / fade) * Mathf.PI)
                    : 0f;
            Color previous = GUI.color;
            GUI.color = new Color(_hubSettings.HubEnergyColor.r, _hubSettings.HubEnergyColor.g, _hubSettings.HubEnergyColor.b, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static string FormatTime(float seconds)
        {
            int rounded = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{rounded / 60:00}:{rounded % 60:00}";
        }

        private static int CountModifiers(CourierContractModifier modifiers)
        {
            int count = 0;
            int value = (int)(modifiers & ~CourierContractModifier.Unknown);
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        private static string CargoNameFor(CourierContractModifier modifiers)
        {
            if ((modifiers & CourierContractModifier.Hazardous) != 0) return "UNSTABLE ENERGY CORE";
            if ((modifiers & CourierContractModifier.Oversized) != 0) return "HEAVY MACHINERY";
            if ((modifiers & CourierContractModifier.Fragile) != 0) return "PRECISION COMPONENTS";
            if ((modifiers & CourierContractModifier.HighValue) != 0) return "VALUABLE RELAY DATA";
            return "COURIER FREIGHT";
        }

        private static DuneLandmarkType ChooseLandmarkType(System.Random random)
        {
            int value = random.Next(0, 5);
            return (DuneLandmarkType)value;
        }

        private static Transform HubPart(
            PrimitiveType primitive,
            string partName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material,
            bool collider)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null) partCollider.enabled = collider;
            return part.transform;
        }

        private static void BuildCircleModelCollider(
            Transform parent,
            string colliderName,
            Vector3 localPosition,
            float radius)
        {
            GameObject circleModel = Resources.Load<GameObject>("circle");
            if (circleModel == null)
            {
                Debug.LogError("World hub collision requires Assets/DuneVector/Resources/circle.glb.");
                return;
            }

            GameObject colliderRoot = Instantiate(circleModel, parent, false);
            colliderRoot.name = colliderName;
            colliderRoot.transform.localPosition = localPosition;
            colliderRoot.transform.localRotation = Quaternion.identity;
            colliderRoot.transform.localScale = new Vector3(radius, 1f, radius);

            Renderer[] renderers = colliderRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            Collider[] importedColliders = colliderRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < importedColliders.Length; i++)
            {
                Destroy(importedColliders[i]);
            }

            MeshFilter[] meshFilters = colliderRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (meshFilters[i].sharedMesh == null)
                {
                    continue;
                }

                MeshCollider meshCollider = meshFilters[i].gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilters[i].sharedMesh;
            }
        }

        private static void BuildSegmentedRing(
            Transform parent,
            int segmentCount,
            float radius,
            float segmentLength,
            float segmentWidth,
            float segmentHeight,
            Material material,
            string segmentName)
        {
            int count = Mathf.Max(3, segmentCount);
            for (int i = 0; i < count; i++)
            {
                float angle = (360f / count) * i;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                HubPart(
                    PrimitiveType.Cube,
                    $"{segmentName} {i + 1:00}",
                    parent,
                    direction * radius,
                    new Vector3(segmentWidth, segmentHeight, segmentLength),
                    Quaternion.Euler(0f, angle + 90f, 0f),
                    material,
                    false);
            }
        }

        private static Material CreateHubMaterial(
            Material source,
            string materialName,
            Color baseColor,
            Color emission)
        {
            Material material = new Material(source) { name = materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", baseColor);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", emission);
            return material;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Damaged -= HandlePlayerDamaged;
            if (_health != null) _health.Died -= HandlePlayerDied;
            if (_world != null) _world.WorldShifted -= HandleWorldShift;
            DestroyTeleportParticles();
            if (_hubMetalMaterial != null) Destroy(_hubMetalMaterial);
            if (_hubEnergyMaterial != null) Destroy(_hubEnergyMaterial);
            if (_terminalPanelTexture != null) Destroy(_terminalPanelTexture);
            if (_terminalCardTexture != null) Destroy(_terminalCardTexture);
            if (_terminalCardHoverTexture != null) Destroy(_terminalCardHoverTexture);
        }
    }
}
