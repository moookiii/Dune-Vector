using System;
using UnityEngine;

namespace DuneVector
{
    public enum DeliveryJobPhase
    {
        FindPackage,
        DeliverPackage,
    }

    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorDeliveryLoop : MonoBehaviour
    {
        public DeliveryJobPhase Phase { get; private set; }
        public int CompletedDeliveries { get; private set; }
        public Transform ActiveObjective { get; private set; }

        private DroneCharacterController _player;
        private DesertWorldStreamer _world;
        private Camera _camera;
        private DuneVectorMaterials _materials;
        private DeliveryTuning _settings;
        private DuneVectorEnvironmentalHazardSystem _environmentalHazards;
        private Transform _package;
        private JobTraversalRing _pickupRing;
        private JobTraversalRing _deliveryRing;
        private LogicalPosition _packageLogicalPosition;
        private LogicalPosition _deliveryLogicalPosition;
        private double _packageHeight;
        private double _deliveryHeight;
        private int _jobIndex;
        private int _sessionSeed;
        private float _completionMessageTime;
        private GUIStyle _statusStyle;
        private readonly DuneVectorObjectiveIndicator _objectiveIndicator = new DuneVectorObjectiveIndicator();

        public void Initialize(
            DroneCharacterController player,
            DesertWorldStreamer world,
            Camera camera,
            DuneVectorMaterials materials,
            DeliveryTuning settings)
        {
            _player = player;
            _world = world;
            _camera = camera;
            _materials = materials;
            _settings = settings;
            _world.WorldShifted += HandleWorldShift;
            _sessionSeed = unchecked(
                world.WorldSeed
                ^ settings.JobSeedOffset
                ^ (settings.RandomizeLocationsEachPlay ? Environment.TickCount : 0));
            BeginNextJob();
        }

        public void BindEnvironmentalHazardSystem(DuneVectorEnvironmentalHazardSystem environmentalHazards)
        {
            _environmentalHazards = environmentalHazards;
        }

        private void HandleWorldShift(Vector3 shift)
        {
            _pickupRing?.ApplyWorldShift(shift);
            _deliveryRing?.ApplyWorldShift(shift);
        }

        private void BeginNextJob()
        {
            CleanupJobObjects();
            _jobIndex++;
            Phase = DeliveryJobPhase.FindPackage;

            LogicalPosition playerLogical = _world.LogicalPlayerPosition;
            System.Random random = new System.Random(unchecked(_sessionSeed ^ (_jobIndex * 486187739)));
            _packageLogicalPosition = ChooseLocation(
                playerLogical,
                random,
                _settings.MinimumPickupDistance,
                Mathf.Max(_settings.MinimumPickupDistance, _settings.MaximumPickupDistance));
            _packageHeight = _world.HeightField.SampleHeight(_packageLogicalPosition.X, _packageLogicalPosition.Z);

            _deliveryLogicalPosition = ChooseLocation(
                _packageLogicalPosition,
                random,
                _settings.MinimumDeliveryDistance,
                Mathf.Max(_settings.MinimumDeliveryDistance, _settings.MaximumDeliveryDistance));
            _deliveryHeight = _world.HeightField.SampleHeight(_deliveryLogicalPosition.X, _deliveryLogicalPosition.Z);

            _package = DuneVectorVisuals.CreatePackageVisual(transform, _materials, _settings.PackageScale);
            _package.name = $"Package {_jobIndex:000}";

            Vector3 approach = LogicalDirection(playerLogical, _packageLogicalPosition);
            double pickupRingHeight = _packageHeight + _settings.PickupRingGroundOffset;
            _pickupRing = CreateJobRing(
                "Pickup Ring",
                _packageLogicalPosition,
                pickupRingHeight,
                approach,
                true,
                HandlePickup);
            ActiveObjective = _package;
            RefreshWorldPositions();
        }

        private void HandlePickup()
        {
            if (Phase != DeliveryJobPhase.FindPackage || _package == null)
            {
                return;
            }

            Phase = DeliveryJobPhase.DeliverPackage;
            if (_pickupRing != null)
            {
                Destroy(_pickupRing.gameObject);
                _pickupRing = null;
            }

            Transform carryParent = _player.DroneVisualRoot != null ? _player.DroneVisualRoot : _player.transform;
            _package.SetParent(carryParent, false);
            _package.localPosition = new Vector3(0f, -0.62f, -0.28f);
            _package.localRotation = Quaternion.Euler(0f, 18f, 0f);

            Vector3 deliveryApproach = LogicalDirection(_packageLogicalPosition, _deliveryLogicalPosition);
            _deliveryRing = CreateJobRing(
                "Delivery Ring",
                _deliveryLogicalPosition,
                _deliveryHeight + _settings.DeliveryRingGroundOffset,
                deliveryApproach,
                false,
                HandleDelivery);
            ActiveObjective = _deliveryRing.transform;
            RefreshWorldPositions();
        }

        private void HandleDelivery()
        {
            if (Phase != DeliveryJobPhase.DeliverPackage || _package == null)
            {
                return;
            }

            CompletedDeliveries++;
            _completionMessageTime = 2.2f;
            ReleaseDeliveredPackage();
            BeginNextJob();
        }

        private void ReleaseDeliveredPackage()
        {
            Vector3 carrierVelocity = _player != null && _player.Motor != null
                ? _player.Motor.Velocity
                : Vector3.zero;
            DroppedDeliveryPackage.Release(_package, transform, _world, _settings, carrierVelocity);
            _package = null;
        }

        private JobTraversalRing CreateJobRing(
            string ringName,
            LogicalPosition logicalPosition,
            double height,
            Vector3 approachDirection,
            bool isPickup,
            Action callback)
        {
            GameObject ringObject = new GameObject(ringName);
            ringObject.transform.SetParent(transform, false);
            Vector3 planarApproach = Vector3.ProjectOnPlane(approachDirection, Vector3.up);
            if (planarApproach.sqrMagnitude < 0.001f)
            {
                planarApproach = Vector3.forward;
            }
            ringObject.transform.rotation = Quaternion.LookRotation(planarApproach.normalized, Vector3.up);

            JobTraversalRing ring = ringObject.AddComponent<JobTraversalRing>();
            float ringRadius = isPickup ? _settings.ObjectiveRingRadius : _settings.DeliveryRingRadius;
            ring.Initialize(_player, _camera, _materials, _settings, isPickup, ringRadius, callback);
            ring.LogicalPosition = logicalPosition;
            ring.LogicalHeight = height;
            return ring;
        }

        private void LateUpdate()
        {
            RefreshWorldPositions();
            if (_completionMessageTime > 0f)
            {
                _completionMessageTime = Mathf.Max(0f, _completionMessageTime - Time.deltaTime);
            }
        }

        private void RefreshWorldPositions()
        {
            if (_world == null)
            {
                return;
            }

            if (Phase == DeliveryJobPhase.FindPackage && _package != null)
            {
                _package.position = _world.LogicalToLocal(
                    _packageLogicalPosition.X,
                    _packageHeight + _settings.PickupRingHeight,
                    _packageLogicalPosition.Z);
                _package.Rotate(0f, 28f * Time.deltaTime, 0f, Space.World);
            }
            if (_pickupRing != null)
            {
                _pickupRing.transform.position = _world.LogicalToLocal(
                    _pickupRing.LogicalPosition.X,
                    _pickupRing.LogicalHeight,
                    _pickupRing.LogicalPosition.Z);
            }
            if (_deliveryRing != null)
            {
                _deliveryRing.transform.position = _world.LogicalToLocal(
                    _deliveryRing.LogicalPosition.X,
                    _deliveryRing.LogicalHeight,
                    _deliveryRing.LogicalPosition.Z);
            }
        }

        private static LogicalPosition ChooseLocation(
            LogicalPosition origin,
            System.Random random,
            float minimumDistance,
            float maximumDistance)
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            double distance = minimumDistance + (random.NextDouble() * (maximumDistance - minimumDistance));
            return new LogicalPosition(
                origin.X + (Math.Cos(angle) * distance),
                origin.Z + (Math.Sin(angle) * distance));
        }

