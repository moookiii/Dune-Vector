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
        private GUIStyle _markerStyle;
        private GUIStyle _arrowStyle;
        private GUIStyle _statusStyle;

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
            _pickupRing = CreateJobRing(
                "Pickup Ring",
                _packageLogicalPosition,
                _packageHeight + _settings.ObjectiveRingHeight,
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
                _deliveryHeight + _settings.ObjectiveRingHeight,
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
            BeginNextJob();
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
            ring.Initialize(_player, _camera, _materials, isPickup, _settings.ObjectiveRingRadius, callback);
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
                    _packageHeight + _settings.ObjectiveRingHeight,
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
            if (_markerStyle != null)
            {
                return;
            }

            _markerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            _arrowStyle = new GUIStyle(_markerStyle)
            {
                fontSize = 28,
            };
            _statusStyle = new GUIStyle(_markerStyle)
            {
                fontSize = 16,
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

            Vector3 projected = _camera.WorldToScreenPoint(ActiveObjective.position);
            Vector2 screenPoint = new Vector2(projected.x, Screen.height - projected.y);
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            const float padding = 56f;
            bool onScreen = projected.z > 0f
                && screenPoint.x >= padding
                && screenPoint.x <= Screen.width - padding
                && screenPoint.y >= padding
                && screenPoint.y <= Screen.height - padding;

            Vector2 markerPosition = screenPoint;
            Vector2 direction = screenPoint - center;
            if (!onScreen)
            {
                if (projected.z <= 0f)
                {
                    direction = -direction;
                }
                if (direction.sqrMagnitude < 0.001f)
                {
                    direction = Vector2.up;
                }
                direction.Normalize();
                float horizontalScale = (center.x - padding) / Mathf.Max(0.001f, Mathf.Abs(direction.x));
                float verticalScale = (center.y - padding) / Mathf.Max(0.001f, Mathf.Abs(direction.y));
                markerPosition = center + (direction * Mathf.Min(horizontalScale, verticalScale));

                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
                Matrix4x4 oldMatrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, markerPosition);
                GUI.Label(new Rect(markerPosition.x - 20f, markerPosition.y - 20f, 40f, 40f), "▲", _arrowStyle);
                GUI.matrix = oldMatrix;
            }
            else
            {
                GUI.Label(new Rect(markerPosition.x - 18f, markerPosition.y - 25f, 36f, 36f), "◆", _arrowStyle);
            }

            float distance = Vector3.Distance(_player.WorldCenter, ActiveObjective.position);
            string objectiveLabel = Phase == DeliveryJobPhase.FindPackage ? "PICKUP" : "DELIVER";
            GUI.Label(
                new Rect(markerPosition.x - 90f, markerPosition.y + 16f, 180f, 24f),
                $"{objectiveLabel}  {distance:0} m",
                _markerStyle);

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
        private Transform _visual;
        private float _innerRadius;
        private Vector3 _previousWorldPosition;
        private bool _hasPreviousPosition;
        private bool _activated;
        private float _spin;

        public void Initialize(
            DroneCharacterController player,
            Camera billboardCamera,
            DuneVectorMaterials materials,
            bool isPickup,
            float radius,
            Action onCrossed)
        {
            _player = player;
            _billboardCamera = billboardCamera;
            _onCrossed = onCrossed;
            _innerRadius = Mathf.Max(0.5f, radius - 0.38f);
            _visual = DuneVectorVisuals.CreateJobRingVisual(transform, isPickup, materials, radius);
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
            if (_activated || _player == null)
            {
                return;
            }

            Vector3 worldPosition = _player.WorldCenter;
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
                        if (radialDistance <= _innerRadius)
                        {
                            _activated = true;
                            _onCrossed?.Invoke();
                            return;
                        }
                    }
                }
            }

            _previousWorldPosition = worldPosition;
            _hasPreviousPosition = true;

            if (_visual != null)
            {
                _spin = Mathf.Repeat(_spin + (32f * Time.deltaTime), 360f);
                _visual.localRotation = Quaternion.AngleAxis(_spin, Vector3.forward);
            }
        }

        private void UpdateBillboard()
        {
            if (_billboardCamera == null)
            {
                _billboardCamera = Camera.main;
            }
            if (_billboardCamera == null)
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
