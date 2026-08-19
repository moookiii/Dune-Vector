using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DroneTrailHorizontalEmissionGate : MonoBehaviour
    {
        private readonly List<ParticleSystem> _distanceEmitters = new List<ParticleSystem>();
        private float _minimumHorizontalSpeed;
        private Vector3 _previousPosition;
        private bool _initialized;
        private bool _emissionEnabled = true;

        public void Initialize(float minimumHorizontalSpeed)
        {
            _minimumHorizontalSpeed = Mathf.Max(0f, minimumHorizontalSpeed);
            _distanceEmitters.Clear();

            ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem != null && particleSystem.emission.rateOverDistanceMultiplier > 0f)
                {
                    _distanceEmitters.Add(particleSystem);
                }
            }

            _previousPosition = transform.position;
            _initialized = true;
            SetEmissionEnabled(false);
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            Vector3 position = transform.position;
            Vector3 displacement = position - _previousPosition;
            _previousPosition = position;

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            float horizontalSpeed = new Vector2(displacement.x, displacement.z).magnitude / deltaTime;
            SetEmissionEnabled(horizontalSpeed >= _minimumHorizontalSpeed);
        }

        private void SetEmissionEnabled(bool enabled)
        {
            if (_emissionEnabled == enabled)
            {
                return;
            }

            _emissionEnabled = enabled;
            for (int i = 0; i < _distanceEmitters.Count; i++)
            {
                ParticleSystem particleSystem = _distanceEmitters[i];
                if (particleSystem == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = particleSystem.emission;
                emission.enabled = enabled;
            }
        }
    }
}
