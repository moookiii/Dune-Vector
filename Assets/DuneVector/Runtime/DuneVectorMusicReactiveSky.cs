using System;
using System.Runtime.InteropServices;
using FMODUnity;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicReactiveSky : MonoBehaviour, IMusicReactiveSink
    {
        private static readonly ProfilerMarker AnalysisIngestMarker = new ProfilerMarker("MusicVisualizer.AnalysisIngest");
        private DuneVectorAudioManager _audio;
        private DuneVectorY2KSky _sky;
        private Bloom _bloom;
        private Camera _camera;
        private MusicReactiveSkyTuning _settings;

        private FMOD.DSP _spectrumDsp;
        private FMOD.ChannelGroup _musicChannelGroup;
        private int _sampleRate;
        private float _analysisTimer;
        private float _rawBass;
        private float _rawMids;
        private float _rawHighs;
        private float _bass;
        private float _mids;
        private float _highs;
        private float _bassPulse;
        private float _highPulse;
        private float _baseBloomIntensity;
        private float _baseBloomThreshold;
        private float _currentBloomIntensity;
        private float _currentBloomThreshold;
        private MusicVisualizerMode _visualizerMode = MusicVisualizerMode.All;
        private uint _analysisSequence;
        private int _lightningWorldAnchorTick = int.MinValue;
        private bool _conductorControlsResponse;
        private float _eventFilamentIntensity;
        private float _eventFilamentAge;
        private float _eventFilamentDuration;
        private int _eventFilamentStrikeCount;

        public MusicAnalysisFrame LatestAnalysisFrame { get; private set; }

        public void EnableConductorControl()
        {
            _conductorControlsResponse = true;
            if (_sky != null)
            {
                _sky.ReactiveShockRingIntensity.Override(
                    _visualizerMode == MusicVisualizerMode.All
                        ? _settings.ShockRingIntensity
                        : 0f);
            }
        }

        public void Initialize(
            DuneVectorAudioManager audio,
            DuneVectorY2KSky sky,
            Bloom bloom,
            Camera camera,
            MusicReactiveSkyTuning settings)
        {
            if (_audio != null)
            {
                _audio.MusicVisualizerModeChanged -= SetVisualizerMode;
                _audio.ActiveMusicTrackChanged -= HandleActiveMusicTrackChanged;
            }

            _audio = audio;
            _sky = sky;
            _bloom = bloom;
            _camera = camera;
            _settings = settings;

            if (_settings == null || !_settings.Enabled || _audio == null || _sky == null)
            {
                enabled = false;
                return;
            }

            ApplyAuthoredSkySettings();
            if (_bloom != null)
            {
                _baseBloomIntensity = _bloom.intensity.value;
                _baseBloomThreshold = _bloom.threshold.value;
                _currentBloomIntensity = _baseBloomIntensity;
                _currentBloomThreshold = _baseBloomThreshold;
            }

            _audio.MusicVisualizerModeChanged += SetVisualizerMode;
            _audio.ActiveMusicTrackChanged += HandleActiveMusicTrackChanged;
            SetVisualizerMode(_audio.VisualizerMode);
        }

        private void HandleActiveMusicTrackChanged(MusicPlaylistTrack track)
        {
            ReleaseSpectrumDsp();
            ClearRawSpectrum();
            _bass = 0f;
            _mids = 0f;
            _highs = 0f;
            _bassPulse = 0f;
            _highPulse = 0f;
            LatestAnalysisFrame = default;
            _analysisTimer = 0f;
        }

        public void SetVisualizerMode(MusicVisualizerMode mode)
        {
            _visualizerMode = mode;
            if (_sky != null)
            {
                _sky.ReactiveShockRingIntensity.Override(
                    mode == MusicVisualizerMode.All
                        ? _settings.ShockRingIntensity
                        : 0f);
                _sky.ReactiveLightningIntensity.Override(
                    mode == MusicVisualizerMode.All
                        ? _settings.LightningIntensity
                        : 0f);
            }
            if (_visualizerMode == MusicVisualizerMode.Off)
            {
                ClearVisualResponse();
            }
            else if (_visualizerMode == MusicVisualizerMode.NoFlash && _bloom != null)
            {
                _currentBloomIntensity = _baseBloomIntensity;
                _currentBloomThreshold = _baseBloomThreshold;
                _bloom.intensity.value = _baseBloomIntensity;
                _bloom.threshold.value = _baseBloomThreshold;
            }
        }

        private void ApplyAuthoredSkySettings()
        {
            _sky.ReactiveFrontColor.Override(_settings.FrontColor);
            _sky.ReactiveFrontIntensity.Override(_settings.FrontIntensity);
            _sky.ReactiveFrontCount.Override(_settings.FrontCount);
            _sky.ReactiveFrontTravelSpeed.Override(_settings.FrontTravelSpeed);
            _sky.ReactiveFrontThickness.Override(_settings.FrontThickness);
            _sky.ReactiveFrontCurvature.Override(_settings.FrontCurvature);
            _sky.ReactiveFrontAltitude.Override(_settings.FrontAltitude);
            _sky.ReactiveFrontVerticalSpan.Override(_settings.FrontVerticalSpan);
            _sky.ReactiveBassExpansion.Override(_settings.BassFrontExpansion);
            _sky.ReactiveFrontEnergyResponse.Override(_settings.FrontEnergyResponse);
            _sky.ReactiveFrontBassResponse.Override(_settings.FrontBassResponse);
            _sky.ReactiveFrontPulseResponse.Override(_settings.FrontPulseResponse);
            _sky.ReactiveFrontPressureWidth.Override(_settings.FrontPressureWidth);
            _sky.ReactiveFrontPressureOpacity.Override(_settings.FrontPressureOpacity);
            _sky.ReactiveAuroraColor.Override(_settings.AuroraColor);
            _sky.ReactiveAuroraIntensity.Override(_settings.AuroraIntensity);
            _sky.ReactiveAuroraAltitude.Override(_settings.AuroraAltitude);
            _sky.ReactiveAuroraThickness.Override(_settings.AuroraThickness);
            _sky.ReactiveAuroraWaviness.Override(_settings.AuroraWaviness);
            _sky.ReactiveAuroraTravelSpeed.Override(_settings.AuroraTravelSpeed);
            _sky.ReactiveAuroraFrequency.Override(_settings.AuroraFrequency);
            _sky.ReactiveAuroraSecondaryIntensity.Override(_settings.AuroraSecondaryIntensity);
            _sky.ReactiveAuroraShimmerAmount.Override(_settings.AuroraShimmerAmount);
            _sky.ReactiveShockRingColor.Override(_settings.ShockRingColor);
            _sky.ReactiveShockRingIntensity.Override(_settings.ShockRingIntensity);
            _sky.ReactiveShockRingCount.Override(_settings.ShockRingCount);
            _sky.ReactiveShockRingThickness.Override(_settings.ShockRingThickness);
            _sky.ReactiveShockRingTravelSpeed.Override(_settings.ShockRingTravelSpeed);
            _sky.ReactiveShockRingVerticalSpan.Override(_settings.ShockRingVerticalSpan);
            _sky.ReactiveShockRingBassResponse.Override(_settings.ShockRingBassResponse);
            _sky.ReactiveShockRingSustainResponse.Override(_settings.ShockRingSustainResponse);
            _sky.ReactiveShockRingBeatRateBpm.Override(_settings.ShockRingBeatRateBpm);
            _sky.ReactiveShockRingBeatDutyCycle.Override(_settings.ShockRingBeatDutyCycle);
            _sky.ReactiveShockRingBreakup.Override(_settings.ShockRingBreakup);
            _sky.ReactiveShockRingZigzagAmount.Override(_settings.ShockRingZigzagAmount);
            _sky.ReactiveShockRingZigzagFrequency.Override(_settings.ShockRingZigzagFrequency);
            _sky.ReactiveLightningColor.Override(_settings.LightningColor);
            _sky.ReactiveLightningIntensity.Override(_settings.LightningIntensity);
            _sky.ReactiveLightningSectorCount.Override(_settings.LightningSectorCount);
            _sky.ReactiveLightningWidth.Override(_settings.LightningWidth);
            _sky.ReactiveLightningJaggedness.Override(_settings.LightningJaggedness);
            _sky.ReactiveLightningRetargetRate.Override(_settings.LightningRetargetRate);
            _sky.ReactiveLightningSustainResponse.Override(_settings.LightningSustainResponse);
            _sky.ReactiveLightningBranchIntensity.Override(_settings.LightningBranchIntensity);
            _sky.ReactiveLightningStrikeCount.Override(_settings.LightningStrikeCount);
            _sky.ReactiveLightningHaloWidthMultiplier.Override(_settings.LightningHaloWidthMultiplier);
            _sky.ReactiveLightningHaloIntensity.Override(_settings.LightningHaloIntensity);
            _sky.ReactiveLightningNodeIntensity.Override(_settings.LightningNodeIntensity);
            _sky.ReactiveLightningNodeSpacing.Override(_settings.LightningNodeSpacing);
            _sky.ReactiveSparkColor.Override(_settings.SparkColor);
            _sky.ReactiveSparkIntensity.Override(_settings.SparkIntensity);
            _sky.ReactiveSparkGridScale.Override(_settings.SparkGridScale);
            _sky.ReactiveSparkDensity.Override(_settings.SparkDensity);
            _sky.ReactiveSparkSize.Override(_settings.SparkSize);
            _sky.ReactiveSparkTwinkleSpeed.Override(_settings.SparkTwinkleSpeed);
            _sky.ReactiveSparkSustainResponse.Override(_settings.SparkSustainResponse);
        }

        private void Update()
        {
            if (_visualizerMode == MusicVisualizerMode.Off)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            if (!_spectrumDsp.hasHandle())
            {
                TryAttachSpectrumDsp();
            }

            ApplyCameraFrustum();

            _analysisTimer -= deltaTime;
            if (_analysisTimer <= 0f)
            {
                float rate = Mathf.Max(1f, _settings.AnalysisRate);
                _analysisTimer = 1f / rate;
                ReadSpectrum();
            }

            using (AnalysisIngestMarker.Auto())
            {
                SmoothMusicResponse(deltaTime);
                PublishAnalysisFrame();
            }
            if (!_conductorControlsResponse)
            {
                ApplyMusicResponse(deltaTime);
            }
        }

        private void ApplyCameraFrustum()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            if (_camera == null)
            {
                return;
            }

            Vector3 horizontalForward = _camera.transform.forward;
            horizontalForward.y = 0f;
            if (horizontalForward.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            horizontalForward.Normalize();
            float cameraAzimuth = Mathf.Atan2(horizontalForward.x, horizontalForward.z);
            float verticalHalfFov = _camera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float horizontalHalfFov = Mathf.Atan(
                Mathf.Tan(verticalHalfFov) * Mathf.Max(0.01f, _camera.aspect));
            float usableFrustum = 1f - Mathf.Clamp01(_settings.LightningFrustumEdgePadding);

            _sky.ReactiveCameraAzimuth.value = Mathf.Repeat(
                cameraAzimuth / (Mathf.PI * 2f) + 0.5f,
                1f);
            _sky.ReactiveLightningAzimuthSpan.value = horizontalHalfFov
                / (Mathf.PI * 2f)
                * usableFrustum;

            int retargetTick = Mathf.FloorToInt(Time.time * _settings.LightningRetargetRate);
            if (retargetTick == _lightningWorldAnchorTick)
            {
                return;
            }

            _lightningWorldAnchorTick = retargetTick;
            float cameraAzimuth01 = _sky.ReactiveCameraAzimuth.value;
            float azimuthSpan = _sky.ReactiveLightningAzimuthSpan.value;
            float slotCount = Mathf.Max(1f, _settings.LightningSectorCount);
            _sky.ReactiveLightningWorldAzimuth0.value = ResolveLightningWorldAzimuth(
                retargetTick, 0, cameraAzimuth01, azimuthSpan, slotCount);
            _sky.ReactiveLightningWorldAzimuth1.value = ResolveLightningWorldAzimuth(
                retargetTick, 1, cameraAzimuth01, azimuthSpan, slotCount);
            _sky.ReactiveLightningWorldAzimuth2.value = ResolveLightningWorldAzimuth(
                retargetTick, 2, cameraAzimuth01, azimuthSpan, slotCount);
            _sky.ReactiveLightningWorldAzimuth3.value = ResolveLightningWorldAzimuth(
                retargetTick, 3, cameraAzimuth01, azimuthSpan, slotCount);
        }

        private static float ResolveLightningWorldAzimuth(
            int retargetTick,
            int strikeIndex,
            float cameraAzimuth,
            float azimuthSpan,
            float slotCount)
        {
            uint state = unchecked((uint)retargetTick * 747796405u)
                ^ unchecked((uint)(strikeIndex + 1) * 2891336453u);
            state ^= state >> 16;
            state *= 2246822519u;
            state ^= state >> 13;
            float choice = (state & 0x00FFFFFFu) / 16777216f;
            float slot = (Mathf.Floor(choice * slotCount) + 0.5f) / slotCount;
            float offset = (slot * 2f - 1f) * azimuthSpan;
            return Mathf.Repeat(cameraAzimuth + offset, 1f);
        }

        private void TryAttachSpectrumDsp()
        {
            if (!_audio.TryGetMusicChannelGroup(out _musicChannelGroup))
            {
                return;
            }

            FMOD.System coreSystem = RuntimeManager.CoreSystem;
            if (coreSystem.createDSPByType(FMOD.DSP_TYPE.FFT, out _spectrumDsp) != FMOD.RESULT.OK)
            {
                _spectrumDsp.clearHandle();
                return;
            }

            int windowSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(_settings.FftWindowSize), 128, 2048);
            _spectrumDsp.setParameterInt((int)FMOD.DSP_FFT.WINDOWSIZE, windowSize);
            _spectrumDsp.setParameterInt((int)FMOD.DSP_FFT.WINDOW, (int)FMOD.DSP_FFT_WINDOW_TYPE.HANNING);

            FMOD.RESULT addResult = _musicChannelGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, _spectrumDsp);
            if (addResult != FMOD.RESULT.OK)
            {
                _spectrumDsp.release();
                _spectrumDsp.clearHandle();
                return;
            }

            if (coreSystem.getSoftwareFormat(out _sampleRate, out _, out _) != FMOD.RESULT.OK)
            {
                _sampleRate = 0;
            }
        }

        private void ReadSpectrum()
        {
            if (!_spectrumDsp.hasHandle() || _audio.MusicVolume <= Mathf.Epsilon)
            {
                ClearRawSpectrum();
                return;
            }

            FMOD.RESULT result = _spectrumDsp.getParameterData(
                (int)FMOD.DSP_FFT.SPECTRUMDATA,
                out IntPtr data,
                out _);
            if (result != FMOD.RESULT.OK || data == IntPtr.Zero || _sampleRate <= 0)
            {
                ClearRawSpectrum();
                return;
            }

            FMOD.DSP_PARAMETER_FFT fft = Marshal.PtrToStructure<FMOD.DSP_PARAMETER_FFT>(data);
            float[][] channels = fft.spectrum;
            if (channels == null || channels.Length == 0 || fft.length <= 0)
            {
                ClearRawSpectrum();
                return;
            }

            float binWidth = _sampleRate / (fft.length * 2f);
            _rawBass = MeasureBand(channels, fft.length, binWidth, _settings.MinimumFrequency, _settings.BassMaximumFrequency)
                * _settings.BassGain;
            _rawMids = MeasureBand(channels, fft.length, binWidth, _settings.BassMaximumFrequency, _settings.MidMaximumFrequency)
                * _settings.MidGain;
            _rawHighs = MeasureBand(channels, fft.length, binWidth, _settings.MidMaximumFrequency, _settings.HighMaximumFrequency)
                * _settings.HighGain;
        }

        private float MeasureBand(
            float[][] channels,
            int spectrumLength,
            float binWidth,
            float minimumFrequency,
            float maximumFrequency)
        {
            int firstBin = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0f, minimumFrequency) / binWidth), 0, spectrumLength - 1);
            int lastBin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(minimumFrequency, maximumFrequency) / binWidth), firstBin, spectrumLength - 1);
            float sum = 0f;
            int samples = 0;

            for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
            {
                float[] channel = channels[channelIndex];
                if (channel == null)
                {
                    continue;
                }

                int channelLastBin = Mathf.Min(lastBin, channel.Length - 1);
                for (int bin = firstBin; bin <= channelLastBin; bin++)
                {
                    sum += channel[bin];
                    samples++;
                }
            }

            if (samples == 0)
            {
                return 0f;
            }

            float magnitude = sum / samples * _settings.SpectrumGain;
            float normalized = Mathf.InverseLerp(_settings.SpectrumNoiseFloor, 1f, magnitude);
            return Mathf.Clamp01(normalized);
        }

        private void SmoothMusicResponse(float deltaTime)
        {
            float bassBeforeSmoothing = _bass;
            float highsBeforeSmoothing = _highs;
            _bass = SmoothBand(_bass, _rawBass, deltaTime);
            _mids = SmoothBand(_mids, _rawMids, deltaTime);
            _highs = SmoothBand(_highs, _rawHighs, deltaTime);

            float bassTransient = Mathf.Max(0f, _rawBass - bassBeforeSmoothing) * _settings.BassTransientSensitivity;
            float highTransient = Mathf.Max(0f, _rawHighs - highsBeforeSmoothing) * _settings.HighTransientSensitivity;
            float pulseDecay = Mathf.Exp(-Mathf.Max(0f, _settings.PulseDecaySpeed) * deltaTime);
            _bassPulse = Mathf.Max(Mathf.Clamp01(bassTransient), _bassPulse * pulseDecay);
            _highPulse = Mathf.Max(Mathf.Clamp01(highTransient), _highPulse * pulseDecay);
        }

        private float SmoothBand(float current, float target, float deltaTime)
        {
            float speed = target > current ? _settings.AttackSpeed : _settings.ReleaseSpeed;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, speed) * deltaTime);
            return Mathf.Lerp(current, target, blend);
        }

        private void PublishAnalysisFrame()
        {
            float energy = Mathf.Clamp01((_bass + _mids + _highs) / 3f);
            LatestAnalysisFrame = new MusicAnalysisFrame
            {
                RawBass = _rawBass,
                RawMid = _rawMids,
                RawHigh = _rawHighs,
                NormalizedBass = _rawBass,
                NormalizedMid = _rawMids,
                NormalizedHigh = _rawHighs,
                SmoothedBass = _bass,
                SmoothedMid = _mids,
                SmoothedHigh = _highs,
                BassTransient = _bassPulse,
                HighTransient = _highPulse,
                TotalEnergy = energy,
                LowHighBalance = Mathf.Clamp((_bass - _highs) * 0.5f + 0.5f, 0f, 1f),
                Sequence = ++_analysisSequence,
                TimelinePositionMilliseconds = _audio != null
                    ? _audio.TimelineState.TimelinePositionMilliseconds
                    : 0,
            };
        }

        private void ApplyMusicResponse(float deltaTime)
        {
            float energy = Mathf.Clamp01((_bass + _mids + _highs) / 3f);
            _sky.ReactiveMusicEnergy.value = energy;
            _sky.ReactiveMusicBass.value = _bass;
            _sky.ReactiveMusicMids.value = _mids;
            _sky.ReactiveMusicHighs.value = _highs;
            _sky.ReactiveBassPulse.value = _bassPulse;
            _sky.ReactiveHighPulse.value = _highPulse;

            if (_bloom == null)
            {
                return;
            }

            bool bloomAllowed = _visualizerMode == MusicVisualizerMode.All;
            float bloomTarget = _baseBloomIntensity
                + (bloomAllowed ? energy * _settings.BloomEnergyBoost : 0f)
                + (bloomAllowed ? _bassPulse * _settings.BloomBassPulseBoost : 0f);
            bloomTarget = Mathf.Min(
                bloomTarget,
                Mathf.Max(_baseBloomIntensity, _settings.BloomMaximumIntensity));
            float thresholdTarget = Mathf.Max(
                0f,
                _baseBloomThreshold - energy * _settings.BloomThresholdReduction);
            _currentBloomIntensity = SmoothBloom(
                _currentBloomIntensity,
                bloomTarget,
                deltaTime);
            _currentBloomThreshold = SmoothBloom(
                _currentBloomThreshold,
                thresholdTarget,
                deltaTime);
            _bloom.intensity.value = _currentBloomIntensity;
            _bloom.threshold.value = _currentBloomThreshold;
        }

        public void ApplyContinuous(in MusicReactiveRuntimeState state)
        {
            if (_sky == null || _visualizerMode == MusicVisualizerMode.Off)
            {
                return;
            }

            _sky.ReactiveMusicEnergy.value = state.Energy;
            _sky.ReactiveMusicBass.value = state.Bass;
            _sky.ReactiveMusicMids.value = state.Mid;
            _sky.ReactiveMusicHighs.value = state.High;
            _sky.ReactiveBassPulse.value = state.Analysis.BassTransient;
            _sky.ReactiveHighPulse.value = state.Analysis.HighTransient;
            _sky.ReactiveAuroraIntensity.value = _settings.AuroraIntensity
                * state.Multipliers.CurrentIntensity;
            _sky.ReactiveAuroraThickness.value = _settings.AuroraThickness
                * state.Multipliers.CurrentThickness;
            _sky.ReactiveAuroraTravelSpeed.value = _settings.AuroraTravelSpeed
                * state.Multipliers.CurrentTravel;

            if (_eventFilamentIntensity > 0f)
            {
                _eventFilamentAge += Time.unscaledDeltaTime;
                float duration = Mathf.Max(0.01f, _eventFilamentDuration);
                float envelope = 1f - Mathf.Clamp01(_eventFilamentAge / duration);
                bool allowed = (state.Permissions & MusicVisualEffectGroups.Filaments) != 0;
                _sky.ReactiveLightningIntensity.value = allowed
                    ? _eventFilamentIntensity * envelope * state.Multipliers.FilamentAvailability
                    : 0f;
                _sky.ReactiveLightningStrikeCount.value = _eventFilamentStrikeCount;
                if (envelope <= 0f)
                {
                    _eventFilamentIntensity = 0f;
                    _sky.ReactiveLightningIntensity.value = _settings.LightningIntensity;
                }
            }

            if (_bloom == null)
            {
                return;
            }

            bool bloomAllowed = (state.Permissions & MusicVisualEffectGroups.Bloom) != 0;
            float contribution = bloomAllowed ? state.Bloom : 0f;
            float bloomTarget = _baseBloomIntensity
                + contribution * _settings.BloomEnergyBoost
                + (bloomAllowed
                    ? state.Analysis.BassTransient * _settings.BloomBassPulseBoost
                    : 0f);
            bloomTarget = Mathf.Min(
                bloomTarget,
                Mathf.Max(_baseBloomIntensity, _settings.BloomMaximumIntensity));
            float thresholdTarget = Mathf.Max(
                0f,
                _baseBloomThreshold - contribution * _settings.BloomThresholdReduction);
            float deltaTime = Time.unscaledDeltaTime;
            _currentBloomIntensity = SmoothBloom(_currentBloomIntensity, bloomTarget, deltaTime);
            _currentBloomThreshold = SmoothBloom(_currentBloomThreshold, thresholdTarget, deltaTime);
            _bloom.intensity.value = _currentBloomIntensity;
            _bloom.threshold.value = _currentBloomThreshold;
        }

        public void Dispatch(in MusicVisualDispatchCommand command, in MusicReactiveRuntimeState state)
        {
            if (_sky == null || _visualizerMode == MusicVisualizerMode.Off)
            {
                return;
            }

            if (command.Type == MusicVisualCueType.FinalRelease)
            {
                _eventFilamentIntensity = 0f;
                _sky.ReactiveLightningIntensity.value = _settings.LightningIntensity;
                return;
            }
            if (command.IsAuthored
                && (command.AllowedEffects & MusicVisualEffectGroups.Filaments) != 0
                && command.FilamentIntensity > 0f)
            {
                _eventFilamentIntensity = command.FilamentIntensity;
                _eventFilamentStrikeCount = Mathf.Clamp(command.FilamentStrikeCount, 1, 3);
                _eventFilamentAge = 0f;
                _eventFilamentDuration = command.DurationBeats * 60f
                    / Mathf.Max(1f, state.Timeline.Tempo);
                _sky.ReactiveLightningIntensity.value = _eventFilamentIntensity;
                _sky.ReactiveLightningStrikeCount.value = _eventFilamentStrikeCount;
            }

            switch (command.Type)
            {
                case MusicVisualCueType.MinorKick:
                case MusicVisualCueType.MajorKick:
                case MusicVisualCueType.ReactorAnticipation:
                case MusicVisualCueType.ReactorDischarge:
                    _sky.ReactiveBassPulse.value = Mathf.Max(_sky.ReactiveBassPulse.value, command.Strength);
                    break;
                case MusicVisualCueType.MinorSnare:
                case MusicVisualCueType.AccentSnare:
                case MusicVisualCueType.TrebleTick:
                case MusicVisualCueType.TrebleBurst:
                    _sky.ReactiveHighPulse.value = Mathf.Max(_sky.ReactiveHighPulse.value, command.Strength);
                    break;
            }
        }

        public void ResetMusicResponse()
        {
            ClearVisualResponse();
        }

        private float SmoothBloom(float current, float target, float deltaTime)
        {
            float speed = target > current ? _settings.BloomAttackSpeed : _settings.BloomReleaseSpeed;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, speed) * deltaTime);
            return Mathf.Lerp(current, target, blend);
        }

        private void ClearRawSpectrum()
        {
            _rawBass = 0f;
            _rawMids = 0f;
            _rawHighs = 0f;
        }

        private void ClearVisualResponse()
        {
            ClearRawSpectrum();
            _bass = 0f;
            _mids = 0f;
            _highs = 0f;
            _bassPulse = 0f;
            _highPulse = 0f;
            _eventFilamentIntensity = 0f;
            _eventFilamentAge = 0f;
            _eventFilamentDuration = 0f;
            _eventFilamentStrikeCount = 0;
            _analysisTimer = 0f;
            LatestAnalysisFrame = default;

            if (_sky != null)
            {
                _sky.ReactiveMusicEnergy.value = 0f;
                _sky.ReactiveMusicBass.value = 0f;
                _sky.ReactiveMusicMids.value = 0f;
                _sky.ReactiveMusicHighs.value = 0f;
                _sky.ReactiveBassPulse.value = 0f;
                _sky.ReactiveHighPulse.value = 0f;
            }

            if (_bloom != null)
            {
                _currentBloomIntensity = _baseBloomIntensity;
                _currentBloomThreshold = _baseBloomThreshold;
                _bloom.intensity.value = _baseBloomIntensity;
                _bloom.threshold.value = _baseBloomThreshold;
            }
        }

        private void OnDestroy()
        {
            if (_audio != null)
            {
                _audio.MusicVisualizerModeChanged -= SetVisualizerMode;
                _audio.ActiveMusicTrackChanged -= HandleActiveMusicTrackChanged;
            }

            if (_bloom != null)
            {
                _bloom.intensity.value = _baseBloomIntensity;
                _bloom.threshold.value = _baseBloomThreshold;
            }

            ReleaseSpectrumDsp();
        }

        private void ReleaseSpectrumDsp()
        {
            if (!_spectrumDsp.hasHandle())
            {
                _musicChannelGroup.clearHandle();
                return;
            }

            if (_musicChannelGroup.hasHandle())
            {
                _musicChannelGroup.removeDSP(_spectrumDsp);
            }
            _spectrumDsp.release();
            _spectrumDsp.clearHandle();
            _musicChannelGroup.clearHandle();
        }
    }
}
