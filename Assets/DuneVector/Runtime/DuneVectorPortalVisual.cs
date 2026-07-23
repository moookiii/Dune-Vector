using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorPortalVisual : MonoBehaviour
    {
        private static readonly int DistanceFadeProperty = Shader.PropertyToID("_DistanceFade");

        private Renderer[] _renderers;
        private MaterialPropertyBlock _properties;
        private float _fadeStartDistance;
        private float _fadeEndDistance;
        private Camera _camera;

        public void Initialize(Renderer[] renderers, RingTuning settings)
        {
            _renderers = renderers;
            _properties = new MaterialPropertyBlock();
            _fadeStartDistance = Mathf.Max(0f, settings.PortalCameraFadeStartDistance);
            _fadeEndDistance = Mathf.Max(_fadeStartDistance + 0.01f, settings.PortalCameraFadeEndDistance);
            ApplyDistanceFade(1f);
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            if (_camera == null || _renderers == null)
            {
                return;
            }

            float distance = Vector3.Distance(_camera.transform.position, transform.position);
            float fade = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(_fadeStartDistance, _fadeEndDistance, distance));
            ApplyDistanceFade(fade);
        }

        private void ApplyDistanceFade(float fade)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(DistanceFadeProperty, fade);
                renderer.SetPropertyBlock(_properties);
            }
        }
    }
}
