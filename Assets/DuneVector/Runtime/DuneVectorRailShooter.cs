using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DuneVector
{
    public enum RailShooterPhase
    {
        Inactive,
        Entry,
        Combat,
        Boss,
        Results,
    }

    public enum RailShooterEnemyKind
    {
        SkyPiercer,
        GroundExploder,
        StormPyramid,
        StrikeOrb,
        VesperKite,
    }

    public enum RailShooterBulletPattern
    {
        Aimed,
        Spread,
        Ring,
        Spiral,
        Wall,
        Weave,
    }

    public enum RailShooterTrick
    {
        None,
        BarrelRollLeft,
        BarrelRollRight,
        Corkscrew,
        Loop,
    }

    /// <summary>
    /// Staged charge lock applied to rift satellites, mirroring the free-roam energy launcher so
    /// both weapons read the same: a target is spotted, held, and only then locked.
    /// </summary>
    public enum RailSatelliteLockState
    {
        None,
        Detected,
        Locking,
        Locked,
    }

    public enum RailShooterRoute
    {
        Signal,
        Black,
    }

    public readonly struct RailShooterCommand
    {
        public readonly Vector2 Move;
        public readonly bool BoostHeld;
        public readonly bool BrakeHeld;
        public readonly bool FirePressed;
        public readonly bool FireHeld;
        public readonly bool FireReleased;
        public readonly bool BombPressed;
        public readonly bool TrickPressed;
        public readonly bool ConfirmPressed;
        public readonly Vector2 Look;
        public readonly Vector2 Stick;

        public RailShooterCommand(in DroneRawInputFrame input)
        {
            Move = Vector2.ClampMagnitude(input.Move, 1f);
            BoostHeld = input.BoostHeld;
            BrakeHeld = input.BrakeHeld;
            FirePressed = input.FirePressed;
            FireHeld = input.FireHeld;
            FireReleased = input.FireReleased;
            BombPressed = input.BombPressed;
            TrickPressed = input.JumpPressed;
            ConfirmPressed = input.ConfirmPressed;
            Look = input.LookDelta;
            Stick = input.LookRate;
        }
    }

    [Serializable]
    public struct RailShooterSimulationState
    {
        public int Seed;
        public float Elapsed;
        public float Distance;
        public float ForwardSpeed;
        public Vector2 FlightOffset;
        public Vector2 LateralVelocity;
        public Vector2 Attitude;
        public Vector3 AimDirection;
        public float ManeuverEnergy;
        public RailShooterTrick Trick;
        public float TrickElapsed;
        public float ChargeElapsed;
        public int Bombs;
        public int Score;
        public int Combo;
        public float ComboMultiplier;
        public int Kills;
        public int ChargeKills;
        public int FormationClears;
        public int Pickups;
        public int ProjectileDeflections;
        public int Grazes;
        public int RouteGatesCleared;
        public int SigilsBroken;
        public int ChainSigilsBroken;
        public int SigilStrikes;
        public bool TookDamage;
        public RailShooterRoute Route;
    }

    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorRailShooterController : MonoBehaviour
    {
        private enum PickupKind
        {
            Gold,
            Health,
            Bomb,
        }

        private sealed class PooledProjectile
        {
            public GameObject Root;
            public Transform Transform;
            public Transform Core;
            public Transform Cage;
            public Transform Halo;
            public Renderer CoreRenderer;
            public Renderer CageRenderer;
            public Renderer HaloRenderer;
            public TrailRenderer Trail;
            public bool Active;
            public bool EnemyOwned;
            public Vector3 Velocity;
            public Vector3 Heading;
            public float Speed;
            public float Remaining;
            public float Radius;
            public float Age;
            public float ArmDuration;
            public float CurveDegreesPerSecond;
            public float WeaveSwing;
            public float WeaveFrequency;
            public float WeavePhase;
            public float PulsePhase;
            public float CoreStretch;
            public bool Grazed;
        }

        private sealed class PooledImpact
        {
            public GameObject Root;
            public Transform Transform;
            public bool Active;
            public float Elapsed;
            public float Scale;
        }

        private sealed class ScorePopup
        {
            public bool Active;
            public float Elapsed;
            public Vector3 World;
            public string Text;
            public Color Color;
        }

        private sealed class PooledPickup
        {
            public GameObject Root;
            public Transform Transform;
            public PickupKind Kind;
            public bool Active;
        }

        private sealed class RailEnemy
        {
            public GameObject Root;
            public Transform Transform;
            public Transform Visual;
            public RailShooterEnemyKind Kind;
            public bool Active;
            public bool Elite;
            public bool Boss;
            public int FormationId;
            public float Health;
            public float MaximumHealth;
            public float HitRadius;
            public float ContactRadius;
            public float HitFlashElapsed;
            public Vector3 BaseScale;
            public float Age;
            public float NextFireAt;
            public float NextSpecialAt;
            public Vector2 BaseOffset;
            public float SpawnZ;
            public int Identity;
        }

        private sealed class FormationRecord
        {
            public int Id;
            public int Remaining;
            public bool Escaped;
            public bool Awarded;
        }

        private sealed class RiftSegment
        {
            public Transform Root;
            public Vector2 PlaneOffset;
            public readonly List<Transform> Rotators = new List<Transform>();
        }

        private sealed class RailSatellite
        {
            public GameObject Root;
            public RailSatelliteLockState LockState;
            public float LockElapsed;
            public float LockGrace;
            public Transform Transform;
            public Vector3 LocalTargetOffset;
            public GameObject Explosion;
            public ParticleSystem[] ExplosionParticles;
            public bool Active;
            public bool Exploding;
            public float Health;
            public float ExplosionElapsed;
            public Vector2 PlaneOffset;
            public Vector3 RotationAxis;
            public Vector3 BaseScale;
        }

        public static bool IsAnyRailShooterActive { get; private set; }
        public bool IsActive => Phase != RailShooterPhase.Inactive;
        public RailShooterPhase Phase { get; private set; } = RailShooterPhase.Inactive;
        public RailShooterSimulationState Simulation => _state;
        public int AwardedGold { get; private set; }
        public string ResultGrade { get; private set; } = "C";

        private readonly List<PooledProjectile> _playerProjectiles = new List<PooledProjectile>();
        private readonly List<PooledProjectile> _enemyProjectiles = new List<PooledProjectile>();
        private readonly List<PooledImpact> _impacts = new List<PooledImpact>();
        private readonly List<PooledPickup> _pickups = new List<PooledPickup>();
        private readonly List<RailEnemy> _enemies = new List<RailEnemy>();
        private readonly List<FormationRecord> _formations = new List<FormationRecord>();
        private readonly List<RiftSegment> _segments = new List<RiftSegment>();
        private readonly List<Transform> _speedStreaks = new List<Transform>();
        private readonly List<Transform> _railRings = new List<Transform>();
        private readonly List<RailSatellite> _satellites = new List<RailSatellite>();
        private readonly List<ScorePopup> _popups = new List<ScorePopup>();
        private readonly List<RailEnemy> _chargeLocks = new List<RailEnemy>();
        private readonly List<RailSatellite> _satelliteChargeLocks = new List<RailSatellite>();
        private readonly List<RailSigilDefinition> _sigilDemand = new List<RailSigilDefinition>();
        private readonly List<RailSigilDefinition> _sigilCandidates = new List<RailSigilDefinition>();
        private readonly List<Vector2> _sigilGlyphPoints = new List<Vector2>();
        private readonly List<Vector2> _sigilAttemptPoints = new List<Vector2>();
        private readonly List<Vector2> _sigilEvaluationAttempt = new List<Vector2>();
        private readonly List<Vector2> _sigilEvaluationTarget = new List<Vector2>();
        private readonly List<Vector2> _sigilTargetPoints = new List<Vector2>();

        private DronePlayer _input;
        private DroneCharacterController _player;
        private DroneHealth _health;
        private Camera _camera;
        private DroneCameraController _cameraController;
        private DesertWorldStreamer _world;
        private DuneVectorMaterials _materials;
        private DroneGoldWallet _wallet;
        private RailShooterTuning _settings;
        private FlyingEnemyTuning _skyPiercerSettings;
        private StormPyramidTuning _stormSettings;
        private PlayerStrikeOrbTuning _strikeOrbSettings;
        private VesperKiteTuning _vesperSettings;
        private GroundExploderTuning _groundExploderSettings;
        private RingTuning _ringSettings;
        private System.Random _random;
        private Action<bool, int, string> _completed;
        private RailShooterSimulationState _state;

        private Transform _modeRoot;
        private Transform _environmentRoot;
        private Transform _enemyRoot;
        private Transform _projectileRoot;
        private Transform _pickupRoot;
        private Transform _effectsRoot;
        private Transform _chargeVisual;
        private Transform _chargedBeamVisual;
        private Transform _bombVisual;
        private Transform _safeGate;
        private Transform _riskGate;
        private LineRenderer _laneWarning;
        private RailEnemy _boss;
        private RailEnemy _chargeLock;
        private RailSatellite _satelliteChargeLock;

        private Vector3 _arenaOrigin;
        private float _startZ;
        private float _furthestSegmentZ;
        private float _furthestSatelliteZ;
        private Vector2 _planeRotation;
        private float _phaseElapsed;
        private float _nextWaveDistance;
        private float _nextPickupDistance;
        private float _nextRegularShotAt;
        private float _comboExpiresAt;
        private float _nextDamageAt;
        private float _bombElapsed = float.PositiveInfinity;
        private float _chargedBeamElapsed = float.PositiveInfinity;
        private float _laneElapsed = float.PositiveInfinity;
        private float _laneCenterX;
        private bool _laneDamageApplied;
        private bool _fireWasHeld;
        private bool _routeGateActive;
        private int _nextRouteGateIndex;
        private int _waveIndex;
        private int _formationSequence;
        private int _pickupSequence;
        private int _riskRouteCount;
        private int _killsSinceDrop;
        private int _distanceGoldBonus;
        private bool _resultSuccess;
        private bool _rewardCommitted;
        private readonly List<RailShooterBulletPattern> _patternCandidates =
            new List<RailShooterBulletPattern>(6);
        private Material[] _bulletCoreMaterials;
        private Material[] _bulletGlowMaterials;
        private Material _playerBoltCoreMaterial;
        private Material _playerBoltGlowMaterial;
        private float _bossNextRingAt;
        private float _bossNextSpiralAt;
        private float _bossNextWallAt;
        private float _bossSpiralBurstEndsAt;
        private float _bossSpiralNextShotAt;
        private float _bossSpiralAngle;
        private int _bossSpiralDirection = 1;
        private float _nextGrazePopupAt;
        private float _cameraShake;
        private float _fovImpulse;
        private int _difficulty = 1;
        private Vector3 _cameraBasePosition;
        private Vector2 _aimViewport = new Vector2(0.5f, 0.5f);
        private float _hitMarkerElapsed = float.PositiveInfinity;
        private float _killMarkerElapsed = float.PositiveInfinity;
        private float _damageFlashElapsed = float.PositiveInfinity;
        private float _lastHitCueAt = float.NegativeInfinity;
        private bool _chargeReadyCued;
        private bool _bossAnnounced;
        private bool _hasApplicationFocus = true;
        private bool _applicationPaused;
        private bool _skipResumeFrame;
        private float _bossBannerElapsed = float.PositiveInfinity;
        private Transform _sigilRoot;
        private Transform _sigilCage;
        private Transform _sigilHalo;
        private Transform _sigilDrawingCursor;
        private bool _sigilActive;
        private bool _sigilChain;
        private bool _sigilDrawing;
        private bool _sigilVerdictBroken;
        private float _sigilElapsed;
        private float _sigilDuration;
        private Vector2 _sigilPlaneOffset;
        private Vector2 _sigilCursorScreen;
        private int _sigilSymbolIndex;
        private int _sigilAttackCount;
        private int _sigilChainCycle;
        private float _sigilNextAttackDistance;
        private float _sigilNextBossAttackAt = float.PositiveInfinity;
        private float _sigilFaultElapsed = float.PositiveInfinity;
        private float _lastSigilFaultCueAt = float.NegativeInfinity;
        private float _sigilVerdictElapsed = float.PositiveInfinity;
        private Component _massiveClouds;
        private readonly List<object> _savedMassiveCloudParameters = new List<object>();

        private Vector3 _savedPlayerPosition;
        private Quaternion _savedPlayerRotation;
        private Vector3 _savedVisualScale;
        private Vector3 _authoredVisualScale = Vector3.one;
        private bool _savedMotorEnabled;
        private bool _savedInputEnabled;
        private bool _savedWorldEnabled;
        private bool _savedCameraControllerEnabled;
        private Vector3 _savedCameraPosition;
        private Quaternion _savedCameraRotation;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackgroundColor;
        private float _savedFieldOfView;
        private Material _savedSkybox;
        private bool _savedFogEnabled;
        private FogMode _savedFogMode;
        private Color _savedFogColor;
        private float _savedFogStartDistance;
        private float _savedFogEndDistance;

        private GUIStyle _smallStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _resultStyle;
        private GUIStyle _popupStyle;
        private GUIStyle _centeredSmallStyle;
        private GUIStyle _statLabelStyle;
        private GUIStyle _statValueStyle;
        private GUIStyle _sigilCountdownStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _gradeStyle;
        private float _hudScale = 1f;
        private float _hudStyleScale = -1f;
        private float _scoreDisplay;
        private float _hullGhost = 1f;
        private float _hullGhostHold;
        private float _bossGhost = 1f;
        private float _bossGhostHold;

        public void Initialize(
            DronePlayer input,
            DroneCharacterController player,
            DroneHealth health,
            Camera camera,
            DroneCameraController cameraController,
            DesertWorldStreamer world,
            DuneVectorMaterials materials,
            DroneGoldWallet wallet,
            RailShooterTuning settings,
            FlyingEnemyTuning skyPiercerSettings,
            StormPyramidTuning stormSettings,
            PlayerStrikeOrbTuning strikeOrbSettings,
            VesperKiteTuning vesperSettings,
            GroundExploderTuning groundExploderSettings,
            RingTuning ringSettings,
            Vector3 authoredVisualScale)
        {
            _input = input;
            _player = player;
            _health = health;
            _camera = camera;
            _cameraController = cameraController;
            _world = world;
            _materials = materials;
            _wallet = wallet;
            _settings = settings ?? new RailShooterTuning();
            _settings.EnsureInitialized();
            _skyPiercerSettings = skyPiercerSettings ?? new FlyingEnemyTuning();
            _stormSettings = stormSettings ?? new StormPyramidTuning();
            _strikeOrbSettings = strikeOrbSettings ?? new PlayerStrikeOrbTuning();
            _vesperSettings = vesperSettings ?? new VesperKiteTuning();
            _groundExploderSettings = groundExploderSettings ?? new GroundExploderTuning();
            _ringSettings = ringSettings ?? new RingTuning();
            _authoredVisualScale = authoredVisualScale;
            BuildPooledMode();
        }

        public bool Begin(int seed, int difficulty, Action<bool, int, string> completed)
        {
            if (!_settings.Enabled || IsActive || _player == null || _camera == null || _health == null)
            {
                return false;
            }

            _completed = completed;
            _hasApplicationFocus = Application.isFocused;
            _applicationPaused = false;
            _skipResumeFrame = false;
            _input?.ClearCapturedInput();
            _difficulty = Mathf.Clamp(difficulty, 1, Mathf.Max(1, _settings.DifficultyCeiling));
            _random = new System.Random(unchecked(seed ^ _settings.SeedOffset ^ (difficulty * 73856093)));
            _planeRotation = new Vector2(NextFloat(0f, 1f), NextFloat(0f, 1f));
            SaveWorldState();
            _arenaOrigin = new Vector3(_savedPlayerPosition.x, _settings.RiftWorldAltitude, _savedPlayerPosition.z);
            _startZ = _arenaOrigin.z;
            _state = new RailShooterSimulationState
            {
                Seed = seed,
                ForwardSpeed = _settings.ForwardSpeed,
                ManeuverEnergy = _settings.ManeuverEnergyCapacity,
                Bombs = _settings.StartingBombs,
                ComboMultiplier = 1f,
                Route = RailShooterRoute.Signal,
                AimDirection = Vector3.forward,
            };
            _phaseElapsed = 0f;
            _nextWaveDistance = _settings.FirstWaveDistance;
            _nextPickupDistance = _settings.PickupSpacing;
            _nextRegularShotAt = 0f;
            _comboExpiresAt = float.NegativeInfinity;
            _nextDamageAt = float.NegativeInfinity;
            _bombElapsed = float.PositiveInfinity;
            _chargedBeamElapsed = float.PositiveInfinity;
            _laneElapsed = float.PositiveInfinity;
            _nextGrazePopupAt = float.NegativeInfinity;
            ResetBossBulletPatterns();
            _fireWasHeld = false;
            _routeGateActive = false;
            _nextRouteGateIndex = 0;
            _waveIndex = 0;
            _formationSequence = 0;
            _pickupSequence = 0;
            _riskRouteCount = 0;
            _killsSinceDrop = 0;
            _rewardCommitted = false;
            _resultSuccess = false;
            _cameraShake = 0f;
            _fovImpulse = 0f;
            _aimViewport = new Vector2(0.5f, 0.5f);
            _hitMarkerElapsed = float.PositiveInfinity;
            _killMarkerElapsed = float.PositiveInfinity;
            _damageFlashElapsed = float.PositiveInfinity;
            _lastHitCueAt = float.NegativeInfinity;
            _chargeReadyCued = false;
            _bossAnnounced = false;
            _bossBannerElapsed = float.PositiveInfinity;
            _chargeLocks.Clear();
            _satelliteChargeLocks.Clear();
            _chargeLock = null;
            _satelliteChargeLock = null;
            _distanceGoldBonus = 0;
            AwardedGold = 0;
            ResultGrade = "C";
            _scoreDisplay = 0f;
            _hullGhost = 1f;
            _hullGhostHold = 0f;
            _bossGhost = 1f;
            _bossGhostHold = 0f;
            ResetPools();
            ResetCourse();
            ResetSigilDuel();
            EnterRailPresentation();
            _modeRoot.gameObject.SetActive(true);
            DuneVectorAudioManager.Instance?.EnterRailSubgameMusic();
            Phase = RailShooterPhase.Entry;
            IsAnyRailShooterActive = true;
            _health.TemporaryHealthPoolDepleted += HandleTemporaryHullDepleted;
            _health.Damaged += HandlePlayerDamaged;
            _health.BeginTemporaryHealthPool(
                _settings.TemporaryHull + (DifficultyLevels() * _settings.DifficultyHullPerLevel));
            return true;
        }

        private void Update()
        {
            if (!IsActive || Phase == RailShooterPhase.Inactive)
            {
                return;
            }

            if (!_hasApplicationFocus || _applicationPaused || !Application.isFocused)
            {
                return;
            }

            if (_skipResumeFrame)
            {
                _skipResumeFrame = false;
                _input?.ClearCapturedInput();
                _fireWasHeld = false;
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            RailShooterCommand command = new RailShooterCommand(_input != null ? _input.CurrentCommand : default);
            _phaseElapsed += deltaTime;
            _state.Elapsed += deltaTime;
            TickHudPresentation(deltaTime);
            if (Phase != RailShooterPhase.Results)
            {
                TickFlight(command, deltaTime);
                TickEnvironment(deltaTime);
                TickRouteGates();
                if (Phase == RailShooterPhase.Combat || Phase == RailShooterPhase.Boss)
                {
                    TickCoursePickups();
                }
                TickPickups(deltaTime);
                TickWeapons(command, deltaTime);
                TickEnemies(deltaTime);
                TickProjectiles(deltaTime);
                TickLaneAttack(deltaTime);
                TickSigilDuel(command, deltaTime);
                TickImpacts(deltaTime);
                TickPresentation(deltaTime);
            }

            switch (Phase)
            {
                case RailShooterPhase.Entry:
                    if (_phaseElapsed >= _settings.EntryHoldDuration)
                    {
                        Phase = RailShooterPhase.Combat;
                        _phaseElapsed = 0f;
                    }
                    break;
                case RailShooterPhase.Combat:
                    TickEncounterDirector();
                    break;
                case RailShooterPhase.Boss:
                    TickBoss(deltaTime);
                    break;
                case RailShooterPhase.Results:
                    TickResults(command);
                    break;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            bool wasSuspended = !_hasApplicationFocus || _applicationPaused;
            _hasApplicationFocus = hasFocus;
            HandleApplicationSuspensionChange(wasSuspended);
            if (hasFocus && IsActive && Time.timeScale > 0f)
            {
                ApplyActiveRailCursorState();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            bool wasSuspended = !_hasApplicationFocus || _applicationPaused;
            _applicationPaused = pauseStatus;
            HandleApplicationSuspensionChange(wasSuspended);
        }

        private void HandleApplicationSuspensionChange(bool wasSuspended)
        {
            if (!IsActive)
            {
                return;
            }

            _input?.ClearCapturedInput();
            _fireWasHeld = false;
            CancelSigilDrawingAttempt();
            bool isSuspended = !_hasApplicationFocus || _applicationPaused;
            if (wasSuspended && !isSuspended)
            {
                _skipResumeFrame = true;
            }
        }

        private void TickFlight(in RailShooterCommand command, float deltaTime)
        {
            bool maneuverAvailable = _state.ManeuverEnergy > 0f;
            bool boosting = command.BoostHeld && !command.BrakeHeld && maneuverAvailable;
            bool braking = command.BrakeHeld && !command.BoostHeld && maneuverAvailable;
            float targetSpeed = _settings.ForwardSpeed;
            if (boosting)
            {
                targetSpeed *= _settings.BoostSpeedMultiplier;
                _state.ManeuverEnergy -= _settings.BoostEnergyPerSecond * deltaTime;
            }
            else if (braking)
            {
                targetSpeed *= _settings.BrakeSpeedMultiplier;
                _state.ManeuverEnergy -= _settings.BrakeEnergyPerSecond * deltaTime;
            }
            else if (_state.Trick == RailShooterTrick.None)
            {
                _state.ManeuverEnergy += _settings.ManeuverEnergyRegeneration * deltaTime;
            }
            _state.ManeuverEnergy = Mathf.Clamp(_state.ManeuverEnergy, 0f, _settings.ManeuverEnergyCapacity);
            _state.ForwardSpeed = Mathf.Lerp(
                _state.ForwardSpeed,
                targetSpeed,
                DuneVectorMath.Sharpness(_settings.ForwardSpeedSharpness, deltaTime));

            bool steering = command.Move.sqrMagnitude >
                _settings.RecenterInputDeadzone * _settings.RecenterInputDeadzone;
            Vector2 targetVelocity = steering
                ? command.Move * _settings.LateralSpeed
                : Vector2.zero;
            _state.LateralVelocity = Vector2.Lerp(
                _state.LateralVelocity,
                targetVelocity,
                DuneVectorMath.Sharpness(_settings.LateralAccelerationSharpness, deltaTime));
            _state.FlightOffset += _state.LateralVelocity * deltaTime;
            RebaseFlightSpaceIfNeeded();
            float attitudeSharpness = steering
                ? _settings.AttitudeInputSharpness
                : _settings.AttitudeReturnSharpness;
            _state.Attitude = Vector2.Lerp(
                _state.Attitude,
                steering ? command.Move : Vector2.zero,
                DuneVectorMath.Sharpness(attitudeSharpness, deltaTime));

            if (command.TrickPressed && _state.Trick == RailShooterTrick.None)
            {
                TryBeginTrick(command.Move);
            }
            TickTrick(deltaTime);

            _state.Distance += _state.ForwardSpeed * deltaTime;
            Vector3 playerPosition = _arenaOrigin + new Vector3(
                _state.FlightOffset.x,
                _state.FlightOffset.y,
                _state.Distance);
            float cameraFollow = Mathf.Clamp01(_settings.CameraLateralFollowFraction);
            Vector3 cameraAnchor = new Vector3(
                Mathf.Lerp(_arenaOrigin.x, playerPosition.x, cameraFollow),
                Mathf.Lerp(_arenaOrigin.y, playerPosition.y, cameraFollow),
                _arenaOrigin.z + _state.Distance);
            Vector3 desiredCameraPosition = cameraAnchor + _settings.CameraLocalOffset;
            _cameraBasePosition = Vector3.Lerp(
                _cameraBasePosition,
                desiredCameraPosition,
                DuneVectorMath.Sharpness(_settings.CameraPositionSharpness, deltaTime));
            _camera.transform.position = _cameraBasePosition +
                (UnityEngine.Random.insideUnitSphere * _cameraShake);
            _camera.transform.rotation = Quaternion.identity;

            Vector2 restingViewport = CalculateRestingAimViewport(
                _state.FlightOffset * (1f - cameraFollow),
                _settings.FlightBounds,
                _settings.RestingAimRegionFraction);
            _aimViewport = restingViewport + Vector2.Scale(
                _state.Attitude,
                _settings.SteeringAimViewportSwing);
            Ray aimRay = _camera.ViewportPointToRay(new Vector3(_aimViewport.x, _aimViewport.y, 0f));
            Quaternion aimRotation = Quaternion.LookRotation(aimRay.direction, Vector3.up);
            // Weapons and reticles follow the aim ray exactly. The cosmetic bank, pitch, and
            // trick spin stay on the hull so shots never leave the crosshair.
            _state.AimDirection = aimRotation * Vector3.forward;
            Quaternion trickRotation = GetTrickRotation();
            Quaternion shipRotation = aimRotation * trickRotation * Quaternion.Euler(
                -_state.Attitude.y * _settings.MaximumPitch,
                _state.Attitude.x * _settings.MaximumYaw,
                -_state.Attitude.x * _settings.MaximumBank);
            _player.transform.SetPositionAndRotation(playerPosition, shipRotation);

            float targetFov = _settings.CameraFieldOfView +
                (boosting ? _settings.BoostFieldOfView : 0f) +
                _fovImpulse;
            _camera.fieldOfView = Mathf.Lerp(
                _camera.fieldOfView,
                targetFov,
                DuneVectorMath.Sharpness(_settings.FieldOfViewSharpness, deltaTime));
            _cameraShake = Mathf.MoveTowards(_cameraShake, 0f, _settings.CameraShakeDecay * deltaTime);
            _fovImpulse = Mathf.MoveTowards(_fovImpulse, 0f, _settings.FieldOfViewSharpness * deltaTime);
        }

        private void RebaseFlightSpaceIfNeeded()
        {
            float threshold = Mathf.Max(1f, _settings.FlightRebaseDistance);
            if (Mathf.Abs(_state.FlightOffset.x) < threshold &&
                Mathf.Abs(_state.FlightOffset.y) < threshold)
            {
                return;
            }

            Vector3 shift = new Vector3(_state.FlightOffset.x, _state.FlightOffset.y, 0f);
            if (_modeRoot != null)
            {
                _modeRoot.position -= shift;
            }
            _cameraBasePosition -= shift;
            _player.HandleWorldShift(-shift);
            if (_player.DroneVisualRoot != null)
            {
                DroneTrailHorizontalEmissionGate[] trailGates =
                    _player.DroneVisualRoot.GetComponentsInChildren<DroneTrailHorizontalEmissionGate>(true);
                for (int i = 0; i < trailGates.Length; i++)
                {
                    trailGates[i].ResetAfterTeleport();
                }
            }
            for (int i = 0; i < _popups.Count; i++)
            {
                if (_popups[i].Active)
                {
                    _popups[i].World -= shift;
                }
            }
            _laneCenterX -= shift.x;
            _state.FlightOffset = Vector2.zero;
        }

        private void TryBeginTrick(Vector2 move)
        {
            RailShooterTrick trick;
            float cost;
            if (move.y > 0.45f)
            {
                trick = RailShooterTrick.Loop;
                cost = _settings.LoopEnergy;
            }
            else if (Mathf.Abs(move.x) > 0.35f)
            {
                trick = move.x < 0f
                    ? RailShooterTrick.BarrelRollLeft
                    : RailShooterTrick.BarrelRollRight;
                cost = _settings.BarrelRollEnergy;
            }
            else
            {
                trick = RailShooterTrick.Corkscrew;
                cost = _settings.BarrelRollEnergy;
            }

            if (_state.ManeuverEnergy < cost)
            {
                return;
            }
            _state.ManeuverEnergy -= cost;
            _state.Trick = trick;
            _state.TrickElapsed = 0f;
        }

        private void TickTrick(float deltaTime)
        {
            if (_state.Trick == RailShooterTrick.None)
            {
                return;
            }
            _state.TrickElapsed += deltaTime;
            float duration = GetTrickDuration() + Mathf.Max(0f, _settings.TrickRecoveryDuration);
            if (_state.TrickElapsed >= duration)
            {
                _state.Trick = RailShooterTrick.None;
                _state.TrickElapsed = 0f;
            }
        }

        private Quaternion GetTrickRotation()
        {
            if (_state.Trick == RailShooterTrick.None)
            {
                return Quaternion.identity;
            }
            float activeDuration = GetTrickDuration();
            float normalized = Mathf.Clamp01(_state.TrickElapsed / activeDuration);
            float turn = normalized * 360f;
            Quaternion rotation = _state.Trick switch
            {
                RailShooterTrick.BarrelRollLeft => Quaternion.Euler(0f, 0f, turn),
                RailShooterTrick.BarrelRollRight => Quaternion.Euler(0f, 0f, -turn),
                RailShooterTrick.Corkscrew => Quaternion.Euler(
                    Mathf.Sin(normalized * Mathf.PI * 2f) * _settings.MaximumPitch,
                    0f,
                    turn * 1.5f),
                RailShooterTrick.Loop => Quaternion.Euler(-turn, 0f, 0f),
                _ => Quaternion.identity,
            };
            if (_state.TrickElapsed <= activeDuration)
            {
                return rotation;
            }
            float recoveryDuration = Mathf.Max(0.001f, _settings.TrickRecoveryDuration);
            float recovery = Mathf.Clamp01((_state.TrickElapsed - activeDuration) / recoveryDuration);
            return Quaternion.Slerp(rotation, Quaternion.identity, Mathf.SmoothStep(0f, 1f, recovery));
        }

        private float GetTrickDuration()
        {
            return _state.Trick switch
            {
                RailShooterTrick.Corkscrew => _settings.CorkscrewDuration,
                RailShooterTrick.Loop => _settings.LoopDuration,
                _ => _settings.BarrelRollDuration,
            };
        }

        private bool IsTrickInvulnerable()
        {
            if (_state.Trick == RailShooterTrick.None)
            {
                return false;
            }
            return _state.TrickElapsed / Mathf.Max(0.01f, GetTrickDuration()) <=
                _settings.TrickInvulnerabilityFraction;
        }

        private void TickEncounterDirector()
        {
            if (!_bossAnnounced &&
                _state.Distance >= _settings.BossSpawnDistance - _settings.BossApproachLeadDistance)
            {
                _bossAnnounced = true;
                _bossBannerElapsed = 0f;
            }

            TickSigilDirector();
            if (_state.Distance >= _settings.BossSpawnDistance)
            {
                BeginBoss();
            }
        }

        private void TickCoursePickups()
        {
            while (_state.Distance >= _nextPickupDistance)
            {
                SpawnCoursePickup((PickupKind)(_pickupSequence % 3), null);
                _pickupSequence++;
                _nextPickupDistance += _settings.PickupSpacing;
            }
        }

        private void SpawnFormation()
        {
            RailShooterEnemyKind kind = (RailShooterEnemyKind)(_waveIndex % 5);
            bool elite = (_waveIndex + 1) % Mathf.Max(1, _settings.EliteEveryWaves) == 0;
            int baseCount = Mathf.RoundToInt(
                NextInt(_settings.FormationMinimumSize, _settings.FormationMaximumSize + 1) *
                DifficultyCountMultiplier());
            baseCount = Mathf.Max(_settings.FormationMinimumSize, baseCount);
            int riskExtra = Mathf.CeilToInt(baseCount *
                (Mathf.Pow(_settings.RiskRouteEnemyMultiplier, _riskRouteCount) - 1f));
            int requestedCount = baseCount + riskExtra;
            FormationRecord formation = new FormationRecord { Id = ++_formationSequence };
            _formations.Add(formation);
            int pattern = _waveIndex % 4;
            for (int i = 0; i < requestedCount; i++)
            {
                RailEnemy enemy = AcquireEnemy(kind);
                if (enemy == null)
                {
                    break;
                }
                Vector2 formationOffset = FormationOffset(pattern, i, requestedCount);
                ActivateEnemy(
                    enemy,
                    formation.Id,
                    formationOffset,
                    elite,
                    _player.transform.position.z + _settings.EnemySpawnAheadDistance + (i * 2f));
                formation.Remaining++;
            }
            _waveIndex++;
        }

        private Vector2 FormationOffset(int pattern, int index, int count)
        {
            float centerIndex = index - ((count - 1) * 0.5f);
            return pattern switch
            {
                0 => new Vector2(
                    centerIndex * (_settings.FormationWidth / Mathf.Max(1, count - 1)),
                    -Mathf.Abs(centerIndex) * (_settings.FormationHeight / Mathf.Max(1, count))),
                1 => new Vector2(
                    centerIndex * (_settings.FormationWidth / Mathf.Max(1, count - 1)),
                    Mathf.Sin(index * Mathf.PI) * _settings.FormationHeight),
                2 => new Vector2(
                    Mathf.Sin((index / (float)Mathf.Max(1, count)) * Mathf.PI * 2f) *
                        (_settings.FormationWidth * 0.5f),
                    Mathf.Cos((index / (float)Mathf.Max(1, count)) * Mathf.PI * 2f) *
                        (_settings.FormationHeight * 0.5f)),
                _ => new Vector2(
                    (index % 2 == 0 ? -1f : 1f) * _settings.FormationWidth * 0.35f,
                    centerIndex * (_settings.FormationHeight / Mathf.Max(1, count - 1))),
            };
        }

        private void ActivateEnemy(
            RailEnemy enemy,
            int formationId,
            Vector2 offset,
            bool elite,
            float spawnZ)
        {
            enemy.Active = true;
            enemy.Elite = elite;
            enemy.FormationId = formationId;
            enemy.Age = 0f;
            enemy.BaseOffset = offset;
            enemy.SpawnZ = spawnZ;
            enemy.MaximumHealth = _settings.EnemyHealth * DifficultyHealthMultiplier() *
                (elite ? _settings.EliteHealthMultiplier : 1f);
            enemy.Health = enemy.MaximumHealth;
            float fireInterval = ScaledEnemyFireInterval();
            enemy.NextFireAt = _settings.EnemyEntryDuration + NextFloat(0f, fireInterval);
            enemy.NextSpecialAt = _settings.EnemyEntryDuration + fireInterval;
            enemy.HitRadius = HitRadiusForKind(enemy.Kind) *
                (elite ? _settings.EliteHitRadiusMultiplier : 1f);
            enemy.ContactRadius = enemy.HitRadius * _settings.ContactRadiusFraction;
            enemy.HitFlashElapsed = float.PositiveInfinity;
            enemy.BaseScale = elite ? Vector3.one * 1.35f : Vector3.one;
            enemy.Transform.position = _arenaOrigin + new Vector3(offset.x, offset.y, spawnZ - _startZ);
            enemy.Transform.localScale = enemy.BaseScale;
            enemy.Root.SetActive(true);
        }

        private void TickEnemies(float deltaTime)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (!enemy.Active)
                {
                    continue;
                }
                TickEnemy(enemy, deltaTime);
            }
        }

        private void TickEnemy(RailEnemy enemy, float deltaTime)
        {
            enemy.Age += deltaTime;
            if (enemy.Boss)
            {
                return;
            }

            float engagement = Mathf.Clamp01(
                (enemy.Age - _settings.EnemyEntryDuration) /
                Mathf.Max(0.01f, _settings.EnemyEngagementDuration));
            float travel = _state.ForwardSpeed * 0.62f * deltaTime;
            Vector3 position = enemy.Transform.position + (Vector3.forward * travel);
            float wave = enemy.Age * _settings.EnemyStrafeFrequency + (enemy.Identity * 0.71f);
            switch (enemy.Kind)
            {
                case RailShooterEnemyKind.SkyPiercer:
                    position.x = _arenaOrigin.x + enemy.BaseOffset.x +
                        (Mathf.Sin(wave) * _settings.EnemyStrafeAmplitude);
                    position.y = _arenaOrigin.y + enemy.BaseOffset.y -
                        (Mathf.Abs(Mathf.Sin(wave * 0.63f)) * _settings.FormationHeight * engagement);
                    break;
                case RailShooterEnemyKind.GroundExploder:
                    position.x = _arenaOrigin.x + enemy.BaseOffset.x;
                    position.y = _arenaOrigin.y - (_settings.FlightBounds.y * 0.85f) +
                        (Mathf.Sin(wave) * _settings.EnemyStrafeAmplitude * 0.15f);
                    break;
                case RailShooterEnemyKind.StormPyramid:
                    position.x = _arenaOrigin.x + enemy.BaseOffset.x +
                        (Mathf.Sin(wave * 0.45f) * _settings.EnemyStrafeAmplitude * 0.55f);
                    position.y = _arenaOrigin.y + (_settings.FlightBounds.y * 0.72f) + enemy.BaseOffset.y;
                    break;
                case RailShooterEnemyKind.StrikeOrb:
                    position.x = _arenaOrigin.x + enemy.BaseOffset.x +
                        (Mathf.Cos(wave) * _settings.EnemyStrafeAmplitude * 0.6f);
                    position.y = _arenaOrigin.y + enemy.BaseOffset.y +
                        (Mathf.Sin(wave) * _settings.EnemyStrafeAmplitude * 0.45f);
                    break;
                case RailShooterEnemyKind.VesperKite:
                    position.x = _arenaOrigin.x + enemy.BaseOffset.x +
                        (Mathf.Cos(wave * 0.55f) * _settings.EnemyStrafeAmplitude);
                    position.y = _arenaOrigin.y + enemy.BaseOffset.y +
                        (Mathf.Sin(wave * 0.55f) * _settings.EnemyStrafeAmplitude * 0.5f);
                    break;
            }
            enemy.Transform.position = position;
            Vector3 facing = _player.transform.position - position;
            if (facing.sqrMagnitude > 0.001f)
            {
                enemy.Transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            }
            if (enemy.Visual != null)
            {
                enemy.Visual.Rotate(0f, _settings.WreckageRotationSpeed * deltaTime, 0f, Space.Self);
            }
            TickEnemyHitFlash(enemy, deltaTime);

            float relativeZ = position.z - _player.transform.position.z;
            if (enemy.Kind == RailShooterEnemyKind.GroundExploder &&
                relativeZ <= _settings.MineTriggerDistance && relativeZ > -_settings.MineExplosionRadius)
            {
                float planar = Vector2.Distance(
                    new Vector2(position.x, position.y),
                    new Vector2(_player.transform.position.x, _player.transform.position.y));
                if (planar <= _settings.MineExplosionRadius)
                {
                    DamagePlayer(_settings.MineDamage, "Rift Ground Exploder mine");
                    SpawnImpact(position, _settings.MineExplosionRadius);
                    EscapeEnemy(enemy);
                    return;
                }
            }

            if (enemy.Age >= enemy.NextFireAt && relativeZ > 0f &&
                enemy.Kind != RailShooterEnemyKind.GroundExploder)
            {
                if (enemy.Kind == RailShooterEnemyKind.StormPyramid &&
                    !float.IsFinite(_laneElapsed))
                {
                    BeginLaneAttack(position.x);
                }
                else
                {
                    FireEnemyPattern(enemy);
                }
                enemy.NextFireAt += ScaledEnemyFireInterval();
            }

            if (Vector3.Distance(position, _player.transform.position) <=
                enemy.ContactRadius + _settings.PlayerCollisionRadius)
            {
                DamagePlayer(_settings.EnemyContactDamage, $"{enemy.Kind} rail collision");
                EscapeEnemy(enemy);
                return;
            }
            if (relativeZ < -_settings.EnemyDespawnBehindDistance ||
                enemy.Age > _settings.EnemyEntryDuration + _settings.EnemyEngagementDuration +
                    _settings.EnemyExitDuration)
            {
                EscapeEnemy(enemy);
            }
        }

        private void EscapeEnemy(RailEnemy enemy)
        {
            enemy.Active = false;
            enemy.Root.SetActive(false);
            FormationRecord formation = FindFormation(enemy.FormationId);
            if (formation != null)
            {
                formation.Escaped = true;
                formation.Remaining = Mathf.Max(0, formation.Remaining - 1);
            }
        }

        private void BeginBoss()
        {
            Phase = RailShooterPhase.Boss;
            _phaseElapsed = 0f;
            _boss.Active = true;
            _boss.Boss = true;
            _boss.Elite = true;
            _boss.MaximumHealth = _settings.BossHealth;
            _boss.Health = _boss.MaximumHealth;
            _boss.HitRadius = _settings.BossHitRadius;
            _boss.ContactRadius = _settings.BossCollisionRadius;
            _boss.HitFlashElapsed = float.PositiveInfinity;
            _boss.BaseScale = Vector3.one;
            _boss.Age = 0f;
            _bossBannerElapsed = 0f;
            PlayCue(_settings.BossSpawnEvent, _player.transform.position);
            _boss.NextFireAt = _settings.BossFireInterval;
            _boss.NextSpecialAt = _settings.BossLaneAttackInterval;
            ResetBossBulletPatterns();
            _sigilNextBossAttackAt = _state.Elapsed + _settings.Sigils.BossAttackInterval;
            _boss.Root.SetActive(true);
        }

        private void TickBoss(float deltaTime)
        {
            if (_boss == null || !_boss.Active)
            {
                return;
            }
            _boss.Age += deltaTime;
            float health01 = Mathf.Clamp01(_boss.Health / Mathf.Max(1f, _boss.MaximumHealth));
            int phase = health01 <= _settings.BossPhaseThreeHealthFraction
                ? 3
                : health01 <= _settings.BossPhaseTwoHealthFraction ? 2 : 1;
            float orbit = _boss.Age * _settings.EnemyStrafeFrequency * (0.5f + (phase * 0.18f));
            Vector3 bossPosition = _player.transform.position + new Vector3(
                Mathf.Sin(orbit) * _settings.FormationWidth * 0.45f,
                Mathf.Cos(orbit * 0.7f) * _settings.FormationHeight * 0.4f,
                _settings.EnemySpawnAheadDistance * 0.72f);
            _boss.Transform.position = bossPosition;
            _boss.Transform.rotation = Quaternion.LookRotation(
                (_player.transform.position - bossPosition).normalized,
                Vector3.up);
            if (_boss.Visual != null)
            {
                float pulse = 1f + ((1f - health01) * 0.18f * Mathf.Sin(_boss.Age * 8f));
                _boss.Visual.localScale = Vector3.one * pulse;
                _boss.Visual.Rotate(0f, _settings.WreckageRotationSpeed * phase * deltaTime, 0f, Space.Self);
            }
            if (_boss.Age >= _boss.NextFireAt)
            {
                FireAimedBullets(
                    _boss,
                    _settings.BossProjectileFanCount + ((phase - 1) * 2),
                    _settings.BossProjectileFanAngle);
                _boss.NextFireAt += _settings.BossFireInterval / (1f + ((phase - 1) * 0.25f));
            }
            TickBossBulletPatterns(phase, deltaTime);
            if (_boss.Age >= _boss.NextSpecialAt && !float.IsFinite(_laneElapsed))
            {
                BeginLaneAttack(PredictLaneInterceptX());
                _boss.NextSpecialAt += _settings.BossLaneAttackInterval / (1f + ((phase - 1) * 0.2f));
            }
            if (Vector3.Distance(bossPosition, _player.transform.position) <=
                _boss.ContactRadius + _settings.PlayerCollisionRadius)
            {
                DamagePlayer(_settings.BossContactDamage, "Vesper Sovereign hull grind");
            }
            TickSigilDirector();
        }

        private void TickWeapons(in RailShooterCommand command, float deltaTime)
        {
            if (_sigilActive)
            {
                _state.ChargeElapsed = 0f;
                _chargeLock = null;
                _satelliteChargeLock = null;
                _chargeReadyCued = false;
                _chargeLocks.Clear();
                ClearSatelliteLocks();
                _fireWasHeld = false;
                if (command.BombPressed && _state.Bombs > 0 && !float.IsFinite(_bombElapsed))
                {
                    DetonateBomb();
                }
                return;
            }
            if (command.FireHeld)
            {
                _state.ChargeElapsed += deltaTime;
                if (_state.ChargeElapsed <= _settings.RegularFireBeforeChargeDuration &&
                    _state.Elapsed >= _nextRegularShotAt)
                {
                    FireRegularShot();
                    _nextRegularShotAt = _state.Elapsed + _settings.RegularShotInterval;
                }
                if (_state.ChargeElapsed >= _settings.ChargeMinimumDuration)
                {
                    UpdateChargeLock(deltaTime);
                    _cameraShake = Mathf.Max(
                        _cameraShake,
                        _settings.ChargeCameraShake * ChargeNormalized());
                }
                if (!_chargeReadyCued && _state.ChargeElapsed >= _settings.ChargeFullDuration)
                {
                    _chargeReadyCued = true;
                    PlayCue(_settings.ChargeReadyEvent, _player.transform.position);
                }
            }
            bool released = command.FireReleased || (_fireWasHeld && !command.FireHeld);
            if (released && _state.ChargeElapsed >= _settings.ChargeMinimumDuration)
            {
                FireChargedBeam();
            }
            else if (released && _state.ChargeElapsed > _settings.RegularFireBeforeChargeDuration)
            {
                // A press let go before the charge threshold still spends as a normal bolt
                // instead of silently discarding the shot.
                FireRegularShot();
            }
            if (!command.FireHeld)
            {
                _state.ChargeElapsed = 0f;
                _chargeLock = null;
                _satelliteChargeLock = null;
                _chargeReadyCued = false;
                _chargeLocks.Clear();
                ClearSatelliteLocks();
            }
            _fireWasHeld = command.FireHeld;

            if (command.BombPressed && _state.Bombs > 0 && !float.IsFinite(_bombElapsed))
            {
                DetonateBomb();
            }
        }

        private void FireRegularShot()
        {
            PooledProjectile projectile = AcquireProjectile(_playerProjectiles);
            if (projectile == null)
            {
                return;
            }
            Vector3 direction = ResolveShotDirection();
            projectile.Active = true;
            projectile.EnemyOwned = false;
            projectile.Heading = direction;
            projectile.Speed = _settings.RegularShotSpeed;
            projectile.Transform.position = _player.transform.position + (direction * 2.2f);
            projectile.Velocity = direction * _settings.RegularShotSpeed;
            projectile.Remaining = _settings.RegularShotLifetime;
            projectile.Radius = _settings.RegularShotRadius;
            projectile.Age = 0f;
            projectile.Transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            if (projectile.Core != null)
            {
                projectile.Core.localScale = new Vector3(
                    _settings.RegularShotRadius,
                    _settings.RegularShotRadius,
                    _settings.RegularShotVisualLength);
            }
            if (projectile.Halo != null)
            {
                float boltGlow = _settings.RegularShotRadius * _settings.BulletHaloScale;
                projectile.Halo.localScale = new Vector3(
                    boltGlow,
                    boltGlow,
                    _settings.RegularShotVisualLength * 1.1f);
            }
            projectile.Root.SetActive(true);
            if (projectile.Trail != null)
            {
                projectile.Trail.Clear();
                projectile.Trail.time = _settings.BulletTrailDuration;
                projectile.Trail.widthMultiplier =
                    _settings.RegularShotRadius * _settings.BulletTrailWidthFraction;
                projectile.Trail.emitting = _settings.BulletTrailDuration > 0f;
            }
            PlayCue(_settings.FireEvent, _player.transform.position);
        }

        private Vector3 ResolveShotDirection()
        {
            Vector3 direction = _state.AimDirection.normalized;
            if (_settings.AimAssistStrength <= 0f)
            {
                return direction;
            }
            RailEnemy target = FindViewportTarget(
                _settings.AimAssistViewportRadius,
                _settings.AimAssistRange);
            if (target == null)
            {
                return direction;
            }
            Vector3 toTarget = target.Transform.position - _player.transform.position;
            if (toTarget.sqrMagnitude <= 0.001f)
            {
                return direction;
            }
            return Vector3.Slerp(
                direction,
                toTarget.normalized,
                Mathf.Clamp01(_settings.AimAssistStrength)).normalized;
        }

        private float HitRadiusForKind(RailShooterEnemyKind kind)
        {
            return kind switch
            {
                RailShooterEnemyKind.SkyPiercer => _settings.SkyPiercerHitRadius,
                RailShooterEnemyKind.GroundExploder => _settings.GroundExploderHitRadius,
                RailShooterEnemyKind.StormPyramid => _settings.StormPyramidHitRadius,
                RailShooterEnemyKind.StrikeOrb => _settings.StrikeOrbHitRadius,
                _ => _settings.VesperKiteHitRadius,
            };
        }

        private float DifficultyLevels()
        {
            return Mathf.Max(0, _difficulty - 1);
        }

        private float DifficultyHealthMultiplier()
        {
            return 1f + (DifficultyLevels() * _settings.DifficultyHealthPerLevel);
        }

        private float DifficultyCountMultiplier()
        {
            return 1f + (DifficultyLevels() * _settings.DifficultyCountPerLevel);
        }

        private float ScaledEnemyFireInterval()
        {
            float scale = 1f - (DifficultyLevels() * _settings.DifficultyFireRatePerLevel);
            return Mathf.Max(0.05f, _settings.EnemyFireInterval * Mathf.Max(0.25f, scale));
        }

        private void FireChargedBeam()
        {
            Vector3 origin = _player.transform.position;
            Vector3 direction = _state.AimDirection.normalized;
            Vector3 lockPosition = _chargeLock != null && _chargeLock.Active
                ? _chargeLock.Transform.position
                : IsSatelliteLocked(_satelliteChargeLock)
                    ? SatelliteTargetPosition(_satelliteChargeLock)
                    : origin + (direction * _settings.ChargedBeamRange);
            for (int i = 0; i < _enemies.Count; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (!enemy.Active)
                {
                    continue;
                }
                float along = Vector3.Dot(enemy.Transform.position - origin, direction);
                if (along < 0f || along > _settings.ChargedBeamRange)
                {
                    continue;
                }
                Vector3 nearest = origin + (direction * along);
                bool inBeam = Vector3.Distance(nearest, enemy.Transform.position) <=
                    _settings.ChargedBeamRadius + enemy.HitRadius;
                bool inBlast = Vector3.Distance(lockPosition, enemy.Transform.position) <=
                    _settings.ChargedBlastRadius + enemy.HitRadius;
                if (inBeam || inBlast)
                {
                    ApplyDamage(enemy, _settings.ChargedShotDamage, charged: true, bomb: false);
                }
            }
            if (_boss != null && _boss.Active)
            {
                float along = Vector3.Dot(_boss.Transform.position - origin, direction);
                Vector3 nearest = origin + (direction * Mathf.Clamp(along, 0f, _settings.ChargedBeamRange));
                if (along >= 0f && along <= _settings.ChargedBeamRange &&
                    Vector3.Distance(nearest, _boss.Transform.position) <=
                    _settings.ChargedBeamRadius + _boss.HitRadius)
                {
                    ApplyDamage(_boss, _settings.ChargedShotDamage, charged: true, bomb: false);
                }
            }
            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                if (!satellite.Active)
                {
                    continue;
                }
                Vector3 targetPosition = SatelliteTargetPosition(satellite);
                float along = Vector3.Dot(targetPosition - origin, direction);
                Vector3 nearest = origin + (direction * Mathf.Clamp(along, 0f, _settings.ChargedBeamRange));
                bool inBeam = along >= 0f && along <= _settings.ChargedBeamRange &&
                    Vector3.Distance(nearest, targetPosition) <=
                    _settings.ChargedBeamRadius + _settings.SatelliteHitRadius;
                bool inBlast = Vector3.Distance(lockPosition, targetPosition) <=
                    _settings.ChargedBlastRadius + _settings.SatelliteHitRadius;
                if (inBeam || inBlast)
                {
                    DamageSatellite(satellite, _settings.ChargedShotDamage);
                }
            }
            _chargedBeamElapsed = 0f;
            _chargedBeamVisual.gameObject.SetActive(true);
            _cameraShake = Mathf.Max(_cameraShake, _settings.ImpactCameraShake * 1.6f);
            _state.ChargeElapsed = 0f;
            _chargeLock = null;
            _satelliteChargeLock = null;
            _chargeReadyCued = false;
            _chargeLocks.Clear();
            ClearSatelliteLocks();
            PlayCue(_settings.ChargedFireEvent, _player.transform.position);
        }

        private void UpdateChargeLock(float deltaTime)
        {
            RailEnemy previous = _chargeLock;
            // The reticle rides the aim viewport, so lock-on has to search around the
            // crosshair rather than the middle of the screen.
            float radius = Mathf.Max(
                _settings.ChargeLockViewportRadius,
                _settings.ChargeLockViewportRadius * ChargeNormalized() * 2f);
            float satelliteRadius = Mathf.Max(radius, _settings.SatelliteLockViewportRadius);
            TickSatelliteLockAcquisition(satelliteRadius, deltaTime);
            _chargeLock = FindViewportTarget(radius, _settings.ChargedBeamRange);
            _satelliteChargeLock = FindViewportSatelliteTarget(
                satelliteRadius,
                _settings.ChargedBeamRange,
                out float satelliteScore);
            // A satellite only takes the primary slot from an enemy once its staged lock has
            // actually completed, so a half-acquired satellite cannot blank the enemy lock.
            if (_chargeLock != null &&
                TryScoreViewportTarget(_chargeLock, radius, _settings.ChargedBeamRange, out float enemyScore) &&
                (!IsSatelliteLocked(_satelliteChargeLock) || enemyScore <= satelliteScore))
            {
                // The enemy keeps the primary slot. The satellite stays tracked for the HUD and
                // still joins the multi-lock list once it finishes acquiring.
            }
            else if (IsSatelliteLocked(_satelliteChargeLock))
            {
                _chargeLock = null;
            }
            CollectChargeLocks(radius, satelliteRadius);
            if (_chargeLock != null && _chargeLock != previous)
            {
                PlayCue(_settings.TargetLockEvent, _chargeLock.Transform.position);
            }
        }

        /// <summary>
        /// Advances the staged satellite lock. Holding a satellite inside the lock radius carries
        /// it from Detected through Locking to Locked; drifting off it freezes the progress for a
        /// grace window before the acquisition is dropped entirely.
        /// </summary>
        private void TickSatelliteLockAcquisition(float viewportRadius, float deltaTime)
        {
            float detectedDuration = Mathf.Max(0f, _settings.SatelliteLockDetectedDuration);
            float acquisitionTime = Mathf.Max(0f, _settings.SatelliteLockAcquisitionTime);
            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                if (satellite == null)
                {
                    continue;
                }
                if (!satellite.Active ||
                    !TryScoreViewportTarget(
                        SatelliteTargetPosition(satellite),
                        viewportRadius,
                        _settings.ChargedBeamRange,
                        out _))
                {
                    if (satellite.LockState == RailSatelliteLockState.None)
                    {
                        continue;
                    }
                    satellite.LockGrace += deltaTime;
                    if (!satellite.Active ||
                        satellite.LockGrace >= _settings.SatelliteLockLossTolerance)
                    {
                        ResetSatelliteLock(satellite);
                    }
                    continue;
                }

                satellite.LockGrace = 0f;
                satellite.LockElapsed += deltaTime;
                RailSatelliteLockState state = satellite.LockElapsed >= detectedDuration + acquisitionTime
                    ? RailSatelliteLockState.Locked
                    : satellite.LockElapsed >= detectedDuration
                        ? RailSatelliteLockState.Locking
                        : RailSatelliteLockState.Detected;
                if (state == satellite.LockState)
                {
                    continue;
                }
                satellite.LockState = state;
                if (state == RailSatelliteLockState.Locked)
                {
                    PlayCue(_settings.TargetLockEvent, SatelliteTargetPosition(satellite));
                }
            }
        }

        private float SatelliteLockProgress(RailSatellite satellite)
        {
            if (satellite == null || satellite.LockState == RailSatelliteLockState.None)
            {
                return 0f;
            }
            if (satellite.LockState == RailSatelliteLockState.Locked)
            {
                return 1f;
            }
            float detectedDuration = Mathf.Max(0f, _settings.SatelliteLockDetectedDuration);
            float acquisitionTime = Mathf.Max(Mathf.Epsilon, _settings.SatelliteLockAcquisitionTime);
            return Mathf.Clamp01((satellite.LockElapsed - detectedDuration) / acquisitionTime);
        }

        private static bool IsSatelliteLocked(RailSatellite satellite)
        {
            return satellite != null && satellite.Active &&
                satellite.LockState == RailSatelliteLockState.Locked;
        }

        private static void ResetSatelliteLock(RailSatellite satellite)
        {
            if (satellite == null)
            {
                return;
            }
            satellite.LockState = RailSatelliteLockState.None;
            satellite.LockElapsed = 0f;
            satellite.LockGrace = 0f;
        }

        private void ClearSatelliteLocks()
        {
            _satelliteChargeLocks.Clear();
            _satelliteChargeLock = null;
            for (int i = 0; i < _satellites.Count; i++)
            {
                ResetSatelliteLock(_satellites[i]);
            }
        }

        private void CollectChargeLocks(float viewportRadius, float satelliteViewportRadius)
        {
            _chargeLocks.Clear();
            _satelliteChargeLocks.Clear();
            int capacity = Mathf.Max(1, _settings.ChargedLockCapacity);
            if (_chargeLock != null && _chargeLock.Active)
            {
                _chargeLocks.Add(_chargeLock);
            }
            if (IsSatelliteLocked(_satelliteChargeLock) && _chargeLocks.Count < capacity)
            {
                _satelliteChargeLocks.Add(_satelliteChargeLock);
            }
            for (int i = 0;
                 i < _enemies.Count && _chargeLocks.Count + _satelliteChargeLocks.Count < capacity;
                 i++)
            {
                RailEnemy enemy = _enemies[i];
                if (enemy.Active && enemy != _chargeLock &&
                    TryScoreViewportTarget(enemy, viewportRadius, _settings.ChargedBeamRange, out _))
                {
                    _chargeLocks.Add(enemy);
                }
            }
            if (_boss != null && _boss.Active && _boss != _chargeLock &&
                _chargeLocks.Count + _satelliteChargeLocks.Count < capacity &&
                TryScoreViewportTarget(_boss, viewportRadius, _settings.ChargedBeamRange, out _))
            {
                _chargeLocks.Add(_boss);
            }
            for (int i = 0;
                 i < _satellites.Count && _chargeLocks.Count + _satelliteChargeLocks.Count < capacity;
                 i++)
            {
                RailSatellite satellite = _satellites[i];
                if (IsSatelliteLocked(satellite) && satellite != _satelliteChargeLock &&
                    TryScoreViewportTarget(
                        SatelliteTargetPosition(satellite),
                        satelliteViewportRadius,
                        _settings.ChargedBeamRange,
                        out _))
                {
                    _satelliteChargeLocks.Add(satellite);
                }
            }
        }

        private RailSatellite FindViewportSatelliteTarget(
            float viewportRadius,
            float maximumRange,
            out float bestScore)
        {
            RailSatellite best = null;
            bestScore = float.PositiveInfinity;
            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                if (!satellite.Active ||
                    !TryScoreViewportTarget(
                        SatelliteTargetPosition(satellite),
                        viewportRadius,
                        maximumRange,
                        out float score) ||
                    score >= bestScore)
                {
                    continue;
                }
                best = satellite;
                bestScore = score;
            }
            return best;
        }

        private RailEnemy FindViewportTarget(float viewportRadius, float maximumRange)
        {
            RailEnemy best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < _enemies.Count; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (!enemy.Active ||
                    !TryScoreViewportTarget(enemy, viewportRadius, maximumRange, out float score))
                {
                    continue;
                }
                if (score < bestScore)
                {
                    best = enemy;
                    bestScore = score;
                }
            }
            if (_boss != null && _boss.Active &&
                TryScoreViewportTarget(_boss, viewportRadius, maximumRange, out float bossScore) &&
                bossScore < bestScore)
            {
                best = _boss;
            }
            return best;
        }

        private bool TryScoreViewportTarget(
            RailEnemy enemy,
            float viewportRadius,
            float maximumRange,
            out float score)
        {
            return TryScoreViewportTarget(enemy?.Transform, viewportRadius, maximumRange, out score);
        }

        private bool TryScoreViewportTarget(
            Transform target,
            float viewportRadius,
            float maximumRange,
            out float score)
        {
            if (target == null)
            {
                score = float.PositiveInfinity;
                return false;
            }
            return TryScoreViewportTarget(target.position, viewportRadius, maximumRange, out score);
        }

        private bool TryScoreViewportTarget(
            Vector3 targetPosition,
            float viewportRadius,
            float maximumRange,
            out float score)
        {
            score = float.PositiveInfinity;
            if (_camera == null)
            {
                return false;
            }
            Vector3 viewport = _camera.WorldToViewportPoint(targetPosition);
            if (viewport.z <= 0f)
            {
                return false;
            }
            if (Vector3.Distance(_player.transform.position, targetPosition) > maximumRange)
            {
                return false;
            }
            float offset = Vector2.Distance(new Vector2(viewport.x, viewport.y), _aimViewport);
            if (offset > viewportRadius)
            {
                return false;
            }
            score = offset;
            return true;
        }

        private void DetonateBomb()
        {
            _state.Bombs--;
            _bombElapsed = 0f;
            _bombVisual.position = _player.transform.position;
            _bombVisual.localScale = Vector3.zero;
            _bombVisual.gameObject.SetActive(true);
            for (int i = 0; i < _enemies.Count; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (enemy.Active &&
                    Vector3.Distance(enemy.Transform.position, _player.transform.position) <=
                    _settings.BombRange + enemy.HitRadius)
                {
                    ApplyDamage(enemy, _settings.BombDamage, charged: false, bomb: true);
                }
            }
            if (_boss != null && _boss.Active)
            {
                ApplyDamage(_boss, _settings.BombDamage, charged: false, bomb: true);
            }
            for (int i = 0; i < _enemyProjectiles.Count; i++)
            {
                DeactivateProjectile(_enemyProjectiles[i]);
            }
            _cameraShake = Mathf.Max(_cameraShake, _settings.BombCameraShake);
            _fovImpulse = Mathf.Max(_fovImpulse, _settings.BombFieldOfViewImpulse);
            PlayCue(_settings.BombEvent, _player.transform.position);
        }

        private Vector3 PredictedPlayerPosition()
        {
            return _player.transform.position + (new Vector3(
                _state.LateralVelocity.x,
                _state.LateralVelocity.y,
                0f) * _settings.EnemyPredictiveLeadSeconds);
        }

        private int ActiveEnemyBulletCount()
        {
            int active = 0;
            for (int i = 0; i < _enemyProjectiles.Count; i++)
            {
                if (_enemyProjectiles[i].Active)
                {
                    active++;
                }
            }
            return active;
        }

        // A curtain the drone cannot physically thread is not a bullet hell, it is a wall,
        // so the live bullet count is capped no matter how many emitters are firing.
        private int RemainingBulletBudget()
        {
            return Mathf.Max(0, _settings.MaximumActiveEnemyBullets - ActiveEnemyBulletCount());
        }

        private static int BulletStyleIndex(RailShooterBulletPattern pattern)
        {
            switch (pattern)
            {
                case RailShooterBulletPattern.Ring:
                case RailShooterBulletPattern.Spiral:
                    return 1;
                case RailShooterBulletPattern.Wall:
                    return 2;
                case RailShooterBulletPattern.Weave:
                    return 3;
                default:
                    return 0;
            }
        }

        private PooledProjectile SpawnEnemyBullet(
            Vector3 origin,
            Vector3 heading,
            float speed,
            RailShooterBulletPattern pattern,
            float curveDegreesPerSecond,
            float weaveSwing)
        {
            PooledProjectile projectile = AcquireProjectile(_enemyProjectiles);
            if (projectile == null || heading.sqrMagnitude <= 0.0001f)
            {
                return null;
            }
            int style = BulletStyleIndex(pattern);
            ApplyBulletStyle(projectile, _bulletCoreMaterials[style], _bulletGlowMaterials[style]);
            projectile.Active = true;
            projectile.EnemyOwned = true;
            projectile.Heading = heading.normalized;
            projectile.Speed = Mathf.Max(1f, speed);
            projectile.Velocity = projectile.Heading * projectile.Speed;
            projectile.Remaining = _settings.EnemyProjectileLifetime;
            projectile.Radius = _settings.EnemyProjectileRadius;
            projectile.Age = 0f;
            projectile.ArmDuration = _settings.BulletArmDuration;
            projectile.CurveDegreesPerSecond = curveDegreesPerSecond;
            projectile.WeaveSwing = weaveSwing;
            projectile.WeaveFrequency = _settings.WeaveFrequency;
            projectile.WeavePhase = (float)(_random.NextDouble() * Mathf.PI * 2f);
            projectile.PulsePhase = (float)(_random.NextDouble() * Mathf.PI * 2f);
            projectile.CoreStretch = _settings.BulletVelocityStretch;
            projectile.Grazed = false;
            projectile.Transform.position = origin;
            projectile.Transform.rotation = Quaternion.LookRotation(projectile.Heading, Vector3.up);
            projectile.Root.SetActive(true);
            if (projectile.Trail != null)
            {
                projectile.Trail.Clear();
                projectile.Trail.time = _settings.BulletTrailDuration;
                float width = _settings.EnemyProjectileRadius * 2f * _settings.BulletTrailWidthFraction;
                projectile.Trail.widthMultiplier = width;
                projectile.Trail.emitting = _settings.BulletTrailDuration > 0f;
            }
            UpdateBulletVisual(projectile, 0f);
            return projectile;
        }

        // The blue radial patterns take long enough to arrive that firing at the drone's
        // current position always trails a strafing player, so their cone axis is aimed at
        // a solved intercept: the point where drone and lattice actually meet.
        private Vector3 RadialInterceptPoint(Vector3 origin, float bulletSpeed)
        {
            if (!_settings.RadialPatternPredictsDrone)
            {
                return PredictedPlayerPosition();
            }
            Vector3 playerPosition = _player.transform.position;
            Vector3 playerVelocity = new Vector3(
                _state.LateralVelocity.x,
                _state.LateralVelocity.y,
                _state.ForwardSpeed);
            float speed = Mathf.Max(1f, bulletSpeed);
            float lead = 0f;
            int iterations = Mathf.Max(1, _settings.RadialPatternInterceptIterations);
            for (int i = 0; i < iterations; i++)
            {
                Vector3 candidate = playerPosition + (playerVelocity * lead);
                lead = Mathf.Clamp(
                    Vector3.Distance(origin, candidate) / speed,
                    0f,
                    _settings.RadialPatternMaximumLead);
            }
            Vector3 intercept =
                playerPosition + (playerVelocity * lead * _settings.RadialPatternLeadStrength);
            // A hard turn right as the pattern fires would otherwise throw the whole lattice
            // outside the arena, where it is neither a threat nor readable.
            intercept.x = Mathf.Clamp(
                intercept.x,
                _arenaOrigin.x - _settings.FlightBounds.x,
                _arenaOrigin.x + _settings.FlightBounds.x);
            intercept.y = Mathf.Clamp(
                intercept.y,
                _arenaOrigin.y - _settings.FlightBounds.y,
                _arenaOrigin.y + _settings.FlightBounds.y);
            return intercept;
        }

        // Rings and spirals fan out sideways while still closing on the drone, so the
        // player flies into an expanding lattice rather than past a stationary one.
        private Vector3 RadialHeading(Vector3 origin, float planarAngleDegrees, float bulletSpeed)
        {
            Vector3 approach = RadialInterceptPoint(origin, bulletSpeed) - origin;
            if (approach.sqrMagnitude <= 0.0001f)
            {
                approach = Vector3.back;
            }
            approach.Normalize();
            float cone = _settings.RadialPatternConeAngle * Mathf.Deg2Rad;
            float radians = planarAngleDegrees * Mathf.Deg2Rad;
            Vector3 planar = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
            return ((planar * Mathf.Sin(cone)) + (approach * Mathf.Cos(cone))).normalized;
        }

        private void FireAimedBullets(RailEnemy source, int count, float spreadAngle)
        {
            int budget = Mathf.Min(Mathf.Max(1, count), RemainingBulletBudget());
            Vector3 origin = source.Transform.position;
            Vector3 baseDirection = (PredictedPlayerPosition() - origin).normalized;
            for (int i = 0; i < budget; i++)
            {
                float normalized = budget > 1 ? i / (float)(budget - 1) : 0.5f;
                float angle = Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, normalized);
                Vector3 heading = Quaternion.AngleAxis(angle, Vector3.forward) * baseDirection;
                SpawnEnemyBullet(
                    origin,
                    heading,
                    _settings.EnemyProjectileSpeed,
                    budget > 1 ? RailShooterBulletPattern.Spread : RailShooterBulletPattern.Aimed,
                    0f,
                    0f);
            }
        }

        private void FireWeavingBullets(RailEnemy source, int count)
        {
            int budget = Mathf.Min(Mathf.Max(1, count), RemainingBulletBudget());
            Vector3 origin = source.Transform.position;
            Vector3 baseDirection = (PredictedPlayerPosition() - origin).normalized;
            for (int i = 0; i < budget; i++)
            {
                // Alternating swing directions braid the bullets around each other, which
                // leaves a moving gap between them instead of a solid pair.
                float swing = _settings.WeaveSwingDegreesPerSecond * ((i % 2 == 0) ? 1f : -1f);
                SpawnEnemyBullet(
                    origin,
                    baseDirection,
                    _settings.EnemyProjectileSpeed,
                    RailShooterBulletPattern.Weave,
                    0f,
                    swing);
            }
        }

        private void FireBulletRing(RailEnemy source, int count, float angleOffset)
        {
            int requested = Mathf.Max(3, count);
            int budget = Mathf.Min(requested, RemainingBulletBudget());
            Vector3 origin = source.Transform.position;
            float speed = _settings.EnemyProjectileSpeed * _settings.RingBulletSpeedMultiplier;
            float step = 360f / requested;
            for (int i = 0; i < budget; i++)
            {
                Vector3 heading = RadialHeading(origin, angleOffset + (step * i), speed);
                SpawnEnemyBullet(
                    origin,
                    heading,
                    speed,
                    RailShooterBulletPattern.Ring,
                    0f,
                    0f);
            }
        }

        private void FireBulletSpiral(RailEnemy source, int arms, float angle)
        {
            int requested = Mathf.Max(1, arms);
            int budget = Mathf.Min(requested, RemainingBulletBudget());
            Vector3 origin = source.Transform.position;
            float speed = _settings.EnemyProjectileSpeed * _settings.RingBulletSpeedMultiplier;
            float step = 360f / requested;
            for (int i = 0; i < budget; i++)
            {
                Vector3 heading = RadialHeading(origin, angle + (step * i), speed);
                SpawnEnemyBullet(
                    origin,
                    heading,
                    speed,
                    RailShooterBulletPattern.Spiral,
                    _settings.SpiralCurveDegreesPerSecond,
                    0f);
            }
        }

        // The curtain always carries one opening wide enough to fly through, and the
        // opening is placed away from the drone so it has to be flown to, not sat in.
        private void FireBulletWall(RailEnemy source)
        {
            int requested = Mathf.Max(3, _settings.BossWallBulletCount);
            int budget = Mathf.Min(requested, RemainingBulletBudget());
            if (budget < 3)
            {
                return;
            }
            Vector3 origin = source.Transform.position;
            Vector3 approach = PredictedPlayerPosition() - origin;
            approach.x = 0f;
            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.0001f)
            {
                approach = Vector3.back;
            }
            approach.Normalize();

            float halfWidth = _settings.BossWallWidth * 0.5f;
            float gapHalfWidth = Mathf.Min(
                _settings.BossWallGapWidth * 0.5f,
                _settings.BossWallWidth * 0.4f);
            float playerX = _player.transform.position.x - _arenaOrigin.x;
            float gapCenter = Mathf.Clamp(
                -Mathf.Sign(playerX == 0f ? 1f : playerX) * (halfWidth * 0.45f),
                -halfWidth + gapHalfWidth,
                halfWidth - gapHalfWidth);
            float step = _settings.BossWallWidth / (requested - 1);
            int spawned = 0;
            for (int i = 0; i < requested && spawned < budget; i++)
            {
                float offset = -halfWidth + (step * i);
                if (Mathf.Abs(offset - gapCenter) <= gapHalfWidth)
                {
                    continue;
                }
                Vector3 slot = new Vector3(_arenaOrigin.x + offset, origin.y, origin.z);
                SpawnEnemyBullet(
                    slot,
                    approach,
                    _settings.EnemyProjectileSpeed * _settings.BossWallBulletSpeedMultiplier,
                    RailShooterBulletPattern.Wall,
                    0f,
                    0f);
                spawned++;
            }
        }

        private RailShooterBulletPattern ChooseGruntPattern(RailEnemy enemy)
        {
            if (!_settings.BulletPatternsEnabled)
            {
                return RailShooterBulletPattern.Aimed;
            }
            _patternCandidates.Clear();
            _patternCandidates.Add(RailShooterBulletPattern.Aimed);
            if (_waveIndex >= _settings.PatternUnlockWaveSpread)
            {
                _patternCandidates.Add(RailShooterBulletPattern.Spread);
            }
            if (_waveIndex >= _settings.PatternUnlockWaveRing)
            {
                int ringWeight = Mathf.Max(1, _settings.RingPatternSelectionWeight);
                for (int i = 0; i < ringWeight; i++)
                {
                    _patternCandidates.Add(RailShooterBulletPattern.Ring);
                }
            }
            if (_waveIndex >= _settings.PatternUnlockWaveWeave)
            {
                _patternCandidates.Add(RailShooterBulletPattern.Weave);
            }
            // Each silhouette leans on one pattern so the player can read the threat from
            // the hostile rather than only from the bullets already in the air.
            RailShooterBulletPattern signature = enemy.Kind switch
            {
                RailShooterEnemyKind.StrikeOrb => RailShooterBulletPattern.Ring,
                RailShooterEnemyKind.VesperKite => RailShooterBulletPattern.Weave,
                _ => RailShooterBulletPattern.Spread,
            };
            if (_patternCandidates.Contains(signature))
            {
                _patternCandidates.Add(signature);
                if (enemy.Elite)
                {
                    _patternCandidates.Add(signature);
                }
            }
            return _patternCandidates[_random.Next(_patternCandidates.Count)];
        }

        private void FireEnemyPattern(RailEnemy source)
        {
            if (RemainingBulletBudget() <= 0)
            {
                return;
            }
            switch (ChooseGruntPattern(source))
            {
                case RailShooterBulletPattern.Spread:
                    FireAimedBullets(
                        source,
                        _settings.GruntSpreadBulletCount + (source.Elite ? 2 : 0),
                        _settings.GruntSpreadAngle);
                    break;
                case RailShooterBulletPattern.Ring:
                    FireBulletRing(
                        source,
                        _settings.GruntRingBulletCount + (source.Elite ? 3 : 0),
                        (float)(_random.NextDouble() * 360.0));
                    break;
                case RailShooterBulletPattern.Weave:
                    FireWeavingBullets(source, source.Elite ? 4 : 2);
                    break;
                default:
                    FireAimedBullets(
                        source,
                        Mathf.Max(1, (_settings.GruntSpreadBulletCount + 1) / 2),
                        _settings.GruntSpreadAngle * 0.5f);
                    break;
            }
        }

        private void ResetBossBulletPatterns()
        {
            _bossNextRingAt = _settings.BossRingInterval;
            _bossNextSpiralAt = _settings.BossSpiralCooldown;
            _bossNextWallAt = _settings.BossWallInterval;
            _bossSpiralBurstEndsAt = float.NegativeInfinity;
            _bossSpiralNextShotAt = 0f;
            _bossSpiralAngle = 0f;
            _bossSpiralDirection = 1;
        }

        private void TickBossBulletPatterns(int phase, float deltaTime)
        {
            if (!_settings.BulletPatternsEnabled || _boss == null || !_boss.Active)
            {
                return;
            }
            float age = _boss.Age;
            if (age >= _bossNextRingAt)
            {
                FireBulletRing(
                    _boss,
                    _settings.BossRingBulletCount + ((phase - 1) * 4),
                    _bossSpiralAngle);
                _bossNextRingAt = age + (_settings.BossRingInterval / (1f + ((phase - 1) * 0.22f)));
            }
            if (phase >= 2)
            {
                if (age >= _bossNextSpiralAt && age >= _bossSpiralBurstEndsAt)
                {
                    _bossSpiralBurstEndsAt = age + _settings.BossSpiralBurstDuration;
                    _bossSpiralNextShotAt = age;
                    // Reversing each burst stops the spiral from becoming a memorised
                    // one-way drift the player can simply hold a stick against.
                    _bossSpiralDirection = -_bossSpiralDirection;
                    _bossNextSpiralAt = _bossSpiralBurstEndsAt +
                        (_settings.BossSpiralCooldown / (1f + ((phase - 2) * 0.35f)));
                }
                if (age < _bossSpiralBurstEndsAt)
                {
                    _bossSpiralAngle +=
                        _settings.BossSpiralRotationDegreesPerSecond * _bossSpiralDirection * deltaTime;
                    float interval = Mathf.Max(0.02f, _settings.BossSpiralShotInterval);
                    int guard = 0;
                    while (age >= _bossSpiralNextShotAt && guard < 8)
                    {
                        FireBulletSpiral(_boss, _settings.BossSpiralArms, _bossSpiralAngle);
                        _bossSpiralNextShotAt += interval;
                        guard++;
                    }
                }
            }
            if (phase >= 3 && age >= _bossNextWallAt)
            {
                FireBulletWall(_boss);
                _bossNextWallAt = age + _settings.BossWallInterval;
            }
        }

        private void TickProjectiles(float deltaTime)
        {
            TickPlayerProjectiles(deltaTime);
            Vector3 playerPosition = _player.transform.position;
            for (int i = 0; i < _enemyProjectiles.Count; i++)
            {
                PooledProjectile projectile = _enemyProjectiles[i];
                if (!projectile.Active)
                {
                    continue;
                }
                projectile.Age += deltaTime;
                projectile.Remaining -= deltaTime;
                AdvanceBullet(projectile, deltaTime);
                UpdateBulletVisual(projectile, deltaTime);
                if (projectile.Remaining <= 0f ||
                    projectile.Transform.position.z < playerPosition.z - _settings.EnemyDespawnBehindDistance)
                {
                    DeactivateProjectile(projectile);
                    continue;
                }
                float playerDistance = Vector3.Distance(
                    projectile.Transform.position,
                    playerPosition);
                if (_state.Trick != RailShooterTrick.None &&
                    playerDistance <= _settings.RollProjectileDeflectRadius)
                {
                    DeactivateProjectile(projectile);
                    _state.ProjectileDeflections++;
                    AddScore(_settings.ProjectileDeflectScore);
                    SpawnImpact(projectile.Transform.position, _settings.RegularShotRadius * 3f);
                    continue;
                }
                // A bullet is inert while it is still growing out of its muzzle flash, so
                // point-blank spawns can never be an unavoidable hit.
                if (projectile.Age < projectile.ArmDuration)
                {
                    continue;
                }
                if (playerDistance <= projectile.Radius + _settings.BulletPlayerHitRadius)
                {
                    DamagePlayer(_settings.EnemyProjectileDamage, "Rift bullet pattern");
                    DeactivateProjectile(projectile);
                    continue;
                }
                if (_settings.GrazeEnabled && !projectile.Grazed &&
                    playerDistance <= projectile.Radius + _settings.BulletGrazeRadius)
                {
                    RegisterGraze(projectile);
                }
            }
        }

        private void AdvanceBullet(PooledProjectile projectile, float deltaTime)
        {
            float steer = projectile.CurveDegreesPerSecond;
            if (!Mathf.Approximately(projectile.WeaveSwing, 0f))
            {
                steer += Mathf.Cos(
                    (projectile.Age * projectile.WeaveFrequency * Mathf.PI * 2f) + projectile.WeavePhase) *
                    projectile.WeaveSwing;
            }
            if (!Mathf.Approximately(steer, 0f))
            {
                // Curling around the rail axis keeps the closing speed intact while the
                // bullet sweeps across the screen plane the player is dodging in.
                projectile.Heading =
                    (Quaternion.AngleAxis(steer * deltaTime, Vector3.forward) * projectile.Heading).normalized;
            }
            projectile.Velocity = projectile.Heading * projectile.Speed;
            projectile.Transform.position += projectile.Velocity * deltaTime;
        }

        private void UpdateBulletVisual(PooledProjectile projectile, float deltaTime)
        {
            float grow = Mathf.Clamp01(projectile.Age / Mathf.Max(0.01f, _settings.BulletGrowDuration));
            float eased = grow * grow * (3f - (2f * grow));
            float diameter = projectile.Radius * 2f;
            if (projectile.Core != null)
            {
                float coreScale = Mathf.Lerp(0.2f, 1f, eased);
                projectile.Core.localScale = new Vector3(
                    diameter * coreScale,
                    diameter * coreScale,
                    diameter * coreScale * Mathf.Max(1f, projectile.CoreStretch));
            }
            if (projectile.Cage != null)
            {
                projectile.Cage.localScale = Vector3.one * (diameter * 1.35f * eased);
                projectile.Cage.Rotate(
                    _settings.BulletCoreSpinDegreesPerSecond * deltaTime * 0.6f,
                    _settings.BulletCoreSpinDegreesPerSecond * deltaTime,
                    0f,
                    Space.Self);
            }
            if (projectile.Halo != null)
            {
                float pulse = 1f + (Mathf.Sin(
                    (projectile.Age * _settings.BulletHaloPulseSpeed) + projectile.PulsePhase) *
                    _settings.BulletHaloPulseAmplitude);
                // The halo starts as a wide flash and collapses into the bullet, which is
                // the tell that a new bullet just appeared there.
                float envelope = Mathf.Lerp(
                    Mathf.Max(_settings.BulletSpawnFlashScale, _settings.BulletHaloScale),
                    _settings.BulletHaloScale,
                    eased);
                projectile.Halo.localScale = Vector3.one * (diameter * envelope * pulse);
            }
            if (projectile.Velocity.sqrMagnitude > 0.0001f)
            {
                projectile.Transform.rotation =
                    Quaternion.LookRotation(projectile.Velocity.normalized, Vector3.up);
            }
        }

        private void RegisterGraze(PooledProjectile projectile)
        {
            projectile.Grazed = true;
            _state.Grazes++;
            AddScore(_settings.BulletGrazeScore);
            _state.ManeuverEnergy = Mathf.Clamp(
                _state.ManeuverEnergy + _settings.BulletGrazeEnergyReward,
                0f,
                _settings.ManeuverEnergyCapacity);
            if (_state.Elapsed >= _nextGrazePopupAt)
            {
                _nextGrazePopupAt = _state.Elapsed + _settings.BulletGrazePopupInterval;
                SpawnScorePopup(
                    projectile.Transform.position,
                    "GRAZE",
                    _settings.HudReticleColor);
            }
        }

        private void TickPlayerProjectiles(float deltaTime)
        {
            for (int i = 0; i < _playerProjectiles.Count; i++)
            {
                PooledProjectile projectile = _playerProjectiles[i];
                if (!projectile.Active)
                {
                    continue;
                }
                Vector3 previous = projectile.Transform.position;
                projectile.Remaining -= deltaTime;
                projectile.Transform.position += projectile.Velocity * deltaTime;
                if (projectile.Remaining <= 0f)
                {
                    DeactivateProjectile(projectile);
                    continue;
                }
                RailSatellite satelliteHit = FindSatelliteHit(
                    previous,
                    projectile.Transform.position,
                    projectile.Radius);
                if (satelliteHit != null)
                {
                    DamageSatellite(satelliteHit, _settings.RegularShotDamage);
                    DeactivateProjectile(projectile);
                    continue;
                }
                RailEnemy hit = FindProjectileHit(previous, projectile.Transform.position, projectile.Radius);
                if (hit != null)
                {
                    SpawnImpact(hit.Transform.position, _settings.RegularShotRadius * 2.5f);
                    ApplyDamage(hit, _settings.RegularShotDamage, charged: false, bomb: false);
                    DeactivateProjectile(projectile);
                }
            }
        }

        private RailEnemy FindProjectileHit(Vector3 start, Vector3 end, float radius)
        {
            RailEnemy best = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < _enemies.Count; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (!enemy.Active || !SegmentIntersectsSphere(
                        start,
                        end,
                        enemy.Transform.position,
                        radius + enemy.HitRadius + _settings.ShotHitRadiusBonus,
                        out float distance))
                {
                    continue;
                }
                if (distance < bestDistance)
                {
                    best = enemy;
                    bestDistance = distance;
                }
            }
            if (_boss != null && _boss.Active && SegmentIntersectsSphere(
                    start,
                    end,
                    _boss.Transform.position,
                    radius + _boss.HitRadius + _settings.ShotHitRadiusBonus,
                    out float bossDistance) &&
                bossDistance < bestDistance)
            {
                best = _boss;
            }
            return best;
        }

        private void ApplyDamage(RailEnemy enemy, float damage, bool charged, bool bomb)
        {
            if (enemy == null || !enemy.Active || damage <= 0f)
            {
                return;
            }
            enemy.Health = Mathf.Max(0f, enemy.Health - damage);
            enemy.HitFlashElapsed = 0f;
            _hitMarkerElapsed = 0f;
            if (_state.Elapsed - _lastHitCueAt >= _settings.HitCueMinimumInterval)
            {
                _lastHitCueAt = _state.Elapsed;
                PlayCue(_settings.EnemyHitEvent, enemy.Transform.position);
            }
            if (enemy.Health > 0f)
            {
                SpawnImpact(enemy.Transform.position, enemy.HitRadius * 0.5f);
                return;
            }

            Vector3 deathPosition = enemy.Transform.position;
            if (enemy.Boss)
            {
                enemy.Active = false;
                enemy.Root.SetActive(false);
                SpawnImpact(deathPosition, _settings.ImpactFlashMaximumScale * 2f);
                AddScore(_settings.BossKillScore);
                BeginResults(true);
                return;
            }
            enemy.Active = false;
            enemy.Root.SetActive(false);
            _state.Kills++;
            _killMarkerElapsed = 0f;
            _cameraShake = Mathf.Max(_cameraShake, _settings.KillCameraShake);
            PlayCue(_settings.EnemyKillEvent, deathPosition);
            if (charged)
            {
                _state.ChargeKills++;
            }
            _state.Combo++;
            _state.ComboMultiplier = Mathf.Clamp(
                1f + ((_state.Combo - 1) * 0.18f),
                1f,
                _settings.MaximumComboMultiplier);
            _comboExpiresAt = _state.Elapsed + _settings.ComboWindow;
            float weaponMultiplier = charged
                ? _settings.ChargedShotScoreMultiplier
                : bomb ? _settings.BombScoreMultiplier : 1f;
            float eliteMultiplier = enemy.Elite ? _settings.EliteScoreMultiplier : 1f;
            int killScore = Mathf.RoundToInt(
                _settings.KillScore * _state.ComboMultiplier * weaponMultiplier * eliteMultiplier);
            AddScore(killScore);
            SpawnScorePopup(
                deathPosition,
                _state.Combo > 1
                    ? $"+{killScore}  x{_state.ComboMultiplier:0.0}"
                    : $"+{killScore}",
                enemy.Elite ? _settings.HudComboColor : _settings.HudPrimaryColor);
            FormationRecord formation = FindFormation(enemy.FormationId);
            if (formation != null)
            {
                formation.Remaining = Mathf.Max(0, formation.Remaining - 1);
                if (formation.Remaining == 0 && !formation.Escaped && !formation.Awarded)
                {
                    formation.Awarded = true;
                    _state.FormationClears++;
                    AddScore(_settings.FormationClearScore);
                    SpawnScorePopup(
                        deathPosition,
                        $"FORMATION CLEAR  +{_settings.FormationClearScore}",
                        _settings.HudChargeColor);
                }
            }
            SpawnImpact(deathPosition, Mathf.Max(_settings.ImpactFlashMaximumScale, enemy.HitRadius));
            _killsSinceDrop++;
            if (_killsSinceDrop >= Mathf.Max(1, _settings.EnemyDropEveryKills))
            {
                _killsSinceDrop = 0;
                SpawnCoursePickup((PickupKind)(_state.Kills % 3), deathPosition);
            }
        }

        private void TickRouteGates()
        {
            if (_nextRouteGateIndex >= _settings.BranchGateCount)
            {
                return;
            }
            float targetDistance = _settings.BranchGateFirstDistance +
                (_nextRouteGateIndex * _settings.BranchGateSpacing);
            if (!_routeGateActive &&
                _state.Distance + _settings.PickupSpawnAheadDistance >= targetDistance)
            {
                float gateZ = _startZ + targetDistance;
                _safeGate.position = new Vector3(
                    _player.transform.position.x - _settings.BranchGateHorizontalOffset,
                    _player.transform.position.y,
                    gateZ);
                _riskGate.position = new Vector3(
                    _player.transform.position.x + _settings.BranchGateHorizontalOffset,
                    _player.transform.position.y,
                    gateZ);
                _safeGate.gameObject.SetActive(true);
                _riskGate.gameObject.SetActive(true);
                _routeGateActive = true;
            }
            if (!_routeGateActive || _player.transform.position.z < _safeGate.position.z)
            {
                return;
            }
            float safeDistance = Vector2.Distance(
                new Vector2(_player.transform.position.x, _player.transform.position.y),
                new Vector2(_safeGate.position.x, _safeGate.position.y));
            float riskDistance = Vector2.Distance(
                new Vector2(_player.transform.position.x, _player.transform.position.y),
                new Vector2(_riskGate.position.x, _riskGate.position.y));
            if (riskDistance <= _settings.BranchGateRadius)
            {
                _state.Route = RailShooterRoute.Black;
                _riskRouteCount++;
                _state.RouteGatesCleared++;
                AddScore(_settings.RiskRouteScoreBonus);
                SpawnScorePopup(
                    _riskGate.position,
                    $"{_settings.RiskRouteLabel}  +{_settings.RiskRouteScoreBonus}",
                    _settings.RiftDangerColor);
                PlayCue(_settings.RouteGateEvent, _riskGate.position);
            }
            else
            {
                _state.Route = RailShooterRoute.Signal;
                if (safeDistance <= _settings.BranchGateRadius)
                {
                    _state.RouteGatesCleared++;
                    _health.RestoreHealth(_settings.HealthPickupAmount * 0.5f);
                    SpawnScorePopup(
                        _safeGate.position,
                        _settings.SafeGateLabel,
                        _settings.RiftSignalColor);
                    PlayCue(_settings.RouteGateEvent, _safeGate.position);
                }
            }
            _safeGate.gameObject.SetActive(false);
            _riskGate.gameObject.SetActive(false);
            _routeGateActive = false;
            _nextRouteGateIndex++;
        }

        private void SpawnCoursePickup(PickupKind kind, Vector3? worldPosition)
        {
            PooledPickup pickup = AcquirePickup(kind);
            if (pickup == null)
            {
                return;
            }
            float side = (_pickupSequence & 1) == 0 ? -1f : 1f;
            pickup.Transform.position = worldPosition ?? (_player.transform.position + new Vector3(
                side * _settings.FlightBounds.x * _settings.PickupRiskLineFraction,
                Mathf.Sin(_pickupSequence * 1.7f) * _settings.FlightBounds.y * 0.65f,
                _settings.PickupSpawnAheadDistance));
            pickup.Active = true;
            pickup.Root.SetActive(true);
        }

        private void TickPickups(float deltaTime)
        {
            for (int i = 0; i < _pickups.Count; i++)
            {
                PooledPickup pickup = _pickups[i];
                if (!pickup.Active)
                {
                    continue;
                }
                pickup.Transform.Rotate(0f, _settings.WreckageRotationSpeed * 3f * deltaTime, 0f, Space.Self);
                float distance = Vector3.Distance(pickup.Transform.position, _player.transform.position);
                if (_state.Trick != RailShooterTrick.None && distance <= _settings.RollPickupMagnetRadius)
                {
                    pickup.Transform.position = Vector3.MoveTowards(
                        pickup.Transform.position,
                        _player.transform.position,
                        _settings.LateralSpeed * deltaTime);
                    distance = Vector3.Distance(pickup.Transform.position, _player.transform.position);
                }
                if (distance <= _settings.PickupRadius + _settings.PlayerCollisionRadius)
                {
                    CollectPickup(pickup);
                    continue;
                }
                if (pickup.Transform.position.z < _player.transform.position.z - _settings.EnemyDespawnBehindDistance)
                {
                    DeactivatePickup(pickup);
                }
            }
        }

        private void CollectPickup(PooledPickup pickup)
        {
            switch (pickup.Kind)
            {
                case PickupKind.Gold:
                    _wallet?.AddGold(_settings.GoldPickupAmount);
                    break;
                case PickupKind.Health:
                    _health.RestoreHealth(_settings.HealthPickupAmount);
                    break;
                case PickupKind.Bomb:
                    _state.Bombs = Mathf.Min(_settings.MaximumBombs, _state.Bombs + 1);
                    break;
            }
            _state.Pickups++;
            AddScore(_settings.PickupScore);
            SpawnScorePopup(
                pickup.Transform.position,
                pickup.Kind switch
                {
                    PickupKind.Gold => $"+{_settings.GoldPickupAmount} GOLD",
                    PickupKind.Health => $"+{Mathf.RoundToInt(_settings.HealthPickupAmount)} HULL",
                    _ => "+1 BOMB",
                },
                pickup.Kind switch
                {
                    PickupKind.Gold => _settings.RiftGoldColor,
                    PickupKind.Health => _settings.HudReticleColor,
                    _ => _settings.HudBombColor,
                });
            PlayCue(_settings.PickupEvent, pickup.Transform.position);
            SpawnImpact(pickup.Transform.position, _settings.PickupRadius);
            DeactivatePickup(pickup);
        }

        private void BeginLaneAttack(float worldX)
        {
            _laneCenterX = worldX;
            _laneElapsed = 0f;
            _laneDamageApplied = false;
            _laneWarning.gameObject.SetActive(true);
        }

        private void TickLaneAttack(float deltaTime)
        {
            if (!float.IsFinite(_laneElapsed))
            {
                return;
            }
            _laneElapsed += deltaTime;
            float trackingEnd = Mathf.Max(
                0f,
                _settings.LightningLaneTelegraphDuration -
                    _settings.LightningLaneLockBeforeActivation);
            if (_laneElapsed < trackingEnd)
            {
                _laneCenterX = Mathf.MoveTowards(
                    _laneCenterX,
                    PredictLaneInterceptX(),
                    _settings.LightningLaneTrackingSpeed * deltaTime);
            }
            float playerY = _player.transform.position.y;
            Vector3 start = new Vector3(
                _laneCenterX,
                playerY - _settings.CorridorHalfHeight,
                _player.transform.position.z);
            Vector3 end = new Vector3(
                _laneCenterX,
                playerY + _settings.CorridorHalfHeight,
                _player.transform.position.z + _settings.EnemySpawnAheadDistance);
            _laneWarning.SetPosition(0, start);
            _laneWarning.SetPosition(1, end);
            bool active = _laneElapsed >= _settings.LightningLaneTelegraphDuration;
            _laneWarning.startWidth = active
                ? _settings.LightningLaneHalfWidth * 2f
                : _settings.ReticleLineThickness * 0.1f;
            _laneWarning.endWidth = _laneWarning.startWidth;
            if (active && !_laneDamageApplied &&
                Mathf.Abs(_player.transform.position.x - _laneCenterX) <= _settings.LightningLaneHalfWidth)
            {
                _laneDamageApplied = true;
                DamagePlayer(_settings.LightningLaneDamage, "Storm Pyramid lightning lane");
            }
            if (_laneElapsed >= _settings.LightningLaneTelegraphDuration + _settings.LightningLaneActiveDuration)
            {
                _laneElapsed = float.PositiveInfinity;
                _laneWarning.gameObject.SetActive(false);
            }
        }

        private float PredictLaneInterceptX()
        {
            return _player.transform.position.x +
                (_state.LateralVelocity.x * _settings.LightningLanePredictiveLeadSeconds);
        }

        private void TickEnvironment(float deltaTime)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                RiftSegment segment = _segments[i];
                if (segment.Root.position.z < _player.transform.position.z - _settings.EnvironmentRecycleBehindDistance)
                {
                    _furthestSegmentZ += _settings.EnvironmentSegmentSpacing;
                    ResetSegment(segment, _furthestSegmentZ, i + Mathf.RoundToInt(_state.Distance));
                }
                for (int rotorIndex = 0; rotorIndex < segment.Rotators.Count; rotorIndex++)
                {
                    segment.Rotators[rotorIndex].Rotate(
                        0f,
                        _settings.WreckageRotationSpeed * (rotorIndex % 2 == 0 ? 1f : -0.7f) * deltaTime,
                        _settings.WreckageRotationSpeed * 0.22f * deltaTime,
                        Space.Self);
                }
            }

            for (int i = 0; i < _speedStreaks.Count; i++)
            {
                Transform streak = _speedStreaks[i];
                streak.position -= Vector3.forward * _settings.SpeedStreakDriftSpeed * deltaTime;
                if (streak.position.z < _camera.transform.position.z)
                {
                    ResetSpeedStreak(streak, i);
                }
            }

            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                if (satellite.Exploding)
                {
                    satellite.ExplosionElapsed += deltaTime;
                    if (satellite.ExplosionElapsed >= _settings.SatelliteExplosionDuration)
                    {
                        satellite.Exploding = false;
                        satellite.Explosion?.SetActive(false);
                    }
                }
                if (satellite.Transform.position.z <
                    _player.transform.position.z - _settings.EnvironmentRecycleBehindDistance)
                {
                    _furthestSatelliteZ += _settings.SatelliteSpacing;
                    ResetSatellite(
                        satellite,
                        _furthestSatelliteZ,
                        i + Mathf.RoundToInt(_state.Distance / Mathf.Max(1f, _settings.SatelliteSpacing)));
                }
                if (!satellite.Active)
                {
                    continue;
                }
                satellite.Transform.Rotate(
                    satellite.RotationAxis,
                    _settings.SatelliteRotationSpeed * deltaTime,
                    Space.Self);
                Vector3 position = SatelliteTargetPosition(satellite);
                if (Vector3.Distance(position, _player.transform.position) <=
                    _settings.SatelliteCollisionRadius)
                {
                    ExplodeSatellite(satellite, false);
                    DamagePlayer(_settings.SatelliteCollisionDamage, "Satellite collision");
                }
            }
        }

        private RailSatellite FindSatelliteHit(Vector3 start, Vector3 end, float radius)
        {
            RailSatellite best = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                if (!satellite.Active || !SegmentIntersectsSphere(
                        start,
                        end,
                        SatelliteTargetPosition(satellite),
                        radius + _settings.SatelliteHitRadius,
                        out float distance) || distance >= bestDistance)
                {
                    continue;
                }
                best = satellite;
                bestDistance = distance;
            }
            return best;
        }

        private void ExplodeSatellite(RailSatellite satellite, bool awardScore)
        {
            if (satellite == null || !satellite.Active)
            {
                return;
            }
            Vector3 position = SatelliteTargetPosition(satellite);
            satellite.Active = false;
            ResetSatelliteLock(satellite);
            _satelliteChargeLocks.Remove(satellite);
            if (_satelliteChargeLock == satellite)
            {
                _satelliteChargeLock = null;
            }
            satellite.Root.SetActive(false);
            satellite.Exploding = satellite.Explosion != null;
            satellite.ExplosionElapsed = 0f;
            if (satellite.Explosion != null)
            {
                satellite.Explosion.transform.SetPositionAndRotation(position, Quaternion.identity);
                satellite.Explosion.transform.localScale = Vector3.one * _settings.SatelliteExplosionScale;
                satellite.Explosion.SetActive(true);
                for (int i = 0; i < satellite.ExplosionParticles.Length; i++)
                {
                    satellite.ExplosionParticles[i].Clear(true);
                    satellite.ExplosionParticles[i].Play(true);
                }
            }
            if (awardScore)
            {
                AddScore(_settings.SatelliteDestroyScore);
                SpawnScorePopup(position, $"+{_settings.SatelliteDestroyScore}", _settings.RiftGoldColor);
            }
        }

        private void DamageSatellite(RailSatellite satellite, float damage)
        {
            if (satellite == null || !satellite.Active)
            {
                return;
            }
            satellite.Health = Mathf.Max(0f, satellite.Health - Mathf.Max(0f, damage));
            SpawnImpact(SatelliteTargetPosition(satellite), _settings.SatelliteHitRadius * 0.35f);
            if (satellite.Health <= 0f)
            {
                ExplodeSatellite(satellite, true);
            }
        }

        private void TickPresentation(float deltaTime)
        {
            float charge = ChargeNormalized();
            if (_chargeVisual != null)
            {
                bool visible = _state.ChargeElapsed > _settings.RegularFireBeforeChargeDuration;
                _chargeVisual.gameObject.SetActive(visible);
                if (visible)
                {
                    float pulse = 1f + (Mathf.Sin(_state.Elapsed * _settings.ChargeVisualPulseSpeed) * 0.08f * charge);
                    float scale = Mathf.Lerp(
                        _settings.ChargeVisualMinimumScale,
                        _settings.ChargeVisualMaximumScale,
                        charge) * pulse;
                    _chargeVisual.position = _player.transform.position + (_state.AimDirection * 2.5f);
                    _chargeVisual.localScale = Vector3.one * scale;
                }
            }
            if (float.IsFinite(_chargedBeamElapsed))
            {
                _chargedBeamElapsed += deltaTime;
                float normalized = 1f - Mathf.Clamp01(
                    _chargedBeamElapsed / _settings.ChargedBeamPresentationDuration);
                _chargedBeamVisual.position = _player.transform.position +
                    (_state.AimDirection * _settings.ChargedBeamRange * 0.5f);
                _chargedBeamVisual.rotation = Quaternion.LookRotation(_state.AimDirection, Vector3.up);
                _chargedBeamVisual.localScale = new Vector3(
                    _settings.ChargedBeamRadius * normalized,
                    _settings.ChargedBeamRadius * normalized,
                    _settings.ChargedBeamRange * 0.5f);
                if (_chargedBeamElapsed >= _settings.ChargedBeamPresentationDuration)
                {
                    _chargedBeamElapsed = float.PositiveInfinity;
                    _chargedBeamVisual.gameObject.SetActive(false);
                }
            }
            if (float.IsFinite(_bombElapsed))
            {
                _bombElapsed += deltaTime;
                float expansion = Mathf.Clamp01(_bombElapsed / _settings.BombExpansionDuration);
                _bombVisual.position = _player.transform.position;
                _bombVisual.localScale = Vector3.one * (_settings.BombRange * expansion * 2f);
                if (_bombElapsed >= _settings.BombPresentationDuration)
                {
                    _bombElapsed = float.PositiveInfinity;
                    _bombVisual.gameObject.SetActive(false);
                }
            }
            if (_state.Combo > 0 && _state.Elapsed >= _comboExpiresAt)
            {
                _state.Combo = 0;
                _state.ComboMultiplier = 1f;
            }
            AdvanceTimer(ref _hitMarkerElapsed, deltaTime, _settings.HitMarkerDuration);
            AdvanceTimer(ref _killMarkerElapsed, deltaTime, _settings.KillMarkerDuration);
            AdvanceTimer(ref _damageFlashElapsed, deltaTime, _settings.DamageFlashDuration);
            AdvanceTimer(ref _bossBannerElapsed, deltaTime, _settings.EntryCardDuration * 2f);
            for (int i = 0; i < _popups.Count; i++)
            {
                ScorePopup popup = _popups[i];
                if (!popup.Active)
                {
                    continue;
                }
                popup.Elapsed += deltaTime;
                popup.World += new Vector3(
                    0f,
                    _settings.ScorePopupRiseSpeed,
                    _state.ForwardSpeed) * deltaTime;
                if (popup.Elapsed >= _settings.ScorePopupDuration)
                {
                    popup.Active = false;
                }
            }
            FaceRailRingsToCamera();
        }

        private void FaceRailRingsToCamera()
        {
            if (_camera == null)
            {
                return;
            }
            Quaternion screenFacingRotation = _camera.transform.rotation;
            for (int i = 0; i < _railRings.Count; i++)
            {
                Transform ring = _railRings[i];
                if (ring != null && ring.gameObject.activeInHierarchy)
                {
                    ring.rotation = screenFacingRotation;
                }
            }
        }

        private static void AdvanceTimer(ref float elapsed, float deltaTime, float duration)
        {
            if (!float.IsFinite(elapsed))
            {
                return;
            }
            elapsed += deltaTime;
            if (elapsed >= Mathf.Max(0.01f, duration))
            {
                elapsed = float.PositiveInfinity;
            }
        }

        private void TickImpacts(float deltaTime)
        {
            for (int i = 0; i < _impacts.Count; i++)
            {
                PooledImpact impact = _impacts[i];
                if (!impact.Active)
                {
                    continue;
                }
                impact.Elapsed += deltaTime;
                float normalized = Mathf.Clamp01(impact.Elapsed / _settings.ImpactFlashDuration);
                impact.Transform.localScale = Vector3.one * (impact.Scale * (1f - normalized));
                if (normalized >= 1f)
                {
                    impact.Active = false;
                    impact.Root.SetActive(false);
                }
            }
        }

        private void SpawnImpact(Vector3 position, float scale)
        {
            for (int i = 0; i < _impacts.Count; i++)
            {
                PooledImpact impact = _impacts[i];
                if (impact.Active)
                {
                    continue;
                }
                impact.Active = true;
                impact.Elapsed = 0f;
                impact.Scale = Mathf.Max(0.01f, scale);
                impact.Transform.position = position;
                impact.Transform.localScale = Vector3.one * impact.Scale;
                impact.Root.SetActive(true);
                return;
            }
        }

        private void TickEnemyHitFlash(RailEnemy enemy, float deltaTime)
        {
            if (enemy.Visual == null || !float.IsFinite(enemy.HitFlashElapsed))
            {
                return;
            }
            enemy.HitFlashElapsed += deltaTime;
            float duration = Mathf.Max(0.01f, _settings.EnemyHitFlashDuration);
            if (enemy.HitFlashElapsed >= duration)
            {
                enemy.HitFlashElapsed = float.PositiveInfinity;
                enemy.Transform.localScale = enemy.BaseScale;
                return;
            }
            float pulse = 1f + ((_settings.EnemyHitFlashScale - 1f) *
                (1f - (enemy.HitFlashElapsed / duration)));
            enemy.Transform.localScale = enemy.BaseScale * pulse;
        }

        private void SpawnScorePopup(Vector3 world, string text, Color color)
        {
            for (int i = 0; i < _popups.Count; i++)
            {
                ScorePopup popup = _popups[i];
                if (popup.Active)
                {
                    continue;
                }
                popup.Active = true;
                popup.Elapsed = 0f;
                popup.World = world;
                popup.Text = text;
                popup.Color = color;
                return;
            }
        }

        private void PlayCue(string eventPath, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return;
            }
            DuneVectorAudioManager audio = DuneVectorAudioManager.Instance;
            if (audio != null)
            {
                audio.PlayRiftInterceptCue(eventPath, position);
            }
        }

        private void DamagePlayer(float damage, string source)
        {
            if (damage <= 0f || IsTrickInvulnerable() || _state.Elapsed < _nextDamageAt ||
                Phase == RailShooterPhase.Results)
            {
                return;
            }
            if (_health.TakeDamage(damage, source, "Rift intercept hull depleted."))
            {
                _nextDamageAt = _state.Elapsed + _settings.CollisionInvulnerabilityDuration;
                _cameraShake = Mathf.Max(_cameraShake, _settings.ImpactCameraShake);
                _damageFlashElapsed = 0f;
                PlayCue(_settings.PlayerDamageEvent, _player.transform.position);
            }
        }

        private void HandlePlayerDamaged(float amount)
        {
            if (IsActive && amount > 0f)
            {
                _state.TookDamage = true;
                _state.Combo = 0;
                _state.ComboMultiplier = 1f;
            }
        }

        private void HandleTemporaryHullDepleted()
        {
            if (IsActive && Phase != RailShooterPhase.Results)
            {
                BeginResults(false);
            }
        }

        private void BeginResults(bool success)
        {
            if (Phase == RailShooterPhase.Results)
            {
                return;
            }
            _resultSuccess = success;
            Phase = RailShooterPhase.Results;
            _phaseElapsed = 0f;
            _hitMarkerElapsed = float.PositiveInfinity;
            _killMarkerElapsed = float.PositiveInfinity;
            _damageFlashElapsed = float.PositiveInfinity;
            _bossBannerElapsed = float.PositiveInfinity;
            _chargeLocks.Clear();
            _satelliteChargeLocks.Clear();
            _chargeLock = null;
            _satelliteChargeLock = null;
            _sigilActive = false;
            _sigilVerdictElapsed = float.PositiveInfinity;
            if (_sigilRoot != null)
            {
                _sigilRoot.gameObject.SetActive(false);
            }
            if (_sigilDrawingCursor != null)
            {
                _sigilDrawingCursor.gameObject.SetActive(false);
            }
            ApplyRailCursorState();
            if (_state.SigilsBroken >= _settings.Sigils.SigilChallengeCount)
            {
                AddScore(_settings.Sigils.SigilChallengeBonus);
            }
            if (!_state.TookDamage)
            {
                AddScore(_settings.NoDamageChallengeBonus);
            }
            if (_state.ChargeKills >= _settings.ChargeKillChallengeCount)
            {
                AddScore(_settings.ChargeKillChallengeBonus);
            }
            if (_state.FormationClears >= _settings.FormationChallengeCount)
            {
                AddScore(_settings.FormationChallengeBonus);
            }
            ResultGrade = _state.Score >= _settings.GradeSScore
                ? "S"
                : _state.Score >= _settings.GradeAScore
                    ? "A"
                    : _state.Score >= _settings.GradeBScore ? "B" : "C";
            float rewardFraction = success ? 1f : _settings.FailureRewardFraction;
            AwardedGold = Mathf.Max(0, Mathf.RoundToInt(_state.Score * _settings.GoldPerScore * rewardFraction));
            _distanceGoldBonus = Mathf.Max(
                0,
                Mathf.FloorToInt(_state.Distance / Mathf.Max(0.01f, _settings.DistanceGoldDivisor)));
            AwardedGold += _distanceGoldBonus;
            if (success)
            {
                AwardedGold += _settings.BossGoldReward;
            }
        }

        private void TickResults(in RailShooterCommand command)
        {
            Keyboard keyboard = Keyboard.current;
            bool directConfirmation =
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                (keyboard != null &&
                 (keyboard.spaceKey.wasPressedThisFrame ||
                  keyboard.enterKey.wasPressedThisFrame ||
                  keyboard.numpadEnterKey.wasPressedThisFrame));
            bool continueRequested = _phaseElapsed >= _settings.ResultsSkipDelay &&
                (directConfirmation || command.FirePressed || command.TrickPressed || command.ConfirmPressed);
            if (!continueRequested)
            {
                return;
            }
            CompleteAndRestore();
        }

        private void CompleteAndRestore()
        {
            if (!IsActive)
            {
                return;
            }
            if (!_rewardCommitted && AwardedGold > 0)
            {
                _wallet?.AddGold(AwardedGold);
                _rewardCommitted = true;
            }
            bool success = _resultSuccess;
            int gold = AwardedGold;
            string grade = ResultGrade;
            _health.TemporaryHealthPoolDepleted -= HandleTemporaryHullDepleted;
            _health.Damaged -= HandlePlayerDamaged;
            _health.EndTemporaryHealthPool();
            RestoreWorldState();
            DuneVectorAudioManager.Instance?.ExitRailSubgameMusic();
            _modeRoot.gameObject.SetActive(false);
            Phase = RailShooterPhase.Inactive;
            IsAnyRailShooterActive = false;
            Action<bool, int, string> callback = _completed;
            _completed = null;
            callback?.Invoke(success, gold, grade);
        }

        private void AddScore(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            _state.Score = _state.Score > int.MaxValue - amount
                ? int.MaxValue
                : _state.Score + amount;
        }

        private void BuildPooledMode()
        {
            if (_modeRoot != null)
            {
                return;
            }
            _modeRoot = NewRoot("Post-Contract Rift Intercept - Isolated Mode", transform);
            _environmentRoot = NewRoot("Pooled Orbital Rift Course - No Desert", _modeRoot);
            _enemyRoot = NewRoot("Pooled Rail Enemy Formations", _modeRoot);
            _projectileRoot = NewRoot("Pooled Rail Projectiles", _modeRoot);
            _pickupRoot = NewRoot("Pooled Rail Pickups", _modeRoot);
            _effectsRoot = NewRoot("Pooled Rail Presentation", _modeRoot);
            BuildEnvironmentPool();
            BuildEnemyPool();
            BuildProjectilePools();
            BuildPickupPool();
            BuildEffectsPool();
            BuildRouteGates();
            BuildLaneWarning();
            BuildSigilSeeker();
            _modeRoot.gameObject.SetActive(false);
        }

        private void BuildEnvironmentPool()
        {
            for (int i = 0; i < _settings.EnvironmentSegmentCount; i++)
            {
                RiftSegment segment = new RiftSegment
                {
                    Root = NewRoot($"Rift Segment {i + 1:00}", _environmentRoot),
                };
                Transform gate = DuneVectorVisuals.CreateRingVisual(
                    segment.Root,
                    TraversalRingType.Flight,
                    _materials,
                    _settings.GateRadius,
                    _ringSettings,
                    faceForward: true);
                gate.name = "Procedural Rift Navigation Ring";
                RegisterRailRing(gate);
                _segments.Add(segment);
            }

            GameObject resourceSatellitePrefab = Resources.Load<GameObject>("SatellitePrefab");
            GameObject satellitePrefab = _settings.SatellitePrefab != null
                ? _settings.SatellitePrefab
                : resourceSatellitePrefab;
            if (satellitePrefab != null)
            {
                for (int i = 0; i < _settings.SatellitePoolSize; i++)
                {
                    GameObject root = InstantiateConfiguredPrefab(
                        satellitePrefab,
                        _environmentRoot,
                        "satellite");
                    if (root == null && resourceSatellitePrefab != null &&
                        satellitePrefab != resourceSatellitePrefab)
                    {
                        root = InstantiateConfiguredPrefab(
                            resourceSatellitePrefab,
                            _environmentRoot,
                            "Resources satellite fallback");
                    }
                    if (root == null)
                    {
                        break;
                    }
                    root.name = $"Destructible Satellite {i + 1:00} - Pooled";
                    DisableVisualPhysics(root.transform);
                    Vector3 fittedScale = FitSatelliteScale(root.transform);
                    Vector3 localTargetOffset = CalculateRendererCenterLocal(root.transform);
                    GameObject explosion = null;
                    ParticleSystem[] particles = Array.Empty<ParticleSystem>();
                    GameObject explosionPrefab = _settings.SatelliteExplosionPrefab != null
                        ? _settings.SatelliteExplosionPrefab
                        : Resources.Load<GameObject>("vfx/RedFireImpactV2 Satellite");
                    if (explosionPrefab != null)
                    {
                        explosion = InstantiateConfiguredPrefab(
                            explosionPrefab,
                            _effectsRoot,
                            "satellite explosion");
                        if (explosion != null)
                        {
                            explosion.name = $"Satellite Explosion {i + 1:00} - Pooled";
                            particles = explosion.GetComponentsInChildren<ParticleSystem>(true);
                            explosion.SetActive(false);
                        }
                    }
                    root.SetActive(false);
                    _satellites.Add(new RailSatellite
                    {
                        Root = root,
                        Transform = root.transform,
                        LocalTargetOffset = localTargetOffset,
                        Explosion = explosion,
                        ExplosionParticles = particles,
                        BaseScale = fittedScale,
                    });
                }
            }
            else
            {
                Debug.LogError("SatellitePrefab could not be loaded; no rail satellites were created.", this);
            }

            for (int i = 0; i < _settings.SpeedStreakPoolSize; i++)
            {
                Transform streak = CreatePart(
                    PrimitiveType.Cube,
                    $"Near-Camera Rift Streak {i + 1:00}",
                    _environmentRoot,
                    Vector3.zero,
                    Vector3.one,
                    Quaternion.identity,
                    _materials.DroneAccent);
                Renderer renderer = streak.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                _speedStreaks.Add(streak);
            }
        }

        private void BuildEnemyPool()
        {
            _boss = CreateRailEnemy(RailShooterEnemyKind.VesperKite, _settings.EnemyPoolSize, true);
            _boss.Root.name = "Vesper Sovereign Boss - Pooled";
            for (int i = 0; i < _settings.ScorePopupPoolSize; i++)
            {
                _popups.Add(new ScorePopup());
            }
        }

        private RailEnemy CreateRailEnemy(RailShooterEnemyKind kind, int identity, bool boss)
        {
            Transform root = NewRoot(
                boss ? "Boss Pool" : $"{kind} Rail Adaptation {identity + 1:00}",
                _enemyRoot);
            Transform visual = kind switch
            {
                RailShooterEnemyKind.SkyPiercer => DuneVectorVisuals.CreateFlyingEnemyVisual(
                    root, _materials, _skyPiercerSettings.VisualScale),
                RailShooterEnemyKind.GroundExploder => DuneVectorVisuals.CreateGroundExploderVisual(
                    root, _materials, _groundExploderSettings, _groundExploderSettings.VisualScale),
                RailShooterEnemyKind.StormPyramid => DuneVectorVisuals.CreateStormPyramidVisual(
                    root, _materials, _stormSettings),
                RailShooterEnemyKind.StrikeOrb => DuneVectorVisuals.CreatePlayerStrikeOrbVisual(
                    root, _materials, _strikeOrbSettings),
                _ => DuneVectorVisuals.CreateVesperKiteVisual(root, _materials, _vesperSettings),
            };
            DisableVisualPhysics(root);
            RailEnemy enemy = new RailEnemy
            {
                Root = root.gameObject,
                Transform = root,
                Visual = visual,
                Kind = kind,
                Boss = boss,
                Identity = identity,
                HitRadius = boss ? _settings.BossHitRadius : HitRadiusForKind(kind),
                ContactRadius = boss
                    ? _settings.BossCollisionRadius
                    : HitRadiusForKind(kind) * _settings.ContactRadiusFraction,
                HitFlashElapsed = float.PositiveInfinity,
                BaseScale = Vector3.one,
            };
            root.gameObject.SetActive(false);
            return enemy;
        }

        private void BuildBulletMaterials()
        {
            if (_bulletCoreMaterials != null)
            {
                return;
            }
            Color[] palette =
            {
                _settings.AimedBulletColor,
                _settings.RingBulletColor,
                _settings.WallBulletColor,
                _settings.WeaveBulletColor,
            };
            string[] names = { "Aimed", "Radial", "Curtain", "Weaving" };
            _bulletCoreMaterials = new Material[palette.Length];
            _bulletGlowMaterials = new Material[palette.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                _bulletCoreMaterials[i] = _materials.CreateRailBulletMaterial(
                    $"Rift Bullet Core - {names[i]}",
                    BulletCoreColor(palette[i]),
                    additive: false);
                _bulletGlowMaterials[i] = _materials.CreateRailBulletMaterial(
                    $"Rift Bullet Glow - {names[i]}",
                    BulletGlowColor(palette[i]),
                    additive: true);
            }
            _playerBoltCoreMaterial = _materials.CreateRailBulletMaterial(
                "Rift Player Bolt Core",
                BulletCoreColor(_settings.PlayerBoltColor),
                additive: false);
            _playerBoltGlowMaterial = _materials.CreateRailBulletMaterial(
                "Rift Player Bolt Glow",
                BulletGlowColor(_settings.PlayerBoltColor),
                additive: true);
        }

        // The core reads as the white-hot centre of a bullet, so it keeps the hue of its
        // family but is pushed most of the way toward white.
        private static Color BulletCoreColor(Color tint)
        {
            float peak = Mathf.Max(0.001f, tint.maxColorComponent);
            Color normalized = new Color(tint.r / peak, tint.g / peak, tint.b / peak);
            Color washed = Color.Lerp(normalized, Color.white, 0.62f);
            return new Color(washed.r * peak, washed.g * peak, washed.b * peak, 1f);
        }

        private Color BulletGlowColor(Color tint)
        {
            float fraction = Mathf.Max(0.01f, _settings.BulletHaloBrightnessFraction);
            return new Color(tint.r * fraction, tint.g * fraction, tint.b * fraction, fraction);
        }

        private void BuildProjectilePools()
        {
            BuildBulletMaterials();
            for (int i = 0; i < _settings.PlayerProjectilePoolSize; i++)
            {
                PooledProjectile bolt = CreateBullet(
                    $"Player Energy Bolt {i + 1:00}",
                    PrimitiveType.Cube,
                    includeCage: false);
                ApplyBulletStyle(bolt, _playerBoltCoreMaterial, _playerBoltGlowMaterial);
                _playerProjectiles.Add(bolt);
            }
            for (int i = 0; i < _settings.EnemyProjectilePoolSize; i++)
            {
                PooledProjectile bullet = CreateBullet(
                    $"Rift Bullet {i + 1:000}",
                    PrimitiveType.Sphere,
                    includeCage: true);
                ApplyBulletStyle(bullet, _bulletCoreMaterials[0], _bulletGlowMaterials[0]);
                _enemyProjectiles.Add(bullet);
            }
        }

        private PooledProjectile CreateBullet(string name, PrimitiveType coreShape, bool includeCage)
        {
            Transform root = NewRoot(name, _projectileRoot);
            Transform core = CreatePart(
                coreShape,
                "Core",
                root,
                Vector3.zero,
                Vector3.one,
                Quaternion.identity,
                _materials.EnemyCore);
            Transform cage = null;
            if (includeCage)
            {
                cage = CreatePart(
                    PrimitiveType.Cube,
                    "Spin Cage",
                    root,
                    Vector3.zero,
                    Vector3.one,
                    Quaternion.identity,
                    _materials.EnemyCore);
            }
            Transform halo = CreatePart(
                PrimitiveType.Sphere,
                "Halo",
                root,
                Vector3.zero,
                Vector3.one,
                Quaternion.identity,
                _materials.EnemyCore);
            TrailRenderer trail = root.gameObject.AddComponent<TrailRenderer>();
            trail.time = _settings.BulletTrailDuration;
            trail.minVertexDistance = 0.35f;
            trail.autodestruct = false;
            trail.emitting = false;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

            PooledProjectile projectile = new PooledProjectile
            {
                Root = root.gameObject,
                Transform = root,
                Core = core,
                Cage = cage,
                Halo = halo,
                CoreRenderer = core.GetComponent<Renderer>(),
                CageRenderer = cage != null ? cage.GetComponent<Renderer>() : null,
                HaloRenderer = halo.GetComponent<Renderer>(),
                Trail = trail,
            };
            projectile.Root.SetActive(false);
            return projectile;
        }

        private static void ApplyBulletStyle(PooledProjectile projectile, Material core, Material glow)
        {
            if (projectile.CoreRenderer != null)
            {
                projectile.CoreRenderer.sharedMaterial = core;
            }
            if (projectile.CageRenderer != null)
            {
                projectile.CageRenderer.sharedMaterial = glow;
            }
            if (projectile.HaloRenderer != null)
            {
                projectile.HaloRenderer.sharedMaterial = glow;
            }
            if (projectile.Trail != null)
            {
                projectile.Trail.sharedMaterial = glow;
            }
        }

        private void BuildPickupPool()
        {
            for (int i = 0; i < _settings.PickupPoolSize; i++)
            {
                PickupKind kind = (PickupKind)(i % 3);
                Transform root = NewRoot($"{kind} Rail Pickup {i + 1:00}", _pickupRoot);
                TraversalRingType ringType = kind switch
                {
                    PickupKind.Gold => TraversalRingType.Coin,
                    PickupKind.Health => TraversalRingType.Health,
                    _ => TraversalRingType.UpperFlight,
                };
                Transform pickupRing = DuneVectorVisuals.CreateRingVisual(
                    root,
                    ringType,
                    _materials,
                    _settings.PickupRadius,
                    _ringSettings,
                    faceForward: true);
                if (kind == PickupKind.Health)
                {
                    Transform heart = pickupRing.Find("Collectible Icon");
                    if (heart != null)
                    {
                        heart.localScale = Vector3.one * _settings.PickupHealthHeartScaleMultiplier;
                        // Apply the final roll in the screen-facing ring's plane after the imported
                        // GLB orientation. This makes the visual flip deterministic regardless of
                        // which local axis the source mesh used as its authored up direction.
                        heart.localRotation = Quaternion.AngleAxis(
                            _settings.PickupHealthHeartScreenRotationDegrees,
                            Vector3.forward) * Quaternion.Euler(_settings.PickupHealthHeartEulerAngles);
                    }
                }
                RegisterRailRing(pickupRing);
                if (kind == PickupKind.Bomb)
                {
                    CreatePart(
                        PrimitiveType.Sphere,
                        "Bomb Charge Core",
                        root,
                        Vector3.zero,
                        Vector3.one * (_settings.PickupRadius * 0.42f),
                        Quaternion.identity,
                        _materials.EnemyCore);
                }
                PooledPickup pickup = new PooledPickup
                {
                    Root = root.gameObject,
                    Transform = root,
                    Kind = kind,
                };
                pickup.Root.SetActive(false);
                _pickups.Add(pickup);
            }
        }

        private void BuildEffectsPool()
        {
            _chargeVisual = CreatePart(
                PrimitiveType.Sphere,
                "Premium Drone Charge Core",
                _effectsRoot,
                Vector3.zero,
                Vector3.one,
                Quaternion.identity,
                _materials.DroneAccent);
            _chargedBeamVisual = CreatePart(
                PrimitiveType.Cube,
                "Charged Penetration Beam",
                _effectsRoot,
                Vector3.zero,
                Vector3.one,
                Quaternion.identity,
                _materials.DroneAccent);
            _bombVisual = CreatePart(
                PrimitiveType.Sphere,
                "Bomb Shockwave",
                _effectsRoot,
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                _materials.LightningWarning);
            _chargeVisual.gameObject.SetActive(false);
            _chargedBeamVisual.gameObject.SetActive(false);
            _bombVisual.gameObject.SetActive(false);
            for (int i = 0; i < _settings.ImpactFlashPoolSize; i++)
            {
                Transform impactTransform = CreatePart(
                    PrimitiveType.Sphere,
                    $"Pooled Impact Flash {i + 1:00}",
                    _effectsRoot,
                    Vector3.zero,
                    Vector3.one,
                    Quaternion.identity,
                    _materials.EnemyCore);
                PooledImpact impact = new PooledImpact
                {
                    Root = impactTransform.gameObject,
                    Transform = impactTransform,
                };
                impact.Root.SetActive(false);
                _impacts.Add(impact);
            }
        }

        private void BuildRouteGates()
        {
            _safeGate = NewRoot("Signal Route Gate - Repair", _environmentRoot);
            _riskGate = NewRoot("Black Route Gate - Elite Reward", _environmentRoot);
            Transform safeRing = DuneVectorVisuals.CreateRingVisual(
                _safeGate,
                TraversalRingType.Flight,
                _materials,
                _settings.BranchGateRadius,
                _ringSettings,
                faceForward: true);
            Transform riskRing = DuneVectorVisuals.CreateRingVisual(
                _riskGate,
                TraversalRingType.UpperFlight,
                _materials,
                _settings.BranchGateRadius,
                _ringSettings,
                faceForward: true);
            RegisterRailRing(safeRing);
            RegisterRailRing(riskRing);
            _safeGate.gameObject.SetActive(false);
            _riskGate.gameObject.SetActive(false);
        }

        private void RegisterRailRing(Transform ring)
        {
            if (ring == null)
            {
                return;
            }
            _railRings.Add(ring);
            Renderer[] renderers = ring.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                string layerName = renderers[i].gameObject.name;
                if (string.Equals(layerName, "Portal Drop Shadow", StringComparison.Ordinal) ||
                    string.Equals(layerName, "Portal Contrast Outline", StringComparison.Ordinal))
                {
                    renderers[i].enabled = false;
                }
            }
        }

        private void BuildLaneWarning()
        {
            GameObject warning = new GameObject("Pooled Storm Lightning Lane");
            warning.transform.SetParent(_effectsRoot, false);
            _laneWarning = warning.AddComponent<LineRenderer>();
            _laneWarning.positionCount = 2;
            _laneWarning.sharedMaterial = _materials.LightningWarning;
            _laneWarning.textureMode = LineTextureMode.Stretch;
            _laneWarning.numCapVertices = 4;
            warning.SetActive(false);
        }

        private void ResetPools()
        {
            if (_modeRoot != null)
            {
                _modeRoot.position = Vector3.zero;
            }
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i].Active = false;
                _enemies[i].Root.SetActive(false);
            }
            if (_boss != null)
            {
                _boss.Active = false;
                _boss.Root.SetActive(false);
            }
            for (int i = 0; i < _playerProjectiles.Count; i++) DeactivateProjectile(_playerProjectiles[i]);
            for (int i = 0; i < _enemyProjectiles.Count; i++) DeactivateProjectile(_enemyProjectiles[i]);
            for (int i = 0; i < _pickups.Count; i++) DeactivatePickup(_pickups[i]);
            for (int i = 0; i < _impacts.Count; i++)
            {
                _impacts[i].Active = false;
                _impacts[i].Root.SetActive(false);
            }
            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                satellite.Active = false;
                satellite.Exploding = false;
                satellite.Root.SetActive(false);
                satellite.Explosion?.SetActive(false);
            }
            _formations.Clear();
            for (int i = 0; i < _popups.Count; i++)
            {
                _popups[i].Active = false;
            }
            _chargeVisual.gameObject.SetActive(false);
            _chargedBeamVisual.gameObject.SetActive(false);
            _bombVisual.gameObject.SetActive(false);
            _safeGate.gameObject.SetActive(false);
            _riskGate.gameObject.SetActive(false);
            _laneWarning.gameObject.SetActive(false);
            if (_sigilRoot != null)
            {
                _sigilRoot.gameObject.SetActive(false);
            }
        }

        private void ResetCourse()
        {
            _furthestSegmentZ = _startZ;
            for (int i = 0; i < _segments.Count; i++)
            {
                _furthestSegmentZ += _settings.EnvironmentSegmentSpacing;
                ResetSegment(_segments[i], _furthestSegmentZ, i);
            }
            _furthestSatelliteZ = _player.transform.position.z +
                _settings.SatelliteSpawnAheadDistance - _settings.SatelliteSpacing;
            for (int i = 0; i < _satellites.Count; i++)
            {
                _furthestSatelliteZ += _settings.SatelliteSpacing;
                ResetSatellite(_satellites[i], _furthestSatelliteZ, i);
            }
            for (int i = 0; i < _speedStreaks.Count; i++)
            {
                ResetSpeedStreak(_speedStreaks[i], i);
            }
        }

        private void ResetSegment(RiftSegment segment, float z, int identity)
        {
            float extent = Mathf.Max(1f, _settings.ProceduralPlaneHalfExtent);
            float depthJitter = Mathf.Max(0f, _settings.ProceduralRingDepthJitter);
            segment.PlaneOffset = EvenPlaneOffset(identity, extent, 0);
            Vector2 planeCenter = CurrentFlightPlaneCenter();
            segment.Root.position = new Vector3(
                planeCenter.x + segment.PlaneOffset.x,
                planeCenter.y + segment.PlaneOffset.y,
                z + NextFloat(-depthJitter, depthJitter));
            segment.Root.rotation = Quaternion.identity;
        }

        private Vector2 CurrentFlightPlaneCenter()
        {
            return new Vector2(
                _arenaOrigin.x + _state.FlightOffset.x,
                _arenaOrigin.y + _state.FlightOffset.y);
        }

        private void ResetSpeedStreak(Transform streak, int identity)
        {
            if (_camera == null)
            {
                streak.position = Vector3.zero;
                return;
            }
            float length = NextFloat(
                _settings.SpeedStreakMinimumLength,
                _settings.SpeedStreakMaximumLength);
            float angle = NextFloat(0f, Mathf.PI * 2f);
            float radius = NextFloat(
                _settings.SpeedStreakConeInnerRadius,
                _settings.SpeedStreakConeOuterRadius);
            // Seed the streaks in a ring around the view axis and point them down the ray they
            // sit on, so they read as warp lines converging on the vanishing point.
            Vector3 local = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                NextFloat(_settings.SpeedStreakNearDistance, _settings.SpeedStreakDepth));
            streak.position = _camera.transform.position + local;
            streak.rotation = Quaternion.LookRotation(local.normalized, Vector3.up);
            streak.localScale = new Vector3(
                _settings.SpeedStreakWidth,
                _settings.SpeedStreakWidth,
                length);
        }

        private void SaveWorldState()
        {
            _savedPlayerPosition = _player.transform.position;
            _savedPlayerRotation = _player.transform.rotation;
            _savedVisualScale = _player.DroneVisualRoot != null
                ? _player.DroneVisualRoot.localScale
                : Vector3.one;
            _savedMotorEnabled = _player.Motor != null && _player.Motor.enabled;
            _savedInputEnabled = _input != null && _input.InputEnabled;
            _savedWorldEnabled = _world != null && _world.enabled;
            _savedCameraControllerEnabled = _cameraController != null && _cameraController.enabled;
            _savedCameraPosition = _camera.transform.position;
            _savedCameraRotation = _camera.transform.rotation;
            _savedClearFlags = _camera.clearFlags;
            _savedBackgroundColor = _camera.backgroundColor;
            _savedFieldOfView = _camera.fieldOfView;
            _savedSkybox = RenderSettings.skybox;
            _savedFogEnabled = RenderSettings.fog;
            _savedFogMode = RenderSettings.fogMode;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogStartDistance = RenderSettings.fogStartDistance;
            _savedFogEndDistance = RenderSettings.fogEndDistance;
        }

        private void EnterRailPresentation()
        {
            _input?.SetInputEnabled(false);
            if (_player.Motor != null)
            {
                _player.Motor.SetPositionAndRotation(_arenaOrigin, Quaternion.identity, true);
                _player.Motor.enabled = false;
            }
            _player.transform.SetPositionAndRotation(_arenaOrigin, Quaternion.identity);
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _authoredVisualScale;
            }
            if (_world != null) _world.enabled = false;
            if (_cameraController != null) _cameraController.enabled = false;
            _camera.clearFlags = _settings.RailSkybox != null
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            _camera.backgroundColor = _settings.RiftBackgroundColor;
            if (_settings.RailSkybox != null)
            {
                RenderSettings.skybox = _settings.RailSkybox;
                DynamicGI.UpdateEnvironment();
            }
            _camera.fieldOfView = _settings.CameraFieldOfView;
            _camera.transform.position = _arenaOrigin + _settings.CameraLocalOffset;
            _camera.transform.rotation = Quaternion.identity;
            _cameraBasePosition = _camera.transform.position;
            ApplyRailMassiveCloudOverride();
            if (_settings.RiftFogEnabled)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = _settings.RiftFogColor;
                RenderSettings.fogStartDistance = _settings.RiftFogStartDistance;
                RenderSettings.fogEndDistance = _settings.RiftFogEndDistance;
            }
            ApplyRailCursorState();
        }

        private void ResetSatellite(RailSatellite satellite, float z, int sequence)
        {
            int pathInterval = Mathf.Max(1, _settings.SatellitePathSpawnInterval);
            bool obstructFlightPath = Mathf.Abs(sequence) % pathInterval == 0;
            if (obstructFlightPath)
            {
                // Deliberate obstruction. It is spread across the whole box the drone can actually
                // reach rather than a small patch on the centre line: an obstacle parked near the
                // axis contests nothing once the player flies out to the edge of FlightBounds.
                satellite.PlaneOffset = EvenPlaneOffset(
                    sequence,
                    _settings.FlightBounds * Mathf.Clamp01(_settings.SatellitePathBoundsFraction),
                    5);
            }
            else
            {
                // Surrounding scenery, outside the reachable box.
                satellite.PlaneOffset = EvenPlaneOffset(
                    sequence,
                    Mathf.Max(0f, _settings.SatellitePlaneHalfExtent),
                    17);
            }
            Vector2 center = CurrentFlightPlaneCenter();
            satellite.Transform.position = new Vector3(
                center.x + satellite.PlaneOffset.x,
                center.y + satellite.PlaneOffset.y,
                z);
            satellite.Transform.rotation = Quaternion.Euler(
                NextFloat(0f, 360f),
                NextFloat(0f, 360f),
                NextFloat(0f, 360f));
            satellite.RotationAxis = new Vector3(
                NextFloat(-1f, 1f),
                NextFloat(-1f, 1f),
                NextFloat(-1f, 1f)).normalized;
            if (satellite.RotationAxis.sqrMagnitude < 0.01f)
            {
                satellite.RotationAxis = Vector3.up;
            }
            satellite.Transform.localScale = satellite.BaseScale;
            satellite.Health = _settings.SatelliteHealth;
            satellite.Active = true;
            satellite.Exploding = false;
            satellite.Explosion?.SetActive(false);
            satellite.Root.SetActive(true);
        }

        // The Halton points keep the field evenly spread, but on their own they depend only on the
        // spawn index, so every run laid the corridor and the satellites out in exactly the same
        // places. The per-run rotation shifts the whole sequence without clumping it.
        private Vector2 EvenPlaneOffset(int identity, float halfExtent, int sequenceSalt)
        {
            return EvenPlaneOffset(identity, new Vector2(halfExtent, halfExtent), sequenceSalt);
        }

        private Vector2 EvenPlaneOffset(int identity, Vector2 halfExtent, int sequenceSalt)
        {
            int sequence = Mathf.Abs(identity) + Mathf.Max(0, sequenceSalt) + 1;
            float x = Mathf.Repeat(RadicalInverse(sequence, 2) + _planeRotation.x, 1f);
            float y = Mathf.Repeat(RadicalInverse(sequence, 3) + _planeRotation.y, 1f);
            return new Vector2(((x * 2f) - 1f) * halfExtent.x, ((y * 2f) - 1f) * halfExtent.y);
        }

        private static float RadicalInverse(int value, int radix)
        {
            float inverseRadix = 1f / Mathf.Max(2, radix);
            float fraction = inverseRadix;
            float result = 0f;
            while (value > 0)
            {
                result += (value % radix) * fraction;
                value /= radix;
                fraction *= inverseRadix;
            }
            return result;
        }

        private Vector3 FitSatelliteScale(Transform root)
        {
            root.localScale = Vector3.one;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.one * _settings.SatelliteVisualScale;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            float maximumDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float fitted = maximumDimension > 0.001f
                ? _settings.SatelliteVisualScale / maximumDimension
                : _settings.SatelliteVisualScale;
            return Vector3.one * Mathf.Max(0.001f, fitted);
        }

        private static Vector3 CalculateRendererCenterLocal(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.zero;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return root.InverseTransformPoint(bounds.center);
        }

        private static Vector3 SatelliteTargetPosition(RailSatellite satellite)
        {
            return satellite.Transform.TransformPoint(satellite.LocalTargetOffset);
        }

        private static void ApplyRailCursorState()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ApplyActiveRailCursorState()
        {
            if (_sigilActive)
            {
                ApplySigilDrawingCursorState();
                return;
            }
            ApplyRailCursorState();
        }

        private static void ApplySigilDrawingCursorState()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
        }

        private void RestoreWorldState()
        {
            if (_player.DroneVisualRoot != null)
            {
                _player.DroneVisualRoot.localScale = _savedVisualScale;
            }
            if (_player.Motor != null)
            {
                _player.Motor.enabled = _savedMotorEnabled;
                _player.Motor.SetPositionAndRotation(_savedPlayerPosition, _savedPlayerRotation, true);
            }
            _player.transform.SetPositionAndRotation(_savedPlayerPosition, _savedPlayerRotation);
            if (_world != null) _world.enabled = _savedWorldEnabled;
            _camera.clearFlags = _savedClearFlags;
            _camera.backgroundColor = _savedBackgroundColor;
            _camera.fieldOfView = _savedFieldOfView;
            RenderSettings.skybox = _savedSkybox;
            DynamicGI.UpdateEnvironment();
            _camera.transform.SetPositionAndRotation(_savedCameraPosition, _savedCameraRotation);
            if (_cameraController != null)
            {
                _cameraController.enabled = _savedCameraControllerEnabled;
                _cameraController.SnapToTarget(_savedPlayerRotation * Vector3.forward);
            }
            RenderSettings.fog = _savedFogEnabled;
            RenderSettings.fogMode = _savedFogMode;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogStartDistance = _savedFogStartDistance;
            RenderSettings.fogEndDistance = _savedFogEndDistance;
            RestoreMassiveCloudParameters();
            Cursor.lockState = _savedInputEnabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_savedInputEnabled;
            _input?.SetInputEnabled(_savedInputEnabled);
        }

        private void ApplyRailMassiveCloudOverride()
        {
            _savedMassiveCloudParameters.Clear();
            _massiveClouds = null;
            if (!_settings.OverrideMassiveCloudsDuringSubgame || _camera == null)
            {
                return;
            }

            MonoBehaviour[] cameraBehaviours = _camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < cameraBehaviours.Length; i++)
            {
                MonoBehaviour behaviour = cameraBehaviours[i];
                if (behaviour != null && behaviour.GetType().FullName == "Mewlist.MassiveClouds")
                {
                    _massiveClouds = behaviour;
                    break;
                }
            }
            IList parameters = GetMassiveCloudParameters();
            if (parameters == null)
            {
                _massiveClouds = null;
                return;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                object parameter = parameters[i];
                _savedMassiveCloudParameters.Add(parameter);
                Type parameterType = parameter.GetType();
                parameterType.GetField("RelativeHeight")?.SetValue(parameter, true);
                parameterType.GetField("FromHeight")?.SetValue(
                    parameter,
                    _settings.MassiveCloudsRelativeFromHeight);
                parameterType.GetField("ToHeight")?.SetValue(
                    parameter,
                    _settings.MassiveCloudsRelativeToHeight);
                parameters[i] = parameter;
            }
        }

        private void RestoreMassiveCloudParameters()
        {
            IList parameters = GetMassiveCloudParameters();
            if (parameters != null && parameters.Count == _savedMassiveCloudParameters.Count)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    parameters[i] = _savedMassiveCloudParameters[i];
                }
            }
            _savedMassiveCloudParameters.Clear();
            _massiveClouds = null;
        }

        private IList GetMassiveCloudParameters()
        {
            return _massiveClouds?.GetType().GetProperty("Parameters")?.GetValue(_massiveClouds) as IList;
        }

        private RailEnemy AcquireEnemy(RailShooterEnemyKind kind)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (!_enemies[i].Active && _enemies[i].Kind == kind)
                {
                    return _enemies[i];
                }
            }
            return null;
        }

        private static PooledProjectile AcquireProjectile(List<PooledProjectile> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].Active)
                {
                    return pool[i];
                }
            }
            return null;
        }

        private PooledPickup AcquirePickup(PickupKind kind)
        {
            for (int i = 0; i < _pickups.Count; i++)
            {
                if (!_pickups[i].Active && _pickups[i].Kind == kind)
                {
                    return _pickups[i];
                }
            }
            return null;
        }

        private static void DeactivateProjectile(PooledProjectile projectile)
        {
            if (projectile == null)
            {
                return;
            }
            projectile.Active = false;
            if (projectile.Trail != null)
            {
                projectile.Trail.emitting = false;
                projectile.Trail.Clear();
            }
            projectile.Root.SetActive(false);
        }

        private static void DeactivatePickup(PooledPickup pickup)
        {
            if (pickup == null)
            {
                return;
            }
            pickup.Active = false;
            pickup.Root.SetActive(false);
        }

        private FormationRecord FindFormation(int id)
        {
            for (int i = 0; i < _formations.Count; i++)
            {
                if (_formations[i].Id == id)
                {
                    return _formations[i];
                }
            }
            return null;
        }

        private float ChargeNormalized()
        {
            return Mathf.Clamp01(_state.ChargeElapsed / Mathf.Max(0.01f, _settings.ChargeFullDuration));
        }

        private float NextFloat(float minimum, float maximum)
        {
            if (_random == null)
            {
                return minimum;
            }
            return Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
        }

        private int NextInt(int minimum, int maximumExclusive)
        {
            return _random != null && maximumExclusive > minimum
                ? _random.Next(minimum, maximumExclusive)
                : minimum;
        }

        private static float SoftClamp(float value, float limit, float softness)
        {
            float clamped = Mathf.Clamp(value, -limit, limit);
            if (softness <= 0f)
            {
                return clamped;
            }
            float edgeStart = Mathf.Max(0f, limit - softness);
            float absolute = Mathf.Abs(clamped);
            if (absolute <= edgeStart)
            {
                return clamped;
            }
            float edge = Mathf.InverseLerp(edgeStart, limit, absolute);
            float eased = Mathf.Lerp(edgeStart, limit, Mathf.SmoothStep(0f, 1f, edge));
            return Mathf.Sign(clamped) * eased;
        }

        public static Vector2 CalculateRestingAimViewport(
            Vector2 flightOffset,
            Vector2 flightBounds,
            float regionFraction)
        {
            Vector2 safeBounds = new Vector2(
                Mathf.Max(0.001f, Mathf.Abs(flightBounds.x)),
                Mathf.Max(0.001f, Mathf.Abs(flightBounds.y)));
            Vector2 normalizedPosition = new Vector2(
                Mathf.Clamp(flightOffset.x / safeBounds.x, -1f, 1f),
                Mathf.Clamp(flightOffset.y / safeBounds.y, -1f, 1f));
            return Vector2.one * 0.5f +
                (normalizedPosition * (Mathf.Clamp01(regionFraction) * 0.5f));
        }

        private static bool SegmentIntersectsSphere(
            Vector3 start,
            Vector3 end,
            Vector3 center,
            float radius,
            out float distance)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            float t = lengthSquared > Mathf.Epsilon
                ? Mathf.Clamp01(Vector3.Dot(center - start, segment) / lengthSquared)
                : 0f;
            Vector3 closest = start + (segment * t);
            distance = Vector3.Distance(start, closest);
            return (closest - center).sqrMagnitude <= radius * radius;
        }

        private static Transform NewRoot(string name, Transform parent)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private static Transform CreatePart(
            PrimitiveType primitive,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
            return part.transform;
        }

        private static void DisableVisualPhysics(Transform root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
            }
        }

        // The null sigil duel. A seeker fades in ahead of the drone and counts down while
        // ModularSphereMissile becomes a full-screen tablet cursor. The player holds fire to
        // paint one complete glyph, then releases to submit the entire normalized path.
        // Powered seekers demand a chain of separately submitted glyphs under one timer.

        private void BuildSigilSeeker()
        {
            _sigilRoot = NewRoot("Pooled Null Sigil Seeker", _effectsRoot);
            GameObject resourceMissilePrefab = Resources.Load<GameObject>("MissilePrefab");
            GameObject missilePrefab = _settings.Sigils.SeekerPrefab != null
                ? _settings.Sigils.SeekerPrefab
                : resourceMissilePrefab;
            GameObject missile = InstantiateConfiguredPrefab(
                missilePrefab,
                _sigilRoot,
                "sigil missile");
            if (missile == null && resourceMissilePrefab != null &&
                missilePrefab != resourceMissilePrefab)
            {
                missile = InstantiateConfiguredPrefab(
                    resourceMissilePrefab,
                    _sigilRoot,
                    "Resources sigil missile fallback");
            }
            if (missile != null)
            {
                missile.name = "MissilePrefab Sigil Seeker";
                missile.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.Euler(_settings.Sigils.SeekerPrefabLocalEulerAngles));
                missile.transform.localScale = FitVisualToMaximumDimension(
                    missile.transform,
                    _settings.Sigils.SeekerPrefabMaximumDimension);
                DisableVisualPhysics(missile.transform);
            }
            else
            {
                Debug.LogError("MissilePrefab could not be loaded for the rail sigil seeker.", this);
            }
            _sigilHalo = null;
            _sigilCage = null;
            _sigilRoot.gameObject.SetActive(false);
            BuildSigilDrawingCursor();
        }

        private void BuildSigilDrawingCursor()
        {
            GameObject cursorPrefab = _strikeOrbSettings != null &&
                _strikeOrbSettings.UseModularSphereMissileVisual
                    ? _strikeOrbSettings.ModularSphereMissilePrefab
                    : null;
            cursorPrefab ??= Resources.Load<GameObject>("vfx/ModularSphereMissile");
            GameObject cursor = InstantiateConfiguredPrefab(
                cursorPrefab,
                _effectsRoot,
                "ModularSphereMissile drawing cursor");
            if (cursor == null)
            {
                Debug.LogError("ModularSphereMissile could not be loaded for the rail drawing cursor.", this);
                return;
            }
            cursor.name = "ModularSphereMissile Full-Screen Drawing Cursor";
            cursor.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            cursor.transform.localScale = FitVisualToMaximumDimension(
                cursor.transform,
                _settings.Sigils.DrawingCursorMaximumDimension);
            DisableVisualPhysics(cursor.transform);
            _sigilDrawingCursor = cursor.transform;
            cursor.SetActive(false);
        }

        private static Vector3 FitVisualToMaximumDimension(Transform root, float targetMaximumDimension)
        {
            root.localScale = Vector3.one;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return Vector3.one * targetMaximumDimension;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            float maximumDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float fittedScale = maximumDimension > 0.001f
                ? targetMaximumDimension / maximumDimension
                : targetMaximumDimension;
            return Vector3.one * fittedScale;
        }

        private GameObject InstantiateConfiguredPrefab(GameObject prefab, Transform parent, string label)
        {
            if (prefab == null)
            {
                return null;
            }
            try
            {
                return Instantiate(prefab, parent);
            }
            catch (InvalidCastException exception)
            {
                Debug.LogError(
                    $"Rail {label} reference does not resolve to a prefab GameObject. " +
                    "The rail mode will continue without that visual.",
                    this);
                Debug.LogException(exception, this);
                return null;
            }
        }

        private void ResetSigilDuel()
        {
            _sigilActive = false;
            _sigilChain = false;
            _sigilElapsed = 0f;
            _sigilDuration = 0f;
            _sigilDemand.Clear();
            _sigilSymbolIndex = 0;
            CancelSigilDrawingAttempt();
            _sigilCursorScreen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _sigilPlaneOffset = Vector2.zero;
            _sigilFaultElapsed = float.PositiveInfinity;
            _lastSigilFaultCueAt = float.NegativeInfinity;
            _sigilVerdictElapsed = float.PositiveInfinity;
            _sigilVerdictBroken = false;
            _sigilAttackCount = 0;
            _sigilChainCycle = 0;
            _sigilNextAttackDistance = _settings.Sigils.FirstAttackDistance;
            _sigilNextBossAttackAt = float.PositiveInfinity;
            if (_sigilRoot != null)
            {
                _sigilRoot.gameObject.SetActive(false);
            }
            if (_sigilDrawingCursor != null)
            {
                _sigilDrawingCursor.gameObject.SetActive(false);
            }
        }

        private void TickSigilDirector()
        {
            RailSigilTuning sigils = _settings.Sigils;
            if (!sigils.Enabled || _sigilActive || Phase == RailShooterPhase.Results)
            {
                return;
            }
            if (Phase == RailShooterPhase.Boss)
            {
                if (sigils.BossAttackInterval > 0f && _state.Elapsed >= _sigilNextBossAttackAt)
                {
                    BeginSigilAttack();
                }
                return;
            }
            if (Phase == RailShooterPhase.Combat && _state.Distance >= _sigilNextAttackDistance)
            {
                BeginSigilAttack();
            }
        }

        private void BeginSigilAttack()
        {
            RailSigilTuning sigils = _settings.Sigils;
            if (sigils.Symbols == null || sigils.Symbols.Count == 0)
            {
                return;
            }
            _sigilAttackCount++;
            _sigilChain = sigils.ChainEveryAttacks > 1 &&
                _sigilAttackCount % sigils.ChainEveryAttacks == 0 &&
                sigils.ChainSymbolCounts != null &&
                sigils.ChainSymbolCounts.Count > 0;
            int demandCount = 1;
            if (_sigilChain)
            {
                demandCount = Mathf.Max(
                    2,
                    sigils.ChainSymbolCounts[_sigilChainCycle % sigils.ChainSymbolCounts.Count]);
                _sigilChainCycle++;
            }

            int maximumStrokes = CurrentSigilStrokeCeiling();
            _sigilDemand.Clear();
            float duration = 0f;
            for (int i = 0; i < demandCount; i++)
            {
                RailSigilDefinition definition = PickSigilDefinition(maximumStrokes);
                if (definition == null)
                {
                    break;
                }
                _sigilDemand.Add(definition);
                duration += sigils.CountdownForStrokes(definition.StrokeCount);
            }
            if (_sigilDemand.Count == 0)
            {
                return;
            }
            if (_sigilChain)
            {
                // One timer covers the whole chain, and it is tighter than drawing each
                // glyph on its own would be.
                duration *= sigils.ChainTimeFraction;
            }

            _sigilActive = true;
            _sigilDuration = Mathf.Max(0.5f, duration);
            _sigilElapsed = 0f;
            _sigilSymbolIndex = 0;
            CancelSigilDrawingAttempt();
            _sigilCursorScreen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            ApplySigilDrawingCursorState();
            Mouse.current?.WarpCursorPosition(new Vector2(
                _sigilCursorScreen.x,
                Screen.height - _sigilCursorScreen.y));
            _sigilFaultElapsed = float.PositiveInfinity;
            _sigilVerdictElapsed = float.PositiveInfinity;
            float spawnAngle = NextFloat(0f, Mathf.PI * 2f);
            _sigilPlaneOffset = new Vector2(Mathf.Cos(spawnAngle), Mathf.Sin(spawnAngle)) *
                sigils.SpawnLateralSpread;
            UpdateSigilTransform();
            _sigilRoot.gameObject.SetActive(true);
            if (_sigilDrawingCursor != null)
            {
                _sigilDrawingCursor.gameObject.SetActive(true);
                UpdateSigilDrawingCursorWorld();
            }
            PlayCue(sigils.SpawnEvent, _sigilRoot.position);
        }

        private int CurrentSigilStrokeCeiling()
        {
            RailSigilTuning sigils = _settings.Sigils;
            int depthUnlocks = Mathf.FloorToInt(
                _state.Distance / Mathf.Max(1f, sigils.StrokeUnlockDistance));
            int difficultyUnlocks = Mathf.FloorToInt(
                DifficultyLevels() * sigils.DifficultyStrokesPerLevel);
            return Mathf.Clamp(
                sigils.StartingMaximumStrokes + depthUnlocks + difficultyUnlocks,
                1,
                sigils.MaximumStrokes);
        }

        private RailSigilDefinition PickSigilDefinition(int maximumStrokes)
        {
            List<RailSigilDefinition> symbols = _settings.Sigils.Symbols;
            _sigilCandidates.Clear();
            RailSigilDefinition shortest = null;
            for (int i = 0; i < symbols.Count; i++)
            {
                RailSigilDefinition definition = symbols[i];
                if (definition == null || definition.StrokeCount < 1)
                {
                    continue;
                }
                if (shortest == null || definition.StrokeCount < shortest.StrokeCount)
                {
                    shortest = definition;
                }
                if (definition.StrokeCount <= maximumStrokes && !_sigilDemand.Contains(definition))
                {
                    _sigilCandidates.Add(definition);
                }
            }
            if (_sigilCandidates.Count == 0)
            {
                return shortest;
            }
            return _sigilCandidates[NextInt(0, _sigilCandidates.Count)];
        }

        private void TickSigilDuel(in RailShooterCommand command, float deltaTime)
        {
            AdvanceTimer(ref _sigilVerdictElapsed, deltaTime, _settings.Sigils.VerdictDuration);
            AdvanceTimer(ref _sigilFaultElapsed, deltaTime, _settings.Sigils.FaultFlashDuration);
            if (!_sigilActive)
            {
                return;
            }
            _sigilElapsed += deltaTime;
            float speedScale = _sigilChain ? _settings.Sigils.ChainSpeedMultiplier : 1f;
            _sigilPlaneOffset = Vector2.MoveTowards(
                _sigilPlaneOffset,
                Vector2.zero,
                _settings.Sigils.HomingLateralSpeed * speedScale * deltaTime);
            UpdateSigilTransform();
            if (_sigilCage != null)
            {
                _sigilCage.Rotate(
                    0f,
                    0f,
                    _settings.Sigils.SeekerSpinSpeed * speedScale * deltaTime,
                    Space.Self);
            }
            TickSigilDrawing(command, deltaTime);
            if (_sigilActive && _sigilElapsed >= _sigilDuration)
            {
                StrikeWithSigil();
            }
        }

        private void UpdateSigilTransform()
        {
            if (_sigilRoot == null)
            {
                return;
            }
            RailSigilTuning sigils = _settings.Sigils;
            // The approach is driven by the countdown, so the seeker always arrives exactly
            // when the timer runs out no matter how the drone is flying.
            float normalized = Mathf.Clamp01(_sigilElapsed / Mathf.Max(0.01f, _sigilDuration));
            float gap = Mathf.Lerp(sigils.SpawnAheadDistance, sigils.StrikeDistance, normalized);
            Vector3 playerPosition = _player.transform.position;
            _sigilRoot.position = new Vector3(
                playerPosition.x + _sigilPlaneOffset.x,
                playerPosition.y + _sigilPlaneOffset.y,
                playerPosition.z + gap);
            float pulse = 1f + (Mathf.Sin(_sigilElapsed * sigils.SeekerPulseSpeed) * sigils.SeekerPulseAmount);
            float scale = sigils.SeekerRadius * pulse * (_sigilChain ? 1.4f : 1f);
            _sigilRoot.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            if (_sigilHalo != null)
            {
                _sigilHalo.gameObject.SetActive(_sigilChain);
            }
        }

        private void TickSigilDrawing(in RailShooterCommand command, float deltaTime)
        {
            RailSigilTuning sigils = _settings.Sigils;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 pointerPosition = mouse.position.ReadValue();
                _sigilCursorScreen = new Vector2(pointerPosition.x, Screen.height - pointerPosition.y);
            }
            else
            {
                _sigilCursorScreen += new Vector2(command.Look.x, -command.Look.y) *
                    sigils.MouseStrokeSensitivity;
            }
            Vector2 stickDelta = new Vector2(command.Stick.x, -command.Stick.y) *
                sigils.StickStrokeSpeed * deltaTime;
            _sigilCursorScreen += stickDelta;
            float margin = Mathf.Max(0f, sigils.DrawingCursorScreenMargin);
            _sigilCursorScreen.x = Mathf.Clamp(_sigilCursorScreen.x, margin, Screen.width - margin);
            _sigilCursorScreen.y = Mathf.Clamp(_sigilCursorScreen.y, margin, Screen.height - margin);
            if (mouse != null && stickDelta.sqrMagnitude > 0f)
            {
                mouse.WarpCursorPosition(new Vector2(
                    _sigilCursorScreen.x,
                    Screen.height - _sigilCursorScreen.y));
            }
            UpdateSigilDrawingCursorWorld();

            if (!_sigilDrawing && (command.FirePressed || command.FireHeld))
            {
                _sigilDrawing = true;
                _sigilAttemptPoints.Clear();
                _sigilAttemptPoints.Add(_sigilCursorScreen);
            }
            if (_sigilDrawing && command.FireHeld)
            {
                float pointSpacing = Mathf.Max(0.5f, sigils.DrawingPointSpacing);
                if (_sigilAttemptPoints.Count == 0 ||
                    Vector2.Distance(_sigilAttemptPoints[^1], _sigilCursorScreen) >= pointSpacing)
                {
                    _sigilAttemptPoints.Add(_sigilCursorScreen);
                }
            }
            if (!_sigilDrawing || (!command.FireReleased && command.FireHeld))
            {
                return;
            }

            if (_sigilAttemptPoints.Count == 0 ||
                Vector2.Distance(_sigilAttemptPoints[^1], _sigilCursorScreen) > 0.5f)
            {
                _sigilAttemptPoints.Add(_sigilCursorScreen);
            }
            ResolveSigilDrawingAttempt();
        }

        private void UpdateSigilDrawingCursorWorld()
        {
            if (_sigilDrawingCursor == null || _camera == null)
            {
                return;
            }
            Vector3 unityScreenPoint = new Vector3(
                _sigilCursorScreen.x,
                Screen.height - _sigilCursorScreen.y,
                0f);
            Ray cursorRay = _camera.ScreenPointToRay(unityScreenPoint);
            _sigilDrawingCursor.position = cursorRay.GetPoint(
                Mathf.Max(_camera.nearClipPlane + 0.1f, _settings.Sigils.DrawingCursorWorldDistance));
            Quaternion spin = Quaternion.Euler(
                0f,
                _state.Elapsed * _settings.Sigils.DrawingCursorSpinSpeed,
                0f);
            _sigilDrawingCursor.rotation = _camera.transform.rotation *
                Quaternion.Euler(_settings.Sigils.DrawingCursorLocalEulerAngles) * spin;
        }

        private void ResolveSigilDrawingAttempt()
        {
            RailSigilDefinition definition = CurrentSigilDefinition();
            bool accepted = definition != null && EvaluateSigilDrawing(definition);
            _sigilDrawing = false;
            _sigilAttemptPoints.Clear();
            if (!accepted)
            {
                RejectSigilDrawing();
                return;
            }

            _sigilFaultElapsed = float.PositiveInfinity;
            _sigilSymbolIndex++;
            if (_sigilSymbolIndex < _sigilDemand.Count)
            {
                PlayCue(_settings.Sigils.GlyphClearedEvent, _player.transform.position);
                return;
            }
            BreakSigil();
        }

        private bool EvaluateSigilDrawing(RailSigilDefinition definition)
        {
            RailSigilTuning sigils = _settings.Sigils;
            if (_sigilAttemptPoints.Count < 2 ||
                PolylineLength(_sigilAttemptPoints) < sigils.DrawingMinimumLength)
            {
                return false;
            }

            BuildSigilRawPoints(definition, _sigilTargetPoints);
            int samples = Mathf.Max(8, sigils.DrawingEvaluationSamples);
            if (!NormalizeAndResamplePolyline(
                    _sigilAttemptPoints,
                    _sigilEvaluationAttempt,
                    samples) ||
                !NormalizeAndResamplePolyline(
                    _sigilTargetPoints,
                    _sigilEvaluationTarget,
                    samples))
            {
                return false;
            }

            float totalError = 0f;
            float maximumError = 0f;
            for (int i = 0; i < samples; i++)
            {
                float error = Vector2.Distance(
                    _sigilEvaluationAttempt[i],
                    _sigilEvaluationTarget[i]);
                totalError += error;
                maximumError = Mathf.Max(maximumError, error);
            }
            float averageError = totalError / samples;
            return averageError <= sigils.DrawingAverageErrorTolerance &&
                maximumError <= sigils.DrawingMaximumPointError;
        }

        private static float PolylineLength(List<Vector2> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vector2.Distance(points[i - 1], points[i]);
            }
            return length;
        }

        private static void BuildSigilRawPoints(
            RailSigilDefinition definition,
            List<Vector2> points)
        {
            points.Clear();
            Vector2 cursor = Vector2.zero;
            points.Add(cursor);
            if (definition?.Strokes == null)
            {
                return;
            }
            for (int i = 0; i < definition.Strokes.Count; i++)
            {
                cursor += SigilStrokeVector(definition.Strokes[i]);
                points.Add(cursor);
            }
        }

        private static bool NormalizeAndResamplePolyline(
            List<Vector2> source,
            List<Vector2> destination,
            int sampleCount)
        {
            destination.Clear();
            if (source == null || source.Count < 2)
            {
                return false;
            }

            Vector2 minimum = source[0];
            Vector2 maximum = source[0];
            for (int i = 1; i < source.Count; i++)
            {
                minimum = Vector2.Min(minimum, source[i]);
                maximum = Vector2.Max(maximum, source[i]);
            }
            float scale = Mathf.Max(maximum.x - minimum.x, maximum.y - minimum.y);
            if (scale < 0.001f)
            {
                return false;
            }
            Vector2 center = (minimum + maximum) * 0.5f;
            Vector2[] normalized = new Vector2[source.Count];
            float[] cumulative = new float[source.Count];
            normalized[0] = (source[0] - center) / scale;
            for (int i = 1; i < source.Count; i++)
            {
                normalized[i] = (source[i] - center) / scale;
                cumulative[i] = cumulative[i - 1] +
                    Vector2.Distance(normalized[i - 1], normalized[i]);
            }
            float totalLength = cumulative[^1];
            if (totalLength < 0.001f)
            {
                return false;
            }

            int segment = 1;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float distance = totalLength * sample / Mathf.Max(1, sampleCount - 1);
                while (segment < cumulative.Length - 1 && cumulative[segment] < distance)
                {
                    segment++;
                }
                float segmentStart = cumulative[segment - 1];
                float segmentLength = cumulative[segment] - segmentStart;
                float t = segmentLength > 0.0001f
                    ? Mathf.Clamp01((distance - segmentStart) / segmentLength)
                    : 0f;
                destination.Add(Vector2.Lerp(normalized[segment - 1], normalized[segment], t));
            }
            return destination.Count == sampleCount;
        }

        private void CancelSigilDrawingAttempt()
        {
            _sigilDrawing = false;
            _sigilAttemptPoints.Clear();
        }

        private void RejectSigilDrawing()
        {
            RailSigilTuning sigils = _settings.Sigils;
            _sigilFaultElapsed = 0f;
            _sigilElapsed += sigils.FaultTimePenalty;
            _cameraShake = Mathf.Max(_cameraShake, sigils.FaultCameraShake);
            if (_state.Elapsed - _lastSigilFaultCueAt >= sigils.FaultEventCooldown)
            {
                _lastSigilFaultCueAt = _state.Elapsed;
                PlayCue(sigils.FaultEvent, _player.transform.position);
            }
        }

        private void BreakSigil()
        {
            RailSigilTuning sigils = _settings.Sigils;
            Vector3 position = _sigilRoot != null ? _sigilRoot.position : _player.transform.position;
            int strokes = 0;
            for (int i = 0; i < _sigilDemand.Count; i++)
            {
                strokes += _sigilDemand[i].StrokeCount;
            }
            _state.Combo++;
            _state.ComboMultiplier = Mathf.Clamp(
                1f + ((_state.Combo - 1) * 0.18f),
                1f,
                _settings.MaximumComboMultiplier);
            _comboExpiresAt = _state.Elapsed + _settings.ComboWindow;
            int score = Mathf.RoundToInt(
                (sigils.BanishScore + (sigils.BanishScorePerStroke * strokes)) *
                _state.ComboMultiplier *
                (_sigilChain ? sigils.ChainScoreMultiplier : 1f));
            AddScore(score);
            SpawnScorePopup(
                position,
                _state.Combo > 1 ? $"+{score}  x{_state.ComboMultiplier:0.0}" : $"+{score}",
                _sigilChain ? sigils.ChainColor : sigils.CompletedStrokeColor);
            SpawnImpact(position, _settings.ImpactFlashMaximumScale * (_sigilChain ? 1.6f : 1f));
            _cameraShake = Mathf.Max(_cameraShake, sigils.BanishCameraShake);
            _killMarkerElapsed = 0f;
            PlayCue(sigils.BanishEvent, position);
            _state.SigilsBroken++;
            if (_sigilChain)
            {
                _state.ChainSigilsBroken++;
            }
            EndSigilAttack(true);
        }

        private void StrikeWithSigil()
        {
            RailSigilTuning sigils = _settings.Sigils;
            Vector3 position = _player.transform.position;
            DamagePlayer(
                sigils.StrikeDamage * (_sigilChain ? sigils.ChainDamageMultiplier : 1f),
                _sigilChain ? "Null choir sigil" : "Null sigil");
            SpawnImpact(position, _settings.ImpactFlashMaximumScale * (_sigilChain ? 1.6f : 1f));
            PlayCue(sigils.StrikeEvent, position);
            _state.SigilStrikes++;
            EndSigilAttack(false);
        }

        private void EndSigilAttack(bool broken)
        {
            RailSigilTuning sigils = _settings.Sigils;
            _sigilActive = false;
            _sigilVerdictBroken = broken;
            _sigilVerdictElapsed = 0f;
            CancelSigilDrawingAttempt();
            if (_sigilRoot != null)
            {
                _sigilRoot.gameObject.SetActive(false);
            }
            if (_sigilDrawingCursor != null)
            {
                _sigilDrawingCursor.gameObject.SetActive(false);
            }
            ApplyRailCursorState();
            _sigilNextAttackDistance = _state.Distance + SigilAttackSpacing();
            _sigilNextBossAttackAt = _state.Elapsed + sigils.BossAttackInterval;
        }

        private float SigilAttackSpacing()
        {
            RailSigilTuning sigils = _settings.Sigils;
            float scale = 1f - (DifficultyLevels() * sigils.DifficultySpacingReductionPerLevel);
            return Mathf.Max(
                sigils.MinimumAttackSpacingDistance,
                sigils.AttackSpacingDistance * Mathf.Max(0.2f, scale));
        }

        private RailSigilDefinition CurrentSigilDefinition()
        {
            return _sigilSymbolIndex >= 0 && _sigilSymbolIndex < _sigilDemand.Count
                ? _sigilDemand[_sigilSymbolIndex]
                : null;
        }

        private float SigilCountdownRemaining()
        {
            return Mathf.Max(0f, _sigilDuration - _sigilElapsed);
        }

        private static Vector2 SigilStrokeVector(RailSigilStroke stroke)
        {
            float radians = (int)stroke * Mathf.PI * 0.25f;
            // Negated Y so the authored compass reads the same way it is drawn on screen.
            return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        }

        private static void BuildSigilGlyphPoints(
            RailSigilDefinition definition,
            Rect box,
            List<Vector2> points)
        {
            points.Clear();
            if (definition == null || definition.StrokeCount < 1)
            {
                return;
            }
            Vector2 cursor = Vector2.zero;
            Vector2 minimum = Vector2.zero;
            Vector2 maximum = Vector2.zero;
            points.Add(cursor);
            for (int i = 0; i < definition.Strokes.Count; i++)
            {
                cursor += SigilStrokeVector(definition.Strokes[i]);
                points.Add(cursor);
                minimum = Vector2.Min(minimum, cursor);
                maximum = Vector2.Max(maximum, cursor);
            }
            Vector2 span = maximum - minimum;
            float horizontal = span.x > 0.001f ? box.width / span.x : float.PositiveInfinity;
            float vertical = span.y > 0.001f ? box.height / span.y : float.PositiveInfinity;
            float scale = Mathf.Min(horizontal, vertical);
            if (!float.IsFinite(scale))
            {
                scale = Mathf.Min(box.width, box.height);
            }
            Vector2 origin = box.center - (((minimum + maximum) * 0.5f) * scale);
            for (int i = 0; i < points.Count; i++)
            {
                points[i] = origin + (points[i] * scale);
            }
        }

        private void DrawSigilDuel()
        {
            RailSigilTuning sigils = _settings.Sigils;
            if (!sigils.Enabled)
            {
                return;
            }
            if (_sigilActive)
            {
                DrawSigilWorldMarker();
                DrawSigilTablet();
            }
            DrawSigilVerdict();
        }

        private void DrawSigilWorldMarker()
        {
            RailSigilTuning sigils = _settings.Sigils;
            if (_sigilRoot == null || !TryProject(_sigilRoot.position, out Vector2 screen))
            {
                return;
            }
            float urgency = Mathf.Clamp01(_sigilElapsed / Mathf.Max(0.01f, _sigilDuration));
            Color marker = Color.Lerp(
                _sigilChain ? sigils.ChainColor : _settings.HudPrimaryColor,
                sigils.FaultColor,
                urgency);
            float size = Scaled(sigils.SeekerMarkerSize) * (1f + (urgency * 0.65f));
            DrawBracket(screen, size, marker);
            // A closing ring of ticks around the seeker so its timer reads in the world too.
            DrawSigilUrgencyTicks(screen, size, urgency, marker);
            RailSigilDefinition definition = CurrentSigilDefinition();
            if (definition != null)
            {
                float width = Scaled(sigils.NameLabelWidth);
                DrawShadowedLabel(
                    new Rect(
                        screen.x - (width * 0.5f),
                        screen.y - (size * 0.5f) - Scaled(sigils.NameLabelGap),
                        width,
                        Scaled(sigils.NameLabelHeight)),
                    definition.Name,
                    _centeredSmallStyle,
                    marker);
            }
        }

        private void DrawSigilUrgencyTicks(Vector2 center, float size, float urgency, Color color)
        {
            int ticks = _settings.Sigils.UrgencyTickCount;
            if (ticks < 2)
            {
                return;
            }
            int lit = Mathf.CeilToInt((1f - urgency) * ticks);
            float thickness = BorderThickness();
            float radius = size * 0.72f;
            float length = size * 0.14f;
            for (int i = 0; i < ticks; i++)
            {
                float angle = (i / (float)ticks) * 360f;
                Matrix4x4 previous = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, center);
                DrawRect(
                    new Rect(center.x + radius, center.y - (thickness * 0.5f), length, thickness),
                    WithAlpha(color, i < lit ? 0.85f : 0.16f));
                GUI.matrix = previous;
            }
        }

        private void DrawSigilTablet()
        {
            RailSigilTuning sigils = _settings.Sigils;
            RailSigilDefinition definition = CurrentSigilDefinition();
            if (definition == null)
            {
                return;
            }
            bool faulted = float.IsFinite(_sigilFaultElapsed);
            Color accent = faulted
                ? sigils.FaultColor
                : _sigilChain ? sigils.ChainColor : _settings.HudPrimaryColor;
            float lineHeight = Scaled(_settings.HudLineHeight);
            float pad = Scaled(sigils.PromptPanelPadding);
            float countdownHeight = lineHeight * 1.7f;
            float pipSize = Scaled(sigils.PromptChainPipSize);
            float pipGap = Scaled(sigils.PromptChainPipGap);
            bool chained = _sigilDemand.Count > 1;

            float plateWidth = Mathf.Min(
                Scaled(sigils.PromptPanelWidth),
                Screen.width - (Scaled(_settings.HudMargin) * 2f));
            float plateHeight = (pad * 2f) + lineHeight + countdownHeight +
                (chained ? pipSize + pipGap : 0f);
            float plateY = Screen.height * sigils.DrawingPromptViewportY;
            if (_boss != null && _boss.Active)
            {
                // The sovereign's meter owns the top of the frame, so the demand drops below it
                // instead of being painted over.
                plateY = Mathf.Max(
                    plateY,
                    Scaled(_settings.BossMeterTop + _settings.BossMeterHeight + _settings.HudSectionGap));
            }
            Rect plate = new Rect(
                (Screen.width - plateWidth) * 0.5f,
                plateY,
                plateWidth,
                plateHeight);
            // The demand sits on its own plate so the glyph name and countdown stay readable over
            // the starfield instead of floating loose across the reticle.
            DrawPanel(plate, accent);

            float cursor = plate.y + pad;
            DrawLabel(
                new Rect(plate.x + pad, cursor, plate.width - (pad * 2f), lineHeight),
                $"{(_sigilChain ? sigils.ChainPromptLabel : sigils.PromptLabel)}   {definition.Name}",
                _centeredSmallStyle,
                accent);
            cursor += lineHeight;
            float remaining = SigilCountdownRemaining();
            DrawShadowedLabel(
                new Rect(plate.x + pad, cursor, plate.width - (pad * 2f), countdownHeight),
                $"{remaining:0.0}s",
                _sigilCountdownStyle,
                accent);
            cursor += countdownHeight;
            if (chained)
            {
                DrawSigilChainPips(
                    new Rect(plate.x + pad, cursor + pipGap, plate.width - (pad * 2f), pipSize),
                    accent);
            }

            float guideSize = Mathf.Min(Screen.width, Screen.height) *
                sigils.DrawingGuideScreenFraction;
            Vector2 guideCenter = new Vector2(
                Screen.width * sigils.DrawingGuideViewportCenter.x,
                Screen.height * sigils.DrawingGuideViewportCenter.y);
            Rect guideBox = new Rect(
                guideCenter.x - (guideSize * 0.5f),
                guideCenter.y - (guideSize * 0.5f),
                guideSize,
                guideSize);
            BuildSigilGlyphPoints(
                definition,
                new Rect(
                    guideBox.x + (guideSize * sigils.DrawingGuidePaddingFraction),
                    guideBox.y + (guideSize * sigils.DrawingGuidePaddingFraction),
                    guideSize * (1f - (sigils.DrawingGuidePaddingFraction * 2f)),
                    guideSize * (1f - (sigils.DrawingGuidePaddingFraction * 2f))),
                _sigilGlyphPoints);
            Color guideColor = faulted
                ? WithAlpha(sigils.FaultColor, sigils.DrawingGuideColor.a)
                : sigils.DrawingGuideColor;
            for (int i = 1; i < _sigilGlyphPoints.Count; i++)
            {
                DrawSigilLine(
                    _sigilGlyphPoints[i - 1],
                    _sigilGlyphPoints[i],
                    Scaled(sigils.DrawingGuideThickness),
                    guideColor);
            }
            if (_sigilGlyphPoints.Count > 0)
            {
                float startSize = Scaled(sigils.DrawingGuideStartSize);
                Vector2 start = _sigilGlyphPoints[0];
                DrawRect(
                    new Rect(
                        start.x - (startSize * 0.5f),
                        start.y - (startSize * 0.5f),
                        startSize,
                        startSize),
                    sigils.CompletedStrokeColor);
            }

            for (int i = 1; i < _sigilAttemptPoints.Count; i++)
            {
                DrawSigilLine(
                    _sigilAttemptPoints[i - 1],
                    _sigilAttemptPoints[i],
                    Scaled(sigils.DrawingPaintThickness),
                    sigils.DrawingPaintColor);
            }

            float countdown = Mathf.Clamp01(remaining / Mathf.Max(0.01f, _sigilDuration));
            float barWidth = Mathf.Min(
                Scaled(sigils.CountdownBarWidth),
                Screen.width - (Scaled(_settings.HudMargin) * 2f));
            float barHeight = Scaled(sigils.CountdownBarHeight);
            float gap = Scaled(_settings.HudRowGap);
            // The hint keeps its authored anchor unless the charge readout is in the way, and the
            // countdown bar then rides just above it, so nothing collides on a short screen.
            float hintY = Mathf.Min(
                Screen.height * sigils.DrawingHintViewportY,
                ChargePanelRect().y - gap - lineHeight);
            float barY = Mathf.Min(
                Screen.height * sigils.DrawingCountdownViewportY,
                hintY - gap - barHeight);
            DrawMeter(
                new Rect((Screen.width - barWidth) * 0.5f, barY, barWidth, barHeight),
                countdown,
                Color.Lerp(sigils.FaultColor, sigils.CompletedStrokeColor, countdown));
            DrawLabel(
                new Rect(0f, hintY, Screen.width, lineHeight),
                _sigilDrawing ? sigils.ReleaseHintLabel : sigils.HintLabel,
                _centeredSmallStyle,
                _settings.HudSecondaryColor);
        }

        // One pip per glyph in a chain demand, so the player can see how much of the choir is left.
        private void DrawSigilChainPips(Rect row, Color accent)
        {
            RailSigilTuning sigils = _settings.Sigils;
            int count = _sigilDemand.Count;
            if (count <= 0)
            {
                return;
            }
            float gap = Scaled(sigils.PromptChainPipGap);
            float size = Mathf.Min(row.height, (row.width - ((count - 1) * gap)) / count);
            float x = row.center.x - (((size * count) + (gap * (count - 1))) * 0.5f);
            float border = BorderThickness();
            for (int i = 0; i < count; i++)
            {
                Rect pip = new Rect(x + (i * (size + gap)), row.y, size, size);
                if (i < _sigilSymbolIndex)
                {
                    DrawRect(pip, sigils.CompletedStrokeColor);
                }
                else if (i == _sigilSymbolIndex)
                {
                    DrawRect(pip, accent);
                }
                else
                {
                    DrawRect(pip, _settings.HudMeterTrackColor);
                    DrawRect(new Rect(pip.x, pip.y, pip.width, border), WithAlpha(accent, 0.5f));
                    DrawRect(new Rect(pip.x, pip.yMax - border, pip.width, border), WithAlpha(accent, 0.5f));
                }
            }
        }

        private void DrawSigilLine(Vector2 from, Vector2 to, float thickness, Color color)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
            {
                return;
            }
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, from);
            DrawRect(new Rect(from.x, from.y - (thickness * 0.5f), length, thickness), color);
            GUI.matrix = previousMatrix;
        }

        private void DrawSigilVerdict()
        {
            if (!float.IsFinite(_sigilVerdictElapsed))
            {
                return;
            }
            RailSigilTuning sigils = _settings.Sigils;
            float normalized = Mathf.Clamp01(
                _sigilVerdictElapsed / Mathf.Max(0.01f, sigils.VerdictDuration));
            float fade = 1f - normalized;
            Color color = _sigilVerdictBroken ? sigils.CompletedStrokeColor : sigils.FaultColor;
            float height = Scaled(sigils.VerdictLabelHeight);
            // The verdict lifts and fades so a banish and a strike are told apart at a glance.
            Rect rect = new Rect(
                0f,
                (Screen.height * sigils.DrawingVerdictViewportY) - (normalized * height * 0.6f),
                Screen.width,
                height);
            DrawShadowedLabel(
                rect,
                _sigilVerdictBroken ? sigils.BanishLabel : sigils.StrikeLabel,
                _centeredSmallStyle,
                WithAlpha(color, fade));
            float rule = Mathf.Max(1f, Scaled(_settings.HudDividerHeight));
            float ruleWidth = Scaled(sigils.VerdictRuleWidth) * (0.4f + (0.6f * fade));
            DrawRect(
                new Rect(rect.center.x - (ruleWidth * 0.5f), rect.yMax, ruleWidth, rule),
                WithAlpha(color, fade * 0.8f));
        }

        private void OnGUI()
        {
            if (!IsActive || Event.current.type != EventType.Repaint || _settings == null ||
                Time.timeScale <= 0f)
            {
                return;
            }
            UpdateHudScale();
            EnsureHudStyles();
            GUI.depth = -1300;
            // World-anchored layers first so the chrome always sits on top of them.
            DrawLaneHudWarning();
            DrawLowHullEdge();
            DrawDamageVignette();
            DrawScorePopups();
            DrawReticles();
            DrawThreatIndicators();
            DrawRoutePrompt();
            DrawSigilDuel();
            DrawHudPanels();
            if (_boss != null && _boss.Active)
            {
                DrawBossMeter();
            }
            DrawBanner();
            if (Phase == RailShooterPhase.Results)
            {
                DrawResults();
            }
        }

        // The chrome is authored against HudReferenceHeight and rescaled per display, so the
        // panels keep their proportion instead of shrinking into a corner on a tall screen.
        private void UpdateHudScale()
        {
            float minimum = Mathf.Min(_settings.HudMinimumScale, _settings.HudMaximumScale);
            float maximum = Mathf.Max(_settings.HudMinimumScale, _settings.HudMaximumScale);
            float scale = Mathf.Clamp(
                Screen.height / Mathf.Max(1f, _settings.HudReferenceHeight),
                minimum,
                maximum);
            if (Mathf.Abs(scale - _hudStyleScale) > 0.004f)
            {
                _bodyStyle = null;
            }
            _hudScale = scale;
        }

        private float Scaled(float value)
        {
            return value * _hudScale;
        }

        private int ScaledFontSize(int size)
        {
            return Mathf.Max(8, Mathf.RoundToInt(size * _hudScale));
        }

        private float BorderThickness()
        {
            return Mathf.Max(1f, Scaled(_settings.ReticleLineThickness));
        }

        // Presentation-only readout state: the score ticker and the trailing meter ghosts.
        private void TickHudPresentation(float deltaTime)
        {
            _scoreDisplay = Mathf.MoveTowards(
                _scoreDisplay,
                _state.Score,
                Mathf.Max(1f, _settings.ScoreTickerSpeed) * deltaTime);
            float hull = _health != null ? _health.NormalizedHealth : 0f;
            TickMeterGhost(hull, ref _hullGhost, ref _hullGhostHold, deltaTime);
            float boss = _boss != null && _boss.Active
                ? Mathf.Clamp01(_boss.Health / Mathf.Max(1f, _boss.MaximumHealth))
                : 1f;
            TickMeterGhost(boss, ref _bossGhost, ref _bossGhostHold, deltaTime);
        }

        private void TickMeterGhost(float value, ref float ghost, ref float hold, float deltaTime)
        {
            if (value >= ghost)
            {
                ghost = value;
                hold = 0f;
                return;
            }
            hold += deltaTime;
            if (hold >= _settings.HudMeterGhostHoldDuration)
            {
                ghost = Mathf.MoveTowards(
                    ghost,
                    value,
                    Mathf.Max(0.01f, _settings.HudMeterGhostDrainSpeed) * deltaTime);
            }
        }

        private void EnsureHudStyles()
        {
            if (_bodyStyle != null)
            {
                return;
            }
            _hudStyleScale = _hudScale;
            Font font = _settings.HudFont != null ? _settings.HudFont : GUI.skin.font;
            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = ScaledFontSize(_settings.HudSmallFontSize),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = _settings.HudSecondaryColor },
            };
            _bodyStyle = new GUIStyle(_smallStyle)
            {
                fontSize = ScaledFontSize(_settings.HudBodyFontSize),
                normal = { textColor = _settings.HudPrimaryColor },
            };
            _titleStyle = new GUIStyle(_bodyStyle)
            {
                fontSize = ScaledFontSize(_settings.HudTitleFontSize),
                fontStyle = FontStyle.Bold,
            };
            _resultStyle = new GUIStyle(_titleStyle)
            {
                fontSize = ScaledFontSize(_settings.HudResultFontSize),
                alignment = TextAnchor.MiddleCenter,
            };
            _popupStyle = new GUIStyle(_bodyStyle)
            {
                fontSize = ScaledFontSize(_settings.ScorePopupFontSize),
                alignment = TextAnchor.MiddleCenter,
            };
            _centeredSmallStyle = new GUIStyle(_smallStyle)
            {
                alignment = TextAnchor.MiddleCenter,
            };
            _statLabelStyle = new GUIStyle(_smallStyle)
            {
                fontSize = ScaledFontSize(_settings.HudBodyFontSize),
                alignment = TextAnchor.MiddleLeft,
            };
            _statValueStyle = new GUIStyle(_bodyStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };
            _valueStyle = new GUIStyle(_bodyStyle)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
            };
            _sectionStyle = new GUIStyle(_smallStyle)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
            };
            _sigilCountdownStyle = new GUIStyle(_titleStyle)
            {
                fontSize = ScaledFontSize(_settings.Sigils.CountdownFontSize),
                alignment = TextAnchor.MiddleCenter,
            };
            _gradeStyle = new GUIStyle(_resultStyle)
            {
                fontSize = ScaledFontSize(Mathf.RoundToInt(_settings.HudResultFontSize * 1.25f)),
            };
            StripHoverStates(_smallStyle);
            StripHoverStates(_bodyStyle);
            StripHoverStates(_titleStyle);
            StripHoverStates(_resultStyle);
            StripHoverStates(_popupStyle);
            StripHoverStates(_centeredSmallStyle);
            StripHoverStates(_statLabelStyle);
            StripHoverStates(_statValueStyle);
            StripHoverStates(_valueStyle);
            StripHoverStates(_sectionStyle);
            StripHoverStates(_sigilCountdownStyle);
            StripHoverStates(_gradeStyle);
        }

        // Every GUIStyle state is pinned to the normal one so the labels never light up or shift
        // under the pointer: this HUD is a readout, not a set of controls.
        private static void StripHoverStates(GUIStyle style)
        {
            if (style == null)
            {
                return;
            }
            Color text = style.normal.textColor;
            Texture2D background = style.normal.background;
            GUIStyleState[] states =
            {
                style.hover, style.active, style.focused,
                style.onNormal, style.onHover, style.onActive, style.onFocused,
            };
            for (int i = 0; i < states.Length; i++)
            {
                states[i].textColor = text;
                states[i].background = background;
            }
        }

        private void DrawLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = previous;
            style.hover.textColor = previous;
            style.active.textColor = previous;
            style.focused.textColor = previous;
            style.onNormal.textColor = previous;
            style.onHover.textColor = previous;
        }

        // Drop shadow behind the glyphs so cyan text still separates from a bright cloud bank.
        private void DrawShadowedLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Vector2 offset = _settings.HudPanelShadowOffset * (_hudScale * 0.35f);
            DrawLabel(
                new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height),
                text,
                style,
                WithAlpha(_settings.HudPanelShadowColor, color.a));
            DrawLabel(rect, text, style, color);
        }

        private void DrawStatRow(Rect row, string label, string value, Color labelColor, Color valueColor)
        {
            DrawLabel(row, label, _smallStyle, labelColor);
            DrawLabel(row, value, _valueStyle, valueColor);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private void DrawHudPanels()
        {
            DrawMissionPanel();
            DrawStatusPanel();
            DrawChargePanel();
        }

        private void DrawMissionPanel()
        {
            float margin = Scaled(_settings.HudMargin);
            float pad = Scaled(_settings.HudPanelPadding);
            float line = Scaled(_settings.HudLineHeight);
            float progress = Scaled(_settings.ProgressMeterHeight);
            float rowGap = Scaled(_settings.HudRowGap);
            float sectionGap = Scaled(_settings.HudSectionGap);
            float titleHeight = Scaled(_settings.HudTitleHeight);
            float chip = Scaled(_settings.RouteChipHeight);
            float width = Scaled(_settings.HudPanelWidth);
            float inner = width - (pad * 2f);
            float divider = Mathf.Max(1f, Scaled(_settings.HudDividerHeight));

            // The panel is sized from exactly the increments the cursor walks below, so no row can
            // ever spill past the frame at any HUD scale.
            float height = (pad * 2f) + titleHeight + line + divider + (rowGap * 6f) +
                (line * 5f) + (progress * 2f) + sectionGap + chip;
            Rect panel = new Rect(margin, margin, width, height);
            DrawPanel(panel, _settings.HudBorderColor);
            DrawPanelHeader(panel, pad + titleHeight + line);

            float cursor = panel.y + pad;
            DrawShadowedLabel(
                new Rect(panel.x + pad, cursor, inner, titleHeight),
                _settings.MissionTitle,
                _titleStyle,
                _settings.HudPrimaryColor);
            cursor += titleHeight;
            DrawLabel(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.MissionSubtitle,
                _smallStyle,
                _settings.HudSecondaryColor);
            cursor += line;
            DrawRect(new Rect(panel.x, cursor, panel.width, divider), _settings.HudDividerColor);
            cursor += divider + rowGap;

            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.ScoreLabel,
                string.Format(_settings.ScoreValueFormat, Mathf.RoundToInt(_scoreDisplay)),
                _settings.HudSecondaryColor,
                _settings.HudPrimaryColor);
            cursor += line;
            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.KillsLabel,
                string.Format(_settings.KillsValueFormat, _state.Kills),
                _settings.HudSecondaryColor,
                _settings.HudPrimaryColor);
            cursor += line + rowGap;

            bool comboLive = _state.Combo > 1;
            float comboFill = comboLive && float.IsFinite(_comboExpiresAt)
                ? Mathf.Clamp01((_comboExpiresAt - _state.Elapsed) / Mathf.Max(0.01f, _settings.ComboWindow))
                : 0f;
            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.ComboLabel,
                string.Format(_settings.ComboValueFormat, _state.ComboMultiplier),
                _settings.HudSecondaryColor,
                comboLive ? _settings.HudComboColor : _settings.HudSecondaryColor);
            cursor += line + rowGap;
            DrawMeter(
                new Rect(panel.x + pad, cursor, inner, progress),
                comboFill,
                _settings.HudComboColor);
            cursor += progress + sectionGap;

            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.FormationsLabel,
                string.Format(_settings.FormationsValueFormat, _state.FormationClears),
                _settings.HudSecondaryColor,
                _settings.HudSecondaryColor);
            cursor += line + rowGap;

            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.ProgressLabel,
                string.Format(_settings.DepthValueFormat, Mathf.RoundToInt(_state.Distance)),
                _settings.HudSecondaryColor,
                _settings.HudPrimaryColor);
            cursor += line + rowGap;
            float depth = Mathf.Clamp01(_state.Distance / Mathf.Max(1f, _settings.BossSpawnDistance));
            Rect depthMeter = new Rect(panel.x + pad, cursor, inner, progress);
            DrawMeter(depthMeter, depth, _settings.HudChargeColor);
            DrawDepthGateMarkers(depthMeter);
            cursor += progress + rowGap;

            bool blackRoute = _state.Route == RailShooterRoute.Black;
            DrawRouteChip(
                new Rect(panel.x + pad, cursor, inner, chip),
                blackRoute ? _settings.RiskRouteLabel : _settings.SafeRouteLabel,
                blackRoute ? _settings.RiftDangerColor : _settings.RiftSignalColor);
        }

        // Ticks on the depth meter for every route split still ahead, so the player can read how
        // far the next branch is instead of only learning about it from the prompt.
        private void DrawDepthGateMarkers(Rect meter)
        {
            float span = Mathf.Max(1f, _settings.BossSpawnDistance);
            float markerWidth = Mathf.Max(1f, Scaled(_settings.DepthGateMarkerWidth));
            for (int i = 0; i < _settings.BranchGateCount; i++)
            {
                float distance = _settings.BranchGateFirstDistance + (i * _settings.BranchGateSpacing);
                float normalized = distance / span;
                if (normalized <= 0f || normalized >= 1f)
                {
                    continue;
                }
                Color color = i < _state.RouteGatesCleared
                    ? _settings.DepthGateClearedMarkerColor
                    : _settings.DepthGateMarkerColor;
                DrawRect(
                    new Rect(
                        meter.x + (meter.width * normalized) - (markerWidth * 0.5f),
                        meter.y - markerWidth,
                        markerWidth,
                        meter.height + (markerWidth * 2f)),
                    color);
            }
        }

        private void DrawRouteChip(Rect row, string label, Color color)
        {
            float padding = Scaled(_settings.RouteChipPadding);
            float width = Mathf.Min(
                row.width,
                _centeredSmallStyle.CalcSize(new GUIContent(label)).x + (padding * 2f));
            Rect chip = new Rect(row.xMax - width, row.y, width, row.height);
            DrawRect(chip, WithAlpha(color, 0.16f));
            float border = BorderThickness();
            DrawRect(new Rect(chip.x, chip.y, border, chip.height), color);
            DrawRect(new Rect(chip.xMax - border, chip.y, border, chip.height), color);
            DrawLabel(chip, label, _centeredSmallStyle, color);
        }

        private void DrawStatusPanel()
        {
            float margin = Scaled(_settings.HudMargin);
            float pad = Scaled(_settings.HudPanelPadding);
            float line = Scaled(_settings.HudLineHeight);
            float meter = Scaled(_settings.HudMeterHeight);
            float rowGap = Scaled(_settings.HudRowGap);
            float sectionGap = Scaled(_settings.HudSectionGap);
            float titleHeight = Scaled(_settings.HudTitleHeight);
            float pip = Scaled(_settings.BombPipSize);
            float width = Scaled(_settings.HudPanelWidth);
            float inner = width - (pad * 2f);
            float divider = Mathf.Max(1f, Scaled(_settings.HudDividerHeight));

            float height = (pad * 2f) + titleHeight + divider + (rowGap * 4f) +
                (line * 3f) + (meter * 2f) + (sectionGap * 2f) + pip;
            Rect panel = new Rect(Screen.width - margin - width, margin, width, height);

            float hull = _health != null ? _health.NormalizedHealth : 0f;
            bool lowHull = hull <= _settings.LowHullWarningFraction;
            float hullPulse = lowHull
                ? 0.55f + (0.45f * Mathf.Abs(Mathf.Sin(_state.Elapsed * _settings.LowHullPulseSpeed)))
                : 1f;
            Color accent = lowHull
                ? Color.Lerp(_settings.HudBorderColor, _settings.HudDamageColor, hullPulse)
                : _settings.HudBorderColor;
            DrawPanel(panel, accent);
            DrawPanelHeader(panel, pad + titleHeight);

            float cursor = panel.y + pad;
            DrawShadowedLabel(
                new Rect(panel.x + pad, cursor, inner, titleHeight),
                _settings.StatusPanelTitle,
                _titleStyle,
                _settings.HudPrimaryColor);
            cursor += titleHeight;
            DrawRect(new Rect(panel.x, cursor, panel.width, divider), _settings.HudDividerColor);
            cursor += divider + rowGap;

            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.HullLabel,
                string.Format(_settings.HullValueFormat, Mathf.CeilToInt(hull * 100f)),
                _settings.HudSecondaryColor,
                WithAlpha(lowHull ? _settings.HudDamageColor : _settings.HudPrimaryColor, hullPulse));
            cursor += line + rowGap;
            DrawMeter(
                new Rect(panel.x + pad, cursor, inner, meter),
                hull,
                Color.Lerp(_settings.HudDamageColor, _settings.HudReticleColor, hull),
                _hullGhost);
            cursor += meter + sectionGap;

            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.BombsLabel,
                string.Format(_settings.BombsValueFormat, _state.Bombs, Mathf.Max(1, _settings.MaximumBombs)),
                _settings.HudSecondaryColor,
                _state.Bombs > 0 ? _settings.HudBombColor : _settings.HudSecondaryColor);
            cursor += line + rowGap;
            DrawBombPips(new Rect(panel.x + pad, cursor, inner, pip));
            cursor += pip + sectionGap;

            float maneuver = Mathf.Clamp01(
                _state.ManeuverEnergy / Mathf.Max(1f, _settings.ManeuverEnergyCapacity));
            bool maneuverReady = maneuver >= 0.999f;
            float readyPulse = 0.6f + (0.4f * Mathf.Abs(Mathf.Sin(_state.Elapsed * _settings.HudReadyPulseSpeed)));
            DrawStatRow(
                new Rect(panel.x + pad, cursor, inner, line),
                _settings.ManeuverLabel,
                maneuverReady
                    ? _settings.ManeuverReadyLabel
                    : string.Format(_settings.ManeuverValueFormat, Mathf.FloorToInt(maneuver * 100f)),
                _settings.HudSecondaryColor,
                maneuverReady
                    ? WithAlpha(_settings.HudReticleColor, readyPulse)
                    : _settings.HudSecondaryColor);
            cursor += line + rowGap;
            DrawMeter(
                new Rect(panel.x + pad, cursor, inner, meter),
                maneuver,
                maneuverReady
                    ? Color.Lerp(_settings.HudChargeColor, _settings.HudReticleColor, readyPulse)
                    : _settings.HudChargeColor);
        }

        // Shared so the sigil hint can stay clear of the charge readout at any HUD scale.
        private Rect ChargePanelRect()
        {
            float margin = Scaled(_settings.HudMargin);
            float pad = Scaled(_settings.HudPanelPadding);
            float line = Scaled(_settings.HudLineHeight);
            float meter = Scaled(_settings.HudMeterHeight);
            float rowGap = Scaled(_settings.HudRowGap);
            float width = Scaled(_settings.HudPanelWidth);
            float height = pad + line + rowGap + meter + pad;
            return new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - margin - height - line,
                width,
                height);
        }

        private void DrawChargePanel()
        {
            float pad = Scaled(_settings.HudPanelPadding);
            float line = Scaled(_settings.HudLineHeight);
            float meter = Scaled(_settings.HudMeterHeight);
            float rowGap = Scaled(_settings.HudRowGap);
            Rect panel = ChargePanelRect();
            float inner = panel.width - (pad * 2f);

            float charge = ChargeNormalized();
            bool ready = charge >= 0.999f;
            int locks = _chargeLocks.Count + _satelliteChargeLocks.Count;
            bool locked = _chargeLock != null || _satelliteChargeLock != null;
            float readyPulse = 0.6f + (0.4f * Mathf.Abs(Mathf.Sin(_state.Elapsed * _settings.HudReadyPulseSpeed)));
            Color accent = ready
                ? Color.Lerp(_settings.HudBorderColor, _settings.HudChargeColor, readyPulse)
                : _settings.HudBorderColor;
            DrawPanel(panel, accent);

            string label = ready
                ? _settings.ChargeReadyLabel
                : locked
                    ? string.Format(_settings.ChargeLockFormat, locks)
                    : _settings.ChargeLabel;
            Color labelColor = ready
                ? WithAlpha(_settings.HudChargeColor, readyPulse)
                : locked
                    ? _settings.HudChargeColor
                    : _settings.HudSecondaryColor;
            DrawLabel(
                new Rect(panel.x + pad, panel.y + pad, inner, line),
                label,
                _centeredSmallStyle,
                labelColor);
            DrawMeter(
                new Rect(panel.x + pad, panel.y + pad + line + rowGap, inner, meter),
                charge,
                ready
                    ? Color.Lerp(_settings.HudChargeColor, Color.white, readyPulse * 0.4f)
                    : _settings.HudChargeColor);
        }

        private void DrawBombPips(Rect row)
        {
            int capacity = Mathf.Max(1, _settings.MaximumBombs);
            float gap = Scaled(_settings.BombPipGap);
            float size = Mathf.Min(
                row.height,
                Mathf.Min(Scaled(_settings.BombPipSize), (row.width - ((capacity - 1) * gap)) / capacity));
            float y = row.y + ((row.height - size) * 0.5f);
            float border = BorderThickness();
            for (int i = 0; i < capacity; i++)
            {
                Rect pip = new Rect(row.x + (i * (size + gap)), y, size, size);
                DrawRect(pip, _settings.HudMeterTrackColor);
                DrawRect(
                    new Rect(pip.x, pip.y, pip.width, border),
                    WithAlpha(_settings.HudBombColor, 0.4f));
                DrawRect(
                    new Rect(pip.x, pip.yMax - border, pip.width, border),
                    WithAlpha(_settings.HudBombColor, 0.4f));
                if (i >= _state.Bombs)
                {
                    continue;
                }
                Rect core = new Rect(
                    pip.x + border,
                    pip.y + border,
                    Mathf.Max(0f, pip.width - (border * 2f)),
                    Mathf.Max(0f, pip.height - (border * 2f)));
                DrawRect(core, _settings.HudBombColor);
                DrawRect(
                    new Rect(core.x, core.y, core.width, Mathf.Max(1f, border)),
                    WithAlpha(Color.white, 0.45f));
            }
        }

        private void DrawReticles()
        {
            DrawWorldReticle(
                _player.transform.position + (_state.AimDirection * _settings.NearReticleDistance),
                Scaled(_settings.ReticleNearSize));
            Vector3 farAim = _player.transform.position +
                (_state.AimDirection * _settings.FarReticleDistance);
            DrawWorldReticle(farAim, Scaled(_settings.ReticleFarSize));

            RailEnemy assist = FindViewportTarget(
                _settings.AimAssistViewportRadius,
                _settings.AimAssistRange);
            if (assist != null && assist != _chargeLock &&
                TryProject(assist.Transform.position, out Vector2 assistScreen))
            {
                DrawBracket(
                    assistScreen,
                    Scaled(_settings.TargetBracketSize),
                    WithAlpha(_settings.HudReticleColor, 0.6f));
                DrawTargetHealth(assist, assistScreen, Scaled(_settings.TargetBracketSize));
            }
            float lockPulse = 0.72f + (0.28f * Mathf.Abs(Mathf.Sin(_state.Elapsed * _settings.HudReadyPulseSpeed)));
            for (int i = 0; i < _chargeLocks.Count; i++)
            {
                RailEnemy locked = _chargeLocks[i];
                if (locked != null && locked.Active &&
                    TryProject(locked.Transform.position, out Vector2 lockScreen))
                {
                    DrawBracket(
                        lockScreen,
                        Scaled(_settings.LockBracketSize),
                        WithAlpha(_settings.HudChargeColor, lockPulse));
                    DrawTargetHealth(locked, lockScreen, Scaled(_settings.LockBracketSize));
                }
            }
            DrawSatelliteLocks(lockPulse);
            if (TryProject(farAim, out Vector2 markerAnchor))
            {
                DrawHitMarkers(markerAnchor);
            }
        }

        // Screen-edge chevrons for hostiles that are alive but out of frame, so an off-screen
        // flanker is a readable threat instead of an unexplained hit.
        private void DrawThreatIndicators()
        {
            if (!_settings.ThreatIndicatorsEnabled || _camera == null || _player == null)
            {
                return;
            }
            float margin = Scaled(_settings.ThreatIndicatorEdgeMargin);
            if (margin * 2f >= Mathf.Min(Screen.width, Screen.height))
            {
                return;
            }
            float size = Scaled(_settings.ThreatIndicatorSize);
            float range = Mathf.Max(1f, _settings.ThreatIndicatorRange);
            float rangeSquared = range * range;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Rect bounds = new Rect(
                margin,
                margin,
                Screen.width - (margin * 2f),
                Screen.height - (margin * 2f));
            Vector3 origin = _player.transform.position;
            int drawn = 0;
            for (int i = 0; i < _enemies.Count && drawn < _settings.ThreatIndicatorMaximumCount; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (enemy == null || !enemy.Active || enemy.Boss || enemy.Transform == null)
                {
                    continue;
                }
                if ((enemy.Transform.position - origin).sqrMagnitude > rangeSquared)
                {
                    continue;
                }
                Vector3 projected = _camera.WorldToScreenPoint(enemy.Transform.position);
                bool behind = projected.z <= 0f;
                Vector2 point = new Vector2(projected.x, Screen.height - projected.y);
                if (behind)
                {
                    // A point behind the lens projects mirrored, so flip it back before it is
                    // turned into a bearing.
                    point = new Vector2(Screen.width - point.x, Screen.height - point.y);
                }
                // Visibility is judged against the whole frame; the inset bounds only decide where
                // the chevron parks, so a hostile near the edge is not double-drawn.
                if (!behind &&
                    point.x >= 0f && point.x <= Screen.width &&
                    point.y >= 0f && point.y <= Screen.height)
                {
                    continue;
                }
                Vector2 direction = point - center;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    continue;
                }
                direction.Normalize();
                DrawThreatChevron(
                    ProjectToBounds(center, direction, bounds),
                    direction,
                    size,
                    enemy.Elite ? _settings.ThreatIndicatorEliteColor : _settings.ThreatIndicatorColor);
                drawn++;
            }
        }

        private static Vector2 ProjectToBounds(Vector2 center, Vector2 direction, Rect bounds)
        {
            float halfWidth = bounds.width * 0.5f;
            float halfHeight = bounds.height * 0.5f;
            float horizontal = Mathf.Abs(direction.x) > 0.0001f
                ? halfWidth / Mathf.Abs(direction.x)
                : float.PositiveInfinity;
            float vertical = Mathf.Abs(direction.y) > 0.0001f
                ? halfHeight / Mathf.Abs(direction.y)
                : float.PositiveInfinity;
            float distance = Mathf.Min(horizontal, vertical);
            if (!float.IsFinite(distance))
            {
                distance = Mathf.Min(halfWidth, halfHeight);
            }
            return center + (direction * distance);
        }

        private void DrawThreatChevron(Vector2 center, Vector2 direction, float size, Color color)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float thickness = BorderThickness();
            Vector2 tip = center + (direction * (size * 0.5f));
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, center);
            DrawRect(
                new Rect(center.x - (size * 0.5f), center.y - (thickness * 0.5f), size, thickness),
                color);
            GUI.matrix = previous;
            GUIUtility.RotateAroundPivot(angle + 145f, tip);
            DrawRect(new Rect(tip.x, tip.y - (thickness * 0.5f), size * 0.6f, thickness), color);
            GUI.matrix = previous;
            GUIUtility.RotateAroundPivot(angle - 145f, tip);
            DrawRect(new Rect(tip.x, tip.y - (thickness * 0.5f), size * 0.6f, thickness), color);
            GUI.matrix = previous;
        }

        // Satellites acquire in stages like the free-roam energy launcher, so the reticle has to
        // show the stage as well as the target: the bracket tightens and recolors as the lock
        // completes, and only a finished lock gets the pulsing confirmation box.
        private void DrawSatelliteLocks(float lockPulse)
        {
            RailSatellite statusSatellite = null;
            Vector2 statusScreen = Vector2.zero;
            float statusSize = 0f;
            for (int i = 0; i < _satellites.Count; i++)
            {
                RailSatellite satellite = _satellites[i];
                if (satellite == null || !satellite.Active ||
                    satellite.LockState == RailSatelliteLockState.None ||
                    !TryProject(SatelliteTargetPosition(satellite), out Vector2 screen))
                {
                    continue;
                }

                float progress = SatelliteLockProgress(satellite);
                float size = Scaled(Mathf.Lerp(
                    _settings.SatelliteDetectedBracketSize,
                    _settings.LockBracketSize,
                    progress));
                Color color = SatelliteLockColor(satellite.LockState);
                if (satellite.LockState == RailSatelliteLockState.Locked)
                {
                    size += Scaled(_settings.SatelliteLockedPulseAmount) * lockPulse;
                    color = WithAlpha(color, lockPulse);
                }
                DrawBracket(screen, size, color);
                DrawSatelliteHealth(satellite, screen, size);
                if (satellite == _satelliteChargeLock)
                {
                    statusSatellite = satellite;
                    statusScreen = screen;
                    statusSize = size;
                }
            }

            if (statusSatellite == null)
            {
                return;
            }
            DrawSatelliteLockStatus(statusSatellite, statusScreen, statusSize);
        }

        private void DrawSatelliteLockStatus(RailSatellite satellite, Vector2 center, float size)
        {
            string status = satellite.LockState switch
            {
                RailSatelliteLockState.Detected => _settings.SatelliteDetectedLabel,
                RailSatelliteLockState.Locking => string.Format(
                    _settings.SatelliteLockingFormat,
                    Mathf.RoundToInt(SatelliteLockProgress(satellite) * 100f)),
                RailSatelliteLockState.Locked => _settings.SatelliteLockedLabel,
                _ => string.Empty,
            };
            if (string.IsNullOrEmpty(status))
            {
                return;
            }
            float line = Scaled(_settings.HudLineHeight);
            float width = Scaled(_settings.HudPanelWidth) * 0.5f;
            DrawLabel(
                new Rect(
                    center.x - (width * 0.5f),
                    center.y - (size * 0.5f) - Scaled(_settings.SatelliteLockStatusOffset) - line,
                    width,
                    line),
                status,
                _centeredSmallStyle,
                SatelliteLockColor(satellite.LockState));
        }

        private Color SatelliteLockColor(RailSatelliteLockState state)
        {
            return state switch
            {
                RailSatelliteLockState.Detected => _settings.SatelliteDetectedColor,
                RailSatelliteLockState.Locking => _settings.SatelliteLockingColor,
                RailSatelliteLockState.Locked => _settings.SatelliteLockedColor,
                _ => Color.clear,
            };
        }

        private void DrawSatelliteHealth(RailSatellite satellite, Vector2 center, float size)
        {
            float health = Mathf.Clamp01(satellite.Health / Mathf.Max(1f, _settings.SatelliteHealth));
            if (health >= 0.999f)
            {
                return;
            }
            float width = size * 1.6f;
            Rect bar = new Rect(
                center.x - (width * 0.5f),
                center.y + (size * 0.5f) + Scaled(_settings.TargetHealthBarGap),
                width,
                Mathf.Max(2f, Scaled(_settings.ProgressMeterHeight) * 0.5f));
            DrawMeter(bar, health, Color.Lerp(_settings.RiftDangerColor, _settings.HudReticleColor, health));
        }

        private void DrawTargetHealth(RailEnemy enemy, Vector2 center, float size)
        {
            float health = Mathf.Clamp01(enemy.Health / Mathf.Max(1f, enemy.MaximumHealth));
            if (health >= 0.999f)
            {
                return;
            }
            float width = size * 1.6f;
            Rect bar = new Rect(
                center.x - (width * 0.5f),
                center.y + (size * 0.5f) + Scaled(_settings.TargetHealthBarGap),
                width,
                Mathf.Max(2f, Scaled(_settings.ProgressMeterHeight) * 0.5f));
            DrawMeter(bar, health, Color.Lerp(_settings.RiftDangerColor, _settings.HudReticleColor, health));
        }

        private bool TryProject(Vector3 world, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;
            if (_camera == null)
            {
                return false;
            }
            Vector3 screen = _camera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                return false;
            }
            screenPoint = new Vector2(screen.x, Screen.height - screen.y);
            return true;
        }

        private void DrawHitMarkers(Vector2 center)
        {
            if (float.IsFinite(_hitMarkerElapsed))
            {
                float fade = 1f - Mathf.Clamp01(
                    _hitMarkerElapsed / Mathf.Max(0.01f, _settings.HitMarkerDuration));
                DrawCross(
                    center,
                    Scaled(_settings.HitMarkerSize),
                    WithAlpha(_settings.HudPrimaryColor, fade));
            }
            if (float.IsFinite(_killMarkerElapsed))
            {
                float normalized = Mathf.Clamp01(
                    _killMarkerElapsed / Mathf.Max(0.01f, _settings.KillMarkerDuration));
                DrawCross(
                    center,
                    Scaled(Mathf.Lerp(_settings.HitMarkerSize, _settings.KillMarkerSize, normalized)),
                    WithAlpha(_settings.HudComboColor, 1f - normalized));
            }
        }

        private void DrawCross(Vector2 center, float size, Color color)
        {
            float half = size * 0.5f;
            float thickness = BorderThickness();
            float arm = size * 0.32f;
            DrawRect(new Rect(center.x - half, center.y - half, arm, thickness), color);
            DrawRect(new Rect(center.x + half - arm, center.y - half, arm, thickness), color);
            DrawRect(new Rect(center.x - half, center.y + half - thickness, arm, thickness), color);
            DrawRect(new Rect(center.x + half - arm, center.y + half - thickness, arm, thickness), color);
            DrawRect(new Rect(center.x - half, center.y - half, thickness, arm), color);
            DrawRect(new Rect(center.x - half, center.y + half - arm, thickness, arm), color);
            DrawRect(new Rect(center.x + half - thickness, center.y - half, thickness, arm), color);
            DrawRect(new Rect(center.x + half - thickness, center.y + half - arm, thickness, arm), color);
        }

        private void DrawScorePopups()
        {
            float width = Scaled(_settings.ScorePopupWidth);
            float height = Scaled(_settings.ScorePopupHeight);
            for (int i = 0; i < _popups.Count; i++)
            {
                ScorePopup popup = _popups[i];
                if (!popup.Active || !TryProject(popup.World, out Vector2 screen))
                {
                    continue;
                }
                float normalized = Mathf.Clamp01(
                    popup.Elapsed / Mathf.Max(0.01f, _settings.ScorePopupDuration));
                float fade = 1f - (normalized * normalized);
                // Popups drift upward as they fade so overlapping awards stay countable.
                Rect rect = new Rect(
                    screen.x - (width * 0.5f),
                    screen.y - (height * 0.5f) - (normalized * height),
                    width,
                    height);
                DrawShadowedLabel(rect, popup.Text, _popupStyle, WithAlpha(popup.Color, fade));
            }
        }

        private void DrawRoutePrompt()
        {
            if (!_routeGateActive || _safeGate == null || _riskGate == null)
            {
                return;
            }
            float ahead = _safeGate.position.z - _player.transform.position.z;
            if (ahead <= 0f || ahead > _settings.RoutePromptLeadDistance)
            {
                return;
            }
            float urgency = 1f - (ahead / Mathf.Max(1f, _settings.RoutePromptLeadDistance));
            float promptWidth = Scaled(_settings.RoutePromptWidth);
            float promptHeight = Scaled(_settings.RoutePromptHeight);
            Rect prompt = new Rect(
                (Screen.width - promptWidth) * 0.5f,
                Screen.height * _settings.RoutePromptViewportY,
                promptWidth,
                promptHeight);
            Color accent = Color.Lerp(_settings.HudSecondaryColor, _settings.HudPrimaryColor, urgency);
            DrawPanel(prompt, accent);
            DrawLabel(
                prompt,
                _settings.RoutePromptLabel,
                _centeredSmallStyle,
                WithAlpha(_settings.HudPrimaryColor, 0.5f + (0.5f * urgency)));
            // A closing bar under the plate turns the prompt into a readable approach timer.
            DrawRect(
                new Rect(prompt.x, prompt.yMax, prompt.width * urgency, Mathf.Max(1f, Scaled(_settings.HudDividerHeight))),
                accent);

            bool hasSafe = TryProject(_safeGate.position, out Vector2 safeScreen);
            bool hasRisk = TryProject(_riskGate.position, out Vector2 riskScreen);
            float chipWidth = Scaled(_settings.GateLabelWidth);
            float chipHeight = Scaled(_settings.GateLabelHeight);
            float offset = Scaled(_settings.GateLabelVerticalOffset);
            float safeY = safeScreen.y - offset;
            float riskY = riskScreen.y - offset;
            // Far down the rift both gates project onto nearly the same pixel, so the chips are
            // pushed apart instead of being painted on top of each other.
            if (hasSafe && hasRisk &&
                Mathf.Abs(safeScreen.x - riskScreen.x) < chipWidth &&
                Mathf.Abs(safeY - riskY) < chipHeight)
            {
                float push = Scaled(_settings.GateLabelSeparation) * 0.5f;
                if (safeScreen.x <= riskScreen.x)
                {
                    safeY -= push;
                    riskY += push;
                }
                else
                {
                    safeY += push;
                    riskY -= push;
                }
            }
            if (hasSafe)
            {
                DrawGateChip(
                    new Vector2(safeScreen.x, safeY),
                    safeScreen,
                    chipWidth,
                    chipHeight,
                    _settings.SafeGateLabel,
                    _settings.RiftSignalColor);
            }
            if (hasRisk)
            {
                DrawGateChip(
                    new Vector2(riskScreen.x, riskY),
                    riskScreen,
                    chipWidth,
                    chipHeight,
                    _settings.RiskGateLabel,
                    _settings.RiftDangerColor);
            }
        }

        private void DrawGateChip(
            Vector2 center,
            Vector2 anchor,
            float width,
            float height,
            string label,
            Color color)
        {
            Rect chip = new Rect(center.x - (width * 0.5f), center.y - (height * 0.5f), width, height);
            float border = BorderThickness();
            // Leader back to the gate so a nudged chip still reads as that gate's tag.
            float top = Mathf.Min(chip.yMax, anchor.y);
            float bottom = Mathf.Max(chip.yMax, anchor.y);
            DrawRect(
                new Rect(anchor.x - (border * 0.5f), top, border, Mathf.Max(0f, bottom - top)),
                WithAlpha(color, 0.4f));
            DrawRect(chip, _settings.HudPanelColor);
            DrawRect(new Rect(chip.x, chip.y, border, chip.height), color);
            DrawRect(new Rect(chip.xMax - border, chip.y, border, chip.height), color);
            DrawRect(new Rect(chip.x, chip.y, chip.width, border), WithAlpha(color, 0.55f));
            DrawRect(new Rect(chip.x, chip.yMax - border, chip.width, border), WithAlpha(color, 0.55f));
            DrawLabel(chip, label, _centeredSmallStyle, color);
        }

        private void DrawBanner()
        {
            string text = null;
            string subtitle = null;
            float alpha = 0f;
            bool hostile = false;
            if (Phase == RailShooterPhase.Entry)
            {
                text = _settings.MissionTitle;
                subtitle = _settings.EntrySubtitle;
                float duration = Mathf.Max(0.01f, _settings.EntryCardDuration);
                alpha = 1f - Mathf.Clamp01((_phaseElapsed - (duration * 0.6f)) / (duration * 0.4f));
            }
            else if (float.IsFinite(_bossBannerElapsed))
            {
                text = _settings.BossApproachLabel;
                subtitle = _settings.BossTitle;
                hostile = true;
                float duration = Mathf.Max(0.01f, _settings.EntryCardDuration * 2f);
                alpha = 1f - Mathf.Clamp01((_bossBannerElapsed - (duration * 0.6f)) / (duration * 0.4f));
                alpha *= 0.55f + (0.45f * Mathf.Abs(Mathf.Sin(_bossBannerElapsed * 6f)));
            }
            if (string.IsNullOrEmpty(text) || alpha <= 0.01f)
            {
                return;
            }
            float width = Mathf.Min(Scaled(_settings.BannerWidth), Screen.width - (Scaled(_settings.HudMargin) * 2f));
            float height = Scaled(_settings.BannerHeight);
            Rect plate = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height * _settings.BannerViewportY) - (height * 0.5f),
                width,
                height);
            Color accent = hostile ? _settings.RiftDangerColor : _settings.HudBorderColor;
            DrawRect(plate, WithAlpha(_settings.HudPanelColor, alpha));
            float rule = Mathf.Max(1f, Scaled(_settings.BannerRuleHeight));
            DrawRect(new Rect(plate.x, plate.y, plate.width, rule), WithAlpha(accent, alpha));
            DrawRect(new Rect(plate.x, plate.yMax - rule, plate.width, rule), WithAlpha(accent, alpha));
            float title = height * 0.58f;
            DrawShadowedLabel(
                new Rect(plate.x, plate.y + (height * 0.06f), plate.width, title),
                text,
                _resultStyle,
                WithAlpha(hostile ? _settings.RiftDangerColor : _settings.HudPrimaryColor, alpha));
            DrawLabel(
                new Rect(plate.x, plate.y + (height * 0.06f) + title, plate.width, height * 0.3f),
                subtitle,
                _centeredSmallStyle,
                WithAlpha(_settings.HudChargeColor, alpha));
        }

        // A persistent bleed on the frame edge while the hull is critical, so the state reads even
        // when the player never looks at the status panel.
        private void DrawLowHullEdge()
        {
            float warning = Mathf.Max(0.0001f, _settings.LowHullWarningFraction);
            float hull = _health != null ? _health.NormalizedHealth : 1f;
            if (hull <= 0f || hull > warning || _settings.LowHullEdgeOpacity <= 0.001f)
            {
                return;
            }
            float severity = 1f - (hull / warning);
            float pulse = 0.55f + (0.45f * Mathf.Abs(Mathf.Sin(_state.Elapsed * _settings.LowHullPulseSpeed)));
            float thickness = Scaled(_settings.LowHullEdgeThickness);
            const int Bands = 4;
            float slice = thickness / Bands;
            for (int i = 0; i < Bands; i++)
            {
                float band = 1f - (i / (float)Bands);
                Color color = WithAlpha(
                    _settings.HudDamageColor,
                    _settings.LowHullEdgeOpacity * severity * pulse * band * band);
                float inset = i * slice;
                DrawRect(new Rect(0f, inset, Screen.width, slice), color);
                DrawRect(new Rect(0f, Screen.height - inset - slice, Screen.width, slice), color);
                DrawRect(new Rect(inset, 0f, slice, Screen.height), color);
                DrawRect(new Rect(Screen.width - inset - slice, 0f, slice, Screen.height), color);
            }
        }

        private void DrawDamageVignette()
        {
            if (!float.IsFinite(_damageFlashElapsed))
            {
                return;
            }
            float fade = 1f - Mathf.Clamp01(
                _damageFlashElapsed / Mathf.Max(0.01f, _settings.DamageFlashDuration));
            float strength = _settings.DamageFlashStrength * fade;
            if (strength <= 0.001f)
            {
                return;
            }
            const int Bands = 7;
            float bandWidth = Screen.height * 0.045f;
            for (int i = 0; i < Bands; i++)
            {
                float band = 1f - (i / (float)Bands);
                Color color = WithAlpha(_settings.HudDamageColor, strength * band * band);
                float inset = i * bandWidth;
                DrawRect(new Rect(0f, inset, Screen.width, bandWidth), color);
                DrawRect(new Rect(0f, Screen.height - inset - bandWidth, Screen.width, bandWidth), color);
                DrawRect(new Rect(inset, 0f, bandWidth, Screen.height), color);
                DrawRect(new Rect(Screen.width - inset - bandWidth, 0f, bandWidth, Screen.height), color);
            }
        }

        private void DrawWorldReticle(Vector3 worldPosition, float size)
        {
            Vector3 screen = _camera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f)
            {
                return;
            }
            DrawBracket(new Vector2(screen.x, Screen.height - screen.y), size, _settings.HudReticleColor);
        }

        private void DrawBracket(Vector2 center, float size, Color color)
        {
            float half = size * 0.5f;
            float corner = size * 0.28f;
            float thickness = BorderThickness();
            DrawRect(new Rect(center.x - half, center.y - half, corner, thickness), color);
            DrawRect(new Rect(center.x - half, center.y - half, thickness, corner), color);
            DrawRect(new Rect(center.x + half - corner, center.y - half, corner, thickness), color);
            DrawRect(new Rect(center.x + half - thickness, center.y - half, thickness, corner), color);
            DrawRect(new Rect(center.x - half, center.y + half - thickness, corner, thickness), color);
            DrawRect(new Rect(center.x - half, center.y + half - corner, thickness, corner), color);
            DrawRect(new Rect(center.x + half - corner, center.y + half - thickness, corner, thickness), color);
            DrawRect(new Rect(center.x + half - thickness, center.y + half - corner, thickness, corner), color);
            DrawRect(new Rect(center.x - thickness * 2f, center.y - thickness * 0.5f, thickness * 4f, thickness), color);
            DrawRect(new Rect(center.x - thickness * 0.5f, center.y - thickness * 2f, thickness, thickness * 4f), color);
        }

        private void DrawLaneHudWarning()
        {
            if (!float.IsFinite(_laneElapsed))
            {
                return;
            }
            Vector3 laneWorld = new Vector3(
                _laneCenterX,
                _arenaOrigin.y,
                _player.transform.position.z + _settings.EnemySpawnAheadDistance * 0.5f);
            if (!TryProject(laneWorld, out Vector2 laneScreen))
            {
                return;
            }
            float width = Screen.width *
                (_settings.LightningLaneHalfWidth / Mathf.Max(1f, _settings.FlightBounds.x * 2f));
            bool active = _laneElapsed >= _settings.LightningLaneTelegraphDuration;
            float telegraph = Mathf.Clamp01(
                _laneElapsed / Mathf.Max(0.01f, _settings.LightningLaneTelegraphDuration));
            // A soft-edged band that tracks the lane in world space instead of a hard slab
            // painted across the whole screen.
            float peak = active
                ? 0.34f
                : 0.16f * telegraph * (0.55f + (0.45f * Mathf.Abs(Mathf.Sin(_laneElapsed * 11f))));
            const int Steps = 5;
            for (int i = Steps; i >= 1; i--)
            {
                float spread = width * (i / (float)Steps);
                float alpha = peak * (1f - ((i - 1) / (float)Steps)) * 0.5f;
                DrawRect(
                    new Rect(laneScreen.x - spread, 0f, spread * 2f, Screen.height),
                    WithAlpha(_settings.RiftDangerColor, alpha));
            }
            float edge = BorderThickness();
            Color edgeColor = WithAlpha(_settings.RiftDangerColor, active ? 0.9f : 0.35f + (0.4f * telegraph));
            DrawRect(new Rect(laneScreen.x - width, 0f, edge, Screen.height), edgeColor);
            DrawRect(new Rect(laneScreen.x + width - edge, 0f, edge, Screen.height), edgeColor);
        }

        private void DrawBossMeter()
        {
            float health = Mathf.Clamp01(_boss.Health / Mathf.Max(1f, _boss.MaximumHealth));
            float pad = Scaled(_settings.HudPanelPadding);
            float meter = Scaled(_settings.HudMeterHeight);
            float titleHeight = Scaled(_settings.HudTitleHeight);
            float width = Mathf.Min(
                Scaled(_settings.BossMeterWidth),
                Screen.width - (Scaled(_settings.HudMargin) * 2f));
            float height = Scaled(_settings.BossMeterHeight);
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                Scaled(_settings.BossMeterTop),
                width,
                height);
            DrawPanel(panel, _settings.RiftDangerColor);
            float inner = panel.width - (pad * 2f);
            Rect titleRow = new Rect(panel.x + pad, panel.y + (pad * 0.4f), inner, titleHeight);
            DrawShadowedLabel(titleRow, _settings.BossTitle, _titleStyle, _settings.HudPrimaryColor);
            DrawLabel(
                titleRow,
                string.Format(_settings.BossHealthFormat, Mathf.CeilToInt(health * 100f)),
                _valueStyle,
                _settings.RiftDangerColor);
            Rect bar = new Rect(panel.x + pad, panel.yMax - pad - meter, inner, meter);
            DrawMeter(
                bar,
                health,
                Color.Lerp(_settings.HudDamageColor, _settings.HudComboColor, health),
                _bossGhost);
            DrawBossPhasePips(bar);
        }

        // Phase thresholds marked on the sovereign's bar so the fight reads as staged rather than
        // as one long drain.
        private void DrawBossPhasePips(Rect bar)
        {
            int phases = Mathf.Max(1, _settings.BossMeterPhaseCount);
            if (phases < 2)
            {
                return;
            }
            float thickness = BorderThickness();
            for (int i = 1; i < phases; i++)
            {
                float x = bar.x + (bar.width * (i / (float)phases));
                DrawRect(
                    new Rect(x - (thickness * 0.5f), bar.y - thickness, thickness, bar.height + (thickness * 2f)),
                    WithAlpha(_settings.HudPrimaryColor, 0.55f));
            }
        }

        private void DrawResults()
        {
            float fade = Mathf.Clamp01(
                _phaseElapsed / Mathf.Max(0.01f, _settings.ResultsFadeDuration));
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.86f * fade));

            float pad = Scaled(_settings.HudPanelPadding);
            float line = Scaled(_settings.HudLineHeight);
            float rowGap = Scaled(_settings.HudRowGap);
            float sectionGap = Scaled(_settings.HudSectionGap);
            float sectionLabel = Scaled(_settings.ResultsSectionLabelHeight);
            float badge = Scaled(_settings.ResultsGradeBadgeSize);
            float columnGap = Scaled(_settings.ResultsColumnGap);
            float divider = Mathf.Max(1f, Scaled(_settings.HudDividerHeight));

            (string Label, string Value, Color Color)[] combat =
            {
                (_settings.ResultsScoreLabel, _state.Score.ToString("0000000"), _settings.HudPrimaryColor),
                (_settings.ResultsKillsLabel, _state.Kills.ToString("000"), _settings.HudPrimaryColor),
                (_settings.ResultsFormationsLabel, _state.FormationClears.ToString("00"), _settings.HudPrimaryColor),
                (_settings.ResultsChargeKillsLabel, _state.ChargeKills.ToString("00"), _settings.HudChargeColor),
                (_settings.ResultsDeflectionsLabel, _state.ProjectileDeflections.ToString("00"), _settings.HudPrimaryColor),
                (_settings.ResultsGrazesLabel, _state.Grazes.ToString("000"), _settings.HudReticleColor),
                (_settings.ResultsPickupsLabel, _state.Pickups.ToString("00"), _settings.HudPrimaryColor),
                (_settings.ResultsFlawlessLabel,
                    _state.TookDamage ? _settings.ResultsNegativeLabel : _settings.ResultsAffirmativeLabel,
                    _state.TookDamage ? _settings.HudSecondaryColor : _settings.HudReticleColor),
            };
            (string Label, string Value, Color Color)[] run =
            {
                (_settings.ResultsSigilsLabel, _state.SigilsBroken.ToString("00"), _settings.Sigils.CompletedStrokeColor),
                (_settings.ResultsChainSigilsLabel, _state.ChainSigilsBroken.ToString("00"), _settings.Sigils.ChainColor),
                (_settings.ResultsSigilStrikesLabel, _state.SigilStrikes.ToString("00"),
                    _state.SigilStrikes > 0 ? _settings.Sigils.FaultColor : _settings.HudSecondaryColor),
                (_settings.ResultsRouteGatesLabel,
                    string.Format(_settings.ResultsRouteGateFormat, _state.RouteGatesCleared, _settings.BranchGateCount),
                    _settings.HudPrimaryColor),
                (_settings.ResultsDepthLabel,
                    string.Format(_settings.DepthValueFormat, Mathf.RoundToInt(_state.Distance)),
                    _settings.HudSecondaryColor),
                (_settings.ResultsDistanceBonusLabel,
                    string.Format(_settings.ResultsGoldFormat, _distanceGoldBonus), _settings.RiftGoldColor),
                (_settings.ResultsPayoutLabel,
                    string.Format(_settings.ResultsGoldFormat, AwardedGold), _settings.RiftGoldColor),
            };
            int rowCount = Mathf.Max(combat.Length, run.Length);

            float width = Mathf.Min(
                Scaled(_settings.ResultsPanelWidth),
                Screen.width - (Scaled(_settings.HudMargin) * 2f));
            float height = (pad * 2f) + badge + sectionGap + divider + rowGap + sectionLabel +
                rowGap + (rowCount * line) + sectionGap + line;
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
            Color accent = _resultSuccess ? _settings.HudChargeColor : _settings.HudDamageColor;
            DrawPanel(panel, accent);

            float cursor = panel.y + pad;
            Rect badgeRect = new Rect(panel.x + pad, cursor, badge, badge);
            DrawRect(badgeRect, WithAlpha(accent, 0.14f));
            float border = BorderThickness();
            DrawRect(new Rect(badgeRect.x, badgeRect.y, badgeRect.width, border), WithAlpha(accent, fade));
            DrawRect(new Rect(badgeRect.x, badgeRect.yMax - border, badgeRect.width, border), WithAlpha(accent, fade));
            DrawRect(new Rect(badgeRect.x, badgeRect.y, border, badgeRect.height), WithAlpha(accent, fade));
            DrawRect(new Rect(badgeRect.xMax - border, badgeRect.y, border, badgeRect.height), WithAlpha(accent, fade));
            DrawLabel(badgeRect, ResultGrade, _gradeStyle, WithAlpha(accent, fade));

            float headX = badgeRect.xMax + pad;
            float headWidth = panel.xMax - pad - headX;
            DrawShadowedLabel(
                new Rect(headX, cursor + (badge * 0.16f), headWidth, badge * 0.4f),
                _resultSuccess ? _settings.ResultsSuccessLabel : _settings.ResultsFailureLabel,
                _titleStyle,
                WithAlpha(_resultSuccess ? _settings.HudPrimaryColor : _settings.HudDamageColor, fade));
            DrawLabel(
                new Rect(headX, cursor + (badge * 0.56f), headWidth, badge * 0.26f),
                _settings.ResultsGradeLabel,
                _smallStyle,
                WithAlpha(_settings.HudSecondaryColor, fade));
            cursor += badge + sectionGap;
            DrawRect(new Rect(panel.x, cursor, panel.width, divider), WithAlpha(_settings.HudDividerColor, fade));
            cursor += divider + rowGap;

            float contentWidth = panel.width - (pad * 4f);
            float columnWidth = (contentWidth - columnGap) * 0.5f;
            float leftX = panel.x + (pad * 2f);
            float rightX = leftX + columnWidth + columnGap;
            DrawLabel(
                new Rect(leftX, cursor, columnWidth, sectionLabel),
                _settings.ResultsCombatSectionLabel,
                _sectionStyle,
                WithAlpha(accent, fade));
            DrawLabel(
                new Rect(rightX, cursor, columnWidth, sectionLabel),
                _settings.ResultsRunSectionLabel,
                _sectionStyle,
                WithAlpha(accent, fade));
            cursor += sectionLabel + rowGap;

            for (int i = 0; i < rowCount; i++)
            {
                // Rows land one after another so the tally reads as it is counted up.
                float reveal = Mathf.Clamp01(
                    (_phaseElapsed - (i * _settings.ResultsRowStagger)) /
                    Mathf.Max(0.01f, _settings.ResultsFadeDuration));
                float rowFade = reveal * fade;
                if (i < combat.Length)
                {
                    DrawResultRow(new Rect(leftX, cursor, columnWidth, line), combat[i], rowFade);
                }
                if (i < run.Length)
                {
                    DrawResultRow(new Rect(rightX, cursor, columnWidth, line), run[i], rowFade);
                }
                cursor += line;
            }

            if (_phaseElapsed >= _settings.ResultsSkipDelay)
            {
                float pulse = 0.5f + (0.5f * Mathf.Abs(Mathf.Sin(_phaseElapsed * 3.2f)));
                DrawLabel(
                    new Rect(panel.x + pad, cursor + sectionGap, panel.width - (pad * 2f), line),
                    _settings.ResultsSkipLabel,
                    _centeredSmallStyle,
                    WithAlpha(_settings.HudSecondaryColor, pulse * fade));
            }
        }

        private void DrawResultRow(Rect row, (string Label, string Value, Color Color) entry, float fade)
        {
            if (fade <= 0.01f)
            {
                return;
            }
            DrawLabel(row, entry.Label, _statLabelStyle, WithAlpha(_settings.HudSecondaryColor, fade));
            DrawLabel(row, entry.Value, _valueStyle, WithAlpha(entry.Color, fade));
        }

        private void DrawPanel(Rect rect, Color accent)
        {
            Vector2 shadow = _settings.HudPanelShadowOffset * _hudScale;
            DrawRect(
                new Rect(rect.x + shadow.x, rect.y + shadow.y, rect.width, rect.height),
                _settings.HudPanelShadowColor);
            DrawRect(rect, _settings.HudPanelColor);
            DrawPanelScanlines(rect);
            float border = BorderThickness();
            Color edge = WithAlpha(accent, 0.5f);
            DrawRect(new Rect(rect.x, rect.y, rect.width, border), edge);
            DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), edge);
            DrawRect(new Rect(rect.x, rect.y, border, rect.height), edge);
            DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), edge);
            DrawPanelCorners(rect, accent, border);
            DrawRect(
                new Rect(rect.x, rect.y, Scaled(_settings.HudPanelAccentWidth), rect.height),
                accent);
        }

        // Faint horizontal ruling inside the plate: it keeps the panel reading as a screen rather
        // than a flat fill without ever competing with the text.
        private void DrawPanelScanlines(Rect rect)
        {
            float spacing = Scaled(_settings.HudPanelScanlineSpacing);
            if (spacing < 1f || _settings.HudPanelScanlineColor.a <= 0.001f)
            {
                return;
            }
            for (float y = rect.y + spacing; y < rect.yMax - 1f; y += spacing)
            {
                DrawRect(new Rect(rect.x, y, rect.width, 1f), _settings.HudPanelScanlineColor);
            }
        }

        // Tinted band behind a panel's title. It is inset past the accent bar and the frame so the
        // border and corner brackets stay unbroken.
        private void DrawPanelHeader(Rect rect, float headerHeight)
        {
            float border = BorderThickness();
            float accent = Scaled(_settings.HudPanelAccentWidth);
            float height = Mathf.Min(headerHeight, rect.height) - border;
            float width = rect.width - accent - border;
            if (height <= 0f || width <= 0f)
            {
                return;
            }
            DrawRect(
                new Rect(rect.x + accent, rect.y + border, width, height),
                _settings.HudPanelHeaderColor);
        }

        private void DrawPanelCorners(Rect rect, Color accent, float thickness)
        {
            float length = Mathf.Min(
                Scaled(_settings.HudPanelCornerLength),
                Mathf.Min(rect.width, rect.height) * 0.4f);
            if (length <= 0f)
            {
                return;
            }
            DrawRect(new Rect(rect.x, rect.y, length, thickness), accent);
            DrawRect(new Rect(rect.x, rect.y, thickness, length), accent);
            DrawRect(new Rect(rect.xMax - length, rect.y, length, thickness), accent);
            DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, length), accent);
            DrawRect(new Rect(rect.x, rect.yMax - thickness, length, thickness), accent);
            DrawRect(new Rect(rect.x, rect.yMax - length, thickness, length), accent);
            DrawRect(new Rect(rect.xMax - length, rect.yMax - thickness, length, thickness), accent);
            DrawRect(new Rect(rect.xMax - thickness, rect.yMax - length, thickness, length), accent);
        }

        private void DrawMeter(Rect rect, float normalized, Color fill)
        {
            DrawMeter(rect, normalized, fill, 0f);
        }

        private void DrawMeter(Rect rect, float normalized, Color fill, float ghost)
        {
            normalized = Mathf.Clamp01(normalized);
            DrawRect(rect, _settings.HudMeterTrackColor);
            float inset = Scaled(_settings.HudMeterInset);
            Rect body = new Rect(
                rect.x + inset,
                rect.y + inset,
                Mathf.Max(0f, rect.width - (inset * 2f)),
                Mathf.Max(0f, rect.height - (inset * 2f)));
            ghost = Mathf.Clamp01(ghost);
            if (ghost > normalized)
            {
                // Trailing bar showing what was just lost, so a hit reads as an amount.
                DrawRect(
                    new Rect(
                        body.x + (body.width * normalized),
                        body.y,
                        body.width * (ghost - normalized),
                        body.height),
                    _settings.HudMeterGhostColor);
            }
            Rect filled = new Rect(body.x, body.y, body.width * normalized, body.height);
            DrawRect(filled, fill);
            float cap = Scaled(_settings.HudMeterCapWidth);
            if (cap > 0f && filled.width > cap)
            {
                DrawRect(
                    new Rect(filled.xMax - cap, filled.y, cap, filled.height),
                    WithAlpha(Color.Lerp(fill, Color.white, 0.55f), _settings.HudMeterCapOpacity));
            }
            DrawMeterSegments(body);
        }

        private void DrawMeterSegments(Rect body)
        {
            int segments = _settings.HudMeterSegmentCount;
            float width = Scaled(_settings.HudMeterSegmentWidth);
            if (segments < 2 || width <= 0f || body.width <= 0f)
            {
                return;
            }
            for (int i = 1; i < segments; i++)
            {
                DrawRect(
                    new Rect(
                        body.x + (body.width * (i / (float)segments)) - (width * 0.5f),
                        body.y,
                        width,
                        body.height),
                    _settings.HudMeterSegmentColor);
            }
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.TemporaryHealthPoolDepleted -= HandleTemporaryHullDepleted;
                _health.Damaged -= HandlePlayerDamaged;
                if (_health.HasTemporaryHealthPool)
                {
                    _health.EndTemporaryHealthPool();
                }
            }
            if (IsActive)
            {
                RestoreWorldState();
            }
            DuneVectorAudioManager.Instance?.ExitRailSubgameMusic();
            IsAnyRailShooterActive = false;
        }
    }
}