        private static Vector3 LogicalDirection(LogicalPosition from, LogicalPosition to)
        {
            return new Vector3((float)(to.X - from.X), 0f, (float)(to.Z - from.Z)).normalized;
        }

        private void CleanupJobObjects()
        {
            if (_pickupRing != null) Destroy(_pickupRing.gameObject);
            if (_deliveryRing != null) Destroy(_deliveryRing.gameObject);
            if (_package != null) Destroy(_package.gameObject);
            _pickupRing = null;
            _deliveryRing = null;
            _package = null;
            ActiveObjective = null;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            CleanupJobObjects();
        }

        private void EnsureStyles()
        {
            if (_statusStyle != null)
            {
                return;
            }

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
        }

        private Color EvaluateCompletionTextColor()
        {
            float phase = Mathf.Repeat(
                Time.unscaledTime * Mathf.Max(0f, _settings.CompletionTextColorCyclesPerSecond) * 3f,
                3f);
            int segment = Mathf.FloorToInt(phase);
            float blend = Mathf.SmoothStep(0f, 1f, phase - segment);

            switch (segment)
            {
                case 0:
                    return Color.Lerp(_settings.CompletionTextRed, _settings.CompletionTextGreen, blend);
                case 1:
                    return Color.Lerp(_settings.CompletionTextGreen, _settings.CompletionTextBlue, blend);
                default:
                    return Color.Lerp(_settings.CompletionTextBlue, _settings.CompletionTextRed, blend);
            }
        }

