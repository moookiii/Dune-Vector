using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class DroneFlightSwooshRenderer : MonoBehaviour
    {
        private struct Streak
        {
            public Vector3 Position;
            public Vector3 Direction;
            public Vector2 DirectionJitter;
            public float Length;
            public float Width;
            public float SweepSpeed;
            public float Lifetime;
            public float Age;
            public float Brightness;
        }

        private static readonly int SwooshColorId = Shader.PropertyToID("_SwooshColor");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int TipSoftnessId = Shader.PropertyToID("_TipSoftness");

        private DroneCharacterController _drone;
        private Camera _camera;
        private FlightSwooshTuning _tuning;
        private Streak[] _streaks;
        private Matrix4x4[] _matrices;
        private Vector4[] _colors;
        private Mesh _mesh;
        private Material _material;
        private MaterialPropertyBlock _propertyBlock;
        private int _activeCount;
        private float _intensity;
        private float _spawnCountdown;
        private uint _randomState;
        private bool _initialized;

        public void Initialize(DroneCharacterController drone, Camera targetCamera, FlightSwooshTuning tuning)
        {
            _drone = drone;
            _camera = targetCamera;
            _tuning = tuning;

            int capacity = tuning != null ? Mathf.Clamp(tuning.MaximumStreakCount, 8, 256) : 8;
            _streaks = new Streak[capacity];
            _matrices = new Matrix4x4[capacity];
            _colors = new Vector4[capacity];
            _propertyBlock = new MaterialPropertyBlock();
            _randomState = unchecked((uint)(GetEntityId().GetHashCode() * 747796405)) | 1u;

            CreateRenderResources();
            _initialized = _mesh != null && _material != null;
            enabled = _initialized && tuning != null && tuning.Enabled;
        }

        private void LateUpdate()
        {
            if (!_initialized || _drone == null || _camera == null || _tuning == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 velocity = _drone.Motor != null ? _drone.Motor.Velocity : Vector3.zero;
            float speed = velocity.magnitude;
            bool isFlying = _drone.CurrentMode == DroneTraversalMode.Flight;
            float speedRange = Mathf.Max(0.01f, _tuning.MaximumIntensitySpeed - _tuning.SpeedThreshold);
            float speed01 = Mathf.Clamp01((speed - _tuning.SpeedThreshold) / speedRange);
            float targetIntensity = isFlying
                ? Mathf.Pow(speed01, Mathf.Max(0.01f, _tuning.DensityCurvePower))
                : 0f;

            if (_drone.IsBoosting || _drone.IsRingBoosting)
            {
                targetIntensity *= Mathf.Max(0f, _tuning.BoostMultiplier);
            }

            _intensity = Mathf.Lerp(
                _intensity,
                targetIntensity,
                DuneVectorMath.Sharpness(_tuning.IntensitySharpness, deltaTime));

            Vector3 movementDirection = speed > 0.01f ? velocity / speed : _camera.transform.forward;
            UpdateStreaks(deltaTime, movementDirection);
            SpawnStreaks(deltaTime, movementDirection);
            RenderStreaks();
        }

        private void UpdateStreaks(float deltaTime, Vector3 movementDirection)
        {
            Vector3 cameraRight = _camera.transform.right;
            Vector3 cameraUp = _camera.transform.up;
            float alignmentBlend = DuneVectorMath.Sharpness(_tuning.MovementAlignmentSharpness, deltaTime);

            int index = 0;
            while (index < _activeCount)
            {
                Streak streak = _streaks[index];
                streak.Age += deltaTime;
                if (streak.Age >= streak.Lifetime)
                {
                    _activeCount--;
                    _streaks[index] = _streaks[_activeCount];
                    continue;
                }

                Vector3 desiredDirection = -movementDirection
                    + (cameraRight * streak.DirectionJitter.x)
                    + (cameraUp * streak.DirectionJitter.y);
                desiredDirection.Normalize();
                streak.Direction = Vector3.Slerp(streak.Direction, desiredDirection, alignmentBlend).normalized;
                streak.Position += streak.Direction * (streak.SweepSpeed * deltaTime);
                _streaks[index] = streak;
                index++;
            }
        }

        private void SpawnStreaks(float deltaTime, Vector3 movementDirection)
        {
            float spawnRate = Mathf.Max(0f, _tuning.Density) * _intensity;
            if (spawnRate <= 0.001f || _activeCount >= _streaks.Length)
            {
                return;
            }

            _spawnCountdown -= deltaTime;
            while (_spawnCountdown <= 0f && _activeCount < _streaks.Length)
            {
                SpawnStreak(movementDirection, Mathf.Clamp01(_intensity));
                float timingScale = Mathf.Lerp(
                    1f - Mathf.Clamp01(_tuning.TimingVariation),
                    1f + Mathf.Clamp01(_tuning.TimingVariation),
                    Next01());
                _spawnCountdown += timingScale / spawnRate;
            }
        }

        private void SpawnStreak(Vector3 movementDirection, float shapeIntensity)
        {
            float angle = Next01() * Mathf.PI * 2f;
            Vector2 radiusRange = OrderedNonNegative(_tuning.SpawnRadiusRange);
            float radius = Mathf.Lerp(radiusRange.x, radiusRange.y, Next01());
            Vector3 viewportPosition = new Vector3(
                0.5f + (Mathf.Cos(angle) * radius),
                0.5f + (Mathf.Sin(angle) * radius),
                RandomRange(OrderedNonNegative(_tuning.SpawnDepthRange)));

            float jitterScale = Mathf.Tan(Mathf.Clamp(_tuning.DirectionJitterDegrees, 0f, 12f) * Mathf.Deg2Rad);
            Vector2 jitter = RandomInsideUnitCircle() * jitterScale;
            Vector3 direction = (-movementDirection
                + (_camera.transform.right * jitter.x)
                + (_camera.transform.up * jitter.y)).normalized;

            Vector2 lengthRange = OrderedNonNegative(_tuning.LengthRange);
            float randomLength = Mathf.Lerp(lengthRange.x, lengthRange.y, Next01());
            float intensityLength = Mathf.Lerp(lengthRange.x, lengthRange.y, shapeIntensity);
            Vector2 speedRange = OrderedNonNegative(_tuning.SweepSpeedRange);
            float randomSpeed = Mathf.Lerp(speedRange.x, speedRange.y, Next01());
            float intensitySpeed = Mathf.Lerp(speedRange.x, speedRange.y, shapeIntensity);

            _streaks[_activeCount++] = new Streak
            {
                Position = _camera.ViewportToWorldPoint(viewportPosition),
                Direction = direction,
                DirectionJitter = jitter,
                Length = Mathf.Lerp(randomLength, intensityLength, shapeIntensity),
                Width = RandomRange(OrderedNonNegative(_tuning.WidthRange)),
                SweepSpeed = Mathf.Lerp(randomSpeed, intensitySpeed, shapeIntensity),
                Lifetime = Mathf.Max(0.01f, RandomRange(OrderedNonNegative(_tuning.LifetimeRange))),
                Age = 0f,
                Brightness = Mathf.Lerp(
                    1f - Mathf.Clamp01(_tuning.BrightnessVariation),
                    1f + Mathf.Clamp01(_tuning.BrightnessVariation),
                    Next01()),
            };
        }

        private void RenderStreaks()
        {
            if (_activeCount == 0)
            {
                return;
            }

            Vector3 cameraPosition = _camera.transform.position;
            Color baseColor = _tuning.Color;
            float globalAlpha = Mathf.Clamp01(_intensity) * Mathf.Clamp01(_tuning.Opacity);

            for (int i = 0; i < _activeCount; i++)
            {
                Streak streak = _streaks[i];
                Vector3 toCamera = cameraPosition - streak.Position;
                Vector3 right = Vector3.Cross(streak.Direction, toCamera);
                if (right.sqrMagnitude <= 0.0001f)
                {
                    right = _camera.transform.right;
                }
                else
                {
                    right.Normalize();
                }

                Vector3 normal = Vector3.Cross(right, streak.Direction).normalized;
                float lifetime01 = Mathf.Clamp01(streak.Age / streak.Lifetime);
                float fadeIn = Mathf.SmoothStep(
                    0f,
                    1f,
                    lifetime01 / Mathf.Max(0.01f, _tuning.FadeInFraction));
                float fadeOut = 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    (lifetime01 - (1f - _tuning.FadeOutFraction)) / Mathf.Max(0.01f, _tuning.FadeOutFraction));
                float alpha = globalAlpha * fadeIn * fadeOut;

                _matrices[i] = CreateMatrix(
                    streak.Position,
                    right * streak.Width,
                    normal,
                    streak.Direction * streak.Length);
                _colors[i] = new Vector4(
                    baseColor.r * streak.Brightness,
                    baseColor.g * streak.Brightness,
                    baseColor.b * streak.Brightness,
                    baseColor.a * alpha);
            }

            _propertyBlock.Clear();
            _propertyBlock.SetVectorArray(SwooshColorId, _colors);
            Graphics.DrawMeshInstanced(
                _mesh,
                0,
                _material,
                _matrices,
                _activeCount,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                gameObject.layer,
                _camera,
                LightProbeUsage.Off);
        }

        private void CreateRenderResources()
        {
            Shader shader = Resources.Load<Shader>("DuneVectorFlightSwoosh");
            if (shader == null)
            {
                Debug.LogError("Dune Vector flight swoosh shader could not be loaded.", this);
                return;
            }

            _material = new Material(shader)
            {
                name = "Dune Vector Flight Swoosh (Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            _material.SetFloat(EdgeSoftnessId, Mathf.Clamp(_tuning.EdgeSoftness, 0.01f, 0.49f));
            _material.SetFloat(TipSoftnessId, Mathf.Clamp(_tuning.TipSoftness, 0.01f, 0.49f));

            _mesh = new Mesh
            {
                name = "Dune Vector Flight Swoosh Quad",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
            });
            _mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            });
            _mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
            _mesh.UploadMeshData(true);
        }

        private void OnDisable()
        {
            _activeCount = 0;
            _intensity = 0f;
            _spawnCountdown = 0f;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        private static Matrix4x4 CreateMatrix(Vector3 position, Vector3 right, Vector3 up, Vector3 forward)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(right.x, right.y, right.z, 0f));
            matrix.SetColumn(1, new Vector4(up.x, up.y, up.z, 0f));
            matrix.SetColumn(2, new Vector4(forward.x, forward.y, forward.z, 0f));
            matrix.SetColumn(3, new Vector4(position.x, position.y, position.z, 1f));
            return matrix;
        }

        private static Vector2 OrderedNonNegative(Vector2 range)
        {
            float minimum = Mathf.Max(0f, Mathf.Min(range.x, range.y));
            float maximum = Mathf.Max(minimum, Mathf.Max(range.x, range.y));
            return new Vector2(minimum, maximum);
        }

        private float RandomRange(Vector2 range)
        {
            return Mathf.Lerp(range.x, range.y, Next01());
        }

        private Vector2 RandomInsideUnitCircle()
        {
            float angle = Next01() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Next01());
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private float Next01()
        {
            uint value = _randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _randomState = value;
            return (value & 0x00ffffffu) / 16777216f;
        }
    }
}
