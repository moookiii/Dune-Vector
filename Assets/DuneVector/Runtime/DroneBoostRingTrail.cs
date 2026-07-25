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
        private Vector3 _lastEmissionPosition;
        private bool _emitting;

        public void Initialize(
            DroneCharacterController drone,
            Camera targetCamera,
            Material portalMaterial,
            RingTuning portalTuning,
            BoostRingTrailTuning tuning)
        {
            _drone = drone;
            _camera = targetCamera;
            _material = portalMaterial;
            _tuning = tuning;
            CacheVisualCenter();

            int capacity = tuning != null ? Mathf.Clamp(tuning.MaximumRingCount, 4, 128) : 4;
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
                EmitAlongFlightPath();
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

        private void EmitAlongFlightPath()
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
                _lastEmissionPosition = position;
                SpawnRing(position, direction);
                return;
            }

            float spacing = Mathf.Max(0.1f, _tuning.SpawnSpacing);
            Vector3 displacement = position - _lastEmissionPosition;
            float distance = displacement.magnitude;
            if (distance < spacing)
            {
                return;
            }

            Vector3 segmentDirection = displacement / distance;
            int emittedThisFrame = 0;
            while (distance >= spacing && emittedThisFrame < _rings.Length)
            {
                _lastEmissionPosition += segmentDirection * spacing;
                SpawnRing(_lastEmissionPosition, segmentDirection);
                distance -= spacing;
                emittedThisFrame++;
            }

            if (distance >= spacing)
            {
                _lastEmissionPosition = position;
            }
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

            int hueSteps = Mathf.Clamp(_tuning.HueStepCount, 2, 64);
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

            int hueSteps = Mathf.Clamp(_tuning.HueStepCount, 2, 64);
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
                float opacity = Mathf.Clamp01(_tuning.Opacity) * fadeIn * fadeOut;
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

        private Quaternion CreateRingRotation(Vector3 direction)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f
                ? _drone.transform.right
                : Vector3.up;
            return Quaternion.LookRotation(direction, up);
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

            if (_emitting)
            {
                _lastEmissionPosition += worldShift;
            }
        }

        private void OnDisable()
        {
            _activeCount = 0;
            _emitting = false;
        }

        private void OnDestroy()
        {
            if (_drone != null && _drone.World != null)
            {
                _drone.World.WorldShifted -= HandleWorldShift;
            }
        }
    }
}
