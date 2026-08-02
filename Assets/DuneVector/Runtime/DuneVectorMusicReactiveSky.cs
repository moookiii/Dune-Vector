using System;
using System.Runtime.InteropServices;
using FMODUnity;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicReactiveSky : MonoBehaviour
    {
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

        public void Initialize(
            DuneVectorAudioManager audio,
            DuneVectorY2KSky sky,
            Bloom bloom,
            Camera camera,
            MusicReactiveSkyTuning settings)
        {
            _audio = audio;
            _sky = sky;
            _bloom = bloom;
            _camera = camera;
            _settings = settings;

            if (_settings == null || !_settings.Enabled || _audio == null || _sky == null || _bloom == null)
            {
                enabled = false;
                return;
            }

            ApplyAuthoredSkySettings();
            _baseBloomIntensity = _bloom.intensity.value;
            _baseBloomThreshold = _bloom.threshold.value;
            _currentBloomIntensity = _baseBloomIntensity;
            _currentBloomThreshold = _baseBloomThreshold;
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
            _sky.ReactiveLightningColor.Override(_settings.LightningColor);
            _sky.ReactiveLightningIntensity.Override(_settings.LightningIntensity);
            _sky.ReactiveLightningSectorCount.Override(_settings.LightningSectorCount);
            _sky.ReactiveLightningWidth.Override(_settings.LightningWidth);
            _sky.ReactiveLightningJaggedness.Override(_settings.LightningJaggedness);
            _sky.ReactiveLightningRetargetRate.Override(_settings.LightningRetargetRate);
            _sky.ReactiveLightningSustainResponse.Override(_settings.LightningSustainResponse);
            _sky.ReactiveLightningBranchIntensity.Override(_settings.LightningBranchIntensity);
        }

        private void Update()
        {
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

            SmoothMusicResponse(deltaTime);
            ApplyMusicResponse(deltaTime);
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

        private void ApplyMusicResponse(float deltaTime)
        {
            float energy = Mathf.Clamp01((_bass + _mids + _highs) / 3f);
            _sky.ReactiveMusicEnergy.value = energy;
            _sky.ReactiveMusicBass.value = _bass;
            _sky.ReactiveMusicMids.value = _mids;
            _sky.ReactiveMusicHighs.value = _highs;
            _sky.ReactiveBassPulse.value = _bassPulse;
            _sky.ReactiveHighPulse.value = _highPulse;

            float bloomTarget = _baseBloomIntensity
                + energy * _settings.BloomEnergyBoost
                + _bassPulse * _settings.BloomBassPulseBoost;
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

        private void OnDestroy()
        {
            if (_bloom != null)
            {
                _bloom.intensity.value = _baseBloomIntensity;
                _bloom.threshold.value = _baseBloomThreshold;
            }

            if (!_spectrumDsp.hasHandle())
            {
                return;
            }

            if (_musicChannelGroup.hasHandle())
            {
                _musicChannelGroup.removeDSP(_spectrumDsp);
            }
            _spectrumDsp.release();
            _spectrumDsp.clearHandle();
        }
    }
}
