using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace DuneVector
{
    public static class DuneVectorMusicGlitchRuntime
    {
        public static float Intensity { get; private set; }

        public static void SetIntensity(float intensity)
        {
            Intensity = Mathf.Clamp01(intensity);
        }

        public static void Reset()
        {
            Intensity = 0f;
        }
    }

    public sealed class DuneVectorMusicWorldGlitchFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material material;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        private WorldGlitchPass _pass;

        public override void Create()
        {
            _pass = new WorldGlitchPass
            {
                renderPassEvent = injectionPoint,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null
                || DuneVectorMusicGlitchRuntime.Intensity <= 0f
                || renderingData.cameraData.cameraType != CameraType.Game
                || renderingData.cameraData.renderType != CameraRenderType.Base)
            {
                return;
            }
            _pass.Setup(material);
            renderer.EnqueuePass(_pass);
        }

        private sealed class WorldGlitchPass : ScriptableRenderPass
        {
            private static readonly ProfilerMarker RecordMarker = new ProfilerMarker("MusicVisualizer.URPGlitchRecord");
            private Material _material;

            public void Setup(Material passMaterial)
            {
                _material = passMaterial;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || DuneVectorMusicGlitchRuntime.Intensity <= 0f)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                {
                    return;
                }

                using (RecordMarker.Auto())
                {
                    TextureHandle source = resources.activeColorTexture;
                    TextureDesc destinationDescriptor = renderGraph.GetTextureDesc(source);
                    destinationDescriptor.name = "DuneVector Music World Glitch Color";
                    destinationDescriptor.clearBuffer = false;
                    TextureHandle destination = renderGraph.CreateTexture(destinationDescriptor);
                    RenderGraphUtils.BlitMaterialParameters parameters = new RenderGraphUtils.BlitMaterialParameters(
                        source,
                        destination,
                        _material,
                        0);
                    renderGraph.AddBlitPass(parameters, "MusicVisualizer.WorldGlitch");
                    resources.cameraColor = destination;
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicWorldGlitchSink : MonoBehaviour, IMusicReactiveSink
    {
        private static readonly int ParametersId = Shader.PropertyToID("_DVMusicGlitchParameters");
        private static readonly int SafetyId = Shader.PropertyToID("_DVMusicGlitchSafety");
        private static readonly int TintId = Shader.PropertyToID("_DVMusicGlitchTint");

        private MusicReactiveSkyTuning _settings;
        private float _age;
        private float _strength;
        private uint _seed;
        private int _accentSnareCount;

        public float Intensity => DuneVectorMusicGlitchRuntime.Intensity;

        public void Initialize(MusicReactiveSkyTuning settings)
        {
            _settings = settings;
            ResetMusicResponse();
        }

        public void ApplyContinuous(in MusicReactiveRuntimeState state)
        {
        }

        public void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            if ((command.AllowedEffects & MusicVisualEffectGroups.Glitch) == 0
                || state.VisualTier < _settings.WorldGlitchMinimumVisualTier)
            {
                return;
            }

            bool accepted = false;
            if (command.Type == MusicVisualCueType.AccentSnare)
            {
                _accentSnareCount++;
                accepted = _accentSnareCount % Mathf.Max(1, _settings.AccentSnaresPerGlitch) == 0;
            }
            else if (command.Type == MusicVisualCueType.ReactorDischarge && command.IsAuthored)
            {
                accepted = true;
            }
            if (!accepted)
            {
                return;
            }

            _age = 0f;
            _strength = Mathf.Clamp01(command.Strength);
            _seed = command.DeterministicSeed;
            ApplyGlobals(_settings.WorldGlitchMaximumIntensity * _strength);
        }

        private void Update()
        {
            if (_settings == null || _strength <= 0f)
            {
                return;
            }
            _age += Time.unscaledDeltaTime;
            float duration = Mathf.Max(0.01f, _settings.WorldGlitchDurationSeconds);
            float normalizedAge = Mathf.Clamp01(_age / duration);
            float envelope = (1f - normalizedAge) * (1f - normalizedAge);
            ApplyGlobals(_settings.WorldGlitchMaximumIntensity * _strength * envelope);
            if (normalizedAge >= 1f)
            {
                _strength = 0f;
                DuneVectorMusicGlitchRuntime.Reset();
            }
        }

        private void ApplyGlobals(float intensity)
        {
            DuneVectorMusicGlitchRuntime.SetIntensity(intensity);
            Shader.SetGlobalVector(
                ParametersId,
                new Vector4(
                    intensity,
                    (_seed & 0x00FFFFFFu) / 16777216f,
                    _settings.WorldGlitchSliceCount,
                    _settings.WorldGlitchHorizontalShift));
            Shader.SetGlobalVector(
                SafetyId,
                new Vector4(
                    _settings.WorldGlitchProtectedHalfWidth,
                    _settings.WorldGlitchProtectedHalfHeight,
                    _settings.WorldGlitchProtectedFeather,
                    _settings.WorldGlitchProtectedIntensityMultiplier));
            Shader.SetGlobalColor(TintId, _settings.WorldGlitchTint);
        }

        public void ResetMusicResponse()
        {
            _age = 0f;
            _strength = 0f;
            _seed = 0u;
            _accentSnareCount = 0;
            DuneVectorMusicGlitchRuntime.Reset();
            Shader.SetGlobalVector(ParametersId, Vector4.zero);
            Shader.SetGlobalVector(SafetyId, Vector4.zero);
            Shader.SetGlobalColor(TintId, Color.clear);
        }

        private void OnDisable()
        {
            ResetMusicResponse();
        }
    }
}
