using System;
using UnityEngine;

namespace DuneVector
{
    public enum TraversalRingType
    {
        GroundBoost,
        Flight,
        Health,
        Coin,
        UpperFlight,
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
        public event Action<TraversalRing> Activated;

        private DroneCharacterController _controller;
        private DroneHealth _health;
        private ITraversalRingReward _collectibleReward;
        private Transform _visualRoot;
        private DuneVectorPortalVisual _portalVisual;
        private Vector3 _previousWorldPosition;
        private bool _hasPreviousWorldPosition;
        private bool _inside;
        private float _nextActivationTime;
        private float _pulse;
        private float _modeScale = 1f;
        private float _visualSpin;
        private float _spinDirection = 1f;
        private Camera _billboardCamera;
        private Quaternion _billboardFacingRotation;
        private bool _hasBillboardFacingRotation;
        private Vector3 _restingLocalPosition;
        private Transform _collectibleIcon;
        private Quaternion _collectibleIconBaseRotation = Quaternion.identity;
        private DuneVectorMaterials _materials;
        private RingTuning _ringTuning;
        private TraversalRing _upperLayerRing;
        private Transform _cachedTransform;

        public void Initialize(
            TraversalRingType type,
            DroneCharacterController controller,
            DroneHealth health,
            DuneVectorMaterials materials,
            float majorRadius,
            RingTuning ringTuning,
            string identity)
        {
            RingType = type;
            _controller = controller;
            _health = health;
            _materials = materials;
            _ringTuning = ringTuning;
            _cachedTransform = transform;
            float visualRadius = DuneVectorVisuals.CalculatePortalVisualRadius(majorRadius, ringTuning);
            InnerRadius = Mathf.Max(0.1f, visualRadius - 0.58f);
            ProceduralIdentity = identity;
            uint spinHash = !string.IsNullOrEmpty(ProceduralIdentity)
                ? DuneVectorMath.StableHash(ProceduralIdentity)
                : DuneVectorMath.Hash(
                    Mathf.RoundToInt(_cachedTransform.position.x),
                    Mathf.RoundToInt(_cachedTransform.position.z),
                    Mathf.RoundToInt(_cachedTransform.position.y));
            _spinDirection = (spinHash & 1u) == 0u ? -1f : 1f;
            _restingLocalPosition = _cachedTransform.localPosition;
            _visualRoot = DuneVectorVisuals.CreateRingVisual(
                _cachedTransform,
                type,
                materials,
                majorRadius,
                ringTuning);
            _billboardFacingRotation = _visualRoot.rotation;
            _hasBillboardFacingRotation = true;
            _portalVisual = _visualRoot.GetComponent<DuneVectorPortalVisual>();
            if (IsCollectible)
            {
                _collectibleIcon = _visualRoot.Find("Collectible Icon");
                if (_collectibleIcon != null)
                {
                    _collectibleIconBaseRotation = _collectibleIcon.localRotation;
                }
            }
            gameObject.name = type switch
            {
                TraversalRingType.GroundBoost => "Ground Boost Ring",
                TraversalRingType.Flight => "Elevated Flight Ring",
                TraversalRingType.UpperFlight => "Upper Flight Ring",
                TraversalRingType.Health => "Health Ring",
                _ => "Coin Ring",
            };
        }

        private bool IsCollectible => RingType == TraversalRingType.Health || RingType == TraversalRingType.Coin;

        public void SetCollectibleReward(ITraversalRingReward reward)
        {
            _collectibleReward = reward;
            _collectibleReward?.BindTargets(_health, _controller != null ? _controller.GetComponent<DroneGoldWallet>() : null);
        }

        public void BindTargets(DroneCharacterController controller, DroneHealth health)
        {
            _controller = controller;
            _health = health;
            _collectibleReward?.BindTargets(health, controller != null ? controller.GetComponent<DroneGoldWallet>() : null);
            _inside = false;
            _hasPreviousWorldPosition = false;
            _upperLayerRing?.BindTargets(controller, health);
        }

