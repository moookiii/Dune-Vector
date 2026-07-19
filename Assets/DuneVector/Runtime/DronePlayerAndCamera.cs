using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuneVector
{
    public struct DroneRawInputFrame
    {
        public Vector2 Move;
        public Vector2 LookDelta;
        public float Scroll;
        public bool JumpPressed;
        public bool JumpHeld;
        public bool FirePressed;
    }

    [DisallowMultipleComponent]
    public sealed class DroneInput : MonoBehaviour
    {
        public DroneRawInputFrame Current { get; private set; }

        public void Capture()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            Vector2 move = Vector2.zero;
            bool jumpPressed = false;
            bool jumpHeld = false;
            if (keyboard != null)
            {
                move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                move.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
                jumpHeld = keyboard.spaceKey.isPressed;
            }

            Current = new DroneRawInputFrame
            {
                Move = Vector2.ClampMagnitude(move, 1f),
                LookDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero,
                Scroll = mouse != null ? mouse.scroll.ReadValue().y / 120f : 0f,
                JumpPressed = jumpPressed,
                JumpHeld = jumpHeld,
                FirePressed = mouse != null && mouse.leftButton.wasPressedThisFrame,
            };
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

        public bool AutomatedInputEnabled { get; private set; }
        public DroneRawInputFrame AutomatedInput { get; private set; }
        public bool InputEnabled { get; private set; } = true;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (Health != null && Health.IsDead)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (!InputEnabled)
            {
                ClearCharacterInput();
                return;
            }

            bool weaponInputAllowed = AutomatedInputEnabled || Cursor.lockState == CursorLockMode.Locked;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            InputSource.Capture();
            DroneRawInputFrame raw = AutomatedInputEnabled ? AutomatedInput : InputSource.Current;
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
            if (!InputEnabled || (Health != null && Health.IsDead))
            {
                return;
            }

            DroneRawInputFrame raw = AutomatedInputEnabled ? AutomatedInput : InputSource.Current;
            Vector2 look = Cursor.lockState == CursorLockMode.Locked || AutomatedInputEnabled ? raw.LookDelta : Vector2.zero;
            CharacterCamera.UpdateWithInput(Time.deltaTime, look, raw.Scroll);

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

        public void ClearAutomatedInput()
        {
            AutomatedInputEnabled = false;
            AutomatedInput = default;
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled)
            {
                ClearCharacterInput();
            }
        }

        private void ClearCharacterInput()
        {
            if (Character == null)
            {
                return;
            }

            DroneControlInput input = default;
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

        private const int MaxObstructions = 32;
        private readonly RaycastHit[] _obstructions = new RaycastHit[MaxObstructions];
        private Vector3 _currentFollowPosition;
        private float _targetPitch;
        private float _currentDistance;
        private bool _distanceIsObstructed;
        private bool _wasFlying;
        private Quaternion _flightTargetRotation;

        private void Awake()
        {
            if (Camera == null)
            {
                Camera = GetComponent<Camera>();
            }
            Camera.fieldOfView = BaseFieldOfView;
            Camera.nearClipPlane = 0.08f;
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

        public void UpdateWithInput(float deltaTime, Vector2 lookDelta, float scrollInput)
        {
            if (FollowTransform == null || Camera == null)
            {
                return;
            }

            if (InvertX)
            {
                lookDelta.x *= -1f;
            }
            if (InvertY)
            {
                lookDelta.y *= -1f;
            }

            bool isFlying = SpeedSource != null && SpeedSource.CurrentMode == DroneTraversalMode.Flight;
            Quaternion desiredRotation;
            if (isFlying)
            {
                if (!_wasFlying)
                {
                    _flightTargetRotation = transform.rotation;
                }

                _flightTargetRotation = Quaternion.AngleAxis(lookDelta.x * LookSensitivity, Vector3.up) * _flightTargetRotation;
                Vector3 pitchAxis = _flightTargetRotation * Vector3.right;
                _flightTargetRotation = Quaternion.AngleAxis(-lookDelta.y * LookSensitivity, pitchAxis) * _flightTargetRotation;
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
                    _targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(exitForward.y, -1f, 1f)) * Mathf.Rad2Deg, MinPitch, MaxPitch);
                }

                Quaternion yawRotation = Quaternion.AngleAxis(lookDelta.x * LookSensitivity, Vector3.up);
                PlanarDirection = Vector3.ProjectOnPlane(yawRotation * PlanarDirection, Vector3.up).normalized;
                if (PlanarDirection.sqrMagnitude < 0.001f)
                {
                    PlanarDirection = Vector3.forward;
                }

                _targetPitch -= lookDelta.y * LookSensitivity;
                _targetPitch = Mathf.Clamp(_targetPitch, MinPitch, MaxPitch);
                Quaternion planarRotation = Quaternion.LookRotation(PlanarDirection, Vector3.up);
                desiredRotation = planarRotation * Quaternion.Euler(_targetPitch, 0f, 0f);
            }
            _wasFlying = isFlying;
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, DuneVectorMath.Sharpness(RotationSharpness, deltaTime));

            TargetDistance = Mathf.Clamp(TargetDistance - (scrollInput * DistanceMovementSpeed), MinDistance, MaxDistance);
            float speed01 = SpeedSource != null ? Mathf.Clamp01(SpeedSource.Speed / SpeedForMaximumDistance) : 0f;
            float idealDistance = Mathf.Clamp(TargetDistance + (speed01 * SpeedDistanceAmount), MinDistance, MaxDistance);

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
                foundObstruction ? Mathf.Max(MinDistance * 0.2f, closestDistance - ObstructionCheckRadius) : idealDistance,
                DuneVectorMath.Sharpness(distanceSharpness, deltaTime));

            Vector3 targetPosition = _currentFollowPosition + (castDirection * _currentDistance);
            targetPosition += transform.right * FollowPointFraming.x;
            targetPosition += transform.up * FollowPointFraming.y;
            transform.position = targetPosition;

            float targetFov = BaseFieldOfView + (speed01 * SpeedFieldOfViewAmount);
            if (SpeedSource != null && SpeedSource.IsBoosting)
            {
                targetFov += BoostFieldOfViewAmount;
            }
            Camera.fieldOfView = Mathf.Lerp(Camera.fieldOfView, targetFov, DuneVectorMath.Sharpness(FieldOfViewSharpness, deltaTime));
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
    }
}
