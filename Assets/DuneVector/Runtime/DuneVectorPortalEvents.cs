using System;
using UnityEngine;

namespace DuneVector
{
    public readonly struct DuneVectorPortalCrossing
    {
        public Vector3 Position { get; }
        public Vector3 TravelDirection { get; }

        public DuneVectorPortalCrossing(Vector3 position, Vector3 travelDirection)
        {
            Position = position;
            TravelDirection = travelDirection.sqrMagnitude > Mathf.Epsilon
                ? travelDirection.normalized
                : Vector3.forward;
        }
    }

    public static class DuneVectorPortalEvents
    {
        public static event Action<DuneVectorPortalCrossing> PlayerCrossed;

        public static void NotifyPlayerCrossed(
            Vector3 portalPosition,
            Vector3 portalForward,
            DroneCharacterController player)
        {
            Vector3 travelDirection = player != null && player.Motor != null
                ? player.Motor.Velocity
                : Vector3.zero;
            if (travelDirection.sqrMagnitude <= Mathf.Epsilon && player != null)
            {
                travelDirection = player.AimDirection;
            }
            if (travelDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                travelDirection = portalForward;
            }

            PlayerCrossed?.Invoke(new DuneVectorPortalCrossing(
                portalPosition,
                travelDirection));
        }
    }

