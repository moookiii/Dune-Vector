using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuneVector
{
    [DefaultExecutionOrder(920)]
    [DisallowMultipleComponent]
    public sealed class DuneVectorHeatZoneVisualSystem : MonoBehaviour
    {
        private sealed class ZoneVisual
        {
            public Vector2Int Id;
            public GameObject Root;
            public ParticleSystem Plumes;
            public ParticleSystem Streaks;
        }

        private readonly Dictionary<Vector2Int, ZoneVisual> _zoneVisuals = new Dictionary<Vector2Int, ZoneVisual>();
        private readonly List<HeatZoneSample> _nearbyZones = new List<HeatZoneSample>();
        private readonly HashSet<Vector2Int> _activeIds = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _removalBuffer = new List<Vector2Int>();

        private DuneVectorEnvironmentalHazardSystem _hazards;
        private DroneCharacterController _drone;
        private DesertWorldStreamer _world;
        private HeatZoneTuning _settings;
        private DuneVectorUpperFlightRingHUD _upperFlightRingHud;
        private Texture2D _distortionTexture;
        private Texture2D _particleTexture;
        private Material _plumeMaterial;
        private Material _streakMaterial;
        private Material _hotPlateMaterial;
        private Material _hotGlowMaterial;
        private Transform _ambientPlumeRoot;
        private ParticleSystem _ambientPlumes;
        private Volume _interiorVolume;
        private VolumeProfile _interiorProfile;
        private float _refreshTimer;
        private float _interiorBlend;
        private GUIStyle _titleStyle;
        private GUIStyle _statusStyle;

        public void Initialize(
            DuneVectorEnvironmentalHazardSystem hazards,
            DroneCharacterController drone,
            DesertWorldStreamer world,
            HeatZoneTuning settings)
        {
            _hazards = hazards;
            _drone = drone;
            _world = world;
            _settings = settings;
            _upperFlightRingHud = world.GetComponent<DuneVectorUpperFlightRingHUD>();
            _distortionTexture = CreateDistortionTexture(Mathf.Max(16, settings.DistortionTextureResolution));
            _particleTexture = CreateSoftParticleTexture(Mathf.Max(16, settings.DistortionTextureResolution));
            _plumeMaterial = CreateHeatPlumeMaterial();
            _streakMaterial = CreateParticleMaterial("Hot Wind Streaks", settings.HeatStreakColor);
            _hotPlateMaterial = CreateLitMaterial("Heat Pocket Basalt", settings.HotSpotPlateColor, Color.black);
            _hotGlowMaterial = CreateLitMaterial("Heat Pocket Mineral Glow", Color.black, settings.HotSpotGlowColor);
            CreateAmbientHeatPlumes();
            CreateInteriorVolume();
            _world.WorldShifted += HandleWorldShift;
            RefreshZoneVisuals();
        }

        private void Update()
        {
            if (_hazards == null || _world == null || _settings == null)
            {
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                RefreshZoneVisuals();
            }

            float desiredBlend = Mathf.Clamp01(
                _hazards.HeatZoneIntensity * Mathf.Max(_settings.MildSeverity, _hazards.CurrentHeatZoneSeverity));
            _interiorBlend = Mathf.Lerp(
                _interiorBlend,
                desiredBlend,
                DuneVectorMath.Sharpness(_settings.InteriorBlendSharpness, Time.deltaTime));
            if (_interiorVolume != null)
            {
                _interiorVolume.weight = _interiorBlend;
            }
        }

        private void RefreshZoneVisuals()
        {
            _refreshTimer = Mathf.Max(0.05f, _settings.VisualRefreshInterval);
            UpdateAmbientPlumePosition();
            _hazards.CollectNearbyHeatZones(_nearbyZones, Mathf.Max(0f, _settings.VisualRange));
            _activeIds.Clear();
            int count = Mathf.Min(Mathf.Max(1, _settings.MaximumVisibleZones), _nearbyZones.Count);
            for (int i = 0; i < count; i++)
            {
                HeatZoneSample sample = _nearbyZones[i];
                _activeIds.Add(sample.Id);
                if (!_zoneVisuals.ContainsKey(sample.Id))
                {
                    _zoneVisuals.Add(sample.Id, CreateZoneVisual(sample));
                }
            }

            _removalBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, ZoneVisual> pair in _zoneVisuals)
            {
                if (!_activeIds.Contains(pair.Key))
                {
                    _removalBuffer.Add(pair.Key);
                }
            }
            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                Vector2Int id = _removalBuffer[i];
                DestroyZoneVisual(_zoneVisuals[id]);
                _zoneVisuals.Remove(id);
            }
        }

        private ZoneVisual CreateZoneVisual(HeatZoneSample sample)
        {
            Vector3 center = _world.LogicalToLocal(sample.LogicalCenter.X, 0d, sample.LogicalCenter.Z);
            center.y = _world.SampleHeightAtLocal(center.x, center.z);
            GameObject root = new GameObject($"Heat Pressure Pocket [{sample.Id.x}, {sample.Id.y}]");
            root.transform.SetParent(transform, true);
            root.transform.position = center;

            ParticleSystem plumes = CreateHeatPlumes(
                root.transform,
                "Sparse Rising Heat Columns",
                sample.Radius * _settings.HeatPlumeRadiusMultiplier,
                Mathf.RoundToInt(_settings.HeatPlumeParticleBudget * sample.Severity),
                _settings.HeatPlumeEmissionRate * sample.Severity,
                1f);
            ParticleSystem streaks = CreateHeatStreaks(root.transform, sample);
            CreateHotSpots(root.transform, center, sample);
            return new ZoneVisual
            {
                Id = sample.Id,
                Root = root,
                Plumes = plumes,
                Streaks = streaks,
            };
        }

        private void CreateAmbientHeatPlumes()
        {
            if (!_settings.AmbientHeatPlumesEnabled)
            {
                return;
            }

            GameObject root = new GameObject("Ambient Ground Heat Plumes");
            root.transform.SetParent(transform, false);
            _ambientPlumeRoot = root.transform;
            UpdateAmbientPlumePosition();
            _ambientPlumes = CreateHeatPlumes(
                _ambientPlumeRoot,
                "Ambient Rising Heat Columns",
                _settings.AmbientHeatPlumeRadius,
                _settings.AmbientHeatPlumeParticleBudget,
                _settings.AmbientHeatPlumeEmissionRate,
                _settings.AmbientHeatPlumeOpacity);
        }

        private void UpdateAmbientPlumePosition()
        {
            if (_ambientPlumeRoot == null || _drone == null || _world == null)
            {
                return;
            }

            Vector3 center = _drone.WorldCenter;
            center.y = _world.SampleHeightAtLocal(center.x, center.z);
            _ambientPlumeRoot.position = center;
        }

        private ParticleSystem CreateHeatPlumes(
            Transform parent,
            string objectName,
            float radius,
            int particleBudget,
            float emissionRate,
            float opacity)
        {
            GameObject plumeObject = new GameObject(objectName);
            plumeObject.transform.SetParent(parent, false);
            plumeObject.transform.localPosition = Vector3.up * _settings.HeatPlumeGroundOffset;
            ParticleSystem system = plumeObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, particleBudget);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                _settings.HeatPlumeMinimumLifetime,
                Mathf.Max(_settings.HeatPlumeMinimumLifetime, _settings.HeatPlumeMaximumLifetime));
            main.startSize = new ParticleSystem.MinMaxCurve(
                _settings.HeatPlumeMinimumSize,
                Mathf.Max(_settings.HeatPlumeMinimumSize, _settings.HeatPlumeMaximumSize));
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(
                _settings.HeatPlumeMinimumSize,
                Mathf.Max(_settings.HeatPlumeMinimumSize, _settings.HeatPlumeMaximumSize));
            main.startSizeY = new ParticleSystem.MinMaxCurve(
                _settings.HeatPlumeMinimumSize * _settings.HeatPlumeMinimumHeightMultiplier,
                Mathf.Max(
                    _settings.HeatPlumeMinimumSize * _settings.HeatPlumeMinimumHeightMultiplier,
                    _settings.HeatPlumeMaximumSize * _settings.HeatPlumeMaximumHeightMultiplier));
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Mathf.Max(0f, emissionRate);
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Max(0f, radius);
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = _settings.HeatPlumeRiseSpeed;
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = _settings.HeatPlumeTurbulence;
            ParticleSystem.ColorOverLifetimeModule plumeFade = system.colorOverLifetime;
            plumeFade.enabled = true;
            Gradient plumeGradient = new Gradient();
            plumeGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(Mathf.Clamp01(opacity), _settings.HeatPlumeLifetimeFadeInFraction),
                    new GradientAlphaKey(Mathf.Clamp01(opacity), _settings.HeatPlumeLifetimeFadeOutFraction),
                    new GradientAlphaKey(0f, 1f),
                });
            plumeFade.color = plumeGradient;
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

        private ParticleSystem CreateHeatStreaks(Transform parent, HeatZoneSample sample)
        {
            GameObject streakObject = new GameObject("Directional Hot Wind Streaks");
            streakObject.transform.SetParent(parent, false);
            streakObject.transform.localPosition = Vector3.up *
                (_settings.ShimmerCurtainHeight * _settings.HeatStreakHeightFraction);
            ParticleSystem system = streakObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, Mathf.RoundToInt(_settings.HeatStreakParticleBudget * sample.Severity));
            main.startLifetime = Mathf.Max(0.1f, _settings.HeatStreakLifetime);
            main.startSize = Mathf.Max(0.01f, _settings.HeatStreakSize);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = _settings.HeatStreakEmissionRate * sample.Severity;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                sample.Radius * _settings.HeatStreakVolumeRadiusMultiplier,
                _settings.ShimmerCurtainHeight * _settings.HeatStreakVolumeHeightMultiplier,
                sample.Radius * _settings.HeatStreakVolumeRadiusMultiplier);
            Vector2 direction = _settings.HeatStreakDirection.sqrMagnitude > 0.001f
                ? _settings.HeatStreakDirection.normalized
                : Vector2.right;
            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = direction.x * _settings.HeatStreakSpeed;
            velocity.z = direction.y * _settings.HeatStreakSpeed;
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                });
            fade.color = gradient;
            ParticleSystemRenderer renderer = streakObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _streakMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = _settings.HeatStreakLength;
            renderer.velocityScale = _settings.HeatStreakVelocityStretch;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            system.Play();
            return system;
        }

        private void CreateHotSpots(Transform parent, Vector3 center, HeatZoneSample sample)
        {
            GameObject hotSpotRoot = new GameObject("Instanced Heat Hot Spots");
            hotSpotRoot.transform.SetParent(parent, false);
            int count = Mathf.Max(0, Mathf.RoundToInt(_settings.HotSpotCount * sample.Severity));
            for (int i = 0; i < count; i++)
            {
                float angle = DuneVectorMath.HashRange(sample.Id.x, sample.Id.y, i, _settings.RandomSeedOffset + 51, 0f, Mathf.PI * 2f);
                float distance = DuneVectorMath.HashRange(
                    sample.Id.x, sample.Id.y, i, _settings.RandomSeedOffset + 52,
                    sample.Radius * _settings.HotSpotMinimumDistanceFraction,
                    sample.Radius * Mathf.Max(
                        _settings.HotSpotMinimumDistanceFraction,
                        _settings.HotSpotMaximumDistanceFraction));
                float radius = DuneVectorMath.HashRange(
                    sample.Id.x, sample.Id.y, i, _settings.RandomSeedOffset + 53,
                    _settings.HotSpotMinimumRadius, Mathf.Max(_settings.HotSpotMinimumRadius, _settings.HotSpotMaximumRadius));
                Vector3 localPosition = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                float terrain = _world.SampleHeightAtLocal(center.x + localPosition.x, center.z + localPosition.z);
                localPosition.y = terrain - center.y + _settings.HotSpotHeightOffset;
                float yaw = DuneVectorMath.HashRange(sample.Id.x, sample.Id.y, i, _settings.RandomSeedOffset + 54, 0f, 360f);

                Transform plate = CreatePrimitive(
                    PrimitiveType.Sphere,
                    $"Sun-Baked Basalt Plate {i + 1}",
                    hotSpotRoot.transform,
                    localPosition,
                    new Vector3(radius * 2f, _settings.HotSpotPlateThickness, radius * _settings.HotSpotPlateAspect),
                    Quaternion.Euler(0f, yaw, 0f),
                    _hotPlateMaterial);
                CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Radiant Mineral Seam",
                    plate,
                    Vector3.up * (_settings.HotSpotPlateThickness * _settings.HotSpotGlowHeightMultiplier),
                    new Vector3(_settings.HotSpotGlowScale, _settings.HotSpotGlowScale, _settings.HotSpotGlowScale),
                    Quaternion.identity,
                    _hotGlowMaterial);
            }
            DuneVectorSpatialInstancing.Capture(hotSpotRoot, false);
        }

        private void CreateInteriorVolume()
        {
            GameObject volumeObject = new GameObject("Heat Zone Interior Atmosphere");
            volumeObject.transform.SetParent(transform, false);
            _interiorVolume = volumeObject.AddComponent<Volume>();
            _interiorVolume.isGlobal = true;
            _interiorVolume.priority = _settings.InteriorVolumePriority;
            _interiorVolume.weight = 0f;
            _interiorProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _interiorProfile.name = "Runtime Heat Zone Atmosphere";
            _interiorVolume.sharedProfile = _interiorProfile;
            ColorAdjustments color = _interiorProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(_settings.InteriorPostExposure);
            color.saturation.Override(_settings.InteriorSaturation);
            color.contrast.Override(_settings.InteriorContrast);
            color.colorFilter.Override(_settings.InteriorColorFilter);
            Bloom bloom = _interiorProfile.Add<Bloom>(true);
            bloom.intensity.Override(Mathf.Max(0f, _settings.InteriorBloomIntensity));
            bloom.threshold.Override(Mathf.Max(0f, _settings.InteriorBloomThreshold));
        }

        private Material CreateHeatPlumeMaterial()
        {
            Shader shader = Shader.Find("DuneVector/HDRP Heat Plume Distortion");
            Material material = new Material(shader) { name = "Masked Heat Plume Refraction" };
            material.SetTexture("_NoiseTex", _distortionTexture);
            material.SetFloat("_DistortionStrength", _settings.HeatPlumeDistortionStrength);
            material.SetFloat("_DistortionBlur", _settings.HeatPlumeDistortionBlur);
            material.SetVector("_PrimaryTiling", _settings.HeatPlumePrimaryTiling);
            material.SetVector("_SecondaryTiling", _settings.HeatPlumeSecondaryTiling);
            material.SetVector("_PrimaryVelocity", _settings.HeatPlumePrimaryVelocity);
            material.SetVector("_SecondaryVelocity", _settings.HeatPlumeSecondaryVelocity);
            material.SetFloat("_SecondaryStrength", _settings.HeatPlumeSecondaryStrength);
            material.SetFloat("_HorizontalTurbulence", _settings.HeatPlumeHorizontalTurbulence);
            material.SetFloat("_CoreWidth", _settings.HeatPlumeCoreWidth);
            material.SetFloat("_TopWidth", _settings.HeatPlumeTopWidth);
            material.SetFloat("_WidthVariation", _settings.HeatPlumeWidthVariation);
            material.SetFloat("_WidthFrequency", _settings.HeatPlumeWidthFrequency);
            material.SetFloat("_SideFeather", _settings.HeatPlumeSideFeather);
            material.SetFloat("_BottomFeather", _settings.HeatPlumeBottomFeather);
            material.SetFloat("_TopFeather", _settings.HeatPlumeTopFeather);
            material.SetFloat("_VerticalDissipationStart", _settings.HeatPlumeVerticalDissipationStart);
            material.SetFloat("_VerticalDissipationPower", _settings.HeatPlumeVerticalDissipationPower);
            material.SetFloat("_Lean", _settings.HeatPlumeMaximumLean);
            material.SetFloat("_MinimumSpeedMultiplier", _settings.HeatPlumeMinimumAnimationSpeedMultiplier);
            material.SetFloat("_MaximumSpeedMultiplier", _settings.HeatPlumeMaximumAnimationSpeedMultiplier);
            material.SetFloat("_MinimumStrengthMultiplier", _settings.HeatPlumeMinimumStrengthMultiplier);
            material.SetFloat("_MaximumStrengthMultiplier", _settings.HeatPlumeMaximumStrengthMultiplier);
            material.SetFloat("_PhaseRange", _settings.HeatPlumePhaseRange);
            material.SetFloat("_PrimaryPhaseOffset", _settings.HeatPlumePrimaryPhaseOffset);
            material.SetFloat("_SecondaryPhaseOffset", _settings.HeatPlumeSecondaryPhaseOffset);
            material.SetFloat("_CardEdgeFeather", _settings.HeatPlumeCardEdgeFeather);
            material.SetFloat("_EdgeNoiseBase", _settings.HeatPlumeEdgeNoiseBase);
            material.SetFloat("_PrimaryEdgeNoise", _settings.HeatPlumePrimaryEdgeNoise);
            material.SetFloat("_SecondaryEdgeNoise", _settings.HeatPlumeSecondaryEdgeNoise);
            material.SetFloat("_FadeProfileVariation", _settings.HeatPlumeFadeProfileVariation);
            material.SetFloat("_DistanceFadeStart", _settings.HeatPlumeDistanceFadeStart);
            material.SetFloat("_DistanceFadeEnd", _settings.HeatPlumeDistanceFadeEnd);
            material.SetFloat("_DetailFadeStart", _settings.HeatPlumeDetailFadeStart);
            material.SetFloat("_DetailFadeEnd", _settings.HeatPlumeDetailFadeEnd);
            material.SetFloat("_DepthFadeDistance", _settings.HeatPlumeDepthFadeDistance);
            material.SetFloat("_MaskClipThreshold", _settings.HeatPlumeMaskClipThreshold);
            return material;
        }

        private Material CreateParticleMaterial(string materialName, Color tint)
        {
            Shader shader = Shader.Find("DuneVector/HDRP Weather Particle");
            if (shader == null)
            {
                shader = Shader.Find("HDRP/Unlit");
            }
            Material material = new Material(shader) { name = materialName };
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", _particleTexture);
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
            if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", _particleTexture);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", tint);
            return material;
        }

        private Material CreateLitMaterial(string materialName, Color baseColor, Color emission)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            Material material = new Material(shader) { name = materialName, enableInstancing = true };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", _settings.HotSpotSmoothness);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", emission);
            if (material.HasProperty("_EmissiveExposureWeight")) material.SetFloat("_EmissiveExposureWeight", 0f);
            return material;
        }

        private static Transform CreatePrimitive(
            PrimitiveType primitive,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return part.transform;
        }

        private static Texture2D CreateDistortionTexture(int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Heat Refraction Vectors",
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

        private static Texture2D CreateSoftParticleTexture(int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Heat Streak Soft Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 point = new Vector2(x, y) / (resolution - 1f);
                    float alpha = Mathf.Clamp01(1f - (Vector2.Distance(point, Vector2.one * 0.5f) * 2f));
                    pixels[(y * resolution) + x] = new Color(1f, 1f, 1f, alpha * alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void HandleWorldShift(Vector3 shift)
        {
            if (_ambientPlumeRoot != null)
            {
                _ambientPlumeRoot.position += shift;
                _ambientPlumes?.Clear();
            }
            foreach (ZoneVisual visual in _zoneVisuals.Values)
            {
                visual.Root.transform.position += shift;
                visual.Plumes?.Clear();
                visual.Streaks?.Clear();
            }
        }

        private void OnGUI()
        {
            if (_hazards == null || _settings == null || DuneVectorCourierGame.IsGameplayHudSuppressed)
            {
                return;
            }
            float visibility = Mathf.Max(_hazards.HeatZoneIntensity, _hazards.NormalizedTemperature);
            if (visibility < _settings.HudVisibilityThreshold)
            {
                return;
            }

            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Bold,
                fontSize = _settings.HudTitleFontSize,
                normal = { textColor = _settings.HudTextColor },
            };
            _statusStyle ??= new GUIStyle(_titleStyle)
            {
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Normal,
                fontSize = _settings.HudStatusFontSize,
            };

            float panelTop = _settings.HudTop;
            if (_upperFlightRingHud != null && _upperFlightRingHud.TryGetVisiblePanelRect(out Rect upperFlightPanel))
            {
                panelTop = Mathf.Max(panelTop, upperFlightPanel.yMax + _settings.HudUpperFlightGap);
            }

            Rect panel = new Rect(
                Screen.width - _settings.HudRight - _settings.HudWidth,
                panelTop,
                _settings.HudWidth,
                _settings.HudHeight);
            DrawRect(panel, _settings.HudPanelColor);
            DrawRect(new Rect(panel.x, panel.y, _settings.HudAccentWidth, panel.height), _settings.HudAccentColor);
            float contentX = panel.x + _settings.HudPadding;
            float contentWidth = panel.width - (_settings.HudPadding * 2f);
            float titleY = panel.y + _settings.HudPadding;
            GUI.Label(
                new Rect(contentX, titleY, contentWidth, _settings.HudTitleRowHeight),
                _settings.HudZoneLabel,
                _titleStyle);
            string status = _hazards.NormalizedTemperature >= _settings.ConsequenceTemperatureThreshold
                ? _settings.HudBoostLabel
                : _settings.HudRisingLabel;
            GUI.Label(
                new Rect(
                    contentX,
                    titleY + _settings.HudTitleRowHeight + _settings.HudTextRowGap,
                    contentWidth,
                    _settings.HudStatusRowHeight),
                status,
                _statusStyle);
            Rect track = new Rect(
                contentX,
                panel.yMax - _settings.HudPadding - _settings.HudBarHeight,
                contentWidth,
                _settings.HudBarHeight);
            DrawRect(track, _settings.HudTrackColor);
            DrawRect(
                new Rect(track.x, track.y, track.width * _hazards.NormalizedTemperature, track.height),
                Color.Lerp(_settings.HudCoolColor, _settings.HudHotColor, _hazards.NormalizedTemperature));
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DestroyZoneVisual(ZoneVisual visual)
        {
            if (visual == null)
            {
                return;
            }
            if (visual.Root != null) Destroy(visual.Root);
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.WorldShifted -= HandleWorldShift;
            }
            foreach (ZoneVisual visual in _zoneVisuals.Values)
            {
                DestroyZoneVisual(visual);
            }
            _zoneVisuals.Clear();
            if (_plumeMaterial != null) Destroy(_plumeMaterial);
            if (_streakMaterial != null) Destroy(_streakMaterial);
            if (_hotPlateMaterial != null) Destroy(_hotPlateMaterial);
            if (_hotGlowMaterial != null) Destroy(_hotGlowMaterial);
            if (_distortionTexture != null) Destroy(_distortionTexture);
            if (_particleTexture != null) Destroy(_particleTexture);
            if (_interiorProfile != null) Destroy(_interiorProfile);
        }
    }
}