        public void SpawnUpperFlightLayer(
            Vector3 restingLocalPosition,
            Quaternion localRotation,
            float flightModeHeightOffset)
        {
            if (RingType != TraversalRingType.Flight || _upperLayerRing != null || _materials == null)
            {
                return;
            }

            GameObject upperObject = new GameObject("Upper Flight Ring");
            upperObject.transform.SetParent(transform.parent, false);
            upperObject.transform.localPosition = restingLocalPosition;
            upperObject.transform.localRotation = localRotation;

            TraversalRing upperRing = upperObject.AddComponent<TraversalRing>();
            upperRing.Initialize(
                TraversalRingType.UpperFlight,
                _controller,
                _health,
                _materials,
                _ringTuning.UpperFlightRingRadius,
                _ringTuning,
                $"{ProceduralIdentity}:upper");
            upperRing.TriggerDepth = TriggerDepth;
            upperRing.ReactivationDelay = ReactivationDelay;
            upperRing.FlightModeScale = _ringTuning.UpperFlightRingActiveScale;
            upperRing.FlightModeScaleSharpness = _ringTuning.UpperFlightRingScaleSharpness;
            upperRing.ClockwiseRotationSpeed = _ringTuning.UpperFlightRingRotationSpeed;
            upperRing.FlightModeHeightOffset = flightModeHeightOffset;
            upperRing.FlightModeHeightSharpness = _ringTuning.UpperFlightModeHeightSharpness;
            if (_controller != null && _controller.CurrentMode == DroneTraversalMode.Flight)
            {
                upperRing.transform.localPosition = restingLocalPosition + (Vector3.up * flightModeHeightOffset);
            }
            _upperLayerRing = upperRing;
        }

        private bool IsFlightRing => RingType == TraversalRingType.Flight || RingType == TraversalRingType.UpperFlight;

        internal void Tick(float deltaTime)
        {
            TickSelf(deltaTime);
            _upperLayerRing?.Tick(deltaTime);
        }

        private void TickSelf(float deltaTime)
        {
            if (_controller == null)
            {
                return;
            }

            if (IsFlightRing)
            {
                bool isFlying = _controller.CurrentMode == DroneTraversalMode.Flight;
                Vector3 targetPosition = _restingLocalPosition
                    + (Vector3.up * (isFlying ? FlightModeHeightOffset : 0f));
                _cachedTransform.localPosition = Vector3.Lerp(
                    _cachedTransform.localPosition,
                    targetPosition,
                    DuneVectorMath.Sharpness(FlightModeHeightSharpness, deltaTime));
            }

            Vector3 worldPosition = _controller.WorldCenter;
            float activationRadius = InnerRadius * _modeScale;
            float activationRadiusSquared = activationRadius * activationRadius;
            Vector3 ringPosition = _cachedTransform.position;
            bool currentlyInside = (worldPosition - ringPosition).sqrMagnitude <= activationRadiusSquared;
            bool passedThrough = false;
            if (_hasPreviousWorldPosition)
            {
                Vector3 segment = worldPosition - _previousWorldPosition;
                float segmentLengthSquared = segment.sqrMagnitude;
                if (segmentLengthSquared > 0.0001f)
                {
                    float interpolation = Mathf.Clamp01(
                        Vector3.Dot(ringPosition - _previousWorldPosition, segment) / segmentLengthSquared);
                    Vector3 closestPoint = _previousWorldPosition + (segment * interpolation);
                    passedThrough = (closestPoint - ringPosition).sqrMagnitude <= activationRadiusSquared;
                }
            }

            if ((currentlyInside && !_inside) || (passedThrough && !_inside))
            {
                TryActivate();
            }

            _inside = currentlyInside;
            _previousWorldPosition = worldPosition;
            _hasPreviousWorldPosition = true;

            _pulse = Mathf.MoveTowards(_pulse, 0f, deltaTime * 1.8f);
            if (_visualRoot != null)
            {
                float targetModeScale = RingType switch
                {
                    TraversalRingType.Flight or TraversalRingType.UpperFlight => _controller.CurrentMode == DroneTraversalMode.Flight
                        ? FlightModeScale
                        : 1f,
                    TraversalRingType.GroundBoost => Mathf.Lerp(
                        1f,
                        BoostRingActiveScale,
                        _controller.RingBoostRemainingNormalized),
                    _ => 1f,
                };
                _modeScale = Mathf.Lerp(
                    _modeScale,
                    targetModeScale,
                    DuneVectorMath.Sharpness(FlightModeScaleSharpness, deltaTime));
                float scale = _modeScale * (1f + (Mathf.Sin(_pulse * Mathf.PI) * 0.085f));
                _visualRoot.localScale = Vector3.one * scale;
                _visualSpin = Mathf.Repeat(
                    _visualSpin + (
                        ClockwiseRotationSpeed *
                        _spinDirection *
                        (_portalVisual != null ? _portalVisual.RotationSpeedMultiplier : 1f) *
                        deltaTime),
                    360f);
                if (IsCollectible)
                {
                    if (_collectibleIcon != null)
                    {
                        // Cancel the parent ring's spin around its normal, then spin the
                        // collectible around the vertical axis lying in the ring plane.
                        _collectibleIcon.localRotation = Quaternion.AngleAxis(-_visualSpin, Vector3.up)
                            * Quaternion.AngleAxis(_visualSpin, Vector3.forward)
                            * _collectibleIconBaseRotation;
                    }
                }
            }
        }

