using System;
using System.Runtime.InteropServices;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace DuneVector
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorMusicVisualSynth : MonoBehaviour
    {
        private MusicVisualSynthTuning _settings;
        private Bus _musicBus;
        private ChannelGroup _musicChannelGroup;
        private DSP _fftDsp;
        private float[][] _spectrum;
        private float[] _bands;
        private float[] _targetBands;
        private int _sampleRate;
        private float _energy;
        private Material _drawMaterial;
        private bool _analyzerAttached;
        private bool _shutdown;

        public void Initialize(MusicVisualSynthTuning settings, Bus musicBus)
        {
            Shutdown();
            _shutdown = false;
            _settings = settings;
            _musicBus = musicBus;
            int bandCount = settings != null ? Mathf.Clamp(settings.BandCount, 12, 96) : 0;
            _bands = new float[bandCount];
            _targetBands = new float[bandCount];
            TryAttachAnalyzer();
        }

        private void Update()
        {
            if (_shutdown || _settings == null || !_settings.Enabled)
            {
                return;
            }

            if (!_analyzerAttached)
            {
                TryAttachAnalyzer();
            }

            bool receivedSpectrum = _analyzerAttached && TryReadSpectrum();
            SmoothBands(receivedSpectrum);
        }

        private void TryAttachAnalyzer()
        {
            if (_analyzerAttached || !_musicBus.isValid())
            {
                return;
            }

            bool locked = _musicBus.lockChannelGroup() == RESULT.OK;
            try
            {
                if (_musicBus.getChannelGroup(out _musicChannelGroup) != RESULT.OK
                    || !_musicChannelGroup.hasHandle())
                {
                    return;
                }

                if (RuntimeManager.CoreSystem.createDSPByType(DSP_TYPE.FFT, out _fftDsp) != RESULT.OK)
                {
                    return;
                }

                int fftWindowSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_settings.FftWindowSize, 256, 4096));
                _fftDsp.setParameterInt((int)DSP_FFT.WINDOWSIZE, fftWindowSize);
                _fftDsp.setParameterInt((int)DSP_FFT.WINDOW, (int)DSP_FFT_WINDOW_TYPE.BLACKMANHARRIS);
                if (_musicChannelGroup.addDSP(CHANNELCONTROL_DSP_INDEX.TAIL, _fftDsp) != RESULT.OK)
                {
                    _fftDsp.release();
                    _fftDsp.clearHandle();
                    return;
                }

                _fftDsp.setActive(true);
                if (RuntimeManager.CoreSystem.getSoftwareFormat(
                        out _sampleRate,
                        out SPEAKERMODE _,
                        out int _) != RESULT.OK)
                {
                    _sampleRate = AudioSettings.outputSampleRate;
                }
                _analyzerAttached = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not attach the music visual synthesizer to FMOD: {exception.Message}", this);
                ReleaseAnalyzer();
            }
            finally
            {
                if (locked && _musicBus.isValid())
                {
                    _musicBus.unlockChannelGroup();
                }
            }
        }

        private bool TryReadSpectrum()
        {
            if (!_fftDsp.hasHandle()
                || _fftDsp.getParameterData((int)DSP_FFT.SPECTRUMDATA, out IntPtr data, out uint _) != RESULT.OK
                || data == IntPtr.Zero)
            {
                return false;
            }

            DSP_PARAMETER_FFT fft = Marshal.PtrToStructure<DSP_PARAMETER_FFT>(data);
            int channelCount = Mathf.Clamp(fft.numchannels, 0, 2);
            if (fft.length <= 0 || channelCount <= 0)
            {
                return false;
            }

            EnsureSpectrumBuffers(channelCount, fft.length);
            fft.getSpectrum(ref _spectrum);

            float nyquist = Mathf.Max(1f, _sampleRate * 0.5f);
            float minimumFrequency = Mathf.Clamp(_settings.MinimumFrequencyHz, 20f, nyquist);
            float maximumFrequency = Mathf.Clamp(_settings.MaximumFrequencyHz, minimumFrequency, nyquist);
            float frequencyRatio = maximumFrequency / minimumFrequency;
            for (int band = 0; band < _bands.Length; band++)
            {
                float lowerT = band / (float)_bands.Length;
                float upperT = (band + 1f) / _bands.Length;
                float lowerFrequency = minimumFrequency * Mathf.Pow(frequencyRatio, lowerT);
                float upperFrequency = minimumFrequency * Mathf.Pow(frequencyRatio, upperT);
                int lowerBin = Mathf.Clamp(Mathf.FloorToInt((lowerFrequency / nyquist) * fft.length), 0, fft.length - 1);
                int upperBin = Mathf.Clamp(Mathf.CeilToInt((upperFrequency / nyquist) * fft.length), lowerBin + 1, fft.length);

                float sum = 0f;
                int sampleCount = 0;
                for (int channel = 0; channel < channelCount; channel++)
                {
                    for (int bin = lowerBin; bin < upperBin; bin++)
                    {
                        float magnitude = _spectrum[channel][bin];
                        sum += magnitude * magnitude;
                        sampleCount++;
                    }
                }

                float rms = sampleCount > 0 ? Mathf.Sqrt(sum / sampleCount) : 0f;
                _targetBands[band] = Mathf.Pow(
                    Mathf.Clamp01((rms - Mathf.Max(0f, _settings.NoiseFloor)) * Mathf.Max(0.1f, _settings.ResponseGain)),
                    Mathf.Clamp(_settings.ResponsePower, 0.2f, 2f));
            }

            return true;
        }

        private void SmoothBands(bool receivedSpectrum)
        {
            float idle = Mathf.Clamp(_settings.IdleAmplitude, 0f, 0.2f);
            float total = 0f;
            for (int band = 0; band < _bands.Length; band++)
            {
                float target = receivedSpectrum ? Mathf.Max(idle, _targetBands[band]) : idle;
                float sharpness = target > _bands[band]
                    ? Mathf.Max(0f, _settings.AttackSharpness)
                    : Mathf.Max(0f, _settings.ReleaseSharpness);
                _bands[band] = Mathf.Lerp(
                    _bands[band],
                    target,
                    1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime));
                total += _bands[band];
            }

            float targetEnergy = _bands.Length > 0 ? total / _bands.Length : 0f;
            float energySharpness = targetEnergy > _energy
                ? Mathf.Max(0f, _settings.AttackSharpness)
                : Mathf.Max(0f, _settings.ReleaseSharpness);
            _energy = Mathf.Lerp(
                _energy,
                targetEnergy,
                1f - Mathf.Exp(-energySharpness * Time.unscaledDeltaTime));
        }

        private void EnsureSpectrumBuffers(int channelCount, int fftLength)
        {
            if (_spectrum != null
                && _spectrum.Length == channelCount
                && _spectrum[0].Length == fftLength)
            {
                return;
            }

            _spectrum = new float[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
            {
                _spectrum[channel] = new float[fftLength];
            }
        }

        private void OnGUI()
        {
            if (_shutdown
                || _settings == null
                || !_settings.Enabled
                || _bands == null
                || _bands.Length == 0
                || DuneVectorCourierGame.IsGameplayHudSuppressed
                || Event.current.type != EventType.Repaint
                || !EnsureDrawMaterial())
            {
                return;
            }

            GUI.depth = 1000;
            Vector2 center = new Vector2(
                Screen.width * Mathf.Clamp01(_settings.ScreenAnchor.x),
                Screen.height * Mathf.Clamp01(_settings.ScreenAnchor.y));
            float opacity = Mathf.Clamp01(_settings.Opacity);
            float coreRadius = Mathf.Max(0f, _settings.CoreRadius)
                + Mathf.Max(0f, _settings.CorePulseRadius) * _energy;

            _drawMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            DrawDisc(center, coreRadius, WithOpacity(_settings.CoreColor, opacity));
            DrawEchoRings(center, coreRadius, opacity);
            DrawSpectrumBars(center, opacity);
            GL.PopMatrix();
        }

        private void DrawSpectrumBars(Vector2 center, float opacity)
        {
            float baseRadius = Mathf.Max(0f, _settings.SpectrumBaseRadius);
            float maximumLength = Mathf.Max(0f, _settings.MaximumBarLength);
            float minimumLength = Mathf.Max(0.1f, _settings.MinimumBarThickness);
            float arcDegrees = Mathf.Clamp(_settings.ArcDegrees, 30f, 360f);
            float angleStep = arcDegrees / _bands.Length;
            float halfBarAngle = angleStep * Mathf.Clamp(_settings.BarAngularFill, 0.1f, 1f) * 0.5f;

            GL.Begin(GL.QUADS);
            for (int band = 0; band < _bands.Length; band++)
            {
                float frequencyT = _bands.Length > 1 ? band / (float)(_bands.Length - 1) : 0f;
                float angle = _settings.ArcStartDegrees + (band + 0.5f) * angleStep;
                float innerAngle = (angle - halfBarAngle) * Mathf.Deg2Rad;
                float outerAngle = (angle + halfBarAngle) * Mathf.Deg2Rad;
                float outerRadius = baseRadius + minimumLength + maximumLength * _bands[band];
                Vector2 direction0 = new Vector2(Mathf.Cos(innerAngle), Mathf.Sin(innerAngle));
                Vector2 direction1 = new Vector2(Mathf.Cos(outerAngle), Mathf.Sin(outerAngle));
                Color color = EvaluateBandColor(frequencyT);
                color.a *= opacity;
                GL.Color(color);
                GL.Vertex(center + direction0 * baseRadius);
                GL.Vertex(center + direction0 * outerRadius);
                GL.Vertex(center + direction1 * outerRadius);
                GL.Vertex(center + direction1 * baseRadius);
            }
            GL.End();
        }

        private void DrawEchoRings(Vector2 center, float coreRadius, float opacity)
        {
            int ringCount = Mathf.Clamp(_settings.EchoRingCount, 0, 8);
            for (int ring = 0; ring < ringCount; ring++)
            {
                float ringT = ringCount > 1 ? ring / (float)(ringCount - 1) : 0f;
                float radius = coreRadius + Mathf.Max(0f, _settings.EchoRingSpacing) * (ring + 1f);
                Color color = _settings.RingColor;
                color.a *= opacity
                    * Mathf.Lerp(1f, Mathf.Clamp01(_settings.OuterRingOpacityMultiplier), ringT)
                    * Mathf.Lerp(Mathf.Clamp01(_settings.QuietRingBrightness), 1f, _energy);
                DrawRing(center, radius, Mathf.Max(0.1f, _settings.RingThickness), color);
            }
        }

        private void DrawDisc(Vector2 center, float radius, Color color)
        {
            int resolution = Mathf.Clamp(_settings.RingResolution, 8, 256);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (int segment = 0; segment < resolution; segment++)
            {
                float angle0 = segment / (float)resolution * Mathf.PI * 2f;
                float angle1 = (segment + 1f) / resolution * Mathf.PI * 2f;
                GL.Vertex(center);
                GL.Vertex(center + new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0)) * radius);
                GL.Vertex(center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius);
            }
            GL.End();
        }

        private void DrawRing(Vector2 center, float radius, float thickness, Color color)
        {
            int resolution = Mathf.Clamp(_settings.RingResolution, 8, 256);
            float innerRadius = Mathf.Max(0f, radius - thickness * 0.5f);
            float outerRadius = radius + thickness * 0.5f;
            GL.Begin(GL.QUADS);
            GL.Color(color);
            for (int segment = 0; segment < resolution; segment++)
            {
                float angle0 = segment / (float)resolution * Mathf.PI * 2f;
                float angle1 = (segment + 1f) / resolution * Mathf.PI * 2f;
                Vector2 direction0 = new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0));
                Vector2 direction1 = new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1));
                GL.Vertex(center + direction0 * innerRadius);
                GL.Vertex(center + direction0 * outerRadius);
                GL.Vertex(center + direction1 * outerRadius);
                GL.Vertex(center + direction1 * innerRadius);
            }
            GL.End();
        }

        private Color EvaluateBandColor(float frequencyT)
        {
            return frequencyT < 0.5f
                ? Color.Lerp(_settings.LowBandColor, _settings.MidBandColor, frequencyT * 2f)
                : Color.Lerp(_settings.MidBandColor, _settings.HighBandColor, (frequencyT - 0.5f) * 2f);
        }

        private bool EnsureDrawMaterial()
        {
            if (_drawMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return false;
            }

            _drawMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _drawMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _drawMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _drawMaterial.SetInt("_Cull", (int)CullMode.Off);
            _drawMaterial.SetInt("_ZWrite", 0);
            return true;
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            color.a *= opacity;
            return color;
        }

        public void Shutdown()
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;
            ReleaseAnalyzer();
        }

        private void ReleaseAnalyzer()
        {
            if (_musicChannelGroup.hasHandle() && _fftDsp.hasHandle())
            {
                _musicChannelGroup.removeDSP(_fftDsp);
            }
            if (_fftDsp.hasHandle())
            {
                _fftDsp.release();
                _fftDsp.clearHandle();
            }
            _musicChannelGroup.clearHandle();
            _analyzerAttached = false;
        }

        private void OnDestroy()
        {
            Shutdown();
            if (_drawMaterial != null)
            {
                Destroy(_drawMaterial);
                _drawMaterial = null;
            }
        }
    }
}
