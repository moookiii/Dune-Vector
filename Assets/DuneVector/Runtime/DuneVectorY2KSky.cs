using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuneVector
{
    [Serializable]
    [VolumeComponentMenu("Sky/Dune Vector Y2K Sky")]
    [SupportedOnRenderPipeline(typeof(HDRenderPipelineAsset))]
    [SkyUniqueID(2000317)]
    public sealed class DuneVectorY2KSky : SkySettings
    {
        public ColorParameter Top = new ColorParameter(Color.blue, true, false, true);
        public ColorParameter Middle = new ColorParameter(Color.cyan, true, false, true);
        public ColorParameter Bottom = new ColorParameter(Color.white, true, false, true);
        public MinFloatParameter GradientDiffusion = new MinFloatParameter(1f, 0f);

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

        public override Type GetSkyRendererType()
        {
            return typeof(DuneVectorY2KSkyRenderer);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = base.GetHashCode();
                hash = hash * 23 + Top.GetHashCode();
                hash = hash * 23 + Middle.GetHashCode();
                hash = hash * 23 + Bottom.GetHashCode();
                hash = hash * 23 + GradientDiffusion.GetHashCode();
                hash = hash * 23 + HorizonGlowColor.GetHashCode();
                hash = hash * 23 + HorizonGlowSize.GetHashCode();
                hash = hash * 23 + HorizonGlowIntensity.GetHashCode();
                hash = hash * 23 + CloudColor.GetHashCode();
                hash = hash * 23 + CloudHighlight.GetHashCode();
                hash = hash * 23 + CloudPearl.GetHashCode();
                hash = hash * 23 + CloudOpacity.GetHashCode();
                hash = hash * 23 + CloudAltitude.GetHashCode();
                hash = hash * 23 + CloudThickness.GetHashCode();
                hash = hash * 23 + CloudScale.GetHashCode();
                hash = hash * 23 + CloudSoftness.GetHashCode();
                hash = hash * 23 + CloudHighlightStrength.GetHashCode();
                hash = hash * 23 + CloudPearlStrength.GetHashCode();
                hash = hash * 23 + CloudDriftSpeed.GetHashCode();
                hash = hash * 23 + StructureColor.GetHashCode();
                hash = hash * 23 + StructureOpacity.GetHashCode();
                hash = hash * 23 + ArcAltitude.GetHashCode();
                hash = hash * 23 + ArcCurvature.GetHashCode();
                hash = hash * 23 + ArcThickness.GetHashCode();
                hash = hash * 23 + ArcFrequency.GetHashCode();
                hash = hash * 23 + RingAltitude.GetHashCode();
                hash = hash * 23 + RingSpacing.GetHashCode();
                hash = hash * 23 + RingThickness.GetHashCode();
                hash = hash * 23 + GridOpacity.GetHashCode();
                hash = hash * 23 + GridScale.GetHashCode();
                hash = hash * 23 + GridHeight.GetHashCode();
                hash = hash * 23 + GridLineThickness.GetHashCode();
                return hash;
            }
        }
    }

    public sealed class DuneVectorY2KSkyRenderer : SkyRenderer
    {
        private const string ShaderName = "Hidden/DuneVector/HDRP Y2K Sky";

        private static readonly int SkyIntensityId = Shader.PropertyToID("_SkyIntensity");
        private static readonly int PixelCoordToViewDirectionId = Shader.PropertyToID("_PixelCoordToViewDirWS");

        private Material _material;
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

        public DuneVectorY2KSkyRenderer()
        {
            SupportDynamicSunLight = false;
        }

        public override void Build()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Dune Vector requires the Resources shader '{ShaderName}' for its HDRP sky.");
            }

            _material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(_material);
        }

        public override void RenderSky(
            BuiltinSkyParameters builtinParams,
            bool renderForCubemap,
            bool renderSunDisk)
        {
            DuneVectorY2KSky sky = builtinParams.skySettings as DuneVectorY2KSky;
            if (sky == null || _material == null)
            {
                return;
            }

            SetColor("_SkyTop", sky.Top.value);
            SetColor("_SkyMiddle", sky.Middle.value);
            SetColor("_SkyBottom", sky.Bottom.value);
            SetFloat("_GradientDiffusion", sky.GradientDiffusion.value);
            SetColor("_HorizonGlowColor", sky.HorizonGlowColor.value);
            SetFloat("_HorizonGlowSize", sky.HorizonGlowSize.value);
            SetFloat("_HorizonGlowIntensity", sky.HorizonGlowIntensity.value);
            SetColor("_CloudColor", sky.CloudColor.value);
            SetColor("_CloudHighlight", sky.CloudHighlight.value);
            SetColor("_CloudPearl", sky.CloudPearl.value);
            SetFloat("_CloudOpacity", sky.CloudOpacity.value);
            SetFloat("_CloudAltitude", sky.CloudAltitude.value);
            SetFloat("_CloudThickness", sky.CloudThickness.value);
            SetFloat("_CloudScale", sky.CloudScale.value);
            SetFloat("_CloudSoftness", sky.CloudSoftness.value);
            SetFloat("_CloudHighlightStrength", sky.CloudHighlightStrength.value);
            SetFloat("_CloudPearlStrength", sky.CloudPearlStrength.value);
            SetFloat("_CloudDriftSpeed", sky.CloudDriftSpeed.value);
            SetColor("_StructureColor", sky.StructureColor.value);
            SetFloat("_StructureOpacity", sky.StructureOpacity.value);
            SetFloat("_ArcAltitude", sky.ArcAltitude.value);
            SetFloat("_ArcCurvature", sky.ArcCurvature.value);
            SetFloat("_ArcThickness", sky.ArcThickness.value);
            SetFloat("_ArcFrequency", sky.ArcFrequency.value);
            SetFloat("_RingAltitude", sky.RingAltitude.value);
            SetFloat("_RingSpacing", sky.RingSpacing.value);
            SetFloat("_RingThickness", sky.RingThickness.value);
            SetFloat("_GridOpacity", sky.GridOpacity.value);
            SetFloat("_GridScale", sky.GridScale.value);
            SetFloat("_GridHeight", sky.GridHeight.value);
            SetFloat("_GridLineThickness", sky.GridLineThickness.value);
            SetFloat("_RenderForCubemap", renderForCubemap ? 1f : 0f);
            SetFloat(SkyIntensityId, GetSkyIntensity(sky, builtinParams.debugSettings));

            _propertyBlock.SetMatrix(
                PixelCoordToViewDirectionId,
                builtinParams.pixelCoordToViewDirMatrix);

            CoreUtils.DrawFullScreen(
                builtinParams.commandBuffer,
                _material,
                _propertyBlock,
                renderForCubemap ? 0 : 1);
        }

        private void SetColor(string propertyName, Color value)
        {
            _material.SetColor(propertyName, value);
        }

        private void SetFloat(string propertyName, float value)
        {
            _material.SetFloat(propertyName, value);
        }

        private void SetFloat(int propertyId, float value)
        {
            _material.SetFloat(propertyId, value);
        }
    }
}
