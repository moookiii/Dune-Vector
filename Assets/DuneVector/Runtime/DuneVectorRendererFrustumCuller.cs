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
            public bool AppliedForceRenderingOff;
        }

        private readonly Plane[] _frustumPlanes = new Plane[6];
        private readonly List<TrackedRenderer> _trackedRenderers = new List<TrackedRenderer>();
        private readonly HashSet<EntityId> _trackedEntityIds = new HashSet<EntityId>();

        private Camera _camera;
        private RendererFrustumCullingTuning _settings;
        private float _nextRendererRefreshTime;
        private bool _cullingWasActive;
        private int _sliceCursor;

        public void Initialize(Camera targetCamera, RendererFrustumCullingTuning settings)
        {
            _camera = targetCamera;
            _settings = settings;
        }

        private void LateUpdate()
        {
            bool shouldCull = _camera != null && _settings != null && _settings.Enabled;
            if (!shouldCull)
            {
                if (_cullingWasActive)
                {
                    RestoreRendererOverrides();
                    _trackedRenderers.Clear();
                }

                _cullingWasActive = false;
                return;
            }

            if (!_cullingWasActive)
            {
                _cullingWasActive = true;
                RefreshRenderers();
            }
            else if (Time.unscaledTime >= _nextRendererRefreshTime)
            {
                RefreshRenderers();
            }

            int slices = Mathf.Max(1, _settings.FrustumTestSlicesPerCycle);
            if (_sliceCursor >= slices)
            {
                _sliceCursor = 0;
            }

            int count = _trackedRenderers.Count;
            if (count == 0)
            {
                _sliceCursor++;
                return;
            }

            GeometryUtility.CalculateFrustumPlanes(_camera, _frustumPlanes);
            ExpandFrustum(_frustumPlanes, _settings.Padding);

            for (int i = _sliceCursor; i < count; i += slices)
            {
                TrackedRenderer tracked = _trackedRenderers[i];
                Renderer renderer = tracked.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                bool shouldForceOff = !GeometryUtility.TestPlanesAABB(_frustumPlanes, renderer.bounds);
                if (shouldForceOff == tracked.AppliedForceRenderingOff)
                {
                    continue;
                }

                renderer.forceRenderingOff = shouldForceOff;
                tracked.AppliedForceRenderingOff = shouldForceOff;
            }

            _sliceCursor++;
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

            Renderer[] sceneRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < sceneRenderers.Length; i++)
            {
                Renderer renderer = sceneRenderers[i];
                if (renderer.forceRenderingOff || !renderer.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (!_trackedEntityIds.Add(renderer.GetEntityId()))
                {
                    continue;
                }

                _trackedRenderers.Add(new TrackedRenderer
                {
                    Renderer = renderer,
                    AppliedForceRenderingOff = false,
                });
            }

            float refreshInterval = _settings != null ? _settings.RendererRefreshInterval : 0f;
            _nextRendererRefreshTime = Time.unscaledTime + refreshInterval;
        }

        private static void ExpandFrustum(Plane[] planes, float padding)
        {
            if (padding <= 0f)
            {
                return;
            }

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
                if (tracked.Renderer != null && tracked.AppliedForceRenderingOff)
                {
                    tracked.Renderer.forceRenderingOff = false;
                }

                tracked.AppliedForceRenderingOff = false;
            }
        }
    }
}