        private void OnGUI()
        {
            // This overlay only draws; it owns no controls and mutates no state. Running the
            // layout pass would repeat every measurement for nothing, so only Repaint does work.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            if (ActiveObjective == null || _camera == null || _player == null)
            {
                return;
            }
            EnsureStyles();

            if (_environmentalHazards == null || !_environmentalHazards.IsElectricalInterferenceActive)
            {
                float distance = Vector3.Distance(_player.WorldCenter, ActiveObjective.position);
                string objectiveLabel = Phase == DeliveryJobPhase.FindPackage ? "PICKUP" : "DELIVER";
                _objectiveIndicator.Draw(_camera, ActiveObjective, objectiveLabel, distance, _settings);
            }

            if (_completionMessageTime > 0f)
            {
                _statusStyle.normal.textColor = EvaluateCompletionTextColor();
                GUI.Label(
                    new Rect(24f, 158f, Mathf.Max(1f, Screen.width - 48f), 30f),
                    $"DELIVERY COMPLETE  •  {CompletedDeliveries} TOTAL",
                    _statusStyle);
            }
        }
    }

    [DefaultExecutionOrder(1200)]
    [DisallowMultipleComponent]
    public sealed class JobTraversalRing : MonoBehaviour
    {
        public LogicalPosition LogicalPosition;
        public double LogicalHeight;
        public float ActivationRadius => _innerRadius;

        private DroneCharacterController _player;
        private Action _onCrossed;
        private Func<bool> _canActivate;
        private Transform _visual;
        private DeliveryTuning _settings;
        private bool _isPickup;
        private float _innerRadius;
        private Vector3 _previousWorldPosition;
        private bool _hasPreviousPosition;
        private bool _activated;
        private bool _playDeliveryAudio = true;

        public void Initialize(
            DroneCharacterController player,
            Camera billboardCamera,
            DuneVectorMaterials materials,
            DeliveryTuning settings,
            bool isPickup,
            float radius,
            Action onCrossed,
            Func<bool> canActivate = null,
            bool playDeliveryAudio = true)
        {
            _player = player;
            _onCrossed = onCrossed;
            _canActivate = canActivate;
            _settings = settings;
            _isPickup = isPickup;
            _playDeliveryAudio = playDeliveryAudio;
            // The zone is a flat ground footprint. Its trigger radius is exactly the radius the
            // hexagon is drawn at, so touching the visible edge is what completes the objective.
            _innerRadius = Mathf.Max(0.5f, radius);
            _visual = CreateGroundDropZoneVisual(radius);
        }

        public void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
            if (_hasPreviousPosition)
            {
                _previousWorldPosition += shift;
            }
        }

        private void Update()
        {
            if (_activated || _player == null)
            {
                return;
            }
            if (_canActivate != null && !_canActivate())
            {
                // Ignore teleport and setup movement entirely. Starting with a fresh
                // sample prevents that movement segment from consuming the zone.
                _hasPreviousPosition = false;
                return;
            }

            Vector3 worldPosition = _player.WorldCenter;
            if (CrossedGroundDropZone(worldPosition))
            {
                Activate(Vector3.up);
                return;
            }

            _previousWorldPosition = worldPosition;
            _hasPreviousPosition = true;
        }

        private Transform CreateGroundDropZoneVisual(float radius)
        {
            if (_settings == null)
            {
                return null;
            }

            GameObject prefab = _isPickup
                ? _settings.PickupRingGroundPrefab
                : _settings.DeliveryRingGroundPrefab;
            if (prefab == null)
            {
                return null;
            }

            float authoredRadius = _isPickup
                ? _settings.PickupRingPrefabAuthoredRadius
                : _settings.DeliveryRingPrefabAuthoredRadius;
            Vector3 scaleMultiplier = _isPickup
                ? _settings.PickupRingPrefabScale
                : _settings.DeliveryRingPrefabScale;
            Vector3 localOffset = _isPickup
                ? _settings.PickupRingPrefabLocalOffset
                : _settings.DeliveryRingPrefabLocalOffset;
            Vector3 localEulerAngles = _isPickup
                ? _settings.PickupRingPrefabLocalEulerAngles
                : _settings.DeliveryRingPrefabLocalEulerAngles;
            float groundOffset = _isPickup
                ? _settings.PickupRingGroundOffset
                : _settings.DeliveryRingGroundOffset;

            GameObject instance = Instantiate(prefab, transform, false);
            instance.name = _isPickup ? "Pickup Ground Ring Visual" : "Delivery Ground Ring Visual";
            Transform instanceTransform = instance.transform;
            float fitScale = radius / Mathf.Max(0.01f, authoredRadius);
            Vector3 authoredScale = instanceTransform.localScale;
            // The zone keeps its authored proportions. Fitting the footprint on X/Z alone leaves
            // the hexagon squashed and lifts its ground plane off the terrain.
            instanceTransform.localScale = Vector3.Scale(authoredScale * fitScale, scaleMultiplier);
            localOffset.y -= groundOffset + _settings.GroundRingPrefabTerrainInset;
            instanceTransform.localPosition += localOffset;
            instanceTransform.localRotation *= Quaternion.Euler(localEulerAngles);
            return instanceTransform;
        }

        private bool CrossedGroundDropZone(Vector3 worldPosition)
        {
            Vector2 center = new Vector2(transform.position.x, transform.position.z);
            Vector2 current = new Vector2(worldPosition.x, worldPosition.z);
            float radiusSquared = _innerRadius * _innerRadius;
            if ((current - center).sqrMagnitude <= radiusSquared)
            {
                return true;
            }
            if (!_hasPreviousPosition)
            {
                return false;
            }

            Vector2 previous = new Vector2(_previousWorldPosition.x, _previousWorldPosition.z);
            Vector2 segment = current - previous;
            float segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return false;
            }

            float interpolation = Mathf.Clamp01(Vector2.Dot(center - previous, segment) / segmentLengthSquared);
            Vector2 closestPoint = previous + (segment * interpolation);
            return (closestPoint - center).sqrMagnitude <= radiusSquared;
        }

        private void Activate(Vector3 crossingDirection)
        {
            _activated = true;
            _visual = null;
            Vector3 activationPosition = transform.position;

            // Complete the authoritative gameplay action before presentation or
            // broadcast callbacks. A portal-event subscriber must never be able
            // to play the pickup sound and then prevent the package state change.
            _onCrossed?.Invoke();
            if (_isPickup || !_playDeliveryAudio)
            {
                DuneVectorAudioManager.Instance?.PlayFlightRingSwoosh(activationPosition);
            }
            else
            {
                DuneVectorAudioManager.Instance?.PlayDeliveryRing(activationPosition);
            }
            DuneVectorPortalEvents.NotifyPlayerCrossed(
                activationPosition,
                crossingDirection,
                _player);
        }

    }
}
