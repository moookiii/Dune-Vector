using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DroneTrailHorizontalEmissionGate : MonoBehaviour
    {
        private sealed class DistanceEmitterState
        {
            public ParticleSystem ParticleSystem;
            public bool UsesDistanceEmission;
            public ParticleSystem.Particle[] Particles = new ParticleSystem.Particle[32];
            public readonly Dictionary<uint, Color32> OriginalColors = new Dictionary<uint, Color32>();
            public readonly HashSet<uint> ActiveSeeds = new HashSet<uint>();
            public readonly List<uint> ExpiredSeeds = new List<uint>();
        }

        private readonly List<DistanceEmitterState> _distanceEmitters = new List<DistanceEmitterState>();
        private float _minimumHorizontalSpeed;
        private float _minimumEffectDistance;
        private float _nearCameraHiddenDistance;
        private float _nearCameraFadeEndDistance;
        private Transform _clearanceOrigin;
        private Camera _camera;
        private Vector3 _previousPosition;
        private bool _initialized;
        private bool _emissionEnabled = true;

        public void Initialize(
            float minimumHorizontalSpeed,
            float minimumEffectDistance,
            float nearCameraHiddenDistance,
            float nearCameraFadeEndDistance,
            Transform clearanceOrigin)
        {
            _minimumHorizontalSpeed = Mathf.Max(0f, minimumHorizontalSpeed);
            _minimumEffectDistance = Mathf.Max(0f, minimumEffectDistance);
            _nearCameraHiddenDistance = Mathf.Max(0f, nearCameraHiddenDistance);
            _nearCameraFadeEndDistance = Mathf.Max(
                _nearCameraHiddenDistance + 0.01f,
                nearCameraFadeEndDistance);
            _clearanceOrigin = clearanceOrigin != null ? clearanceOrigin : transform;
            _camera = Camera.main;
            _distanceEmitters.Clear();

            ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem != null)
                {
                    _distanceEmitters.Add(new DistanceEmitterState
                    {
                        ParticleSystem = particleSystem,
                        UsesDistanceEmission = particleSystem.emission.rateOverDistanceMultiplier > 0f,
                    });
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
            ApplyParticleVisibility();
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
                ParticleSystem particleSystem = _distanceEmitters[i].ParticleSystem;
                if (particleSystem == null || !_distanceEmitters[i].UsesDistanceEmission)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = particleSystem.emission;
                emission.enabled = enabled;
            }
        }

        private void ApplyParticleVisibility()
        {
            float clearanceSqr = _minimumEffectDistance * _minimumEffectDistance;
            Vector3 origin = _clearanceOrigin.position;
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
            }
            Vector3 cameraPosition = _camera != null ? _camera.transform.position : Vector3.zero;

            for (int i = 0; i < _distanceEmitters.Count; i++)
            {
                DistanceEmitterState state = _distanceEmitters[i];
                ParticleSystem particleSystem = state.ParticleSystem;
                if (particleSystem == null)
                {
                    continue;
                }

                int particleCount = particleSystem.particleCount;
                if (state.Particles.Length < particleCount)
                {
                    state.Particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(particleCount)];
                }

                int count = particleSystem.GetParticles(state.Particles);
                state.ActiveSeeds.Clear();
                ParticleSystem.MainModule main = particleSystem.main;
                for (int particleIndex = 0; particleIndex < count; particleIndex++)
                {
                    ParticleSystem.Particle particle = state.Particles[particleIndex];
                    uint seed = particle.randomSeed;
                    state.ActiveSeeds.Add(seed);

                    if (!state.OriginalColors.TryGetValue(seed, out Color32 originalColor))
                    {
                        originalColor = particle.startColor;
                        state.OriginalColors.Add(seed, originalColor);
                    }

                    Vector3 worldPosition = GetParticleWorldPosition(particleSystem, main, particle.position);
                    Color32 displayColor = originalColor;
                    if ((worldPosition - origin).sqrMagnitude < clearanceSqr)
                    {
                        displayColor.a = 0;
                    }
                    else if (_camera != null)
                    {
                        float cameraDistance = Vector3.Distance(worldPosition, cameraPosition);
                        float cameraVisibility = Mathf.SmoothStep(
                            0f,
                            1f,
                            Mathf.InverseLerp(
                                _nearCameraHiddenDistance,
                                _nearCameraFadeEndDistance,
                                cameraDistance));
                        displayColor.a = (byte)Mathf.RoundToInt(originalColor.a * cameraVisibility);
                    }

                    particle.startColor = displayColor;
                    state.Particles[particleIndex] = particle;
                }

                particleSystem.SetParticles(state.Particles, count);
                RemoveExpiredParticleColors(state);
            }
        }

        private static Vector3 GetParticleWorldPosition(
            ParticleSystem particleSystem,
            ParticleSystem.MainModule main,
            Vector3 particlePosition)
        {
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
            {
                return particlePosition;
            }

            Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Custom
                ? main.customSimulationSpace
                : particleSystem.transform;
            return space != null ? space.TransformPoint(particlePosition) : particlePosition;
        }

        private static void RemoveExpiredParticleColors(DistanceEmitterState state)
        {
            state.ExpiredSeeds.Clear();
            foreach (uint seed in state.OriginalColors.Keys)
            {
                if (!state.ActiveSeeds.Contains(seed))
                {
                    state.ExpiredSeeds.Add(seed);
                }
            }

            for (int i = 0; i < state.ExpiredSeeds.Count; i++)
            {
                state.OriginalColors.Remove(state.ExpiredSeeds[i]);
            }
        }
    }
}
