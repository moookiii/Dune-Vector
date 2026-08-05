using UnityEngine;

namespace DuneVector
{
    public static class DuneVectorMusicGlitchRuntime
    {
        private static int _availableFeatureCount;

        public static float Intensity { get; private set; }
        public static bool FeatureAvailable => _availableFeatureCount > 0;

        internal static void RegisterFeature()
        {
            _availableFeatureCount++;
        }

        internal static void UnregisterFeature()
        {
            _availableFeatureCount = Mathf.Max(0, _availableFeatureCount - 1);
        }

        public static void SetIntensity(float intensity)
        {
            Intensity = Mathf.Clamp01(intensity);
        }

        public static void Reset()
        {
            Intensity = 0f;
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
