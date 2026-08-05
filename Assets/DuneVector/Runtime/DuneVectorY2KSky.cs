using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [Serializable]
    [VolumeComponentMenu("Dune Vector/Y2K Sky Data")]
    public sealed class DuneVectorY2KSky : VolumeComponent
    {
        public ColorParameter Top = new ColorParameter(Color.blue, true, false, true);
        public ColorParameter Middle = new ColorParameter(Color.cyan, true, false, true);
        public ColorParameter Bottom = new ColorParameter(Color.white, true, false, true);
        public MinFloatParameter GradientDiffusion = new MinFloatParameter(1f, 0f);
        public MinFloatParameter Multiplier = new MinFloatParameter(1f, 0f);

        public ColorParameter HorizonGlowColor = new ColorParameter(Color.cyan, true, false, true);
        public ClampedFloatParameter HorizonGlowSize = new ClampedFloatParameter(0.14f, 0.01f, 0.5f);
        public MinFloatParameter HorizonGlowIntensity = new MinFloatParameter(0.7f, 0f);

        public ColorParameter CloudColor = new ColorParameter(Color.white, true, false, true);
        public ColorParameter CloudHighlight = new ColorParameter(Color.white, true, false, true);
        public ColorParameter CloudPearl = new ColorParameter(Color.cyan, true, false, true);
        public ClampedFloatParameter CloudOpacity = new ClampedFloatParameter(0.8f, 0f, 1f);
        public ClampedFloatParameter CloudAltitude = new ClampedFloatParameter(0.28f, 0.05f, 0.8f);
        public ClampedFloatParameter CloudThickness = new ClampedFloatParameter(0.2f, 0.03f, 0.5f);
        public MinFloatParameter CloudScale = new MinFloatParameter(3.8f, 0.1f);
        public ClampedFloatParameter CloudSoftness = new ClampedFloatParameter(0.075f, 0.005f, 0.25f);
        public ClampedFloatParameter CloudHighlightStrength = new ClampedFloatParameter(0.62f, 0f, 2f);
        public ClampedFloatParameter CloudPearlStrength = new ClampedFloatParameter(0.24f, 0f, 2f);
        public MinFloatParameter CloudDriftSpeed = new MinFloatParameter(0.012f, 0f);

        public ColorParameter StructureColor = new ColorParameter(Color.cyan, true, false, true);
        public ClampedFloatParameter StructureOpacity = new ClampedFloatParameter(0.1f, 0f, 1f);
        public ClampedFloatParameter ArcAltitude = new ClampedFloatParameter(0.2f, 0.02f, 0.65f);
        public ClampedFloatParameter ArcCurvature = new ClampedFloatParameter(0.32f, 0f, 1f);
        public ClampedFloatParameter ArcThickness = new ClampedFloatParameter(0.006f, 0.001f, 0.05f);
        public ClampedFloatParameter ArcFrequency = new ClampedFloatParameter(3f, 1f, 12f);
        public ClampedFloatParameter RingAltitude = new ClampedFloatParameter(0.12f, 0.02f, 0.6f);
        public ClampedFloatParameter RingSpacing = new ClampedFloatParameter(0.075f, 0.01f, 0.3f);
        public ClampedFloatParameter RingThickness = new ClampedFloatParameter(0.0035f, 0.001f, 0.04f);
        public ClampedFloatParameter GridOpacity = new ClampedFloatParameter(0.42f, 0f, 1f);
        public ClampedFloatParameter GridScale = new ClampedFloatParameter(14f, 2f, 40f);
        public ClampedFloatParameter GridHeight = new ClampedFloatParameter(0.11f, 0.02f, 0.35f);
        public ClampedFloatParameter GridLineThickness = new ClampedFloatParameter(0.018f, 0.001f, 0.08f);

        public ColorParameter ReactiveFrontColor = new ColorParameter(Color.cyan, true, false, true);
        public MinFloatParameter ReactiveFrontIntensity = new MinFloatParameter(1f, 0f);
        public ClampedFloatParameter ReactiveFrontCount = new ClampedFloatParameter(4f, 1f, 12f);
        public MinFloatParameter ReactiveFrontTravelSpeed = new MinFloatParameter(0.1f, 0f);
        public ClampedFloatParameter ReactiveFrontThickness = new ClampedFloatParameter(0.03f, 0.001f, 0.15f);
        public ClampedFloatParameter ReactiveFrontCurvature = new ClampedFloatParameter(0.7f, 0f, 2f);
        public ClampedFloatParameter ReactiveFrontAltitude = new ClampedFloatParameter(0f, -0.2f, 0.8f);
        public ClampedFloatParameter ReactiveFrontVerticalSpan = new ClampedFloatParameter(0.66f, 0.05f, 1f);
        public ClampedFloatParameter ReactiveBassExpansion = new ClampedFloatParameter(0.58f, 0f, 2f);
        public ClampedFloatParameter ReactiveFrontEnergyResponse = new ClampedFloatParameter(0.28f, 0f, 2f);
        public ClampedFloatParameter ReactiveFrontBassResponse = new ClampedFloatParameter(0.82f, 0f, 2f);
        public ClampedFloatParameter ReactiveFrontPulseResponse = new ClampedFloatParameter(1f, 0f, 2f);
        public ClampedFloatParameter ReactiveFrontPressureWidth = new ClampedFloatParameter(4.5f, 1f, 8f);
        public ClampedFloatParameter ReactiveFrontPressureOpacity = new ClampedFloatParameter(0.32f, 0f, 1f);

        public ColorParameter ReactiveAuroraColor = new ColorParameter(Color.magenta, true, false, true);
        public MinFloatParameter ReactiveAuroraIntensity = new MinFloatParameter(0.8f, 0f);
        public ClampedFloatParameter ReactiveAuroraAltitude = new ClampedFloatParameter(0.42f, -0.1f, 0.9f);
        public ClampedFloatParameter ReactiveAuroraThickness = new ClampedFloatParameter(0.055f, 0.001f, 0.2f);
        public ClampedFloatParameter ReactiveAuroraWaviness = new ClampedFloatParameter(0.24f, 0f, 1f);
        public MinFloatParameter ReactiveAuroraTravelSpeed = new MinFloatParameter(0.075f, 0f);
        public ClampedFloatParameter ReactiveAuroraFrequency = new ClampedFloatParameter(3.5f, 1f, 12f);
        public ClampedFloatParameter ReactiveAuroraSecondaryIntensity = new ClampedFloatParameter(0.58f, 0f, 1f);
        public ClampedFloatParameter ReactiveAuroraShimmerAmount = new ClampedFloatParameter(0.38f, 0f, 1f);

        public ColorParameter ReactiveShockRingColor = new ColorParameter(Color.yellow, true, false, true);
        public MinFloatParameter ReactiveShockRingIntensity = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter ReactiveShockRingCount = new ClampedFloatParameter(1f, 1f, 16f);
        public ClampedFloatParameter ReactiveShockRingThickness = new ClampedFloatParameter(0.006f, 0.0005f, 0.08f);
        public MinFloatParameter ReactiveShockRingTravelSpeed = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter ReactiveShockRingVerticalSpan = new ClampedFloatParameter(0.72f, 0.05f, 1f);
        public ClampedFloatParameter ReactiveShockRingBassResponse = new ClampedFloatParameter(1f, 0f, 2f);
        public ClampedFloatParameter ReactiveShockRingSustainResponse = new ClampedFloatParameter(0f, 0f, 1f);
        public MinFloatParameter ReactiveShockRingBeatRateBpm = new MinFloatParameter(1f, 1f);
        public ClampedFloatParameter ReactiveShockRingBeatDutyCycle = new ClampedFloatParameter(0.24f, 0.02f, 0.8f);
        public ClampedFloatParameter ReactiveShockRingBreakup = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveShockRingZigzagAmount = new ClampedFloatParameter(0.42f, 0f, 1.5f);
        public ClampedFloatParameter ReactiveShockRingZigzagFrequency = new ClampedFloatParameter(12f, 1f, 32f);

        public ColorParameter ReactiveLightningColor = new ColorParameter(Color.white, true, false, true);
        public MinFloatParameter ReactiveLightningIntensity = new MinFloatParameter(2.8f, 0f);
        public ClampedFloatParameter ReactiveLightningSectorCount = new ClampedFloatParameter(14f, 1f, 32f);
        public ClampedFloatParameter ReactiveLightningWidth = new ClampedFloatParameter(0.012f, 0.0001f, 0.08f);
        public ClampedFloatParameter ReactiveLightningJaggedness = new ClampedFloatParameter(0.32f, 0f, 1f);
        public MinFloatParameter ReactiveLightningRetargetRate = new MinFloatParameter(8f, 0.1f);
        public ClampedFloatParameter ReactiveLightningSustainResponse = new ClampedFloatParameter(0.24f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningBranchIntensity = new ClampedFloatParameter(0.75f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningStrikeCount = new ClampedFloatParameter(1f, 1f, 4f);
        public ClampedFloatParameter ReactiveLightningHaloWidthMultiplier = new ClampedFloatParameter(1f, 1f, 12f);
        public ClampedFloatParameter ReactiveLightningHaloIntensity = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningNodeIntensity = new ClampedFloatParameter(0f, 0f, 2f);
        public ClampedFloatParameter ReactiveLightningNodeSpacing = new ClampedFloatParameter(9f, 2f, 24f);
        public ClampedFloatParameter ReactiveCameraAzimuth = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningAzimuthSpan = new ClampedFloatParameter(0.1f, 0f, 0.5f);
        public ClampedFloatParameter ReactiveLightningWorldAzimuth0 = new ClampedFloatParameter(0.125f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningWorldAzimuth1 = new ClampedFloatParameter(0.375f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningWorldAzimuth2 = new ClampedFloatParameter(0.625f, 0f, 1f);
        public ClampedFloatParameter ReactiveLightningWorldAzimuth3 = new ClampedFloatParameter(0.875f, 0f, 1f);

        public ColorParameter ReactiveSparkColor = new ColorParameter(Color.white, true, false, true);
        public MinFloatParameter ReactiveSparkIntensity = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter ReactiveSparkGridScale = new ClampedFloatParameter(28f, 4f, 64f);
        public ClampedFloatParameter ReactiveSparkDensity = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveSparkSize = new ClampedFloatParameter(0.035f, 0.002f, 0.2f);
        public MinFloatParameter ReactiveSparkTwinkleSpeed = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter ReactiveSparkSustainResponse = new ClampedFloatParameter(0f, 0f, 1f);

        public ClampedFloatParameter ReactiveMusicEnergy = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveMusicBass = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveMusicMids = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveMusicHighs = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveBassPulse = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter ReactiveHighPulse = new ClampedFloatParameter(0f, 0f, 1f);

    }

    public sealed class DuneVectorUrpFogState
    {
        public readonly ColorParameter color = new ColorParameter(Color.gray);
        public readonly FloatParameter startDistance = new FloatParameter(0f);
        public readonly FloatParameter meanFreePath = new FloatParameter(0f);
        public readonly FloatParameter maxFogDistance = new FloatParameter(0f);
        public readonly FloatParameter baseHeight = new FloatParameter(0f);
        public readonly FloatParameter maximumHeight = new FloatParameter(0f);
        public readonly BoolParameter enableVolumetricFog = new BoolParameter(false);
    }

    [DisallowMultipleComponent]
    public sealed class DuneVectorUrpEnvironmentDriver : MonoBehaviour
    {
        private const string ShaderName = "DuneVector/URP Y2K Sky";
        private static readonly FieldInfo[] SkyFields = typeof(DuneVectorY2KSky).GetFields(BindingFlags.Public | BindingFlags.Instance);
        private readonly List<SkyPropertyBinding> _skyPropertyBindings = new List<SkyPropertyBinding>(SkyFields.Length);
        private Material _skyMaterial;
        private DuneVectorY2KSky _sky;
        private DuneVectorUrpFogState _fog;
        private Material _previousSkybox;
        private bool _previousFogEnabled;
        private FogMode _previousFogMode;
        private float _previousFogStartDistance;
        private float _previousFogEndDistance;
        private Color _previousFogColor;

        private readonly struct SkyPropertyBinding
        {
            public readonly int PropertyId;
            public readonly VolumeParameter Parameter;

            public SkyPropertyBinding(int propertyId, VolumeParameter parameter)
            {
                PropertyId = propertyId;
                Parameter = parameter;
            }
        }

        public void Initialize(DuneVectorY2KSky sky, DuneVectorUrpFogState fog)
        {
            _sky = sky;
            _fog = fog;
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Dune Vector requires the URP sky shader '{ShaderName}'.");
            }

            _previousSkybox = RenderSettings.skybox;
            _previousFogEnabled = RenderSettings.fog;
            _previousFogMode = RenderSettings.fogMode;
            _previousFogStartDistance = RenderSettings.fogStartDistance;
            _previousFogEndDistance = RenderSettings.fogEndDistance;
            _previousFogColor = RenderSettings.fogColor;
            _skyMaterial = new Material(shader) { name = "Runtime Dune Vector URP Sky" };
            CacheSkyPropertyBindings();
            RenderSettings.skybox = _skyMaterial;
            Apply();
            DynamicGI.UpdateEnvironment();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void OnDestroy()
        {
            if (RenderSettings.skybox == _skyMaterial)
            {
                RenderSettings.skybox = _previousSkybox;
            }
            RenderSettings.fog = _previousFogEnabled;
            RenderSettings.fogMode = _previousFogMode;
            RenderSettings.fogStartDistance = _previousFogStartDistance;
            RenderSettings.fogEndDistance = _previousFogEndDistance;
            RenderSettings.fogColor = _previousFogColor;
            CoreUtils.Destroy(_skyMaterial);
        }

        private void CacheSkyPropertyBindings()
        {
            _skyPropertyBindings.Clear();
            foreach (FieldInfo field in SkyFields)
            {
                if (field.Name == nameof(DuneVectorY2KSky.Multiplier))
                {
                    continue;
                }

                object parameter = field.GetValue(_sky);
                if (!(parameter is ColorParameter) && !(parameter is FloatParameter))
                {
                    continue;
                }

                string propertyName = field.Name switch
                {
                    nameof(DuneVectorY2KSky.Top) => "_SkyTop",
                    nameof(DuneVectorY2KSky.Middle) => "_SkyMiddle",
                    nameof(DuneVectorY2KSky.Bottom) => "_SkyBottom",
                    _ => "_" + field.Name
                };
                int propertyId = Shader.PropertyToID(propertyName);
                _skyPropertyBindings.Add(new SkyPropertyBinding(propertyId, (VolumeParameter)parameter));
            }
        }

        private void Apply()
        {
            if (_skyMaterial == null || _sky == null)
            {
                return;
            }

            foreach (SkyPropertyBinding binding in _skyPropertyBindings)
            {
                if (binding.Parameter is ColorParameter color)
                {
                    _skyMaterial.SetColor(binding.PropertyId, color.value);
                }
                else if (binding.Parameter is FloatParameter number)
                {
                    _skyMaterial.SetFloat(binding.PropertyId, number.value);
                }
            }

            _skyMaterial.SetFloat("_SkyIntensity", _sky.Multiplier.value);
            RenderSettings.fog = _previousFogEnabled && _fog != null && _fog.maxFogDistance.value > 0f;
            if (RenderSettings.fog)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = Mathf.Min(_fog.startDistance.value, _fog.maxFogDistance.value);
                RenderSettings.fogEndDistance = _fog.maxFogDistance.value;
                RenderSettings.fogColor = _fog.color.value;
            }
        }
    }
}
