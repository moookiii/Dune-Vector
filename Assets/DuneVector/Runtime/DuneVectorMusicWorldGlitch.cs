using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    public static class DuneVectorMusicGlitchRuntime
    {
        // Offscreen cameras that render their own isolated subject, such as the drone trail
        // unlock showcase. The glitch is a world-view effect, so it must not reach them.
        private static readonly HashSet<Camera> ExcludedCameras = new HashSet<Camera>();
        private static int _availableFeatureCount;

        public static float Intensity { get; private set; }
        public static bool FeatureAvailable => _availableFeatureCount > 0;

        public static void ExcludeCamera(Camera camera)
        {
            if (camera != null)
            {
                ExcludedCameras.Add(camera);
            }
        }

        public static void IncludeCamera(Camera camera)
        {
            if (camera != null)
            {
                ExcludedCameras.Remove(camera);
            }
        }

        public static bool IsCameraExcluded(Camera camera)
        {
            return camera != null && ExcludedCameras.Count > 0 && ExcludedCameras.Contains(camera);
        }

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
        private static readonly int ShapeId = Shader.PropertyToID("_DVMusicGlitchShape");
        private static readonly int SafetyId = Shader.PropertyToID("_DVMusicGlitchSafety");
        private static readonly int TintId = Shader.PropertyToID("_DVMusicGlitchTint");

        private MusicReactiveSkyTuning _settings;
        private float _age;
        private float _strength;
        private uint _seed;
        private int _accentSnareCount;
        private float _duration;
        private float _displacement;
        private int _sliceCount;
        private float _hudAge;
        private float _hudDuration;
        private float _hudStrength;

        public float Intensity => DuneVectorMusicGlitchRuntime.Intensity;

        public void Initialize(MusicReactiveSkyTuning settings)
        {
            _settings = settings;
            ResetMusicResponse();
        }

        public void ApplyContinuous(in MusicReactiveRuntimeState state)
        {
            if ((state.Permissions & MusicVisualEffectGroups.Glitch) == 0
                && (_strength > 0f || DuneVectorMusicGlitchRuntime.Intensity > 0f))
            {
                ResetWorldGlitch();
            }
            if ((state.Permissions & MusicVisualEffectGroups.HudBorder) == 0)
            {
                ResetHudBorder();
            }
        }

        public void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            if (state.VisualTier < _settings.WorldGlitchMinimumVisualTier)
            {
                return;
            }

            bool accepted = command.IsAuthored && ResolveAuthoredDisplacement(command) > 0f;
            if (!command.IsAuthored
                && (command.Type == MusicVisualCueType.AccentSnare
                    || command.Type == MusicVisualCueType.TrebleBurst))
            {
                _accentSnareCount++;
                accepted = _accentSnareCount % Mathf.Max(1, _settings.AccentSnaresPerGlitch) == 0;
            }

            float duration = ResolveDuration(command, state);
            if ((command.AllowedEffects & MusicVisualEffectGroups.HudBorder) != 0)
            {
                float hudResponse = ResolveHudResponse(command);
                if (hudResponse > 0f)
                {
                    _hudAge = 0f;
                    _hudDuration = duration > 0f
                        ? duration
                        : _settings.HudBorderFallbackDurationSeconds;
                    _hudStrength = Mathf.Clamp01(command.Strength) * hudResponse;
                }
            }

            if ((command.AllowedEffects & MusicVisualEffectGroups.Glitch) == 0 || !accepted)
            {
                return;
            }

            _age = 0f;
            _strength = Mathf.Clamp01(command.Strength);
            _seed = command.DeterministicSeed;
            _duration = duration > 0f ? duration : _settings.WorldGlitchDurationSeconds;
            float requestedDisplacement = ResolveAuthoredDisplacement(command);
            if (requestedDisplacement <= 0f)
            {
                requestedDisplacement = _settings.WorldGlitchHorizontalShift;
            }
            _displacement = Mathf.Min(
                requestedDisplacement,
                _settings.MaximumGlitchUvDisplacement > 0f
                    ? _settings.MaximumGlitchUvDisplacement
                    : requestedDisplacement);
            _sliceCount = command.GlitchSliceCount > 0
                ? command.GlitchSliceCount
                : _settings.WorldGlitchSliceCount;
            ApplyGlobals(_settings.WorldGlitchMaximumIntensity * _strength);
        }

        private float ResolveAuthoredDisplacement(in MusicVisualDispatchCommand command)
        {
            float configuredDisplacement = command.Type switch
            {
                MusicVisualCueType.ReactorDischarge => _settings.ClimaxGlitchUvDisplacement,
                MusicVisualCueType.FinalRelease => _settings.ClimaxGlitchUvDisplacement,
                MusicVisualCueType.AccentSnare => _settings.AccentGlitchUvDisplacement,
                MusicVisualCueType.TrebleBurst => _settings.AccentGlitchUvDisplacement,
                MusicVisualCueType.MajorKick => _settings.OrdinaryGlitchUvDisplacement,
                MusicVisualCueType.MinorSnare => _settings.OrdinaryGlitchUvDisplacement,
                _ => 0f,
            };
            return Mathf.Max(command.GlitchUvDisplacement, configuredDisplacement);
        }

        private float ResolveDuration(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            float configuredBeats = command.Type switch
            {
                MusicVisualCueType.ReactorDischarge => _settings.ReactorGlitchDurationBeats,
                MusicVisualCueType.FinalRelease => _settings.ReactorGlitchDurationBeats,
                MusicVisualCueType.AccentSnare => _settings.AccentGlitchDurationBeats,
                MusicVisualCueType.TrebleBurst => _settings.AccentGlitchDurationBeats,
                _ => _settings.OrdinaryGlitchDurationBeats,
            };
            float beats = Mathf.Max(command.DurationBeats, configuredBeats);
            return beats > 0f
                ? beats * 60f / Mathf.Max(1f, state.Timeline.Tempo)
                : 0f;
        }

        private float ResolveHudResponse(in MusicVisualDispatchCommand command)
        {
            if (command.Type == MusicVisualCueType.ReactorDischarge
                || command.Type == MusicVisualCueType.FinalRelease)
            {
                return _settings.ReactorHudBorderResponse;
            }
            if (command.Type == MusicVisualCueType.AccentSnare
                || command.Type == MusicVisualCueType.TrebleBurst
                || command.Type == MusicVisualCueType.MajorKick)
            {
                return _settings.StrongHudBorderResponse;
            }
            return _settings.OrdinaryHudBorderResponse;
        }

        private void Update()
        {
            if (_settings == null || _strength <= 0f)
            {
                UpdateHudBorder();
                return;
            }
            _age += Time.unscaledDeltaTime;
            float duration = Mathf.Max(0.01f, _duration);
            float normalizedAge = Mathf.Clamp01(_age / duration);
            float envelope = (1f - normalizedAge) * (1f - normalizedAge);
            ApplyGlobals(_settings.WorldGlitchMaximumIntensity * _strength * envelope);
            if (normalizedAge >= 1f)
            {
                _strength = 0f;
                DuneVectorMusicGlitchRuntime.Reset();
            }
            UpdateHudBorder();
        }

        private void UpdateHudBorder()
        {
            if (_hudStrength <= 0f)
            {
                return;
            }
            _hudAge += Time.unscaledDeltaTime;
            if (_hudAge >= Mathf.Max(0.01f, _hudDuration))
            {
                ResetHudBorder();
            }
        }

        private void OnGUI()
        {
            if (_settings == null
                || _hudStrength <= 0f
                || Event.current.type != EventType.Repaint
                || DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }

            float normalizedAge = Mathf.Clamp01(_hudAge / Mathf.Max(0.01f, _hudDuration));
            float alpha = _hudStrength * (1f - normalizedAge) * (1f - normalizedAge);
            Color color = _settings.HudBorderColor;
            color.a *= Mathf.Clamp01(alpha);
            float inset = Mathf.Max(0f, _settings.HudBorderInset);
            float thickness = Mathf.Max(0f, _settings.HudBorderThickness);
            float corner = Mathf.Min(
                Mathf.Max(0f, _settings.HudBorderCornerLength),
                Mathf.Min(Screen.width, Screen.height) * 0.5f);
            if (thickness <= 0f || corner <= 0f || color.a <= 0f)
            {
                return;
            }

            DrawCorner(new Rect(inset, inset, corner, thickness), color);
            DrawCorner(new Rect(inset, inset, thickness, corner), color);
            DrawCorner(new Rect(Screen.width - inset - corner, inset, corner, thickness), color);
            DrawCorner(new Rect(Screen.width - inset - thickness, inset, thickness, corner), color);
            DrawCorner(new Rect(inset, Screen.height - inset - thickness, corner, thickness), color);
            DrawCorner(new Rect(inset, Screen.height - inset - corner, thickness, corner), color);
            DrawCorner(new Rect(Screen.width - inset - corner, Screen.height - inset - thickness, corner, thickness), color);
            DrawCorner(new Rect(Screen.width - inset - thickness, Screen.height - inset - corner, thickness, corner), color);
        }

        private static void DrawCorner(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void ApplyGlobals(float intensity)
        {
            DuneVectorMusicGlitchRuntime.SetIntensity(intensity);
            float maximumIntensity = Mathf.Max(0.0001f, _settings.WorldGlitchMaximumIntensity);
            float envelope = Mathf.Clamp01(intensity / maximumIntensity);
            float normalizedAge = Mathf.Clamp01(_age / Mathf.Max(0.01f, _duration));
            float seedPhase = (_seed % 65521u) / 65521f;
            float rowSelectionPhase = seedPhase * 251f + normalizedAge * 17f;
            Shader.SetGlobalVector(
                ParametersId,
                new Vector4(
                    envelope,
                    rowSelectionPhase,
                    Mathf.Max(1, _settings.WorldGlitchSliceCount),
                    _displacement));
            Shader.SetGlobalVector(
                ShapeId,
                new Vector4(Mathf.Max(1, _sliceCount), _strength, 0f, 0f));
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
            ResetWorldGlitch();
            ResetHudBorder();
        }

        private void ResetWorldGlitch()
        {
            _age = 0f;
            _strength = 0f;
            _seed = 0u;
            _accentSnareCount = 0;
            _duration = 0f;
            _displacement = 0f;
            _sliceCount = 0;
            DuneVectorMusicGlitchRuntime.Reset();
            Shader.SetGlobalVector(ParametersId, Vector4.zero);
            Shader.SetGlobalVector(ShapeId, Vector4.zero);
            Shader.SetGlobalVector(SafetyId, Vector4.zero);
            Shader.SetGlobalColor(TintId, Color.clear);
        }

        private void ResetHudBorder()
        {
            _hudAge = 0f;
            _hudDuration = 0f;
            _hudStrength = 0f;
        }

        private void OnDisable()
        {
            ResetMusicResponse();
        }
    }
}
