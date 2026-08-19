using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DuneVector
{
    internal sealed class DuneVectorSandAmbusherPalette : IDisposable
    {
        public Material Armor { get; }
        public Material Underside { get; }
        public Material Ridge { get; }
        public Material CreaseSand { get; }
        public Material Fracture { get; }
        public Material Dust { get; }
        public Mesh DebrisMesh { get; }

        private readonly List<Material> _materials = new List<Material>();
        private readonly Texture2D _particleTexture;

        public DuneVectorSandAmbusherPalette(CourierContractTuning settings)
        {
            Armor = CreateLit("Sand Ambusher - Mineral Armor", settings.SandAmbusherArmorColor,
                settings.SandAmbusherArmorSmoothness, settings.SandAmbusherArmorMetallic, settings.SandAmbusherArmorEmission);
            Underside = CreateLit("Sand Ambusher - Worn Underside", settings.SandAmbusherUndersideColor,
                settings.SandAmbusherUndersideSmoothness, settings.SandAmbusherUndersideMetallic, Color.black);
            Ridge = CreateLit("Sand Ambusher - Exposed Ridges", settings.SandAmbusherRidgeColor,
                settings.SandAmbusherRidgeSmoothness, settings.SandAmbusherRidgeMetallic, settings.SandAmbusherRidgeEmission);
            CreaseSand = CreateLit("Sand Ambusher - Crease Sand", settings.SandAmbusherCreaseSandColor,
                settings.SandAmbusherCreaseSandSmoothness, 0f, Color.black);
            Shader fractureShader = Shader.Find("DuneVector/URP Sand Fracture");
            if (fractureShader == null)
            {
                fractureShader = FindFallbackShader();
                Debug.LogError(
                    "Sand Ambusher fracture shader is unavailable. Using a fallback shader so contract and hub initialization can continue.");
            }
            Fracture = new Material(fractureShader) { name = "Sand Ambusher - Branching Fracture" };
            Fracture.SetColor("_Color", settings.SandAmbusherFractureColor);
            Fracture.SetFloat("_EdgeNoiseScale", settings.SandAmbusherFractureEdgeNoiseScale);
            Fracture.SetFloat("_EdgeNoiseStrength", settings.SandAmbusherFractureEdgeNoiseStrength);
            _materials.Add(Fracture);

            _particleTexture = CreateSoftParticleTexture(Mathf.Max(8, settings.SandAmbusherParticleTextureResolution));
            Dust = CreateParticleMaterial("Sand Ambusher - Dust and Sand", settings.SandAmbusherDustColor, _particleTexture);
            DebrisMesh = DuneVectorSandAmbusherMeshUtility.CreateOrganicArmorMesh(
                settings.SandAmbusherDebrisMeshRings,
                settings.SandAmbusherDebrisMeshRadialSegments,
                settings.SandAmbusherDebrisMeshIrregularity,
                settings.SandAmbusherVisualSeed + 991);
            DebrisMesh.name = "Sand Ambusher Displaced Sand Clump";
        }

        public void Dispose()
        {
            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] != null)
                {
                    UnityEngine.Object.Destroy(_materials[i]);
                }
            }
            if (_particleTexture != null)
            {
                UnityEngine.Object.Destroy(_particleTexture);
            }
            if (DebrisMesh != null)
            {
                UnityEngine.Object.Destroy(DebrisMesh);
            }
        }

        private Material CreateLit(string name, Color color, float smoothness, float metallic, Color emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = FindFallbackShader();
            }
            Material material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
            _materials.Add(material);
            return material;
        }

        private Material CreateParticleMaterial(string name, Color color, Texture texture)
        {
            Shader shader = Shader.Find("DuneVector/URP Weather Particle");
            if (shader == null)
            {
                shader = FindFallbackShader();
            }
            Material material = new Material(shader) { name = name };
            material.SetTexture("_MainTex", texture);
            material.SetColor("_Tint", color);
            _materials.Add(material);
            return material;
        }

        private static Shader FindFallbackShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Hidden/InternalErrorShader");
        }

        private static Texture2D CreateSoftParticleTexture(int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Sand Ambusher Soft Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 uv = new Vector2(
                        ((x + 0.5f) / resolution) * 2f - 1f,
                        ((y + 0.5f) / resolution) * 2f - 1f);
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - uv.sqrMagnitude), 2f);
                    pixels[(y * resolution) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }

    internal sealed class DuneVectorSandAmbusherVisual : MonoBehaviour
    {
        private readonly List<Transform> _segments = new List<Transform>();
        private readonly List<Transform> _joints = new List<Transform>();
        private readonly List<Vector3> _segmentBaseScales = new List<Vector3>();
        private readonly List<Vector3> _segmentBaseRotations = new List<Vector3>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();

        private CourierContractTuning _settings;
        private DuneVectorSandAmbusherPalette _palette;
        private Transform _visualRoot;
        private Transform _body;
        private Animator _bodyAnimator;
        private Quaternion _bodyBaseRotation = Quaternion.identity;
        private Transform _leftProng;
        private Transform _rightProng;
        private ParticleSystem _trickle;
        private float _age;
        private float _emergenceAge;
        private float _trickleRemaining;
        private bool _emerging;
        private bool _retreating;

        public void Initialize(CourierContractTuning settings, DuneVectorSandAmbusherPalette palette, int seed)
        {
            _settings = settings;
            _palette = palette;
            BuildCreature(seed);
            BuildTrickleParticles();
        }

        public void BeginEmergence()
        {
            _emerging = true;
            _retreating = false;
            _emergenceAge = 0f;
            _trickleRemaining = Mathf.Max(0f, _settings.SandAmbusherTrickleDuration);
            if (_trickle != null)
            {
                ParticleSystem.EmissionModule emission = _trickle.emission;
                emission.enabled = true;
                _trickle.Play();
            }
        }

        public void PlayAttackAnimation()
        {
            if (_bodyAnimator != null && !string.IsNullOrWhiteSpace(_settings.SandAmbusherJumpAnimatorTrigger))
            {
                _bodyAnimator.SetTrigger(_settings.SandAmbusherJumpAnimatorTrigger.Trim());
            }
        }

        public void BeginRetreat()
        {
            _retreating = true;
            if (_trickle != null)
            {
                ParticleSystem.EmissionModule emission = _trickle.emission;
                emission.enabled = false;
            }
        }

        private void Update()
        {
            if (_settings == null || _visualRoot == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _age += deltaTime;
            if (_emerging)
            {
                _emergenceAge += deltaTime;
            }
            if (_trickleRemaining > 0f)
            {
                _trickleRemaining -= deltaTime;
                if (_trickleRemaining <= 0f && _trickle != null)
                {
                    ParticleSystem.EmissionModule emission = _trickle.emission;
                    emission.enabled = false;
                }
            }

            float swayBlend = _emerging ? Mathf.Clamp01(_emergenceAge / Mathf.Max(0.1f, _settings.SandAmbusherFullSwayBlendDuration)) : 0f;
            if (_body != null)
            {
                UpdatePrefabBody(swayBlend);
                return;
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                float delay = i * Mathf.Max(0f, _settings.SandAmbusherSegmentEmergenceDelay);
                float extension = _emerging
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_emergenceAge - delay) /
                        Mathf.Max(0.01f, _settings.SandAmbusherSegmentExtensionDuration)))
                    : 0f;
                if (_retreating)
                {
                    extension = Mathf.Max(extension, 1f);
                }

                float spacing = Mathf.Max(0.1f, _settings.SandAmbusherSegmentSpacing);
                Vector3 position = CalculateSegmentPosition(i, extension, swayBlend);
                _segments[i].localPosition = position;

                float phase = (_age * Mathf.Max(0f, _settings.SandAmbusherIdleSwayFrequency)) -
                    (i * Mathf.Max(0f, _settings.SandAmbusherSwayPhasePerSegment));
                float sway = Mathf.Sin(phase) * Mathf.Max(0f, _settings.SandAmbusherIdleSwayAmplitude) * swayBlend;
                float crossSway = Mathf.Cos((phase * _settings.SandAmbusherCrossSwayFrequencyMultiplier) + i) *
                    Mathf.Max(0f, _settings.SandAmbusherCrossSwayAmplitude) * swayBlend;
                Vector3 rotation = _segmentBaseRotations[i];
                rotation.z += sway * _settings.SandAmbusherSwayRotationMultiplier;
                rotation.x += crossSway * _settings.SandAmbusherSwayRotationMultiplier;
                _segments[i].localRotation = Quaternion.Euler(rotation);
                Vector3 baseScale = _segmentBaseScales[i];
                float compression = Mathf.Lerp(_settings.SandAmbusherSegmentEmergenceScale, 1f, extension);
                _segments[i].localScale = new Vector3(baseScale.x, baseScale.y * compression, baseScale.z);

                if (i < _joints.Count)
                {
                    float nextDelay = (i + 1) * Mathf.Max(0f, _settings.SandAmbusherSegmentEmergenceDelay);
                    float nextExtension = _emerging
                        ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_emergenceAge - nextDelay) /
                            Mathf.Max(0.01f, _settings.SandAmbusherSegmentExtensionDuration)))
                        : 0f;
                    if (_retreating)
                    {
                        nextExtension = Mathf.Max(nextExtension, 1f);
                    }
                    Vector3 nextPosition = CalculateSegmentPosition(i + 1, nextExtension, swayBlend);
                    Vector3 connection = position - nextPosition;
                    _joints[i].localPosition = (position + nextPosition) * 0.5f;
                    _joints[i].localRotation = connection.sqrMagnitude > 0.001f
                        ? Quaternion.FromToRotation(Vector3.up, connection.normalized)
                        : Quaternion.identity;
                    float jointRadius = Mathf.Lerp(
                        _settings.SandAmbusherJointCompressedScale,
                        _settings.SandAmbusherJointScale,
                        extension);
                    _joints[i].localScale = new Vector3(jointRadius,
                        Mathf.Max(jointRadius, connection.magnitude * _settings.SandAmbusherJointLengthMultiplier),
                        jointRadius);
                }
            }

            float prongAngle = Mathf.Sin(_age * Mathf.Max(0f, _settings.SandAmbusherProngMotionFrequency)) *
                Mathf.Max(0f, _settings.SandAmbusherProngMotionDegrees);
            if (_leftProng != null)
            {
                _leftProng.localRotation = Quaternion.Euler(0f, 0f, -prongAngle);
            }
            if (_rightProng != null)
            {
                _rightProng.localRotation = Quaternion.Euler(0f, 0f, prongAngle * _settings.SandAmbusherProngMotionAsymmetry);
            }
        }

        private Vector3 CalculateSegmentPosition(int index, float extension, float swayBlend)
        {
            float spacing = Mathf.Max(0.1f, _settings.SandAmbusherSegmentSpacing);
            float compressedSpacing = spacing * Mathf.Clamp01(_settings.SandAmbusherSegmentCompressedSpacing);
            Vector3 position = Vector3.down * index * Mathf.Lerp(compressedSpacing, spacing, extension);
            float phase = (_age * Mathf.Max(0f, _settings.SandAmbusherIdleSwayFrequency)) -
                (index * Mathf.Max(0f, _settings.SandAmbusherSwayPhasePerSegment));
            float sway = Mathf.Sin(phase) * Mathf.Max(0f, _settings.SandAmbusherIdleSwayAmplitude) * swayBlend;
            float crossSway = Mathf.Cos((phase * _settings.SandAmbusherCrossSwayFrequencyMultiplier) + index) *
                Mathf.Max(0f, _settings.SandAmbusherCrossSwayAmplitude) * swayBlend;
            position.x += sway * (1f - (index / (float)Mathf.Max(1, _segments.Count)) * _settings.SandAmbusherTailSwayFalloff);
            position.z += crossSway;
            return position;
        }

        private void BuildCreature(int seed)
        {
            System.Random random = new System.Random(seed);
            GameObject visualObject = new GameObject("Sand Ambusher Premium Segmented Visual");
            visualObject.transform.SetParent(transform, false);
            _visualRoot = visualObject.transform;

            GameObject bodyPrefab = ResolveBodyPrefab();
            if (bodyPrefab != null)
            {
                BuildPrefabBody(bodyPrefab);
                return;
            }

            int segmentCount = Mathf.Max(3, _settings.SandAmbusherVisualSegmentCount);
            Mesh jointMesh = DuneVectorSandAmbusherMeshUtility.CreateCapsuleMesh(
                _settings.SandAmbusherJointMeshRadialSegments,
                _settings.SandAmbusherJointMeshHemisphereRings);
            jointMesh.name = "Sand Ambusher High Resolution Articulated Joint";
            _ownedMeshes.Add(jointMesh);
            Mesh ridgeMesh = DuneVectorSandAmbusherMeshUtility.CreateTaperedSpikeMesh(
                _settings.SandAmbusherRidgeMeshLengthSegments,
                _settings.SandAmbusherRidgeMeshRadialSegments,
                _settings.SandAmbusherRidgeTipScale);
            ridgeMesh.name = "Sand Ambusher Tapered Armor Spike";
            _ownedMeshes.Add(ridgeMesh);
            for (int i = 0; i < segmentCount; i++)
            {
                float t = i / (float)Mathf.Max(1, segmentCount - 1);
                float radius = Mathf.Lerp(_settings.SandAmbusherUpperSegmentRadius, _settings.SandAmbusherLowerSegmentRadius, t);
                radius *= Mathf.Lerp(1f - _settings.SandAmbusherSegmentScaleVariation,
                    1f + _settings.SandAmbusherSegmentScaleVariation, (float)random.NextDouble());
                float height = Mathf.Lerp(_settings.SandAmbusherUpperSegmentHeight, _settings.SandAmbusherLowerSegmentHeight, t);
                Vector3 scale = new Vector3(
                    radius * Mathf.Lerp(0.82f, 1.18f, (float)random.NextDouble()),
                    height,
                    radius * Mathf.Lerp(0.82f, 1.18f, (float)random.NextDouble()));
                Vector3 rotation = new Vector3(
                    Mathf.Lerp(-_settings.SandAmbusherSegmentRotationVariation, _settings.SandAmbusherSegmentRotationVariation, (float)random.NextDouble()),
                    Mathf.Lerp(0f, 360f, (float)random.NextDouble()),
                    Mathf.Lerp(-_settings.SandAmbusherSegmentRotationVariation, _settings.SandAmbusherSegmentRotationVariation, (float)random.NextDouble()));

                GameObject segment = new GameObject($"Organic Armor Segment {i + 1}");
                segment.transform.SetParent(_visualRoot, false);
                segment.transform.localPosition = Vector3.down * i * _settings.SandAmbusherSegmentSpacing;
                segment.transform.localRotation = Quaternion.Euler(rotation);
                segment.transform.localScale = scale;
                Mesh mesh = DuneVectorSandAmbusherMeshUtility.CreateOrganicArmorMesh(
                    _settings.SandAmbusherArmorMeshRings,
                    _settings.SandAmbusherArmorMeshRadialSegments,
                    _settings.SandAmbusherArmorIrregularity,
                    seed + (i * 137));
                mesh.name = $"Sand Ambusher Organic Segment {i + 1}";
                _ownedMeshes.Add(mesh);
                MeshFilter filter = segment.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = segment.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { _palette.Armor, _palette.Underside };
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;

                BuildArmorRidges(segment.transform, radius, height, i, random, ridgeMesh);
                BuildCreaseSand(segment.transform, radius, height, i);
                _segments.Add(segment.transform);
                _segmentBaseScales.Add(scale);
                _segmentBaseRotations.Add(rotation);

                if (i < segmentCount - 1)
                {
                    Transform joint = CreateMeshObject(
                        $"Articulated Joint {i + 1}", _visualRoot, jointMesh, _palette.Underside).transform;
                    joint.localPosition = Vector3.down * (i + 0.5f) * _settings.SandAmbusherSegmentSpacing;
                    joint.localScale = Vector3.one * _settings.SandAmbusherJointScale;
                    _joints.Add(joint);
                }
            }

            BuildCrown(seed + 701);
        }

        private GameObject ResolveBodyPrefab()
        {
            if (_settings.SandAmbusherBodyPrefab != null)
            {
                return _settings.SandAmbusherBodyPrefab;
            }
            if (string.IsNullOrWhiteSpace(_settings.SandAmbusherBodyPrefabResourcePath))
            {
                return null;
            }
            return Resources.Load<GameObject>(_settings.SandAmbusherBodyPrefabResourcePath.Trim());
        }

        private void BuildPrefabBody(GameObject prefab)
        {
            GameObject body = Instantiate(prefab, _visualRoot, false);
            body.name = "Sand Ambusher Body";
            _body = body.transform;
            _bodyAnimator = body.GetComponentInChildren<Animator>(true);
            _body.localPosition = _settings.SandAmbusherBodyPrefabLocalPosition;
            _bodyBaseRotation = _body.localRotation *
                Quaternion.Euler(_settings.SandAmbusherBodyPrefabLocalEulerAngles);
            _body.localRotation = _bodyBaseRotation;
            _body.localScale = Vector3.Scale(
                _body.localScale,
                _settings.SandAmbusherBodyPrefabLocalScale);
        }

        private void UpdatePrefabBody(float swayBlend)
        {
            float phase = _age * Mathf.Max(0f, _settings.SandAmbusherIdleSwayFrequency);
            float sway = Mathf.Sin(phase) *
                Mathf.Max(0f, _settings.SandAmbusherBodyPrefabSwayDegrees) * swayBlend;
            float crossSway = Mathf.Cos(phase * _settings.SandAmbusherCrossSwayFrequencyMultiplier) *
                Mathf.Max(0f, _settings.SandAmbusherBodyPrefabSwayDegrees) * swayBlend;
            _body.localRotation = _bodyBaseRotation * Quaternion.Euler(crossSway, 0f, sway);
        }

        private void BuildArmorRidges(Transform parent, float radius, float height, int segmentIndex,
            System.Random random, Mesh ridgeMesh)
        {
            int ridgeCount = Mathf.Max(0, _settings.SandAmbusherRidgesPerSegment);
            for (int ridgeIndex = 0; ridgeIndex < ridgeCount; ridgeIndex++)
            {
                if (segmentIndex > 0 && ridgeIndex == ridgeCount - 1 && random.NextDouble() < _settings.SandAmbusherMissingRidgeChance)
                {
                    continue;
                }
                float angle = ((ridgeIndex / (float)Mathf.Max(1, ridgeCount)) * Mathf.PI * 2f) +
                    Mathf.Lerp(-_settings.SandAmbusherRidgeAngularVariation, _settings.SandAmbusherRidgeAngularVariation,
                        (float)random.NextDouble()) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Transform ridge = CreateMeshObject(
                    $"Tapered Armor Spike {ridgeIndex + 1}", parent, ridgeMesh, _palette.Ridge).transform;
                ridge.localPosition = radial * _settings.SandAmbusherRidgeRadialOffset;
                ridge.localScale = new Vector3(
                    _settings.SandAmbusherRidgeWidth,
                    _settings.SandAmbusherRidgeHeight,
                    _settings.SandAmbusherRidgeDepth);
                ridge.localRotation = Quaternion.Euler(
                    0f, -angle * Mathf.Rad2Deg, _settings.SandAmbusherRidgeTilt);
                ridge.localPosition += Vector3.up * Mathf.Lerp(
                    -_settings.SandAmbusherRidgeVerticalOffset,
                    _settings.SandAmbusherRidgeVerticalOffset,
                    (float)random.NextDouble());
            }
        }

        private void BuildCreaseSand(Transform parent, float radius, float height, int segmentIndex)
        {
            Mesh torus = DuneVectorVisuals.CreateTorusMesh(
                Mathf.Max(0.1f, _settings.SandAmbusherCreaseSandRadius),
                Mathf.Max(0.02f, _settings.SandAmbusherCreaseSandThickness),
                _settings.SandAmbusherCreaseSandMajorSegments,
                _settings.SandAmbusherCreaseSandTubeSegments);
            torus.name = $"Sand Ambusher Crease Sand {segmentIndex + 1}";
            _ownedMeshes.Add(torus);
            GameObject sand = new GameObject("Accumulated Sand in Armor Crease");
            sand.transform.SetParent(parent, false);
            sand.transform.localPosition = Vector3.down * height * _settings.SandAmbusherCreaseSandVerticalPosition;
            sand.transform.localRotation = Quaternion.Euler(_settings.SandAmbusherCreaseSandTilt, segmentIndex * 31f, 0f);
            sand.AddComponent<MeshFilter>().sharedMesh = torus;
            MeshRenderer renderer = sand.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _palette.CreaseSand;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private void BuildCrown(int seed)
        {
            Transform head = _segments[0];
            Mesh leftMesh = DuneVectorSandAmbusherMeshUtility.CreateCurvedProngMesh(
                -1f, _settings, seed);
            Mesh rightMesh = DuneVectorSandAmbusherMeshUtility.CreateCurvedProngMesh(
                1f, _settings, seed + 17);
            _ownedMeshes.Add(leftMesh);
            _ownedMeshes.Add(rightMesh);
            _leftProng = CreateMeshObject("Split Crown - Left Fossil Prong", head, leftMesh, _palette.Ridge).transform;
            _rightProng = CreateMeshObject("Split Crown - Right Fossil Prong", head, rightMesh, _palette.Ridge).transform;

            Mesh crownCoreMesh = DuneVectorSandAmbusherMeshUtility.CreateSphereMesh(
                _settings.SandAmbusherCrownCoreMeshRings,
                _settings.SandAmbusherCrownCoreMeshRadialSegments);
            crownCoreMesh.name = "Sand Ambusher High Resolution Crown Core";
            _ownedMeshes.Add(crownCoreMesh);
            Transform crownCore = CreateMeshObject(
                "Jaw Crown Mineral Core", head, crownCoreMesh, _palette.Underside).transform;
            crownCore.localPosition = Vector3.up * _settings.SandAmbusherCrownBaseHeight;
            crownCore.localScale = new Vector3(
                _settings.SandAmbusherCrownCoreWidth,
                _settings.SandAmbusherCrownCoreHeight,
                _settings.SandAmbusherCrownCoreDepth);
            crownCore.localRotation = Quaternion.Euler(_settings.SandAmbusherCrownCoreTilt, 0f, 0f);
            crownCore.SetAsFirstSibling();
        }

        private void BuildTrickleParticles()
        {
            GameObject trickleObject = new GameObject("Falling Sand from Newly Exposed Armor");
            trickleObject.transform.SetParent(_visualRoot, false);
            _trickle = trickleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _trickle.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, _settings.SandAmbusherTrickleMaximumParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                _settings.SandAmbusherTrickleMinimumLifetime,
                Mathf.Max(_settings.SandAmbusherTrickleMinimumLifetime, _settings.SandAmbusherTrickleMaximumLifetime));
            main.startSize = new ParticleSystem.MinMaxCurve(
                _settings.SandAmbusherTrickleMinimumSize,
                Mathf.Max(_settings.SandAmbusherTrickleMinimumSize, _settings.SandAmbusherTrickleMaximumSize));
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                _settings.SandAmbusherTrickleMinimumSpeed,
                Mathf.Max(_settings.SandAmbusherTrickleMinimumSpeed, _settings.SandAmbusherTrickleMaximumSpeed));
            main.gravityModifier = _settings.SandAmbusherTrickleGravity;
            ParticleSystem.EmissionModule emission = _trickle.emission;
            emission.rateOverTime = Mathf.Max(0f, _settings.SandAmbusherTrickleEmissionRate);
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _trickle.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                _settings.SandAmbusherLowerSegmentRadius * 2f,
                _settings.SandAmbusherSegmentSpacing * _settings.SandAmbusherVisualSegmentCount,
                _settings.SandAmbusherLowerSegmentRadius * 2f);
            ParticleSystemRenderer renderer = trickleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _palette.Dust;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = _settings.SandAmbusherTrickleStretch;
            renderer.velocityScale = _settings.SandAmbusherTrickleVelocityScale;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return result;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _ownedMeshes.Count; i++)
            {
                if (_ownedMeshes[i] != null)
                {
                    Destroy(_ownedMeshes[i]);
                }
            }
        }
    }

    internal sealed class DuneVectorSandAmbusherEmergence : MonoBehaviour
    {
        private sealed class CrackBranch
        {
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public float Delay;
            public float Spread;
        }

        private readonly List<CrackBranch> _branches = new List<CrackBranch>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private CourierContractTuning _settings;
        private DesertWorldStreamer _world;
        private DuneVectorSandAmbusherPalette _palette;
        private ParticleSystem[] _sandBursts;
        private ParticleSystem _dust;
        private ParticleSystem _debris;
        private float _burstAge;
        private float _preBreakDustAccumulator;
        private float _preBreakDebrisAccumulator;
        private Vector2 _fractureDirection;
        private bool _burst;

        public void Initialize(CourierContractTuning settings, DesertWorldStreamer world,
            DuneVectorSandAmbusherPalette palette, int seed)
        {
            _settings = settings;
            _world = world;
            _palette = palette;
            _world.WorldShifted += HandleWorldShift;
            BuildFracture(seed);
            BuildParticles();
            TickWarning(0f);
        }

        public void TickWarning(float progress)
        {
            if (_burst)
            {
                return;
            }
            float warning = Mathf.Clamp01(progress);
            for (int i = 0; i < _branches.Count; i++)
            {
                CrackBranch branch = _branches[i];
                float reveal = Mathf.Clamp01((warning - branch.Delay) / Mathf.Max(0.01f, branch.Spread));
                float intensity = Mathf.Lerp(
                    _settings.SandAmbusherFractureMinimumIntensity,
                    _settings.SandAmbusherFractureMaximumIntensity,
                    Mathf.Pow(warning, _settings.SandAmbusherFractureIntensityPower));
                SetCrackProperties(branch, reveal,
                    Mathf.Lerp(_settings.SandAmbusherFractureInitialWidth, _settings.SandAmbusherFracturePreBurstWidth, warning),
                    intensity, 1f);
            }
            float preBreak = Mathf.InverseLerp(_settings.SandAmbusherPreBreakStartFraction, 1f, warning);
            _preBreakDustAccumulator += Time.deltaTime * _settings.SandAmbusherPreBreakDustEmissionRate * preBreak;
            _preBreakDebrisAccumulator += Time.deltaTime * _settings.SandAmbusherPreBreakDebrisEmissionRate * preBreak;
            int dustCount = Mathf.FloorToInt(_preBreakDustAccumulator);
            int debrisCount = Mathf.FloorToInt(_preBreakDebrisAccumulator);
            if (dustCount > 0)
            {
                _dust.Emit(dustCount);
                _preBreakDustAccumulator -= dustCount;
            }
            if (debrisCount > 0)
            {
                _debris.Emit(debrisCount);
                _preBreakDebrisAccumulator -= debrisCount;
            }
        }

        public void Burst()
        {
            if (_burst)
            {
                return;
            }
            _burst = true;
            _burstAge = 0f;
            SpawnEmergencePrefab();
            for (int i = 0; i < _branches.Count; i++)
            {
                SetCrackProperties(_branches[i], 1f, 1f, _settings.SandAmbusherFractureBurstIntensity, 1f);
            }
            for (int i = 0; i < _sandBursts.Length; i++)
            {
                _sandBursts[i].Emit(Mathf.Max(0, _settings.SandAmbusherDirectionalBurstParticleCount));
            }
            _dust.Emit(Mathf.Max(0, _settings.SandAmbusherDustBurstParticleCount));
            _debris.Emit(Mathf.Max(0, _settings.SandAmbusherDebrisParticleCount));
        }

        private void SpawnEmergencePrefab()
        {
            if (_settings.SandAmbusherEmergencePrefab == null)
            {
                return;
            }

            GameObject effect = Instantiate(_settings.SandAmbusherEmergencePrefab, transform, false);
            Transform effectTransform = effect.transform;
            effectTransform.localPosition = _settings.SandAmbusherEmergencePrefabLocalPosition;
            effectTransform.localRotation *= Quaternion.Euler(_settings.SandAmbusherEmergencePrefabLocalEulerAngles);
            effectTransform.localScale = Vector3.Scale(
                effectTransform.localScale,
                _settings.SandAmbusherEmergencePrefabLocalScale);

            float lifetime = Mathf.Max(0f, _settings.SandAmbusherEmergencePrefabLifetime);
            if (lifetime > 0f)
            {
                Destroy(effect, lifetime);
            }
        }

        private void Update()
        {
            if (!_burst)
            {
                return;
            }
            _burstAge += Time.deltaTime;
            float crackFade = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((_burstAge - _settings.SandAmbusherFractureBurstHoldDuration) /
                    Mathf.Max(0.01f, _settings.SandAmbusherFractureFadeDuration)));
            for (int i = 0; i < _branches.Count; i++)
            {
                SetCrackProperties(_branches[i], 1f, 1f,
                    Mathf.Lerp(_settings.SandAmbusherFractureMaximumIntensity,
                        _settings.SandAmbusherFractureBurstIntensity, crackFade), crackFade);
            }
            float lifetime = _settings.SandAmbusherFractureBurstHoldDuration +
                _settings.SandAmbusherFractureFadeDuration;
            if (_burstAge >= lifetime)
            {
                Destroy(gameObject);
            }
        }

        private void BuildFracture(int seed)
        {
            System.Random random = new System.Random(seed);
            float overallScale = Mathf.Max(0.01f, _settings.SandAmbusherFractureOverallScale);
            Vector2 direction = RandomDirectionInCone(
                random,
                _settings.SandAmbusherFractureRotation,
                _settings.SandAmbusherFractureAllowedRotation);
            _fractureDirection = direction;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            List<Vector2> mainPath = BuildPath(
                Vector2.zero - (direction * _settings.SandAmbusherFractureMainLength * overallScale * 0.5f),
                direction,
                _settings.SandAmbusherFractureMainLength * overallScale,
                _settings.SandAmbusherFractureMainPointCount,
                _settings.SandAmbusherFractureMainJitter * overallScale,
                random);
            CreateBranch("Primary Dune Rupture", mainPath,
                _settings.SandAmbusherFractureMainWidth * overallScale, 0f,
                _settings.SandAmbusherFracturePrimarySpreadFraction);

            int branchCount = Mathf.Max(0, _settings.SandAmbusherFractureBranchCount);
            for (int i = 0; i < branchCount; i++)
            {
                float along = Mathf.Lerp(_settings.SandAmbusherFractureBranchMinimumOrigin,
                    _settings.SandAmbusherFractureBranchMaximumOrigin, (float)random.NextDouble());
                int originIndex = Mathf.Clamp(Mathf.RoundToInt(along * (mainPath.Count - 1)), 0, mainPath.Count - 1);
                float side = (i & 1) == 0 ? -1f : 1f;
                Vector2 branchDirection = (perpendicular * side + direction * Mathf.Lerp(
                    -_settings.SandAmbusherFractureBranchForwardBias,
                    _settings.SandAmbusherFractureBranchForwardBias,
                    (float)random.NextDouble())).normalized;
                float length = Mathf.Lerp(_settings.SandAmbusherFractureBranchMinimumLength,
                    _settings.SandAmbusherFractureBranchMaximumLength, (float)random.NextDouble()) * overallScale;
                List<Vector2> branchPath = BuildPath(mainPath[originIndex], branchDirection, length,
                    _settings.SandAmbusherFractureBranchPointCount,
                    _settings.SandAmbusherFractureBranchJitter * overallScale, random);
                CreateBranch($"Secondary Branching Fracture {i + 1}", branchPath,
                    Mathf.Lerp(_settings.SandAmbusherFractureBranchMinimumWidth,
                        _settings.SandAmbusherFractureBranchMaximumWidth,
                        (float)random.NextDouble()) * overallScale,
                    Mathf.Lerp(_settings.SandAmbusherFractureBranchMinimumDelay,
                        _settings.SandAmbusherFractureBranchMaximumDelay, (float)random.NextDouble()),
                    Mathf.Lerp(_settings.SandAmbusherFractureBranchMinimumSpread,
                        _settings.SandAmbusherFractureBranchMaximumSpread, (float)random.NextDouble()));
            }
        }

        private List<Vector2> BuildPath(Vector2 start, Vector2 direction, float length, int pointCount,
            float jitter, System.Random random)
        {
            pointCount = Mathf.Max(2, pointCount);
            List<Vector2> path = new List<Vector2>(pointCount);
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            float wandering = 0f;
            for (int i = 0; i < pointCount; i++)
            {
                float t = i / (float)(pointCount - 1);
                wandering = Mathf.Lerp(wandering,
                    Mathf.Lerp(-jitter, jitter, (float)random.NextDouble()),
                    _settings.SandAmbusherFractureJitterPersistence);
                path.Add(start + (direction * length * t) + (perpendicular * wandering));
            }
            return path;
        }

        private void CreateBranch(string name, List<Vector2> path, float width, float delay, float spread)
        {
            Mesh mesh = DuneVectorSandAmbusherMeshUtility.CreateTerrainRibbon(
                path, width, transform.position, _world, _settings.SandAmbusherFractureSurfaceOffset);
            mesh.name = name;
            _ownedMeshes.Add(mesh);
            GameObject branchObject = new GameObject(name);
            branchObject.transform.SetParent(transform, false);
            branchObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = branchObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _palette.Fracture;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _branches.Add(new CrackBranch
            {
                Renderer = renderer,
                Properties = new MaterialPropertyBlock(),
                Delay = delay,
                Spread = spread,
            });
        }

        private void BuildParticles()
        {
            int burstCount = Mathf.Max(1, _settings.SandAmbusherDirectionalBurstEmitterCount);
            _sandBursts = new ParticleSystem[burstCount];
            for (int i = 0; i < burstCount; i++)
            {
                float angle = (i / (float)burstCount) * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), _settings.SandAmbusherDirectionalBurstUpwardBias,
                    Mathf.Sin(angle)).normalized;
                _sandBursts[i] = CreateParticleSystem($"Directional Sand Burst {i + 1}",
                    ParticleSystemRenderMode.Stretch, _palette.Dust,
                    _settings.SandAmbusherDirectionalBurstMaximumParticles,
                    _settings.SandAmbusherDirectionalBurstMinimumLifetime,
                    _settings.SandAmbusherDirectionalBurstMaximumLifetime,
                    _settings.SandAmbusherDirectionalBurstMinimumSize,
                    _settings.SandAmbusherDirectionalBurstMaximumSize,
                    _settings.SandAmbusherDirectionalBurstMinimumSpeed,
                    _settings.SandAmbusherDirectionalBurstMaximumSpeed,
                    _settings.SandAmbusherDirectionalBurstGravity);
                _sandBursts[i].transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                ParticleSystem.ShapeModule shape = _sandBursts[i].shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = _settings.SandAmbusherDirectionalBurstConeAngle;
                shape.radius = _settings.SandAmbusherDirectionalBurstEmitterRadius;
                ParticleSystemRenderer renderer = _sandBursts[i].GetComponent<ParticleSystemRenderer>();
                renderer.lengthScale = _settings.SandAmbusherDirectionalBurstStretch;
                renderer.velocityScale = _settings.SandAmbusherDirectionalBurstVelocityScale;
            }

            _dust = CreateParticleSystem("Dense Low Dune Dust Plume", ParticleSystemRenderMode.HorizontalBillboard,
                _palette.Dust, _settings.SandAmbusherDustMaximumParticles,
                _settings.SandAmbusherDustMinimumLifetime, _settings.SandAmbusherDustMaximumLifetime,
                _settings.SandAmbusherDustMinimumSize, _settings.SandAmbusherDustMaximumSize,
                _settings.SandAmbusherDustMinimumSpeed, _settings.SandAmbusherDustMaximumSpeed,
                _settings.SandAmbusherDustGravity);
            _dust.transform.localRotation = Quaternion.Euler(0f,
                Mathf.Atan2(_fractureDirection.x, _fractureDirection.y) * Mathf.Rad2Deg, 0f);
            ParticleSystem.ShapeModule dustShape = _dust.shape;
            dustShape.shapeType = ParticleSystemShapeType.Box;
            dustShape.scale = new Vector3(_settings.SandAmbusherFractureMainLength,
                _settings.SandAmbusherDustEmitterHeight, _settings.SandAmbusherDustEmitterWidth);
            ParticleSystem.NoiseModule dustNoise = _dust.noise;
            dustNoise.enabled = true;
            dustNoise.quality = ParticleSystemNoiseQuality.Medium;
            dustNoise.strength = _settings.SandAmbusherDustTurbulence;
            dustNoise.frequency = _settings.SandAmbusherDustTurbulenceFrequency;

            _debris = CreateParticleSystem("Displaced Sand Clumps", ParticleSystemRenderMode.Mesh,
                _palette.CreaseSand, _settings.SandAmbusherDebrisMaximumParticles,
                _settings.SandAmbusherDebrisMinimumLifetime, _settings.SandAmbusherDebrisMaximumLifetime,
                _settings.SandAmbusherDebrisMinimumSize, _settings.SandAmbusherDebrisMaximumSize,
                _settings.SandAmbusherDebrisMinimumSpeed, _settings.SandAmbusherDebrisMaximumSpeed,
                _settings.SandAmbusherDebrisGravity);
            ParticleSystem.ShapeModule debrisShape = _debris.shape;
            debrisShape.shapeType = ParticleSystemShapeType.Cone;
            debrisShape.angle = _settings.SandAmbusherDebrisConeAngle;
            debrisShape.radius = _settings.SandAmbusherDebrisEmitterRadius;
            ParticleSystemRenderer debrisRenderer = _debris.GetComponent<ParticleSystemRenderer>();
            debrisRenderer.mesh = _palette.DebrisMesh;
            debrisRenderer.shadowCastingMode = ShadowCastingMode.On;
        }

        private ParticleSystem CreateParticleSystem(string name, ParticleSystemRenderMode renderMode,
            Material material, int maxParticles, float minimumLifetime, float maximumLifetime,
            float minimumSize, float maximumSize, float minimumSpeed, float maximumSpeed, float gravity)
        {
            GameObject particleObject = new GameObject(name);
            particleObject.transform.SetParent(transform, false);
            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, maxParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(minimumLifetime, Mathf.Max(minimumLifetime, maximumLifetime));
            main.startSize = new ParticleSystem.MinMaxCurve(minimumSize, Mathf.Max(minimumSize, maximumSize));
            main.startSpeed = new ParticleSystem.MinMaxCurve(minimumSpeed, Mathf.Max(minimumSpeed, maximumSpeed));
            main.gravityModifier = gravity;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, _settings.SandAmbusherParticleFadeInFraction),
                    new GradientAlphaKey(1f, _settings.SandAmbusherParticleFadeOutFraction),
                    new GradientAlphaKey(0f, 1f),
                });
            fade.color = gradient;
            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = renderMode;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private void SetCrackProperties(CrackBranch branch, float reveal, float width, float intensity, float fade)
        {
            branch.Properties.SetFloat("_Reveal", reveal);
            branch.Properties.SetFloat("_Width", width);
            branch.Properties.SetFloat("_Intensity", intensity);
            branch.Properties.SetFloat("_Fade", fade);
            branch.Renderer.SetPropertyBlock(branch.Properties);
        }

        private static Vector2 RandomDirectionInCone(
            System.Random random,
            float centerAngleDegrees,
            float allowedAngleDegrees)
        {
            float halfAngle = Mathf.Clamp(allowedAngleDegrees, 0f, 360f) * 0.5f;
            float angle = centerAngleDegrees + Mathf.Lerp(
                -halfAngle,
                halfAngle,
                (float)random.NextDouble());
            angle *= Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private void HandleWorldShift(Vector3 shift)
        {
            transform.position += shift;
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            for (int i = 0; i < _ownedMeshes.Count; i++)
            {
                if (_ownedMeshes[i] != null)
                {
                    Destroy(_ownedMeshes[i]);
                }
            }
        }
    }

    internal static class DuneVectorSandAmbusherMeshUtility
    {
        public static Mesh CreateOrganicArmorMesh(int ringCount, int radialSegments, float irregularity, int seed)
        {
            ringCount = Mathf.Max(3, ringCount);
            radialSegments = Mathf.Max(5, radialSegments);
            System.Random random = new System.Random(seed);
            int interiorRingCount = ringCount - 1;
            Vector3[] vertices = new Vector3[2 + (interiorRingCount * radialSegments)];
            Vector2[] uvs = new Vector2[vertices.Length];
            List<int> armorTriangles = new List<int>();
            List<int> undersideTriangles = new List<int>();
            float phaseA = (float)random.NextDouble() * Mathf.PI * 2f;
            float phaseB = (float)random.NextDouble() * Mathf.PI * 2f;
            float phaseC = (float)random.NextDouble() * Mathf.PI * 2f;
            float phaseD = (float)random.NextDouble() * Mathf.PI * 2f;

            vertices[0] = new Vector3(
                Mathf.Sin(phaseA) * irregularity * 0.18f,
                -0.5f,
                Mathf.Cos(phaseB) * irregularity * 0.14f);
            uvs[0] = new Vector2(0.5f, 0f);
            for (int ring = 1; ring < ringCount; ring++)
            {
                float v = ring / (float)ringCount;
                float profile = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.58f);
                float centerX = Mathf.Sin((v * Mathf.PI * 1.7f) + phaseA) * irregularity * 0.18f;
                float centerZ = Mathf.Cos((v * Mathf.PI * 1.35f) + phaseB) * irregularity * 0.14f;
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    float u = radial / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    float noise = 1f +
                        (Mathf.Sin((angle * 3f) + phaseC + (v * Mathf.PI * 1.4f)) * irregularity * 0.34f) +
                        (Mathf.Sin((angle * 5f) + phaseD - (v * Mathf.PI * 2.1f)) * irregularity * 0.16f);
                    int vertex = 1 + ((ring - 1) * radialSegments) + radial;
                    vertices[vertex] = new Vector3(
                        centerX + Mathf.Cos(angle) * profile * noise,
                        v - 0.5f,
                        centerZ + Mathf.Sin(angle) * profile * noise);
                    uvs[vertex] = new Vector2(u, v);
                }
            }
            int topVertex = vertices.Length - 1;
            vertices[topVertex] = new Vector3(
                Mathf.Sin((Mathf.PI * 1.7f) + phaseA) * irregularity * 0.18f,
                0.5f,
                Mathf.Cos((Mathf.PI * 1.35f) + phaseB) * irregularity * 0.14f);
            uvs[topVertex] = new Vector2(0.5f, 1f);

            for (int radial = 0; radial < radialSegments; radial++)
            {
                int current = 1 + radial;
                int next = 1 + ((radial + 1) % radialSegments);
                undersideTriangles.Add(0);
                undersideTriangles.Add(current);
                undersideTriangles.Add(next);
            }
            for (int ring = 1; ring < ringCount - 1; ring++)
            {
                float bandV = (ring + 0.5f) / ringCount;
                List<int> target = bandV < 0.38f ? undersideTriangles : armorTriangles;
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    int next = (radial + 1) % radialSegments;
                    int current = 1 + ((ring - 1) * radialSegments) + radial;
                    int currentNext = 1 + ((ring - 1) * radialSegments) + next;
                    int upper = 1 + (ring * radialSegments) + radial;
                    int upperNext = 1 + (ring * radialSegments) + next;
                    target.Add(current);
                    target.Add(upper);
                    target.Add(currentNext);
                    target.Add(currentNext);
                    target.Add(upper);
                    target.Add(upperNext);
                }
            }
            int topRingStart = 1 + ((ringCount - 2) * radialSegments);
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int current = topRingStart + radial;
                int next = topRingStart + ((radial + 1) % radialSegments);
                armorTriangles.Add(topVertex);
                armorTriangles.Add(next);
                armorTriangles.Add(current);
            }
            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(armorTriangles, 0);
            mesh.SetTriangles(undersideTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateTaperedSpikeMesh(int lengthSegments, int radialSegments, float tipScale)
        {
            lengthSegments = Mathf.Max(2, lengthSegments);
            radialSegments = Mathf.Max(5, radialSegments);
            tipScale = Mathf.Clamp(tipScale, 0f, 0.35f);
            int ringVertexCount = (lengthSegments + 1) * radialSegments;
            Vector3[] vertices = new Vector3[ringVertexCount + 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[(lengthSegments * radialSegments * 6) + (radialSegments * 6)];

            for (int length = 0; length <= lengthSegments; length++)
            {
                float t = length / (float)lengthSegments;
                float radius = Mathf.Lerp(0.5f, 0.5f * tipScale, Mathf.SmoothStep(0f, 1f, t));
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    float u = radial / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    int vertex = (length * radialSegments) + radial;
                    vertices[vertex] = new Vector3(
                        Mathf.Cos(angle) * radius,
                        t - 0.5f,
                        Mathf.Sin(angle) * radius);
                    uvs[vertex] = new Vector2(u, t);
                }
            }

            int triangle = 0;
            for (int length = 0; length < lengthSegments; length++)
            {
                int lowerStart = length * radialSegments;
                int upperStart = (length + 1) * radialSegments;
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    int next = (radial + 1) % radialSegments;
                    int current = lowerStart + radial;
                    int currentNext = lowerStart + next;
                    int upper = upperStart + radial;
                    int upperNext = upperStart + next;
                    triangles[triangle++] = current;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = currentNext;
                    triangles[triangle++] = currentNext;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = upperNext;
                }
            }

            int baseCenter = ringVertexCount;
            int tipCenter = ringVertexCount + 1;
            vertices[baseCenter] = new Vector3(0f, -0.5f, 0f);
            vertices[tipCenter] = new Vector3(0f, 0.5f, 0f);
            uvs[baseCenter] = new Vector2(0.5f, 0f);
            uvs[tipCenter] = new Vector2(0.5f, 1f);
            int tipRingStart = lengthSegments * radialSegments;
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int next = (radial + 1) % radialSegments;
                triangles[triangle++] = baseCenter;
                triangles[triangle++] = radial;
                triangles[triangle++] = next;
                triangles[triangle++] = tipCenter;
                triangles[triangle++] = tipRingStart + next;
                triangles[triangle++] = tipRingStart + radial;
            }

            Mesh mesh = new Mesh { name = "Tapered Armor Spike" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateCapsuleMesh(int radialSegments, int hemisphereRings)
        {
            radialSegments = Mathf.Max(8, radialSegments);
            hemisphereRings = Mathf.Max(3, hemisphereRings);
            List<Vector2> profile = new List<Vector2>((hemisphereRings * 2) + 2)
            {
                new Vector2(0f, -1f),
            };
            for (int ring = 1; ring <= hemisphereRings; ring++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, 0f, ring / (float)hemisphereRings);
                profile.Add(new Vector2(Mathf.Cos(angle) * 0.5f, -0.5f + (Mathf.Sin(angle) * 0.5f)));
            }
            profile.Add(new Vector2(0.5f, 0.5f));
            for (int ring = 1; ring < hemisphereRings; ring++)
            {
                float angle = Mathf.Lerp(0f, Mathf.PI * 0.5f, ring / (float)hemisphereRings);
                profile.Add(new Vector2(Mathf.Cos(angle) * 0.5f, 0.5f + (Mathf.Sin(angle) * 0.5f)));
            }
            profile.Add(new Vector2(0f, 1f));
            return CreateRevolvedMesh(profile, radialSegments, "High Resolution Capsule");
        }

        public static Mesh CreateSphereMesh(int ringCount, int radialSegments)
        {
            ringCount = Mathf.Max(6, ringCount);
            radialSegments = Mathf.Max(8, radialSegments);
            List<Vector2> profile = new List<Vector2>(ringCount + 1);
            for (int ring = 0; ring <= ringCount; ring++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, ring / (float)ringCount);
                profile.Add(new Vector2(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f));
            }
            return CreateRevolvedMesh(profile, radialSegments, "High Resolution Sphere");
        }

        private static Mesh CreateRevolvedMesh(List<Vector2> profile, int radialSegments, string meshName)
        {
            int[] ringStarts = new int[profile.Count];
            int vertexCount = 0;
            for (int ring = 0; ring < profile.Count; ring++)
            {
                ringStarts[ring] = vertexCount;
                vertexCount += profile[ring].x <= Mathf.Epsilon ? 1 : radialSegments;
            }

            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            for (int ring = 0; ring < profile.Count; ring++)
            {
                float v = ring / (float)Mathf.Max(1, profile.Count - 1);
                float radius = profile[ring].x;
                float y = profile[ring].y;
                if (radius <= Mathf.Epsilon)
                {
                    vertices[ringStarts[ring]] = new Vector3(0f, y, 0f);
                    uvs[ringStarts[ring]] = new Vector2(0.5f, v);
                    continue;
                }
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    float u = radial / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    int vertex = ringStarts[ring] + radial;
                    vertices[vertex] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    uvs[vertex] = new Vector2(u, v);
                }
            }

            List<int> triangles = new List<int>((profile.Count - 1) * radialSegments * 6);
            for (int ring = 0; ring < profile.Count - 1; ring++)
            {
                bool lowerPoint = profile[ring].x <= Mathf.Epsilon;
                bool upperPoint = profile[ring + 1].x <= Mathf.Epsilon;
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    int next = (radial + 1) % radialSegments;
                    if (lowerPoint)
                    {
                        triangles.Add(ringStarts[ring]);
                        triangles.Add(ringStarts[ring + 1] + radial);
                        triangles.Add(ringStarts[ring + 1] + next);
                    }
                    else if (upperPoint)
                    {
                        triangles.Add(ringStarts[ring + 1]);
                        triangles.Add(ringStarts[ring] + next);
                        triangles.Add(ringStarts[ring] + radial);
                    }
                    else
                    {
                        int current = ringStarts[ring] + radial;
                        int currentNext = ringStarts[ring] + next;
                        int upper = ringStarts[ring + 1] + radial;
                        int upperNext = ringStarts[ring + 1] + next;
                        triangles.Add(current);
                        triangles.Add(upper);
                        triangles.Add(currentNext);
                        triangles.Add(currentNext);
                        triangles.Add(upper);
                        triangles.Add(upperNext);
                    }
                }
            }

            Mesh mesh = new Mesh { name = meshName };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateCurvedProngMesh(float side, CourierContractTuning settings, int seed)
        {
            int pathSegments = Mathf.Max(3, settings.SandAmbusherCrownProngPathSegments);
            int radialSegments = Mathf.Max(5, settings.SandAmbusherCrownProngRadialSegments);
            int ringVertexCount = (pathSegments + 1) * radialSegments;
            Vector3[] vertices = new Vector3[ringVertexCount + 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[(pathSegments * radialSegments * 6) + (radialSegments * 6)];
            System.Random random = new System.Random(seed);
            float depthPhase = (float)random.NextDouble();
            for (int path = 0; path <= pathSegments; path++)
            {
                float t = path / (float)pathSegments;
                Vector3 center = EvaluateCurvedProngCenter(t, side, settings, depthPhase);
                float tangentStep = 1f / pathSegments;
                Vector3 previous = EvaluateCurvedProngCenter(Mathf.Max(0f, t - tangentStep), side, settings, depthPhase);
                Vector3 nextCenter = EvaluateCurvedProngCenter(Mathf.Min(1f, t + tangentStep), side, settings, depthPhase);
                Vector3 tangent = (nextCenter - previous).normalized;
                Vector3 widthAxis = Vector3.Cross(tangent, Vector3.forward).normalized;
                if (widthAxis.sqrMagnitude <= Mathf.Epsilon)
                {
                    widthAxis = Vector3.right;
                }
                Vector3 depthAxis = Vector3.Cross(widthAxis, tangent).normalized;
                float radius = Mathf.Lerp(settings.SandAmbusherCrownProngBaseRadius,
                    settings.SandAmbusherCrownProngTipRadius, Mathf.Pow(t, settings.SandAmbusherCrownProngTaperPower));
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    float u = radial / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    vertices[(path * radialSegments) + radial] = center +
                        ((widthAxis * Mathf.Cos(angle)) + (depthAxis * Mathf.Sin(angle))) * radius;
                    uvs[(path * radialSegments) + radial] = new Vector2(u, t);
                }
            }
            int triangle = 0;
            for (int path = 0; path < pathSegments; path++)
            {
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    int next = (radial + 1) % radialSegments;
                    int current = (path * radialSegments) + radial;
                    int currentNext = (path * radialSegments) + next;
                    int upper = ((path + 1) * radialSegments) + radial;
                    int upperNext = ((path + 1) * radialSegments) + next;
                    triangles[triangle++] = current;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = currentNext;
                    triangles[triangle++] = currentNext;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = upperNext;
                }
            }
            int baseCenter = ringVertexCount;
            int tipCenter = ringVertexCount + 1;
            vertices[baseCenter] = EvaluateCurvedProngCenter(0f, side, settings, depthPhase);
            vertices[tipCenter] = EvaluateCurvedProngCenter(1f, side, settings, depthPhase);
            uvs[baseCenter] = new Vector2(0.5f, 0f);
            uvs[tipCenter] = new Vector2(0.5f, 1f);
            int tipRingStart = pathSegments * radialSegments;
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int next = (radial + 1) % radialSegments;
                triangles[triangle++] = baseCenter;
                triangles[triangle++] = radial;
                triangles[triangle++] = next;
                triangles[triangle++] = tipCenter;
                triangles[triangle++] = tipRingStart + next;
                triangles[triangle++] = tipRingStart + radial;
            }
            Mesh mesh = new Mesh { name = side < 0f ? "Left Split Crown Prong" : "Right Split Crown Prong" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 EvaluateCurvedProngCenter(float t, float side, CourierContractTuning settings,
            float depthPhase)
        {
            float curve = Mathf.Sin(t * Mathf.PI * 0.5f);
            return new Vector3(
                side * (settings.SandAmbusherCrownProngBaseSeparation +
                    (curve * settings.SandAmbusherCrownProngSpread)),
                settings.SandAmbusherCrownBaseHeight + (t * settings.SandAmbusherCrownProngHeight),
                Mathf.Sin((t * Mathf.PI) + depthPhase) * settings.SandAmbusherCrownProngDepthCurve);
        }

        public static Mesh CreateTerrainRibbon(List<Vector2> points, float width, Vector3 origin,
            DesertWorldStreamer world, float heightOffset)
        {
            int count = points.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uvs = new Vector2[count * 2];
            int[] triangles = new int[(count - 1) * 6];
            float totalLength = 0f;
            float[] lengths = new float[count];
            for (int i = 1; i < count; i++)
            {
                totalLength += Vector2.Distance(points[i - 1], points[i]);
                lengths[i] = totalLength;
            }
            for (int i = 0; i < count; i++)
            {
                Vector2 tangent = i == 0 ? points[1] - points[0] :
                    i == count - 1 ? points[count - 1] - points[count - 2] : points[i + 1] - points[i - 1];
                tangent.Normalize();
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                for (int side = 0; side < 2; side++)
                {
                    float sideSign = side == 0 ? -1f : 1f;
                    Vector2 position = points[i] + (normal * width * 0.5f * sideSign);
                    float worldX = origin.x + position.x;
                    float worldZ = origin.z + position.y;
                    float terrainY = world.SampleHeightAtLocal(worldX, worldZ);
                    int index = (i * 2) + side;
                    vertices[index] = new Vector3(position.x, terrainY - origin.y + heightOffset, position.y);
                    uvs[index] = new Vector2(totalLength > 0f ? lengths[i] / totalLength : 0f, side);
                }
            }
            for (int i = 0; i < count - 1; i++)
            {
                int vertex = i * 2;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

    }
}
