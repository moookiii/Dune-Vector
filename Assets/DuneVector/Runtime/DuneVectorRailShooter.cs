using System;
using System.Collections.Generic;
using UnityEngine;
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

    public enum RailShooterTrick
    {
        None,
        BarrelRollLeft,
        BarrelRollRight,
        Corkscrew,
        Loop,
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
        public int RouteGatesCleared;
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
            public bool Active;
            public bool EnemyOwned;
            public Vector3 Velocity;
            public float Remaining;
            public float Radius;
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
            public readonly List<Transform> Rotators = new List<Transform>();
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
        private readonly List<ScorePopup> _popups = new List<ScorePopup>();
        private readonly List<RailEnemy> _chargeLocks = new List<RailEnemy>();

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

        private Vector3 _arenaOrigin;
        private float _startZ;
        private float _furthestSegmentZ;
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
        private bool _resultSuccess;
        private bool _rewardCommitted;
        private float _cameraShake;
        private float _fovImpulse;
        private float _timeSinceSteeringInput;
        private int _difficulty = 1;
        private Vector3 _cameraBasePosition;
        private Vector2 _aimViewport = new Vector2(0.5f, 0.5f);
        private float _hitMarkerElapsed = float.PositiveInfinity;
        private float _killMarkerElapsed = float.PositiveInfinity;
        private float _damageFlashElapsed = float.PositiveInfinity;
        private float _lastHitCueAt = float.NegativeInfinity;
        private bool _chargeReadyCued;
        private bool _bossAnnounced;
        private float _bossBannerElapsed = float.PositiveInfinity;

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
            _difficulty = Mathf.Clamp(difficulty, 1, Mathf.Max(1, _settings.DifficultyCeiling));
            _random = new System.Random(unchecked(seed ^ _settings.SeedOffset ^ (difficulty * 73856093)));
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
            _timeSinceSteeringInput = 0f;
            _aimViewport = new Vector2(0.5f, 0.5f);
            _hitMarkerElapsed = float.PositiveInfinity;
            _killMarkerElapsed = float.PositiveInfinity;
            _damageFlashElapsed = float.PositiveInfinity;
            _lastHitCueAt = float.NegativeInfinity;
            _chargeReadyCued = false;
            _bossAnnounced = false;
            _bossBannerElapsed = float.PositiveInfinity;
            _chargeLocks.Clear();
            AwardedGold = 0;
            ResultGrade = "C";
            ResetPools();
            ResetCourse();
            EnterRailPresentation();
            _modeRoot.gameObject.SetActive(true);
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

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            RailShooterCommand command = new RailShooterCommand(_input != null ? _input.CurrentCommand : default);
            _phaseElapsed += deltaTime;
            _state.Elapsed += deltaTime;
            if (Phase != RailShooterPhase.Results)
            {
                TickFlight(command, deltaTime);
                TickEnvironment(deltaTime);
                TickRouteGates();
                TickPickups(deltaTime);
                TickWeapons(command, deltaTime);
                TickEnemies(deltaTime);
                TickProjectiles(deltaTime);
                TickLaneAttack(deltaTime);
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
            _timeSinceSteeringInput = steering
                ? 0f
                : _timeSinceSteeringInput + deltaTime;
            Vector2 targetVelocity = steering
                ? command.Move * _settings.LateralSpeed
                : Vector2.zero;
            _state.LateralVelocity = Vector2.Lerp(
                _state.LateralVelocity,
                targetVelocity,
                DuneVectorMath.Sharpness(_settings.LateralAccelerationSharpness, deltaTime));
            _state.FlightOffset += _state.LateralVelocity * deltaTime;
            if (!steering && _timeSinceSteeringInput >= _settings.PositionRecenterDelay)
            {
                _state.FlightOffset = Vector2.Lerp(
                    _state.FlightOffset,
                    Vector2.zero,
                    DuneVectorMath.Sharpness(_settings.PositionRecenterSharpness, deltaTime));
            }
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
            Vector3 cameraAnchor = _arenaOrigin + new Vector3(0f, 0f, _state.Distance);
            Vector3 desiredCameraPosition = cameraAnchor + _settings.CameraLocalOffset;
            _cameraBasePosition = Vector3.Lerp(
                _cameraBasePosition,
                desiredCameraPosition,
                DuneVectorMath.Sharpness(_settings.CameraPositionSharpness, deltaTime));
            _camera.transform.position = _cameraBasePosition +
                (UnityEngine.Random.insideUnitSphere * _cameraShake);
            _camera.transform.rotation = Quaternion.Slerp(
                _camera.transform.rotation,
                Quaternion.identity,
                DuneVectorMath.Sharpness(_settings.CameraRotationSharpness, deltaTime));

            Vector2 restingViewport = CalculateRestingAimViewport(
                _state.FlightOffset,
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
            float duration = GetTrickDuration();
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
            float normalized = Mathf.Clamp01(_state.TrickElapsed / GetTrickDuration());
            float turn = normalized * 360f;
            return _state.Trick switch
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
            while (_state.Distance >= _nextPickupDistance &&
                   _nextPickupDistance < _settings.BossSpawnDistance)
            {
                SpawnCoursePickup((PickupKind)(_pickupSequence % 3), null);
                _pickupSequence++;
                _nextPickupDistance += _settings.PickupSpacing;
            }

            if (!_bossAnnounced &&
                _state.Distance >= _settings.BossSpawnDistance - _settings.BossApproachLeadDistance)
            {
                _bossAnnounced = true;
                _bossBannerElapsed = 0f;
            }

            if (_state.Distance >= _settings.BossSpawnDistance)
            {
                BeginBoss();
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
                    FireEnemyProjectile(enemy, 1);
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
            Vector3 bossPosition = _arenaOrigin + new Vector3(
                Mathf.Sin(orbit) * _settings.FormationWidth * 0.45f,
                Mathf.Cos(orbit * 0.7f) * _settings.FormationHeight * 0.4f,
                _state.Distance + (_settings.EnemySpawnAheadDistance * 0.72f));
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
                FireEnemyProjectile(_boss, _settings.BossProjectileFanCount + ((phase - 1) * 2));
                _boss.NextFireAt += _settings.BossFireInterval / (1f + ((phase - 1) * 0.25f));
            }
            if (_boss.Age >= _boss.NextSpecialAt && !float.IsFinite(_laneElapsed))
            {
                BeginLaneAttack(_arenaOrigin.x + (Mathf.Sin(_boss.Age) * _settings.FormationWidth * 0.35f));
                _boss.NextSpecialAt += _settings.BossLaneAttackInterval / (1f + ((phase - 1) * 0.2f));
            }
            if (Vector3.Distance(bossPosition, _player.transform.position) <=
                _boss.ContactRadius + _settings.PlayerCollisionRadius)
            {
                DamagePlayer(_settings.BossContactDamage, "Vesper Sovereign hull grind");
            }
        }

        private void TickWeapons(in RailShooterCommand command, float deltaTime)
        {
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
                    UpdateChargeLock();
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
                _chargeReadyCued = false;
                _chargeLocks.Clear();
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
            projectile.Transform.position = _player.transform.position + (direction * 2.2f);
            projectile.Velocity = direction * _settings.RegularShotSpeed;
            projectile.Remaining = _settings.RegularShotLifetime;
            projectile.Radius = _settings.RegularShotRadius;
            projectile.Transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            projectile.Transform.localScale = new Vector3(
                _settings.RegularShotRadius,
                _settings.RegularShotRadius,
                _settings.RegularShotVisualLength);
            projectile.Root.SetActive(true);
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
            _chargedBeamElapsed = 0f;
            _chargedBeamVisual.gameObject.SetActive(true);
            _cameraShake = Mathf.Max(_cameraShake, _settings.ImpactCameraShake * 1.6f);
            _state.ChargeElapsed = 0f;
            _chargeLock = null;
            _chargeReadyCued = false;
            _chargeLocks.Clear();
            PlayCue(_settings.ChargedFireEvent, _player.transform.position);
        }

        private void UpdateChargeLock()
        {
            RailEnemy previous = _chargeLock;
            // The reticle rides the aim viewport, so lock-on has to search around the
            // crosshair rather than the middle of the screen.
            float radius = Mathf.Max(
                _settings.ChargeLockViewportRadius,
                _settings.ChargeLockViewportRadius * ChargeNormalized() * 2f);
            _chargeLock = FindViewportTarget(radius, _settings.ChargedBeamRange);
            CollectChargeLocks(radius);
            if (_chargeLock != null && _chargeLock != previous)
            {
                PlayCue(_settings.TargetLockEvent, _chargeLock.Transform.position);
            }
        }

        private void CollectChargeLocks(float viewportRadius)
        {
            _chargeLocks.Clear();
            int capacity = Mathf.Max(1, _settings.ChargedLockCapacity);
            for (int i = 0; i < _enemies.Count && _chargeLocks.Count < capacity; i++)
            {
                RailEnemy enemy = _enemies[i];
                if (enemy.Active &&
                    TryScoreViewportTarget(enemy, viewportRadius, _settings.ChargedBeamRange, out _))
                {
                    _chargeLocks.Add(enemy);
                }
            }
            if (_boss != null && _boss.Active && _chargeLocks.Count < capacity &&
                TryScoreViewportTarget(_boss, viewportRadius, _settings.ChargedBeamRange, out _))
            {
                _chargeLocks.Add(_boss);
            }
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
            score = float.PositiveInfinity;
            if (_camera == null || enemy == null || enemy.Transform == null)
            {
                return false;
            }
            Vector3 viewport = _camera.WorldToViewportPoint(enemy.Transform.position);
            if (viewport.z <= 0f)
            {
                return false;
            }
            if (Vector3.Distance(_player.transform.position, enemy.Transform.position) > maximumRange)
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

        private void FireEnemyProjectile(RailEnemy source, int fanCount)
        {
            Vector3 predictedPlayer = _player.transform.position + new Vector3(
                _state.LateralVelocity.x,
                _state.LateralVelocity.y,
                0f) * _settings.EnemyPredictiveLeadSeconds;
            Vector3 baseDirection = (predictedPlayer - source.Transform.position).normalized;
            int count = Mathf.Max(1, fanCount);
            for (int i = 0; i < count; i++)
            {
                PooledProjectile projectile = AcquireProjectile(_enemyProjectiles);
                if (projectile == null)
                {
                    break;
                }
                float normalized = count > 1 ? i / (float)(count - 1) : 0.5f;
                float angle = Mathf.Lerp(
                    -_settings.BossProjectileFanAngle * 0.5f,
                    _settings.BossProjectileFanAngle * 0.5f,
                    normalized);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.forward) * baseDirection;
                projectile.Active = true;
                projectile.EnemyOwned = true;
                projectile.Transform.position = source.Transform.position;
                projectile.Velocity = direction * _settings.EnemyProjectileSpeed;
                projectile.Remaining = _settings.EnemyProjectileLifetime;
                projectile.Radius = _settings.EnemyProjectileRadius;
                projectile.Transform.localScale = Vector3.one * (_settings.EnemyProjectileRadius * 2f);
                projectile.Root.SetActive(true);
            }
        }

        private void TickProjectiles(float deltaTime)
        {
            TickPlayerProjectiles(deltaTime);
            for (int i = 0; i < _enemyProjectiles.Count; i++)
            {
                PooledProjectile projectile = _enemyProjectiles[i];
                if (!projectile.Active)
                {
                    continue;
                }
                projectile.Remaining -= deltaTime;
                projectile.Transform.position += projectile.Velocity * deltaTime;
                if (projectile.Remaining <= 0f ||
                    projectile.Transform.position.z < _player.transform.position.z - _settings.EnemyDespawnBehindDistance)
                {
                    DeactivateProjectile(projectile);
                    continue;
                }
                float playerDistance = Vector3.Distance(
                    projectile.Transform.position,
                    _player.transform.position);
                if (_state.Trick != RailShooterTrick.None &&
                    playerDistance <= _settings.RollProjectileDeflectRadius)
                {
                    DeactivateProjectile(projectile);
                    _state.ProjectileDeflections++;
                    AddScore(_settings.ProjectileDeflectScore);
                    SpawnImpact(projectile.Transform.position, _settings.RegularShotRadius * 3f);
                    continue;
                }
                if (playerDistance <= projectile.Radius + _settings.PlayerCollisionRadius)
                {
                    DamagePlayer(_settings.EnemyProjectileDamage, "Predictive rift projectile");
                    DeactivateProjectile(projectile);
                }
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
                    _arenaOrigin.x - _settings.BranchGateHorizontalOffset,
                    _arenaOrigin.y,
                    gateZ);
                _riskGate.position = new Vector3(
                    _arenaOrigin.x + _settings.BranchGateHorizontalOffset,
                    _arenaOrigin.y,
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
            pickup.Transform.position = worldPosition ?? (_arenaOrigin + new Vector3(
                side * _settings.FlightBounds.x * _settings.PickupRiskLineFraction,
                Mathf.Sin(_pickupSequence * 1.7f) * _settings.FlightBounds.y * 0.65f,
                _state.Distance + _settings.PickupSpawnAheadDistance));
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
            _laneCenterX = Mathf.Clamp(
                worldX,
                _arenaOrigin.x - _settings.FlightBounds.x,
                _arenaOrigin.x + _settings.FlightBounds.x);
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
            Vector3 start = new Vector3(_laneCenterX, _arenaOrigin.y - _settings.CorridorHalfHeight, _player.transform.position.z);
            Vector3 end = new Vector3(_laneCenterX, _arenaOrigin.y + _settings.CorridorHalfHeight, _player.transform.position.z + _settings.EnemySpawnAheadDistance);
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
            _chargeLock = null;
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
            if (success)
            {
                AwardedGold += _settings.BossGoldReward;
            }
        }

        private void TickResults(in RailShooterCommand command)
        {
            bool skipRequested = _phaseElapsed >= _settings.ResultsSkipDelay &&
                (command.FirePressed || command.BombPressed || command.TrickPressed);
            if (_phaseElapsed < _settings.ResultHoldDuration && !skipRequested)
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
                if (i % 3 == 0)
                {
                    Transform gate = DuneVectorVisuals.CreateRingVisual(
                        segment.Root,
                        TraversalRingType.Flight,
                        _materials,
                        _settings.GateRadius,
                        _ringSettings);
                    gate.name = "Rift Signal Gate Architecture";
                    segment.Rotators.Add(gate);
                }
                _segments.Add(segment);
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

        private void BuildProjectilePools()
        {
            for (int i = 0; i < _settings.PlayerProjectilePoolSize; i++)
            {
                _playerProjectiles.Add(CreateProjectile(
                    $"Player Energy Bolt {i + 1:00}",
                    PrimitiveType.Cube,
                    _materials.DroneAccent));
            }
            for (int i = 0; i < _settings.EnemyProjectilePoolSize; i++)
            {
                _enemyProjectiles.Add(CreateProjectile(
                    $"Enemy Predictive Bolt {i + 1:00}",
                    PrimitiveType.Sphere,
                    _materials.EnemyCore));
            }
        }

        private PooledProjectile CreateProjectile(string name, PrimitiveType primitive, Material material)
        {
            Transform visual = CreatePart(
                primitive,
                name,
                _projectileRoot,
                Vector3.zero,
                Vector3.one,
                Quaternion.identity,
                material);
            PooledProjectile projectile = new PooledProjectile
            {
                Root = visual.gameObject,
                Transform = visual,
            };
            projectile.Root.SetActive(false);
            return projectile;
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
                DuneVectorVisuals.CreateRingVisual(
                    root,
                    ringType,
                    _materials,
                    _settings.PickupRadius,
                    _ringSettings);
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
            DuneVectorVisuals.CreateRingVisual(
                _safeGate,
                TraversalRingType.Flight,
                _materials,
                _settings.BranchGateRadius,
                _ringSettings);
            DuneVectorVisuals.CreateRingVisual(
                _riskGate,
                TraversalRingType.UpperFlight,
                _materials,
                _settings.BranchGateRadius,
                _ringSettings);
            _safeGate.gameObject.SetActive(false);
            _riskGate.gameObject.SetActive(false);
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
        }

        private void ResetCourse()
        {
            _furthestSegmentZ = _startZ;
            for (int i = 0; i < _segments.Count; i++)
            {
                _furthestSegmentZ += _settings.EnvironmentSegmentSpacing;
                ResetSegment(_segments[i], _furthestSegmentZ, i);
            }
            for (int i = 0; i < _speedStreaks.Count; i++)
            {
                ResetSpeedStreak(_speedStreaks[i], i);
            }
        }

        private void ResetSegment(RiftSegment segment, float z, int identity)
        {
            segment.Root.position = new Vector3(_arenaOrigin.x, _arenaOrigin.y, z);
            segment.Root.rotation = Quaternion.identity;
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
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = _settings.RiftBackgroundColor;
            _camera.fieldOfView = _settings.CameraFieldOfView;
            _camera.transform.position = _arenaOrigin + _settings.CameraLocalOffset;
            _camera.transform.rotation = Quaternion.identity;
            _cameraBasePosition = _camera.transform.position;
            if (_settings.RiftFogEnabled)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = _settings.RiftFogColor;
                RenderSettings.fogStartDistance = _settings.RiftFogStartDistance;
                RenderSettings.fogEndDistance = _settings.RiftFogEndDistance;
            }
            Cursor.lockState = CursorLockMode.Locked;
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
            Cursor.lockState = _savedInputEnabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_savedInputEnabled;
            _input?.SetInputEnabled(_savedInputEnabled);
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

        private void OnGUI()
        {
            if (!IsActive || Event.current.type != EventType.Repaint || _settings == null ||
                Time.timeScale <= 0f)
            {
                return;
            }
            EnsureHudStyles();
            GUI.depth = -1300;
            // World-anchored layers first so the chrome always sits on top of them.
            DrawLaneHudWarning();
            DrawDamageVignette();
            DrawScorePopups();
            DrawReticles();
            DrawRoutePrompt();
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

        private void EnsureHudStyles()
        {
            if (_bodyStyle != null)
            {
                return;
            }
            Font font = _settings.HudFont != null ? _settings.HudFont : GUI.skin.font;
            _smallStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = _settings.HudSmallFontSize,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = _settings.HudSecondaryColor },
            };
            _bodyStyle = new GUIStyle(_smallStyle)
            {
                fontSize = _settings.HudBodyFontSize,
                normal = { textColor = _settings.HudPrimaryColor },
            };
            _titleStyle = new GUIStyle(_bodyStyle)
            {
                fontSize = _settings.HudTitleFontSize,
                fontStyle = FontStyle.Bold,
            };
            _resultStyle = new GUIStyle(_titleStyle)
            {
                fontSize = _settings.HudResultFontSize,
                alignment = TextAnchor.MiddleCenter,
            };
            _popupStyle = new GUIStyle(_bodyStyle)
            {
                fontSize = _settings.ScorePopupFontSize,
                alignment = TextAnchor.MiddleCenter,
            };
            _centeredSmallStyle = new GUIStyle(_smallStyle)
            {
                alignment = TextAnchor.MiddleCenter,
            };
            _statLabelStyle = new GUIStyle(_smallStyle)
            {
                fontSize = _settings.HudBodyFontSize,
                alignment = TextAnchor.MiddleLeft,
            };
            _statValueStyle = new GUIStyle(_bodyStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };
        }

        private void DrawLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = previous;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private void DrawHudPanels()
        {
            float margin = _settings.HudMargin;
            float pad = _settings.HudPanelPadding;
            float line = _settings.HudLineHeight;
            float meter = _settings.HudMeterHeight;
            float width = _settings.HudPanelWidth;
            float inner = width - (pad * 2f);

            // Every row is laid out from a running cursor and the panel is sized to match, so
            // no label can ever spill past the panel or off the top of the screen.
            float leftHeight = (pad * 2f) + _settings.HudTitleHeight + (line * 4f) +
                (_settings.ProgressMeterHeight * 2f) + 10f;
            Rect left = new Rect(margin, margin, width, leftHeight);
            DrawPanel(left);
            float cursor = left.y + pad;
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, _settings.HudTitleHeight),
                _settings.MissionTitle,
                _titleStyle,
                _settings.HudPrimaryColor);
            cursor += _settings.HudTitleHeight;
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                _settings.MissionSubtitle,
                _smallStyle,
                _settings.HudSecondaryColor);
            cursor += line;
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                $"SCORE  {_state.Score:0000000}",
                _bodyStyle,
                _settings.HudPrimaryColor);
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                $"HIT  {_state.Kills:000}",
                _statValueStyle,
                _settings.HudPrimaryColor);
            cursor += line;

            float comboFill = _state.Combo > 1 && float.IsFinite(_comboExpiresAt)
                ? Mathf.Clamp01((_comboExpiresAt - _state.Elapsed) / Mathf.Max(0.01f, _settings.ComboWindow))
                : 0f;
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                $"COMBO  x{_state.ComboMultiplier:0.0}",
                _smallStyle,
                _state.Combo > 1 ? _settings.HudComboColor : _settings.HudSecondaryColor);
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                $"FORMATIONS  {_state.FormationClears:00}",
                _statValueStyle,
                _settings.HudSecondaryColor);
            cursor += line;
            DrawMeter(
                new Rect(left.x + pad, cursor, inner, _settings.ProgressMeterHeight),
                comboFill,
                _settings.HudComboColor);
            cursor += _settings.ProgressMeterHeight + 6f;

            float depth = Mathf.Clamp01(_state.Distance / Mathf.Max(1f, _settings.BossSpawnDistance));
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                _settings.ProgressLabel,
                _smallStyle,
                _settings.HudSecondaryColor);
            DrawLabel(
                new Rect(left.x + pad, cursor, inner, line),
                _state.Route == RailShooterRoute.Black
                    ? _settings.RiskRouteLabel
                    : _settings.SafeRouteLabel,
                _statValueStyle,
                _state.Route == RailShooterRoute.Black
                    ? _settings.RiftDangerColor
                    : _settings.RiftSignalColor);
            cursor += line;
            DrawMeter(
                new Rect(left.x + pad, cursor, inner, _settings.ProgressMeterHeight),
                depth,
                _settings.HudChargeColor);

            float rightHeight = (pad * 2f) + (line * 3f) + (meter * 2f) + 16f;
            Rect right = new Rect(Screen.width - margin - width, margin, width, rightHeight);
            DrawPanel(right);
            cursor = right.y + pad;
            float hull = _health != null ? _health.NormalizedHealth : 0f;
            bool lowHull = hull <= _settings.LowHullWarningFraction;
            float hullPulse = lowHull
                ? 0.55f + (0.45f * Mathf.Abs(Mathf.Sin(_state.Elapsed * _settings.LowHullPulseSpeed)))
                : 1f;
            DrawLabel(
                new Rect(right.x + pad, cursor, inner, line),
                $"RIFT HULL  {Mathf.CeilToInt(hull * 100f):000}%",
                _bodyStyle,
                WithAlpha(lowHull ? _settings.HudDamageColor : _settings.HudPrimaryColor, hullPulse));
            cursor += line;
            DrawMeter(
                new Rect(right.x + pad, cursor, inner, meter),
                hull,
                Color.Lerp(_settings.HudDamageColor, _settings.HudReticleColor, hull));
            cursor += meter + 8f;
            DrawLabel(
                new Rect(right.x + pad, cursor, inner, line),
                "BOMBS",
                _bodyStyle,
                _settings.HudPrimaryColor);
            DrawBombPips(new Rect(right.x + pad, cursor, inner, line));
            cursor += line;
            DrawLabel(
                new Rect(right.x + pad, cursor, inner, line),
                "MANEUVER",
                _smallStyle,
                _settings.HudSecondaryColor);
            cursor += line;
            DrawMeter(
                new Rect(right.x + pad, cursor, inner, meter),
                _state.ManeuverEnergy / Mathf.Max(1f, _settings.ManeuverEnergyCapacity),
                _settings.HudChargeColor);

            float chargeHeight = (pad * 0.5f * 2f) + line + meter;
            Rect chargeRect = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - margin - chargeHeight - line,
                width,
                chargeHeight);
            DrawPanel(chargeRect);
            DrawLabel(
                new Rect(chargeRect.x + 12f, chargeRect.y + (pad * 0.5f), chargeRect.width - 24f, line),
                _chargeLock != null
                    ? $"CHARGE // LOCK {_chargeLocks.Count:00}"
                    : "CHARGE // PENETRATION",
                _smallStyle,
                _chargeLock != null ? _settings.HudChargeColor : _settings.HudSecondaryColor);
            DrawMeter(
                new Rect(
                    chargeRect.x + 12f,
                    chargeRect.y + (pad * 0.5f) + line,
                    chargeRect.width - 24f,
                    meter),
                ChargeNormalized(),
                _settings.HudChargeColor);

            DrawLabel(
                new Rect(margin, Screen.height - margin - line, Screen.width - (margin * 2f), line),
                _settings.ControlsLabel,
                _smallStyle,
                WithAlpha(_settings.HudSecondaryColor, 0.85f));
        }

        private void DrawBombPips(Rect row)
        {
            int capacity = Mathf.Max(1, _settings.MaximumBombs);
            float size = Mathf.Min(row.height * 0.45f, 14f);
            float spacing = size + 8f;
            float x = row.xMax - (capacity * spacing) + (spacing - size);
            float y = row.y + ((row.height - size) * 0.5f);
            for (int i = 0; i < capacity; i++)
            {
                Rect pip = new Rect(x + (i * spacing), y, size, size);
                DrawRect(pip, new Color(0f, 0f, 0f, 0.72f));
                if (i < _state.Bombs)
                {
                    DrawRect(
                        new Rect(pip.x + 2f, pip.y + 2f, pip.width - 4f, pip.height - 4f),
                        _settings.HudBombColor);
                }
                else
                {
                    DrawRect(
                        new Rect(pip.x, pip.y, pip.width, 1f),
                        WithAlpha(_settings.HudSecondaryColor, 0.6f));
                }
            }
        }

        private void DrawReticles()
        {
            DrawWorldReticle(
                _player.transform.position + (_state.AimDirection * _settings.NearReticleDistance),
                _settings.ReticleNearSize);
            Vector3 farAim = _player.transform.position +
                (_state.AimDirection * _settings.FarReticleDistance);
            DrawWorldReticle(farAim, _settings.ReticleFarSize);

            RailEnemy assist = FindViewportTarget(
                _settings.AimAssistViewportRadius,
                _settings.AimAssistRange);
            if (assist != null && assist != _chargeLock &&
                TryProject(assist.Transform.position, out Vector2 assistScreen))
            {
                DrawBracket(
                    assistScreen,
                    _settings.TargetBracketSize,
                    WithAlpha(_settings.HudReticleColor, 0.6f));
                DrawTargetHealth(assist, assistScreen, _settings.TargetBracketSize);
            }
            for (int i = 0; i < _chargeLocks.Count; i++)
            {
                RailEnemy locked = _chargeLocks[i];
                if (locked != null && locked.Active &&
                    TryProject(locked.Transform.position, out Vector2 lockScreen))
                {
                    DrawBracket(lockScreen, _settings.LockBracketSize, _settings.HudChargeColor);
                }
            }
            if (TryProject(farAim, out Vector2 markerAnchor))
            {
                DrawHitMarkers(markerAnchor);
            }
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
                center.y + (size * 0.5f) + 4f,
                width,
                Mathf.Max(2f, _settings.ProgressMeterHeight * 0.5f));
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
                    _settings.HitMarkerSize,
                    WithAlpha(_settings.HudPrimaryColor, fade));
            }
            if (float.IsFinite(_killMarkerElapsed))
            {
                float normalized = Mathf.Clamp01(
                    _killMarkerElapsed / Mathf.Max(0.01f, _settings.KillMarkerDuration));
                DrawCross(
                    center,
                    Mathf.Lerp(_settings.HitMarkerSize, _settings.KillMarkerSize, normalized),
                    WithAlpha(_settings.HudComboColor, 1f - normalized));
            }
        }

        private void DrawCross(Vector2 center, float size, Color color)
        {
            float half = size * 0.5f;
            float thickness = Mathf.Max(1f, _settings.ReticleLineThickness);
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
                DrawLabel(
                    new Rect(screen.x - 140f, screen.y - 16f, 280f, 32f),
                    popup.Text,
                    _popupStyle,
                    WithAlpha(popup.Color, fade));
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
            DrawLabel(
                new Rect(0f, Screen.height * 0.24f, Screen.width, 30f),
                _settings.RoutePromptLabel,
                _centeredSmallStyle,
                WithAlpha(_settings.HudPrimaryColor, 0.45f + (0.55f * urgency)));
            if (TryProject(_safeGate.position, out Vector2 safeScreen))
            {
                DrawLabel(
                    new Rect(safeScreen.x - 130f, safeScreen.y - 12f, 260f, 24f),
                    _settings.SafeGateLabel,
                    _centeredSmallStyle,
                    _settings.RiftSignalColor);
            }
            if (TryProject(_riskGate.position, out Vector2 riskScreen))
            {
                DrawLabel(
                    new Rect(riskScreen.x - 130f, riskScreen.y - 12f, 260f, 24f),
                    _settings.RiskGateLabel,
                    _centeredSmallStyle,
                    _settings.RiftDangerColor);
            }
        }

        private void DrawBanner()
        {
            string text = null;
            string subtitle = null;
            float alpha = 0f;
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
                float duration = Mathf.Max(0.01f, _settings.EntryCardDuration * 2f);
                alpha = 1f - Mathf.Clamp01((_bossBannerElapsed - (duration * 0.6f)) / (duration * 0.4f));
                alpha *= 0.55f + (0.45f * Mathf.Abs(Mathf.Sin(_bossBannerElapsed * 6f)));
            }
            if (string.IsNullOrEmpty(text) || alpha <= 0.01f)
            {
                return;
            }
            DrawLabel(
                new Rect(0f, (Screen.height * 0.38f) - 40f, Screen.width, 60f),
                text,
                _resultStyle,
                WithAlpha(_settings.HudPrimaryColor, alpha));
            DrawLabel(
                new Rect(0f, (Screen.height * 0.38f) + 26f, Screen.width, 28f),
                subtitle,
                _centeredSmallStyle,
                WithAlpha(_settings.HudChargeColor, alpha));
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
            float thickness = _settings.ReticleLineThickness;
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
            float edge = Mathf.Max(1f, _settings.ReticleLineThickness);
            Color edgeColor = WithAlpha(_settings.RiftDangerColor, active ? 0.9f : 0.35f + (0.4f * telegraph));
            DrawRect(new Rect(laneScreen.x - width, 0f, edge, Screen.height), edgeColor);
            DrawRect(new Rect(laneScreen.x + width - edge, 0f, edge, Screen.height), edgeColor);
        }

        private void DrawBossMeter()
        {
            float health = Mathf.Clamp01(_boss.Health / Mathf.Max(1f, _boss.MaximumHealth));
            Rect panel = new Rect(
                (Screen.width - _settings.BossMeterWidth) * 0.5f,
                _settings.BossMeterTop,
                _settings.BossMeterWidth,
                58f);
            DrawPanel(panel);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 5f, panel.width - 28f, 24f),
                _settings.BossTitle, _titleStyle);
            DrawMeter(new Rect(panel.x + 14f, panel.y + 36f, panel.width - 28f, _settings.HudMeterHeight),
                health, _settings.HudDamageColor);
        }

        private void DrawResults()
        {
            float fade = Mathf.Clamp01(
                _phaseElapsed / Mathf.Max(0.01f, _settings.ResultsFadeDuration));
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.82f * fade));

            float line = _settings.HudLineHeight;
            float pad = _settings.HudPanelPadding;
            (string Label, string Value, Color Color)[] rows =
            {
                ("SCORE", _state.Score.ToString("0000000"), _settings.HudPrimaryColor),
                ("HOSTILES DOWNED", _state.Kills.ToString("000"), _settings.HudPrimaryColor),
                ("FORMATION CLEARS", _state.FormationClears.ToString("00"), _settings.HudPrimaryColor),
                ("CHARGED KILLS", _state.ChargeKills.ToString("00"), _settings.HudPrimaryColor),
                ("PROJECTILES DEFLECTED", _state.ProjectileDeflections.ToString("00"), _settings.HudPrimaryColor),
                ("PICKUPS RECOVERED", _state.Pickups.ToString("00"), _settings.HudPrimaryColor),
                ("ROUTE GATES", $"{_state.RouteGatesCleared:00} / {_settings.BranchGateCount:00}", _settings.HudPrimaryColor),
                ("RIFT DEPTH", $"{Mathf.RoundToInt(_state.Distance)} M", _settings.HudSecondaryColor),
                ("FLAWLESS RUN", _state.TookDamage ? "NO" : "YES",
                    _state.TookDamage ? _settings.HudSecondaryColor : _settings.HudReticleColor),
                ("COMBAT PAYOUT", $"+{AwardedGold} GOLD", _settings.RiftGoldColor),
            };

            float width = Mathf.Min(760f, Screen.width - (_settings.HudMargin * 2f));
            float height = (pad * 2f) + 76f + 56f + (rows.Length * line) + line + 16f;
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
            DrawPanel(panel);
            float cursor = panel.y + pad;
            DrawLabel(
                new Rect(panel.x + pad, cursor, panel.width - (pad * 2f), 70f),
                _resultSuccess ? "RIFT INTERCEPT CLEARED" : "EMERGENCY EXTRACTION",
                _resultStyle,
                WithAlpha(_resultSuccess ? _settings.HudPrimaryColor : _settings.HudDamageColor, fade));
            cursor += 76f;
            DrawLabel(
                new Rect(panel.x + pad, cursor, panel.width - (pad * 2f), 50f),
                $"COMBAT RATING  {ResultGrade}",
                _resultStyle,
                WithAlpha(_settings.HudChargeColor, fade));
            cursor += 56f;

            float columnX = panel.x + (pad * 3f);
            float columnWidth = panel.width - (pad * 6f);
            for (int i = 0; i < rows.Length; i++)
            {
                Rect row = new Rect(columnX, cursor, columnWidth, line);
                DrawLabel(row, rows[i].Label, _statLabelStyle, WithAlpha(_settings.HudSecondaryColor, fade));
                DrawLabel(row, rows[i].Value, _statValueStyle, WithAlpha(rows[i].Color, fade));
                cursor += line;
            }

            if (_phaseElapsed >= _settings.ResultsSkipDelay)
            {
                float pulse = 0.5f + (0.5f * Mathf.Abs(Mathf.Sin(_phaseElapsed * 3.2f)));
                DrawLabel(
                    new Rect(panel.x + pad, cursor + 8f, panel.width - (pad * 2f), line),
                    _settings.ResultsSkipLabel,
                    _centeredSmallStyle,
                    WithAlpha(_settings.HudSecondaryColor, pulse * fade));
            }
        }

        private void DrawPanel(Rect rect)
        {
            DrawRect(rect, _settings.HudPanelColor);
            float border = Mathf.Max(1f, _settings.ReticleLineThickness);
            DrawRect(new Rect(rect.x, rect.y, rect.width, border), _settings.HudBorderColor);
            DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), _settings.HudBorderColor);
            DrawRect(new Rect(rect.x, rect.y, border, rect.height), _settings.HudBorderColor);
            DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), _settings.HudBorderColor);
        }

        private void DrawMeter(Rect rect, float normalized, Color fill)
        {
            DrawRect(rect, new Color(0f, 0f, 0f, 0.72f));
            Rect inset = new Rect(rect.x + 2f, rect.y + 2f, Mathf.Max(0f, rect.width - 4f), Mathf.Max(0f, rect.height - 4f));
            inset.width *= Mathf.Clamp01(normalized);
            DrawRect(inset, fill);
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
            IsAnyRailShooterActive = false;
        }
    }
}
