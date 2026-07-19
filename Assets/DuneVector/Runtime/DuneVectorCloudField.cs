using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorCloudField : MonoBehaviour
    {
        private readonly List<Transform> _clusters = new List<Transform>();
        private float _driftSpeed;
        private Vector2 _driftDirection;

        public void Initialize(
            Material material,
            int clusterCount,
            float chunkSize,
            CloudTuning tuning,
            int randomSeed)
        {
            _driftSpeed = Mathf.Max(0f, tuning.DriftSpeed);
            _driftDirection = tuning.DriftDirection.sqrMagnitude > 0.0001f
                ? tuning.DriftDirection.normalized
                : Vector2.zero;

            System.Random random = new System.Random(randomSeed);
            int minimumLobes = Mathf.Max(1, tuning.MinimumLobes);
            int maximumLobesExclusive = Mathf.Max(minimumLobes, tuning.MaximumLobes) + 1;
            for (int clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
            {
                GameObject clusterObject = new GameObject($"Cloud Cluster {clusterIndex + 1:00}");
                Transform cluster = clusterObject.transform;
                cluster.SetParent(transform, false);
                cluster.localPosition = new Vector3(
                    Range(random, 0f, chunkSize),
                    tuning.Altitude + Range(random, -tuning.AltitudeVariation, tuning.AltitudeVariation),
                    Range(random, 0f, chunkSize));
                cluster.localRotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);

                int lobeCount = random.Next(minimumLobes, maximumLobesExclusive);
                for (int lobeIndex = 0; lobeIndex < lobeCount; lobeIndex++)
                {
                    GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    lobe.name = $"Cloud Lobe {lobeIndex + 1:00}";
                    lobe.transform.SetParent(cluster, false);
                    lobe.transform.localPosition = new Vector3(
                        Range(random, tuning.MinimumLobeOffset.x, tuning.MaximumLobeOffset.x),
                        Range(random, tuning.MinimumLobeOffset.y, tuning.MaximumLobeOffset.y),
                        Range(random, tuning.MinimumLobeOffset.z, tuning.MaximumLobeOffset.z));
                    lobe.transform.localScale = new Vector3(
                        Range(random, tuning.MinimumLobeScale.x, tuning.MaximumLobeScale.x),
                        Range(random, tuning.MinimumLobeScale.y, tuning.MaximumLobeScale.y),
                        Range(random, tuning.MinimumLobeScale.z, tuning.MaximumLobeScale.z));

                    Collider collider = lobe.GetComponent<Collider>();
                    if (collider != null)
                    {
                        collider.enabled = false;
                    }

                    MeshRenderer renderer = lobe.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }

                _clusters.Add(cluster);
            }
        }

        private void LateUpdate()
        {
            Vector3 drift = new Vector3(_driftDirection.x, 0f, _driftDirection.y) * (_driftSpeed * Time.deltaTime);
            for (int i = 0; i < _clusters.Count; i++)
            {
                Transform cluster = _clusters[i];
                cluster.localPosition += drift;
            }
        }

        private static float Range(System.Random random, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }
    }
}
