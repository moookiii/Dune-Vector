using UnityEngine;

namespace DuneVector
{
    public enum TraversalRingType
    {
        GroundBoost,
        Flight,
        Health,
    }

    [DisallowMultipleComponent]
    public sealed class TraversalRing : MonoBehaviour
    {
        public TraversalRingType RingType;
        [Min(0.1f)] public float InnerRadius = 2.75f;
        [Min(0.05f)] public float TriggerDepth = 0.65f;
        [Min(0f)] public float ReactivationDelay = 2.5f;
        [Min(1f)] public float BoostRingActiveScale = 1.45f;
        [InspectorName("Flight Ring Active Scale")]
        [Min(1f)] public float FlightModeScale = 1.45f;
        [Min(0f)] public float FlightModeScaleSharpness = 4.5f;
        [Min(0f)] public float ClockwiseRotationSpeed = 32f;
        [Min(0f)] public float FlightModeHeightOffset;
        [Min(0f)] public float FlightModeHeightSharpness;
        public string ProceduralIdentity;

        public bool HasActivated { get; private set; }
        public int ActivationCount { get; private set; }

        private DroneCharacterController _controller;
        private DroneHealth _health;
        private float _healthRestored;
        private Transform _visualRoot;
        private Vector3 _previousWorldPosition;
        private bool _hasPreviousWorldPosition;
        private bool _inside;
        private float _nextActivationTime;
        private float _pulse;
        private float _modeScale = 1f;
        private float _visualSpin;
        private Camera _billboardCamera;
        private Vector3 _restingLocalPosition;

        public void Initialize(
            TraversalRingType type,
            DroneCharacterController controller,
            DroneHealth health,
            DuneVectorMaterials materials,
            float majorRadius,
            float healthRestored,
            float healthHeartScale,
            string identity)
        {
            RingType = type;
            _controller = controller;
            _health = health;
            _healthRestored = healthRestored;
            InnerRadius = majorRadius - 0.58f;
            ProceduralIdentity = identity;
            _restingLocalPosition = transform.localPosition;
            _visualRoot = DuneVectorVisuals.CreateRingVisual(transform, type, materials, majorRadius, healthHeartScale);
            gameObject.name = type switch
            {
                TraversalRingType.GroundBoost => "Ground Boost Ring",
                TraversalRingType.Flight => "Elevated Flight Ring",
                _ => "Health Ring",
            };
        }

        public void BindTargets(DroneCharacterController controller, DroneHealth health)
        {
            _controller = controller;
            _health = health;
            _inside = false;
            _hasPreviousWorldPosition = false;
        }

        private void Update()
        {
            if (_controller == null)
            {
                return;
            }

            if (RingType == TraversalRingType.Flight)
            {
                bool isFlying = _controller.CurrentMode == DroneTraversalMode.Flight;
                Vector3 targetPosition = _restingLocalPosition
                    + (Vector3.up * (isFlying ? FlightModeHeightOffset : 0f));
                transform.localPosition = Vector3.Lerp(
                    transform.localPosition,
                    targetPosition,
                    DuneVectorMath.Sharpness(FlightModeHeightSharpness, Time.deltaTime));
            }

            Vector3 worldPosition = _controller.WorldCenter;
            float activationRadius = InnerRadius * _modeScale;
            bool currentlyInside = Vector3.Distance(worldPosition, transform.position) <= activationRadius;
            bool passedThrough = false;
            if (_hasPreviousWorldPosition)
            {
                Vector3 segment = worldPosition - _previousWorldPosition;
                float segmentLengthSquared = segment.sqrMagnitude;
                if (segmentLengthSquared > 0.0001f)
                {
                    float interpolation = Mathf.Clamp01(
                        Vector3.Dot(transform.position - _previousWorldPosition, segment) / segmentLengthSquared);
                    Vector3 closestPoint = _previousWorldPosition + (segment * interpolation);
                    passedThrough = Vector3.Distance(closestPoint, transform.position) <= activationRadius;
                }
            }

            if ((currentlyInside && !_inside) || (passedThrough && !_inside))
            {
                TryActivate();
            }

            _inside = currentlyInside;
            _previousWorldPosition = worldPosition;
            _hasPreviousWorldPosition = true;

            _pulse = Mathf.MoveTowards(_pulse, 0f, Time.deltaTime * 1.8f);
            if (_visualRoot != null)
            {
                float targetModeScale = RingType switch
                {
                    TraversalRingType.Flight => _controller.CurrentMode == DroneTraversalMode.Flight
                        ? FlightModeScale
                        : 1f,
                    TraversalRingType.GroundBoost => Mathf.Lerp(
                        1f,
                        BoostRingActiveScale,
                        _controller.BoostRemainingNormalized),
                    _ => 1f,
                };
                _modeScale = Mathf.Lerp(
                    _modeScale,
                    targetModeScale,
                    DuneVectorMath.Sharpness(FlightModeScaleSharpness, Time.deltaTime));
                float scale = _modeScale * (1f + (Mathf.Sin(_pulse * Mathf.PI) * 0.085f));
                _visualRoot.localScale = Vector3.one * scale;
                _visualSpin = Mathf.Repeat(
                    _visualSpin - (ClockwiseRotationSpeed * Time.deltaTime),
                    360f);
            }
        }

        private void LateUpdate()
        {
            if (_visualRoot == null)
            {
                return;
            }

            if (_billboardCamera == null)
            {
                _billboardCamera = Camera.main;
            }
            if (_billboardCamera == null)
            {
                return;
            }

            Vector3 toCamera = _billboardCamera.transform.position - _visualRoot.position;
            if (toCamera.sqrMagnitude < 0.001f)
            {
                return;
            }

            _visualRoot.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                * Quaternion.AngleAxis(_visualSpin, Vector3.forward);
        }

        private void TryActivate()
        {
            if (Time.time < _nextActivationTime)
            {
                return;
            }

            if (RingType == TraversalRingType.Health && (_health == null || !_health.RestoreHealth(_healthRestored)))
            {
                return;
            }

            _nextActivationTime = Time.time + ReactivationDelay;
            HasActivated = true;
            ActivationCount++;
            _pulse = 1f;

            if (RingType == TraversalRingType.GroundBoost)
            {
                _controller.ActivateBoost();
            }
            else
            {
                if (RingType == TraversalRingType.Flight)
                {
                    _controller.RequestFlight(transform.forward);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = RingType switch
            {
                TraversalRingType.GroundBoost => new Color(1f, 0.5f, 0f, 0.7f),
                TraversalRingType.Flight => new Color(0f, 0.8f, 1f, 0.7f),
                _ => new Color(1f, 0.1f, 0.25f, 0.7f),
            };
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, InnerRadius);
        }
    }
}
