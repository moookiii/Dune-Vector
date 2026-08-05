using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorPerspectivePressureFronts : MonoBehaviour, IMusicReactiveSink
    {
        private static readonly ProfilerMarker VfxDispatchMarker = new ProfilerMarker("MusicVisualizer.VFXDispatch");

        private struct FrontSlot
        {
            public LineRenderer Renderer;
            public Vector3 Origin;
            public Vector3 Forward;
            public Vector3 Right;
            public float BaseHeight;
            public float Age;
            public float Delay;
            public float Strength;
            public bool Reactor;
            public bool Active;
        }

        private Camera _camera;
        private MusicReactiveSkyTuning _settings;
        private FrontSlot[] _ordinary;
        private FrontSlot[] _reactor;
        private Vector3[] _positions;
        private int _ordinaryCursor;
        private int _reactorCursor;
        private int _droppedFronts;

        public int ActiveOrdinaryCount => CountActive(_ordinary);
        public int ActiveReactorCount => CountActive(_reactor);
        public int DroppedFrontCount => _droppedFronts;

        public void Initialize(Camera camera, Material sharedMaterial, MusicReactiveSkyTuning settings)
        {
            _camera = camera;
            _settings = settings;
            int segmentCount = Mathf.Max(2, settings.PressureFrontSegments);
            _positions = new Vector3[segmentCount];
            _ordinary = BuildPool("Ordinary Pressure Front", settings.OrdinaryPressureFrontPoolSize, sharedMaterial);
            _reactor = BuildPool("Reactor Pressure Front", settings.ReactorPressureFrontPoolSize, sharedMaterial);
        }

        private FrontSlot[] BuildPool(string label, int count, Material sharedMaterial)
        {
            FrontSlot[] pool = new FrontSlot[Mathf.Max(1, count)];
            for (int i = 0; i < pool.Length; i++)
            {
                GameObject frontObject = new GameObject($"{label} {i + 1}");
                frontObject.transform.SetParent(transform, false);
                LineRenderer line = frontObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = false;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.positionCount = _positions.Length;
                line.sharedMaterial = sharedMaterial;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.enabled = false;
                pool[i].Renderer = line;
            }
            return pool;
        }

        public void ApplyContinuous(in MusicReactiveRuntimeState state)
        {
        }

        public void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            if ((command.AllowedEffects & MusicVisualEffectGroups.PressureFront) == 0)
            {
                return;
            }

            using (VfxDispatchMarker.Auto())
            {
                if (command.Type == MusicVisualCueType.MajorKick)
                {
                    TryEmit(_ordinary, ref _ordinaryCursor, command.Strength, false, 0f);
                }
                else if (command.Type == MusicVisualCueType.ReactorDischarge && command.IsAuthored)
                {
                    int count = Mathf.Min(_reactor.Length, _settings.ReactorPressureFrontPoolSize);
                    for (int i = 0; i < count; i++)
                    {
                        TryEmit(
                            _reactor,
                            ref _reactorCursor,
                            command.Strength,
                            true,
                            i * _settings.ReactorFrontStaggerSeconds);
                    }
                }
            }
        }

        private void TryEmit(FrontSlot[] pool, ref int cursor, float strength, bool reactor, float delay)
        {
            if (_camera == null || pool == null || pool.Length == 0)
            {
                return;
            }

            int selected = -1;
            for (int offset = 0; offset < pool.Length; offset++)
            {
                int index = (cursor + offset) % pool.Length;
                if (!pool[index].Active)
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0)
            {
                _droppedFronts++;
                return;
            }

            Transform cameraTransform = _camera.transform;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= Mathf.Epsilon)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            FrontSlot slot = pool[selected];
            slot.Origin = cameraTransform.position;
            slot.Forward = forward;
            slot.Right = Vector3.Cross(Vector3.up, forward).normalized;
            slot.BaseHeight = cameraTransform.position.y - _settings.PressureFrontCameraHeightOffset;
            slot.Age = 0f;
            slot.Delay = Mathf.Max(0f, delay);
            slot.Strength = Mathf.Clamp01(strength);
            slot.Reactor = reactor;
            slot.Active = true;
            slot.Renderer.enabled = delay <= 0f;
            pool[selected] = slot;
            cursor = (selected + 1) % pool.Length;
        }

        private void Update()
        {
            if (_settings == null)
            {
                return;
            }
            float deltaTime = Time.unscaledDeltaTime;
            UpdatePool(_ordinary, deltaTime);
            UpdatePool(_reactor, deltaTime);
        }

        private void UpdatePool(FrontSlot[] pool, float deltaTime)
        {
            if (pool == null)
            {
                return;
            }
            for (int i = 0; i < pool.Length; i++)
            {
                FrontSlot slot = pool[i];
                if (!slot.Active)
                {
                    continue;
                }
                if (slot.Delay > 0f)
                {
                    slot.Delay -= deltaTime;
                    if (slot.Delay > 0f)
                    {
                        pool[i] = slot;
                        continue;
                    }
                    slot.Renderer.enabled = true;
                }

                slot.Age += deltaTime;
                float progress = Mathf.Clamp01(slot.Age / Mathf.Max(0.01f, _settings.PressureFrontDurationSeconds));
                if (progress >= 1f)
                {
                    Deactivate(ref slot);
                    pool[i] = slot;
                    continue;
                }

                float distance = Mathf.Lerp(_settings.PressureFrontStartDistance, _settings.PressureFrontEndDistance, progress * progress);
                float widthMultiplier = slot.Reactor ? _settings.ReactorFrontWidthMultiplier : 1f;
                float halfWidth = _settings.PressureFrontWidth * widthMultiplier * 0.5f;
                for (int segment = 0; segment < _positions.Length; segment++)
                {
                    float normalized = _positions.Length > 1 ? segment / (float)(_positions.Length - 1) : 0f;
                    float lateral = Mathf.Lerp(-halfWidth, halfWidth, normalized);
                    float arc = (1f - Mathf.Pow(normalized * 2f - 1f, 2f)) * _settings.PressureFrontArcDepth;
                    _positions[segment] = slot.Origin
                        + slot.Forward * (distance + arc)
                        + slot.Right * lateral;
                    _positions[segment].y = slot.BaseHeight;
                }
                slot.Renderer.SetPositions(_positions);
                slot.Renderer.startWidth = Mathf.Lerp(_settings.PressureFrontStartWidth, _settings.PressureFrontEndWidth, progress);
                slot.Renderer.endWidth = slot.Renderer.startWidth;
                float nearFade = 1f - Mathf.InverseLerp(_settings.PressureFrontNearFadeStart, 1f, progress);
                float alpha = _settings.PressureFrontMaximumAlpha * slot.Strength * nearFade;
                Color color = _settings.PressureFrontColor;
                color.a = alpha;
                slot.Renderer.startColor = color;
                slot.Renderer.endColor = color;
                pool[i] = slot;
            }
        }

        public void ResetMusicResponse()
        {
            ResetPool(_ordinary);
            ResetPool(_reactor);
        }

        private static void ResetPool(FrontSlot[] pool)
        {
            if (pool == null)
            {
                return;
            }
            for (int i = 0; i < pool.Length; i++)
            {
                FrontSlot slot = pool[i];
                Deactivate(ref slot);
                pool[i] = slot;
            }
        }

        private static void Deactivate(ref FrontSlot slot)
        {
            slot.Active = false;
            slot.Age = 0f;
            slot.Delay = 0f;
            if (slot.Renderer != null)
            {
                slot.Renderer.enabled = false;
            }
        }

        private static int CountActive(FrontSlot[] pool)
        {
            int count = 0;
            if (pool == null)
            {
                return count;
            }
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].Active)
                {
                    count++;
                }
            }
            return count;
        }

        private void OnDisable()
        {
            ResetMusicResponse();
        }
    }
}
