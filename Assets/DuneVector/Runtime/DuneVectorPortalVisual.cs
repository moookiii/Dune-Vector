using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorPortalVisual : MonoBehaviour
    {
        private static readonly int DistanceFadeProperty = Shader.PropertyToID("_DistanceFade");
        private static readonly int DistanceBloomBoostProperty = Shader.PropertyToID("_DistanceBloomBoost");
        private static readonly int ActivationBloomBoostProperty = Shader.PropertyToID("_ActivationBloomBoost");
        private static readonly int OpacityProperty = Shader.PropertyToID("_Opacity");

        private Renderer[] _renderers;
        private Renderer _activationPulseRenderer;
        private Transform _activationPulseTransform;
        private MaterialPropertyBlock _properties;
        private float _fadeStartDistance;
        private float _fadeEndDistance;
        private float _distanceVisibilityStartDistance;
        private float _distanceVisibilityEndDistance;
        private float _distanceVisibilityBloomMultiplier;
        private float _reactionDuration;
        private float _reactionBloomMultiplier;
        private float _reactionMinimumVisibility;
        private float _reactionRotationMultiplier;
        private float _reactionPulseExpansion;
        private float _reactionPulseOpacity;
        private float _reactionTimeRemaining;
        private float _reactionStrength;
        private bool _destroyAfterReaction;
        private float _detachedSpinSpeed;
        private Vector3 _detachedSpinAxis;
        private Camera _camera;

        public float RotationSpeedMultiplier { get; private set; } = 1f;

        public void Initialize(
            Renderer[] renderers,
            Renderer activationPulseRenderer,
            Transform activationPulseTransform,
            RingTuning settings)
        {
            _renderers = renderers;
            _activationPulseRenderer = activationPulseRenderer;
            _activationPulseTransform = activationPulseTransform;
            _properties = new MaterialPropertyBlock();
            _fadeStartDistance = Mathf.Max(0f, settings.PortalCameraFadeStartDistance);
            _fadeEndDistance = Mathf.Max(_fadeStartDistance + 0.01f, settings.PortalCameraFadeEndDistance);
            _distanceVisibilityStartDistance =
                Mathf.Max(0f, settings.PortalDistanceVisibilityStartDistance);
            _distanceVisibilityEndDistance = Mathf.Max(
                _distanceVisibilityStartDistance + 0.01f,
                settings.PortalDistanceVisibilityEndDistance);
            _distanceVisibilityBloomMultiplier =
                Mathf.Max(1f, settings.PortalDistanceVisibilityBloomMultiplier);
            _reactionDuration = Mathf.Max(0.01f, settings.PortalActivationReactionDuration);
            _reactionBloomMultiplier = Mathf.Max(1f, settings.PortalActivationBloomMultiplier);
            _reactionMinimumVisibility = Mathf.Clamp01(settings.PortalActivationMinimumVisibility);
            _reactionRotationMultiplier = Mathf.Max(1f, settings.PortalActivationRotationMultiplier);
            _reactionPulseExpansion = Mathf.Max(1f, settings.PortalActivationPulseExpansion);
            _reactionPulseOpacity = Mathf.Clamp01(settings.PortalActivationPulseOpacity);
            ApplyDistanceVisibility(1f, 1f);
            ApplyActivationBloom(1f);
            if (_activationPulseRenderer != null)
            {
                ApplyPulseOpacity(0f);
                _activationPulseRenderer.gameObject.SetActive(false);
            }
        }

        public void PlayActivationReaction(
            bool detachForReaction,
            float detachedSpinSpeed,
            Vector3 detachedSpinAxis)
        {
            _reactionTimeRemaining = _reactionDuration;
            _destroyAfterReaction = detachForReaction;
            _detachedSpinSpeed = detachedSpinSpeed;
            _detachedSpinAxis = detachedSpinAxis.sqrMagnitude > 0.001f
                ? detachedSpinAxis.normalized
                : Vector3.forward;
            if (detachForReaction)
            {
                transform.SetParent(null, true);
            }
            if (_activationPulseTransform != null)
            {
                _activationPulseTransform.localScale = Vector3.one;
                _activationPulseTransform.gameObject.SetActive(true);
            }
            ApplyReaction(0f);
        }

        private void LateUpdate()
        {
            UpdateActivationReaction(Time.deltaTime);
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
            fade = Mathf.Max(fade, _reactionMinimumVisibility * _reactionStrength);

            float distanceVisibility = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    _distanceVisibilityStartDistance,
                    _distanceVisibilityEndDistance,
                    distance));
            float distanceBloomBoost =
                Mathf.Lerp(1f, _distanceVisibilityBloomMultiplier, distanceVisibility);
            ApplyDistanceVisibility(fade, distanceBloomBoost);
        }

        private void UpdateActivationReaction(float deltaTime)
        {
            if (_reactionTimeRemaining <= 0f)
            {
                return;
            }

            _reactionTimeRemaining = Mathf.Max(0f, _reactionTimeRemaining - deltaTime);
            float progress = 1f - (_reactionTimeRemaining / _reactionDuration);
            ApplyReaction(progress);

            if (_destroyAfterReaction)
            {
                transform.Rotate(
                    _detachedSpinAxis,
                    _detachedSpinSpeed * RotationSpeedMultiplier * deltaTime,
                    Space.Self);
            }

            if (_reactionTimeRemaining > 0f)
            {
                return;
            }

            RotationSpeedMultiplier = 1f;
            _reactionStrength = 0f;
            ApplyActivationBloom(1f);
            if (_activationPulseRenderer != null)
            {
                ApplyPulseOpacity(0f);
                _activationPulseRenderer.gameObject.SetActive(false);
            }
            if (_destroyAfterReaction)
            {
                Destroy(gameObject);
            }
        }

        private void ApplyReaction(float progress)
        {
            float clampedProgress = Mathf.Clamp01(progress);
            float reactionStrength = 1f - clampedProgress;
            reactionStrength *= reactionStrength;
            _reactionStrength = reactionStrength;
            RotationSpeedMultiplier = Mathf.Lerp(1f, _reactionRotationMultiplier, reactionStrength);
            ApplyActivationBloom(Mathf.Lerp(1f, _reactionBloomMultiplier, reactionStrength));

            if (_activationPulseTransform == null || _activationPulseRenderer == null)
            {
                return;
            }

            float expansionProgress = 1f - Mathf.Pow(1f - clampedProgress, 3f);
            float scale = Mathf.Lerp(1f, _reactionPulseExpansion, expansionProgress);
            _activationPulseTransform.localScale = Vector3.one * scale;
            ApplyPulseOpacity(_reactionPulseOpacity * reactionStrength);
        }

        private void ApplyDistanceVisibility(float fade, float bloomBoost)
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
                _properties.SetFloat(DistanceBloomBoostProperty, bloomBoost);
                renderer.SetPropertyBlock(_properties);
            }
        }

        private void ApplyActivationBloom(float multiplier)
        {
            ApplyRendererFloat(_renderers, ActivationBloomBoostProperty, multiplier);
        }

        private void ApplyPulseOpacity(float opacity)
        {
            if (_activationPulseRenderer == null)
            {
                return;
            }

            _activationPulseRenderer.GetPropertyBlock(_properties);
            _properties.SetFloat(OpacityProperty, opacity);
            _activationPulseRenderer.SetPropertyBlock(_properties);
        }

        private void ApplyRendererFloat(Renderer[] renderers, int propertyId, float value)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_properties);
                _properties.SetFloat(propertyId, value);
                renderer.SetPropertyBlock(_properties);
            }
        }
    }
}
