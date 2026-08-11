using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class DroneBoostRingTrail : MonoBehaviour
    {
        private struct TrailRing
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Age;
            public float Lifetime;
            public int HueIndex;
        }

        private static readonly int PortalColorId = Shader.PropertyToID("_PortalColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int BloomIntensityId = Shader.PropertyToID("_BloomIntensity");
        private static readonly int DistanceFadeId = Shader.PropertyToID("_DistanceFade");
        private static readonly int SolidityId = Shader.PropertyToID("_Solidity");

        private DroneCharacterController _drone;
        private Camera _camera;
        private BoostRingTrailTuning _tuning;
        private TrailRing[] _rings;
        private Mesh _mesh;
        private Material _material;
        private MaterialPropertyBlock _properties;
        private Transform _visualRoot;
        private Vector3 _visualCenterLocalPosition;
        private int _activeCount;
        private int _nextHueIndex;
        private bool _emitting;
        private readonly Vector3[] _controlPoints = new Vector3[4];
        private int _controlCount;
        private Vector3 _smoothedPosition;
        private Vector3 _lastRingUp;

        public void Initialize(
            DroneCharacterController drone,
            Camera targetCamera,
            Material portalMaterial,
            RingTuning portalTuning,
            BoostRingTrailTuning tuning)
        {
            _drone = drone;
            _camera = targetCamera;
            _material = portalMaterial != null ? new Material(portalMaterial) : null;
            if (_material != null)
            {
                _material.name = $"{portalMaterial.name} - Flight Stamina Boost Trail";
                _material.enableInstancing = true;
                _material.SetFloat(SolidityId, 1f);
            }
            _tuning = tuning;
            CacheVisualCenter();

            int capacity = tuning != null ? Mathf.Clamp(tuning.MaximumRingCount, 4, 2048) : 4;
            _rings = new TrailRing[capacity];
            _properties = new MaterialPropertyBlock();
            _mesh = tuning != null && portalTuning != null
                ? DuneVectorVisuals.GetInnermostPortalRingMesh(
                    tuning.Radius,
                    tuning.LineThicknessMultiplier,
                    portalTuning)
                : null;
            if (_drone != null && _drone.World != null)
            {
                _drone.World.WorldShifted += HandleWorldShift;
            }

            enabled = tuning != null &&
                tuning.Enabled &&
                _drone != null &&
                _camera != null &&
                _material != null &&
                _mesh != null;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            UpdateRings(deltaTime);

            bool shouldEmit = _drone != null &&
                _drone.CurrentMode == DroneTraversalMode.Flight &&
                _drone.IsBoosting;
            if (shouldEmit)
            {
                EmitAlongFlightPath(deltaTime);
            }
            else
            {
                _emitting = false;
            }

            RenderRings();
        }

        private void UpdateRings(float deltaTime)
        {
            int index = 0;
            while (index < _activeCount)
            {
                TrailRing ring = _rings[index];
                ring.Age += deltaTime;
                if (ring.Age >= ring.Lifetime)
                {
                    _activeCount--;
                    _rings[index] = _rings[_activeCount];
                    continue;
                }

                _rings[index] = ring;
                index++;
            }
        }

        private void EmitAlongFlightPath(float deltaTime)
        {
            Vector3 position = _visualRoot != null
                ? _visualRoot.TransformPoint(_visualCenterLocalPosition)
                : _drone.WorldCenter;
            Vector3 velocity = _drone.Motor != null ? _drone.Motor.Velocity : Vector3.zero;
            Vector3 direction = velocity.sqrMagnitude > 0.01f
                ? velocity.normalized
                : _drone.transform.forward;

            if (!_emitting)
            {
                _emitting = true;
                _smoothedPosition = position;
                _lastRingUp = Vector3.up;
                _controlCount = 0;
                _controlPoints[_controlCount++] = position;
                SpawnRing(position, direction);
                return;
            }

            float smoothingTime = Mathf.Max(0f, _tuning.PathSmoothingTime);
            _smoothedPosition = smoothingTime > 0f
                ? Vector3.Lerp(_smoothedPosition, position, 1f - Mathf.Exp(-deltaTime / smoothingTime))
                : position;

            float spacing = Mathf.Max(0.005f, _tuning.SpawnSpacing);
            float controlSpacing = spacing * Mathf.Clamp(_tuning.CurveControlSpacingMultiplier, 1, 32);
            Vector3 displacement = _smoothedPosition - _controlPoints[_controlCount - 1];
            float distance = displacement.magnitude;
            if (distance < controlSpacing)
            {
                return;
            }

            Vector3 segmentDirection = displacement / distance;
            int addedThisFrame = 0;
            while (distance >= controlSpacing && addedThisFrame < _rings.Length)
            {
                PushControlPoint(_controlPoints[_controlCount - 1] + (segmentDirection * controlSpacing), spacing);
                distance -= controlSpacing;
                addedThisFrame++;
            }
        }

        private void PushControlPoint(Vector3 point, float ringSpacing)
        {
            if (_controlCount < _controlPoints.Length)
            {
                _controlPoints[_controlCount++] = point;
            }
            else
            {
                _controlPoints[0] = _controlPoints[1];
                _controlPoints[1] = _controlPoints[2];
                _controlPoints[2] = _controlPoints[3];
                _controlPoints[3] = point;
            }

            if (_controlCount < _controlPoints.Length)
            {
                return;
            }

            EmitCurveSegment(
                _controlPoints[0],
                _controlPoints[1],
                _controlPoints[2],
                _controlPoints[3],
                ringSpacing);
        }

        private void EmitCurveSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float ringSpacing)
        {
            int steps = Mathf.Clamp(
                Mathf.RoundToInt((p2 - p1).magnitude / ringSpacing),
                1,
                _rings.Length);
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                SpawnRing(
                    EvaluateCatmullRom(p0, p1, p2, p3, t),
                    EvaluateCatmullRomTangent(p0, p1, p2, p3, t));
            }
        }

        private static Vector3 EvaluateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float tSquared = t * t;
            float tCubed = tSquared * t;
            return 0.5f * (
                (2f * p1) +
                ((-p0 + p2) * t) +
                (((2f * p0) - (5f * p1) + (4f * p2) - p3) * tSquared) +
                ((-p0 + (3f * p1) - (3f * p2) + p3) * tCubed));
        }

        private static Vector3 EvaluateCatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            Vector3 tangent = 0.5f * (
                (-p0 + p2) +
                ((((2f * p0) - (5f * p1) + (4f * p2) - p3) * 2f) * t) +
                (((-p0 + (3f * p1) - (3f * p2) + p3) * 3f) * t * t));
            return tangent.sqrMagnitude > 0.0000001f ? tangent.normalized : (p2 - p1).normalized;
        }

        private void SpawnRing(Vector3 sampledPosition, Vector3 direction)
        {
            int ringIndex;
            if (_activeCount < _rings.Length)
            {
                ringIndex = _activeCount++;
            }
            else
            {
                ringIndex = FindOldestRingIndex();
            }

            int hueSteps = Mathf.Clamp(_tuning.HueStepCount, 2, 1024);
            _rings[ringIndex] = new TrailRing
            {
                Position = sampledPosition - (direction * Mathf.Max(0f, _tuning.SpawnBehindDistance)),
                Rotation = CreateRingRotation(direction),
                Age = 0f,
                Lifetime = Mathf.Max(0.05f, _tuning.Lifetime),
                HueIndex = _nextHueIndex,
            };
            _nextHueIndex = (_nextHueIndex + 1) % hueSteps;
        }

        private int FindOldestRingIndex()
        {
            int oldestIndex = 0;
            float oldestAge = _rings[0].Age;
            for (int i = 1; i < _activeCount; i++)
            {
                if (_rings[i].Age <= oldestAge)
                {
                    continue;
                }

                oldestIndex = i;
                oldestAge = _rings[i].Age;
            }

            return oldestIndex;
        }

        private void RenderRings()
        {
            if (_activeCount == 0)
            {
                return;
            }

            int hueSteps = Mathf.Clamp(_tuning.HueStepCount, 2, 1024);
            float fadeInFraction = Mathf.Clamp(_tuning.FadeInFraction, 0.01f, 0.49f);
            float fadeOutFraction = Mathf.Clamp(_tuning.FadeOutFraction, 0.01f, 0.99f);
            RenderParams renderParams = new RenderParams(_material)
            {
                camera = _camera,
                layer = gameObject.layer,
                matProps = _properties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };

            for (int i = 0; i < _activeCount; i++)
            {
                TrailRing ring = _rings[i];
                float lifetime01 = Mathf.Clamp01(ring.Age / ring.Lifetime);
                float fadeIn = Mathf.SmoothStep(0f, 1f, lifetime01 / fadeInFraction);
                float fadeOut = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    (lifetime01 - (1f - fadeOutFraction)) / fadeOutFraction);
                float viewOpacity = CalculateViewOpacity(ring);
                float opacity = Mathf.Clamp01(_tuning.Opacity) * fadeIn * fadeOut * viewOpacity;
                float hue = Mathf.Repeat(
                    _tuning.StartingHue + (ring.HueIndex / (float)hueSteps),
                    1f);
                Color color = Color.HSVToRGB(hue, 1f, 1f, true) * Mathf.Max(0f, _tuning.ColorIntensity);
                color.a = 1f;

                float scale = Mathf.Lerp(
                    Mathf.Max(0.01f, _tuning.StartScale),
                    Mathf.Max(0.01f, _tuning.EndScale),
                    lifetime01);
                Quaternion rotation = ring.Rotation *
                    Quaternion.AngleAxis(_tuning.RotationSpeed * ring.Age, Vector3.forward);
                Matrix4x4 matrix = Matrix4x4.TRS(ring.Position, rotation, Vector3.one * scale);

                _properties.Clear();
                _properties.SetColor(PortalColorId, color);
                _properties.SetFloat(OpacityId, opacity);
                _properties.SetFloat(BloomIntensityId, Mathf.Max(0f, _tuning.BloomIntensity));
                _properties.SetFloat(DistanceFadeId, 1f);
                renderParams.worldBounds = DuneVectorSpatialInstancing.TransformBounds(matrix, _mesh.bounds);
                Graphics.RenderMesh(renderParams, _mesh, 0, matrix);
            }
        }

        private float CalculateViewOpacity(TrailRing ring)
        {
            Vector3 cameraOffset = _camera.transform.position - ring.Position;
            float cameraDistance = cameraOffset.magnitude;
            float hiddenDistance = Mathf.Max(0f, _tuning.NearCameraHiddenDistance);
            float fadeEndDistance = Mathf.Max(hiddenDistance + 0.01f, _tuning.NearCameraFadeEndDistance);
            float distanceVisibility = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(hiddenDistance, fadeEndDistance, cameraDistance));
            if (distanceVisibility <= 0f || cameraDistance <= Mathf.Epsilon)
            {
                return 0f;
            }

            Vector3 ringNormal = ring.Rotation * Vector3.forward;
            float faceOnAlignment = Mathf.Clamp01(
                Mathf.Abs(Vector3.Dot(ringNormal, cameraOffset / cameraDistance)));
            float viewAngle = Mathf.Acos(faceOnAlignment) * Mathf.Rad2Deg;
            float fadeStart = Mathf.Clamp(_tuning.HeadOnFadeStartAngle, 0f, 89f);
            float fadeEnd = Mathf.Clamp(_tuning.HeadOnFadeEndAngle, fadeStart + 0.01f, 90f);
            float angledVisibility = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(fadeStart, fadeEnd, viewAngle));
            float angleVisibility = Mathf.Lerp(
                Mathf.Clamp01(_tuning.HeadOnOpacityMultiplier),
                1f,
                angledVisibility);
            return distanceVisibility * angleVisibility;
        }

        private Quaternion CreateRingRotation(Vector3 direction)
        {
            // Carry the previous ring's up vector forward so the ribbon never rolls
            // abruptly when the flight path passes through vertical.
            Vector3 reference = _lastRingUp.sqrMagnitude > 0.0001f ? _lastRingUp : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(direction, reference)) > 0.98f)
            {
                reference = _drone.transform.right;
            }

            Vector3 up = Vector3.ProjectOnPlane(reference, direction);
            if (up.sqrMagnitude <= 0.0001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, direction);
            }

            _lastRingUp = up.normalized;
            return Quaternion.LookRotation(direction, _lastRingUp);
        }

        private void CacheVisualCenter()
        {
            _visualRoot = _drone != null ? _drone.DroneVisualRoot : null;
            _visualCenterLocalPosition = Vector3.zero;
            if (_visualRoot == null)
            {
                return;
            }

            Renderer[] renderers = _visualRoot.GetComponentsInChildren<Renderer>(true);
            Bounds combinedBounds = default;
            bool hasBounds = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Bounds sourceBounds;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    sourceBounds = skinnedRenderer.localBounds;
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }

                    sourceBounds = filter.sharedMesh.bounds;
                }
                else
                {
                    continue;
                }

                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 corner = sourceBounds.center + Vector3.Scale(
                        sourceBounds.extents,
                        new Vector3(
                            (cornerIndex & 1) == 0 ? -1f : 1f,
                            (cornerIndex & 2) == 0 ? -1f : 1f,
                            (cornerIndex & 4) == 0 ? -1f : 1f));
                    Vector3 localCorner = _visualRoot.InverseTransformPoint(
                        renderer.transform.TransformPoint(corner));
                    if (!hasBounds)
                    {
                        combinedBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(localCorner);
                    }
                }
            }

            if (hasBounds)
            {
                _visualCenterLocalPosition = combinedBounds.center;
            }
        }

        private void HandleWorldShift(Vector3 worldShift)
        {
            for (int i = 0; i < _activeCount; i++)
            {
                TrailRing ring = _rings[i];
                ring.Position += worldShift;
                _rings[i] = ring;
            }

            if (!_emitting)
            {
                return;
            }

            _smoothedPosition += worldShift;
            for (int i = 0; i < _controlCount; i++)
            {
                _controlPoints[i] += worldShift;
            }
        }

        private void OnDisable()
        {
            _activeCount = 0;
            _emitting = false;
            _controlCount = 0;
        }

        private void OnDestroy()
        {
            if (_drone != null && _drone.World != null)
            {
                _drone.World.WorldShifted -= HandleWorldShift;
            }

            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