    /// <summary>
    /// Runtime behavior added to the authored flat WarpGatePrefab instances streamed into free
    /// roam. A gate is one-way: the drone must cross its horizontal plane from below while moving
    /// upward through the opening. The rail controller owns the exact return position, so consuming
    /// the gate before launch makes the encounter a single-use discovery.
    /// </summary>
    [DefaultExecutionOrder(1210)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorWarpGate : MonoBehaviour
    {
        public string Identity { get; private set; }
        public bool IsConsumed { get; private set; }

        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private float _openingRadius;
        private float _minimumEntryUpwardSpeed;
        private bool _visibleDuringContracts;
        private bool _hideOutsideFreeRoam;
        private ParticleSystem[] _particleSystems;
        private int _occupancyHandle = DuneVectorWorldOccupancy.InvalidHandle;
        private int _spacingHandle = DuneVectorWorldOccupancy.InvalidHandle;
        private Renderer[] _renderers;
        private bool[] _rendererInitialEnabled;
        private bool _visualVisible;
        private Vector3 _previousPlayerPosition;
        private bool _hasPreviousPlayerPosition;

        public void Initialize(
            string identity,
            float openingRadius,
            float minimumEntryUpwardSpeed,
            bool visibleDuringContracts,
            bool hideOutsideFreeRoam)
        {
            Identity = identity;
            _openingRadius = Mathf.Max(0.1f, openingRadius);
            _minimumEntryUpwardSpeed = Mathf.Max(0f, minimumEntryUpwardSpeed);
            _visibleDuringContracts = visibleDuringContracts;
            _hideOutsideFreeRoam = hideOutsideFreeRoam;
            _world = GetComponentInParent<DesertWorldStreamer>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            _rendererInitialEnabled = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _rendererInitialEnabled[i] = _renderers[i] != null && _renderers[i].enabled;
            }
            _visualVisible = true;
            if (_hideOutsideFreeRoam)
            {
                SetVisualVisible(false);
            }
            _player = ResolvePlayer();
            if (_player != null)
            {
                _previousPlayerPosition = _player.WorldCenter;
                _hasPreviousPlayerPosition = true;
            }
        }

        internal void SetOccupancyHandles(int footprintHandle, int spacingHandle)
        {
            _occupancyHandle = footprintHandle;
            _spacingHandle = spacingHandle;
        }

        private void LateUpdate()
        {
            if (IsConsumed)
            {
                return;
            }

            DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
            DuneVectorCourierGame courier = bootstrap != null ? bootstrap.CourierGame : null;
            bool isFreeRoam = courier != null && courier.State == CourierRunState.FreeRoam;
            bool isContract = courier != null && courier.IsContractActive;
            if (_hideOutsideFreeRoam)
            {
                SetVisualVisible(isFreeRoam || (isContract && _visibleDuringContracts));
            }
            bool canEnter = isFreeRoam;
            if (_player == null)
            {
                _player = ResolvePlayer();
            }
            if (_player == null)
            {
                return;
            }

            Vector3 currentPlayerPosition = _player.WorldCenter;
            if (!canEnter)
            {
                _previousPlayerPosition = currentPlayerPosition;
                _hasPreviousPlayerPosition = true;
                return;
            }
            if (!_hasPreviousPlayerPosition)
            {
                _previousPlayerPosition = currentPlayerPosition;
                _hasPreviousPlayerPosition = true;
                return;
            }

            Vector3 planeNormal = transform.up.sqrMagnitude > Mathf.Epsilon
                ? transform.up.normalized
                : Vector3.up;
            Vector3 velocity = _player.Motor != null
                ? _player.Motor.Velocity
                : (currentPlayerPosition - _previousPlayerPosition) / Mathf.Max(0.0001f, Time.deltaTime);
            bool movingUpward = Vector3.Dot(velocity, planeNormal) >= _minimumEntryUpwardSpeed;
            if (movingUpward && HasBottomEntryCrossing(
                    _previousPlayerPosition,
                    currentPlayerPosition,
                    transform.position,
                    planeNormal,
                    _openingRadius))
            {
                if (courier != null && courier.TryEnterRailFromWarpGate(this))
                {
                    Consume();
                    return;
                }
            }

            _previousPlayerPosition = currentPlayerPosition;
        }

        public void Consume()
        {
            if (IsConsumed)
            {
                return;
            }

            IsConsumed = true;
            _world?.MarkWarpGateConsumed(Identity);
            ReleaseOccupancy();
            enabled = false;
            Destroy(gameObject);
        }

        public static bool HasBottomEntryCrossing(
            Vector3 previousPosition,
            Vector3 currentPosition,
            Vector3 planePosition,
            Vector3 planeNormal,
            float openingRadius)
        {
            Vector3 normal = planeNormal.sqrMagnitude > Mathf.Epsilon
                ? planeNormal.normalized
                : Vector3.up;
            float previousSignedDistance = Vector3.Dot(previousPosition - planePosition, normal);
            float currentSignedDistance = Vector3.Dot(currentPosition - planePosition, normal);
            if (previousSignedDistance >= 0f || currentSignedDistance < 0f)
            {
                return false;
            }

            Vector3 travel = currentPosition - previousPosition;
            float normalTravel = Vector3.Dot(travel, normal);
            if (normalTravel <= Mathf.Epsilon)
            {
                return false;
            }

            float crossingFraction = Mathf.Clamp01(-previousSignedDistance / normalTravel);
            Vector3 crossingPosition = previousPosition + (travel * crossingFraction);
            Vector3 planarOffset = crossingPosition - planePosition;
            planarOffset -= normal * Vector3.Dot(planarOffset, normal);
            return planarOffset.sqrMagnitude <= Mathf.Max(0f, openingRadius) * Mathf.Max(0f, openingRadius);
        }

        private DroneCharacterController ResolvePlayer()
        {
            DuneVectorBootstrap bootstrap = DuneVectorBootstrap.Instance;
            return bootstrap != null ? bootstrap.Drone : null;
        }

        private void SetVisualVisible(bool visible)
        {
            if (_visualVisible == visible || _renderers == null)
            {
                return;
            }

            _visualVisible = visible;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    _renderers[i].enabled = visible && _rendererInitialEnabled[i];
                }
            }

            if (!visible || _particleSystems == null)
            {
                return;
            }

            // A hidden gate's systems sit under their authored culling mode, which pauses
            // them while nothing renders. Restarting on the way back keeps a revealed gate
            // from appearing as an empty transform.
            for (int i = 0; i < _particleSystems.Length; i++)
            {
                ParticleSystem system = _particleSystems[i];
                if (system != null && !system.isPlaying)
                {
                    system.Play(false);
                }
            }
        }

        private void ReleaseOccupancy()
        {
            DuneVectorWorldOccupancy.Release(_occupancyHandle);
            _occupancyHandle = DuneVectorWorldOccupancy.InvalidHandle;
            DuneVectorWorldOccupancy.Release(_spacingHandle);
            _spacingHandle = DuneVectorWorldOccupancy.InvalidHandle;
        }

        private void OnDestroy()
        {
            ReleaseOccupancy();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.75f);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(Vector3.zero, _openingRadius > 0f ? _openingRadius : 10f);
            Gizmos.matrix = previousMatrix;
        }
    }
}
