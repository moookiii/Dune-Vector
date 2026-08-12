using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    public struct DroneRawInputFrame
    {
        public Vector2 Move;
        public Vector2 LookDelta;
        public Vector2 LookRate;
        public float Scroll;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool BoostHeld;
        public bool FirePressed;
        public bool InteractPressed;
        public float MenuNavigate;
        public bool ConfirmPressed;
        public bool CancelPressed;
    }

    [DisallowMultipleComponent]
    public sealed class DroneInput : MonoBehaviour
    {
        public DroneRawInputFrame Current { get; private set; }
        private DroneTuning _tuning;
        private bool _menuAxisEngaged;

        public void Initialize(DroneTuning tuning) => _tuning = tuning;

        public void Capture()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad gamepad = Gamepad.current;

            Vector2 move = Vector2.zero;
            bool jumpPressed = false;
            bool jumpHeld = false;
            bool boostHeld = false;
            if (keyboard != null)
            {
                move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                move.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
                jumpHeld = keyboard.spaceKey.isPressed;
                boostHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }

            if (gamepad != null)
            {
                move += gamepad.leftStick.ReadValue();
                if (gamepad.leftTrigger.ReadValue() >= ControllerTriggerThreshold)
                {
                    move.y += 1f;
                }
                jumpPressed |= gamepad.buttonSouth.wasPressedThisFrame;
                jumpHeld |= gamepad.buttonSouth.isPressed;
                boostHeld |= gamepad.rightTrigger.ReadValue() >= ControllerTriggerThreshold;
            }

            bool gamepadFirePressed = gamepad != null && gamepad.buttonWest.wasPressedThisFrame;
            float gamepadMenuNavigate = ReadGamepadMenuNavigate(gamepad);

            Current = new DroneRawInputFrame
            {
                Move = Vector2.ClampMagnitude(move, 1f),
                LookDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero,
                LookRate = gamepad != null ? gamepad.rightStick.ReadValue() : Vector2.zero,
                Scroll = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f,
                JumpPressed = jumpPressed,
                JumpHeld = jumpHeld,
                BoostHeld = boostHeld,
                FirePressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) || gamepadFirePressed,
                InteractPressed = (keyboard != null && keyboard.eKey.wasPressedThisFrame) ||
                                  (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame),
                MenuNavigate = (keyboard != null
                    ? (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ? 1f : 0f) -
                      (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame ? 1f : 0f)
                    : 0f) + gamepadMenuNavigate,
                ConfirmPressed = (keyboard != null &&
                    (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) ||
                    (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame),
                CancelPressed = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                                (gamepad != null && gamepad.buttonEast.wasPressedThisFrame),
            };
        }

        private float ControllerTriggerThreshold => _tuning != null ? _tuning.ControllerTriggerThreshold : 1f;

        private float ReadGamepadMenuNavigate(Gamepad gamepad)
        {
            if (gamepad == null)
            {
                _menuAxisEngaged = false;
                return 0f;
            }

            float axis = gamepad.dpad.ReadValue().y + gamepad.leftStick.ReadValue().y;
            float deadzone = _tuning != null ? _tuning.ControllerMenuDeadzone : 1f;
            if (Mathf.Abs(axis) < deadzone)
            {
                _menuAxisEngaged = false;
                return 0f;
            }
            if (_menuAxisEngaged)
            {
                return 0f;
            }

            _menuAxisEngaged = true;
            return axis < 0f ? 1f : -1f;
        }
    }

    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class DronePlayer : MonoBehaviour
    {
        public DroneCharacterController Character;
        public DroneCameraController CharacterCamera;
        public DroneInput InputSource;
        public DroneHealth Health;
        public DroneEnergyLauncher EnergyLauncher;
        public DroneStaminaSystem Stamina;

        public bool AutomatedInputEnabled { get; private set; }
        public bool HumanGameplayInputSuppressed { get; private set; }
        public DroneRawInputFrame AutomatedInput { get; private set; }
        public bool InputEnabled { get; private set; } = true;
        public bool IsHazardControlLocked => _hazardControlLockTimeRemaining > 0f;
        public float HazardControlLockTimeRemaining => _hazardControlLockTimeRemaining;
        public DroneRawInputFrame CurrentCommand => AutomatedInputEnabled
            ? AutomatedInput
            : InputSource != null ? InputSource.Current : default;

        private Quaternion _disabledMovementRotation = Quaternion.identity;
        private bool _disabledFlightStopEnabled;
        private float _hazardControlLockTimeRemaining;

        private void Start()
        {
            Cursor.lockState = InputEnabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !InputEnabled;
        }

        private void Update()
        {
            if (Health != null && Health.IsDead)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (DuneVectorMapHUD.IsWorldMapOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                ClearCharacterInput();
                return;
            }

            if (!HumanGameplayInputSuppressed)
            {
                InputSource?.Capture();
            }

            if (!InputEnabled)
            {
                ClearCharacterInput();
                return;
            }

            if (_hazardControlLockTimeRemaining > 0f)
            {
                _hazardControlLockTimeRemaining = Mathf.Max(
                    0f,
                    _hazardControlLockTimeRemaining - Time.deltaTime);
                ClearCharacterInput(true);
                return;
            }

            bool weaponInputAllowed = AutomatedInputEnabled ||
                                      Cursor.lockState == CursorLockMode.Locked ||
                                      (Gamepad.current != null &&
                                       Gamepad.current.buttonWest.wasPressedThisFrame);
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            DroneRawInputFrame raw = CurrentCommand;
            Stamina?.Tick(raw.BoostHeld, Time.deltaTime);
            DroneControlInput characterInput = new DroneControlInput
            {
                Move = raw.Move,
                CameraRotation = CharacterCamera.transform.rotation,
                JumpPressed = raw.JumpPressed,
                JumpHeld = raw.JumpHeld,
            };
            Character.SetInputs(in characterInput);

            if (weaponInputAllowed && raw.FirePressed)
            {
                EnergyLauncher?.RequestFire();
            }

            if (AutomatedInputEnabled && (AutomatedInput.JumpPressed || AutomatedInput.FirePressed))
            {
                DroneRawInputFrame consumed = AutomatedInput;
                consumed.JumpPressed = false;
                consumed.FirePressed = false;
                AutomatedInput = consumed;
            }
        }

        private void LateUpdate()
        {
            if (!InputEnabled ||
                DuneVectorMapHUD.IsWorldMapOpen ||
                (Health != null && Health.IsDead))
            {
                return;
            }

            DroneRawInputFrame raw = AutomatedInputEnabled ? AutomatedInput : InputSource.Current;
            Vector2 look = Cursor.lockState == CursorLockMode.Locked || AutomatedInputEnabled
                ? raw.LookDelta
                : Vector2.zero;
            CharacterCamera.UpdateWithInput(Time.deltaTime, look, raw.LookRate, raw.Scroll);

            if (AutomatedInputEnabled && raw.LookDelta.sqrMagnitude > 0f)
            {
                DroneRawInputFrame consumed = AutomatedInput;
                consumed.LookDelta = Vector2.zero;
                AutomatedInput = consumed;
            }
        }

        public void SetAutomatedInput(DroneRawInputFrame input)
        {
            AutomatedInputEnabled = true;
            AutomatedInput = input;
        }

        public void SetHumanGameplayInputSuppressed(bool suppressed)
        {
            HumanGameplayInputSuppressed = suppressed;
            if (suppressed && InputSource != null)
            {
                InputSource.enabled = false;
            }
        }

        public void ClearAutomatedInput()
        {
            AutomatedInputEnabled = false;
            AutomatedInput = default;
        }

        public void ConsumeContextualActions()
        {
            if (!AutomatedInputEnabled)
            {
                return;
            }

            DroneRawInputFrame consumed = AutomatedInput;
            consumed.InteractPressed = false;
            consumed.MenuNavigate = 0f;
            consumed.ConfirmPressed = false;
            consumed.CancelPressed = false;
            AutomatedInput = consumed;
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled)
            {
                CaptureDisabledMovementRotation();
                ClearCharacterInput();
            }
        }

        public void SetDisabledFlightStopEnabled(bool enabled)
        {
            _disabledFlightStopEnabled = enabled;
        }

        public void SetDisabledMovementRotation(Quaternion rotation)
        {
            _disabledMovementRotation = rotation;
        }

        public void ApplyHazardAirBrake(float duration)
        {
            if (duration <= 0f)
            {
                return;
            }

            _hazardControlLockTimeRemaining = Mathf.Max(
                _hazardControlLockTimeRemaining,
                duration);
            CaptureDisabledMovementRotation();
            ClearCharacterInput(true);
        }

        private void CaptureDisabledMovementRotation()
        {
            if (Character == null || Character.Motor == null)
            {
                _disabledMovementRotation = Quaternion.identity;
                return;
            }

            Vector3 direction = Character.CurrentMode == DroneTraversalMode.Flight
                ? Character.Motor.BaseVelocity
                : Character.Motor.CharacterForward;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Character.Motor.CharacterForward;
            }

            direction.Normalize();
            Vector3 up = Character.Motor.CharacterUp;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.999f)
            {
                up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.999f
                    ? Vector3.up
                    : Vector3.forward;
            }
            _disabledMovementRotation = Quaternion.LookRotation(direction, up);
        }

        private void ClearCharacterInput(bool forceFlightBrake = false)
        {
            if (Character == null)
            {
                return;
            }

            bool stopFlight = _disabledFlightStopEnabled &&
                Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
            DroneControlInput input = new DroneControlInput
            {
                CameraRotation = _disabledMovementRotation,
                JumpHeld = forceFlightBrake || stopFlight,
                StopWhenFlightBraking = stopFlight,
            };
            Stamina?.Tick(false, Time.deltaTime);
            Character.SetInputs(in input);
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class DroneCameraController : MonoBehaviour
    {
        [Header("Framing")]
        public Camera Camera;
        public Transform FollowTransform;
        public DroneCharacterController SpeedSource;
        public Vector2 FollowPointFraming = new Vector2(0f, 0.55f);
        [Min(0f)] public float FollowingSharpness = 4.2f;

        [Header("Distance")]
        [Min(0f)] public float DefaultDistance = 8.5f;
        [Min(0f)] public float MinDistance = 3f;
        [Min(0f)] public float MaxDistance = 15f;
        [Min(0f)] public float DistanceMovementSpeed = 0.75f;
        [Min(0f)] public float DistanceSharpness = 5f;
        [Min(0f)] public float SpeedDistanceAmount = 3.2f;
        [Min(0.01f)] public float SpeedForMaximumDistance = 38f;

        [Header("Rotation")]
        public bool InvertX;
        public bool InvertY;
        [Range(-89f, 89f)] public float DefaultPitch = 14f;
        [Range(-89f, 89f)] public float MinPitch = -18f;
        [Range(-89f, 89f)] public float MaxPitch = 58f;
        [Min(0f)] public float LookSensitivity = 0.085f;
        public float ControllerLookSpeed { get; private set; }
        [Min(0f)] public float RotationSharpness = 30f;

        [Header("Obstruction")]
        [Min(0f)] public float ObstructionCheckRadius = 0.28f;
        public LayerMask ObstructionLayers = -1;
        [Min(0f)] public float ObstructionSharpness = 35f;
        [Min(0f)] public float ObstructionReleaseSharpness = 7f;
        public List<Collider> IgnoredColliders = new List<Collider>();

        [Header("Speed FOV")]
        [Range(30f, 100f)] public float BaseFieldOfView = 64f;
        [Range(0f, 20f)] public float SpeedFieldOfViewAmount = 7f;
        [Range(0f, 10f)] public float BoostFieldOfViewAmount = 2.5f;
        [Min(0f)] public float FieldOfViewSharpness = 5f;

        public Vector3 PlanarDirection { get; private set; } = Vector3.forward;
        public float TargetDistance { get; private set; }
        public Vector3 CurrentFollowPosition => _currentFollowPosition;
        public float FollowingError => FollowTransform != null ? Vector3.Distance(_currentFollowPosition, FollowTransform.position) : 0f;
        public float GameplayFieldOfView => _gameplayFieldOfView;
        public float AppliedMusicFovOffset => _musicFovOffset;

        private const int MaxObstructions = 32;
        private readonly RaycastHit[] _obstructions = new RaycastHit[MaxObstructions];
        private Vector3 _currentFollowPosition;
        private float _targetPitch;
        private float _currentDistance;
        private bool _distanceIsObstructed;
        private bool _wasFlying;
        private Quaternion _flightTargetRotation;
        private bool _photographyModeActive;
        private float _photographyDistance;
        private float _photographyHeight;
        private float _photographyMinPitch = -85f;
        private float _photographyMaxPitch = 85f;
        private float _gameplayFieldOfView;
        private float _musicFovOffset;
        private float _musicRollDegrees;
        private Vector3 _musicLocalPositionOffset;

        public void SetMusicVisualizerPresentation(float fovOffset, float rollDegrees, Vector3 localPositionOffset)
        {
            _musicFovOffset = fovOffset;
            _musicRollDegrees = rollDegrees;
            _musicLocalPositionOffset = localPositionOffset;
        }

        public void SetPhotographyMode(bool active, float cameraDistance, float cameraHeight, float minPitch, float maxPitch)
        {
            _photographyModeActive = active;
            _photographyDistance = Mathf.Max(0f, cameraDistance);
            _photographyHeight = Mathf.Max(0f, cameraHeight);
            _photographyMinPitch = Mathf.Min(minPitch, maxPitch);
            _photographyMaxPitch = Mathf.Max(minPitch, maxPitch);
            if (active)
            {
                _currentDistance = _photographyDistance;
            }
            else
            {
                _targetPitch = Mathf.Clamp(_targetPitch, MinPitch, MaxPitch);
            }
        }

        private float CurrentMinPitch => _photographyModeActive ? _photographyMinPitch : MinPitch;

        private float CurrentMaxPitch => _photographyModeActive ? _photographyMaxPitch : MaxPitch;

        private void Awake()
        {
            if (Camera == null)
            {
                Camera = GetComponent<Camera>();
            }
            Camera.fieldOfView = BaseFieldOfView;
            _gameplayFieldOfView = BaseFieldOfView;
            TargetDistance = Mathf.Clamp(DefaultDistance, MinDistance, MaxDistance);
            _currentDistance = TargetDistance;
            _targetPitch = Mathf.Clamp(DefaultPitch, MinPitch, MaxPitch);
        }

        private void OnValidate()
        {
            DefaultDistance = Mathf.Clamp(DefaultDistance, MinDistance, MaxDistance);
            DefaultPitch = Mathf.Clamp(DefaultPitch, MinPitch, MaxPitch);
        }

        public void SetFollowTransform(Transform followTransform)
        {
            FollowTransform = followTransform;
            PlanarDirection = Vector3.ProjectOnPlane(followTransform.forward, Vector3.up).normalized;
            if (PlanarDirection.sqrMagnitude < 0.001f)
            {
                PlanarDirection = Vector3.forward;
            }
            _currentFollowPosition = followTransform.position;
            SnapToTarget();
        }

        public void ConfigureControllerLook(float lookSpeed)
        {
            ControllerLookSpeed = Mathf.Max(0f, lookSpeed);
        }

        public void UpdateWithInput(float deltaTime, Vector2 lookDelta, float scrollInput)
        {
            UpdateWithInput(deltaTime, lookDelta, Vector2.zero, scrollInput);
        }

        public void UpdateWithInput(
            float deltaTime,
            Vector2 lookDelta,
            Vector2 lookRate,
            float scrollInput)
        {
            if (FollowTransform == null || Camera == null)
            {
                return;
            }

            if (InvertX)
            {
                lookDelta.x *= -1f;
                lookRate.x *= -1f;
            }
            if (InvertY)
            {
                lookDelta.y *= -1f;
                lookRate.y *= -1f;
            }

            Vector2 lookAngles = (lookDelta * LookSensitivity) +
                                 (lookRate * ControllerLookSpeed * deltaTime);

            bool isFlying = SpeedSource != null && SpeedSource.CurrentMode == DroneTraversalMode.Flight;
            Quaternion desiredRotation;
            if (isFlying)
            {
                if (!_wasFlying)
                {
                    _flightTargetRotation = transform.rotation;
                }

                _flightTargetRotation = Quaternion.AngleAxis(lookAngles.x, Vector3.up) * _flightTargetRotation;
                Vector3 pitchAxis = _flightTargetRotation * Vector3.right;
                _flightTargetRotation = Quaternion.AngleAxis(-lookAngles.y, pitchAxis) * _flightTargetRotation;
                desiredRotation = _flightTargetRotation;
            }
            else
            {
                if (_wasFlying)
                {
                    Vector3 exitForward = transform.forward;
                    Vector3 exitPlanarForward = Vector3.ProjectOnPlane(exitForward, Vector3.up);
                    if (exitPlanarForward.sqrMagnitude > 0.001f)
                    {
                        PlanarDirection = exitPlanarForward.normalized;
                    }
                    _targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(exitForward.y, -1f, 1f)) * Mathf.Rad2Deg, CurrentMinPitch, CurrentMaxPitch);
                }

                Quaternion yawRotation = Quaternion.AngleAxis(lookAngles.x, Vector3.up);
                PlanarDirection = Vector3.ProjectOnPlane(yawRotation * PlanarDirection, Vector3.up).normalized;
                if (PlanarDirection.sqrMagnitude < 0.001f)
                {
                    PlanarDirection = Vector3.forward;
                }

                _targetPitch -= lookAngles.y;
                _targetPitch = Mathf.Clamp(_targetPitch, CurrentMinPitch, CurrentMaxPitch);
                Quaternion planarRotation = Quaternion.LookRotation(PlanarDirection, Vector3.up);
                desiredRotation = planarRotation * Quaternion.Euler(_targetPitch, 0f, 0f);
            }
            _wasFlying = isFlying;
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, DuneVectorMath.Sharpness(RotationSharpness, deltaTime));

            if (!_photographyModeActive)
            {
                TargetDistance = Mathf.Clamp(TargetDistance - (scrollInput * DistanceMovementSpeed), MinDistance, MaxDistance);
            }
            float speed01 = !_photographyModeActive && SpeedSource != null
                ? Mathf.Clamp01(SpeedSource.Speed / SpeedForMaximumDistance)
                : 0f;
            float idealDistance = _photographyModeActive
                ? _photographyDistance
                : Mathf.Clamp(TargetDistance + (speed01 * SpeedDistanceAmount), MinDistance, MaxDistance);

            _currentFollowPosition = Vector3.Lerp(
                _currentFollowPosition,
                FollowTransform.position,
                DuneVectorMath.Sharpness(FollowingSharpness, deltaTime));

            Vector3 castDirection = -(transform.rotation * Vector3.forward);
            float closestDistance = idealDistance;
            bool foundObstruction = false;
            int obstructionCount = Physics.SphereCastNonAlloc(
                _currentFollowPosition,
                ObstructionCheckRadius,
                castDirection,
                _obstructions,
                idealDistance,
                ObstructionLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < obstructionCount; i++)
            {
                Collider collider = _obstructions[i].collider;
                if (collider == null || IgnoredColliders.Contains(collider) || _obstructions[i].distance <= 0f)
                {
                    continue;
                }
                if (_obstructions[i].distance < closestDistance)
                {
                    closestDistance = _obstructions[i].distance;
                    foundObstruction = true;
                }
            }

            _distanceIsObstructed = foundObstruction;
            float distanceSharpness = _distanceIsObstructed ? ObstructionSharpness : ObstructionReleaseSharpness;
            _currentDistance = Mathf.Lerp(
                _currentDistance,
                foundObstruction
                    ? Mathf.Max(_photographyModeActive ? 0f : MinDistance * 0.2f, closestDistance - ObstructionCheckRadius)
                    : idealDistance,
                DuneVectorMath.Sharpness(distanceSharpness, deltaTime));

            Vector3 targetPosition;
            if (_photographyModeActive)
            {
                targetPosition = _currentFollowPosition +
                    (castDirection * _currentDistance) +
                    (Vector3.up * _photographyHeight);
            }
            else
            {
                targetPosition = _currentFollowPosition + (castDirection * _currentDistance);
                targetPosition += transform.right * FollowPointFraming.x;
                targetPosition += transform.up * FollowPointFraming.y;
            }
            transform.position = targetPosition;
            transform.position += transform.TransformVector(_musicLocalPositionOffset);
            transform.rotation *= Quaternion.Euler(0f, 0f, _musicRollDegrees);

            float targetFov = BaseFieldOfView + (speed01 * SpeedFieldOfViewAmount);
            if (SpeedSource != null && SpeedSource.IsBoosting)
            {
                targetFov += BoostFieldOfViewAmount;
            }
            _gameplayFieldOfView = Mathf.Lerp(
                _gameplayFieldOfView,
                targetFov,
                DuneVectorMath.Sharpness(FieldOfViewSharpness, deltaTime));
            Camera.fieldOfView = _gameplayFieldOfView + _musicFovOffset;
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            _currentFollowPosition += shift;
            transform.position += shift;
        }

        public void SnapToTarget()
        {
            if (FollowTransform == null)
            {
                return;
            }
            _currentFollowPosition = FollowTransform.position;
            transform.rotation = Quaternion.LookRotation(PlanarDirection, Vector3.up) * Quaternion.Euler(_targetPitch, 0f, 0f);
            Vector3 castDirection = -(transform.rotation * Vector3.forward);
            transform.position = _currentFollowPosition + (castDirection * _currentDistance) + (transform.up * FollowPointFraming.y);
        }

        /// <summary>
        /// Re-anchors the smoothed follow point onto the target without touching the camera's
        /// yaw or pitch. Used to pin the camera to the drone for a few frames after a teleport,
        /// so a floating-origin rebase cannot leave the follow point smoothing in from the
        /// position the drone teleported away from.
        /// </summary>
        public void SnapFollowPositionToTarget()
        {
            if (FollowTransform == null)
            {
                return;
            }
            _currentFollowPosition = FollowTransform.position;
            Vector3 castDirection = -(transform.rotation * Vector3.forward);
            transform.position = _currentFollowPosition + (castDirection * _currentDistance) + (transform.up * FollowPointFraming.y);
        }

        public void SnapToTarget(Vector3 forward)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (planarForward.sqrMagnitude > 0.001f)
            {
                PlanarDirection = planarForward.normalized;
            }
            SnapToTarget();
        }
    }
}
