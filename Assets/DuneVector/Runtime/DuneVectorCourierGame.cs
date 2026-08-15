using System;
using System.Collections;
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
            public int Version = 7;
            public int CompletedDeliveries;
            public int FailedDeliveries;
            public int TotalContractGold;
            public int HighestDifficulty;
            public int FreeRoamDeliveries;
            public int TotalFreeRoamGold;
            public int HighestFreeRoamStreak;
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
        public int FreeRoamDeliveries { get; private set; }
        public int TotalFreeRoamGold { get; private set; }
        public int HighestFreeRoamStreak { get; private set; }
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
            if (!DuneTrainingRuntime.Enabled)
            {
                Load();
            }
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

        public void RecordFreeRoamDelivery(int reward, int streak)
        {
            FreeRoamDeliveries++;
            TotalFreeRoamGold = TotalFreeRoamGold > int.MaxValue - Mathf.Max(0, reward)
                ? int.MaxValue
                : TotalFreeRoamGold + Mathf.Max(0, reward);
            HighestFreeRoamStreak = Mathf.Max(HighestFreeRoamStreak, streak);
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
            if (DuneTrainingRuntime.Enabled || string.IsNullOrEmpty(contractId) ||
                _acceptedContractIds.Contains(contractId))
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

        public void ResetTrainingEpisodeProgress()
        {
            if (!DuneTrainingRuntime.Enabled)
            {
                return;
            }

            CompletedDeliveries = 0;
            FailedDeliveries = 0;
            TotalContractGold = 0;
            HighestDifficulty = 0;
            FreeRoamDeliveries = 0;
            TotalFreeRoamGold = 0;
            HighestFreeRoamStreak = 0;
            NextDeliveryMessageIndex = 0;
            PendingDeliveryMessageIndex = -1;
            DeliveryMessageInputHintAcknowledged = false;
            StrikeOrbDeathNoteAcknowledged = false;
            VesperPilgrimDeathNoteAcknowledged = false;
            _acceptedContractIds.Clear();
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
                if (data.Version >= 7)
                {
                    FreeRoamDeliveries = Mathf.Max(0, data.FreeRoamDeliveries);
                    TotalFreeRoamGold = Mathf.Max(0, data.TotalFreeRoamGold);
                    HighestFreeRoamStreak = Mathf.Max(0, data.HighestFreeRoamStreak);
                }
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
            if (DuneTrainingRuntime.Enabled)
            {
                return;
            }
            try
            {
                SaveData data = new SaveData
                {
                    CompletedDeliveries = CompletedDeliveries,
                    FailedDeliveries = FailedDeliveries,
                    TotalContractGold = TotalContractGold,
                    HighestDifficulty = HighestDifficulty,
                    FreeRoamDeliveries = FreeRoamDeliveries,
                    TotalFreeRoamGold = TotalFreeRoamGold,
                    HighestFreeRoamStreak = HighestFreeRoamStreak,
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
        private enum HubTerminalMode
        {
            None,
            Contracts,
            MessageArchive,
            FreeRoam,
        }

        public CourierRunState State { get; private set; }
        public bool AllowsPlayerCombatTargeting =>
            State == CourierRunState.FreeRoam ||
            State == CourierRunState.FindPackage ||
            State == CourierRunState.Delivering;
        public CourierContract ActiveContract { get; private set; }
        public Transform ActiveObjective { get; private set; }
        public LogicalPosition ActiveObjectiveLogicalPosition { get; private set; }
        public bool IsContractActive => State == CourierRunState.FindPackage || State == CourierRunState.Delivering;
        public bool IsCarryingCargo =>
            State == CourierRunState.Delivering ||
            (_freeRoamDeliveries != null && _freeRoamDeliveries.IsCarryingCargo);
        public DuneVectorFreeRoamDeliverySystem FreeRoamDeliveries => _freeRoamDeliveries;
        public bool IsTerminalOpen => _hubTerminalMode != HubTerminalMode.None;
        public bool IsDeliveryMessageOpen => _messagePresenter != null && _messagePresenter.IsOpen;
        public Vector3 HubSpawnPosition => _hubSpawn;
        public Vector3 HubFloorPosition => _hubSpawn - (Vector3.up * _hubSettings.PlayerSpawnHeight);
        public Transform ContractTerminal => _terminal;
        public Transform MessageArchiveTerminal => _messageArchiveTerminal;
        public Transform FreeRoamTerminal => _freeRoamTerminal;
        public DuneVectorDesertAtlas DesertAtlas { get; private set; }
        public int ArchivedMessageCount => GetArchivedMessageCount();
        public int HubTerminalMenuKind => (int)_hubTerminalMode;
        public int HubTerminalSelectedIndex => _hubTerminalSelectedIndex;
        public int HubTerminalChoiceCount => _hubTerminalMode == HubTerminalMode.Contracts ? _offers.Count : 0;
        public bool HubTerminalConfirmValid =>
            State == CourierRunState.Hub &&
            _hubTerminalMode == HubTerminalMode.Contracts &&
            _hubTerminalSelectedIndex >= 0 &&
            _hubTerminalSelectedIndex < _offers.Count;
        public bool IsDeploymentTransition => State == CourierRunState.TeleportingToDesert;
        public int PickupSequence { get; private set; }
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
        private DuneVectorProceduralBuildingDirector _buildings;
        private CourierContractTuning _settings;
        private DeliveryMessageTuning _messageSettings;
        private DeliveryTuning _deliverySettings;
        private DuneVectorWindFieldSystem _windFields;
        private DuneVectorDustDevilSystem _dustDevils;
        private WorldHubTuning _hubSettings;
        private DesertAtlasTuning _desertAtlasSettings;
        private FreeRoamDeliveryTuning _freeRoamSettings;
        private GeoglyphSystemTuning _geoglyphs;
        private RingTuning _ringSettings;
        private DuneVectorFreeRoamDeliverySystem _freeRoamDeliveries;
        private DuneVectorEnemyDirector _enemyDirector;
        private DuneVectorStormPyramidDirector _stormDirector;
        private DuneVectorVesperKiteDirector _vesperKiteDirector;
        private DuneVectorRouteEncounterDirector _routeEncounterDirector;
        private DuneVectorEnvironmentalHazardSystem _environmentalHazards;
        private DuneVectorDeliveryMessagePresenter _messagePresenter;
        private DuneVectorSandAmbusherSystem _sandAmbusherSystem;

        private Transform _hubRoot;
        private Transform _terminal;
        private Transform _messageArchiveTerminal;
        private Transform _freeRoamTerminal;
        private Transform _teleportPlatform;
        private Transform _hubEnergyOrbit;
        private Transform _upgradeEnergyOrbit;
        private Coroutine _hubClothResetCoroutine;
        private Vector3 _hubSpawn;
        // Counts down while the follow camera is pinned to the drone after a teleport, so the
        // camera cannot smooth in from where the drone came from.
        private float _cameraPinSecondsRemaining;
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
        private int _paidOfferRefreshIndex;
        private float _teleportTimer;
        private bool _teleportMoved;
        private bool _returnStartsVanished;
        private bool _deliveryCompletionInProgress;
        private bool _deliveryMessageSafetyActive;
        private bool _infiniteHealthBeforeDeliveryMessage;
        private HubTerminalMode _hubTerminalMode;
        private int _hubTerminalSelectedIndex;
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
        private readonly List<Material> _hubAuthoredScreenMaterials = new List<Material>();
        private bool _hubRgbTerminalsApplied;
        private bool? _hubAuthoredScreenColorNoiseUnlocked;

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
        private GUIStyle _hudLabelStyle;
        private GUIStyle _hudValueStyle;
        private GUIStyle _objectiveStyle;
        private GUIStyle _statusStyle;
        private readonly DuneVectorObjectiveIndicator _objectiveIndicator = new DuneVectorObjectiveIndicator();
        private Texture2D _terminalPanelTexture;
        private Texture2D _terminalCardTexture;
        private Texture2D _terminalCardHoverTexture;
        private Texture2D _archiveTileTexture;
        private Texture2D _archiveTileHoverTexture;
        private Vector2 _archiveScrollPosition;

        private bool UsesPremiumHubVisual => _hubSettings.PremiumVisualPrefab != null
            && _hubSettings.ReplaceProceduralStructureVisuals;

        private float PremiumHubHorizontalScale => Mathf.Max(
            Mathf.Abs(_hubSettings.PremiumVisualLocalScale.x),
            Mathf.Abs(_hubSettings.PremiumVisualLocalScale.z));

        private float HubPlatformSurfaceRadius
        {
            get
            {
                if (!UsesPremiumHubVisual)
                {
                    return Mathf.Max(0f, _hubSettings.PlatformRadius * 0.5f);
                }

                return Mathf.Max(0f, _hubSettings.PremiumVisualSurfaceRadius * PremiumHubHorizontalScale);
            }
        }

        /// <summary>
        /// Radius of the authored hub's sunken centre plaza. Zero means the hub
        /// walks as one flat floor at <see cref="HubDeckSurfaceLocalHeight"/>.
        /// </summary>
        private float HubPlazaRadius => UsesPremiumHubVisual
            ? Mathf.Clamp(
                _hubSettings.PremiumVisualPlazaRadius * PremiumHubHorizontalScale,
                0f,
                HubPlatformSurfaceRadius)
            : 0f;

        /// <summary>Hub-local height of the outer walkable deck ring.</summary>
        private float HubDeckSurfaceLocalHeight => UsesPremiumHubVisual
            ? _hubSettings.PremiumVisualDeckSurfaceHeight * Mathf.Abs(_hubSettings.PremiumVisualLocalScale.y)
            : _hubSettings.PlatformThickness * 0.5f;

        /// <summary>Hub-local height of the sunken centre plaza the drone spawns on.</summary>
        private float HubPlazaSurfaceLocalHeight => UsesPremiumHubVisual && HubPlazaRadius > 0f
            ? _hubSettings.PremiumVisualPlazaSurfaceHeight * Mathf.Abs(_hubSettings.PremiumVisualLocalScale.y)
            : HubDeckSurfaceLocalHeight;

        private float GetHubSurfaceLocalHeight(float planarDistanceFromHubCenter)
        {
            float plazaRadius = HubPlazaRadius;
            return plazaRadius > 0f && planarDistanceFromHubCenter <= plazaRadius
                ? HubPlazaSurfaceLocalHeight
                : HubDeckSurfaceLocalHeight;
        }

        public void Initialize(
            DronePlayer playerInput,
            DuneVectorAudioManager audio,
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
            FreeRoamDeliveryTuning freeRoamDeliverySettings,
            RingTuning ringSettings,
            GeoglyphSystemTuning geoglyphs,
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
            _freeRoamSettings = freeRoamDeliverySettings ?? new FreeRoamDeliveryTuning();
            _freeRoamSettings.EnsureInitialized();
            _ringSettings = ringSettings ?? new RingTuning();
            _geoglyphs = geoglyphs;
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
                audio,
                Progress.DeliveryMessageInputHintAcknowledged,
                Progress.AcknowledgeDeliveryMessageInputHint);
            if (!DuneTrainingRuntime.ControlledPreHazardStage)
            {
                _sandAmbusherSystem = gameObject.AddComponent<DuneVectorSandAmbusherSystem>();
                _sandAmbusherSystem.Initialize(_player, _health, _world, _settings);
            }
            _freeRoamDeliveries = gameObject.AddComponent<DuneVectorFreeRoamDeliverySystem>();
            _freeRoamDeliveries.Initialize(
                _player,
                _world,
                _camera,
                _materials,
                _wallet,
                _landmarks,
                this,
                Progress,
                _deliverySettings,
                _settings,
                _freeRoamSettings,
                _ringSettings);
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

        public void BindDustDevils(DuneVectorDustDevilSystem dustDevils)
        {
            _dustDevils = dustDevils;
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
            _freeRoamDeliveries?.NotifyStreakBroken();
            BeginTeleport(toHub: true);
        }

        public void RestartAtHub(bool playReturnEffect = true)
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
            if (playReturnEffect)
            {
                _player.PlayHubReturnEffect(HubFloorPosition);
            }
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
            _freeRoamDeliveries?.EndDeployment();
            _landmarks?.ClearContractLandmarks();
            PrepareFreeRoamDeployment();
            BeginTeleport(toHub: false);
            return true;
        }

        public void BindProceduralBuildings(DuneVectorProceduralBuildingDirector buildings)
        {
            _buildings = buildings;
        }

        /// <summary>
        /// Free roam drops the drone at a random point inside the combined footprint of every
        /// authored geoglyph, so each deployment starts beside a different landmark.
        /// </summary>
        private void PrepareFreeRoamDeployment()
        {
            LogicalPosition deployment = ChooseFreeRoamDeploymentPosition();
            float insertionHeight =
                (float)_world.HeightField.SampleHeight(deployment.X, deployment.Z) +
                _hubSettings.DesertInsertionHeight;
            _desertSpawn = _world.LogicalToLocal(deployment.X, insertionHeight, deployment.Z);

            float headingRadians = _hubSettings.FreeRoamDeploymentHeadingDegrees * Mathf.Deg2Rad;
            Vector3 heading = new Vector3(
                Mathf.Cos(headingRadians),
                0f,
                Mathf.Sin(headingRadians));
            _desertRotation = Quaternion.LookRotation(heading, Vector3.up);
        }

        private LogicalPosition ChooseFreeRoamDeploymentPosition()
        {
            Vector2 minimum;
            Vector2 maximum;
            if (_geoglyphs == null ||
                !_geoglyphs.TryGetCombinedFootprintBounds(out minimum, out maximum))
            {
                float halfExtent = _freeRoamSettings.DeploymentFallbackHalfExtent;
                Vector2 hub = DesertWorldStreamer.StartingLogicalPosition;
                minimum = hub - new Vector2(halfExtent, halfExtent);
                maximum = hub + new Vector2(halfExtent, halfExtent);
            }

            float inset = _freeRoamSettings.DeploymentBoundsInset;
            Vector2 center = (minimum + maximum) * 0.5f;
            minimum = Vector2.Min(minimum + new Vector2(inset, inset), center);
            maximum = Vector2.Max(maximum - new Vector2(inset, inset), center);

            int attempts = Mathf.Max(1, _freeRoamSettings.DeploymentPlacementAttempts);
            LogicalPosition sample = new LogicalPosition(center.x, center.y);
            for (int i = 0; i < attempts; i++)
            {
                sample = new LogicalPosition(
                    UnityEngine.Random.Range(minimum.x, maximum.x),
                    UnityEngine.Random.Range(minimum.y, maximum.y));
                if (_landmarks == null ||
                    !_landmarks.OverlapsLandmarkFootprint(
                        sample.X,
                        sample.Z,
                        _freeRoamSettings.DeploymentLandmarkClearance))
                {
                    break;
                }
            }

            return _world.ResolvePlayerSpawnAwayFromObstacles(sample, Vector3.forward);
        }

        /// <summary>
        /// Snaps the follow camera onto the teleported drone and pins it there for a short window.
        /// A single snap at placement time is not enough: the character motor settles during the
        /// next simulation step, and a long jump (dying far out in free roam and restoring to the
        /// hub) makes the streamer rebase its floating origin on a later frame. Either one can
        /// leave the follow point behind, and the camera then sweeps across the desert to catch up.
        /// </summary>
        private void PinCameraToPlayer(Vector3 forward)
        {
            _cameraPinSecondsRemaining = Mathf.Max(0f, _hubSettings.TeleportCameraPinSeconds);
            _cameraController?.SnapToTarget(forward);
        }

        /// <summary>
        /// Holds the pinned camera on the drone through the window. This runs after the streamer's
        /// floating-origin rebase and after the player camera update, so it is the last word on
        /// where the follow point sits. Only a trail long enough to read as a sweep is corrected,
        /// so the drone's short drop onto the hub still smooths normally, and only the follow
        /// position is re-anchored, leaving the player free to look around immediately.
        /// </summary>
        private void LateUpdate()
        {
            if (_cameraPinSecondsRemaining <= 0f)
            {
                return;
            }

            _cameraPinSecondsRemaining -= Time.unscaledDeltaTime;
            if (_cameraController != null &&
                _cameraController.FollowingError > _hubSettings.TeleportCameraPinMaximumTrailMeters)
            {
                _cameraController.SnapFollowPositionToTarget();
            }
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

            DroneRawInputFrame command = _playerInput != null
                ? _playerInput.CurrentCommand
                : default;
            if (_hubTerminalMode != HubTerminalMode.None && command.CancelPressed)
            {
                SetHubTerminalMode(HubTerminalMode.None);
                _playerInput?.ConsumeContextualActions();
                return;
            }

            if (_hubTerminalMode == HubTerminalMode.Contracts)
            {
                if (_offers.Count > 0 && Mathf.Abs(command.MenuNavigate) > 0.5f)
                {
                    int direction = command.MenuNavigate > 0f ? 1 : -1;
                    _hubTerminalSelectedIndex =
                        (_hubTerminalSelectedIndex + direction + _offers.Count) % _offers.Count;
                }
                if (command.ConfirmPressed && HubTerminalConfirmValid)
                {
                    AcceptOffer(_hubTerminalSelectedIndex);
                }
                _playerInput?.ConsumeContextualActions();
                return;
            }

            if (_hubTerminalMode == HubTerminalMode.None && command.InteractPressed &&
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
            _playerInput?.ConsumeContextualActions();
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
            bool replaceProceduralStructureVisuals = BuildPremiumHubVisual();

            if (!replaceProceduralStructureVisuals)
            {
                HubPart(PrimitiveType.Cylinder, "Main Teleport Platform", _hubRoot, Vector3.zero,
                    new Vector3(_hubSettings.PlatformRadius, _hubSettings.PlatformThickness * 0.5f, _hubSettings.PlatformRadius),
                    Quaternion.identity, _hubMetalMaterial, false);
            }
            BuildHubFloorColliders();
            BuildHubContainment();
            if (!replaceProceduralStructureVisuals)
            {
                HubPart(PrimitiveType.Cylinder, "Energy Inlay", _hubRoot,
                    new Vector3(0f, HubDeckSurfaceLocalHeight + 0.08f, 0f),
                    new Vector3(_hubSettings.PlatformRadius * 0.72f, 0.08f, _hubSettings.PlatformRadius * 0.72f),
                    Quaternion.identity, _hubEnergyMaterial, false);
            }

            if (_hubSettings.PlatformEnergyLanesEnabled)
            {
                _hubEnergyOrbit = new GameObject("Rotating Platform Energy Lanes").transform;
                _hubEnergyOrbit.SetParent(_hubRoot, false);
                _hubEnergyOrbit.localPosition = Vector3.up * (HubDeckSurfaceLocalHeight + 0.18f);
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

            if (!replaceProceduralStructureVisuals)
            {
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
            }

            _terminal = BuildPhysicalTerminal(
                "Physical Contract Terminal",
                _hubSettings.ContractTerminalLocalPosition,
                Quaternion.Euler(_hubSettings.ContractTerminalLocalEulerAngles));
            _messageArchiveTerminal = BuildPhysicalTerminal(
                "Physical Message Archive Terminal",
                _hubSettings.ArchiveTerminalLocalPosition,
                Quaternion.Euler(_hubSettings.ArchiveTerminalLocalEulerAngles));
            _freeRoamTerminal = BuildPhysicalTerminal(
                "Physical Free Roam Terminal",
                _hubSettings.FreeRoamTerminalLocalPosition,
                Quaternion.Euler(_hubSettings.FreeRoamTerminalLocalEulerAngles));

            // The authored hub carries its own upgrade bay, so the primitive pad
            // and its rotating calibration arms only exist for the procedural hub.
            if (!replaceProceduralStructureVisuals)
            {
                BuildUpgradeArea();
            }

            _teleportPlatform = _hubRoot;
            _hubSpawn = _hubRoot.position
                + (Vector3.up * (_hubSettings.PlayerSpawnHeight + GetHubSurfaceLocalHeight(0f)));
            DuneVectorPhotographableMarker.Register(
                hubObject,
                DuneVectorCompendiumSubjectIds.Hub,
                PhotographableSubjectCategory.Misc,
                minimumObserverHorizontalDistance:
                    _hubSettings.PhotographySuppressionRadius);
        }

        private void BuildUpgradeArea()
        {
            Transform upgradeArea = new GameObject("Drone Upgrade Area").transform;
            upgradeArea.SetParent(_hubRoot, false);
            upgradeArea.localPosition = _hubSettings.UpgradeAreaLocalPosition;
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
        }

        /// <summary>
        /// Builds the walkable floor collision for the procedural hub. An authored
        /// hub collides against its own meshes instead, so its modelled ramps and
        /// rails are the collision and nothing extra is generated over them. The
        /// authored floor is probed afterwards, because a hub whose centre has no
        /// upward-facing collision would drop the drone straight through it.
        /// </summary>
        private void BuildHubFloorColliders()
        {
            if (!UsesPremiumHubVisual || !_hubSettings.PremiumVisualMeshCollisionEnabled)
            {
                BuildCircleModelCollider(
                    _hubRoot,
                    "Main Teleport Platform Collider (circle.glb)",
                    Vector3.up * HubDeckSurfaceLocalHeight,
                    HubPlatformSurfaceRadius);
                return;
            }

            Physics.SyncTransforms();
            float plazaRadius = HubPlazaRadius > 0f ? HubPlazaRadius : HubPlatformSurfaceRadius;
            if (HasAuthoredHubFloorUnder(Vector3.zero)
                && HasAuthoredHubFloorUnder(Vector3.forward * (plazaRadius * 0.6f))
                && HasAuthoredHubFloorUnder(Vector3.right * (plazaRadius * 0.6f)))
            {
                return;
            }

            Debug.LogWarning(
                "The authored hub has no walkable collision over part of its centre, so the drone "
                + "would fall through it. Filling the centre with a flat floor collider at "
                + $"PremiumVisualPlazaSurfaceHeight ({_hubSettings.PremiumVisualPlazaSurfaceHeight}). "
                + "Model an upward-facing floor across the plaza to remove it.",
                this);
            BuildCircleModelCollider(
                _hubRoot,
                "Hub Plaza Floor Fallback Collider (circle.glb)",
                Vector3.up * HubPlazaSurfaceLocalHeight,
                plazaRadius);
        }

        /// <summary>
        /// Drops a probe onto the authored hub from above the plaza floor and
        /// reports whether any of the hub's own colliders caught it.
        /// </summary>
        private bool HasAuthoredHubFloorUnder(Vector3 hubLocalOffset)
        {
            const float probeHeightAboveFloor = 6f;
            const float probeDepthBelowFloor = 3f;
            Vector3 origin = _hubRoot.position
                + hubLocalOffset
                + (Vector3.up * (HubPlazaSurfaceLocalHeight + probeHeightAboveFloor));
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                probeHeightAboveFloor + probeDepthBelowFloor,
                ~0,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                if (hits[hitIndex].collider != null
                    && hits[hitIndex].collider.transform.IsChildOf(_hubRoot))
                {
                    return true;
                }
            }

            return false;
        }

        private bool BuildPremiumHubVisual()
        {
            if (_hubSettings.PremiumVisualPrefab == null)
            {
                return false;
            }

            GameObject visual = Instantiate(_hubSettings.PremiumVisualPrefab, _hubRoot, false);
            visual.name = _hubSettings.PremiumVisualPrefab.name;
            visual.transform.SetLocalPositionAndRotation(
                _hubSettings.PremiumVisualLocalPosition,
                Quaternion.Euler(_hubSettings.PremiumVisualLocalEulerAngles));
            visual.transform.localScale = _hubSettings.PremiumVisualLocalScale;
            BuildPremiumHubMeshColliders(visual.transform);
            CollectAuthoredHubScreenMaterials(visual.transform);
            return _hubSettings.ReplaceProceduralStructureVisuals;
        }

        /// <summary>
        /// Collides the drone against the authored hub exactly as it was modelled,
        /// so its ramps, rails, and props are the collision surface. Nothing is
        /// generated on top of the mesh, which is what would otherwise leave an
        /// invisible lip where an approximated floor met a modelled slope.
        /// </summary>
        private void BuildPremiumHubMeshColliders(Transform visualRoot)
        {
            if (!_hubSettings.PremiumVisualMeshCollisionEnabled || visualRoot == null)
            {
                return;
            }

            int collidersAdded = 0;
            int unreadableMeshes = 0;
            MeshFilter[] meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
            for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
            {
                MeshFilter meshFilter = meshFilters[meshIndex];
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null || meshFilter.gameObject.GetComponent<MeshCollider>() != null)
                {
                    continue;
                }

                // A mesh without CPU-side data cannot be cooked into collision.
                if (!mesh.isReadable)
                {
                    unreadableMeshes++;
                    continue;
                }

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collidersAdded++;
            }

            if (unreadableMeshes > 0)
            {
                Debug.LogError(
                    $"{unreadableMeshes} of {meshFilters.Length} meshes on "
                    + $"{_hubSettings.PremiumVisualPrefab.name} are not readable, so they cannot "
                    + "collide. Enable Read/Write on the hub model importer.",
                    this);
            }
            else if (collidersAdded == 0)
            {
                Debug.LogError(
                    $"{_hubSettings.PremiumVisualPrefab.name} produced no hub collision. "
                    + "The drone will fall through the hub.",
                    this);
            }
        }

        /// <summary>
        /// Grabs the authored hub's own terminal screens so the RGB unlock can
        /// drive them. They are modelled into the hub rather than built from
        /// primitives, so they are found by the object names the model uses.
        /// </summary>
        private void CollectAuthoredHubScreenMaterials(Transform visualRoot)
        {
            _hubAuthoredScreenMaterials.Clear();
            _hubAuthoredScreenColorNoiseUnlocked = null;
            HubRgbTerminalUnlockTuning tuning = _permanentUpgrades?.HubRgbTerminalTuning;
            string[] screenNames = tuning?.AuthoredScreenObjectNames;
            if (visualRoot == null || screenNames == null || screenNames.Length == 0)
            {
                return;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || !IsAuthoredHubScreen(renderer.gameObject.name, screenNames))
                {
                    continue;
                }

                // Instanced materials, so the RGB unlock never writes onto the
                // shared asset and leaks the noise into every other hub screen.
                Material[] materials = renderer.materials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material != null)
                    {
                        _hubAuthoredScreenMaterials.Add(material);
                    }
                }
            }

            if (_hubAuthoredScreenMaterials.Count == 0)
            {
                Debug.LogWarning(
                    $"None of the authored hub screens named in {nameof(HubRgbTerminalUnlockTuning)}."
                    + $"{nameof(HubRgbTerminalUnlockTuning.AuthoredScreenObjectNames)} were found under "
                    + $"{visualRoot.name}, so the RGB terminal unlock cannot drive their colour noise.",
                    this);
            }
        }

        private static bool IsAuthoredHubScreen(string objectName, string[] screenNames)
        {
            for (int index = 0; index < screenNames.Length; index++)
            {
                if (string.Equals(objectName, screenNames[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private Transform BuildPhysicalTerminal(string objectName, Vector3 localPosition, Quaternion localRotation)
        {
            Transform terminal = new GameObject(objectName).transform;
            terminal.SetParent(_hubRoot, false);
            terminal.SetLocalPositionAndRotation(localPosition, localRotation);
            if (_hubSettings.UseAuthoredTerminalGeometry && UsesPremiumHubVisual)
            {
                // The authored hub models its own consoles, so this stays an
                // invisible interaction anchor standing in front of one.
                return terminal;
            }

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
                HubDeckSurfaceLocalHeight + (wallHeight * 0.5f));

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

            float platformSurfaceY = _hubRoot.position.y
                + GetHubSurfaceLocalHeight(planarOffset.magnitude);
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
            if (_permanentUpgrades == null
                || (_hubTerminalPanelMaterials.Count == 0 && _hubAuthoredScreenMaterials.Count == 0))
            {
                return;
            }

            HubRgbTerminalUnlockTuning tuning = _permanentUpgrades.HubRgbTerminalTuning;
            if (tuning == null || !_permanentUpgrades.AreHubRgbTerminalsEnabled)
            {
                ResetHubTerminalEnergyMaterials();
                ApplyAuthoredHubScreenColorNoise(tuning, unlocked: false);
                return;
            }

            ApplyAuthoredHubScreenColorNoise(tuning, unlocked: true);
            if (_hubTerminalPanelMaterials.Count == 0)
            {
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

        /// <summary>
        /// Drives the authored hub screens' colour noise for the RGB terminal
        /// unlock. Those screens run the TV static shader instead of the hub
        /// energy material, so the unlock reads on them as full per-channel
        /// colour noise rather than as a cycling emission colour.
        /// </summary>
        private void ApplyAuthoredHubScreenColorNoise(HubRgbTerminalUnlockTuning tuning, bool unlocked)
        {
            if (_hubAuthoredScreenMaterials.Count == 0)
            {
                return;
            }

            // The value only changes when the unlock is toggled, so this stays
            // off the per-frame material write path. The first evaluation always
            // writes, because the authored material ships whichever look it was
            // last saved with.
            if (_hubAuthoredScreenColorNoiseUnlocked == unlocked)
            {
                return;
            }

            string colorNoiseProperty = tuning?.AuthoredScreenColorNoiseProperty;
            if (string.IsNullOrEmpty(colorNoiseProperty))
            {
                return;
            }

            float colorNoise = unlocked
                ? tuning.AuthoredScreenUnlockedColorNoise
                : tuning.AuthoredScreenLockedColorNoise;
            for (int index = 0; index < _hubAuthoredScreenMaterials.Count; index++)
            {
                Material material = _hubAuthoredScreenMaterials[index];
                if (material != null && material.HasProperty(colorNoiseProperty))
                {
                    material.SetFloat(colorNoiseProperty, colorNoise);
                }
            }
            _hubAuthoredScreenColorNoiseUnlocked = unlocked;
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
            BeginHubClothReset();
            _health.SetDamageImmune(true);
            _player.RestoreRunningFlightMeter();
            CleanupContractObjects();
            // Returning to the hub ends the free-roam run and breaks any delivery streak.
            _freeRoamDeliveries?.EndDeployment();
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
                // An immediate hub restore must complete its floating-origin shift before the
                // camera is snapped. Deferring this rebase to the streamer's LateUpdate leaves
                // the follow camera in the old desert frame for a visible cross-world sweep.
                _world.RebaseAroundPlayerIfNeeded();
                _player.ResetTraversalAfterTeleport(Vector3.forward);
                PinCameraToPlayer(Vector3.forward);
            }
            SetCombatSystemsActive(false);
            _stormDirector?.SetHubLightningActive();
            _sandAmbusherSystem?.EndContract();
            DuneVectorContractRisk.Reset();
            State = CourierRunState.Hub;
            _playerInput.SetInputEnabled(!openTerminal);
            SetTerminalOpen(openTerminal);
        }

        private void BeginHubClothReset()
        {
            if (!_hubSettings.ResetClothOnHubEntry || _hubRoot == null)
            {
                return;
            }

            if (_hubClothResetCoroutine != null)
            {
                return;
            }

            Cloth[] hubCloth = _hubRoot.GetComponentsInChildren<Cloth>(true);
            List<Cloth> enabledCloth = new List<Cloth>(hubCloth.Length);
            for (int index = 0; index < hubCloth.Length; index++)
            {
                Cloth cloth = hubCloth[index];
                if (cloth == null || !cloth.enabled)
                {
                    continue;
                }

                cloth.enabled = false;
                enabledCloth.Add(cloth);
            }

            if (enabledCloth.Count > 0)
            {
                _hubClothResetCoroutine = StartCoroutine(CompleteHubClothReset(enabledCloth));
            }
        }

        private IEnumerator CompleteHubClothReset(List<Cloth> hubCloth)
        {
            float delaySeconds = Mathf.Max(0f, _hubSettings.ClothResetDelaySeconds);
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }
            else
            {
                yield return null;
            }

            Physics.SyncTransforms();
            for (int index = 0; index < hubCloth.Count; index++)
            {
                Cloth cloth = hubCloth[index];
                if (cloth == null)
                {
                    continue;
                }

                cloth.enabled = true;
                cloth.ClearTransformMotion();
            }

            _hubClothResetCoroutine = null;
        }

        private void GenerateOffers()
        {
            _offers.Clear();
            int completionTier = Progress != null ? Progress.CompletedDeliveries : 0;
            int count = DuneTrainingRuntime.ControlledPreHazardStage
                ? 1
                : Mathf.Clamp(_settings.OfferedContractCount, 5, 8);
            // Wall-clock startup time differs substantially between visual and headless builds.
            // Do not let that select a different contract for the same training/evaluation seed.
            int batch = DuneTrainingRuntime.Enabled
                ? 0
                : Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(1f, _settings.ContractRefreshSeconds));
            System.Random random = new System.Random(unchecked(
                _world.WorldSeed ^ _settings.ContractSeedOffset ^ (completionTier * 486187739) ^ batch ^
                _paidOfferRefreshIndex));
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

        private void TryPurchaseOfferRefresh()
        {
            int refreshCost = Mathf.Max(0, _settings.ContractRefreshGoldCost);
            if (refreshCost > 0 && (_wallet == null || !_wallet.TrySpendGold(refreshCost)))
            {
                return;
            }

            _paidOfferRefreshIndex = _paidOfferRefreshIndex == int.MaxValue
                ? int.MinValue
                : _paidOfferRefreshIndex + 1;
            GenerateOffers();
        }

        private CourierContract CreateOffer(System.Random random, int index, int completed)
        {
            int seed = random.Next();
            int difficulty = _settings.EvaluateRisk(completed);
            float minimumRouteDistance = _settings.EvaluateMinimumRouteDistance(difficulty);
            float maximumRouteDistance = _settings.EvaluateMaximumRouteDistance(difficulty);
            if (DuneTrainingRuntime.ControlledPreHazardStage && DuneVectorBootstrap.Instance != null)
            {
                PufferTrainingTuning training = DuneVectorBootstrap.Instance.PufferTraining;
                float controlledMinimum = Mathf.Max(10f, training.Stage2MinimumRouteDistance);
                float controlledMaximum = Mathf.Max(controlledMinimum, training.Stage2MaximumRouteDistance);
                if (DuneTrainingRuntime.ControlledGroundStage)
                {
                    float distanceScale = DuneTrainingRuntime.ReadStage2DistanceScale();
                    minimumRouteDistance = Mathf.Lerp(controlledMinimum, minimumRouteDistance, distanceScale);
                    maximumRouteDistance = Mathf.Lerp(controlledMaximum, maximumRouteDistance, distanceScale);
                }
                else
                {
                    minimumRouteDistance = controlledMinimum;
                    maximumRouteDistance = controlledMaximum;
                }
            }
            float distance = Mathf.Lerp(minimumRouteDistance, maximumRouteDistance, (float)random.NextDouble());
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
            _player.BeginContractFlightMeter();
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

            routeOrigin = ConstrainStage2SpawnOutsidePickupZone(routeOrigin, -pickupForward);
            routeOrigin = _world.ResolvePlayerSpawnAwayFromObstacles(routeOrigin, -pickupForward);
            routeOrigin = ConstrainStage2SpawnOutsidePickupZone(routeOrigin, -pickupForward);
            insertionHeight =
                (float)_world.HeightField.SampleHeight(routeOrigin.X, routeOrigin.Z) +
                _hubSettings.DesertInsertionHeight;
            _desertSpawn = _world.LogicalToLocal(routeOrigin.X, insertionHeight, routeOrigin.Z);

            Vector3 actualPickupForward = Vector3.ProjectOnPlane(_package.position - _desertSpawn, Vector3.up);
            if (actualPickupForward.sqrMagnitude > 0.001f)
            {
                _desertRotation = Quaternion.LookRotation(actualPickupForward.normalized, Vector3.up);
            }
            ProtectContractObjectivesFromWind();
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
            if (DuneTrainingRuntime.ControlledPreHazardStage && DuneVectorBootstrap.Instance != null)
            {
                PufferTrainingTuning training = DuneVectorBootstrap.Instance.PufferTraining;
                float controlledMinimum = Mathf.Max(10f, training.Stage2MinimumRouteDistance) /
                    Mathf.Max(1, contract.StopCount);
                float controlledMaximum = Mathf.Max(
                    controlledMinimum,
                    training.Stage2MaximumRouteDistance / Mathf.Max(1, contract.StopCount));
                if (DuneTrainingRuntime.ControlledGroundStage)
                {
                    float distanceScale = DuneTrainingRuntime.ReadStage2DistanceScale();
                    minimumLegDistance = Mathf.Lerp(controlledMinimum, minimumLegDistance, distanceScale);
                    maximumLegDistance = Mathf.Lerp(controlledMaximum, maximumLegDistance, distanceScale);
                }
                else
                {
                    minimumLegDistance = controlledMinimum;
                    maximumLegDistance = controlledMaximum;
                }
            }
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
            double pickupRingHeight = pickupGroundHeight + _deliverySettings.PickupRingGroundOffset;
            _package = DuneVectorVisuals.CreatePackageVisual(transform, _materials, _settings.ObjectivePackageScale);
            _package.name = $"Contract Cargo {ActiveContract.ContractId}";
            _package.position = objectivePosition;
            _objectiveRing = CreateObjectiveRing(
                "Contract Pickup Ring",
                landmark,
                objectiveLogical,
                pickupRingHeight,
                true,
                HandlePackagePickup);
            // Navigation, UI, and training observations must target the same
            // fitted landmark zone that actually completes pickup. Large landmark
            // bounds can move that zone far from the authored package socket.
            ActiveObjective = _objectiveRing.transform;
            ActiveObjectiveLogicalPosition = _objectiveRing.LogicalPosition;
        }

        private void ProtectContractObjectivesFromWind()
        {
            if (_windFields == null)
            {
                return;
            }

            List<WindFieldExclusion> exclusions =
                new List<WindFieldExclusion>(_routeLandmarks.Count + 1);
            LogicalPosition spawnLogical = LocalToLogical(_desertSpawn);
            exclusions.Add(new WindFieldExclusion(
                new Vector2((float)spawnLogical.X, (float)spawnLogical.Z),
                0f,
                isPlayerSpawn: true));
            for (int i = 0; i < _routeLandmarks.Count; i++)
            {
                DuneVectorLandmarkInstance landmark = _routeLandmarks[i];
                Transform socket = i == 0 ? landmark.ContractSocket : landmark.DeliverySocket;
                LogicalPosition logical = LocalToLogical(socket.position);
                float ringRadius = i == 0
                    ? _deliverySettings.ObjectiveRingRadius
                    : _deliverySettings.DeliveryRingRadius;
                if (TryResolveLandmarkZone(landmark, out LogicalPosition zoneCenter, out float zoneRadius))
                {
                    logical = zoneCenter;
                    ringRadius = zoneRadius;
                }
                exclusions.Add(new WindFieldExclusion(
                    new Vector2((float)logical.X, (float)logical.Z),
                    ringRadius));
            }
            _windFields.SetContractObjectiveExclusions(exclusions);
        }

        private JobTraversalRing CreateObjectiveRing(
            string objectName,
            DuneVectorLandmarkInstance landmark,
            LogicalPosition logical,
            double height,
            bool pickup,
            Action crossed,
            bool playDeliveryAudio = true)
        {
            LogicalPosition center = logical;
            double ringHeight = height;
            float radius = Mathf.Max(1f, pickup
                ? _deliverySettings.ObjectiveRingRadius
                : _deliverySettings.DeliveryRingRadius);
            if (TryResolveLandmarkZone(landmark, out LogicalPosition zoneCenter, out float zoneRadius))
            {
                center = zoneCenter;
                radius = zoneRadius;
                ringHeight = _world.HeightField.SampleHeight(center.X, center.Z) + (pickup
                    ? _deliverySettings.PickupRingGroundOffset
                    : _deliverySettings.DeliveryRingGroundOffset);
            }

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
                radius,
                crossed,
                canActivate,
                playDeliveryAudio);
            ring.LogicalPosition = center;
            ring.LogicalHeight = ringHeight;
            ring.transform.position = _world.LogicalToLocal(center.X, ringHeight, center.Z);
            return ring;
        }

        /// <summary>
        /// Contract objectives use the same fitted hexagon zone free roam does: centered on the
        /// landmark's rendered mesh bounds and padded past them, so the zone hugs the silhouette
        /// the player actually sees instead of using one fixed radius everywhere.
        /// </summary>
        private bool TryResolveLandmarkZone(
            DuneVectorLandmarkInstance landmark,
            out LogicalPosition center,
            out float radius)
        {
            center = default;
            radius = 0f;
            if (landmark == null ||
                _settings == null ||
                !_settings.FitObjectiveZonesToLandmark ||
                !landmark.TryCalculateMeshBounds(out Bounds bounds))
            {
                return false;
            }

            center = LocalToLogical(bounds.center);
            radius = Mathf.Clamp(
                Mathf.Max(bounds.extents.x, bounds.extents.z) + _settings.ObjectiveZonePadding,
                _settings.MinimumObjectiveZoneRadius,
                Mathf.Max(_settings.MinimumObjectiveZoneRadius, _settings.MaximumObjectiveZoneRadius));
            return true;
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
            PickupSequence++;
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
            double groundHeight = _world.HeightField.SampleHeight(
                objectiveLogical.X,
                objectiveLogical.Z);
            _objectiveRing = CreateObjectiveRing(
                $"Delivery Ring {_deliveryIndex + 1}",
                landmark,
                objectiveLogical,
                groundHeight + _deliverySettings.DeliveryRingGroundOffset,
                false,
                HandleDelivery,
                _deliveryIndex == ActiveContract.DeliveryPositions.Count - 1);
            ActiveObjective = _objectiveRing.transform;
            ActiveObjectiveLogicalPosition = _objectiveRing.LogicalPosition;
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
            // Dying always breaks the free-roam streak, whether or not a contract was running.
            _freeRoamDeliveries?.NotifyStreakBroken();
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
                DuneVectorEnemySpawnClearance.Clear();
            }
            else
            {
                PublishDesertSpawnClearance();
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
            PinCameraToPlayer(Vector3.forward);
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
                if (!toHub)
                {
                    if (!TryPlacePlayerAtSupportedDesertSpawn())
                    {
                        // Remain vanished with input disabled and retry next frame. Simulation must
                        // never resume at an unverified desert position.
                        return;
                    }
                    PublishDesertSpawnClearance();
                    _world.RemoveEnemiesInsidePlayerSpawnClearance();
                }
                _teleportMoved = true;
                Vector3 position = toHub ? _hubSpawn : _desertSpawn;
                Quaternion rotation = toHub ? Quaternion.identity : _desertRotation;
                if (toHub)
                {
                    _player.Motor.SetPositionAndRotation(position, rotation, true);
                }
                _health.SetDamageImmune(toHub);
                _player.ResetTraversalAfterTeleport(rotation * Vector3.forward);
                PinCameraToPlayer(rotation * Vector3.forward);
                RecenterTeleportParticles(position);
                if (!toHub)
                {
                    _player.PlayTeleportNovaEffect(_player.WorldCenter);
                }
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
                if (isFreeRoam)
                {
                    // Runs after the deployment banner so the first pickup callout replaces it.
                    _freeRoamDeliveries?.BeginDeployment();
                }
            }
        }

        /// <summary>
        /// Publishes the desert deployment point so enemy placement keeps the authored radius around
        /// it clear for both contract insertions and free roam deployments.
        /// </summary>
        private void PublishDesertSpawnClearance()
        {
            LogicalPosition spawnLogical = LocalToLogical(_desertSpawn);
            DuneVectorEnemySpawnClearance.SetSpawnPoint(spawnLogical.X, spawnLogical.Z);
        }

        private bool TryPlacePlayerAtSupportedDesertSpawn()
        {
            LogicalPosition requested = LocalToLogical(_desertSpawn);
            Vector3 fallbackDirection = -(_desertRotation * Vector3.forward);
            int attemptCount = Mathf.Max(1, _hubSettings.DeploymentGroundRetryCount);
            int deploymentSeed = ActiveContract != null ? ActiveContract.Seed : _world.WorldSeed;
            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                LogicalPosition candidate = requested;
                if (attempt > 0)
                {
                    float angle = DuneVectorMath.HashRange(
                        deploymentSeed,
                        attempt,
                        _world.WorldSeed,
                        991,
                        0f,
                        Mathf.PI * 2f);
                    float radius = Mathf.Max(0.1f, _hubSettings.DeploymentGroundRetrySpacing) * attempt;
                    candidate = new LogicalPosition(
                        requested.X + (Math.Cos(angle) * radius),
                        requested.Z + (Math.Sin(angle) * radius));
                }

                LogicalPosition resolved = ResolveDesertDeploymentCandidate(candidate, fallbackDirection);
                if (!_world.TryPreparePlayerTeleportDestination(
                        resolved,
                        _hubSettings.DesertInsertionHeight,
                        _hubSettings.DeploymentMaximumGroundSlope,
                        out Vector3 supportedSpawn))
                {
                    continue;
                }

                _desertSpawn = supportedSpawn;
                FaceDesertSpawnTowardPackage();
                _player.Motor.BaseVelocity = Vector3.zero;
                _player.Motor.SetPositionAndRotation(_desertSpawn, _desertRotation, true);
                Physics.SyncTransforms();
                if (!_world.HasPreparedTerrainSupport(
                        _player.Motor.TransientPosition,
                        _hubSettings.DeploymentGroundSupportDistance,
                        _hubSettings.DeploymentMaximumGroundSlope))
                {
                    continue;
                }

                _buildings?.ReservePlayerDeployment(resolved);
                if (ActiveContract != null)
                {
                    ProtectContractObjectivesFromWind();
                }
                return true;
            }

            _player.Motor.BaseVelocity = Vector3.zero;
            _player.Motor.SetPositionAndRotation(_hubSpawn, Quaternion.identity, true);
            Physics.SyncTransforms();
            return false;
        }

        private LogicalPosition ResolveDesertDeploymentCandidate(
            LogicalPosition candidate,
            Vector3 fallbackDirection)
        {
            LogicalPosition resolved = _world.ResolvePlayerSpawnAwayFromObstacles(
                candidate,
                fallbackDirection);
            if (_dustDevils != null)
            {
                resolved = _dustDevils.ResolvePlayerDeployment(resolved, fallbackDirection);
            }
            if (_buildings != null)
            {
                resolved = _buildings.ResolvePlayerDeployment(resolved, fallbackDirection);
            }
            return ConstrainStage2SpawnOutsidePickupZone(resolved, fallbackDirection);
        }

        private void FaceDesertSpawnTowardPackage()
        {
            if (_package == null)
            {
                return;
            }

            Vector3 pickupForward = Vector3.ProjectOnPlane(_package.position - _desertSpawn, Vector3.up);
            if (pickupForward.sqrMagnitude > 0.001f)
            {
                _desertRotation = Quaternion.LookRotation(pickupForward.normalized, Vector3.up);
            }
        }

        private LogicalPosition ConstrainStage2SpawnOutsidePickupZone(
            LogicalPosition candidate,
            Vector3 fallbackAwayDirection)
        {
            if (!DuneTrainingRuntime.ControlledPreHazardStage ||
                _objectiveRing == null ||
                DuneVectorBootstrap.Instance == null)
            {
                return candidate;
            }

            PufferTrainingTuning training = DuneVectorBootstrap.Instance.PufferTraining;
            double minimumDistance = _objectiveRing.ActivationRadius +
                Math.Max(10.0, training.Stage2MinimumRouteDistance);
            double controlledMaximumDistance = _objectiveRing.ActivationRadius +
                Math.Max(training.Stage2MinimumRouteDistance, training.Stage2MaximumRouteDistance);
            double maximumDistance = controlledMaximumDistance;
            if (DuneTrainingRuntime.ControlledGroundStage)
            {
                maximumDistance = Mathf.Lerp(
                    (float)controlledMaximumDistance,
                    Mathf.Max((float)minimumDistance, _settings.MaximumPickupSpawnDistance),
                    DuneTrainingRuntime.ReadStage2DistanceScale());
            }
            LogicalPosition center = _objectiveRing.LogicalPosition;
            double deltaX = candidate.X - center.X;
            double deltaZ = candidate.Z - center.Z;
            double distance = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
            if (distance >= minimumDistance && distance <= maximumDistance)
            {
                return candidate;
            }

            if (distance < 0.001)
            {
                Vector3 away = Vector3.ProjectOnPlane(fallbackAwayDirection, Vector3.up).normalized;
                if (away.sqrMagnitude < 0.001f)
                {
                    away = Vector3.back;
                }
                deltaX = away.x;
                deltaZ = away.z;
                distance = 1.0;
            }

            double boundedDistance = Math.Max(minimumDistance, Math.Min(maximumDistance, distance));
            double scale = boundedDistance / distance;
            LogicalPosition constrained = new LogicalPosition(
                center.X + (deltaX * scale),
                center.Z + (deltaZ * scale));
            return constrained;
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
            _player.PlayHubReturnEffect(HubFloorPosition);
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
            if (mode == HubTerminalMode.Contracts)
            {
                _hubTerminalSelectedIndex = Mathf.Clamp(_hubTerminalSelectedIndex, 0, Mathf.Max(0, _offers.Count - 1));
            }
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
            // A rebase triggered by the teleport itself must not strand the pinned follow point:
            // re-anchor it in the new origin frame the moment the world moves under it.
            if (_cameraPinSecondsRemaining > 0f)
            {
                _cameraController?.SnapFollowPositionToTarget();
            }
            _objectiveRing?.ApplyWorldShift(shift);
            if ((State == CourierRunState.TeleportingToDesert ||
                 State == CourierRunState.FindPackage) &&
                _package != null)
            {
                _package.position += shift;
            }
        }

        /// <summary>
        /// Publishes a banner through the shared courier status line so free-roam callouts sit in
        /// the same place as contract callouts.
        /// </summary>
        public void ShowStatusMessage(string message, float duration)
        {
            ShowStatus(message, duration);
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
            // Contract panel labels are tinted per-draw through GUI.color, so their styles stay white.
            _hudTitleStyle = LabelStyle(_settings.HudTitleFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            _hudTitleStyle.wordWrap = false;
            _hudTitleStyle.clipping = TextClipping.Clip;
            _hudBodyStyle = LabelStyle(_settings.HudBodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft, _settings.HudTextColor);
            _hudLabelStyle = LabelStyle(_settings.HudLabelFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            _hudLabelStyle.wordWrap = false;
            _hudLabelStyle.clipping = TextClipping.Clip;
            _hudValueStyle = new GUIStyle(_hudLabelStyle) { alignment = TextAnchor.MiddleRight };
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
            if (DuneTrainingRuntime.Enabled && !DuneTrainingRuntime.VisualEvaluation)
            {
                return;
            }
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
            float refreshButtonWidth = Mathf.Min(
                _hubSettings.TerminalContractRefreshButtonWidth,
                contentWidth);
            float refreshButtonHeight = Mathf.Min(
                _hubSettings.TerminalContractRefreshButtonHeight,
                _hubSettings.TerminalFooterHeight);
            Rect refreshButton = new Rect(
                panel.center.x - (refreshButtonWidth * 0.5f),
                panel.yMax - _hubSettings.TerminalFooterHeight +
                    ((_hubSettings.TerminalFooterHeight - refreshButtonHeight) * 0.5f),
                refreshButtonWidth,
                refreshButtonHeight);
            float footerSideWidth = Mathf.Max(0f, refreshButton.x - (panel.x + padding) - gap);
            GUI.Label(
                new Rect(
                    panel.x + padding,
                    panel.yMax - _hubSettings.TerminalFooterHeight + 7f,
                    footerSideWidth,
                    22f),
                "SELECT A CONTRACT TO DEPLOY",
                _terminalMetaStyle);
            int refreshCost = Mathf.Max(0, _settings.ContractRefreshGoldCost);
            bool canAffordRefresh = refreshCost == 0 || (_wallet != null && _wallet.Gold >= refreshCost);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = canAffordRefresh;
            if (GUI.Button(
                    refreshButton,
                    $"REFRESH CONTRACTS  ·  {refreshCost:N0} GOLD",
                    _terminalButtonStyle))
            {
                TryPurchaseOfferRefresh();
            }
            GUI.enabled = previousEnabled;
            _terminalActionStyle.normal.textColor = GuiTextColor(_hubSettings.TerminalAccentColor);
            GUI.Label(
                new Rect(
                    refreshButton.xMax + gap,
                    panel.yMax - _hubSettings.TerminalFooterHeight + 7f,
                    footerSideWidth,
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
            if (distance <= _hubSettings.TerminalPromptVisibilityRadius)
            {
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
                DrawTerminalPrompt(promptRect, prompt, distance <= interactionRadius);
            }
            GUI.Label(new Rect(24f, 24f, 360f, 86f),
                $"COURIER AERIE\nDELIVERIES  {Progress.CompletedDeliveries}\nCONTRACT GOLD  {Progress.TotalContractGold:N0}", _hudBodyStyle);
        }

        /// <summary>
        /// Centered terminal plate: smoked glass, corner brackets and an accent underline that
        /// blooms from the middle. The accent brightens once the drone is inside interaction range.
        /// </summary>
        private void DrawTerminalPrompt(Rect promptRect, string prompt, bool inRange)
        {
            Color accent = inRange
                ? _hubSettings.TerminalAccentColor
                : Color.Lerp(_hubSettings.TerminalAccentColor, _hubSettings.TerminalMutedTextColor, 0.55f);

            DuneVectorHudChrome.DrawSoftShadow(
                promptRect,
                _hubSettings.TerminalShadowColor,
                new Vector2(4f, 5f),
                5f);

            Color border = Color.Lerp(_hubSettings.TerminalBorderColor, accent, 0.45f);
            border.a = _hubSettings.TerminalBorderColor.a;
            DuneVectorHudChrome.DrawGlassPanel(promptRect, _hubSettings.TerminalPanelColor, border, 1f, 1f);

            Color bracket = accent;
            bracket.a *= inRange ? 0.9f : 0.55f;
            DuneVectorHudChrome.DrawCornerBrackets(promptRect, bracket, 12f, 1f);

            Color underline = accent;
            underline.a *= inRange ? 0.9f : 0.5f;
            float half = promptRect.width * 0.5f;
            Rect rule = new Rect(promptRect.x, promptRect.yMax - 2f, half, 2f);
            DuneVectorHudChrome.DrawHorizontalFade(rule, underline, false);
            DuneVectorHudChrome.DrawHorizontalFade(
                new Rect(promptRect.center.x, rule.y, half, rule.height),
                underline,
                true);

            DuneVectorHudChrome.DrawLabel(
                promptRect,
                prompt,
                _objectiveStyle,
                Color.white,
                new Color(0f, 0f, 0f, 0.65f),
                new Vector2(1f, 1f));
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

        public bool TryGetHubInteractionObservation(
            out int terminalKind,
            out Vector3 terminalPosition,
            out float distance,
            out float interactionRadius)
        {
            bool found = TryGetNearestHubTerminal(
                out HubTerminalMode mode,
                out Transform terminal,
                out distance,
                out interactionRadius);
            terminalKind = (int)mode;
            terminalPosition = terminal != null ? terminal.position : Vector3.zero;
            return found;
        }

        public bool TryGetContractTerminalObservation(
            out Vector3 terminalPosition,
            out float distance,
            out float interactionRadius)
        {
            terminalPosition = _terminal != null ? _terminal.position : Vector3.zero;
            distance = _terminal != null && _player != null
                ? Vector3.Distance(_player.WorldCenter, _terminal.position)
                : float.PositiveInfinity;
            interactionRadius = _hubSettings.TerminalInteractionRadius;
            return State == CourierRunState.Hub && _terminal != null && _player != null;
        }

        public bool TryGetSelectedHubOffer(out CourierContract offer)
        {
            if (!HubTerminalConfirmValid)
            {
                offer = null;
                return false;
            }
            offer = _offers[_hubTerminalSelectedIndex];
            return offer != null;
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
            // Draw at authored size under a uniform GUI scale so chrome and text shrink together.
            float hudScale = ContractHudScale;
            Matrix4x4 previousHudMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(hudScale, hudScale, 1f));
            Rect panel = new Rect(
                _settings.HudLeft / hudScale,
                _settings.HudTop / hudScale,
                _settings.HudWidth,
                ContractPanelAuthoredHeight());

            Color accent = _settings.HudAccentColor;
            Color border = Color.Lerp(_settings.HudBorderColor, accent, 0.35f);
            border.a = _settings.HudBorderColor.a;
            DuneVectorHudChrome.DrawSoftShadow(panel, _settings.HudShadowColor, _settings.HudShadowOffset, 6f);
            DuneVectorHudChrome.DrawGlassPanel(
                panel,
                _settings.HudPanelColor,
                border,
                _settings.HudBorderThickness,
                1f);
            DuneVectorHudChrome.DrawAccentRail(panel, accent, _settings.HudAccentWidth, 26f);
            Rect topRule = new Rect(
                panel.x + _settings.HudAccentWidth,
                panel.y,
                panel.width - _settings.HudAccentWidth,
                _settings.HudTopRuleHeight);
            DuneVectorHudChrome.DrawRect(topRule, WithAlpha(accent, accent.a * _settings.HudTopRuleOpacity));
            DuneVectorHudChrome.DrawVerticalFade(
                new Rect(topRule.x, topRule.yMax, topRule.width, 9f),
                WithAlpha(accent, accent.a * _settings.HudTopRuleOpacity * 0.4f),
                true);
            DuneVectorHudChrome.DrawCornerBrackets(
                panel,
                WithAlpha(accent, accent.a * 0.6f),
                _settings.HudCornerBracketLength,
                _settings.HudBorderThickness);

            float padding = _settings.HudPadding;
            float contentX = panel.x + padding + _settings.HudAccentWidth;
            float contentWidth = panel.width - (padding * 2f) - _settings.HudAccentWidth;
            Vector2 textShadowOffset = new Vector2(1f, 1f);
            Color textShadow = new Color(0f, 0f, 0f, 0.55f);
            float y = panel.y + padding;

            string modifier = ActiveContract.DisplayModifiers == CourierContractModifier.Unknown && !_unknownRevealed
                ? "UNKNOWN CARGO"
                : CourierContract.FormatModifiers(ActiveContract.GameplayModifiers);
            DuneVectorHudChrome.DrawGlowLabel(
                new Rect(contentX, y, contentWidth, _settings.HudTitleHeight),
                modifier,
                _hudTitleStyle,
                accent,
                WithAlpha(accent, accent.a * _settings.HudTitleGlowOpacity),
                _settings.HudTitleGlowRadius,
                textShadow,
                textShadowOffset);
            y += _settings.HudTitleHeight;

            int stopCount = Mathf.Max(1, ActiveContract.StopCount);
            int currentStop = Mathf.Clamp(_deliveryIndex + 1, 1, stopCount);
            string objective = State == CourierRunState.FindPackage
                ? _settings.HudPickupObjectiveLabel
                : stopCount > 1
                    ? FormatDesignerText(_settings.HudDeliverStopObjectiveFormat, currentStop, stopCount)
                    : _settings.HudDeliverObjectiveLabel;
            DuneVectorHudChrome.DrawLabel(
                new Rect(contentX, y, contentWidth, _settings.HudLineHeight),
                objective,
                _hudBodyStyle,
                _settings.HudTextColor,
                textShadow,
                textShadowOffset);
            y += _settings.HudLineHeight + _settings.HudRowGap;

            // Multi-drop only: one segment per stop so the route reads at a glance instead of
            // forcing the player to parse "2 / 3" mid-flight.
            if (stopCount > 1)
            {
                y += _settings.HudRouteBarTopPadding;
                Rect routeLabel = new Rect(contentX, y, contentWidth, _settings.HudRouteBarLabelHeight);
                DuneVectorHudChrome.DrawLabel(
                    routeLabel,
                    _settings.HudRouteLabel,
                    _hudLabelStyle,
                    _settings.HudMutedTextColor,
                    textShadow,
                    textShadowOffset);
                bool finalStop = State != CourierRunState.FindPackage && currentStop >= stopCount;
                DuneVectorHudChrome.DrawLabel(
                    routeLabel,
                    finalStop
                        ? _settings.HudRouteCompleteLabel
                        : FormatDesignerText(_settings.HudRouteStopFormat, currentStop, stopCount),
                    _hudValueStyle,
                    accent,
                    textShadow,
                    textShadowOffset);
                y += _settings.HudRouteBarLabelHeight;

                float gap = _settings.HudRouteBarGap;
                float segmentWidth = (contentWidth - (gap * (stopCount - 1))) / stopCount;
                float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * _settings.HudRouteCurrentPulseSpeed));
                for (int index = 0; index < stopCount; index++)
                {
                    Rect segment = new Rect(
                        contentX + (index * (segmentWidth + gap)),
                        y,
                        segmentWidth,
                        _settings.HudRouteBarHeight);
                    bool completed = State != CourierRunState.FindPackage && index < _deliveryIndex;
                    bool current = State != CourierRunState.FindPackage && index == _deliveryIndex;
                    Color segmentColor = completed
                        ? _settings.HudRouteCompletedColor
                        : current
                            ? Color.Lerp(accent, Color.white, pulse * _settings.HudRouteCurrentPulseAmount)
                            : _settings.HudRoutePendingColor;
                    DuneVectorHudChrome.DrawMeter(
                        segment,
                        completed || current ? 1f : 0f,
                        segmentColor,
                        _settings.HudTrackColor,
                        1f,
                        1,
                        Color.clear,
                        1f,
                        1f);
                }
                y += _settings.HudRouteBarHeight + _settings.HudRowGap;
            }

            y = DrawContractHudRow(
                contentX,
                y,
                contentWidth,
                _settings.HudRewardLabel,
                FormatDesignerText(_settings.HudRewardFormat, ActiveContract.OfferedReward),
                accent,
                textShadow,
                textShadowOffset);

            if (ActiveContract.Has(CourierContractModifier.Express))
            {
                y = DrawContractHudRow(
                    contentX,
                    y,
                    contentWidth,
                    _settings.HudTimeLabel,
                    FormatTime(ExpressTimeRemaining),
                    _settings.HudTextColor,
                    textShadow,
                    textShadowOffset);
            }

            if (State == CourierRunState.Delivering && CargoUsesIntegrity())
            {
                float normalized = Mathf.Clamp01(CargoIntegrity / 100f);
                Color integrityColor = Color.Lerp(
                    _settings.IntegrityCriticalColor,
                    _settings.IntegrityHealthyColor,
                    normalized);
                y = DrawContractHudRow(
                    contentX,
                    y,
                    contentWidth,
                    _settings.HudIntegrityLabel,
                    FormatDesignerText(_settings.HudIntegrityFormat, Mathf.CeilToInt(CargoIntegrity)),
                    integrityColor,
                    textShadow,
                    textShadowOffset);
                DuneVectorHudChrome.DrawMeter(
                    new Rect(contentX, y, contentWidth, _settings.HudRouteBarHeight),
                    normalized,
                    integrityColor,
                    _settings.HudTrackColor,
                    1f,
                    1,
                    Color.clear,
                    1f,
                    1f);
            }

            GUI.matrix = previousHudMatrix;
        }

        private float DrawContractHudRow(
            float contentX,
            float y,
            float contentWidth,
            string label,
            string value,
            Color valueColor,
            Color textShadow,
            Vector2 textShadowOffset)
        {
            Rect row = new Rect(contentX, y, contentWidth, _settings.HudRouteBarLabelHeight);
            DuneVectorHudChrome.DrawLabel(
                row,
                label,
                _hudLabelStyle,
                _settings.HudMutedTextColor,
                textShadow,
                textShadowOffset);
            DuneVectorHudChrome.DrawLabel(
                row,
                value,
                _hudValueStyle,
                valueColor,
                textShadow,
                textShadowOffset);
            return y + _settings.HudRouteBarLabelHeight + _settings.HudRowGap;
        }

        /// <summary>Panel height in authored (pre-scale) space, grown to fit whichever rows this contract shows.</summary>
        private float ContractPanelAuthoredHeight()
        {
            float height = (_settings.HudPadding * 2f) + _settings.HudTitleHeight
                + _settings.HudLineHeight + _settings.HudRowGap;
            if (IsContractActive && Mathf.Max(1, ActiveContract.StopCount) > 1)
            {
                height += _settings.HudRouteBarTopPadding + _settings.HudRouteBarLabelHeight
                    + _settings.HudRouteBarHeight + _settings.HudRowGap;
            }
            // Reward row.
            height += _settings.HudRouteBarLabelHeight + _settings.HudRowGap;
            if (IsContractActive && ActiveContract.Has(CourierContractModifier.Express))
            {
                height += _settings.HudRouteBarLabelHeight + _settings.HudRowGap;
            }
            if (State == CourierRunState.Delivering && CargoUsesIntegrity())
            {
                height += _settings.HudRouteBarLabelHeight + _settings.HudRowGap + _settings.HudRouteBarHeight;
            }
            return Mathf.Max(_settings.HudHeight, height);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private float ContractHudScale => _settings == null ? 1f : Mathf.Clamp(_settings.HudScale, 0.4f, 2f);

        public bool TryGetVisibleContractPanelRect(out Rect panel)
        {
            if (_settings != null && IsContractActive && !IsGameplayHudSuppressed)
            {
                float hudScale = ContractHudScale;
                panel = new Rect(
                    _settings.HudLeft,
                    _settings.HudTop,
                    _settings.HudWidth * hudScale,
                    ContractPanelAuthoredHeight() * hudScale);
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
            bool isFlightRace = ActiveContract.Has(CourierContractModifier.Express);
            _objectiveIndicator.Draw(
                _camera,
                ActiveObjective,
                objectiveLabel,
                distance,
                _deliverySettings,
                isFlightRace ? _deliverySettings.FlightRaceObjectiveIndicatorIcon : null,
                isFlightRace ? _deliverySettings.FlightRaceObjectiveIndicatorRadius : -1f);
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
            return DuneLandmarkNames.GetDisplayName(type);
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
            if (_hubClothResetCoroutine != null)
            {
                StopCoroutine(_hubClothResetCoroutine);
                _hubClothResetCoroutine = null;
            }
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
