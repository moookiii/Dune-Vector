using System.Collections.Generic;
using UnityEngine;

namespace DuneVector
{
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorRendererFrustumCuller : MonoBehaviour
    {
        private sealed class TrackedRenderer
        {
            public Renderer Renderer;
            public bool OriginalForceRenderingOff;
        }

        private readonly Plane[] _frustumPlanes = new Plane[6];
        private readonly List<TrackedRenderer> _trackedRenderers = new List<TrackedRenderer>();
        private readonly HashSet<EntityId> _trackedEntityIds = new HashSet<EntityId>();

        private Camera _camera;
        private RendererFrustumCullingTuning _settings;
        private float _nextRendererRefreshTime;
        private bool _cullingWasActive;

        public void Initialize(Camera targetCamera, RendererFrustumCullingTuning settings)
        {
            _camera = targetCamera;
            _settings = settings;
            RefreshRenderers();
        }

        private void LateUpdate()
        {
            bool shouldCull = _camera != null && _settings != null && _settings.Enabled;
            if (!shouldCull)
            {
                if (_cullingWasActive)
                {
                    RestoreRendererOverrides();
                }

                _cullingWasActive = false;
                return;
            }

            _cullingWasActive = true;
            if (Time.unscaledTime >= _nextRendererRefreshTime)
            {
                RefreshRenderers();
            }

            GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanes);
            ExpandFrustum(_frustumPlanes, _settings.Padding);

            for (int i = _trackedRenderers.Count - 1; i >= 0; i--)
            {
                TrackedRenderer tracked = _trackedRenderers[i];
                Renderer renderer = tracked.Renderer;
                if (renderer == null)
                {
                    _trackedRenderers.RemoveAt(i);
                    continue;
                }

                bool insidePaddedFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, renderer.bounds);
                renderer.forceRenderingOff = tracked.OriginalForceRenderingOff || !insidePaddedFrustum;
            }
        }

        private void RefreshRenderers()
        {
            _trackedEntityIds.Clear();
            for (int i = _trackedRenderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = _trackedRenderers[i].Renderer;
                if (renderer == null)
                {
                    _trackedRenderers.RemoveAt(i);
                    continue;
                }

                _trackedEntityIds.Add(renderer.GetEntityId());
            }

            Renderer[] sceneRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < sceneRenderers.Length; i++)
            {
                Renderer renderer = sceneRenderers[i];
                if (!renderer.gameObject.scene.IsValid() || !_trackedEntityIds.Add(renderer.GetEntityId()))
                {
                    continue;
                }

                _trackedRenderers.Add(new TrackedRenderer
                {
                    Renderer = renderer,
                    OriginalForceRenderingOff = renderer.forceRenderingOff,
                });
            }

            float refreshInterval = _settings != null ? _settings.RendererRefreshInterval : 0f;
            _nextRendererRefreshTime = Time.unscaledTime + refreshInterval;
        }

        private static void ExpandFrustum(Plane[] planes, float padding)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                Plane plane = planes[i];
                plane.distance += padding;
                planes[i] = plane;
            }
        }

        private void OnDisable()
        {
            RestoreRendererOverrides();
            _cullingWasActive = false;
        }

        private void OnDestroy()
        {
            RestoreRendererOverrides();
        }

        private void RestoreRendererOverrides()
        {
            for (int i = 0; i < _trackedRenderers.Count; i++)
            {
                TrackedRenderer tracked = _trackedRenderers[i];
                if (tracked.Renderer != null)
                {
                    tracked.Renderer.forceRenderingOff = tracked.OriginalForceRenderingOff;
                }
            }
        }
    }
}
