using System;
using System.Collections.Generic;
using System.IO;
using KinematicCharacterController;
using UnityEngine;

namespace DuneVector
{
    public enum DroneTraversalMode
    {
        Normal,
        Flight,
    }

    public struct DroneControlInput
    {
        public Vector2 Move;
        public Quaternion CameraRotation;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool StopWhenFlightBraking;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(KinematicCharacterMotor))]
    public sealed class DroneCharacterController : MonoBehaviour, ICharacterController
    {
        private const string FlightMeterSaveFileName = "DuneVectorFlightMeter.dat";
        private const float FlightMeterAutosaveIntervalSeconds = 1f;

        [Serializable]
        private sealed class FlightMeterSaveData
        {
            public int Version = 1;
            public float RemainingSeconds;
        }

        [Header("KCC")]
        public KinematicCharacterMotor Motor;
        public Transform DroneVisualRoot;
        public Transform CameraTarget;

        [Header("Drone Ground Movement")]
        [Min(0f)] public float MaxGroundSpeed = 18f;
        [Min(0f)] public float GroundMovementSharpness = 8.5f;
        [Min(0f)] public float GroundBrakingSharpness = 5.5f;
        [Min(0f)] public float RotationSharpness = 11f;
        [Min(0f)] public float AirAcceleration = 17f;
        [Min(0f)] public float MaxAirSpeed = 22f;
        [Min(0f)] public float AirDrag = 0.08f;
        public Vector3 Gravity = new Vector3(0f, -28f, 0f);

        [Header("Jump")]
        [Min(0f)] public float JumpSpeed = 13f;
        [Min(0f)] public float JumpBufferTime = 0.16f;
        [Min(0f)] public float CoyoteTime = 0.12f;

        [Header("Boost Rings")]
        [Min(0f)] public float RingBoostAcceleration = 9.5f;
        [Min(0f)] public float RingBoostDuration = 2.6f;
        [Min(0f)] public float RingBoostMaxSpeed = 39f;

        [Header("Ring Entry Burst")]
        [Min(1f)] public float RingBurstSpeedMultiplier = 1.45f;
        [Min(0.05f)] public float RingBurstDuration = 0.7f;
        [Min(0f)] public float RingBurstAcceleration = 28f;

        [Header("Flight")]
        [Min(0f)] public float FlightSpeed = 27f;
        [Min(0f)] public float MaximumFlightSpeed = 38f;
        [Min(0f)] public float FlightAcceleration = 3.8f;
        [Min(0f)] public float FlightBrakeSpeed = 12f;
        [Min(0f)] public float FlightBrakeSharpness = 9f;
        [Min(0f)] public float FlightSteeringSharpness = 10f;
        [Tooltip("How quickly flight removes roll inherited from grounded traversal and returns the drone's up-axis toward world-up.")]
        [Min(0f)] public float FlightLevelingSharpness = 5f;
        [Min(0f)] public float FlightYawRate = 125f;
        [Min(0.1f)] public float FlightDuration = 60f;
        [Tooltip("Seconds restored to the flight meter when the drone passes through a flight ring.")]
        [Min(0f)] public float FlightRingRechargeSeconds = 7f;
        [Tooltip("How long a newly started flight receives a protective upward lift. Flight-ring refreshes do not restart it.")]
        [Min(0f)] public float FlightEntryLiftDuration = 0.75f;
        [Tooltip("Minimum upward speed at the beginning of a newly started flight.")]
        [Min(0f)] public float FlightEntryLiftSpeed = 16f;

        [Header("Landing")]
        [Min(0f)] public float MinimumFlightTime = 1.6f;
        [Min(0f)] public float AcceptableLandingDistance = 1.25f;
        public float MaximumLandingVerticalSpeed = 2f;
        [Range(0f, 89f)] public float MaximumLandingAngle = 48f;

        [Header("Visual Banking")]
        [Range(0f, 80f)] public float MaximumBankAngle = 34f;
        [Range(0.1f, 1f)] public float BankYawRateFractionForMaximum = 0.4f;
        [Range(5f, 90f)] public float BankYawErrorForMaximum = 34f;
        [Min(0f)] public float BankSharpness = 8f;
        [Min(0f)] public float BankRecoverySharpness = 5f;
        [Range(0f, 15f)] public float GroundVisualPitch = 4f;
        [Range(0f, 15f)] public float GroundTurnLean = 5f;
        [Min(0f)] public float HoverAmplitude = 0.055f;
        [Min(0f)] public float HoverFrequency = 2.2f;
        [Min(0f)] public float TrailMinimumSpeed = 0.35f;

        [Header("Collision Filtering")]
        public List<Collider> IgnoredColliders = new List<Collider>();

        public DroneTraversalMode CurrentMode { get; private set; } = DroneTraversalMode.Normal;
        public bool IsBoosting => _stamina != null && _stamina.IsBoosting;
        public float StaminaBoostBlend => _boostSpeedModifier != null ? _boostSpeedModifier.BoostBlend : 0f;
        public bool IsRingBoosting => _ringBoostTimeRemaining > 0f;
        public float RingBoostRemainingNormalized => RingBoostDuration > 0f
            ? Mathf.Clamp01(_ringBoostTimeRemaining / RingBoostDuration)
            : 0f;
        public bool IsStableGrounded => Motor != null && Motor.GroundingStatus.IsStableOnGround;
        public float Speed => Motor != null ? Motor.Velocity.magnitude : 0f;
        public Vector3 WorldCenter => Motor != null
            ? Motor.TransientPosition + (Motor.CharacterUp * (Motor.Capsule.height * 0.5f))
            : transform.position;
        public Quaternion AimRotation => DroneVisualRoot != null
            ? DroneVisualRoot.rotation
            : transform.rotation;
        public Vector3 AimDirection => AimRotation * Vector3.forward;
        public float FlightElapsedTime => _flightElapsedTime;
        public float FlightTimeRemaining { get; private set; }
        public float FlightTimeNormalized => FlightDuration > 0f ? Mathf.Clamp01(FlightTimeRemaining / FlightDuration) : 0f;
        public bool DebugInfiniteFlight { get; private set; }
        public float FlightSpeedMultiplier => _flightSpeedMultiplier;
        public float CurrentMaximumFlightSpeed => MaximumFlightSpeed * _flightSpeedMultiplier * CargoSpeedMultiplier;
        public float CurrentSpeedometerMaximum
        {
            get
            {
                float maximumSpeed;
                if (CurrentMode == DroneTraversalMode.Flight)
                {
                    maximumSpeed = CurrentMaximumFlightSpeed * GetRingBurstMultiplier();
                }
                else if (IsRingBoosting)
                {
                    maximumSpeed = RingBoostMaxSpeed * CargoSpeedMultiplier * GetRingBurstMultiplier();
                }
                else
                {
                    bool isGrounded = Motor != null && Motor.GroundingStatus.IsStableOnGround;
                    maximumSpeed = (isGrounded ? MaxGroundSpeed : MaxAirSpeed) * CargoSpeedMultiplier;
                }

                return _boostSpeedModifier != null
                    ? _boostSpeedModifier.GetMaximumModifiedSpeed(maximumSpeed)
                    : maximumSpeed;
            }
        }
        public float CargoSpeedMultiplier { get; private set; } = 1f;
        public float CargoAccelerationMultiplier { get; private set; } = 1f;
        public float CargoTurningMultiplier { get; private set; } = 1f;
        public DesertWorldStreamer World { get; private set; }
        public Vector3 CurrentWindForce { get; private set; }
        public float CurrentWindInfluence { get; private set; }
        public WindFieldType CurrentWindType { get; private set; }
        public Vector3 CurrentDustDevilForce { get; private set; }
        public float CurrentDustDevilInfluence { get; private set; }
        public float CurrentDustDevilCoreInfluence { get; private set; }
        public bool IsFlightSuspendedByDustDevil => CurrentMode == DroneTraversalMode.Flight
            && CurrentDustDevilInfluence > 0f;

        private Vector2 _rawMove;
        private bool _flightBrakeHeld;
        private bool _stopWhenFlightBraking;
        private Vector3 _moveInputWorld;
        private Vector3 _cameraForward;
        private Vector3 _cameraRight;
        private Vector3 _lookInputWorld;
        private bool _jumpRequested;
        private bool _jumpConsumed;
        private bool _hoverEnabled = true;
        private bool _jumpedThisUpdate;
        private float _timeSinceJumpRequested = float.PositiveInfinity;
        private float _timeSinceStableGround = float.PositiveInfinity;
        private GameObject _flightStartEffectPrefab;
        private Quaternion _flightStartEffectRotationOffset = Quaternion.identity;
        private float _flightStartEffectGroundOffset;
        private Vector3 _flightStartEffectScale = Vector3.one;
        private float _flightStartEffectLifetime;
        private GameObject _hubReturnEffectPrefab;
        private Vector3 _hubReturnEffectFloorOffset;
        private Quaternion _hubReturnEffectRotationOffset = Quaternion.identity;
        private Vector3 _hubReturnEffectScale = Vector3.one;
        private float _hubReturnEffectLifetime;

        private float _ringBoostTimeRemaining;
        private float _ringBurstTimeRemaining;
        private DroneStaminaSystem _stamina;
        private DroneBoostSpeedModifier _boostSpeedModifier;
        private DuneVectorWindFieldSystem _windFields;
        private WindFieldSystemTuning _windFieldSettings;
        private DuneVectorDustDevilSystem _dustDevils;
        private DustDevilTuning _dustDevilSettings;
        private DustDevilSample _currentDustDevilSample;
        private int _activeDustDevilId = int.MinValue;
        private bool _entryLaunchApplied;
        private bool _launchedByActiveDustDevil;

        private bool _flightRequested;
        private bool _flightJustEntered;
        private bool _flightBurstRequested;
        private Vector3 _requestedFlightDirection;
        private float _requestedFlightSpeedMultiplier = 1f;
        private Vector3 _flightDirection;
        private float _flightSpeedMultiplier = 1f;
        private float _flightElapsedTime;
        private float _flightEntryLiftTimeRemaining;
        private bool _flightMeterInitialized;
        private bool _flightMeterSaveDirty;
        private float _flightMeterAutosaveTimeRemaining;
        private string _flightMeterSavePath;

        private Vector3 _visualBaseLocalPosition;
        private Vector3 _lastVisualForward;
        private float _currentVisualBank;
        private float _currentVisualPitch;
        private float _flightLandingVisualBlendStartClearance;
        private float _flightLandingVisualBlendCompleteClearance;
        private bool _flightLandingVisualActive;
        private bool _flightLandingSurfaceValid;
        private float _flightLandingSurfaceHeight;
        private int _flightLandingSurfaceUpdatedFrame = -1;
        private float _flightLandingActualBlendStartClearance;
        private float _flightLandingStartBank;
        private float _flightLandingStartPitch;
        private TrailRenderer[] _trailRenderers;
        private bool _trailsVisible = true;

        private void Awake()
        {
            if (Motor == null)
            {
                Motor = GetComponent<KinematicCharacterMotor>();
            }

            Motor.CharacterController = this;
            _lastVisualForward = transform.forward;
        }

        private void OnEnable()
        {
            if (Motor != null)
            {
                Motor.CharacterController = this;
            }
        }

        public void ConfigurePresentation(Transform visualRoot, Transform cameraTarget, DesertWorldStreamer world)
        {
            DroneVisualRoot = visualRoot;
            CameraTarget = cameraTarget;
            World = world;
            if (DroneVisualRoot != null)
            {
                _visualBaseLocalPosition = DroneVisualRoot.localPosition;
                _trailRenderers = DroneVisualRoot.GetComponentsInChildren<TrailRenderer>(true);
            }
        }

        public void BindStaminaBoost(DroneStaminaSystem stamina, DroneBoostSpeedModifier boostSpeedModifier)
        {
            _stamina = stamina;
            _boostSpeedModifier = boostSpeedModifier;
        }

        public void ConfigureFlightStartEffect(
            GameObject prefab,
            Vector3 eulerAngles,
            float groundOffset,
            Vector3 scale,
            float lifetime)
        {
            _flightStartEffectPrefab = prefab;
            _flightStartEffectRotationOffset = Quaternion.Euler(eulerAngles);
            _flightStartEffectGroundOffset = groundOffset;
            _flightStartEffectScale = scale;
            _flightStartEffectLifetime = Mathf.Max(0f, lifetime);
        }

        public void ConfigureHubReturnEffect(
            GameObject prefab,
            Vector3 floorOffset,
            Vector3 eulerOffset,
            Vector3 scale,
            float lifetime)
        {
            _hubReturnEffectPrefab = prefab;
            _hubReturnEffectFloorOffset = floorOffset;
            _hubReturnEffectRotationOffset = Quaternion.Euler(eulerOffset);
            _hubReturnEffectScale = scale;
            _hubReturnEffectLifetime = Mathf.Max(0f, lifetime);
        }

        public void PlayHubReturnEffect(Vector3 hubFloorPosition)
        {
            if (_hubReturnEffectPrefab == null)
            {
                return;
            }

            Quaternion effectRotation =
                _hubReturnEffectPrefab.transform.localRotation * _hubReturnEffectRotationOffset;
            GameObject effect = Instantiate(
                _hubReturnEffectPrefab,
                hubFloorPosition + _hubReturnEffectFloorOffset,
                effectRotation);
            effect.transform.localScale = Vector3.Scale(
                _hubReturnEffectPrefab.transform.localScale,
                _hubReturnEffectScale);
            ParticleSystem[] particleSystems = effect.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particleSystem in particleSystems)
            {
                particleSystem.Clear(withChildren: true);
                particleSystem.Play(withChildren: true);
            }
            if (_hubReturnEffectLifetime > 0f)
            {
                Destroy(effect, _hubReturnEffectLifetime);
            }
        }

        public void RestoreStaminaToFull()
        {
            _stamina?.RestoreToFull();
        }

        public void BindWindFields(DuneVectorWindFieldSystem windFields, WindFieldSystemTuning settings)
        {
            _windFields = windFields;
            _windFieldSettings = settings;
        }

        public void BindDustDevils(DuneVectorDustDevilSystem dustDevils, DustDevilTuning settings)
        {
            _dustDevils = dustDevils;
            _dustDevilSettings = settings;
        }

        public void SetCargoHandlingModifiers(float speedMultiplier, float accelerationMultiplier, float turningMultiplier)
        {
            CargoSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
            CargoAccelerationMultiplier = Mathf.Clamp(accelerationMultiplier, 0.1f, 1f);
            CargoTurningMultiplier = Mathf.Clamp(turningMultiplier, 0.1f, 1f);
        }

        public void ResetTraversalAfterTeleport(Vector3 forward)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (planarForward.sqrMagnitude < 0.001f)
            {
                planarForward = Vector3.forward;
            }
            planarForward.Normalize();
            CurrentMode = DroneTraversalMode.Normal;
            Motor.SetGroundSolvingActivation(true);
            Motor.BaseVelocity = Vector3.zero;
            Motor.SetRotation(Quaternion.LookRotation(planarForward, Vector3.up));
            _lookInputWorld = planarForward;
            _flightDirection = planarForward;
            _flightRequested = false;
            _flightBurstRequested = false;
            _flightElapsedTime = 0f;
            _flightSpeedMultiplier = 1f;
            _requestedFlightSpeedMultiplier = 1f;
            _flightEntryLiftTimeRemaining = 0f;
            _flightLandingVisualActive = false;
            _flightLandingSurfaceValid = false;
            _flightLandingSurfaceUpdatedFrame = -1;
            _ringBoostTimeRemaining = 0f;
            _ringBurstTimeRemaining = 0f;
            _jumpRequested = false;
        }

        public void HandleWorldShift(Vector3 shift)
        {
            if (_trailRenderers == null)
            {
                return;
            }

            // Trail vertices remain in absolute world space when the KCC is
            // teleported for a floating-origin rebase. Clear the old segment so
            // it cannot draw a line across the entire shift distance.
            for (int i = 0; i < _trailRenderers.Length; i++)
            {
                if (_trailRenderers[i] != null)
                {
                    _trailRenderers[i].Clear();
                }
            }
        }

        public void SetInputs(in DroneControlInput inputs)
        {
            _rawMove = Vector2.ClampMagnitude(inputs.Move, 1f);
            _flightBrakeHeld = inputs.JumpHeld;
            _stopWhenFlightBraking = inputs.StopWhenFlightBraking;

            Vector3 planarForward = Vector3.ProjectOnPlane(inputs.CameraRotation * Vector3.forward, Motor.CharacterUp);
            if (planarForward.sqrMagnitude < 0.0001f)
            {
                planarForward = Vector3.ProjectOnPlane(inputs.CameraRotation * Vector3.up, Motor.CharacterUp);
            }
            planarForward.Normalize();
            Quaternion planarCameraRotation = Quaternion.LookRotation(planarForward, Motor.CharacterUp);
            _moveInputWorld = planarCameraRotation * new Vector3(_rawMove.x, 0f, _rawMove.y);
            _cameraForward = (inputs.CameraRotation * Vector3.forward).normalized;
            _cameraRight = (inputs.CameraRotation * Vector3.right).normalized;

            if (_moveInputWorld.sqrMagnitude > 0.0001f)
            {
                _lookInputWorld = _moveInputWorld.normalized;
            }

            if (inputs.JumpPressed && CurrentMode == DroneTraversalMode.Normal)
            {
                bool canGroundJump = !_jumpConsumed
                    && (Motor.GroundingStatus.IsStableOnGround || _timeSinceStableGround <= CoyoteTime);
                if (canGroundJump)
                {
                    _jumpRequested = true;
                    _timeSinceJumpRequested = 0f;
                }
                else if (!Motor.GroundingStatus.IsStableOnGround)
                {
                    RequestFlight(_cameraForward);
                }
            }
        }

        private void OnDisable()
        {
            SaveFlightMeter();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveFlightMeter();
            }
        }

        public void ConfigureFlightMeter(float maximumSeconds, float ringRechargeSeconds, bool debugInfiniteFlight)
        {
            FlightDuration = Mathf.Max(0.1f, maximumSeconds);
            FlightRingRechargeSeconds = Mathf.Max(0f, ringRechargeSeconds);
            DebugInfiniteFlight = debugInfiniteFlight;
            if (!_flightMeterInitialized)
            {
                _flightMeterInitialized = true;
                _flightMeterSavePath = Path.Combine(Application.persistentDataPath, FlightMeterSaveFileName);
                LoadFlightMeter();
            }
            else
            {
                FlightTimeRemaining = Mathf.Min(FlightTimeRemaining, FlightDuration);
            }
        }

        public void ConfigureFlightLandingVisual(
            float blendStartClearance,
            float blendCompleteClearance)
        {
            _flightLandingVisualBlendStartClearance = Mathf.Max(0f, blendStartClearance);
            _flightLandingVisualBlendCompleteClearance = Mathf.Clamp(
                blendCompleteClearance,
                0f,
                _flightLandingVisualBlendStartClearance);
        }

        public void ActivateBoost()
        {
            _ringBoostTimeRemaining = RingBoostDuration;
            StartRingBurst();
        }

        public void RequestFlight(Vector3 launchDirection, float speedMultiplier = 1f)
        {
            if (FlightTimeRemaining <= 0f)
            {
                return;
            }

            float requestedMultiplier = Mathf.Max(1f, speedMultiplier);
            if (CurrentMode == DroneTraversalMode.Flight)
            {
                bool returningToStandardSpeed = requestedMultiplier < _flightSpeedMultiplier;
                _flightSpeedMultiplier = requestedMultiplier;
                if (returningToStandardSpeed)
                {
                    _ringBurstTimeRemaining = 0f;
                }
                else
                {
                    StartRingBurst();
                }
                return;
            }

            _requestedFlightDirection = launchDirection.sqrMagnitude > 0.001f
                ? launchDirection.normalized
                : Motor.CharacterForward;
            _requestedFlightSpeedMultiplier = requestedMultiplier;
            _flightBurstRequested = true;
            _flightRequested = true;
        }

        public void RequestFlightFromRing(Vector3 launchDirection, float speedMultiplier = 1f)
        {
            FlightTimeRemaining = Mathf.Min(
                FlightDuration,
                FlightTimeRemaining + FlightRingRechargeSeconds);
            MarkFlightMeterDirty();
            SaveFlightMeter();
            RequestFlight(launchDirection, speedMultiplier);
        }

        public void BeforeCharacterUpdate(float deltaTime)
        {
            RefreshDustDevilSample();

            if (_flightRequested)
            {
                _flightRequested = false;
                CurrentMode = DroneTraversalMode.Flight;
                _flightElapsedTime = 0f;
                if (_flightBurstRequested)
                {
                    StartRingBurst();
                    _flightBurstRequested = false;
                }
                _flightDirection = _requestedFlightDirection.normalized;
                _flightSpeedMultiplier = _requestedFlightSpeedMultiplier;
                _flightJustEntered = true;
                PlayFlightStartEffect();
                _flightEntryLiftTimeRemaining = FlightEntryLiftDuration;
                _flightLandingVisualActive = false;
                _flightLandingSurfaceValid = false;
                _flightLandingSurfaceUpdatedFrame = -1;
                _jumpRequested = false;
                _jumpConsumed = true;
                Motor.ForceUnground(0.2f);
                Motor.SetGroundSolvingActivation(false);
            }
        }

        public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
        {
            if (_dustDevils != null
                && _dustDevilSettings != null
                && _dustDevils.IsControlDisruptionActive)
            {
                float disruptionSpinDegrees = _dustDevilSettings.DroneSpinDegreesPerSecond
                    * _dustDevils.ControlDisruptionSpinSign
                    * _dustDevils.ControlDisruptionSpinMultiplier
                    * Mathf.Max(0f, deltaTime);
                currentRotation = Quaternion.AngleAxis(
                    disruptionSpinDegrees,
                    Vector3.up) * currentRotation;
                return;
            }

            if (IsFlightSuspendedByDustDevil)
            {
                float spinDegrees = _dustDevilSettings.DroneSpinDegreesPerSecond
                    * CurrentDustDevilInfluence
                    * _currentDustDevilSample.SpinSign
                    * Mathf.Max(0f, deltaTime);
                currentRotation = Quaternion.AngleAxis(spinDegrees, Vector3.up) * currentRotation;
                return;
            }

            if (CurrentMode == DroneTraversalMode.Flight)
            {
                Vector3 desiredForward = _flightDirection.sqrMagnitude > 0.001f ? _flightDirection : Motor.CharacterForward;
                float maximumRadians = Mathf.Deg2Rad * FlightYawRate * CargoTurningMultiplier * deltaTime;
                Vector3 rateLimited = Vector3.RotateTowards(Motor.CharacterForward, desiredForward, maximumRadians, 0f);
                Vector3 smoothedForward = Vector3.Slerp(
                    Motor.CharacterForward,
                    rateLimited,
                    DuneVectorMath.Sharpness(FlightSteeringSharpness * CargoTurningMultiplier, deltaTime)).normalized;
                Quaternion steeredRotation = Quaternion.FromToRotation(Motor.CharacterForward, smoothedForward) * currentRotation;
                Quaternion leveledRotation = Quaternion.LookRotation(smoothedForward, Vector3.up);
                currentRotation = Quaternion.Slerp(
                    steeredRotation,
                    leveledRotation,
                    DuneVectorMath.Sharpness(FlightLevelingSharpness, deltaTime));
                return;
            }

            Vector3 desiredGroundForward = _lookInputWorld.sqrMagnitude > 0.001f
                ? _lookInputWorld
                : Vector3.ProjectOnPlane(currentRotation * Vector3.forward, Motor.CharacterUp);
            if (desiredGroundForward.sqrMagnitude > 0.001f)
            {
                Vector3 smoothedLook = Vector3.Slerp(
                    Motor.CharacterForward,
                    desiredGroundForward.normalized,
                    DuneVectorMath.Sharpness(RotationSharpness * CargoTurningMultiplier, deltaTime)).normalized;
                currentRotation = Quaternion.LookRotation(smoothedLook, Motor.CharacterUp);
            }

            Vector3 currentUp = currentRotation * Vector3.up;
            float orientationBlend = DuneVectorMath.Sharpness(RotationSharpness, deltaTime);
            if (Motor.GroundingStatus.IsStableOnGround)
            {
                Vector3 initialBottomHemiCenter =
                    Motor.TransientPosition + (currentUp * Motor.Capsule.radius);
                Vector3 smoothedGroundNormal = Vector3.Slerp(
                    Motor.CharacterUp,
                    Motor.GroundingStatus.GroundNormal,
                    orientationBlend);

                currentRotation =
                    Quaternion.FromToRotation(currentUp, smoothedGroundNormal) * currentRotation;

                // Preserve the capsule's contact height without applying the horizontal
                // correction that would make ground alignment walk the drone across slopes.
                Vector3 rotatedBottomHemiCenter =
                    Motor.TransientPosition + (currentRotation * Vector3.up * Motor.Capsule.radius);
                Vector3 gravityUp = Gravity.sqrMagnitude > 0.001f
                    ? -Gravity.normalized
                    : Vector3.up;
                Vector3 contactHeightCorrection = Vector3.Project(
                    initialBottomHemiCenter - rotatedBottomHemiCenter,
                    gravityUp);
                Motor.SetTransientPosition(Motor.TransientPosition + contactHeightCorrection);
            }
            else if (Gravity.sqrMagnitude > 0.001f)
            {
                Vector3 smoothedGravityUp = Vector3.Slerp(
                    currentUp,
                    -Gravity.normalized,
                    orientationBlend);
                currentRotation =
                    Quaternion.FromToRotation(currentUp, smoothedGravityUp) * currentRotation;
            }
        }

        public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            _boostSpeedModifier?.Tick(IsBoosting, deltaTime);
            if (CurrentMode == DroneTraversalMode.Flight)
            {
                if (!IsFlightSuspendedByDustDevil || _flightBrakeHeld)
                {
                    UpdateFlightVelocity(ref currentVelocity, deltaTime);
                }
            }
            else
            {
                UpdateNormalVelocity(ref currentVelocity, deltaTime);
            }
            ApplyWindFieldForce(ref currentVelocity, deltaTime);
            ApplyDustDevilForce(ref currentVelocity, deltaTime);
        }

        private void ApplyDustDevilForce(ref Vector3 currentVelocity, float deltaTime)
        {
            if (_dustDevils == null || _dustDevilSettings == null)
            {
                _activeDustDevilId = int.MinValue;
                _entryLaunchApplied = false;
                _launchedByActiveDustDevil = false;
                return;
            }

            DustDevilSample sample = _currentDustDevilSample;
            if (sample.Influence <= 0f)
            {
                _activeDustDevilId = int.MinValue;
                _entryLaunchApplied = false;
                _launchedByActiveDustDevil = false;
                return;
            }

            if (_activeDustDevilId != sample.SourceId)
            {
                _activeDustDevilId = sample.SourceId;
                _entryLaunchApplied = false;
                _launchedByActiveDustDevil = false;
            }

            if (!_entryLaunchApplied
                && sample.Influence >= _dustDevilSettings.GroundLaunchInfluenceThreshold)
            {
                _entryLaunchApplied = true;
                Motor.ForceUnground();
                currentVelocity.y = Mathf.Max(
                    currentVelocity.y,
                    _dustDevilSettings.MinimumEntryLaunchSpeed);
            }

            Vector3 planarVelocity = Vector3.ProjectOnPlane(currentVelocity, Vector3.up);
            if (planarVelocity.sqrMagnitude > 0.001f)
            {
                float spinDegrees = _dustDevilSettings.TrajectorySpinDegreesPerSecond
                    * sample.Influence
                    * sample.SpinSign
                    * Mathf.Max(0f, deltaTime);
                Vector3 spunPlanarVelocity = Quaternion.AngleAxis(spinDegrees, Vector3.up) * planarVelocity;
                currentVelocity += spunPlanarVelocity - planarVelocity;
            }

            currentVelocity += sample.Acceleration * Mathf.Max(0f, deltaTime);
            currentVelocity.y = Mathf.Min(currentVelocity.y, _dustDevilSettings.MaximumUpwardSpeed);

            if (_launchedByActiveDustDevil
                || sample.CoreInfluence < _dustDevilSettings.CoreLaunchInfluenceThreshold)
            {
                return;
            }

            _launchedByActiveDustDevil = true;
            Motor.ForceUnground();
            currentVelocity.y = Mathf.Max(currentVelocity.y, _dustDevilSettings.CoreMinimumLaunchSpeed);
            Vector3 launchPlanar = Vector3.ProjectOnPlane(currentVelocity, Vector3.up);
            if (launchPlanar.sqrMagnitude < 0.001f)
            {
                launchPlanar = Vector3.ProjectOnPlane(Motor.CharacterForward, Vector3.up);
            }
            Vector3 launchDirection = (Vector3.up * _dustDevilSettings.LaunchUpwardWeight)
                + (launchPlanar.normalized * _dustDevilSettings.LaunchForwardWeight)
                + (sample.Tangent * _dustDevilSettings.LaunchTangentialWeight);
            if (launchDirection.sqrMagnitude < 0.001f)
            {
                launchDirection = Vector3.up;
            }
            RequestFlight(launchDirection, _dustDevilSettings.LaunchFlightSpeedMultiplier);
        }

        private void RefreshDustDevilSample()
        {
            _currentDustDevilSample = _dustDevils != null && _dustDevilSettings != null
                ? _dustDevils.Sample(WorldCenter)
                : default;
            CurrentDustDevilForce = _currentDustDevilSample.Acceleration;
            CurrentDustDevilInfluence = _currentDustDevilSample.Influence;
            CurrentDustDevilCoreInfluence = _currentDustDevilSample.CoreInfluence;
        }

        private void ApplyWindFieldForce(ref Vector3 currentVelocity, float deltaTime)
        {
            if (_windFields == null || _windFieldSettings == null)
            {
                CurrentWindForce = Vector3.zero;
                CurrentWindInfluence = 0f;
                return;
            }

            WindFieldSample sample = _windFields.Sample(WorldCenter, Time.time);
            CurrentWindInfluence = sample.Influence;
            CurrentWindType = sample.DominantType;
            float traversalMultiplier = CurrentMode == DroneTraversalMode.Flight
                ? _windFieldSettings.FlightForceMultiplier
                : Motor.GroundingStatus.IsStableOnGround
                    ? _windFieldSettings.GroundedForceMultiplier
                    : _windFieldSettings.FlightForceMultiplier;
            float forceResponse = sample.DominantType == WindFieldType.Updraft
                ? _windFieldSettings.UpdraftPlayerForceResponse
                : _windFieldSettings.PlayerForceResponse;
            CurrentWindForce = sample.Force * forceResponse * traversalMultiplier;
            if (sample.DominantType == WindFieldType.Updraft
                && sample.Influence >= _windFieldSettings.UpdraftLaunchInfluenceThreshold
                && Motor.GroundingStatus.IsStableOnGround)
            {
                Motor.ForceUnground();
                currentVelocity.y = Mathf.Max(currentVelocity.y, _windFieldSettings.UpdraftMinimumLaunchSpeed);
            }
            currentVelocity += CurrentWindForce * Mathf.Max(0f, deltaTime);
            if (sample.DominantType == WindFieldType.Updraft)
            {
                currentVelocity.y = Mathf.Min(
                    currentVelocity.y,
                    Mathf.Max(0f, _windFieldSettings.UpdraftMaximumUpwardSpeed));
            }
        }

        private void UpdateNormalVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            if (Motor.GroundingStatus.IsStableOnGround)
            {
                float currentMagnitude = currentVelocity.magnitude;
                Vector3 groundNormal = Motor.GroundingStatus.GroundNormal;
                if (currentMagnitude > 0.001f)
                {
                    currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, groundNormal) * currentMagnitude;
                }

                Vector3 targetDirection = Vector3.zero;
                float targetSpeed = 0f;
                float sharpness = _rawMove.sqrMagnitude > 0.001f ? GroundMovementSharpness : GroundBrakingSharpness;

                if (_moveInputWorld.sqrMagnitude > 0.001f)
                {
                    Vector3 inputRight = Vector3.Cross(_moveInputWorld, Motor.CharacterUp);
                    targetDirection = Vector3.Cross(groundNormal, inputRight).normalized;
                    targetSpeed = MaxGroundSpeed * CargoSpeedMultiplier * _moveInputWorld.magnitude;
                    sharpness *= CargoAccelerationMultiplier;
                }

                if (IsRingBoosting && targetDirection.sqrMagnitude > 0.001f)
                {
                    float burstMultiplier = GetRingBurstMultiplier();
                    targetSpeed = RingBoostMaxSpeed * CargoSpeedMultiplier * burstMultiplier * _moveInputWorld.magnitude;
                    sharpness = (_ringBurstTimeRemaining > 0f ? RingBurstAcceleration : RingBoostAcceleration) * CargoAccelerationMultiplier;
                }
                if (targetDirection.sqrMagnitude > 0.001f && _boostSpeedModifier != null)
                {
                    targetSpeed = _boostSpeedModifier.ModifyTargetSpeed(targetSpeed);
                    if (_boostSpeedModifier.BoostBlend > 0f)
                    {
                        sharpness = Mathf.Max(sharpness, _boostSpeedModifier.CurrentResponse);
                    }
                }

                Vector3 targetVelocity = targetDirection * targetSpeed;
                currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, DuneVectorMath.Sharpness(sharpness, deltaTime));
            }
            else
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);
                if (_moveInputWorld.sqrMagnitude > 0.001f)
                {
                    Vector3 addedVelocity = _moveInputWorld * (AirAcceleration * CargoAccelerationMultiplier * deltaTime);
                    float maximumAirSpeed = _boostSpeedModifier != null
                        ? _boostSpeedModifier.ModifyTargetSpeed(MaxAirSpeed * CargoSpeedMultiplier)
                        : MaxAirSpeed * CargoSpeedMultiplier;
                    if (planarVelocity.magnitude < maximumAirSpeed)
                    {
                        Vector3 newPlanarVelocity = Vector3.ClampMagnitude(planarVelocity + addedVelocity, maximumAirSpeed);
                        currentVelocity += newPlanarVelocity - planarVelocity;
                    }
                    else if (Vector3.Dot(planarVelocity, addedVelocity) <= 0f)
                    {
                        currentVelocity += addedVelocity;
                    }
                }

                currentVelocity += Gravity * deltaTime;
                currentVelocity *= 1f / (1f + (AirDrag * deltaTime));
            }

            _jumpedThisUpdate = false;
            if (_jumpRequested
                && !_jumpConsumed
                && (Motor.GroundingStatus.IsStableOnGround || _timeSinceStableGround <= CoyoteTime))
            {
                Motor.ForceUnground(0.12f);
                currentVelocity += (Motor.CharacterUp * JumpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
                _jumpRequested = false;
                _jumpConsumed = true;
                _jumpedThisUpdate = true;
            }
        }

        private void PlayFlightStartEffect()
        {
            if (_flightStartEffectPrefab == null || Motor == null)
            {
                return;
            }

            Vector3 effectPosition = Motor.TransientPosition;
            if (Motor.GroundingStatus.IsStableOnGround)
            {
                effectPosition.y = Motor.GroundingStatus.GroundPoint.y;
            }
            else if (World != null)
            {
                effectPosition.y = World.SampleHeightAtLocal(effectPosition.x, effectPosition.z);
            }

            effectPosition.y += _flightStartEffectGroundOffset;
            Quaternion droneRotation = AimRotation;
            Quaternion backFacingRotation = Quaternion.LookRotation(
                droneRotation * Vector3.back,
                droneRotation * Vector3.up);
            Quaternion effectRotation = backFacingRotation * _flightStartEffectRotationOffset;
            GameObject effect = Instantiate(_flightStartEffectPrefab, effectPosition, effectRotation);
            effect.transform.localScale = Vector3.Scale(effect.transform.localScale, _flightStartEffectScale);
            if (_flightStartEffectLifetime > 0f)
            {
                Destroy(effect, _flightStartEffectLifetime);
            }
        }

        private void UpdateFlightVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            float forwardInput = _rawMove.y;
            Vector3 cameraHeading = _cameraForward.sqrMagnitude > 0.001f ? _cameraForward : Motor.CharacterForward;
            Vector3 desiredDirection = cameraHeading;
            desiredDirection += _cameraRight * (_rawMove.x * 0.72f);
            desiredDirection.Normalize();

            if (desiredDirection.sqrMagnitude > 0.001f)
            {
                _flightDirection = Vector3.Slerp(
                    _flightDirection.sqrMagnitude > 0.001f ? _flightDirection : desiredDirection,
                    desiredDirection,
                    DuneVectorMath.Sharpness(FlightSteeringSharpness * CargoTurningMultiplier, deltaTime)).normalized;
            }

            float throttle = Mathf.Clamp01((forwardInput + 1f) * 0.5f);
            float activeFlightSpeed = FlightSpeed * _flightSpeedMultiplier * CargoSpeedMultiplier;
            float activeMaximumFlightSpeed = MaximumFlightSpeed * _flightSpeedMultiplier * CargoSpeedMultiplier;
            float targetSpeed = Mathf.Lerp(activeFlightSpeed * 0.62f, activeMaximumFlightSpeed, throttle);
            if (Mathf.Abs(forwardInput) < 0.05f)
            {
                targetSpeed = activeFlightSpeed;
            }
            if (_flightBrakeHeld)
            {
                targetSpeed = _stopWhenFlightBraking ? 0f : FlightBrakeSpeed;
            }
            else
            {
                targetSpeed *= GetRingBurstMultiplier();
                if (_boostSpeedModifier != null)
                {
                    targetSpeed = _boostSpeedModifier.ModifyTargetSpeed(targetSpeed);
                }
            }

            Vector3 targetVelocity = _flightDirection * targetSpeed;

            if (_flightJustEntered)
            {
                _flightJustEntered = false;
                if (currentVelocity.magnitude < activeFlightSpeed * 0.72f)
                {
                    targetVelocity = Vector3.Slerp(targetVelocity.normalized, _requestedFlightDirection, 0.32f).normalized * activeFlightSpeed;
                }
            }

            if (_flightEntryLiftTimeRemaining > 0f && FlightEntryLiftDuration > 0f)
            {
                float lift01 = Mathf.Clamp01(_flightEntryLiftTimeRemaining / FlightEntryLiftDuration);
                lift01 = lift01 * lift01 * (3f - (2f * lift01));
                float minimumUpwardSpeed = FlightEntryLiftSpeed * lift01;
                targetVelocity.y = Mathf.Max(targetVelocity.y, minimumUpwardSpeed);
                _flightEntryLiftTimeRemaining = Mathf.Max(0f, _flightEntryLiftTimeRemaining - deltaTime);
            }

            float acceleration = _flightBrakeHeld
                ? FlightBrakeSharpness
                : _ringBurstTimeRemaining > 0f
                    ? RingBurstAcceleration
                    : _boostSpeedModifier != null
                        ? Mathf.Max(FlightAcceleration, _boostSpeedModifier.CurrentResponse)
                        : FlightAcceleration;
            currentVelocity = Vector3.Lerp(
                currentVelocity,
                targetVelocity,
                DuneVectorMath.Sharpness(acceleration * CargoAccelerationMultiplier, deltaTime));
        }

        public void PostGroundingUpdate(float deltaTime)
        {
            if (CurrentMode == DroneTraversalMode.Normal)
            {
                if (Motor.GroundingStatus.IsStableOnGround)
                {
                    _timeSinceStableGround = 0f;
                    if (!_jumpedThisUpdate)
                    {
                        _jumpConsumed = false;
                    }
                }
            }
        }

        public void AfterCharacterUpdate(float deltaTime)
        {
            if (_ringBoostTimeRemaining > 0f)
            {
                _ringBoostTimeRemaining = Mathf.Max(0f, _ringBoostTimeRemaining - deltaTime);
            }
            if (_ringBurstTimeRemaining > 0f)
            {
                _ringBurstTimeRemaining = Mathf.Max(0f, _ringBurstTimeRemaining - deltaTime);
            }

            if (CurrentMode == DroneTraversalMode.Normal)
            {
                _timeSinceJumpRequested += deltaTime;
                if (_jumpRequested && _timeSinceJumpRequested > JumpBufferTime)
                {
                    _jumpRequested = false;
                }

                if (!Motor.GroundingStatus.IsStableOnGround)
                {
                    _timeSinceStableGround += deltaTime;
                }
            }
            else
            {
                if (IsFlightSuspendedByDustDevil)
                {
                    return;
                }

                _flightElapsedTime += deltaTime;
                FlightTimeRemaining = DebugInfiniteFlight
                    ? FlightDuration
                    : Mathf.Max(0f, FlightTimeRemaining - deltaTime);
                MarkFlightMeterDirty();
                TickFlightMeterAutosave(deltaTime);
                if (FlightTimeRemaining <= 0f)
                {
                    FinishFlight();
                    return;
                }
                TryFinishFlight();
            }
        }

        private void LoadFlightMeter()
        {
            FlightTimeRemaining = 0f;
            if (!File.Exists(_flightMeterSavePath))
            {
                MarkFlightMeterDirty();
                SaveFlightMeter();
                return;
            }

            try
            {
                FlightMeterSaveData data = JsonUtility.FromJson<FlightMeterSaveData>(
                    File.ReadAllText(_flightMeterSavePath));
                if (data != null)
                {
                    FlightTimeRemaining = Mathf.Clamp(data.RemainingSeconds, 0f, FlightDuration);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not load flight meter from '{_flightMeterSavePath}': {exception.Message}",
                    this);
            }
        }

        private void MarkFlightMeterDirty()
        {
            if (!_flightMeterInitialized)
            {
                return;
            }

            _flightMeterSaveDirty = true;
        }

        private void TickFlightMeterAutosave(float deltaTime)
        {
            if (!_flightMeterSaveDirty)
            {
                return;
            }

            _flightMeterAutosaveTimeRemaining -= Mathf.Max(0f, deltaTime);
            if (_flightMeterAutosaveTimeRemaining <= 0f)
            {
                SaveFlightMeter();
            }
        }

        private void SaveFlightMeter()
        {
            if (!_flightMeterInitialized || !_flightMeterSaveDirty || string.IsNullOrEmpty(_flightMeterSavePath))
            {
                return;
            }

            try
            {
                FlightMeterSaveData data = new FlightMeterSaveData
                {
                    RemainingSeconds = Mathf.Clamp(FlightTimeRemaining, 0f, FlightDuration),
                };
                File.WriteAllText(_flightMeterSavePath, JsonUtility.ToJson(data));
                _flightMeterSaveDirty = false;
                _flightMeterAutosaveTimeRemaining = FlightMeterAutosaveIntervalSeconds;
            }
            catch (Exception exception)
            {
                _flightMeterAutosaveTimeRemaining = FlightMeterAutosaveIntervalSeconds;
                Debug.LogWarning(
                    $"Could not save flight meter to '{_flightMeterSavePath}': {exception.Message}",
                    this);
            }
        }

        private void TryFinishFlight()
        {
            if (World == null)
            {
                return;
            }

            float terrainHeight = World.SampleHeightAtLocal(Motor.TransientPosition.x, Motor.TransientPosition.z);
            Vector3 terrainNormal = World.SampleNormalAtLocal(Motor.TransientPosition.x, Motor.TransientPosition.z);
            TryFinishFlightOnSurface(terrainHeight, terrainNormal);
        }

        public void TryFinishFlightOnSurface(float surfaceHeight, Vector3 surfaceNormal)
        {
            UpdateFlightLandingSurface(surfaceHeight);
            if (CurrentMode != DroneTraversalMode.Flight || _flightElapsedTime < MinimumFlightTime)
            {
                return;
            }

            float clearance = Motor.TransientPosition.y - surfaceHeight;
            float surfaceAngle = Vector3.Angle(surfaceNormal, Vector3.up);
            if (clearance <= AcceptableLandingDistance
                && Motor.BaseVelocity.y <= MaximumLandingVerticalSpeed
                && surfaceAngle <= MaximumLandingAngle)
            {
                FinishFlight();
            }
        }

        private void FinishFlight()
        {
            BeginFlightLandingVisual();
            Vector3 landingForward = Vector3.ProjectOnPlane(Motor.CharacterForward, Vector3.up);
            if (landingForward.sqrMagnitude < 0.001f)
            {
                landingForward = Vector3.ProjectOnPlane(_flightDirection, Vector3.up);
            }
            if (landingForward.sqrMagnitude < 0.001f)
            {
                landingForward = Vector3.forward;
            }

            landingForward.Normalize();
            Motor.SetRotation(Quaternion.LookRotation(landingForward, Vector3.up));
            _lookInputWorld = landingForward;
            CurrentMode = DroneTraversalMode.Normal;
            Motor.SetGroundSolvingActivation(true);
            _flightElapsedTime = 0f;
            _flightSpeedMultiplier = 1f;
            _requestedFlightSpeedMultiplier = 1f;
            _flightEntryLiftTimeRemaining = 0f;
            _timeSinceStableGround = float.PositiveInfinity;
        }

        private void BeginFlightLandingVisual()
        {
            if (_flightLandingVisualActive)
            {
                return;
            }

            _flightLandingVisualActive = true;
            _flightLandingStartBank = _currentVisualBank;
            _flightLandingStartPitch = _currentVisualPitch;
            float currentClearance = _flightLandingSurfaceValid
                ? Motor.TransientPosition.y - _flightLandingSurfaceHeight
                : _flightLandingVisualBlendStartClearance;
            _flightLandingActualBlendStartClearance = Mathf.Clamp(
                currentClearance,
                _flightLandingVisualBlendCompleteClearance,
                _flightLandingVisualBlendStartClearance);
        }

        private void UpdateFlightLandingSurface(float surfaceHeight)
        {
            if (!_flightLandingVisualActive && CurrentMode != DroneTraversalMode.Flight)
            {
                return;
            }

            if (!_flightLandingSurfaceValid || _flightLandingSurfaceUpdatedFrame != Time.frameCount)
            {
                _flightLandingSurfaceHeight = surfaceHeight;
            }
            else
            {
                _flightLandingSurfaceHeight = Mathf.Max(_flightLandingSurfaceHeight, surfaceHeight);
            }
            _flightLandingSurfaceValid = true;
            _flightLandingSurfaceUpdatedFrame = Time.frameCount;
        }

        private void StartRingBurst()
        {
            _ringBurstTimeRemaining = Mathf.Max(0.05f, RingBurstDuration);
        }

        private float GetRingBurstMultiplier()
        {
            if (_ringBurstTimeRemaining <= 0f || RingBurstDuration <= 0f)
            {
                return 1f;
            }

            float burst01 = Mathf.Clamp01(_ringBurstTimeRemaining / RingBurstDuration);
            burst01 = burst01 * burst01 * (3f - (2f * burst01));
            return Mathf.Lerp(1f, Mathf.Max(1f, RingBurstSpeedMultiplier), burst01);
        }

        private void LateUpdate()
        {
            if (DroneVisualRoot == null || Motor == null)
            {
                return;
            }

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            UpdateTrailVisibility();
            Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 previousForward = Vector3.ProjectOnPlane(_lastVisualForward, Vector3.up).normalized;
            float yawRate = 0f;
            if (currentForward.sqrMagnitude > 0.001f && previousForward.sqrMagnitude > 0.001f)
            {
                yawRate = Vector3.SignedAngle(previousForward, currentForward, Vector3.up) / deltaTime;
            }
            _lastVisualForward = transform.forward;

            float targetBank = 0f;
            float targetPitch = 0f;
            if (CurrentMode == DroneTraversalMode.Flight)
            {
                float yawRateForMaximumBank = Mathf.Max(1f, FlightYawRate * BankYawRateFractionForMaximum);
                float actualTurnIntensity = -yawRate / yawRateForMaximumBank;
                Vector3 intendedPlanarForward = Vector3.ProjectOnPlane(_flightDirection, Vector3.up).normalized;
                float intendedYawError = intendedPlanarForward.sqrMagnitude > 0.001f
                    ? Vector3.SignedAngle(currentForward, intendedPlanarForward, Vector3.up)
                    : 0f;
                float intendedTurnIntensity = -intendedYawError / Mathf.Max(1f, BankYawErrorForMaximum);
                float turnIntensity = Mathf.Abs(intendedTurnIntensity) > Mathf.Abs(actualTurnIntensity)
                    ? intendedTurnIntensity
                    : actualTurnIntensity;
                targetBank = Mathf.Clamp(turnIntensity, -1f, 1f) * MaximumBankAngle;
                targetPitch = Mathf.Clamp(-Motor.BaseVelocity.y / Mathf.Max(1f, CurrentMaximumFlightSpeed), -1f, 1f) * 5f;
            }
            else
            {
                targetBank = -_rawMove.x * GroundTurnLean;
                targetPitch = -Mathf.Max(0f, _rawMove.y) * GroundVisualPitch;
            }

            if (_flightLandingVisualActive)
            {
                UpdateFlightLandingVisual();
            }
            else
            {
                float bankSharpness = Mathf.Abs(targetBank) > Mathf.Abs(_currentVisualBank) ? BankSharpness : BankRecoverySharpness;
                _currentVisualBank = Mathf.Lerp(_currentVisualBank, targetBank, DuneVectorMath.Sharpness(bankSharpness, deltaTime));
                _currentVisualPitch = Mathf.Lerp(_currentVisualPitch, targetPitch, DuneVectorMath.Sharpness(BankRecoverySharpness, deltaTime));
            }
            DroneVisualRoot.localRotation = Quaternion.Euler(_currentVisualPitch, 0f, _currentVisualBank);

            float hover = _hoverEnabled
                ? Mathf.Sin(Time.time * HoverFrequency * Mathf.PI * 2f) * HoverAmplitude
                : 0f;
            DroneVisualRoot.localPosition = _visualBaseLocalPosition + (Vector3.up * hover);
        }

        private void UpdateFlightLandingVisual()
        {
            if (World != null && _flightLandingSurfaceUpdatedFrame != Time.frameCount)
            {
                _flightLandingSurfaceHeight = World.SampleHeightAtLocal(
                    Motor.TransientPosition.x,
                    Motor.TransientPosition.z);
                _flightLandingSurfaceValid = true;
                _flightLandingSurfaceUpdatedFrame = Time.frameCount;
            }

            if (!_flightLandingSurfaceValid)
            {
                return;
            }

            float clearance = Motor.TransientPosition.y - _flightLandingSurfaceHeight;
            float blend = _flightLandingActualBlendStartClearance
                <= _flightLandingVisualBlendCompleteClearance
                ? 1f
                : Mathf.InverseLerp(
                    _flightLandingActualBlendStartClearance,
                    _flightLandingVisualBlendCompleteClearance,
                    clearance);
            blend = blend * blend * (3f - (2f * blend));
            _currentVisualBank = Mathf.Lerp(_flightLandingStartBank, 0f, blend);
            _currentVisualPitch = Mathf.Lerp(_flightLandingStartPitch, 0f, blend);

            if (Motor.GroundingStatus.IsStableOnGround)
            {
                _currentVisualBank = 0f;
                _currentVisualPitch = 0f;
                _flightLandingVisualActive = false;
                _flightLandingSurfaceValid = false;
                _flightLandingSurfaceUpdatedFrame = -1;
            }
        }

        public void SetHoverEnabled(bool enabled)
        {
            _hoverEnabled = enabled;
            if (!enabled && DroneVisualRoot != null)
            {
                DroneVisualRoot.localPosition = _visualBaseLocalPosition;
            }
        }

        private void UpdateTrailVisibility()
        {
            bool shouldShowTrails = Speed > TrailMinimumSpeed;
            if (shouldShowTrails == _trailsVisible || _trailRenderers == null)
            {
                return;
            }

            _trailsVisible = shouldShowTrails;
            for (int i = 0; i < _trailRenderers.Length; i++)
            {
                TrailRenderer trail = _trailRenderers[i];
                if (trail == null)
                {
                    continue;
                }

                trail.emitting = false;
                trail.Clear();
                trail.enabled = shouldShowTrails;
                trail.emitting = shouldShowTrails;
            }
        }

        public bool IsColliderValidForCollisions(Collider coll)
        {
            return !IgnoredColliders.Contains(coll);
        }

        public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
        {
        }

        public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
        {
        }

        public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport)
        {
        }

        public void OnDiscreteCollisionDetected(Collider hitCollider)
        {
        }
    }
}
