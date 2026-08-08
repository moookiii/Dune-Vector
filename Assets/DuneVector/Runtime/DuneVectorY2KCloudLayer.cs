using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorY2KCloudLayer : MonoBehaviour
    {
        private const string ShaderName = "DuneVector/URP Y2K Cloud Layer";

        private static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int CloudHighlightId = Shader.PropertyToID("_CloudHighlight");
        private static readonly int CloudPearlId = Shader.PropertyToID("_CloudPearl");
        private static readonly int CloudOpacityId = Shader.PropertyToID("_CloudOpacity");
        private static readonly int CloudAltitudeId = Shader.PropertyToID("_CloudAltitude");
        private static readonly int CloudThicknessId = Shader.PropertyToID("_CloudThickness");
        private static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
        private static readonly int CloudSoftnessId = Shader.PropertyToID("_CloudSoftness");
        private static readonly int CloudHighlightStrengthId = Shader.PropertyToID("_CloudHighlightStrength");
        private static readonly int CloudPearlStrengthId = Shader.PropertyToID("_CloudPearlStrength");
        private static readonly int CloudDriftSpeedId = Shader.PropertyToID("_CloudDriftSpeed");

        private DuneVectorY2KSky _sky;
        private GameObject _dome;
        private Material _material;

        public void Initialize(DuneVectorY2KSky sky)
        {
            _sky = sky;
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Dune Vector requires the URP cloud shader '{ShaderName}'.");
            }

            _material = new Material(shader) { name = "Runtime Dune Vector Y2K Clouds" };
            _dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dome.name = "Y2K Procedural Cloud Dome";
            _dome.transform.SetParent(transform, false);

            Collider collider = _dome.GetComponent<Collider>();
            CoreUtils.Destroy(collider);

            MeshRenderer renderer = _dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            ApplyCloudProperties();
        }

        private void LateUpdate()
        {
            if (_dome == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                _dome.transform.position = camera.transform.position;
                float diameter = camera.farClipPlane * 1.8f;
                _dome.transform.localScale = Vector3.one * diameter;
            }

            ApplyCloudProperties();
        }

        private void ApplyCloudProperties()
        {
            if (_material == null || _sky == null)
            {
                return;
            }

            _material.SetColor(CloudColorId, _sky.CloudColor.value);
            _material.SetColor(CloudHighlightId, _sky.CloudHighlight.value);
            _material.SetColor(CloudPearlId, _sky.CloudPearl.value);
            _material.SetFloat(
                CloudOpacityId,
                _sky.CloudsEnabled.value ? _sky.CloudOpacity.value : 0f);
            _material.SetFloat(CloudAltitudeId, _sky.CloudAltitude.value);
            _material.SetFloat(CloudThicknessId, _sky.CloudThickness.value);
            _material.SetFloat(CloudScaleId, _sky.CloudScale.value);
            _material.SetFloat(CloudSoftnessId, _sky.CloudSoftness.value);
            _material.SetFloat(CloudHighlightStrengthId, _sky.CloudHighlightStrength.value);
            _material.SetFloat(CloudPearlStrengthId, _sky.CloudPearlStrength.value);
            _material.SetFloat(CloudDriftSpeedId, _sky.CloudDriftSpeed.value);
        }

        private void OnDestroy()
        {
            CoreUtils.Destroy(_dome);
            CoreUtils.Destroy(_material);
        }
    }
}
