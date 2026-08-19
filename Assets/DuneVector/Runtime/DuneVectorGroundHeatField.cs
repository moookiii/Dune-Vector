using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    [DefaultExecutionOrder(920)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorGroundHeatFieldSystem : MonoBehaviour
    {
        private DesertWorldStreamer _world;
        private GroundHeatFieldTuning _settings;
        private GameObject _fieldRoot;
        private Material _groundMaterial;
        private Material _plumeMaterial;
        private Mesh _groundMesh;
        private Mesh _heatVeilMesh;
        private Texture2D _distortionTexture;
        private ParticleSystem _plumes;
        private Vector2Int _fieldCell = new Vector2Int(int.MinValue, int.MinValue);

        public void Initialize(DesertWorldStreamer world, GroundHeatFieldTuning settings)
        {
            _world = world;
            _settings = settings;
            _distortionTexture = CreateDistortionTexture(Mathf.Max(16, settings.DistortionTextureResolution));
            _groundMaterial = CreateGroundMaterial();
            _plumeMaterial = CreatePlumeMaterial();
            _world.WorldShifted += HandleWorldShift;
            RefreshField();
        }

        private void Update()
        {
            if (_world == null || _settings == null || !_settings.Enabled)
            {
                return;
            }

            RefreshField();
            if (_groundMaterial != null && _groundMaterial.HasProperty("_DistortionVectorMap"))
            {
                _groundMaterial.SetTextureOffset(
                    "_DistortionVectorMap",
                    _settings.DistortionScrollVelocity * Time.time);
            }
        }

        private void RefreshField()
        {
            LogicalPosition player = _world.LogicalPlayerPosition;
            float recenterDistance = Mathf.Max(5f, _settings.RecenterDistance);
            Vector2Int cell = new Vector2Int(
                Mathf.FloorToInt((float)(player.X / recenterDistance)),
                Mathf.FloorToInt((float)(player.Z / recenterDistance)));
            if (cell == _fieldCell)
            {
                return;
            }

            _fieldCell = cell;
            RebuildField(new LogicalPosition(
                (cell.x + 0.5d) * recenterDistance,
                (cell.y + 0.5d) * recenterDistance));
        }

        private void RebuildField(LogicalPosition logicalCenter)
        {
            if (_fieldRoot != null)
            {
                Destroy(_fieldRoot);
            }
            if (_groundMesh != null)
            {
                Destroy(_groundMesh);
            }
            if (_heatVeilMesh != null)
            {
                Destroy(_heatVeilMesh);
            }

            float radius = Mathf.Max(20f, _settings.FollowRadius);
            Vector3 center = _world.LogicalToLocal(logicalCenter.X, 0d, logicalCenter.Z);
            center.y = _world.SampleHeightAtLocal(center.x, center.z);
            _fieldRoot = new GameObject("Player-Centered Ground Heat Field");
            _fieldRoot.transform.SetParent(transform, true);
            _fieldRoot.transform.position = center;

            _groundMesh = CreateGroundMirageMesh(center, radius * _settings.RadiusMultiplier);
            int shellCount = Mathf.Max(1, _settings.DistortionShellCount);
            for (int shell = 0; shell < shellCount; shell++)
            {
                Renderer renderer = CreateMeshRenderer(
                    $"Terrain-Following Ground Distortion Shell {shell + 1}",
                    _fieldRoot.transform,
                    _groundMesh,
                    _groundMaterial);
                renderer.transform.localPosition = Vector3.up * (_settings.DistortionShellSpacing * shell);
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                properties.SetFloat(
                    "_ShellStrengthMultiplier",
                    Mathf.Pow(_settings.DistortionShellStrengthFalloff, shell));
                renderer.SetPropertyBlock(properties);
            }

            _heatVeilMesh = CreateHeatVeilMesh(center, radius);
            Renderer veil = CreateMeshRenderer(
                "Continuous Dune-Rising Heat Veils",
                _fieldRoot.transform,
                _heatVeilMesh,
                _groundMaterial);
            MaterialPropertyBlock veilProperties = new MaterialPropertyBlock();
            veilProperties.SetFloat("_VerticalVeil", 1f);
            veil.SetPropertyBlock(veilProperties);
            _plumes = CreateHeatPlumes(_fieldRoot.transform, radius);
        }

        private Mesh CreateGroundMirageMesh(Vector3 center, float radius)
        {
            int rings = Mathf.Max(2, _settings.GroundMirageRings);
            int segments = Mathf.Max(8, _settings.GroundMirageSegments);
            Vector3[] vertices = new Vector3[1 + (rings * segments)];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[((rings - 1) * segments * 6) + (segments * 3)];
            vertices[0] = new Vector3(0f, _settings.GroundMirageHeightOffset, 0f);
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int ring = 1; ring <= rings; ring++)
            {
                float ringRadius = radius * ring / rings;
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = Mathf.PI * 2f * segment / segments;
                    float x = Mathf.Cos(angle) * ringRadius;
                    float z = Mathf.Sin(angle) * ringRadius;
                    float terrain = _world.SampleHeightAtLocal(center.x + x, center.z + z);
                    int index = 1 + ((ring - 1) * segments) + segment;
                    vertices[index] = new Vector3(x, terrain - center.y + _settings.GroundMirageHeightOffset, z);
                    uvs[index] = new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f);
                }
            }

            int triangle = 0;
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles[triangle++] = 0;
                triangles[triangle++] = 1 + next;
                triangles[triangle++] = 1 + segment;
            }
            for (int ring = 1; ring < rings; ring++)
            {
                int inner = 1 + ((ring - 1) * segments);
                int outer = 1 + (ring * segments);
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    triangles[triangle++] = inner + segment;
                    triangles[triangle++] = inner + next;
                    triangles[triangle++] = outer + segment;
                    triangles[triangle++] = outer + segment;
                    triangles[triangle++] = inner + next;
                    triangles[triangle++] = outer + next;
                }
            }
            Mesh mesh = new Mesh { name = "Terrain-Following Ground Heat Field" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh CreateHeatVeilMesh(Vector3 center, float fieldRadius)
        {
            int rings = Mathf.Max(1, _settings.HeatVeilRingCount);
            int segments = Mathf.Max(16, _settings.HeatVeilSegments);
            Vector3[] vertices = new Vector3[rings * segments * 4];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[rings * segments * 6];
            float maximumRadius = fieldRadius * _settings.RadiusMultiplier;
            float minimumRadius = Mathf.Min(maximumRadius, Mathf.Max(0.25f, _settings.HeatVeilMinimumRadius));
            float minimumHeight = Mathf.Max(0.1f, _settings.HeatVeilMinimumHeight);
            float maximumHeight = Mathf.Max(minimumHeight, _settings.HeatVeilMaximumHeight);
            for (int ring = 0; ring < rings; ring++)
            {
                float progress = rings == 1 ? 0f : ring / (rings - 1f);
                float radius = Mathf.Lerp(
                    minimumRadius,
                    maximumRadius,
                    Mathf.Pow(progress, Mathf.Max(0.25f, _settings.HeatVeilRadiusDistribution)));
                float height = DuneVectorMath.HashRange(
                    _fieldCell.x, _fieldCell.y, ring, _settings.RandomSeedOffset + 101,
                    minimumHeight, maximumHeight);
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    float angle = Mathf.PI * 2f * segment / segments;
                    float nextAngle = Mathf.PI * 2f * next / segments;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    float nextX = Mathf.Cos(nextAngle) * radius;
                    float nextZ = Mathf.Sin(nextAngle) * radius;
                    float terrain = _world.SampleHeightAtLocal(center.x + x, center.z + z) - center.y;
                    float nextTerrain = _world.SampleHeightAtLocal(center.x + nextX, center.z + nextZ) - center.y;
                    int vertex = ((ring * segments) + segment) * 4;
                    int tri = ((ring * segments) + segment) * 6;
                    vertices[vertex] = new Vector3(x, terrain + _settings.HeatVeilBaseOffset, z);
                    vertices[vertex + 1] = new Vector3(nextX, nextTerrain + _settings.HeatVeilBaseOffset, nextZ);
                    vertices[vertex + 2] = vertices[vertex] + (Vector3.up * height);
                    vertices[vertex + 3] = vertices[vertex + 1] + (Vector3.up * height);
                    float u = segment * 0.22f;
                    uvs[vertex] = new Vector2(u, 0f);
                    uvs[vertex + 1] = new Vector2(u + 0.22f, 0f);
                    uvs[vertex + 2] = new Vector2(u, 1f);
                    uvs[vertex + 3] = new Vector2(u + 0.22f, 1f);
                    triangles[tri] = vertex;
                    triangles[tri + 1] = vertex + 2;
                    triangles[tri + 2] = vertex + 1;
                    triangles[tri + 3] = vertex + 1;
                    triangles[tri + 4] = vertex + 2;
                    triangles[tri + 5] = vertex + 3;
                }
            }
            Mesh mesh = new Mesh { name = "Continuous Dune-Rising Heat Veils" };
            mesh.indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private ParticleSystem CreateHeatPlumes(Transform parent, float radius)
        {
            GameObject plumeObject = new GameObject("Sparse Rising Heat Columns");
            plumeObject.transform.SetParent(parent, false);
            ParticleSystem system = plumeObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, _settings.PlumeParticleBudget);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                _settings.PlumeMinimumLifetime,
                Mathf.Max(_settings.PlumeMinimumLifetime, _settings.PlumeMaximumLifetime));
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(_settings.PlumeMinimumSize, _settings.PlumeMaximumSize);
            main.startSizeY = new ParticleSystem.MinMaxCurve(
                _settings.PlumeMinimumSize * _settings.PlumeMinimumHeightMultiplier,
                _settings.PlumeMaximumSize * _settings.PlumeMaximumHeightMultiplier);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = _settings.PlumeEmissionRate;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * _settings.RadiusMultiplier;
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = _settings.PlumeRiseSpeed;
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.PlumeTurbulence;
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, _settings.PlumeLifetimeFadeInFraction),
                    new GradientAlphaKey(1f, _settings.PlumeLifetimeFadeOutFraction),
                    new GradientAlphaKey(0f, 1f),
                });
            fade.color = gradient;
            ParticleSystemRenderer renderer = plumeObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _plumeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
            });
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }

        private Material CreateGroundMaterial()
        {
            Shader shader = Shader.Find("DuneVector/URP Dune Heat Distortion");
            if (shader == null)
            {
                return CreateTransparentFallback("Ground Heat Field Fallback");
            }
            Material material = new Material(shader) { name = "Ground Heat Field" };
            material.SetTexture("_NoiseTex", _distortionTexture);
            material.SetFloat("_DistortionStrength", Mathf.Max(0f, _settings.DistortionStrength));
            material.SetFloat("_DistortionBlur", Mathf.Clamp01(_settings.DistortionBlurStrength));
            material.SetFloat("_TextureScale", Mathf.Max(0.01f, _settings.DistortionTextureScale));
            material.SetVector("_ScrollVelocity", _settings.DistortionScrollVelocity);
            material.SetFloat("_ShimmerOpacity", Mathf.Clamp(_settings.ShimmerOpacity, 0f, 0.3f));
            material.SetColor("_ShimmerColor", _settings.ShimmerColor);
            return material;
        }

        private Material CreatePlumeMaterial()
        {
            Shader shader = Shader.Find("DuneVector/URP Heat Plume Distortion");
            if (shader == null)
            {
                return CreateTransparentFallback("Ground Heat Plume Fallback");
            }
            Material material = new Material(shader) { name = "Ground Heat Plume Refraction" };
            material.SetTexture("_NoiseTex", _distortionTexture);
            material.SetFloat("_DistortionStrength", _settings.PlumeDistortionStrength);
            material.SetFloat("_DistortionBlur", _settings.PlumeDistortionBlur);
            material.SetVector("_PrimaryTiling", _settings.PlumePrimaryTiling);
            material.SetVector("_SecondaryTiling", _settings.PlumeSecondaryTiling);
            material.SetVector("_PrimaryVelocity", _settings.PlumePrimaryVelocity);
            material.SetVector("_SecondaryVelocity", _settings.PlumeSecondaryVelocity);
            material.SetFloat("_SecondaryStrength", _settings.PlumeSecondaryStrength);
            material.SetFloat("_HorizontalTurbulence", _settings.PlumeHorizontalTurbulence);
            material.SetFloat("_CoreWidth", _settings.PlumeCoreWidth);
            material.SetFloat("_TopWidth", _settings.PlumeTopWidth);
            material.SetFloat("_WidthVariation", _settings.PlumeWidthVariation);
            material.SetFloat("_WidthFrequency", _settings.PlumeWidthFrequency);
            material.SetFloat("_SideFeather", _settings.PlumeSideFeather);
            material.SetFloat("_BottomFeather", _settings.PlumeBottomFeather);
            material.SetFloat("_TopFeather", _settings.PlumeTopFeather);
            material.SetFloat("_VerticalDissipationStart", _settings.PlumeVerticalDissipationStart);
            material.SetFloat("_VerticalDissipationPower", _settings.PlumeVerticalDissipationPower);
            material.SetFloat("_Lean", _settings.PlumeMaximumLean);
            material.SetFloat("_MinimumSpeedMultiplier", _settings.PlumeMinimumAnimationSpeedMultiplier);
            material.SetFloat("_MaximumSpeedMultiplier", _settings.PlumeMaximumAnimationSpeedMultiplier);
            material.SetFloat("_MinimumStrengthMultiplier", _settings.PlumeMinimumStrengthMultiplier);
            material.SetFloat("_MaximumStrengthMultiplier", _settings.PlumeMaximumStrengthMultiplier);
            material.SetFloat("_PhaseRange", _settings.PlumePhaseRange);
            material.SetFloat("_PrimaryPhaseOffset", _settings.PlumePrimaryPhaseOffset);
            material.SetFloat("_SecondaryPhaseOffset", _settings.PlumeSecondaryPhaseOffset);
            material.SetFloat("_CardEdgeFeather", _settings.PlumeCardEdgeFeather);
            material.SetFloat("_EdgeNoiseBase", _settings.PlumeEdgeNoiseBase);
            material.SetFloat("_PrimaryEdgeNoise", _settings.PlumePrimaryEdgeNoise);
            material.SetFloat("_SecondaryEdgeNoise", _settings.PlumeSecondaryEdgeNoise);
            material.SetFloat("_FadeProfileVariation", _settings.PlumeFadeProfileVariation);
            material.SetFloat("_DistanceFadeStart", _settings.PlumeDistanceFadeStart);
            material.SetFloat("_DistanceFadeEnd", _settings.PlumeDistanceFadeEnd);
            material.SetFloat("_DetailFadeStart", _settings.PlumeDetailFadeStart);
            material.SetFloat("_DetailFadeEnd", _settings.PlumeDetailFadeEnd);
            material.SetFloat("_DepthFadeDistance", _settings.PlumeDepthFadeDistance);
            material.SetFloat("_MaskClipThreshold", _settings.PlumeMaskClipThreshold);
            return material;
        }

        private static Material CreateTransparentFallback(string name)
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            // _Surface only drives the URP material inspector. A material built at runtime
            // keeps the shader's default One/Zero blend, so a clear base color would still
            // write opaque black over the dunes. Set the blend state the inspector would
            // have written or the fallback mesh paints a black disc around the player.
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetColor("_BaseColor", Color.clear);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Renderer CreateMeshRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject meshObject = new GameObject(name);
            meshObject.transform.SetParent(parent, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static Texture2D CreateDistortionTexture(int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Ground Heat Refraction Vectors",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = y / (float)(resolution - 1);
                float verticalFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(v, 1f - v) * 5f);
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)(resolution - 1);
                    float first = Mathf.PerlinNoise((u * 3.7f) + 11.2f, (v * 8.1f) + 4.6f) - 0.5f;
                    float second = Mathf.PerlinNoise((u * 7.3f) - 2.4f, (v * 4.9f) + 15.8f) - 0.5f;
                    float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 8f) * verticalFade;
                    pixels[(y * resolution) + x] = new Color(
                        0.5f + (first * edgeFade),
                        0.5f + (second * edgeFade),
                        Mathf.Clamp01(Mathf.Abs(first) + Mathf.Abs(second)),
                        edgeFade);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void HandleWorldShift(Vector3 shift)
        {
            if (_fieldRoot != null)
            {
                _fieldRoot.transform.position += shift;
                _plumes?.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            if (_groundMaterial != null) Destroy(_groundMaterial);
            if (_plumeMaterial != null) Destroy(_plumeMaterial);
            if (_groundMesh != null) Destroy(_groundMesh);
            if (_heatVeilMesh != null) Destroy(_heatVeilMesh);
            if (_distortionTexture != null) Destroy(_distortionTexture);
        }
    }
}
