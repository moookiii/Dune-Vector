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

        private DroneCharacterController _player;
        private Camera _billboardCamera;
        private Action _onCrossed;
        private Func<bool> _canActivate;
        private Transform _visual;
        private DuneVectorPortalVisual _portalVisual;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _colorProperties;
        private DeliveryTuning _settings;
        private RingTuning _ringTuning;
        private bool _isPickup;
        private bool _isGroundDropZone;
        private float _innerRadius;
        private float _speedScale = 1f;
        private Vector3 _previousWorldPosition;
        private bool _hasPreviousPosition;
        private bool _activated;
        private bool _playDeliveryAudio = true;
        private float _spin;
        private float _spinSpeed;
        private float _spinDirection = 1f;

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
            _billboardCamera = billboardCamera;
            _onCrossed = onCrossed;
            _canActivate = canActivate;
            _settings = settings;
            _ringTuning = materials.RingPortalTuning;
            _isPickup = isPickup;
            _playDeliveryAudio = playDeliveryAudio;
            _isGroundDropZone = true;
            if (_isGroundDropZone)
            {
                _innerRadius = Mathf.Max(0.5f, radius);
                _visual = CreateGroundDropZoneVisual(radius);
                _renderers = Array.Empty<Renderer>();
                _colorProperties = new MaterialPropertyBlock();
                return;
            }

            float visualRadius = DuneVectorVisuals.CalculatePortalVisualRadius(
                radius,
                _ringTuning);
            _innerRadius = Mathf.Max(0.5f, visualRadius - 0.38f);
            _spinSpeed = _ringTuning.ClockwiseRotationSpeed;
            uint spinHash = DuneVectorMath.Hash(
                Mathf.RoundToInt(transform.position.x),
                Mathf.RoundToInt(transform.position.z),
                Mathf.RoundToInt(transform.position.y),
                isPickup ? 1 : 0);
            _spinDirection = (spinHash & 1u) == 0u ? -1f : 1f;
            _visual = DuneVectorVisuals.CreateJobRingVisual(transform, isPickup, materials, radius);
            _portalVisual = _visual.GetComponent<DuneVectorPortalVisual>();
            _renderers = _visual.GetComponentsInChildren<Renderer>(true);
            _colorProperties = new MaterialPropertyBlock();
            UpdateRgbBlend();
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
            UpdateBillboard();
            UpdateRgbBlend();
            UpdateSpeedScale();
            if (_activated || _player == null)
            {
                return;
            }
            if (_canActivate != null && !_canActivate())
            {
                // Ignore teleport and setup movement entirely. Starting with a fresh
                // sample prevents that movement segment from consuming the ring.
                _hasPreviousPosition = false;
                return;
            }

            Vector3 worldPosition = _player.WorldCenter;
            if (_isGroundDropZone)
            {
                if (CrossedGroundDropZone(worldPosition))
                {
                    Activate(Vector3.up);
                    return;
                }

                _previousWorldPosition = worldPosition;
                _hasPreviousPosition = true;
                return;
            }

            Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
            if (_hasPreviousPosition)
            {
                // Convert both segment endpoints using this frame's billboard rotation.
                // Camera movement alone therefore cannot look like a ring crossing.
                Vector3 previousLocalPosition = transform.InverseTransformPoint(_previousWorldPosition);
                if (Mathf.Sign(previousLocalPosition.z) != Mathf.Sign(localPosition.z))
                {
                    float denominator = previousLocalPosition.z - localPosition.z;
                    if (Mathf.Abs(denominator) > 0.0001f)
                    {
                        float interpolation = Mathf.Clamp01(previousLocalPosition.z / denominator);
                        Vector3 crossingPoint = Vector3.Lerp(previousLocalPosition, localPosition, interpolation);
                        float radialDistance = new Vector2(crossingPoint.x, crossingPoint.y).magnitude;
                        bool crossedOpening = _player.CurrentMode == DroneTraversalMode.Normal
                            ? Mathf.Abs(crossingPoint.x) <= _innerRadius
                            : radialDistance <= _innerRadius;
                        if (crossedOpening)
                        {
                            Activate(transform.forward);
                            return;
                        }
                    }
                }
            }

            _previousWorldPosition = worldPosition;
            _hasPreviousPosition = true;

            if (_visual != null)
            {
                _spin = Mathf.Repeat(
                    _spin + (
                        _spinSpeed *
                        _spinDirection *
                        (_portalVisual != null ? _portalVisual.RotationSpeedMultiplier : 1f) *
                        Time.deltaTime),
                    360f);
                _visual.localRotation = Quaternion.AngleAxis(_spin, Vector3.forward);
            }
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
            instanceTransform.localScale = Vector3.Scale(
                instanceTransform.localScale * fitScale,
                scaleMultiplier);
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
            _portalVisual?.PlayActivationReaction(
                true,
                _spinSpeed * _spinDirection,
                crossingDirection);
            _visual = null;
            _portalVisual = null;
            if (_isPickup || !_playDeliveryAudio)
            {
                DuneVectorAudioManager.Instance?.PlayFlightRingSwoosh(transform.position);
            }
            else
            {
                DuneVectorAudioManager.Instance?.PlayDeliveryRing(transform.position);
            }
            DuneVectorPortalEvents.NotifyPlayerCrossed(
                transform.position,
                crossingDirection,
                _player);
            _onCrossed?.Invoke();
        }

        private void UpdateSpeedScale()
        {
            if (_isGroundDropZone || _visual == null || _player == null || _ringTuning == null)
            {
                return;
            }

            float targetScale = 1f;
            if (_player.CurrentMode == DroneTraversalMode.Flight)
            {
                float speedNormalized = Mathf.Clamp01(
                    _player.Speed / Mathf.Max(Mathf.Epsilon, _player.CurrentMaximumFlightSpeed));
                float flightModeScale = Mathf.Max(1f, _ringTuning.UpperFlightRingActiveScale);
                float maximumSpeedScale = Mathf.Max(
                    flightModeScale,
                    _ringTuning.UpperFlightRingMaximumSpeedScale);
                targetScale = Mathf.Lerp(flightModeScale, maximumSpeedScale, speedNormalized);
            }

            _speedScale = Mathf.Lerp(
                _speedScale,
                targetScale,
                DuneVectorMath.Sharpness(_ringTuning.UpperFlightRingScaleSharpness, Time.deltaTime));
            transform.localScale = Vector3.one * _speedScale;
        }

        private void UpdateRgbBlend()
        {
            if (_isGroundDropZone || _settings == null || _renderers == null || _colorProperties == null)
            {
                return;
            }

            float hueOffset = _isPickup ? 0f : _settings.DeliveryRingRgbHueOffset;
            float hue = Mathf.Repeat((Time.unscaledTime * _settings.ObjectiveRingRgbBlendSpeed) + hueOffset, 1f);
            Color rgb = Color.HSVToRGB(hue, 1f, 1f);
            Color baseColor = rgb * _settings.ObjectiveRingRgbBaseIntensity;
            Color emissionColor = rgb * _settings.ObjectiveRingRgbEmissionIntensity;
            baseColor.a = 1f;
            emissionColor.a = 1f;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_colorProperties);
                _colorProperties.SetColor("_BaseColor", baseColor);
                _colorProperties.SetColor("_EmissionColor", emissionColor);
                _colorProperties.SetColor("_PortalColor", emissionColor);
                renderer.SetPropertyBlock(_colorProperties);
            }
        }

        private void UpdateBillboard()
        {
            if (_isGroundDropZone)
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

            float billboardDisableRadius = _settings != null
                ? Mathf.Max(0f, _settings.ObjectiveRingBillboardDisableRadius)
                : 0f;
            if (_player != null
                && billboardDisableRadius > 0f
                && (_player.WorldCenter - transform.position).sqrMagnitude
                    <= billboardDisableRadius * billboardDisableRadius)
            {
                return;
            }

            Vector3 toCamera = _billboardCamera.transform.position - transform.position;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                // The root defines both the rendered ring plane and its mathematical
                // pass-through collider, so they always billboard together.
                transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
            }
        }
    }
}
