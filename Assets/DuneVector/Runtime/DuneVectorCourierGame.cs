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
        FreeRoam,
        FindPackage,
        Delivering,
        DeliveryComplete,
        TeleportOut,
        DeliveryMessage,
        ReturnToBase,
        ContractFailed,
    }

    [Serializable]
    public sealed class CourierContract
    {
        public string ContractId;
        public string PickupName;
        public DuneLandmarkType PickupLandmarkType;
        public string DestinationName;
        public DuneLandmarkType DestinationLandmarkType;
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
        [NonSerialized] public readonly List<DuneLandmarkPlacementRecord> RoutePlacementRecords =
            new List<DuneLandmarkPlacementRecord>();
        [NonSerialized] public float PlannedRouteDistance;
        [NonSerialized] public LogicalPosition RouteOrigin;
        [NonSerialized] public double RouteAngle;

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
            public int Version = 6;
            public int CompletedDeliveries;
            public int FailedDeliveries;
            public int TotalContractGold;
            public int HighestDifficulty;
            public int NextDeliveryMessageIndex;
            public int PendingDeliveryMessageIndex = -1;
            public bool DeliveryMessageInputHintAcknowledged;
            public bool StrikeOrbDeathNoteAcknowledged;
            public bool VesperPilgrimDeathNoteAcknowledged;
            public List<string> AcceptedContractIds = new List<string>();
        }

        public int CompletedDeliveries { get; private set; }
        public int FailedDeliveries { get; private set; }
        public int TotalContractGold { get; private set; }
        public int HighestDifficulty { get; private set; }
        public int NextDeliveryMessageIndex { get; private set; }
        public int PendingDeliveryMessageIndex { get; private set; } = -1;
        public bool DeliveryMessageInputHintAcknowledged { get; private set; }
        public bool StrikeOrbDeathNoteAcknowledged { get; private set; }
        public bool VesperPilgrimDeathNoteAcknowledged { get; private set; }
        public IReadOnlyList<string> AcceptedContractIds => _acceptedContractIds;
        public event Action Changed;

        private string _savePath;
        private readonly List<string> _acceptedContractIds = new List<string>();

        public void Initialize()
        {
            _savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
            Load();
        }

        public void RecordCompletion(int reward, int difficulty, bool assignDeliveryMessage)
        {
            CompletedDeliveries++;
            TotalContractGold = TotalContractGold > int.MaxValue - Mathf.Max(0, reward)
                ? int.MaxValue
                : TotalContractGold + Mathf.Max(0, reward);
            HighestDifficulty = Mathf.Max(HighestDifficulty, difficulty);
            if (assignDeliveryMessage && PendingDeliveryMessageIndex < 0)
            {
                PendingDeliveryMessageIndex = NextDeliveryMessageIndex;
            }
            Save();
            Changed?.Invoke();
        }

        public bool CompletePendingDeliveryMessage(int completedSequenceIndex)
        {
            if (completedSequenceIndex < 0 || PendingDeliveryMessageIndex != completedSequenceIndex)
            {
                return false;
            }

            NextDeliveryMessageIndex = Mathf.Max(NextDeliveryMessageIndex, completedSequenceIndex + 1);
            PendingDeliveryMessageIndex = -1;
            Save();
            Changed?.Invoke();
            return true;
        }

        public void AcknowledgeDeliveryMessageInputHint()
        {
            if (DeliveryMessageInputHintAcknowledged)
            {
                return;
            }

            DeliveryMessageInputHintAcknowledged = true;
            Save();
            Changed?.Invoke();
        }

        public void AcknowledgeStrikeOrbDeathNote()
        {
            if (StrikeOrbDeathNoteAcknowledged)
            {
                return;
            }

            StrikeOrbDeathNoteAcknowledged = true;
            Save();
            Changed?.Invoke();
        }

        public void AcknowledgeVesperPilgrimDeathNote()
        {
            if (VesperPilgrimDeathNoteAcknowledged)
            {
                return;
            }

            VesperPilgrimDeathNoteAcknowledged = true;
            Save();
            Changed?.Invoke();
        }

        public void RecordFailure()
        {
            FailedDeliveries++;
            Save();
            Changed?.Invoke();
        }

        public void RecordContractAccepted(string contractId)
        {
            if (string.IsNullOrEmpty(contractId) || _acceptedContractIds.Contains(contractId))
            {
                return;
            }

            _acceptedContractIds.Add(contractId);
            Save();
            Changed?.Invoke();
        }

        public bool WasContractAccepted(string contractId)
        {
            return !string.IsNullOrEmpty(contractId) && _acceptedContractIds.Contains(contractId);
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
                if (data.Version >= 2)
                {
                    NextDeliveryMessageIndex = Mathf.Max(0, data.NextDeliveryMessageIndex);
                    PendingDeliveryMessageIndex = Mathf.Max(-1, data.PendingDeliveryMessageIndex);
                }
                else
                {
                    // Existing completions predate delivery messages and must not replay old narrative.
                    NextDeliveryMessageIndex = CompletedDeliveries;
                    PendingDeliveryMessageIndex = -1;
                }
                DeliveryMessageInputHintAcknowledged =
                    data.Version >= 3 && data.DeliveryMessageInputHintAcknowledged;
                StrikeOrbDeathNoteAcknowledged =
                    data.Version >= 5 && data.StrikeOrbDeathNoteAcknowledged;
                VesperPilgrimDeathNoteAcknowledged =
                    data.Version >= 6 && data.VesperPilgrimDeathNoteAcknowledged;
                _acceptedContractIds.Clear();
                if (data.Version >= 4 && data.AcceptedContractIds != null)
                {
                    for (int i = 0; i < data.AcceptedContractIds.Count; i++)
                    {
                        string contractId = data.AcceptedContractIds[i];
                        if (!string.IsNullOrEmpty(contractId) && !_acceptedContractIds.Contains(contractId))
                        {
                            _acceptedContractIds.Add(contractId);
                        }
                    }
                }
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
                    NextDeliveryMessageIndex = NextDeliveryMessageIndex,
                    PendingDeliveryMessageIndex = PendingDeliveryMessageIndex,
                    DeliveryMessageInputHintAcknowledged = DeliveryMessageInputHintAcknowledged,
                    StrikeOrbDeathNoteAcknowledged = StrikeOrbDeathNoteAcknowledged,
                    VesperPilgrimDeathNoteAcknowledged = VesperPilgrimDeathNoteAcknowledged,
                    AcceptedContractIds = new List<string>(_acceptedContractIds),
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
        private const string HubRuneRingResourcePath = "rune_ringPrefab";

        private enum HubTerminalMode
        {
            None,
            Contracts,
            MessageArchive,
            FreeRoam,
        }

        public CourierRunState State { get; private set; }
        public CourierContract ActiveContract { get; private set; }
        public Transform ActiveObjective { get; private set; }
        public LogicalPosition ActiveObjectiveLogicalPosition { get; private set; }
        public bool IsContractActive => State == CourierRunState.FindPackage || State == CourierRunState.Delivering;
        public bool IsCarryingCargo => State == CourierRunState.Delivering;
        public bool IsTerminalOpen => _hubTerminalMode != HubTerminalMode.None;
        public bool IsDeliveryMessageOpen => _messagePresenter != null && _messagePresenter.IsOpen;
        public Vector3 HubSpawnPosition => _hubSpawn;
        public Transform ContractTerminal => _terminal;
        public Transform MessageArchiveTerminal => _messageArchiveTerminal;
        public Transform FreeRoamTerminal => _freeRoamTerminal;
        public Transform HubRuneRing => _hubRuneRing;
        public DuneVectorDesertAtlas DesertAtlas { get; private set; }
        public int ArchivedMessageCount => GetArchivedMessageCount();
        public static bool IsGameplayHudSuppressed
        {
            get
            {
                DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
                return IsMapHudSuppressed ||
                    DuneVectorMapHUD.IsWorldMapOpen ||
                    (bootstrap != null &&
                     bootstrap.PauseMenu != null &&
                     bootstrap.PauseMenu.IsCompendiumOpen);
            }
        }
        public static bool IsMapHudSuppressed
        {
            get
            {
                DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
                return bootstrap != null && bootstrap.CourierGame != null &&
                    (DuneVectorPhotographySystem.IsCameraModeActive ||
                     bootstrap.CourierGame.IsTerminalOpen ||
                     bootstrap.CourierGame.State == CourierRunState.DeliveryMessage);
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
        private DronePermanentUpgradeSystem _permanentUpgrades;
        private DuneVectorLandmarkDirector _landmarks;
        private CourierContractTuning _settings;
        private DeliveryMessageTuning _messageSettings;
        private DeliveryTuning _deliverySettings;
        private DuneVectorWindFieldSystem _windFields;
        private WorldHubTuning _hubSettings;
        private DesertAtlasTuning _desertAtlasSettings;
        private DuneVectorEnemyDirector _enemyDirector;
        private DuneVectorStormPyramidDirector _stormDirector;
        private DuneVectorVesperKiteDirector _vesperKiteDirector;
        private DuneVectorRouteEncounterDirector _routeEncounterDirector;
        private DuneVectorEnvironmentalHazardSystem _environmentalHazards;
        private DuneVectorDeliveryMessagePresenter _messagePresenter;
        private DuneVectorSandAmbusherSystem _sandAmbusherSystem;

        private Transform _hubRoot;
        private Transform _hubRuneRing;
        private Transform _terminal;
        private Transform _messageArchiveTerminal;
        private Transform _freeRoamTerminal;
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
        private int _archiveReplayClosedFrame = -1;
        private float _stateTimer;
        private float _hazardPulseTimer;
        private float _unknownRevealTimer;
        private float _offerRefreshTimer;
        private float _teleportTimer;
        private bool _teleportMoved;
        private bool _returnStartsVanished;
        private bool _deliveryCompletionInProgress;
        private bool _deliveryMessageSafetyActive;
        private bool _infiniteHealthBeforeDeliveryMessage;
        private HubTerminalMode _hubTerminalMode;
        private bool _unknownRevealed;
        private bool _wasGrounded;
        private float _minimumAirVerticalSpeed;
        private string _statusMessage;
        private float _statusMessageUntil;
        private Vector3 _droneVisualOriginalScale;
        private Material _hubMetalMaterial;
        private Material _hubEnergyMaterial;
        private Material _hubPlatformEnergyMaterial;
        private readonly List<Material> _hubTerminalPanelMaterials = new List<Material>();
        private readonly List<Material> _hubTerminalAntennaMaterials = new List<Material>();
        private bool _hubRgbTerminalsApplied;

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
        private GUIStyle _archiveTitleStyle;
        private GUIStyle _archiveEntryStyle;
        private GUIStyle _archiveMetaStyle;
        private GUIStyle _archiveEmptyStyle;
        private GUIStyle _archiveTileButtonStyle;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _hudBodyStyle;
        private GUIStyle _objectiveStyle;
        private GUIStyle _statusStyle;
        private readonly DuneVectorObjectiveIndicator _objectiveIndicator = new DuneVectorObjectiveIndicator();
        private Texture2D _terminalPanelTexture;
        private Texture2D _terminalCardTexture;
        private Texture2D _terminalCardHoverTexture;
        private Texture2D _archiveTileTexture;
        private Texture2D _archiveTileHoverTexture;
        private Vector2 _archiveScrollPosition;

        private float HubPlatformSurfaceRadius => Mathf.Max(0f, _hubSettings.PlatformRadius * 0.5f);

        public void Initialize(
            DronePlayer playerInput,
            DroneCharacterController player,
            DroneHealth health,
            DesertWorldStreamer world,
            Camera camera,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            DronePermanentUpgradeSystem permanentUpgrades,
            DuneVectorLandmarkDirector landmarks,
            DeliveryTuning deliverySettings,
            DuneVectorWindFieldSystem windFields,
            CourierContractTuning settings,
            DeliveryMessageTuning messageSettings,
            WorldHubTuning hubSettings,
            DesertAtlasTuning desertAtlasSettings,
            CompassHudTuning compassHudSettings,
            DuneVectorEnemyDirector enemyDirector,
            DuneVectorStormPyramidDirector stormDirector,
            DuneVectorVesperKiteDirector vesperKiteDirector)
        {
            _playerInput = playerInput;
            _player = player;
            _health = health;
            _world = world;
            _camera = camera;
            _cameraController = camera != null ? camera.GetComponent<DroneCameraController>() : null;
            _materials = materials;
            _wallet = wallet;
            _permanentUpgrades = permanentUpgrades;
            _landmarks = landmarks;
            _deliverySettings = deliverySettings;
            _windFields = windFields;
            _settings = settings;
            _messageSettings = messageSettings ?? new DeliveryMessageTuning();
            _messageSettings.EnsureInitialized();
            _hubSettings = hubSettings;
            _desertAtlasSettings = desertAtlasSettings ?? new DesertAtlasTuning();
            _desertAtlasSettings.EnsureInitialized();
            _enemyDirector = enemyDirector;
            _stormDirector = stormDirector;
            _vesperKiteDirector = vesperKiteDirector;
            Progress = gameObject.AddComponent<DuneVectorCourierProgress>();
            Progress.Initialize();
            DesertAtlas = gameObject.AddComponent<DuneVectorDesertAtlas>();
            DesertAtlas.Initialize(
                _player, _health, _world, _materials, _wallet, Progress, this,
                _desertAtlasSettings, compassHudSettings, _deliverySettings, _camera);
            _messagePresenter = gameObject.AddComponent<DuneVectorDeliveryMessagePresenter>();
            _messagePresenter.Initialize(
                _messageSettings,
                Progress.DeliveryMessageInputHintAcknowledged,
                Progress.AcknowledgeDeliveryMessageInputHint);
            _sandAmbusherSystem = gameObject.AddComponent<DuneVectorSandAmbusherSystem>();
            _sandAmbusherSystem.Initialize(_player, _health, _world, _settings);
            _health.Damaged += HandlePlayerDamaged;
            _health.Died += HandlePlayerDied;
            _world.WorldShifted += HandleWorldShift;
            if (_player.DroneVisualRoot != null)
            {
                _droneVisualOriginalScale = _player.DroneVisualRoot.localScale;
            }
            BuildHub();
            GenerateOffers();
            EnterHubImmediate(openTerminal: false);
            if (Progress.PendingDeliveryMessageIndex >= 0 &&
                _messageSettings.TryResolve(Progress.PendingDeliveryMessageIndex, out DeliveryMessageAsset pendingMessage))
            {
                BeginDeliveryMessageSafety();
                if (_player.DroneVisualRoot != null)
                {
                    _player.DroneVisualRoot.localScale = Vector3.zero;
                }
                State = CourierRunState.DeliveryMessage;
                _playerInput.SetInputEnabled(false);
                _messagePresenter.Open(pendingMessage, HandleDeliveryMessageCompleted);
            }
        }

        public void BindEnvironmentalHazardSystem(DuneVectorEnvironmentalHazardSystem environmentalHazards)
        {
            _environmentalHazards = environmentalHazards;
            _environmentalHazards?.SetGameplayActive(
                State != CourierRunState.Hub &&
                State != CourierRunState.DeliveryComplete &&
                State != CourierRunState.TeleportOut &&
                State != CourierRunState.DeliveryMessage &&
                State != CourierRunState.ReturnToBase);
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
            if (State == CourierRunState.Hub ||
                State == CourierRunState.DeliveryComplete ||
                State == CourierRunState.TeleportOut ||
                State == CourierRunState.DeliveryMessage ||
                State == CourierRunState.ReturnToBase)
            {
                return;
            }
            if (IsContractActive)
            {
                FailContract("CONTRACT ABANDONED", recordFailure: recordAbandonment, beginReturn: false);
            }
            BeginTeleport(toHub: true);
        }

        public void RestartAtHub()
        {
            DestroyTeleportParticles();
            _routeEncounterDirector?.EndContract();
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _droneVisualOriginalScale;
            }
            _returnStartsVanished = false;
            _deliveryCompletionInProgress = false;
            EndDeliveryMessageSafety();
            EnterHubImmediate(openTerminal: false);
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

        public bool StartFreeRoam()
        {
            if (State != CourierRunState.Hub)
            {
                return false;
            }

            ActiveContract = null;
            CleanupContractObjects();
            _landmarks?.ClearContractLandmarks();
            PrepareFreeRoamDeployment();
            BeginTeleport(toHub: false);
            return true;
        }

        private void PrepareFreeRoamDeployment()
        {
            float headingRadians = _hubSettings.FreeRoamDeploymentHeadingDegrees * Mathf.Deg2Rad;
            Vector3 heading = new Vector3(
                Mathf.Cos(headingRadians),
                0f,
                Mathf.Sin(headingRadians));
            Vector3 hubPosition = _hubRoot != null
                ? _hubRoot.position
                : _world.LogicalToLocal(
                    DesertWorldStreamer.StartingLogicalPosition.x,
                    0f,
                    DesertWorldStreamer.StartingLogicalPosition.y);
            float terrainHeight = _world.SampleHeightAtLocal(hubPosition.x, hubPosition.z);
            _desertSpawn = new Vector3(
                hubPosition.x,
                terrainHeight + _hubSettings.DesertInsertionHeight,
                hubPosition.z);
            _desertRotation = Quaternion.LookRotation(heading, Vector3.up);
        }

        private void Update()
        {
            if (_world == null || _player == null)
            {
                return;
            }

            if (State == CourierRunState.TeleportingToDesert || State == CourierRunState.ReturnToBase)
            {
                UpdateTeleport();
                return;
            }

            if (State == CourierRunState.TeleportOut)
            {
                UpdateDeliveryTeleportOut();
                return;
            }

            if (State == CourierRunState.DeliveryMessage)
            {
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
            else if (State == CourierRunState.DeliveryComplete || State == CourierRunState.ContractFailed)
            {
                _stateTimer -= Time.deltaTime;
                if (_stateTimer <= 0f)
                {
                    if (State == CourierRunState.DeliveryComplete && Progress.PendingDeliveryMessageIndex >= 0)
                    {
                        BeginDeliveryTeleportOut();
                    }
                    else
                    {
                        BeginTeleport(toHub: true);
                    }
                }
            }
        }

        private void UpdateHub()
        {
            EnforceHubContainment();
            AnimateHubPresentation();
            _offerRefreshTimer -= Time.unscaledDeltaTime;
            if (_offerRefreshTimer <= 0f)
            {
                GenerateOffers();
            }

            if (_messagePresenter != null && _messagePresenter.IsOpen)
            {
                return;
            }
            if (_archiveReplayClosedFrame == Time.frameCount)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (_hubTerminalMode != HubTerminalMode.None &&
                keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetHubTerminalMode(HubTerminalMode.None);
                return;
            }

            if (_hubTerminalMode == HubTerminalMode.None &&
                keyboard != null && keyboard.eKey.wasPressedThisFrame &&
                TryGetNearestHubTerminal(out HubTerminalMode mode, out _, out float distance, out float radius) &&
                distance <= radius)
            {
                if (mode == HubTerminalMode.FreeRoam)
                {
                    StartFreeRoam();
                }
                else
                {
                    SetHubTerminalMode(mode);
                }
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
                    _health.TakeDamage(
                        GetHazardousPulseDamage(),
                        "Hazardous Cargo pulse",
                        _settings.HazardousPulseDeathMessage);
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

        private float GetHazardousPulseDamage()
        {
            float referenceDistance = _settings.EvaluateMinimumRouteDistance(0);
            float routeDistance = ActiveContract.RouteDistance;
            if (referenceDistance <= 0f || routeDistance <= referenceDistance)
            {
                return _settings.HazardousPulseDamage;
            }

            float minimumMultiplier = Mathf.Clamp01(_settings.HazardousPulseMinimumDistanceMultiplier);
            float distanceMultiplier = Mathf.Clamp(referenceDistance / routeDistance, minimumMultiplier, 1f);
            return _settings.HazardousPulseDamage * distanceMultiplier;
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
            _hubPlatformEnergyMaterial = CreateHubMaterial(
                _materials.DroneAccent,
                "World Hub Platform Energy",
                new Color(
                    _hubSettings.PlatformEnergyColor.r * 0.12f,
                    _hubSettings.PlatformEnergyColor.g * 0.12f,
                    _hubSettings.PlatformEnergyColor.b * 0.12f),
                _hubSettings.PlatformEnergyColor);
            LogicalPosition hubLogical = new LogicalPosition(
                DesertWorldStreamer.StartingLogicalPosition.x,
                DesertWorldStreamer.StartingLogicalPosition.y);
            float groundHeight = (float)_world.HeightField.SampleHeight(hubLogical.X, hubLogical.Z);
            float platformY = groundHeight + _hubSettings.PlatformHeightAboveTerrain;
            GameObject hubObject = new GameObject("World Hub - Courier Aerie");
            _hubRoot = hubObject.transform;
            _hubRoot.SetParent(transform, false);
            _hubRoot.position = _world.LogicalToLocal(hubLogical.X, platformY, hubLogical.Z);
            BuildHubRuneRing();

            HubPart(PrimitiveType.Cylinder, "Main Teleport Platform", _hubRoot, Vector3.zero,
                new Vector3(_hubSettings.PlatformRadius, _hubSettings.PlatformThickness * 0.5f, _hubSettings.PlatformRadius),
                Quaternion.identity, _hubMetalMaterial, false);
            BuildCircleModelCollider(
                _hubRoot,
                "Main Teleport Platform Collider (circle.glb)",
                Vector3.up * (_hubSettings.PlatformThickness * 0.5f),
                HubPlatformSurfaceRadius);
            BuildHubContainment();
            HubPart(PrimitiveType.Cylinder, "Energy Inlay", _hubRoot,
                new Vector3(0f, (_hubSettings.PlatformThickness * 0.5f) + 0.08f, 0f),
                new Vector3(_hubSettings.PlatformRadius * 0.72f, 0.08f, _hubSettings.PlatformRadius * 0.72f),
                Quaternion.identity, _hubEnergyMaterial, false);

            if (_hubSettings.PlatformEnergyLanesEnabled)
            {
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
                    _hubPlatformEnergyMaterial,
                    "Platform Energy Lane");
            }

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

            _terminal = BuildPhysicalTerminal(
                "Physical Contract Terminal",
                Vector3.forward * _hubSettings.TerminalForwardOffset,
                Quaternion.identity);
            _messageArchiveTerminal = BuildPhysicalTerminal(
                "Physical Message Archive Terminal",
                _hubSettings.ArchiveTerminalLocalPosition,
                Quaternion.Euler(_hubSettings.ArchiveTerminalLocalEulerAngles));
            _freeRoamTerminal = BuildPhysicalTerminal(
                "Physical Free Roam Terminal",
                Vector3.left * _hubSettings.FreeRoamTerminalLeftOffset,
                Quaternion.Euler(0f, -90f, 0f));

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
            DuneVectorPhotographableMarker.Register(
                hubObject,
                DuneVectorCompendiumSubjectIds.Hub,
                PhotographableSubjectCategory.Misc,
                minimumObserverHorizontalDistance:
                    _hubSettings.PhotographySuppressionRadius);
        }

        private void BuildHubRuneRing()
        {
            if (_hubRuneRing != null)
            {
                return;
            }

            GameObject runeRingPrefab = Resources.Load<GameObject>(HubRuneRingResourcePath);
            if (runeRingPrefab == null)
            {
                Debug.LogError(
                    $"World hub requires Assets/DuneVector/Resources/{HubRuneRingResourcePath}.prefab.",
                    this);
                return;
            }

            GameObject runeRing = Instantiate(runeRingPrefab);
            runeRing.name = runeRingPrefab.name;
            _hubRuneRing = runeRing.transform;

            // Preserve the prefab-authored world position, rotation, and scale, then make the
            // ring part of the hub so floating-origin shifts cannot leave copies in the desert.
            _hubRuneRing.SetParent(_hubRoot, true);
        }

        private Transform BuildPhysicalTerminal(string objectName, Vector3 localPosition, Quaternion localRotation)
        {
            Transform terminal = new GameObject(objectName).transform;
            terminal.SetParent(_hubRoot, false);
            terminal.SetLocalPositionAndRotation(localPosition, localRotation);
            Material terminalPanelMaterial = new Material(_hubEnergyMaterial)
            {
                name = $"{objectName} RGB Panel",
            };
            Material terminalAntennaMaterial = new Material(_hubEnergyMaterial)
            {
                name = $"{objectName} RGB Antennae",
            };
            _hubTerminalPanelMaterials.Add(terminalPanelMaterial);
            _hubTerminalAntennaMaterials.Add(terminalAntennaMaterial);
            HubPart(
                PrimitiveType.Cube,
                "Terminal Pedestal",
                terminal,
                _hubSettings.TerminalPedestalLocalPosition,
                _hubSettings.TerminalPedestalScale,
                Quaternion.identity,
                _hubMetalMaterial,
                true);
            HubPart(
                PrimitiveType.Cube,
                "Terminal Screen",
                terminal,
                _hubSettings.TerminalScreenLocalPosition,
                _hubSettings.TerminalScreenScale,
                Quaternion.Euler(_hubSettings.TerminalScreenTilt, 0f, 0f),
                terminalPanelMaterial,
                false);
            HubPart(
                PrimitiveType.Cube,
                "Terminal Header",
                terminal,
                _hubSettings.TerminalHeaderLocalPosition,
                _hubSettings.TerminalHeaderScale,
                Quaternion.identity,
                _hubMetalMaterial,
                false);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 mastPosition = _hubSettings.TerminalSignalMastLocalPosition;
                mastPosition.x = side * _hubSettings.TerminalSignalMastHorizontalOffset;
                HubPart(
                    PrimitiveType.Cylinder,
                    $"Terminal Signal Mast {(side < 0 ? "Left" : "Right")}",
                    terminal,
                    mastPosition,
                    _hubSettings.TerminalSignalMastScale,
                    Quaternion.identity,
                    terminalAntennaMaterial,
                    false);
            }
            return terminal;
        }

        private void BuildHubContainment()
        {
            if (!_hubSettings.ContainmentEnabled)
            {
                return;
            }

            int segmentCount = Mathf.Max(3, _hubSettings.ContainmentWallSegments);
            float wallThickness = Mathf.Max(0.01f, _hubSettings.ContainmentWallThickness);
            float innerRadius = Mathf.Max(
                wallThickness,
                HubPlatformSurfaceRadius - wallThickness);
            float wallCenterRadius = innerRadius + (wallThickness * 0.5f);
            float wallHeight = Mathf.Max(0.01f, _hubSettings.ContainmentWallHeight);
            float segmentLength = (2f * wallCenterRadius * Mathf.Tan(Mathf.PI / segmentCount)) + wallThickness;

            Transform boundaryRoot = new GameObject("Invisible Hub Containment Boundary").transform;
            boundaryRoot.SetParent(_hubRoot, false);
            boundaryRoot.localPosition = Vector3.up * (
                (_hubSettings.PlatformThickness * 0.5f) + (wallHeight * 0.5f));

            for (int i = 0; i < segmentCount; i++)
            {
                float angle = (360f / segmentCount) * i;
                Vector3 outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                GameObject segment = new GameObject($"Containment Wall {i + 1:00}");
                segment.transform.SetParent(boundaryRoot, false);
                segment.transform.localPosition = outward * wallCenterRadius;
                segment.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
                BoxCollider wallCollider = segment.AddComponent<BoxCollider>();
                wallCollider.size = new Vector3(segmentLength, wallHeight, wallThickness);
                _cameraController?.IgnoredColliders.Add(wallCollider);
            }
        }

        private void EnforceHubContainment()
        {
            if (!_hubSettings.ContainmentEnabled || _hubRoot == null || _player?.Motor == null)
            {
                return;
            }

            KinematicCharacterMotor motor = _player.Motor;
            float wallInnerRadius = Mathf.Max(
                0f,
                HubPlatformSurfaceRadius - _hubSettings.ContainmentWallThickness);
            float safeRadius = Mathf.Max(
                0f,
                wallInnerRadius - motor.Capsule.radius - _hubSettings.ContainmentSafetyPadding);
            Vector3 position = motor.TransientPosition;
            Vector3 hubOffset = position - _hubRoot.position;
            Vector3 planarOffset = Vector3.ProjectOnPlane(hubOffset, Vector3.up);
            bool positionChanged = false;

            float platformSurfaceY = _hubRoot.position.y + (_hubSettings.PlatformThickness * 0.5f);
            if (planarOffset.sqrMagnitude <= safeRadius * safeRadius)
            {
                _player.TryFinishFlightOnSurface(platformSurfaceY, Vector3.up);
            }

            if (planarOffset.sqrMagnitude > safeRadius * safeRadius)
            {
                Vector3 outward = planarOffset.sqrMagnitude > 0f ? planarOffset.normalized : Vector3.forward;
                position = _hubRoot.position + (outward * safeRadius) + (Vector3.up * hubOffset.y);
                float outwardSpeed = Vector3.Dot(motor.BaseVelocity, outward);
                if (outwardSpeed > 0f)
                {
                    motor.BaseVelocity -= outward * outwardSpeed;
                }
                positionChanged = true;
            }

            float platformRecoveryY = platformSurfaceY - _hubSettings.PlatformThickness;
            if (position.y < platformRecoveryY)
            {
                position.y = platformSurfaceY;
                if (motor.BaseVelocity.y < 0f)
                {
                    motor.BaseVelocity = Vector3.ProjectOnPlane(motor.BaseVelocity, Vector3.up);
                }
                positionChanged = true;
            }

            if (positionChanged)
            {
                motor.SetPosition(position, true);
            }
        }

        private void AnimateHubPresentation()
        {
            float deltaTime = Time.unscaledDeltaTime;
            AnimateUnlockedHubTerminals();
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

        private void AnimateUnlockedHubTerminals()
        {
            if (_hubTerminalPanelMaterials.Count == 0 || _permanentUpgrades == null)
            {
                return;
            }

            if (!_permanentUpgrades.AreHubRgbTerminalsEnabled)
            {
                ResetHubTerminalEnergyMaterials();
                return;
            }

            HubRgbTerminalUnlockTuning tuning = _permanentUpgrades.HubRgbTerminalTuning;
            if (tuning == null)
            {
                ResetHubTerminalEnergyMaterials();
                return;
            }

            float basePhase = Time.unscaledTime * Mathf.Max(0.01f, tuning.ColorCycleSpeed);
            for (int index = 0; index < _hubTerminalPanelMaterials.Count; index++)
            {
                float panelPhase = Mathf.Repeat(basePhase + (index * tuning.StartingPhaseOffset), 3f);
                ApplyRgbPhase(_hubTerminalPanelMaterials[index], tuning, panelPhase);

                if (index < _hubTerminalAntennaMaterials.Count)
                {
                    float antennaPhase = Mathf.Repeat(
                        panelPhase + tuning.AntennaStartingPhaseOffset,
                        3f);
                    ApplyRgbPhase(_hubTerminalAntennaMaterials[index], tuning, antennaPhase);
                }
            }
            _hubRgbTerminalsApplied = true;
        }

        private static void ApplyRgbPhase(
            Material material,
            HubRgbTerminalUnlockTuning tuning,
            float phase)
        {
            if (material == null)
            {
                return;
            }

            Color blended = EvaluateRgbBlend(tuning, phase);
            SetHubMaterialColors(
                material,
                ScaleRgb(blended, Mathf.Clamp01(tuning.BaseColorIntensity)),
                ScaleRgb(blended, Mathf.Max(0f, tuning.EmissionIntensity)));
        }

        private void ResetHubTerminalEnergyMaterials()
        {
            if (!_hubRgbTerminalsApplied || _hubEnergyMaterial == null)
            {
                return;
            }

            ResetHubTerminalEnergyMaterials(_hubTerminalPanelMaterials);
            ResetHubTerminalEnergyMaterials(_hubTerminalAntennaMaterials);
            _hubRgbTerminalsApplied = false;
        }

        private void ResetHubTerminalEnergyMaterials(List<Material> materials)
        {
            for (int index = 0; index < materials.Count; index++)
            {
                Material material = materials[index];
                if (material == null)
                {
                    continue;
                }

                string materialName = material.name;
                material.CopyPropertiesFromMaterial(_hubEnergyMaterial);
                material.name = materialName;
            }
        }

        private static Color EvaluateRgbBlend(HubRgbTerminalUnlockTuning tuning, float phase)
        {
            return phase < 1f
                ? Color.Lerp(tuning.Red, tuning.Green, phase)
                : phase < 2f
                    ? Color.Lerp(tuning.Green, tuning.Blue, phase - 1f)
                    : Color.Lerp(tuning.Blue, tuning.Red, phase - 2f);
        }

        private static Color ScaleRgb(Color color, float intensity)
        {
            return new Color(
                color.r * intensity,
                color.g * intensity,
                color.b * intensity,
                color.a);
        }

        private void EnterHubImmediate(bool openTerminal, bool placePlayerAtSpawn = true)
        {
            _health.SetDamageImmune(true);
            CleanupContractObjects();
            _landmarks?.ClearContractLandmarks();
            ActiveContract = null;
            CargoIntegrity = 100f;
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            if (_hubSettings.RestoreHealthOnReturn && !_health.IsDead)
            {
                _health.RestoreHealth(_health.MaximumHealth);
            }
            if (_hubSettings.RestoreStaminaOnReturn)
            {
                _player.RestoreStaminaToFull();
            }
            if (placePlayerAtSpawn)
            {
                _player.Motor.SetPositionAndRotation(_hubSpawn, Quaternion.identity, true);
                _player.ResetTraversalAfterTeleport(Vector3.forward);
                _cameraController?.SnapToTarget();
            }
            SetCombatSystemsActive(false);
            _sandAmbusherSystem?.EndContract();
            DuneVectorContractRisk.Reset();
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
            int offerIndex = 0;
            while (_offers.Count < count)
            {
                CourierContract offer = CreateOffer(random, offerIndex, completionTier);
                offerIndex++;
                if (!Progress.WasContractAccepted(offer.ContractId))
                {
                    _offers.Add(offer);
                }
            }
            _offerRefreshTimer = Mathf.Max(5f, _settings.ContractRefreshSeconds);
        }

        private CourierContract CreateOffer(System.Random random, int index, int completed)
        {
            int seed = random.Next();
            int difficulty = Mathf.Clamp(
                (completed / 10) + random.Next(0, 3),
                0,
                Mathf.Max(1, _settings.MaximumRisk));
            float distance = Mathf.Lerp(
                _settings.EvaluateMinimumRouteDistance(difficulty),
                _settings.EvaluateMaximumRouteDistance(difficulty),
                (float)random.NextDouble());
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
            multiplier *= DuneVectorContractRisk.GetRewardMultiplier(_settings, difficulty);

            DuneLandmarkType pickupType = ChooseLandmarkType(random);
            DuneLandmarkType destinationType = ChooseLandmarkType(random);
            CourierContract contract = new CourierContract
            {
                ContractId = $"DV-{completed:0000}-{index + 1:00}-{Math.Abs(seed % 10000):0000}",
                PickupName = GetContractLocationName(pickupType),
                PickupLandmarkType = pickupType,
                DestinationName = GetContractLocationName(destinationType),
                DestinationLandmarkType = destinationType,
                CargoName = display == CourierContractModifier.Unknown ? "CLASSIFIED" : CargoNameFor(gameplay),
                Seed = seed,
                Difficulty = difficulty,
                RouteDistance = distance,
                PlannedRouteDistance = distance,
                StopCount = stops,
                EncounterIntensity = (gameplay & CourierContractModifier.HighValue) != 0 ? 1.8f : 1f + (difficulty * 0.04f),
                DisplayModifiers = display,
                GameplayModifiers = gameplay,
            };
            ResolveRouteLandmarks(contract);
            distance = contract.RouteDistance;
            float baseReward = Mathf.Lerp(
                _settings.MinimumBaseReward,
                _settings.MaximumBaseReward,
                distance / Mathf.Max(1f, _settings.EvaluateMaximumRouteDistance(_settings.MaximumRisk)));
            baseReward += distance * _settings.DistanceRewardPerMeter;
            contract.BaseReward = Mathf.RoundToInt(baseReward);
            contract.OfferedReward = Mathf.RoundToInt(baseReward * multiplier);
            contract.TimeLimit = (gameplay & CourierContractModifier.Express) != 0
                ? (distance / _settings.ExpressExpectedSpeed) + _settings.ExpressGraceSeconds
                : 0f;
            return contract;
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
            Progress.RecordContractAccepted(contract.ContractId);
            GenerateOffers();
            if (_settings.DebugCompleteContractsInstantlyWithoutPayout)
            {
                CompleteContractInstantlyWithoutPayout(contract);
                return;
            }
            ActiveContract = contract;
            PrepareRoute(contract);
            SetTerminalOpen(false);
            BeginTeleport(toHub: false);
        }

        private void CompleteContractInstantlyWithoutPayout(CourierContract contract)
        {
            SetTerminalOpen(false);
            ActiveContract = contract;
            bool hasAssignedMessage = _messageSettings.TryResolve(
                Progress.NextDeliveryMessageIndex,
                out DeliveryMessageAsset deliveryMessage);
            Progress.RecordCompletion(0, contract.Difficulty, hasAssignedMessage);
            ContractEnded?.Invoke(contract);
            ActiveContract = null;
            GenerateOffers();
            ShowStatus("DEBUG CONTRACT COMPLETE — NO PAYOUT", _settings.CompletionReturnDelay);

            if (!hasAssignedMessage)
            {
                return;
            }

            State = CourierRunState.DeliveryMessage;
            _playerInput.SetInputEnabled(false);
            BeginDeliveryMessageSafety();
            if (!_messagePresenter.Open(deliveryMessage, HandleInstantContractMessageCompleted))
            {
                Debug.LogError(
                    $"Debug-completed contract message {Progress.PendingDeliveryMessageIndex} could not be opened. Its progression index remains pending.",
                    this);
                EnterHubImmediate(openTerminal: false);
                EndDeliveryMessageSafety();
            }
        }

        private void HandleInstantContractMessageCompleted()
        {
            if (State != CourierRunState.DeliveryMessage)
            {
                return;
            }

            int completedMessageIndex = Progress.PendingDeliveryMessageIndex;
            if (completedMessageIndex >= 0)
            {
                Progress.CompletePendingDeliveryMessage(completedMessageIndex);
            }
            EnterHubImmediate(openTerminal: false);
            EndDeliveryMessageSafety();
        }

        private void PrepareRoute(CourierContract contract)
        {
            CleanupContractObjects();
            _landmarks.ClearContractLandmarks();
            _routeLandmarks.Clear();
            if (contract.RoutePlacementRecords.Count == 0)
            {
                ResolveRouteLandmarks(contract);
            }
            LogicalPosition routeOrigin = contract.RouteOrigin;
            double routeAngle = contract.RouteAngle;
            for (int i = 0; i < contract.RoutePlacementRecords.Count; i++)
            {
                DuneVectorLandmarkInstance landmark =
                    _landmarks.PinWorldLandmark(contract.RoutePlacementRecords[i]);
                if (landmark == null)
                {
                    throw new InvalidOperationException("A resolved contract landmark could not be pinned.");
                }
                _routeLandmarks.Add(landmark);
            }

            Vector3 pickupForward = new Vector3(
                (float)(contract.PickupPosition.X - routeOrigin.X),
                0f,
                (float)(contract.PickupPosition.Z - routeOrigin.Z)).normalized;
            if (pickupForward.sqrMagnitude < 0.001f)
            {
                pickupForward = new Vector3((float)Math.Cos(routeAngle), 0f, (float)Math.Sin(routeAngle));
            }
            float insertionHeight = (float)_world.HeightField.SampleHeight(routeOrigin.X, routeOrigin.Z) + _hubSettings.DesertInsertionHeight;
            _desertSpawn = _world.LogicalToLocal(routeOrigin.X, insertionHeight, routeOrigin.Z);
            _desertRotation = Quaternion.LookRotation(pickupForward, Vector3.up);
            BuildPickupObjective();
            ProtectContractObjectivesFromWind();

            double objectiveDeltaX = ActiveObjectiveLogicalPosition.X - routeOrigin.X;
            double objectiveDeltaZ = ActiveObjectiveLogicalPosition.Z - routeOrigin.Z;
            double objectiveDistance = Math.Sqrt(
                (objectiveDeltaX * objectiveDeltaX) + (objectiveDeltaZ * objectiveDeltaZ));
            double maximumPickupSpawnDistance = Math.Max(1.0, _settings.MaximumPickupSpawnDistance);
            if (objectiveDistance > maximumPickupSpawnDistance)
            {
                double insertionScale = maximumPickupSpawnDistance / objectiveDistance;
                routeOrigin = new LogicalPosition(
                    ActiveObjectiveLogicalPosition.X - (objectiveDeltaX * insertionScale),
                    ActiveObjectiveLogicalPosition.Z - (objectiveDeltaZ * insertionScale));
                insertionHeight =
                    (float)_world.HeightField.SampleHeight(routeOrigin.X, routeOrigin.Z) +
                    _hubSettings.DesertInsertionHeight;
                _desertSpawn = _world.LogicalToLocal(routeOrigin.X, insertionHeight, routeOrigin.Z);
            }

            Vector3 actualPickupForward = Vector3.ProjectOnPlane(_package.position - _desertSpawn, Vector3.up);
            if (actualPickupForward.sqrMagnitude > 0.001f)
            {
                _desertRotation = Quaternion.LookRotation(actualPickupForward.normalized, Vector3.up);
            }
        }

        private void ResolveRouteLandmarks(CourierContract contract)
        {
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
            contract.RouteOrigin = routeOrigin;
            contract.RouteAngle = routeAngle;
            float pickupOffset = Mathf.Lerp(
                _settings.MinimumPickupInsertionDistance,
                _settings.MaximumPickupInsertionDistance,
                (float)random.NextDouble());
            contract.PickupPosition = new LogicalPosition(
                routeOrigin.X + (Math.Cos(routeAngle) * pickupOffset),
                routeOrigin.Z + (Math.Sin(routeAngle) * pickupOffset));
            contract.DeliveryPositions.Clear();
            LogicalPosition previous = contract.PickupPosition;
            float perLeg = contract.PlannedRouteDistance / Mathf.Max(1, contract.StopCount);
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

            contract.RoutePlacementRecords.Clear();
            HashSet<string> routePlacementIds = new HashSet<string>();
            DuneLandmarkPlacementRecord pickupLandmark = _landmarks.ResolveNearestWorldLandmark(
                contract.PickupLandmarkType, contract.PickupPosition, routePlacementIds);
            if (pickupLandmark == null)
            {
                throw new InvalidOperationException("A contract route could not resolve a pickup world landmark.");
            }
            contract.RoutePlacementRecords.Add(pickupLandmark);
            routePlacementIds.Add(pickupLandmark.PersistentId);
            LogicalPosition plannedPickupPosition = contract.PickupPosition;
            contract.PickupPosition = pickupLandmark.LogicalPosition;
            contract.PickupLandmarkType = pickupLandmark.Type;
            contract.PickupName = GetContractLocationName(pickupLandmark.Type);
            LogicalPosition plannedLegStart = plannedPickupPosition;
            LogicalPosition resolvedLegStart = contract.PickupPosition;
            float minimumLegDistance =
                _settings.EvaluateMinimumRouteDistance(contract.Difficulty) /
                Mathf.Max(1, contract.StopCount);
            float maximumLegDistance =
                _settings.EvaluateMaximumRouteDistance(contract.Difficulty) /
                Mathf.Max(1, contract.StopCount);
            for (int i = 0; i < contract.DeliveryPositions.Count; i++)
            {
                LogicalPosition plannedDeliveryPosition = contract.DeliveryPositions[i];
                LogicalPosition desiredDeliveryPosition = new LogicalPosition(
                    resolvedLegStart.X + (plannedDeliveryPosition.X - plannedLegStart.X),
                    resolvedLegStart.Z + (plannedDeliveryPosition.Z - plannedLegStart.Z));
                DuneLandmarkType deliveryType = i == contract.DeliveryPositions.Count - 1
                    ? contract.DestinationLandmarkType
                    : ChooseLandmarkType(random);
                DuneLandmarkPlacementRecord deliveryLandmark = _landmarks.ResolveNearestWorldLandmark(
                    deliveryType,
                    desiredDeliveryPosition,
                    resolvedLegStart,
                    minimumLegDistance,
                    maximumLegDistance,
                    routePlacementIds);
                if (deliveryLandmark == null)
                {
                    throw new InvalidOperationException("A contract route could not resolve a delivery world landmark.");
                }
                contract.RoutePlacementRecords.Add(deliveryLandmark);
                routePlacementIds.Add(deliveryLandmark.PersistentId);
                contract.DeliveryPositions[i] = deliveryLandmark.LogicalPosition;
                plannedLegStart = plannedDeliveryPosition;
                resolvedLegStart = deliveryLandmark.LogicalPosition;
                if (i == contract.DeliveryPositions.Count - 1)
                {
                    contract.DestinationLandmarkType = deliveryLandmark.Type;
                    contract.DestinationName = GetContractLocationName(deliveryLandmark.Type);
                }
            }

            double routeDistance = 0.0;
            LogicalPosition legStart = contract.PickupPosition;
            for (int i = 0; i < contract.DeliveryPositions.Count; i++)
            {
                LogicalPosition legEnd = contract.DeliveryPositions[i];
                double deltaX = legEnd.X - legStart.X;
                double deltaZ = legEnd.Z - legStart.Z;
                routeDistance += Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
                legStart = legEnd;
            }
            contract.RouteDistance = (float)routeDistance;
        }

        private void BuildPickupObjective()
        {
            DuneVectorLandmarkInstance landmark = _routeLandmarks[0];
            Vector3 objectivePosition = landmark.ContractSocket.position;
            LogicalPosition objectiveLogical = LocalToLogical(objectivePosition);
            double pickupGroundHeight = _world.HeightField.SampleHeight(
                objectiveLogical.X,
                objectiveLogical.Z);
            double pickupRingHeight = DuneVectorVisuals.CalculateGroundedPortalCenterHeight(
                pickupGroundHeight,
                _deliverySettings.ObjectiveRingRadius,
                _materials.RingPortalTuning);
            _package = DuneVectorVisuals.CreatePackageVisual(transform, _materials, _settings.ObjectivePackageScale);
            _package.name = $"Contract Cargo {ActiveContract.ContractId}";
            _package.position = objectivePosition;
            _objectiveRing = CreateObjectiveRing(
                "Contract Pickup Ring",
                objectiveLogical,
                pickupRingHeight,
                true,
                HandlePackagePickup);
            ActiveObjective = _package;
            ActiveObjectiveLogicalPosition = objectiveLogical;
        }

        private void ProtectContractObjectivesFromWind()
        {
            if (_windFields == null)
            {
                return;
            }

            List<WindFieldExclusion> exclusions =
                new List<WindFieldExclusion>(_routeLandmarks.Count);
            for (int i = 0; i < _routeLandmarks.Count; i++)
            {
                DuneVectorLandmarkInstance landmark = _routeLandmarks[i];
                Transform socket = i == 0 ? landmark.ContractSocket : landmark.DeliverySocket;
                LogicalPosition logical = LocalToLogical(socket.position);
                float ringRadius = i == 0
                    ? _deliverySettings.ObjectiveRingRadius
                    : _deliverySettings.DeliveryRingRadius;
                exclusions.Add(new WindFieldExclusion(
                    new Vector2((float)logical.X, (float)logical.Z),
                    ringRadius));
            }
            _windFields.SetContractObjectiveExclusions(exclusions);
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
            Func<bool> canActivate = pickup
                ? () => State == CourierRunState.FindPackage && _package != null
                : () => State == CourierRunState.Delivering && IsCarryingCargo;
            ring.Initialize(
                _player,
                _camera,
                _materials,
                _deliverySettings,
                pickup,
                Mathf.Max(1f, pickup
                    ? _deliverySettings.ObjectiveRingRadius
                    : _deliverySettings.DeliveryRingRadius),
                crossed,
                canActivate);
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
            ConfigureHighValueWorldThreats();
            ShowStatus("CARGO SECURED — PROCEED TO DESTINATION", 3f);
        }

        private void ConfigureHighValueWorldThreats()
        {
            bool highValue = ActiveContract != null && ActiveContract.Has(CourierContractModifier.HighValue);
            int riskBonusGroundEnemies = Mathf.CeilToInt(
                Mathf.Max(0f, DuneVectorContractRisk.EnemySpawnMultiplier - 1f) *
                Mathf.Max(1, _settings.RiskGroundEnemyReferenceCount));
            int highValueGroundEnemies = highValue && _routeEncounterDirector != null
                ? _routeEncounterDirector.Settings.HighValueGroundEnemyBonus
                : 0;
            int totalGroundEnemies = highValueGroundEnemies + riskBonusGroundEnemies;
            if (totalGroundEnemies > 0)
            {
                _world.SetContractGroundExploders(
                    totalGroundEnemies,
                    _routeEncounterDirector != null ? _routeEncounterDirector.Settings.HighValueGroundEnemyMinimumSpawnDistance : 0f,
                    _routeEncounterDirector != null ? _routeEncounterDirector.Settings.HighValueGroundEnemyMaximumSpawnDistance : 0f,
                    ActiveContract.Seed);
            }
            else
            {
                _world.ClearContractGroundExploders();
            }

            if (highValue)
            {
                _stormDirector?.SetContractBonusEnemies(
                    _routeEncounterDirector != null ? _routeEncounterDirector.Settings.HighValueStormPyramidBonus : 0,
                    ActiveContract.Seed);
            }
            else
            {
                _stormDirector?.ClearContractBonusEnemies();
            }
        }

        private void BuildDeliveryObjective()
        {
            if (_objectiveRing != null)
            {
                Destroy(_objectiveRing.gameObject);
            }
            DuneVectorLandmarkInstance landmark = _routeLandmarks[_deliveryIndex + 1];
            Vector3 objectivePosition = landmark.DeliverySocket.position;
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
            if (State != CourierRunState.Delivering || _deliveryCompletionInProgress)
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
            if (_deliveryCompletionInProgress || State != CourierRunState.Delivering || ActiveContract == null)
            {
                return;
            }
            _deliveryCompletionInProgress = true;
            CourierContract completed = ActiveContract;
            float integrityFactor = CargoUsesIntegrity()
                ? Mathf.Lerp(
                    _settings.IntegrityRewardFloor,
                    1f,
                    Mathf.Clamp01(CargoIntegrity / 100f))
                : 1f;
            int reward = Mathf.RoundToInt(completed.OfferedReward * integrityFactor);
            _wallet?.AddGold(reward);
            bool hasAssignedMessage = _messageSettings.TryResolve(
                Progress.NextDeliveryMessageIndex,
                out _);
            Progress.RecordCompletion(reward, completed.Difficulty, hasAssignedMessage);
            _routeEncounterDirector?.EndContract();
            _sandAmbusherSystem?.EndContract();
            DuneVectorContractRisk.Reset();
            SetCombatSystemsActive(false);
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            ReleaseDeliveredPackage();
            CleanupContractObjects();
            State = CourierRunState.DeliveryComplete;
            _stateTimer = _settings.CompletionReturnDelay;
            ShowStatus($"CONTRACT COMPLETE  +{reward} GOLD", _stateTimer);
            ContractEnded?.Invoke(completed);
            GenerateOffers();
        }

        private void ReleaseDeliveredPackage()
        {
            if (_package == null)
            {
                return;
            }

            Vector3 carrierVelocity = _player != null && _player.Motor != null
                ? _player.Motor.Velocity
                : Vector3.zero;
            DroppedDeliveryPackage.Release(_package, transform, _world, _deliverySettings, carrierVelocity);
            _package = null;
            _cargoWarning = null;
            _cargoSparks = null;
        }

        private void FailContract(string reason, bool recordFailure, bool beginReturn)
        {
            CourierContract failed = ActiveContract;
            if (recordFailure && failed != null)
            {
                Progress.RecordFailure();
            }
            RemoveContractOffer(failed);
            _routeEncounterDirector?.EndContract();
            _sandAmbusherSystem?.EndContract();
            DuneVectorContractRisk.Reset();
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

        public bool DamageFragileCargo(float amount)
        {
            if (amount <= 0f
                || State != CourierRunState.Delivering
                || ActiveContract == null
                || !ActiveContract.Has(CourierContractModifier.Fragile))
            {
                return false;
            }

            DamageCargo(amount);
            return true;
        }

        private void HandlePlayerDied()
        {
            if (!IsContractActive || ActiveContract == null)
            {
                return;
            }
            CourierContract failed = ActiveContract;
            Progress.RecordFailure();
            RemoveContractOffer(failed);
            _routeEncounterDirector?.EndContract();
            _sandAmbusherSystem?.EndContract();
            DuneVectorContractRisk.Reset();
            _player.SetCargoHandlingModifiers(1f, 1f, 1f);
            CleanupContractObjects();
            State = CourierRunState.ContractFailed;
            _stateTimer = float.PositiveInfinity;
            ContractEnded?.Invoke(failed);
        }

        private void RemoveContractOffer(CourierContract contract)
        {
            if (contract == null)
            {
                return;
            }

            _offers.RemoveAll(offer =>
                ReferenceEquals(offer, contract) ||
                (offer != null && offer.ContractId == contract.ContractId));
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
            if (toHub)
            {
                SetCombatSystemsActive(false);
            }
            State = toHub ? CourierRunState.ReturnToBase : CourierRunState.TeleportingToDesert;
            _teleportTimer = 0f;
            _teleportMoved = false;
            _returnStartsVanished = false;
            _playerInput.SetInputEnabled(false);
            BeginDeliveryMessageSafety();
            CreateTeleportParticles();
        }

        private void BeginDeliveryTeleportOut()
        {
            if (State != CourierRunState.DeliveryComplete)
            {
                return;
            }

            SetTerminalOpen(false);
            State = CourierRunState.TeleportOut;
            _teleportTimer = 0f;
            _teleportMoved = false;
            _returnStartsVanished = false;
            _playerInput.SetInputEnabled(false);
            CreateTeleportParticles();
        }

        private void UpdateDeliveryTeleportOut()
        {
            float build = _hubSettings.TeleportBuildDuration;
            float fade = _hubSettings.TeleportFadeDuration;
            float vanishAt = build + (fade * 0.5f);
            _teleportTimer += Time.deltaTime;
            _player.Motor.BaseVelocity = Vector3.Lerp(
                _player.Motor.BaseVelocity,
                Vector3.zero,
                DuneVectorMath.Sharpness(_hubSettings.StabilizeSharpness, Time.deltaTime));

            float visualScale = 1f - Mathf.Clamp01(
                (_teleportTimer - build) / Mathf.Max(0.01f, fade * 0.5f));
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _droneVisualOriginalScale * visualScale;
            }
            AnimateTeleportParticles(vanishAt);

            if (_teleportTimer < vanishAt)
            {
                return;
            }

            DestroyTeleportParticles();
            State = CourierRunState.DeliveryMessage;
            int pendingIndex = Progress.PendingDeliveryMessageIndex;
            if (pendingIndex < 0)
            {
                BeginReturnToBaseAfterMessage();
                return;
            }

            if (!_messageSettings.TryResolve(pendingIndex, out DeliveryMessageAsset message) ||
                !_messagePresenter.Open(message, HandleDeliveryMessageCompleted))
            {
                Debug.LogError(
                    $"Pending delivery message {pendingIndex} is unavailable. Its progression index remains pending.",
                    this);
                BeginReturnToBaseAfterMessage();
            }
        }

        private void HandleDeliveryMessageCompleted()
        {
            if (State != CourierRunState.DeliveryMessage)
            {
                return;
            }

            int completedMessageIndex = Progress.PendingDeliveryMessageIndex;
            if (completedMessageIndex >= 0)
            {
                Progress.CompletePendingDeliveryMessage(completedMessageIndex);
            }

            BeginReturnToBaseAfterMessage();
        }

        private void BeginReturnToBaseAfterMessage()
        {
            if (State != CourierRunState.DeliveryMessage)
            {
                return;
            }

            State = CourierRunState.ReturnToBase;
            _teleportTimer = 0f;
            _teleportMoved = true;
            _returnStartsVanished = true;
            _playerInput.SetInputEnabled(false);
            _player.Motor.SetPositionAndRotation(_hubSpawn, Quaternion.identity, true);
            _health.SetDamageImmune(true);
            _player.ResetTraversalAfterTeleport(Vector3.forward);
            _cameraController?.SnapToTarget(Vector3.forward);
            CreateTeleportParticles();
            RecenterTeleportParticles(_hubSpawn);
        }

        private void UpdateTeleport()
        {
            bool toHub = State == CourierRunState.ReturnToBase;
            float build = _hubSettings.TeleportBuildDuration;
            float fade = _hubSettings.TeleportFadeDuration;
            float rebuild = _hubSettings.TeleportRebuildDuration;
            if (toHub && _returnStartsVanished)
            {
                _teleportTimer += Time.deltaTime;
                float rebuildScale = Mathf.Clamp01(_teleportTimer / Mathf.Max(0.01f, rebuild));
                if (_player.DroneVisualRoot != null)
                {
                    _player.DroneVisualRoot.localScale = _droneVisualOriginalScale * rebuildScale;
                }
                AnimateTeleportParticles(rebuild);
                if (_teleportTimer < rebuild)
                {
                    return;
                }

                FinishReturnToHub();
                return;
            }

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
                _health.SetDamageImmune(toHub);
                _player.ResetTraversalAfterTeleport(rotation * Vector3.forward);
                _cameraController?.SnapToTarget(rotation * Vector3.forward);
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
                FinishReturnToHub();
            }
            else
            {
                bool isFreeRoam = ActiveContract == null;
                State = isFreeRoam ? CourierRunState.FreeRoam : CourierRunState.FindPackage;
                int risk = ActiveContract != null ? ActiveContract.Difficulty : 1;
                DuneVectorContractRisk.Configure(_settings, risk);
                _sandAmbusherSystem?.BeginContract(risk, ActiveContract != null ? ActiveContract.Seed : 0);
                EndDeliveryMessageSafety();
                SetCombatSystemsActive(true);
                _playerInput.SetInputEnabled(true);
                ShowStatus(
                    isFreeRoam
                        ? "FREE ROAM DEPLOYED"
                        : risk >= Mathf.Max(1, _settings.SandAmbusherMinimumRisk)
                        ? $"RISK {risk} // SAND AMBUSHERS ACTIVE"
                        : "CONTRACT DEPLOYED — LOCATE CARGO",
                    3f);
            }
        }

        private void FinishReturnToHub()
        {
            DestroyTeleportParticles();
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _droneVisualOriginalScale;
            }
            _returnStartsVanished = false;
            _deliveryCompletionInProgress = false;
            EnterHubImmediate(openTerminal: false, placePlayerAtSpawn: false);
            EndDeliveryMessageSafety();
            ShowStatus("RETURNED TO COURIER AERIE", 2.5f);
        }

        private void BeginDeliveryMessageSafety()
        {
            if (_deliveryMessageSafetyActive || _health == null)
            {
                return;
            }

            _deliveryMessageSafetyActive = true;
            _infiniteHealthBeforeDeliveryMessage = _health.HasInfiniteHealth;
            _health.SetInfiniteHealth(true);
            _environmentalHazards?.SetGameplayActive(false);
        }

        private void EndDeliveryMessageSafety()
        {
            if (!_deliveryMessageSafetyActive || _health == null)
            {
                return;
            }

            _health.SetInfiniteHealth(_infiniteHealthBeforeDeliveryMessage);
            _deliveryMessageSafetyActive = false;
        }

        private void SetCombatSystemsActive(bool active)
        {
            _enemyDirector?.SetGameplayActive(active);
            _stormDirector?.SetGameplayActive(active);
            _vesperKiteDirector?.SetGameplayActive(active);
            _environmentalHazards?.SetGameplayActive(active);
            if (_routeEncounterDirector != null) _routeEncounterDirector.enabled = active;
        }

        private void SetTerminalOpen(bool open)
        {
            SetHubTerminalMode(open ? HubTerminalMode.Contracts : HubTerminalMode.None);
        }

        private void SetHubTerminalMode(HubTerminalMode mode)
        {
            _hubTerminalMode = mode;
            if (mode != HubTerminalMode.MessageArchive)
            {
                _archiveScrollPosition = Vector2.zero;
            }
            if (State != CourierRunState.Hub)
            {
                return;
            }
            bool open = mode != HubTerminalMode.None;
            _playerInput.SetInputEnabled(!open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        private void CleanupContractObjects()
        {
            _windFields?.SetContractObjectiveExclusions(null);
            _world?.ClearContractGroundExploders();
            _stormDirector?.ClearContractBonusEnemies();
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
            if ((State == CourierRunState.TeleportingToDesert ||
                 State == CourierRunState.FindPackage) &&
                _package != null)
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
            Font archiveFont = _messagePresenter != null ? _messagePresenter.PresentationFont : _messageSettings.NarrativeFont;
            _archiveTitleStyle = MessageArchiveStyle(
                archiveFont,
                _messageSettings.ArchiveTitleFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                _messageSettings.PageStartTextColor);
            _archiveEntryStyle = MessageArchiveStyle(
                archiveFont,
                _messageSettings.ArchiveEntryFontSize,
                FontStyle.Normal,
                TextAnchor.UpperCenter,
                _messageSettings.NarrativeTextColor);
            _archiveMetaStyle = MessageArchiveStyle(
                archiveFont,
                _messageSettings.ArchiveMetaFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleRight,
                _messageSettings.SecondaryTextColor);
            _archiveEmptyStyle = MessageArchiveStyle(
                archiveFont,
                _messageSettings.ArchiveEmptyFontSize,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                _messageSettings.SecondaryTextColor);
            _archiveTileTexture = SolidTexture(_messageSettings.ArchiveTileColor, "Message Archive Tile");
            _archiveTileHoverTexture = SolidTexture(_messageSettings.ArchiveTileHoverColor, "Message Archive Tile Hover");
            _archiveTileButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _archiveTileButtonStyle.normal.background = _archiveTileTexture;
            _archiveTileButtonStyle.hover.background = _archiveTileHoverTexture;
            _archiveTileButtonStyle.active.background = _archiveTileHoverTexture;
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
            _terminalButtonStyle.normal.textColor = GuiTextColor(_hubSettings.TerminalTextColor);
            _terminalButtonStyle.hover.textColor = GuiTextColor(_hubSettings.TerminalTextColor);
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
                normal = { textColor = GuiTextColor(color) },
            };
        }

        private static Color GuiTextColor(Color color)
        {
#if UNITY_EDITOR
            return color;
#else
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.gamma : color;
#endif
        }

        private static GUIStyle MessageArchiveStyle(
            Font font,
            int size,
            FontStyle fontStyle,
            TextAnchor anchor,
            Color color)
        {
            GUIStyle style = LabelStyle(size, fontStyle, anchor, color);
            if (font != null)
            {
                style.font = font;
            }
            return style;
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
            if (_hubTerminalMode == HubTerminalMode.Contracts && State == CourierRunState.Hub)
            {
                DrawContractTerminal();
            }
            else if (_hubTerminalMode == HubTerminalMode.MessageArchive &&
                State == CourierRunState.Hub &&
                (_messagePresenter == null || !_messagePresenter.IsOpen))
            {
                DrawMessageArchiveTerminal();
            }
            else if (State == CourierRunState.Hub && !IsGameplayHudSuppressed)
            {
                if (_messagePresenter == null || !_messagePresenter.IsOpen)
                {
                    DrawHubHUD();
                }
            }
            else if (IsContractActive && !IsGameplayHudSuppressed)
            {
                DrawContractHUD();
                if (_environmentalHazards == null || !_environmentalHazards.IsElectricalInterferenceActive)
                {
                    DrawObjectiveMarker();
                }
            }
            if (State == CourierRunState.TeleportingToDesert ||
                State == CourierRunState.TeleportOut ||
                State == CourierRunState.ReturnToBase)
            {
                DrawTeleportFade();
            }
            if (!IsTerminalOpen && !IsGameplayHudSuppressed && Time.unscaledTime < _statusMessageUntil)
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
            _terminalActionStyle.normal.textColor = GuiTextColor(_hubSettings.TerminalAccentColor);
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
            int riskPipsPerRow = Mathf.Max(1, _hubSettings.TerminalRiskPipsPerRow);
            bool showSecondRiskRow = false;
            for (int i = 0; i < _offers.Count; i++)
            {
                if (_offers[i].Difficulty > riskPipsPerRow)
                {
                    showSecondRiskRow = true;
                    break;
                }
            }
            CourierContract selectedOffer = null;
            string hoveredContractTypeTooltip = null;
            Vector2 virtualMousePosition = Event.current.mousePosition;
            for (int i = 0; i < _offers.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect card = new Rect(panel.x + padding + (column * (cardWidth + gap)), cardsTop + (row * (cardHeight + gap)), cardWidth, cardHeight);
                CourierContract offer = _offers[i];
                if (DrawContractCard(
                    card,
                    offer,
                    showSecondRiskRow,
                    virtualMousePosition,
                    out string contractTypeTooltip))
                {
                    selectedOffer = offer;
                }
                if (!string.IsNullOrEmpty(contractTypeTooltip))
                {
                    hoveredContractTypeTooltip = contractTypeTooltip;
                }
            }

            DrawSolidRect(
                new Rect(panel.x + padding, panel.yMax - _hubSettings.TerminalFooterHeight, contentWidth, 1f),
                _hubSettings.TerminalDividerColor);
            GUI.Label(
                new Rect(
                    panel.x + padding,
                    panel.yMax - _hubSettings.TerminalFooterHeight + 7f,
                    contentWidth * 0.5f,
                    22f),
                "SELECT A CONTRACT TO DEPLOY",
                _terminalMetaStyle);
            _terminalActionStyle.normal.textColor = GuiTextColor(_hubSettings.TerminalAccentColor);
            GUI.Label(
                new Rect(
                    panel.center.x,
                    panel.yMax - _hubSettings.TerminalFooterHeight + 7f,
                    contentWidth * 0.5f,
                    22f),
                "CONTRACTS REFRESH AUTOMATICALLY",
                _terminalActionStyle);
            DrawContractTypeTooltip(panel, virtualWidth, virtualHeight, hoveredContractTypeTooltip);

            GUI.matrix = previousMatrix;
            GUI.backgroundColor = previousBackground;
            if (selectedOffer != null)
            {
                AcceptContract(selectedOffer);
            }
        }

        private void DrawMessageArchiveTerminal()
        {
            GUI.depth = -1150;
            Matrix4x4 previousMatrix = GUI.matrix;
            float minimumScale = Mathf.Min(_messageSettings.MinimumScale, _messageSettings.MaximumScale);
            float maximumScale = Mathf.Max(_messageSettings.MinimumScale, _messageSettings.MaximumScale);
            float scale = Mathf.Clamp(
                Mathf.Min(
                    Screen.width / Mathf.Max(1f, _messageSettings.ReferenceWidth),
                    Screen.height / Mathf.Max(1f, _messageSettings.ReferenceHeight)),
                minimumScale,
                maximumScale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float virtualWidth = Screen.width / scale;
            float virtualHeight = Screen.height / scale;
            float panelWidth = Mathf.Min(
                _messageSettings.ArchivePanelWidth,
                virtualWidth - (_messageSettings.ScreenMargin * 2f));
            float panelHeight = Mathf.Min(
                _messageSettings.ArchivePanelHeight,
                virtualHeight - (_messageSettings.ScreenMargin * 2f));
            Rect panel = new Rect(
                (virtualWidth - panelWidth) * 0.5f,
                ((virtualHeight - panelHeight) * 0.5f) + _messageSettings.ArchivePanelVerticalOffset,
                panelWidth,
                panelHeight);

            _messagePresenter.DrawArchiveChrome(virtualWidth, virtualHeight, panel);
            float padding = _messageSettings.ArchivePadding;
            float contentWidth = Mathf.Max(1f, panel.width - (padding * 2f));
            GUI.Label(
                new Rect(
                    panel.x + padding,
                    panel.y + _messageSettings.RuleOffset,
                    contentWidth,
                    Mathf.Max(1f, _messageSettings.ArchiveHeaderHeight - _messageSettings.RuleOffset)),
                _messageSettings.ArchiveTitle ?? string.Empty,
                _archiveTitleStyle);

            float listTop = panel.y + _messageSettings.ArchiveHeaderHeight;
            float listBottom = panel.yMax - _messageSettings.ArchiveFooterHeight;
            Rect listViewport = new Rect(
                panel.x + padding,
                listTop,
                contentWidth,
                Mathf.Max(1f, listBottom - listTop));
            int archivedCount = GetArchivedMessageCount();
            if (archivedCount == 0)
            {
                GUI.Label(listViewport, _messageSettings.ArchiveEmptyState ?? string.Empty, _archiveEmptyStyle);
            }
            else
            {
                int columns = Mathf.Max(1, _messageSettings.ArchiveGridColumns);
                int rowCount = Mathf.CeilToInt(archivedCount / (float)columns);
                float tileHeight = _messageSettings.ArchiveTileHeight;
                float tileGap = _messageSettings.ArchiveTileGap;
                float contentHeight = Mathf.Max(
                    listViewport.height,
                    (rowCount * tileHeight) + (Mathf.Max(0, rowCount - 1) * tileGap));
                bool needsVerticalScrollbar = contentHeight > listViewport.height;
                float gridWidth = Mathf.Max(
                    1f,
                    listViewport.width -
                    _messageSettings.ArchiveContentRightPadding -
                    (needsVerticalScrollbar ? _messageSettings.ArchiveScrollbarReserve : 0f));
                float tileWidth = Mathf.Max(
                    1f,
                    (gridWidth - (Mathf.Max(0, columns - 1) * tileGap)) / columns);
                Rect scrollContent = new Rect(0f, 0f, gridWidth, contentHeight);
                _archiveScrollPosition = GUI.BeginScrollView(
                    listViewport,
                    _archiveScrollPosition,
                    scrollContent,
                    false,
                    needsVerticalScrollbar);
                int displayedIndex = 0;
                int completedExclusive = Mathf.Max(0, Progress.NextDeliveryMessageIndex);
                int sequenceCount = _messageSettings.Sequence != null ? _messageSettings.Sequence.Count : 0;
                for (int sequenceIndex = 0; sequenceIndex < completedExclusive && sequenceIndex < sequenceCount; sequenceIndex++)
                {
                    DeliveryMessageAsset message = _messageSettings.Sequence[sequenceIndex];
                    if (message == null)
                    {
                        continue;
                    }

                    int column = displayedIndex % columns;
                    int row = displayedIndex / columns;
                    Rect tile = new Rect(
                        column * (tileWidth + tileGap),
                        row * (tileHeight + tileGap),
                        tileWidth,
                        tileHeight);
                    bool clicked = GUI.Button(tile, GUIContent.none, _archiveTileButtonStyle);
                    float iconSize = Mathf.Min(
                        _messageSettings.ArchiveIconSize,
                        Mathf.Min(tile.width, tile.height));
                    Rect icon = new Rect(
                        tile.x + ((tile.width - iconSize) * 0.5f),
                        tile.y + _messageSettings.ArchiveIconTopPadding,
                        iconSize,
                        iconSize);
                    DrawArchiveTransmissionIcon(icon);
                    string entryLabel = FormatArchiveEntryLabel(sequenceIndex + 1);
                    GUI.Label(
                        new Rect(
                            tile.x,
                            icon.yMax + _messageSettings.ArchiveLabelTopGap,
                            tile.width,
                            _messageSettings.ArchiveLabelHeight),
                        entryLabel,
                        _archiveEntryStyle);
                    if (clicked)
                    {
                        OpenArchivedMessage(message);
                    }
                    displayedIndex++;
                }
                GUI.EndScrollView();
            }

            GUI.Label(
                new Rect(
                    panel.x + padding,
                    panel.yMax - _messageSettings.ArchiveFooterHeight,
                    contentWidth,
                    _messageSettings.ArchiveFooterHeight),
                _messageSettings.ArchiveFooter ?? string.Empty,
                _archiveEmptyStyle);
            GUI.matrix = previousMatrix;
        }

        private void DrawArchiveTransmissionIcon(Rect icon)
        {
            DrawSolidRect(icon, _messageSettings.ArchiveIconColor);
            DrawBorder(
                icon,
                _messageSettings.ArchiveIconDetailColor,
                _messageSettings.ArchiveIconBorderThickness);
            float inset = Mathf.Min(_messageSettings.ArchiveIconInset, icon.width * 0.5f);
            float detailWidth = Mathf.Max(0f, icon.width - (inset * 2f));
            float headerY = icon.y + inset;
            DrawSolidRect(
                new Rect(icon.x + inset, headerY, detailWidth, _messageSettings.ArchiveIconHeaderHeight),
                _messageSettings.ArchiveIconDetailColor);
            float lineY = headerY + _messageSettings.ArchiveIconHeaderHeight + _messageSettings.ArchiveIconLineGap;
            for (int line = 0; line < _messageSettings.ArchiveIconLineCount; line++)
            {
                if (lineY + _messageSettings.ArchiveIconLineThickness > icon.yMax - inset)
                {
                    break;
                }
                DrawSolidRect(
                    new Rect(
                        icon.x + inset,
                        lineY,
                        detailWidth,
                        _messageSettings.ArchiveIconLineThickness),
                    _messageSettings.ArchiveIconDetailColor);
                lineY += _messageSettings.ArchiveIconLineThickness + _messageSettings.ArchiveIconLineGap;
            }
        }

        private int GetArchivedMessageCount()
        {
            if (Progress == null || _messageSettings == null || _messageSettings.Sequence == null)
            {
                return 0;
            }
            int completedExclusive = Mathf.Min(
                Mathf.Max(0, Progress.NextDeliveryMessageIndex),
                _messageSettings.Sequence.Count);
            int count = 0;
            for (int index = 0; index < completedExclusive; index++)
            {
                if (_messageSettings.Sequence[index] != null)
                {
                    count++;
                }
            }
            return count;
        }

        private string FormatArchiveEntryLabel(int displayIndex)
        {
            return FormatDesignerText(_messageSettings.ArchiveEntryFormat, displayIndex);
        }

        private void OpenArchivedMessage(DeliveryMessageAsset message)
        {
            if (_hubTerminalMode != HubTerminalMode.MessageArchive || message == null ||
                _messagePresenter == null || _messagePresenter.IsOpen)
            {
                return;
            }
            _messagePresenter.OpenReplay(message, HandleArchivedMessageClosed);
        }

        private void HandleArchivedMessageClosed()
        {
            _archiveReplayClosedFrame = Time.frameCount;
        }

        private bool DrawContractCard(
            Rect card,
            CourierContract offer,
            bool showSecondRiskRow,
            Vector2 mousePosition,
            out string contractTypeTooltip)
        {
            contractTypeTooltip = null;
            bool accepted = GUI.Button(card, GUIContent.none, _terminalButtonStyle);
            Color modifierColor = GetContractModifierColor(offer.DisplayModifiers);
            DrawSolidRect(
                new Rect(card.x, card.y, _hubSettings.TerminalCardAccentWidth, card.height),
                modifierColor);

            float left = card.x + _hubSettings.TerminalCardAccentWidth + 16f;
            float right = card.xMax - 16f;
            float contentWidth = right - left;
            _terminalKickerStyle.normal.textColor = GuiTextColor(modifierColor);
            Rect modifierLabel = new Rect(left, card.y + 12f, contentWidth, 20f);
            GUI.Label(modifierLabel, offer.DisplayModifierText, _terminalKickerStyle);
            if (modifierLabel.Contains(mousePosition))
            {
                contractTypeTooltip = GetContractTypeTooltip(offer.DisplayModifiers);
            }

            int maximumRisk = Mathf.Max(1, _settings.MaximumRisk);
            int filledRiskPips = Mathf.Clamp(offer.Difficulty, 0, maximumRisk);
            int pipsPerRow = Mathf.Clamp(_hubSettings.TerminalRiskPipsPerRow, 1, maximumRisk);
            float pipSize = _hubSettings.TerminalContractOrderPipSize;
            float pipGap = _hubSettings.TerminalContractOrderPipGap;
            int displayedPipCount = showSecondRiskRow ? maximumRisk : Mathf.Min(maximumRisk, pipsPerRow);
            for (int row = 0, pipIndex = 0; pipIndex < displayedPipCount && row < 2; row++)
            {
                int rowPipCount = Mathf.Min(pipsPerRow, displayedPipCount - pipIndex);
                float pipStart = right - ((pipSize * rowPipCount) + (pipGap * (rowPipCount - 1)));
                float pipY = card.y + 19f + (row * (pipSize + _hubSettings.TerminalRiskPipRowGap));
                for (int column = 0; column < rowPipCount; column++, pipIndex++)
                {
                    DrawSolidRect(
                        new Rect(pipStart + (column * (pipSize + pipGap)), pipY, pipSize, pipSize),
                        pipIndex < filledRiskPips
                            ? _hubSettings.TerminalAccentColor
                            : _hubSettings.TerminalDividerColor);
                }
            }

            GUI.Label(new Rect(left, card.y + 40f, contentWidth, 27f), offer.PickupName, _terminalDestinationStyle);
            GUI.Label(
                new Rect(left, card.y + 70f, contentWidth, 21f),
                $"ROUTE  {offer.RouteDistance / 1000f:0.0} KM      RISK  {offer.Difficulty:00}",
                _terminalMetaStyle);
            DrawSolidRect(
                new Rect(left, card.y + 101f, contentWidth, 1f),
                _hubSettings.TerminalDividerColor);

            _terminalKickerStyle.normal.textColor = GuiTextColor(_hubSettings.TerminalMutedTextColor);
            GUI.Label(new Rect(left, card.y + 111f, contentWidth, 18f), "CONTRACT PAYOUT", _terminalKickerStyle);
            GUI.Label(new Rect(left, card.y + 130f, contentWidth, 28f), $"{offer.OfferedReward:N0} GOLD", _terminalRewardStyle);

            string details = offer.TimeLimit > 0f ? $"EXPRESS {FormatTime(offer.TimeLimit)}" : "OPEN WINDOW";
            if (offer.StopCount > 1) details += $"   /   {offer.StopCount} STOPS";
            GUI.Label(new Rect(left, card.yMax - 29f, contentWidth * 0.72f, 20f), details, _terminalMetaStyle);
            _terminalActionStyle.normal.textColor = GuiTextColor(modifierColor);
            GUI.Label(new Rect(left, card.yMax - 29f, contentWidth, 20f), "SELECT", _terminalActionStyle);
            return accepted;
        }

        private void DrawContractTypeTooltip(
            Rect panel,
            float virtualWidth,
            float virtualHeight,
            string tooltip)
        {
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
            Vector2 mouse = Event.current.mousePosition;
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
            if (!TryGetNearestHubTerminal(
                out HubTerminalMode mode,
                out _,
                out float distance,
                out float interactionRadius))
            {
                return;
            }
            string terminalName;
            switch (mode)
            {
                case HubTerminalMode.MessageArchive:
                    terminalName = _hubSettings.ArchiveTerminalName;
                    break;
                case HubTerminalMode.FreeRoam:
                    terminalName = _hubSettings.FreeRoamTerminalName;
                    break;
                default:
                    terminalName = _hubSettings.ContractTerminalName;
                    break;
            }
            string prompt = distance <= interactionRadius
                ? mode == HubTerminalMode.FreeRoam
                    ? _hubSettings.FreeRoamTerminalNearbyPrompt
                    : FormatDesignerText(_hubSettings.TerminalNearbyPromptFormat, terminalName)
                : FormatDesignerText(_hubSettings.TerminalDistancePromptFormat, terminalName, distance);
            float promptWidth = Mathf.Min(_hubSettings.TerminalPromptWidth, Screen.width);
            float promptHeight = _hubSettings.TerminalPromptHeight;
            Rect promptRect = new Rect(
                (Screen.width - promptWidth) * 0.5f,
                (Screen.height * 0.5f) + _hubSettings.TerminalPromptVerticalOffset - (promptHeight * 0.5f),
                promptWidth,
                promptHeight);
            GUI.Box(promptRect, GUIContent.none);
            GUI.Label(promptRect, prompt, _objectiveStyle);
            GUI.Label(new Rect(24f, 24f, 360f, 86f),
                $"COURIER AERIE\nDELIVERIES  {Progress.CompletedDeliveries}\nCONTRACT GOLD  {Progress.TotalContractGold:N0}", _hudBodyStyle);
        }

        private bool TryGetNearestHubTerminal(
            out HubTerminalMode mode,
            out Transform terminal,
            out float distance,
            out float interactionRadius)
        {
            mode = HubTerminalMode.None;
            terminal = null;
            distance = float.PositiveInfinity;
            interactionRadius = 0f;
            if (_player == null)
            {
                return false;
            }

            float contractDistance = _terminal != null
                ? Vector3.Distance(_player.WorldCenter, _terminal.position)
                : float.PositiveInfinity;
            float archiveDistance = _messageArchiveTerminal != null
                ? Vector3.Distance(_player.WorldCenter, _messageArchiveTerminal.position)
                : float.PositiveInfinity;
            float freeRoamDistance = _freeRoamTerminal != null
                ? Vector3.Distance(_player.WorldCenter, _freeRoamTerminal.position)
                : float.PositiveInfinity;
            if (contractDistance <= archiveDistance && contractDistance <= freeRoamDistance && _terminal != null)
            {
                mode = HubTerminalMode.Contracts;
                terminal = _terminal;
                distance = contractDistance;
                interactionRadius = _hubSettings.TerminalInteractionRadius;
                return true;
            }
            if (archiveDistance <= freeRoamDistance && _messageArchiveTerminal != null)
            {
                mode = HubTerminalMode.MessageArchive;
                terminal = _messageArchiveTerminal;
                distance = archiveDistance;
                interactionRadius = _hubSettings.ArchiveTerminalInteractionRadius;
                return true;
            }
            if (_freeRoamTerminal != null)
            {
                mode = HubTerminalMode.FreeRoam;
                terminal = _freeRoamTerminal;
                distance = freeRoamDistance;
                interactionRadius = _hubSettings.FreeRoamTerminalInteractionRadius;
                return true;
            }
            return false;
        }

        private static string FormatDesignerText(string format, params object[] arguments)
        {
            string safeFormat = format ?? string.Empty;
            try
            {
                return string.Format(safeFormat, arguments);
            }
            catch (FormatException)
            {
                return safeFormat;
            }
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

        public bool TryGetVisibleContractPanelRect(out Rect panel)
        {
            if (_settings != null && IsContractActive && !IsGameplayHudSuppressed)
            {
                panel = new Rect(_settings.HudLeft, _settings.HudTop, _settings.HudWidth, _settings.HudHeight);
                return true;
            }
            panel = default;
            return false;
        }

        private void DrawObjectiveMarker()
        {
            if (ActiveObjective == null || _camera == null)
            {
                return;
            }
            float distance = Vector3.Distance(_player.WorldCenter, ActiveObjective.position);
            string objectiveLabel = State == CourierRunState.FindPackage ? "PICKUP" : "DELIVER";
            _objectiveIndicator.Draw(
                _camera,
                ActiveObjective,
                objectiveLabel,
                distance,
                _deliverySettings);
        }

        private void DrawTeleportFade()
        {
            float build = _hubSettings.TeleportBuildDuration;
            float fade = _hubSettings.TeleportFadeDuration;
            float alpha = _returnStartsVanished
                ? 1f - Mathf.Clamp01(_teleportTimer / Mathf.Max(0.01f, _hubSettings.TeleportRebuildDuration))
                : _teleportTimer < build
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

        private string GetContractLocationName(DuneLandmarkType type)
        {
            LandmarkContractLocation[] locations = _settings.LandmarkLocations;
            if (locations != null)
            {
                for (int i = 0; i < locations.Length; i++)
                {
                    LandmarkContractLocation location = locations[i];
                    if (location != null && location.Type == type && !string.IsNullOrWhiteSpace(location.DisplayName))
                    {
                        return location.DisplayName;
                    }
                }
            }
            return type.ToString().ToUpperInvariant();
        }

        private DuneLandmarkType ChooseLandmarkType(System.Random random)
        {
            DuneLandmarkType[] types = _settings.ContractLandmarkTypes;
            if (types != null && types.Length > 0)
            {
                return types[random.Next(0, types.Length)];
            }
            return (DuneLandmarkType)random.Next(0, (int)DuneLandmarkType.SandRing + 1);
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
            SetHubMaterialColors(material, baseColor, emission);
            return material;
        }

        private static void SetHubMaterialColors(Material material, Color baseColor, Color emission)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
                material.EnableKeyword("_EMISSION");
            }
        }

        private void OnDestroy()
        {
            DuneVectorContractRisk.Reset();
            if (_health != null) _health.Damaged -= HandlePlayerDamaged;
            if (_health != null) _health.Died -= HandlePlayerDied;
            if (_world != null) _world.WorldShifted -= HandleWorldShift;
            DestroyTeleportParticles();
            if (_hubMetalMaterial != null) Destroy(_hubMetalMaterial);
            DestroyHubTerminalMaterials(_hubTerminalPanelMaterials);
            DestroyHubTerminalMaterials(_hubTerminalAntennaMaterials);
            if (_hubEnergyMaterial != null) Destroy(_hubEnergyMaterial);
            if (_hubPlatformEnergyMaterial != null) Destroy(_hubPlatformEnergyMaterial);
            if (_terminalPanelTexture != null) Destroy(_terminalPanelTexture);
            if (_terminalCardTexture != null) Destroy(_terminalCardTexture);
            if (_terminalCardHoverTexture != null) Destroy(_terminalCardHoverTexture);
            if (_archiveTileTexture != null) Destroy(_archiveTileTexture);
            if (_archiveTileHoverTexture != null) Destroy(_archiveTileHoverTexture);
        }

        private void DestroyHubTerminalMaterials(List<Material> materials)
        {
            for (int index = 0; index < materials.Count; index++)
            {
                if (materials[index] != null) Destroy(materials[index]);
            }
            materials.Clear();
        }
    }
}
