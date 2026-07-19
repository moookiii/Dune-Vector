using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DisallowMultipleComponent]
    public sealed class DuneVectorCloudField : MonoBehaviour
    {
        private readonly List<Transform> _clusters = new List<Transform>();
        private readonly List<Mesh> _generatedMeshes = new List<Mesh>();
        private static Mesh _sphereMesh;
        private float _driftSpeed;
        private Vector2 _driftDirection;

        public void Initialize(
            Material sunlitMaterial,
            Material underbellyMaterial,
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
            float minimumClusterScale = Mathf.Max(0.1f, tuning.MinimumClusterScale);
            float maximumClusterScale = Mathf.Max(minimumClusterScale, tuning.MaximumClusterScale);
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
                cluster.localScale = Vector3.one * Range(random, minimumClusterScale, maximumClusterScale);

                int lobeCount = random.Next(minimumLobes, maximumLobesExclusive);
                List<CombineInstance> underbellyLobes = new List<CombineInstance>(lobeCount);
                List<CombineInstance> sunlitLobes = new List<CombineInstance>(lobeCount);
                for (int lobeIndex = 0; lobeIndex < lobeCount; lobeIndex++)
                {
                    float angle = Range(random, 0f, Mathf.PI * 2f);
                    float radius = lobeIndex == 0 ? 0f : Mathf.Sqrt((float)random.NextDouble());
                    float x01 = (Mathf.Cos(angle) * radius + 1f) * 0.5f;
                    float z01 = (Mathf.Sin(angle) * radius + 1f) * 0.5f;
                    float height01 = Mathf.Clamp01(((1f - radius) * 0.7f) + (Range(random, 0f, 1f) * 0.3f));
                    Vector3 lobePosition = new Vector3(
                        Mathf.Lerp(tuning.MinimumLobeOffset.x, tuning.MaximumLobeOffset.x, x01),
                        Mathf.Lerp(tuning.MinimumLobeOffset.y, tuning.MaximumLobeOffset.y, height01),
                        Mathf.Lerp(tuning.MinimumLobeOffset.z, tuning.MaximumLobeOffset.z, z01));
                    Vector3 lobeScale = new Vector3(
                        Mathf.Max(0.1f, Range(random, tuning.MinimumLobeScale.x, tuning.MaximumLobeScale.x)),
                        Mathf.Max(0.1f, Range(random, tuning.MinimumLobeScale.y, tuning.MaximumLobeScale.y)),
                        Mathf.Max(0.1f, Range(random, tuning.MinimumLobeScale.z, tuning.MaximumLobeScale.z)));

                    float edgeScale = Mathf.Lerp(1f, tuning.EdgeLobeScaleMultiplier, radius);
                    lobeScale *= edgeScale;
                    if (lobeIndex == 0)
                    {
                        lobeScale *= Mathf.Max(0.1f, tuning.CoreLobeScaleMultiplier);
                    }

                    bool isCrownLobe = lobeIndex > 0 &&
                        random.NextDouble() < Mathf.Clamp01(tuning.CrownLobeChance);
                    if (isCrownLobe)
                    {
                        float minimumCrownHeight = Mathf.Min(tuning.CrownHeightRange.x, tuning.CrownHeightRange.y);
                        float maximumCrownHeight = Mathf.Max(tuning.CrownHeightRange.x, tuning.CrownHeightRange.y);
                        lobePosition.y += Range(random, minimumCrownHeight, maximumCrownHeight) * (1f - radius * 0.45f);
                        lobeScale = Vector3.Scale(lobeScale, PositiveScale(tuning.CrownScaleMultiplier));
                    }

                    underbellyLobes.Add(CreateLobeInstance(lobePosition, lobeScale));
                    sunlitLobes.Add(CreateLobeInstance(
                        lobePosition + (Vector3.up * Mathf.Max(0f, tuning.SunlitLayerLift)),
                        Vector3.Scale(lobeScale, PositiveScale(tuning.SunlitLayerScale))));
                }

                CreateLayer("Cloud Underbelly", cluster, underbellyLobes, underbellyMaterial);
                CreateLayer("Cloud Sunlit Layer", cluster, sunlitLobes, sunlitMaterial);

                _clusters.Add(cluster);
            }
        }

        private static CombineInstance CreateLobeInstance(Vector3 localPosition, Vector3 localScale)
        {
            return new CombineInstance
            {
                mesh = GetSphereMesh(),
                transform = Matrix4x4.TRS(localPosition, Quaternion.identity, localScale),
            };
        }

        private void CreateLayer(
            string name,
            Transform parent,
            List<CombineInstance> lobes,
            Material material)
        {
            GameObject layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            Mesh mesh = new Mesh { name = $"{name} Mesh" };
            mesh.CombineMeshes(lobes.ToArray(), true, true, false);
            _generatedMeshes.Add(mesh);

            MeshFilter filter = layer.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static Mesh GetSphereMesh()
        {
            if (_sphereMesh != null)
            {
                return _sphereMesh;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
            return _sphereMesh;
        }

        private static Vector3 PositiveScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Max(0.1f, scale.x),
                Mathf.Max(0.1f, scale.y),
                Mathf.Max(0.1f, scale.z));
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _generatedMeshes.Count; i++)
            {
                if (_generatedMeshes[i] != null)
                {
                    Destroy(_generatedMeshes[i]);
                }
            }
            _generatedMeshes.Clear();
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
