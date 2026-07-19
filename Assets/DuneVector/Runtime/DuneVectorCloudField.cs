using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorCloudField : MonoBehaviour
    {
        private readonly List<Transform> _clusters = new List<Transform>();
        private float _fieldRadius;
        private float _driftSpeed;
        private Vector2 _driftDirection;
        private DesertWorldStreamer _world;

        public void BindWorld(DesertWorldStreamer world)
        {
            if (_world != null)
            {
                _world.WorldShifted -= ApplyWorldShift;
            }
            _world = world;
            if (_world != null)
            {
                _world.WorldShifted += ApplyWorldShift;
            }
        }

        private void ApplyWorldShift(Vector3 shift)
        {
            transform.position += shift;
        }

        public void Initialize(Material material, int clusterCount, float altitude, float fieldRadius, float driftSpeed)
        {
            _fieldRadius = Mathf.Max(50f, fieldRadius);
            _driftSpeed = Mathf.Max(0f, driftSpeed);
            _driftDirection = new Vector2(0.82f, 0.57f).normalized;

            System.Random random = new System.Random(7319);
            for (int clusterIndex = 0; clusterIndex < Mathf.Max(1, clusterCount); clusterIndex++)
            {
                GameObject clusterObject = new GameObject($"Cloud Cluster {clusterIndex + 1:00}");
                Transform cluster = clusterObject.transform;
                cluster.SetParent(transform, false);
                cluster.localPosition = new Vector3(
                    Range(random, -_fieldRadius, _fieldRadius),
                    altitude + Range(random, -18f, 22f),
                    Range(random, -_fieldRadius, _fieldRadius));
                cluster.localRotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);

                int lobeCount = random.Next(4, 8);
                for (int lobeIndex = 0; lobeIndex < lobeCount; lobeIndex++)
                {
                    GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    lobe.name = $"Cloud Lobe {lobeIndex + 1:00}";
                    lobe.transform.SetParent(cluster, false);
                    lobe.transform.localPosition = new Vector3(
                        Range(random, -13f, 13f),
                        Range(random, -2.5f, 4.5f),
                        Range(random, -7f, 7f));
                    lobe.transform.localScale = new Vector3(
                        Range(random, 12f, 24f),
                        Range(random, 4.5f, 8.5f),
                        Range(random, 8f, 17f));

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
                Vector3 localPosition = cluster.localPosition + drift;
                if (localPosition.x > _fieldRadius) localPosition.x -= _fieldRadius * 2f;
                if (localPosition.x < -_fieldRadius) localPosition.x += _fieldRadius * 2f;
                if (localPosition.z > _fieldRadius) localPosition.z -= _fieldRadius * 2f;
                if (localPosition.z < -_fieldRadius) localPosition.z += _fieldRadius * 2f;
                cluster.localPosition = localPosition;
            }
        }

        private static float Range(System.Random random, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= ApplyWorldShift;
            }
        }
    }
}