        internal void LateTick(Camera viewCamera)
        {
            LateTickSelf(viewCamera);
            _upperLayerRing?.LateTick(viewCamera);
        }

        private void LateTickSelf(Camera viewCamera)
        {
            if (_visualRoot == null)
            {
                return;
            }

            if (viewCamera != null)
            {
                _billboardCamera = viewCamera;
            }
            else if (_billboardCamera == null)
            {
                _billboardCamera = Camera.main;
            }
            if (_billboardCamera == null)
            {
                return;
            }

            float billboardDisableRadius = _ringTuning != null
                ? Mathf.Max(0f, _ringTuning.BillboardDisableRadius)
                : 0f;
            bool freezeBillboardFacing = _controller != null
                && billboardDisableRadius > 0f
                && (_controller.WorldCenter - _visualRoot.position).sqrMagnitude
                    <= billboardDisableRadius * billboardDisableRadius;
            if (!freezeBillboardFacing)
            {
                Vector3 toCamera = _billboardCamera.transform.position - _visualRoot.position;
                if (toCamera.sqrMagnitude >= 0.001f)
                {
                    _billboardFacingRotation = IsCollectible
                        ? Quaternion.FromToRotation(Vector3.up, toCamera.normalized)
                        : Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                    _hasBillboardFacingRotation = true;
                }
            }

            if (!_hasBillboardFacingRotation)
            {
                return;
            }

            Vector3 spinAxis = IsCollectible ? Vector3.up : Vector3.forward;
            _visualRoot.rotation = _billboardFacingRotation
                * Quaternion.AngleAxis(_visualSpin, spinAxis);
        }

        private void TryActivate()
        {
            if (Time.time < _nextActivationTime)
            {
                return;
            }

            if (_collectibleReward != null && !_collectibleReward.TryReward())
            {
                return;
            }

            _nextActivationTime = Time.time + ReactivationDelay;
            HasActivated = true;
            ActivationCount++;
            _pulse = 1f;
            bool detachReaction = IsCollectible;
            _portalVisual?.PlayActivationReaction(
                detachReaction,
                ClockwiseRotationSpeed * _spinDirection,
                IsCollectible ? Vector3.up : Vector3.forward);
            if (detachReaction)
            {
                _visualRoot = null;
                _collectibleIcon = null;
                _portalVisual = null;
            }

            if (RingType == TraversalRingType.GroundBoost)
            {
                _controller.ActivateBoost();
            }
            else
            {
                if (IsFlightRing)
                {
                    float speedMultiplier = RingType == TraversalRingType.UpperFlight
                        ? _ringTuning.UpperFlightSpeedMultiplier
                        : 1f;
                    _controller.RequestFlightFromRing(transform.forward, speedMultiplier);
                }
                else
                {
                    Destroy(gameObject);
                }
            }

            Activated?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = RingType switch
            {
                TraversalRingType.GroundBoost => new Color(1f, 0.5f, 0f, 0.7f),
                TraversalRingType.Flight => new Color(0f, 0.8f, 1f, 0.7f),
                TraversalRingType.UpperFlight => new Color(0.75f, 0.1f, 1f, 0.7f),
                TraversalRingType.Health => new Color(1f, 0.1f, 0.25f, 0.7f),
                _ => new Color(1f, 0.72f, 0.08f, 0.7f),
            };
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, InnerRadius);
        }
    }
}
