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
            public LineRenderer HaloRenderer;
            public Vector3 Origin;
            public Vector3 Forward;
            public Vector3 Right;
            public float BaseHeight;
            public float Age;
            public float Delay;
            public float Strength;
            public float CoreMultiplier;
            public float Duration;
            public float EdgeBreakup;
            public float LateralOffset;
            public uint Seed;
            public Color Color;
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
                GameObject haloObject = new GameObject("Halo");
                haloObject.transform.SetParent(frontObject.transform, false);
                LineRenderer halo = haloObject.AddComponent<LineRenderer>();
                halo.useWorldSpace = true;
                halo.loop = false;
                halo.alignment = LineAlignment.View;
                halo.textureMode = LineTextureMode.Stretch;
                halo.positionCount = _positions.Length;
                halo.sharedMaterial = sharedMaterial;
                halo.shadowCastingMode = ShadowCastingMode.Off;
                halo.receiveShadows = false;
                halo.lightProbeUsage = LightProbeUsage.Off;
                halo.reflectionProbeUsage = ReflectionProbeUsage.Off;
                halo.enabled = false;
                pool[i].Renderer = line;
                pool[i].HaloRenderer = halo;
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
                if (!command.IsAuthored)
                {
                    return;
                }
                MusicVisualFrontKind frontKind = command.FrontKind;
                if (frontKind == MusicVisualFrontKind.None)
                {
                    if (command.Type == MusicVisualCueType.MinorKick
                        || command.Type == MusicVisualCueType.MajorKick)
                    {
                        frontKind = MusicVisualFrontKind.Ordinary;
                    }
                    else if (command.Type == MusicVisualCueType.ReactorDischarge)
                    {
                        frontKind = MusicVisualFrontKind.Reactor;
                    }
                    else
                    {
                        return;
                    }
                }
                bool reactor = frontKind == MusicVisualFrontKind.Reactor;
                FrontSlot[] pool = reactor ? _reactor : _ordinary;
                ref int cursor = ref (reactor ? ref _reactorCursor : ref _ordinaryCursor);
                int requestedCount = command.FrontArcCount > 0
                    ? command.FrontArcCount
                    : reactor ? _settings.ReactorPressureFrontPoolSize : 1;
                int count = Mathf.Clamp(requestedCount, 1, pool.Length);
                for (int i = 0; i < count; i++)
                {
                    float lateralOffset = count > 1
                        ? Mathf.Lerp(
                            -_settings.SplitFrontLateralOffset,
                            _settings.SplitFrontLateralOffset,
                            i / (float)(count - 1))
                        : 0f;
                    TryEmit(
                        pool,
                        ref cursor,
                        command.Strength,
                        command.FrontStrengthMultiplier > 0f ? command.FrontStrengthMultiplier : 1f,
                        reactor,
                        i * _settings.ReactorFrontStaggerSeconds,
                        command.FrontTravelSeconds,
                        command.FrontEdgeBreakup,
                        lateralOffset,
                        command.FrontColor,
                        command.DeterministicSeed + (uint)i);
                }
            }
        }

        private void TryEmit(
            FrontSlot[] pool,
            ref int cursor,
            float strength,
            float coreMultiplier,
            bool reactor,
            float delay,
            float duration,
            float edgeBreakup,
            float lateralOffset,
            Color color,
            uint seed)
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
            slot.BaseHeight = ResolveBaseHeight(cameraTransform);
            slot.Age = 0f;
            slot.Delay = Mathf.Max(0f, delay);
            slot.Strength = Mathf.Clamp01(strength);
            slot.CoreMultiplier = Mathf.Max(0f, coreMultiplier);
            slot.Duration = duration > 0f ? duration : _settings.PressureFrontDurationSeconds;
            slot.EdgeBreakup = Mathf.Clamp01(edgeBreakup);
            slot.LateralOffset = lateralOffset;
            slot.Seed = seed;
            slot.Color = color.maxColorComponent > 0f ? color : _settings.PressureFrontColor;
            slot.Reactor = reactor;
            slot.Active = true;
            slot.Renderer.enabled = delay <= 0f;
            slot.HaloRenderer.enabled = delay <= 0f;
            pool[selected] = slot;
            cursor = (selected + 1) % pool.Length;
        }

        private float ResolveBaseHeight(Transform cameraTransform)
        {
            float fallback = cameraTransform.position.y - _settings.PressureFrontCameraHeightOffset;
            Vector3 probeOrigin = cameraTransform.position + Vector3.up * _settings.PressureFrontGroundProbeHeight;
            if (Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    _settings.PressureFrontGroundProbeDistance,
                    _settings.PressureFrontGroundProbeLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(fallback, hit.point.y + _settings.PressureFrontGroundClearance);
            }
            return fallback;
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
                    slot.HaloRenderer.enabled = true;
                }

                slot.Age += deltaTime;
                float progress = Mathf.Clamp01(slot.Age / Mathf.Max(0.01f, slot.Duration));
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
                    float breakup = (Hash01(slot.Seed + (uint)segment) * 2f - 1f)
                        * slot.EdgeBreakup
                        * _settings.PressureFrontArcDepth;
                    _positions[segment] = slot.Origin
                        + slot.Forward * (distance + arc)
                        + slot.Right * (lateral + slot.LateralOffset * halfWidth);
                    _positions[segment].y = slot.BaseHeight
                        + (1f - progress) * _settings.PressureFrontHorizonHeight
                        + breakup;
                }
                slot.Renderer.SetPositions(_positions);
                slot.HaloRenderer.SetPositions(_positions);
                slot.Renderer.startWidth = Mathf.Lerp(_settings.PressureFrontStartWidth, _settings.PressureFrontEndWidth, progress);
                slot.Renderer.endWidth = slot.Renderer.startWidth;
                slot.HaloRenderer.startWidth = slot.Renderer.startWidth * _settings.PressureFrontArrivalThicknessGrowth;
                slot.HaloRenderer.endWidth = slot.HaloRenderer.startWidth;
                float nearFade = 1f - Mathf.InverseLerp(_settings.PressureFrontNearFadeStart, 1f, progress);
                float alpha = _settings.PressureFrontMaximumAlpha * slot.Strength * nearFade;
                Color color = slot.Color;
                float colorAlpha = color.a;
                color *= slot.CoreMultiplier;
                color.a = colorAlpha;
                color.a = alpha;
                slot.Renderer.startColor = color;
                slot.Renderer.endColor = color;
                Color haloColor = _settings.PressureFrontHaloColor;
                haloColor.a = alpha * _settings.PressureFrontHaloIntensityMultiplier;
                slot.HaloRenderer.startColor = haloColor;
                slot.HaloRenderer.endColor = haloColor;
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
            if (slot.HaloRenderer != null)
            {
                slot.HaloRenderer.enabled = false;
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

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777216f;
        }

        private void OnDisable()
        {
            ResetMusicResponse();
        }
    }
}
