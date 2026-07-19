using System.Collections.Generic;
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
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(KinematicCharacterMotor))]
    public sealed class DroneCharacterController : MonoBehaviour, ICharacterController
    {
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
        [Min(0.1f)] public float FlightDuration = 14f;
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
        public bool IsRingBoosting => _ringBoostTimeRemaining > 0f;
        public float RingBoostRemainingNormalized => RingBoostDuration > 0f
            ? Mathf.Clamp01(_ringBoostTimeRemaining / RingBoostDuration)
            : 0f;
        public bool IsStableGrounded => Motor != null && Motor.GroundingStatus.IsStableOnGround;
        public float Speed => Motor != null ? Motor.Velocity.magnitude : 0f;
        public Vector3 WorldCenter => Motor != null
            ? Motor.TransientPosition + (Motor.CharacterUp * (Motor.Capsule.height * 0.5f))
            : transform.position;
        public float FlightElapsedTime => _flightElapsedTime;
        public float FlightTimeRemaining { get; private set; }
        public float FlightTimeNormalized => FlightDuration > 0f ? Mathf.Clamp01(FlightTimeRemaining / FlightDuration) : 0f;
        public DesertWorldStreamer World { get; private set; }

        private Vector2 _rawMove;
        private bool _flightBrakeHeld;
        private Vector3 _moveInputWorld;
        private Vector3 _cameraForward;
        private Vector3 _cameraRight;
        private Vector3 _lookInputWorld;
        private bool _jumpRequested;
        private bool _jumpConsumed;
        private bool _jumpedThisUpdate;
        private float _timeSinceJumpRequested = float.PositiveInfinity;
        private float _timeSinceStableGround = float.PositiveInfinity;

        private float _ringBoostTimeRemaining;
        private float _ringBurstTimeRemaining;
        private DroneStaminaSystem _stamina;
        private DroneBoostSpeedModifier _boostSpeedModifier;

        private bool _flightRequested;
        private bool _flightJustEntered;
        private bool _flightBurstRequested;
        private Vector3 _requestedFlightDirection;
        private Vector3 _flightDirection;
        private float _flightElapsedTime;
        private float _flightEntryLiftTimeRemaining;

        private Vector3 _visualBaseLocalPosition;
        private Vector3 _lastVisualForward;
        private float _currentVisualBank;
        private float _currentVisualPitch;
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
                _jumpRequested = true;
                _timeSinceJumpRequested = 0f;
            }
        }

        public void ActivateBoost()
        {
            _ringBoostTimeRemaining = RingBoostDuration;
            StartRingBurst();
        }

        public void RequestFlight(Vector3 launchDirection)
        {
            if (CurrentMode == DroneTraversalMode.Flight)
            {
                FlightTimeRemaining = FlightDuration;
                StartRingBurst();
                return;
            }

            _requestedFlightDirection = launchDirection.sqrMagnitude > 0.001f
                ? launchDirection.normalized
                : Motor.CharacterForward;
            _flightBurstRequested = true;
            _flightRequested = true;
        }

        public void BeforeCharacterUpdate(float deltaTime)
        {
            if (_flightRequested)
            {
                _flightRequested = false;
                CurrentMode = DroneTraversalMode.Flight;
                _flightElapsedTime = 0f;
                FlightTimeRemaining = FlightDuration;
                if (_flightBurstRequested)
                {
                    StartRingBurst();
                    _flightBurstRequested = false;
                }
                _flightDirection = _requestedFlightDirection.normalized;
                _flightJustEntered = true;
                _flightEntryLiftTimeRemaining = FlightEntryLiftDuration;
                _jumpRequested = false;
                _jumpConsumed = true;
                Motor.ForceUnground(0.2f);
                Motor.SetGroundSolvingActivation(false);
            }
        }

        public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
        {
            if (CurrentMode == DroneTraversalMode.Flight)
            {
                Vector3 desiredForward = _flightDirection.sqrMagnitude > 0.001f ? _flightDirection : Motor.CharacterForward;
                float maximumRadians = Mathf.Deg2Rad * FlightYawRate * deltaTime;
                Vector3 rateLimited = Vector3.RotateTowards(Motor.CharacterForward, desiredForward, maximumRadians, 0f);
                Vector3 smoothedForward = Vector3.Slerp(
                    Motor.CharacterForward,
                    rateLimited,
                    DuneVectorMath.Sharpness(FlightSteeringSharpness, deltaTime)).normalized;
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
                    DuneVectorMath.Sharpness(RotationSharpness, deltaTime)).normalized;
                currentRotation = Quaternion.LookRotation(smoothedLook, Motor.CharacterUp);
            }
        }

        public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            _boostSpeedModifier?.Tick(IsBoosting, deltaTime);
            if (CurrentMode == DroneTraversalMode.Flight)
            {
                UpdateFlightVelocity(ref currentVelocity, deltaTime);
            }
            else
            {
                UpdateNormalVelocity(ref currentVelocity, deltaTime);
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
                    targetSpeed = MaxGroundSpeed * _moveInputWorld.magnitude;
                }

                if (IsRingBoosting && targetDirection.sqrMagnitude > 0.001f)
                {
                    float burstMultiplier = GetRingBurstMultiplier();
                    targetSpeed = RingBoostMaxSpeed * burstMultiplier * _moveInputWorld.magnitude;
                    sharpness = _ringBurstTimeRemaining > 0f ? RingBurstAcceleration : RingBoostAcceleration;
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
                    Vector3 addedVelocity = _moveInputWorld * (AirAcceleration * deltaTime);
                    float maximumAirSpeed = _boostSpeedModifier != null
                        ? _boostSpeedModifier.ModifyTargetSpeed(MaxAirSpeed)
                        : MaxAirSpeed;
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
                    DuneVectorMath.Sharpness(FlightSteeringSharpness, deltaTime)).normalized;
            }

            float throttle = Mathf.Clamp01((forwardInput + 1f) * 0.5f);
            float targetSpeed = Mathf.Lerp(FlightSpeed * 0.62f, MaximumFlightSpeed, throttle);
            if (Mathf.Abs(forwardInput) < 0.05f)
            {
                targetSpeed = FlightSpeed;
            }
            if (_flightBrakeHeld)
            {
                targetSpeed = FlightBrakeSpeed;
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
                if (currentVelocity.magnitude < FlightSpeed * 0.72f)
                {
                    targetVelocity = Vector3.Slerp(targetVelocity.normalized, _requestedFlightDirection, 0.32f).normalized * FlightSpeed;
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
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, DuneVectorMath.Sharpness(acceleration, deltaTime));
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
                _flightElapsedTime += deltaTime;
                FlightTimeRemaining = Mathf.Max(0f, FlightTimeRemaining - deltaTime);
                if (FlightTimeRemaining <= 0f)
                {
                    FinishFlight();
                    return;
                }
                TryFinishFlight();
            }
        }

        private void TryFinishFlight()
        {
            if (_flightElapsedTime < MinimumFlightTime || World == null)
            {
                return;
            }

            float terrainHeight = World.SampleHeightAtLocal(Motor.TransientPosition.x, Motor.TransientPosition.z);
            float clearance = Motor.TransientPosition.y - terrainHeight;
            Vector3 terrainNormal = World.SampleNormalAtLocal(Motor.TransientPosition.x, Motor.TransientPosition.z);
            float terrainAngle = Vector3.Angle(terrainNormal, Vector3.up);

            if (clearance <= AcceptableLandingDistance
                && Motor.BaseVelocity.y <= MaximumLandingVerticalSpeed
                && terrainAngle <= MaximumLandingAngle)
            {
                FinishFlight();
            }
        }

        private void FinishFlight()
        {
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
            FlightTimeRemaining = 0f;
            _flightEntryLiftTimeRemaining = 0f;
            _timeSinceStableGround = float.PositiveInfinity;
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
                targetPitch = Mathf.Clamp(-Motor.BaseVelocity.y / Mathf.Max(1f, MaximumFlightSpeed), -1f, 1f) * 5f;
            }
            else
            {
                targetBank = -_rawMove.x * GroundTurnLean;
                targetPitch = -Mathf.Max(0f, _rawMove.y) * GroundVisualPitch;
            }

            float bankSharpness = Mathf.Abs(targetBank) > Mathf.Abs(_currentVisualBank) ? BankSharpness : BankRecoverySharpness;
            _currentVisualBank = Mathf.Lerp(_currentVisualBank, targetBank, DuneVectorMath.Sharpness(bankSharpness, deltaTime));
            _currentVisualPitch = Mathf.Lerp(_currentVisualPitch, targetPitch, DuneVectorMath.Sharpness(BankRecoverySharpness, deltaTime));
            DroneVisualRoot.localRotation = Quaternion.Euler(_currentVisualPitch, 0f, _currentVisualBank);

            float hover = Mathf.Sin(Time.time * HoverFrequency * Mathf.PI * 2f) * HoverAmplitude;
            DroneVisualRoot.localPosition = _visualBaseLocalPosition + (Vector3.up * hover);
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
