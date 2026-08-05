using Unity.Profiling;
using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicCameraEffects : MonoBehaviour, IMusicReactiveSink
    {
        private static readonly ProfilerMarker CameraApplyMarker = new ProfilerMarker("MusicVisualizer.CameraApply");

        private DroneCameraController _camera;
        private DuneVectorAudioManager _audio;
        private MusicReactiveSkyTuning _settings;
        private float _requestedFov;
        private float _requestedRoll;
        private float _requestedPosition;
        private float _appliedFov;
        private float _appliedRoll;
        private float _appliedPosition;

        public float RequestedFovOffset => _requestedFov;
        public float AppliedFovOffset => _appliedFov;
        public float RequestedRoll => _requestedRoll;
        public float AppliedRoll => _appliedRoll;
        public float RequestedPosition => _requestedPosition;
        public float AppliedPosition => _appliedPosition;

        public void Initialize(
            DroneCameraController camera,
            DuneVectorAudioManager audio,
            MusicReactiveSkyTuning settings)
        {
            _camera = camera;
            _audio = audio;
            _settings = settings;
            if (_audio != null)
            {
                _audio.VisualizerFovEnabledChanged += HandleVisualizerFovChanged;
            }
        }

        public void ApplyContinuous(in MusicReactiveRuntimeState state)
        {
        }

        public void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            if ((command.AllowedEffects & MusicVisualEffectGroups.Camera) == 0)
            {
                return;
            }

            float strength = Mathf.Clamp01(command.Strength);
            switch (command.Type)
            {
                case MusicVisualCueType.MajorKick:
                    if (command.IsAuthored && _audio != null && _audio.VisualizerFovEnabled)
                    {
                        _requestedFov = Mathf.Max(
                            _requestedFov,
                            strength * _settings.MajorKickFovStrength * _settings.MaximumVisualizerFovOffset);
                    }
                    break;
                case MusicVisualCueType.ReactorDischarge:
                    if (!command.IsAuthored)
                    {
                        break;
                    }
                    if (_audio != null && _audio.VisualizerFovEnabled)
                    {
                        _requestedFov = Mathf.Max(
                            _requestedFov,
                            strength * _settings.ReactorFovStrength * _settings.MaximumVisualizerFovOffset);
                    }
                    break;
            }
        }

        private void Update()
        {
            if (_camera == null || _settings == null)
            {
                return;
            }
            float deltaTime = Time.unscaledDeltaTime;
            float attack = Mathf.Max(0.01f, _settings.CameraKickAttackSeconds);
            float release = Mathf.Max(0.01f, _settings.CameraKickReleaseSeconds);
            _appliedFov = Mathf.MoveTowards(_appliedFov, _requestedFov, _settings.MaximumVisualizerFovOffset * deltaTime / attack);
            _appliedRoll = Mathf.MoveTowards(_appliedRoll, _requestedRoll, _settings.MaximumVisualizerRollDegrees * deltaTime / attack);
            _appliedPosition = Mathf.MoveTowards(
                _appliedPosition,
                _requestedPosition,
                _settings.MaximumVisualizerPositionOffset * deltaTime / attack);

            _requestedFov = Mathf.MoveTowards(_requestedFov, 0f, _settings.MaximumVisualizerFovOffset * deltaTime / release);
            _requestedRoll = Mathf.MoveTowards(_requestedRoll, 0f, _settings.MaximumVisualizerRollDegrees * deltaTime / release);
            _requestedPosition = Mathf.MoveTowards(
                _requestedPosition,
                0f,
                _settings.MaximumVisualizerPositionOffset * deltaTime / release);

            if (_audio == null || !_audio.VisualizerFovEnabled)
            {
                float disableRelease = Mathf.Max(0.01f, _settings.VisualizerFovDisableReleaseSeconds);
                _requestedFov = 0f;
                _appliedFov = Mathf.MoveTowards(
                    _appliedFov,
                    0f,
                    _settings.MaximumVisualizerFovOffset * deltaTime / disableRelease);
            }

            _appliedFov = Mathf.Clamp(_appliedFov, 0f, _settings.MaximumVisualizerFovOffset);
            _appliedRoll = Mathf.Clamp(
                _appliedRoll,
                -_settings.MaximumVisualizerRollDegrees,
                _settings.MaximumVisualizerRollDegrees);
            _appliedPosition = Mathf.Clamp(
                _appliedPosition,
                0f,
                _settings.MaximumVisualizerPositionOffset);
            using (CameraApplyMarker.Auto())
            {
                _camera.SetMusicVisualizerPresentation(
                    _appliedFov,
                    _appliedRoll,
                    Vector3.back * _appliedPosition);
            }
        }

        private void HandleVisualizerFovChanged(bool enabled)
        {
            if (!enabled)
            {
                _requestedFov = 0f;
            }
        }

        public void ResetMusicResponse()
        {
            _requestedFov = 0f;
            _requestedRoll = 0f;
            _requestedPosition = 0f;
            _appliedFov = 0f;
            _appliedRoll = 0f;
            _appliedPosition = 0f;
            if (_camera != null)
            {
                _camera.SetMusicVisualizerPresentation(0f, 0f, Vector3.zero);
            }
        }

        private void OnDestroy()
        {
            if (_audio != null)
            {
                _audio.VisualizerFovEnabledChanged -= HandleVisualizerFovChanged;
            }
            ResetMusicResponse();
        }
    }
}
