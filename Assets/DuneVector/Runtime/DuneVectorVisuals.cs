using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public sealed class DuneVectorMaterials : IDisposable
    {
        private const int MaximumGeoglyphPlacements = 8;

        public Material Sand { get; }
        public Material GeoglyphOverlay { get; }
        public Material[] TerrainMaterials { get; }
        public Material DroneBody { get; }
        public Material DroneAccent { get; }
        public Material RivalDroneTop { get; }
        public Material NeutralDroneTop { get; }
        public Material DroneDark { get; }
        public Material Cactus { get; }
        public IReadOnlyList<Material> Shrubs => _shrubMaterials;
        public Material Sandstone { get; }
        public Material LandmarkStone { get; }
        public Material LandmarkMetal { get; }
        public Material LandmarkSecondary { get; }
        public Material LandmarkInterior { get; }
        public Material LandmarkAccent { get; }
        public Material BoostRing { get; }
        public Material FlightRing { get; }
        public Material UpperFlightRing { get; }
        public Material HealthRing { get; }
        public Material HealthHeart { get; }
        public GameObject HealthHeartModel { get; }
        public Material CoinRing { get; }
        public Material Coin { get; }
        public GameObject CoinModel { get; }
        public Material Trail { get; }
        public Material Cloud { get; }
        public Material CloudUnderbelly { get; }
        public Material Package { get; }
        public Material PickupRing { get; }
        public Material DeliveryRing { get; }
        public Material EnemyBody { get; }
        public Material EnemyCore { get; }
        public Material GroundEnemyBody { get; }
        public Material GroundEnemyWarning { get; }
        public Material StormPyramidBody { get; }
        public Material StormPyramidCore { get; }
        public Material PlayerStrikeOrbBody { get; }
        public Material PlayerStrikeOrbCore { get; }
        public Material PlayerStrikeOrbExplosionWhite { get; }
        public Material PlayerStrikeOrbExplosionBlue { get; }
        public Material Lightning { get; }
        public Material LightningWarning { get; }

        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly List<Material> _shrubMaterials = new List<Material>();
        private readonly List<Rect> _geoglyphWorldBounds = new List<Rect>();
        private readonly Material[] _sandOnlyTerrainMaterials;

        public DuneVectorMaterials(
            Texture2D duneTexture,
            float duneTextureTileSize,
            RingTuning ringTuning = null,
            DeliveryTuning deliveryTuning = null,
            CloudTuning cloudTuning = null,
            DynamicCourierTuning dynamicCourierTuning = null,
            DesertShrubTuning shrubTuning = null,
            DroneVisualTuning droneVisualTuning = null,
            GeoglyphSystemTuning geoglyphTuning = null,
            LandmarkSystemTuning landmarkTuning = null,
            PlayerStrikeOrbTuning playerStrikeOrbTuning = null)
        {
            RingTuning rings = ringTuning ?? new RingTuning();
            DeliveryTuning delivery = deliveryTuning ?? new DeliveryTuning();
            CloudTuning clouds = cloudTuning ?? new CloudTuning();
            DynamicCourierTuning couriers = dynamicCourierTuning ?? new DynamicCourierTuning();
            DroneVisualTuning droneVisuals = droneVisualTuning ?? new DroneVisualTuning();
            PlayerStrikeOrbTuning strikeOrbs = playerStrikeOrbTuning ?? new PlayerStrikeOrbTuning();
            Sand = CreateLit("Sand - Textured Dunes", Color.white, 0.14f, 0f);
            ConfigureDuneTexture(Sand, duneTexture, duneTextureTileSize);
            _sandOnlyTerrainMaterials = new[] { Sand };
            GeoglyphOverlay = CreateGeoglyphOverlay(geoglyphTuning);
            TerrainMaterials = GeoglyphOverlay != null
                ? new[] { Sand, GeoglyphOverlay }
                : _sandOnlyTerrainMaterials;
            DroneBody = CreateLit(
                "Drone - Ivory",
                droneVisuals.BodyColor,
                droneVisuals.BodySmoothness,
                droneVisuals.BodyMetallic);
            DroneAccent = CreateLit(
                "Drone - Player Blue Top",
                couriers.PlayerTopColor,
                couriers.TopMaterialSmoothness,
                couriers.TopMaterialMetallic,
                couriers.PlayerTopEmission);
            RivalDroneTop = CreateLit(
                "Drone - Rival Red Top",
                couriers.RivalTopColor,
                couriers.TopMaterialSmoothness,
                couriers.TopMaterialMetallic,
                couriers.RivalTopEmission);
            NeutralDroneTop = CreateLit(
                "Drone - Neutral Orange Top",
                couriers.NeutralTopColor,
                couriers.TopMaterialSmoothness,
                couriers.TopMaterialMetallic,
                couriers.NeutralTopEmission);
            DroneDark = CreateLit(
                "Drone - Graphite",
                droneVisuals.FrameColor,
                droneVisuals.FrameSmoothness,
                droneVisuals.FrameMetallic);
            Cactus = CreateLit("Cactus - Stylized", new Color(0.08f, 0.31f, 0.16f), 0.25f, 0f);
            if (shrubTuning != null)
            {
                shrubTuning.EnsureInitialized();
                for (int i = 0; i < shrubTuning.Variants.Count; i++)
                {
                    DesertShrubVariantTuning variant = shrubTuning.Variants[i];
                    if (variant == null)
                    {
                        _shrubMaterials.Add(null);
                        continue;
                    }
                    _shrubMaterials.Add(CreateLit(
                        $"Desert Shrub - {variant.Name}",
                        variant.Color,
                        variant.Smoothness,
                        0f));
                }
            }
            Sandstone = CreateLit("Pyramid - Sandstone", new Color(0.58f, 0.31f, 0.13f), 0.18f, 0f);
            if (landmarkTuning != null)
            {
                LandmarkStone = CreateLit(
                    "Landmark - Weathered Stone",
                    landmarkTuning.LandmarkStoneColor,
                    landmarkTuning.LandmarkStoneSmoothness,
                    0f);
                LandmarkMetal = CreateLit(
                    "Landmark - Oxidized Metal",
                    landmarkTuning.LandmarkMetalColor,
                    landmarkTuning.LandmarkMetalSmoothness,
                    landmarkTuning.LandmarkMetallic);
                LandmarkSecondary = CreateLit(
                    "Landmark - Sun-Bleached Structure",
                    landmarkTuning.LandmarkSecondaryColor,
                    landmarkTuning.LandmarkStoneSmoothness,
                    landmarkTuning.LandmarkMetallic * 0.35f);
                LandmarkInterior = CreateLit(
                    "Landmark - Recessed Interior",
                    landmarkTuning.LandmarkInteriorColor,
                    landmarkTuning.LandmarkMetalSmoothness * 0.55f,
                    landmarkTuning.LandmarkMetallic);
                LandmarkAccent = CreateLit(
                    "Landmark - Cyan Signal",
                    landmarkTuning.LandmarkAccentColor,
                    landmarkTuning.LandmarkMetalSmoothness,
                    landmarkTuning.LandmarkMetallic,
                    landmarkTuning.LandmarkAccentEmission);
            }
            else
            {
                LandmarkStone = Sandstone;
                LandmarkMetal = DroneBody;
                LandmarkSecondary = DroneBody;
                LandmarkInterior = DroneDark;
                LandmarkAccent = DroneAccent;
            }
            BoostRing = CreateLit("Ring - Boost Amber", rings.BoostRingBaseColor, 0.65f, 0.4f, rings.BoostRingEmissionColor);
            FlightRing = CreateLit("Ring - Flight Cyan", rings.FlightRingBaseColor, 0.7f, 0.5f, rings.FlightRingEmissionColor);
            UpperFlightRing = CreateLit(
                "Ring - Upper Flight Violet",
                rings.UpperFlightRingBaseColor,
                0.7f,
                0.5f,
                rings.UpperFlightRingEmissionColor);
            HealthRing = CreateLit(
                "Ring - Health Crimson",
                rings.HealthRingBaseColor,
                rings.HealthMaterialSmoothness,
                rings.HealthMaterialMetallic,
                rings.HealthRingEmissionColor);
            HealthHeart = CreateLit(
                "Ring - Health Heart",
                rings.HealthHeartBaseColor,
                rings.HealthMaterialSmoothness,
                rings.HealthMaterialMetallic,
                rings.HealthHeartEmissionColor);
            HealthHeartModel = Resources.Load<GameObject>("heartpiece");
            if (HealthHeartModel == null)
            {
                Debug.LogError("Health rings require Assets/DuneVector/Resources/heartpiece.glb.");
            }
            CoinRing = CreateLit(
                "Ring - Coin Gold",
                rings.CoinRingBaseColor,
                rings.CoinMaterialSmoothness,
                rings.CoinMaterialMetallic,
                rings.CoinRingEmissionColor);
            Coin = CreateLit(
                "Ring - Coin Icon",
                rings.CoinBaseColor,
                rings.CoinMaterialSmoothness,
                rings.CoinMaterialMetallic,
                rings.CoinEmissionColor);
            CoinModel = Resources.Load<GameObject>("coin");
            if (CoinModel == null)
            {
                Debug.LogError("Coin rings require Assets/DuneVector/Resources/coin.glb.");
            }
            Trail = CreateLit(
                "Drone - Trail",
                droneVisuals.TrailColor,
                droneVisuals.TrailSmoothness,
                droneVisuals.TrailMetallic,
                droneVisuals.TrailEmission);
            Cloud = CreateLit(
                "Cloud - Sunlit",
                clouds.SunlitColor,
                clouds.MaterialSmoothness,
                clouds.MaterialMetallic);
            CloudUnderbelly = CreateLit(
                "Cloud - Underbelly",
                clouds.UnderbellyColor,
                clouds.MaterialSmoothness,
                clouds.MaterialMetallic);
            Package = CreateLit("Delivery Package", new Color(0.72f, 0.24f, 0.035f), 0.34f, 0.05f, new Color(1.4f, 0.2f, 0.01f));
            PickupRing = CreateLit("Job Ring - Pickup", delivery.PickupRingBaseColor, 0.72f, 0.32f, delivery.PickupRingEmissionColor);
            DeliveryRing = CreateLit("Job Ring - Delivery", delivery.DeliveryRingBaseColor, 0.68f, 0.28f, delivery.DeliveryRingEmissionColor);
            EnemyBody = CreateLit("Sky Piercer - Body", new Color(0.13f, 0.025f, 0.035f), 0.48f, 0.72f, new Color(1.7f, 0.035f, 0.06f));
            EnemyCore = CreateLit("Sky Piercer - Core", new Color(0.008f, 0.004f, 0.012f), 0.82f, 0.18f, new Color(3.2f, 0.02f, 0.55f));
            GroundEnemyBody = CreateLit("Ground Exploder - Body", new Color(0.055f, 0.045f, 0.04f), 0.5f, 0.78f, new Color(0.16f, 0.025f, 0.005f));
            GroundEnemyWarning = CreateLit("Ground Exploder - Warning", new Color(0.46f, 0.055f, 0.008f), 0.62f, 0.3f, new Color(5.2f, 0.32f, 0.015f));
            StormPyramidBody = CreateLit("Storm Pyramid - Body", new Color(0.025f, 0.035f, 0.09f), 0.58f, 0.82f, new Color(0.08f, 0.12f, 0.55f));
            StormPyramidCore = CreateLit("Storm Pyramid - Core", new Color(0.01f, 0.08f, 0.14f), 0.76f, 0.22f, new Color(0.15f, 3.6f, 6.5f));
            PlayerStrikeOrbBody = CreateLit("Strike Orb - Body", strikeOrbs.BodyColor, 0.64f, 0.76f, strikeOrbs.BodyEmission);
            PlayerStrikeOrbCore = CreateLit("Strike Orb - Satellites", strikeOrbs.OrbColor, 0.78f, 0.28f, strikeOrbs.OrbEmission);
            PlayerStrikeOrbExplosionWhite = CreateUnlit(
                "Strike Orb Explosion - White",
                strikeOrbs.FlyThroughExplosionWhiteColor,
                strikeOrbs.FlyThroughExplosionWhiteEmission);
            PlayerStrikeOrbExplosionBlue = CreateUnlit(
                "Strike Orb Explosion - Blue",
                strikeOrbs.FlyThroughExplosionBlueColor,
                strikeOrbs.FlyThroughExplosionBlueEmission);
            Lightning = CreateUnlit("Storm Pyramid - Lightning", new Color(0.55f, 0.86f, 1f), new Color(7.5f, 12f, 18f));
            LightningWarning = CreateUnlit("Storm Pyramid - Warning", new Color(0.18f, 0.42f, 0.62f), new Color(0.45f, 2.8f, 5.8f));
        }

        private static void ConfigureDuneTexture(Material material, Texture2D texture, float tileSize)
        {
            if (material == null || texture == null)
            {
                return;
            }

            Vector2 tiling = Vector2.one / Mathf.Max(0.01f, tileSize);
            if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTexture("_BaseColorMap", texture);
                material.SetTextureScale("_BaseColorMap", tiling);
            }
            if (material.HasProperty("_UnlitColorMap"))
            {
                material.SetTexture("_UnlitColorMap", texture);
                material.SetTextureScale("_UnlitColorMap", tiling);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", tiling);
            }
        }

        public void SetGeoglyphLogicalOrigin(double originOffsetX, double originOffsetZ)
        {
            if (GeoglyphOverlay != null)
            {
                GeoglyphOverlay.SetVector(
                    "_DVGeoglyphOriginOffset",
                    new Vector4((float)originOffsetX, (float)originOffsetZ, 0f, 0f));
            }
        }

        public Material[] GetTerrainMaterials(Vector2Int chunkCoordinate, float chunkSize)
        {
            if (GeoglyphOverlay == null)
            {
                return TerrainMaterials;
            }

            Rect chunkBounds = new Rect(
                chunkCoordinate.x * chunkSize,
                chunkCoordinate.y * chunkSize,
                chunkSize,
                chunkSize);
            for (int i = 0; i < _geoglyphWorldBounds.Count; i++)
            {
                if (_geoglyphWorldBounds[i].Overlaps(chunkBounds, true))
                {
                    return TerrainMaterials;
                }
            }
            return _sandOnlyTerrainMaterials;
        }

        public void Dispose()
        {
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] != null)
                {
                    UnityEngine.Object.Destroy(_ownedMaterials[i]);
                }
            }
            _ownedMaterials.Clear();
        }

        public void ConfigureStormPyramid(StormPyramidTuning settings)
        {
            if (settings == null)
            {
                return;
            }

            ConfigureLitColors(StormPyramidBody, settings.BodyColor, settings.BodyEmission);
            ConfigureLitColors(StormPyramidCore, settings.CoreColor, settings.CoreEmission);
            ConfigureLitColors(
                Lightning,
                settings.LightningColor,
                settings.LightningEmission * settings.LightningBloomIntensity);
            ConfigureLitColors(LightningWarning, settings.WarningColor, settings.WarningEmission);
        }

        public void ConfigurePlayerStrikeOrb(PlayerStrikeOrbTuning settings)
        {
            if (settings == null)
            {
                return;
            }

            ConfigureLitColors(PlayerStrikeOrbBody, settings.BodyColor, settings.BodyEmission);
            ConfigureLitColors(PlayerStrikeOrbCore, settings.OrbColor, settings.OrbEmission);
            ConfigureLitColors(
                PlayerStrikeOrbExplosionWhite,
                settings.FlyThroughExplosionWhiteColor,
                settings.FlyThroughExplosionWhiteEmission);
            ConfigureLitColors(
                PlayerStrikeOrbExplosionBlue,
                settings.FlyThroughExplosionBlueColor,
                settings.FlyThroughExplosionBlueEmission);
        }

        private static void ConfigureLitColors(Material material, Color baseColor, Color emission)
        {
            if (material == null)
            {
                return;
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", baseColor);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", emission);
        }

        private Material CreateLit(string name, Color baseColor, float smoothness, float metallic, Color? emission = null)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                shader = Shader.Find("HDRP/Unlit");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("Dune Vector requires an HDRP-compatible shader, but HDRP/Lit could not be found.");
            }

            Material material = new Material(shader) { name = name };
            material.enableInstancing = true;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }
            if (material.HasProperty("_UnlitColor"))
            {
                material.SetColor("_UnlitColor", baseColor);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }
            if (emission.HasValue && material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", emission.Value);
                if (material.HasProperty("_EmissiveExposureWeight"))
                {
                    material.SetFloat("_EmissiveExposureWeight", 0f);
                }
                material.EnableKeyword("_EMISSIVE_COLOR_MAP");
            }

            _ownedMaterials.Add(material);
            return material;
        }

        private Material CreateUnlit(string name, Color baseColor, Color emission)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException("Dune Vector requires the HDRP/Unlit shader for attack effects.");
            }

            Material material = new Material(shader) { name = name };
            material.enableInstancing = true;
            ConfigureLitColors(material, baseColor, emission);
            if (material.HasProperty("_EmissiveExposureWeight"))
            {
                material.SetFloat("_EmissiveExposureWeight", 0f);
            }
            _ownedMaterials.Add(material);
            return material;
        }

        private Material CreateGeoglyphOverlay(GeoglyphSystemTuning tuning)
        {
            if (tuning == null || !tuning.Enabled || tuning.Placements == null)
            {
                return null;
            }

            List<GeoglyphArtworkPlacement> placements = new List<GeoglyphArtworkPlacement>();
            for (int i = 0; i < tuning.Placements.Count; i++)
            {
                GeoglyphArtworkPlacement placement = tuning.Placements[i];
                if (placement != null && placement.Mask != null && placement.BlendStrength > 0f &&
                    placement.WorldSize.x > 0.01f && placement.WorldSize.y > 0.01f)
                {
                    placements.Add(placement);
                }
            }

            if (placements.Count == 0)
            {
                return null;
            }

            Shader shader = Shader.Find("DuneVector/HDRP World Geoglyph Overlay");
            if (shader == null)
            {
                Debug.LogError("Geoglyph artwork requires Assets/DuneVector/Runtime/DuneVectorGeoglyphOverlay.shader.");
                return null;
            }

            if (placements.Count > MaximumGeoglyphPlacements)
            {
                Debug.LogWarning(
                    $"The geoglyph shader supports {MaximumGeoglyphPlacements} unique placements per material. " +
                    $"Only the first {MaximumGeoglyphPlacements} valid entries will render.");
            }

            int count = Mathf.Min(placements.Count, MaximumGeoglyphPlacements);
            Vector4[] transforms = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] rotations = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] masks = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] slopes = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] colors = new Vector4[MaximumGeoglyphPlacements];
            Material material = new Material(shader) { name = "Terrain - Persistent World Geoglyphs" };
            material.enableInstancing = true;

            for (int i = 0; i < count; i++)
            {
                GeoglyphArtworkPlacement placement = placements[i];
                float radians = placement.RotationDegrees * Mathf.Deg2Rad;
                transforms[i] = new Vector4(
                    placement.WorldCenter.x,
                    placement.WorldCenter.y,
                    1f / Mathf.Max(0.01f, placement.WorldSize.x),
                    1f / Mathf.Max(0.01f, placement.WorldSize.y));
                rotations[i] = new Vector4(
                    Mathf.Cos(radians),
                    Mathf.Sin(radians),
                    Mathf.Clamp01(placement.BlendStrength),
                    0f);
                masks[i] = new Vector4(
                    Mathf.Clamp01(placement.MaskThreshold),
                    Mathf.Max(0.0001f, placement.EdgeSoftness),
                    0f,
                    0f);
                slopes[i] = new Vector4(
                    Mathf.Clamp01(placement.SlopeCorrectionStrength),
                    Mathf.Cos(Mathf.Clamp(placement.SlopeCorrectionStartAngle, 0f, 89f) * Mathf.Deg2Rad),
                    Mathf.Max(0f, placement.MaximumSlopeCorrection),
                    placement.SlopeReferenceHeight);
                colors[i] = placement.LineColor;
                material.SetTexture($"_DVGeoglyphMask{i}", placement.Mask);

                Vector2 halfSize = placement.WorldSize * 0.5f;
                float absoluteCosine = Mathf.Abs(Mathf.Cos(radians));
                float absoluteSine = Mathf.Abs(Mathf.Sin(radians));
                Vector2 boundsHalfSize = new Vector2(
                    (absoluteCosine * halfSize.x) + (absoluteSine * halfSize.y),
                    (absoluteSine * halfSize.x) + (absoluteCosine * halfSize.y));
                boundsHalfSize += Vector2.one * Mathf.Max(0f, placement.MaximumSlopeCorrection);
                _geoglyphWorldBounds.Add(new Rect(
                    placement.WorldCenter - boundsHalfSize,
                    boundsHalfSize * 2f));
            }

            material.SetInt("_DVGeoglyphCount", count);
            material.SetVectorArray("_DVGeoglyphTransform", transforms);
            material.SetVectorArray("_DVGeoglyphRotation", rotations);
            material.SetVectorArray("_DVGeoglyphMaskSettings", masks);
            material.SetVectorArray("_DVGeoglyphSlope", slopes);
            material.SetVectorArray("_DVGeoglyphLineColor", colors);
            material.SetVector("_DVGeoglyphOriginOffset", Vector4.zero);
            _ownedMaterials.Add(material);
            return material;
        }
    }

    public static class DuneVectorVisuals
    {
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();

        public static Transform CreateDroneVisual(
            Transform parent,
            DuneVectorMaterials materials,
            CourierDroneFaction faction = CourierDroneFaction.Player,
            DroneVisualTuning tuning = null)
        {
            DroneVisualTuning settings = tuning ?? new DroneVisualTuning();
            GameObject visualObject = new GameObject("DroneVisualRoot");
            Transform visual = visualObject.transform;
            visual.SetParent(parent, false);
            visual.localPosition = Vector3.up * settings.CourierVisualHeight;

            Material topMaterial = faction switch
            {
                CourierDroneFaction.Rival => materials.RivalDroneTop,
                CourierDroneFaction.Neutral => materials.NeutralDroneTop,
                _ => materials.DroneAccent,
            };

            CreatePart(
                PrimitiveType.Sphere,
                "Lower Graphite Hull",
                visual,
                settings.LowerHullPosition,
                settings.LowerHullScale,
                Quaternion.identity,
                materials.DroneDark);
            CreatePart(
                PrimitiveType.Sphere,
                "Upper Ivory Hull",
                visual,
                settings.UpperHullPosition,
                settings.UpperHullScale,
                Quaternion.identity,
                materials.DroneBody);

            CreateDroneWing(visual, false, settings, materials.DroneBody, topMaterial);
            CreateDroneWing(visual, true, settings, materials.DroneBody, topMaterial);

            CreatePart(
                PrimitiveType.Sphere,
                "Faction Canopy",
                visual,
                settings.CanopyPosition,
                settings.CanopyScale,
                Quaternion.identity,
                topMaterial);
            Transform noseSensor = CreatePart(
                PrimitiveType.Sphere,
                "Forward Sensor",
                visual,
                settings.NoseSensorPosition,
                settings.NoseSensorScale,
                Quaternion.identity,
                topMaterial);
            DisableRendererShadows(noseSensor.gameObject);
            Transform tailLight = CreatePart(
                PrimitiveType.Cube,
                "Tail Light",
                visual,
                settings.TailLightPosition,
                settings.TailLightScale,
                Quaternion.identity,
                topMaterial);
            DisableRendererShadows(tailLight.gameObject);

            Vector3[] rotorPositions =
            {
                new Vector3(-settings.FrontRotorPosition.x, settings.FrontRotorPosition.y, settings.FrontRotorPosition.z),
                settings.FrontRotorPosition,
                new Vector3(-settings.RearRotorPosition.x, settings.RearRotorPosition.y, settings.RearRotorPosition.z),
                settings.RearRotorPosition,
            };

            Transform[] bladeRotors = new Transform[rotorPositions.Length];
            Transform[] glowRings = new Transform[rotorPositions.Length];
            for (int i = 0; i < rotorPositions.Length; i++)
            {
                GameObject rotorObject = new GameObject($"Protected Rotor {i + 1}");
                Transform rotor = rotorObject.transform;
                rotor.SetParent(visual, false);
                rotor.localPosition = rotorPositions[i];

                CreatePart(
                    PrimitiveType.Sphere,
                    "Nacelle",
                    rotor,
                    Vector3.zero,
                    settings.RotorNacelleScale,
                    Quaternion.identity,
                    materials.DroneDark);

                GameObject guard = CreateMeshObject(
                    "Rotor Guard",
                    rotor,
                    GetTorusMesh(settings.RotorGuardRadius, settings.RotorGuardThickness, 32, 6),
                    materials.DroneBody);
                guard.transform.localPosition = Vector3.up * settings.RotorGuardHeight;
                guard.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                GameObject glow = CreateMeshObject(
                    "Faction Rotor Glow",
                    rotor,
                    GetTorusMesh(
                        Mathf.Max(settings.RotorGlowThickness, settings.RotorGuardRadius - settings.RotorGuardThickness),
                        settings.RotorGlowThickness,
                        32,
                        6),
                    topMaterial);
                glow.transform.localPosition = Vector3.up * (settings.RotorGuardHeight + settings.RotorGuardThickness);
                glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                DisableRendererShadows(glow);
                glowRings[i] = glow.transform;

                GameObject bladeRootObject = new GameObject("Blade Rotor");
                Transform bladeRoot = bladeRootObject.transform;
                bladeRoot.SetParent(rotor, false);
                bladeRoot.localPosition = Vector3.up * settings.RotorGuardHeight;
                bladeRotors[i] = bladeRoot;
                CreatePart(
                    PrimitiveType.Cube,
                    "Blade A",
                    bladeRoot,
                    Vector3.zero,
                    new Vector3(settings.RotorBladeLength, settings.RotorBladeThickness, settings.RotorBladeWidth),
                    Quaternion.identity,
                    materials.DroneDark);
                CreatePart(
                    PrimitiveType.Cube,
                    "Blade B",
                    bladeRoot,
                    Vector3.zero,
                    new Vector3(settings.RotorBladeWidth, settings.RotorBladeThickness, settings.RotorBladeLength),
                    Quaternion.identity,
                    materials.DroneDark);
                CreatePart(
                    PrimitiveType.Sphere,
                    "Rotor Hub",
                    bladeRoot,
                    Vector3.zero,
                    settings.RotorHubScale,
                    Quaternion.identity,
                    topMaterial);
            }

            CreateTrail(
                visual,
                new Vector3(-settings.TrailPosition.x, settings.TrailPosition.y, settings.TrailPosition.z),
                materials.Trail,
                settings);
            CreateTrail(visual, settings.TrailPosition, materials.Trail, settings);

            DroneVisualAnimator animator = visualObject.AddComponent<DroneVisualAnimator>();
            animator.Initialize(bladeRotors, glowRings, settings);
            return visual;
        }

        private static void CreateDroneWing(
            Transform visual,
            bool right,
            DroneVisualTuning settings,
            Material bodyMaterial,
            Material accentMaterial)
        {
            GameObject wing = CreateMeshObject(
                right ? "Right Swept Wing" : "Left Swept Wing",
                visual,
                GetDroneWingMesh(
                    right,
                    settings.WingInnerOffset,
                    settings.WingSpan,
                    settings.WingRootChord,
                    settings.WingTipChord,
                    settings.WingSweep,
                    settings.WingThickness,
                    settings.WingForwardOffset),
                bodyMaterial);
            wing.transform.localPosition = Vector3.up * settings.WingHeight;

            float accentInset = Mathf.Min(
                settings.WingAccentInset,
                Mathf.Min(settings.WingSpan * 0.4f, settings.WingTipChord * 0.4f));
            GameObject accent = CreateMeshObject(
                right ? "Right Faction Wing Inlay" : "Left Faction Wing Inlay",
                visual,
                GetDroneWingMesh(
                    right,
                    settings.WingInnerOffset + accentInset,
                    Mathf.Max(0.02f, settings.WingSpan - (accentInset * 2f)),
                    Mathf.Max(0.02f, settings.WingRootChord - (accentInset * 2f)),
                    Mathf.Max(0.02f, settings.WingTipChord - (accentInset * 2f)),
                    settings.WingSweep,
                    settings.WingAccentThickness,
                    settings.WingForwardOffset),
                accentMaterial);
            accent.transform.localPosition = Vector3.up * (settings.WingHeight + settings.WingAccentLift);
            DisableRendererShadows(accent);
        }

        public static Transform CreateCactus(Transform parent, Vector3 localPosition, float height, float thickness, float yaw, int arms, int seed, Material material)
        {
            GameObject rootObject = new GameObject("Cactus");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Transform trunk = CreatePart(PrimitiveType.Capsule, "Trunk", root, new Vector3(0f, height * 0.5f, 0f), new Vector3(thickness, height * 0.5f, thickness), Quaternion.identity, material, true);

            for (int i = 0; i < arms; i++)
            {
                float side = ((DuneVectorMath.Hash(seed, i, seed, 17) & 1u) == 0u) ? -1f : 1f;
                float armHeight = Mathf.Lerp(height * 0.38f, height * 0.72f, DuneVectorMath.Hash01(seed, i, seed, 23));
                float angle = DuneVectorMath.HashRange(seed, i, seed, 29, -24f, 24f);
                Transform arm = CreatePart(
                    PrimitiveType.Capsule,
                    $"Arm {i + 1}",
                    root,
                    new Vector3(side * thickness * 1.35f, armHeight, 0f),
                    new Vector3(thickness * 0.68f, height * 0.16f, thickness * 0.68f),
                    Quaternion.Euler(0f, angle, side * 68f),
                    material,
                    true);
            }

            return root;
        }

        public static Transform CreatePyramid(Transform parent, Vector3 localPosition, float scale, float yaw, Material material)
        {
            GameObject root = new GameObject("Small Pyramid");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = Vector3.one * scale;

            Mesh mesh = GetPyramidMesh();
            MeshFilter filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            MeshCollider collider = root.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            return root.transform;
        }

        public static Transform CreateRingVisual(
            Transform parent,
            TraversalRingType type,
            DuneVectorMaterials materials,
            float majorRadius,
            RingTuning settings)
        {
            Material material = type switch
            {
                TraversalRingType.GroundBoost => materials.BoostRing,
                TraversalRingType.Flight => materials.FlightRing,
                TraversalRingType.UpperFlight => materials.UpperFlightRing,
                TraversalRingType.Health => materials.HealthRing,
                _ => materials.CoinRing,
            };
            GameObject visualRoot = new GameObject("Ring Visual Root");
            visualRoot.transform.SetParent(parent, false);
            Transform geometryParent = visualRoot.transform;
            if (type == TraversalRingType.Health || type == TraversalRingType.Coin)
            {
                GameObject geometryObject = new GameObject("Health Ring XZ Geometry");
                geometryParent = geometryObject.transform;
                geometryParent.SetParent(visualRoot.transform, false);
                geometryParent.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            const float arcStart = -65f;
            const float arcSweep = 310f;
            const float tubeRadius = 0.31f;
            GameObject primary = CreateMeshObject(
                "Open Outer Arc",
                geometryParent,
                GetArcTorusMesh(majorRadius, tubeRadius, 46, 8, arcStart, arcSweep),
                material);
            DisableRendererShadows(primary);

            float[] endpointAngles = { arcStart, arcStart + arcSweep };
            for (int i = 0; i < endpointAngles.Length; i++)
            {
                float angle = endpointAngles[i] * Mathf.Deg2Rad;
                Transform cap = CreatePart(
                    PrimitiveType.Sphere,
                    $"Rounded Arc End {i + 1}",
                    geometryParent,
                    new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * majorRadius,
                    Vector3.one * (tubeRadius * 2f),
                    Quaternion.identity,
                    material);
                DisableRendererShadows(cap.gameObject);
            }

            float dashRadius = Mathf.Max(0.5f, majorRadius - 0.62f);
            const int dashCount = 7;
            for (int i = 0; i < dashCount; i++)
            {
                float t = (i + 0.7f) / (dashCount + 0.4f);
                float angleDegrees = arcStart + (arcSweep * t);
                float angle = angleDegrees * Mathf.Deg2Rad;
                Vector3 dashPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * dashRadius;
                Transform dash = CreatePart(
                    PrimitiveType.Cube,
                    $"Inner Dash {i + 1}",
                    geometryParent,
                    dashPosition,
                    new Vector3(0.58f, 0.1f, 0.15f),
                    Quaternion.Euler(0f, 0f, angleDegrees + 90f),
                    material);
                DisableRendererShadows(dash.gameObject);
            }

            if (type == TraversalRingType.Health)
            {
                CreateCollectibleModelVisual(
                    visualRoot.transform,
                    materials.HealthHeartModel,
                    materials.HealthHeart,
                    settings.HealthHeartScale,
                    settings.HealthHeartOffset,
                    settings.HealthHeartEulerAngles);
            }
            else if (type == TraversalRingType.Coin)
            {
                CreateCollectibleModelVisual(
                    visualRoot.transform,
                    materials.CoinModel,
                    materials.Coin,
                    settings.CoinModelScale,
                    settings.CoinModelOffset,
                    settings.CoinModelEulerAngles);
            }
            return visualRoot.transform;
        }

        private static Transform CreateCollectibleModelVisual(
            Transform parent,
            GameObject model,
            Material material,
            float targetSize,
            Vector3 localOffset,
            Vector3 localEulerAngles)
        {
            if (model == null)
            {
                return null;
            }

            GameObject heartObject = UnityEngine.Object.Instantiate(model, parent, false);
            heartObject.name = "Collectible Icon";
            Transform heart = heartObject.transform;
            heart.localPosition = Vector3.zero;
            heart.localRotation = Quaternion.Euler(localEulerAngles);
            heart.localScale = Vector3.one;

            Renderer[] renderers = heartObject.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material[] modelMaterials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < modelMaterials.Length; materialIndex++)
                {
                    modelMaterials[materialIndex] = material;
                }
                renderer.sharedMaterials = modelMaterials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            Collider[] colliders = heartObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
                UnityEngine.Object.Destroy(colliders[i]);
            }

            if (hasBounds)
            {
                float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (largestDimension > 0.0001f)
                {
                    heart.localScale = Vector3.one * (Mathf.Max(0.1f, targetSize) / largestDimension);

                    Bounds scaledBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        scaledBounds.Encapsulate(renderers[i].bounds);
                    }
                    heart.position += parent.position - scaledBounds.center;
                }
            }
            heart.localPosition += localOffset;
            return heart;
        }

        public static Transform CreateJobRingVisual(Transform parent, bool isPickup, DuneVectorMaterials materials, float radius)
        {
            Material material = isPickup ? materials.PickupRing : materials.DeliveryRing;
            GameObject visualRoot = new GameObject(isPickup ? "Pickup Ring Visual" : "Delivery Ring Visual");
            visualRoot.transform.SetParent(parent, false);

            const float jobArcStart = -65f;
            const float jobArcSweep = 310f;
            const float jobTubeRadius = 0.26f;
            GameObject primary = CreateMeshObject(
                "Open Outer Ring",
                visualRoot.transform,
                GetArcTorusMesh(radius, jobTubeRadius, 46, 8, jobArcStart, jobArcSweep),
                material);
            DisableRendererShadows(primary);

            float[] jobEndpointAngles = { jobArcStart, jobArcStart + jobArcSweep };
            for (int i = 0; i < jobEndpointAngles.Length; i++)
            {
                float endpointAngle = jobEndpointAngles[i] * Mathf.Deg2Rad;
                Transform cap = CreatePart(
                    PrimitiveType.Sphere,
                    $"Rounded Job Arc End {i + 1}",
                    visualRoot.transform,
                    new Vector3(Mathf.Cos(endpointAngle), Mathf.Sin(endpointAngle), 0f) * radius,
                    Vector3.one * (jobTubeRadius * 2f),
                    Quaternion.identity,
                    material);
                DisableRendererShadows(cap.gameObject);
            }

            if (isPickup)
            {
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * 90f;
                    Vector3 position = Quaternion.Euler(0f, 0f, angle) * new Vector3(0f, radius + 0.48f, 0f);
                    Transform bracket = CreatePart(
                        PrimitiveType.Cube,
                        $"Pickup Bracket {i + 1}",
                        visualRoot.transform,
                        position,
                        new Vector3(0.52f, 0.14f, 0.18f),
                        Quaternion.Euler(0f, 0f, angle),
                        material);
                    DisableRendererShadows(bracket.gameObject);
                }
            }
            else
            {
                GameObject inner = CreateMeshObject("Inner Delivery Ring", visualRoot.transform, GetTorusMesh(Mathf.Max(0.4f, radius - 0.48f), 0.09f, 48, 6), material);
                inner.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
                DisableRendererShadows(inner);
            }

            return visualRoot.transform;
        }

        public static Transform CreatePackageVisual(Transform parent, DuneVectorMaterials materials, float scale)
        {
            GameObject rootObject = new GameObject("Delivery Package");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            CreatePart(PrimitiveType.Cube, "Package Body", root, Vector3.zero, new Vector3(1.2f, 0.82f, 1f), Quaternion.identity, materials.Package);
            CreatePart(PrimitiveType.Cube, "Package Strap A", root, new Vector3(0f, 0.43f, 0f), new Vector3(0.18f, 0.05f, 1.04f), Quaternion.identity, materials.DroneDark);
            CreatePart(PrimitiveType.Cube, "Package Strap B", root, new Vector3(0f, 0.43f, 0f), new Vector3(1.24f, 0.05f, 0.18f), Quaternion.identity, materials.DroneDark);
            return root;
        }

        public static Transform CreateFlyingEnemyVisual(Transform parent, DuneVectorMaterials materials, float scale)
        {
            GameObject rootObject = new GameObject("Sky Piercer Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            GameObject body = CreateMeshObject("Pointed Body", root, GetEnemyBodyMesh(), materials.EnemyBody);
            body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;

            GameObject crown = CreateMeshObject("Circular Crown", root, GetTorusMesh(0.66f, 0.13f, 36, 7), materials.EnemyBody);
            crown.transform.localPosition = new Vector3(0f, 1.48f, 0f);

            CreatePart(
                PrimitiveType.Sphere,
                "Crown Neck",
                root,
                new Vector3(0f, 0.92f, 0f),
                new Vector3(0.25f, 0.42f, 0.22f),
                Quaternion.identity,
                materials.EnemyBody);

            Transform core = CreatePart(
                PrimitiveType.Sphere,
                "Recessed Core",
                root,
                new Vector3(0f, -0.12f, 0.24f),
                new Vector3(0.36f, 0.42f, 0.1f),
                Quaternion.identity,
                materials.EnemyCore);
            core.gameObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }

        public static Transform CreateStormPyramidVisual(
            Transform parent,
            DuneVectorMaterials materials,
            StormPyramidTuning settings)
        {
            GameObject rootObject = new GameObject("Storm Pyramid Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * settings.VisualScale;

            GameObject armorRotorObject = new GameObject("Armor Rotor");
            Transform armorRotor = armorRotorObject.transform;
            armorRotor.SetParent(root, false);

            float bodyWidth = Mathf.Max(0.1f, settings.BodyWidth);
            float bodyHeight = Mathf.Max(0.1f, settings.BodyHeight);
            float bodyHalfWidth = bodyWidth * 0.5f;
            float cornerCut = Mathf.Clamp(settings.BodyCornerCut, 0f, bodyHalfWidth * 0.9f);
            CreateMeshObject(
                "Faceted Inverted Pyramid Hull",
                armorRotor,
                GetStormPyramidMesh(bodyWidth, bodyHeight, cornerCut),
                materials.StormPyramidBody);

            int bandCount = Mathf.Max(1, settings.EnergyBandCount);
            float bandStart = Mathf.Clamp01(Mathf.Min(settings.EnergyBandStart, settings.EnergyBandEnd));
            float bandEnd = Mathf.Clamp01(Mathf.Max(settings.EnergyBandStart, settings.EnergyBandEnd));
            float bandThickness = Mathf.Max(0.005f, settings.EnergyBandThickness);
            for (int i = 0; i < bandCount; i++)
            {
                float band01 = bandCount > 1 ? i / (float)(bandCount - 1) : 0.5f;
                float depth01 = Mathf.Lerp(bandStart, bandEnd, band01);
                float halfWidth = bodyHalfWidth * (1f - depth01);
                CreateStormPyramidEnergyBand(
                    armorRotor,
                    i,
                    -bodyHeight * depth01,
                    halfWidth,
                    bandThickness,
                    materials.StormPyramidCore);
            }

            float conduitRadius = Mathf.Max(0.005f, settings.EdgeConduitRadius);
            float conduitTop = bodyHalfWidth - (cornerCut * 0.5f);
            Vector3 tip = new Vector3(0f, -bodyHeight, 0f);
            for (int i = 0; i < 4; i++)
            {
                float x = i == 0 || i == 3 ? -conduitTop : conduitTop;
                float z = i < 2 ? -conduitTop : conduitTop;
                Transform conduit = CreateBeamBetween(
                    $"Edge Conduit {i + 1}",
                    armorRotor,
                    new Vector3(x, 0f, z),
                    tip,
                    conduitRadius,
                    materials.StormPyramidCore);
                DisableRendererShadows(conduit.gameObject);
            }

            int finCount = Mathf.Max(3, settings.CrownFinCount);
            for (int i = 0; i < finCount; i++)
            {
                float degrees = (360f * i) / finCount;
                float radians = degrees * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                CreatePart(
                    PrimitiveType.Cube,
                    $"Crown Fin {i + 1}",
                    armorRotor,
                    (radial * settings.CrownFinRadius) + (Vector3.up * settings.CrownHeight),
                    settings.CrownFinSize,
                    Quaternion.Euler(settings.CrownFinOutwardTilt, degrees, 0f),
                    materials.StormPyramidBody);
            }

            Transform core = CreatePart(
                PrimitiveType.Sphere,
                "Storm Core",
                root,
                new Vector3(0f, settings.CoreHeight, 0f),
                settings.CoreScale,
                Quaternion.identity,
                materials.StormPyramidCore);
            DisableRendererShadows(core.gameObject);

            GameObject counterRotatorObject = new GameObject("Counter Rotator");
            Transform counterRotator = counterRotatorObject.transform;
            counterRotator.SetParent(root, false);

            GameObject crownRing = CreateMeshObject(
                "Crown Energy Ring",
                counterRotator,
                GetTorusMesh(settings.CrownRingRadius, settings.CrownRingThickness, 48, 6),
                materials.StormPyramidCore);
            crownRing.transform.localPosition = new Vector3(0f, settings.CrownHeight, 0f);
            crownRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(crownRing);

            GameObject coreRing = CreateMeshObject(
                "Inner Core Ring",
                counterRotator,
                GetTorusMesh(settings.CoreRingRadius, settings.CoreRingThickness, 40, 6),
                materials.LightningWarning);
            coreRing.transform.localPosition = new Vector3(0f, settings.CoreRingHeight, 0f);
            coreRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(coreRing);

            float orbitNodeWidth = Mathf.Max(0.02f, settings.CoreRingThickness * 3f);
            float orbitNodeLength = Mathf.Max(orbitNodeWidth, settings.CoreRingThickness * 7f);
            for (int i = 0; i < finCount; i++)
            {
                float degrees = (360f * i) / finCount;
                float radians = degrees * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                Transform node = CreatePart(
                    PrimitiveType.Cube,
                    $"Core Orbit Node {i + 1}",
                    counterRotator,
                    (radial * settings.CoreRingRadius) + (Vector3.up * settings.CoreRingHeight),
                    new Vector3(orbitNodeWidth, orbitNodeWidth, orbitNodeLength),
                    Quaternion.Euler(0f, degrees, 0f),
                    materials.LightningWarning);
                DisableRendererShadows(node.gameObject);
            }

            GameObject halo = CreateMeshObject(
                "Charge Halo",
                root,
                GetTorusMesh(settings.ChargeHaloRadius, settings.ChargeHaloThickness, 44, 6),
                materials.LightningWarning);
            halo.transform.localPosition = new Vector3(0f, settings.ChargeHaloHeight, 0f);
            halo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            halo.transform.localScale = Vector3.zero;
            DisableRendererShadows(halo);

            GameObject originObject = new GameObject("Lightning Origin");
            originObject.transform.SetParent(root, false);
            originObject.transform.localPosition = new Vector3(
                0f,
                -bodyHeight - settings.LightningOriginTipOffset,
                0f);
            return root;
        }

        public static Transform CreatePlayerStrikeOrbVisual(
            Transform parent,
            DuneVectorMaterials materials,
            PlayerStrikeOrbTuning settings)
        {
            GameObject rootObject = new GameObject("Strike Orb Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * settings.VisualScale;

            GameObject ring = CreateMeshObject(
                "Central Energy Ring",
                root,
                GetTorusMesh(settings.RingRadius, settings.RingThickness, 52, 8),
                materials.PlayerStrikeOrbBody);
            DisableRendererShadows(ring);

            GameObject innerRing = CreateMeshObject(
                "Inner Energy Ring",
                root,
                GetTorusMesh(
                    Mathf.Max(0.1f, settings.RingRadius - (settings.RingThickness * 1.7f)),
                    settings.InnerRingThickness,
                    48,
                    6),
                materials.PlayerStrikeOrbCore);
            innerRing.transform.localPosition = new Vector3(0f, 0f, 0.015f);
            DisableRendererShadows(innerRing);

            CreateOrbitingOrb(
                root,
                "First Orb Pivot",
                "First Orbiting Orb",
                settings.FirstOrbOrbitTilt,
                settings.FirstOrbStartAngle,
                settings.OrbitRadius,
                settings.OrbitingOrbRadius,
                materials.PlayerStrikeOrbCore);
            CreateOrbitingOrb(
                root,
                "Second Orb Pivot",
                "Second Orbiting Orb",
                settings.SecondOrbOrbitTilt,
                settings.SecondOrbStartAngle,
                settings.OrbitRadius,
                settings.OrbitingOrbRadius,
                materials.PlayerStrikeOrbCore);

            GameObject halo = CreateMeshObject(
                "Charge Halo",
                root,
                GetTorusMesh(settings.ChargeHaloRadius, settings.ChargeHaloThickness, 48, 6),
                materials.LightningWarning);
            halo.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            halo.transform.localScale = Vector3.zero;
            DisableRendererShadows(halo);

            GameObject originObject = new GameObject("Lightning Origin");
            originObject.transform.SetParent(root, false);
            return root;
        }

        public static PlayerStrikeOrbFlyThroughExplosion CreatePlayerStrikeOrbFlyThroughExplosion(
            Vector3 position,
            Quaternion rotation,
            DuneVectorMaterials materials,
            PlayerStrikeOrbTuning settings)
        {
            GameObject rootObject = new GameObject("Strike Orb Fly-Through Explosion");
            Transform root = rootObject.transform;
            root.SetPositionAndRotation(position, rotation);

            Transform flash = CreatePart(
                PrimitiveType.Sphere,
                "White Energy Flash",
                root,
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                materials.PlayerStrikeOrbExplosionWhite);
            DisableRendererShadows(flash.gameObject);

            int shockwaveCount = Mathf.Max(1, settings.FlyThroughShockwaveCount);
            Transform[] shockwaves = new Transform[shockwaveCount];
            for (int i = 0; i < shockwaveCount; i++)
            {
                float angle = (360f / shockwaveCount) * i;
                GameObject shockwave = CreateMeshObject(
                    $"Blue Energy Shockwave {i + 1:00}",
                    root,
                    GetTorusMesh(1f, settings.FlyThroughShockwaveThickness, 64, 8),
                    materials.PlayerStrikeOrbExplosionBlue);
                shockwave.transform.localRotation = Quaternion.Euler(angle, angle * 0.5f, 0f);
                shockwave.transform.localScale = Vector3.zero;
                DisableRendererShadows(shockwave);
                shockwaves[i] = shockwave.transform;
            }

            Light explosionLight = rootObject.AddComponent<Light>();
            explosionLight.type = LightType.Point;
            explosionLight.color = settings.FlyThroughExplosionBlueColor;
            explosionLight.intensity = 0f;
            explosionLight.range = settings.FlyThroughExplosionLightRange;
            explosionLight.shadows = LightShadows.None;

            PlayerStrikeOrbFlyThroughExplosion explosion = rootObject.AddComponent<PlayerStrikeOrbFlyThroughExplosion>();
            explosion.Initialize(flash, shockwaves, explosionLight, settings);
            return explosion;
        }

        private static void CreateOrbitingOrb(
            Transform parent,
            string pivotName,
            string orbName,
            float tilt,
            float startAngle,
            float orbitRadius,
            float orbRadius,
            Material material)
        {
            GameObject pivotObject = new GameObject(pivotName);
            Transform pivot = pivotObject.transform;
            pivot.SetParent(parent, false);
            pivot.localRotation = Quaternion.Euler(tilt, 0f, startAngle);
            Transform orb = CreatePart(
                PrimitiveType.Sphere,
                orbName,
                pivot,
                Vector3.right * orbitRadius,
                Vector3.one * orbRadius,
                Quaternion.identity,
                material);
            DisableRendererShadows(orb.gameObject);
        }

        private static void CreateStormPyramidEnergyBand(
            Transform parent,
            int index,
            float height,
            float halfWidth,
            float thickness,
            Material material)
        {
            float fullWidth = Mathf.Max(thickness, halfWidth * 2f);
            Vector3 xScale = new Vector3(fullWidth + thickness, thickness, thickness);
            Vector3 zScale = new Vector3(thickness, thickness, fullWidth + thickness);
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                Vector3 position = alongX
                    ? new Vector3(0f, height, side == 0 ? -halfWidth : halfWidth)
                    : new Vector3(side == 2 ? -halfWidth : halfWidth, height, 0f);
                Transform rail = CreatePart(
                    PrimitiveType.Cube,
                    $"Energy Band {index + 1} Rail {side + 1}",
                    parent,
                    position,
                    alongX ? xScale : zScale,
                    Quaternion.identity,
                    material);
                DisableRendererShadows(rail.gameObject);
            }
        }

        private static Transform CreateBeamBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float radius,
            Material material)
        {
            Vector3 direction = end - start;
            return CreatePart(
                PrimitiveType.Cylinder,
                name,
                parent,
                (start + end) * 0.5f,
                new Vector3(radius, direction.magnitude * 0.5f, radius),
                Quaternion.FromToRotation(Vector3.up, direction.normalized),
                material);
        }

        public static Transform CreateStormStrikeMarker(
            Transform parent,
            DuneVectorMaterials materials,
            float radius)
        {
            GameObject rootObject = new GameObject("Lightning Strike Marker");
            Transform root = rootObject.transform;
            root.SetParent(parent, true);

            GameObject outerRing = CreateMeshObject(
                "Outer Warning Ring",
                root,
                GetTorusMesh(Mathf.Max(0.2f, radius), 0.09f, 48, 6),
                materials.LightningWarning);
            outerRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(outerRing);

            GameObject innerRing = CreateMeshObject(
                "Inner Warning Ring",
                root,
                GetTorusMesh(Mathf.Max(0.12f, radius * 0.58f), 0.055f, 42, 6),
                materials.LightningWarning);
            innerRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(innerRing);

            Transform impactFlash = CreatePart(
                PrimitiveType.Sphere,
                "Strike Impact Flash",
                root,
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                materials.Lightning);
            DisableRendererShadows(impactFlash.gameObject);
            rootObject.SetActive(false);
            return root;
        }

        public static Transform CreateStormGroundImpactWave(
            Transform marker,
            DuneVectorMaterials materials,
            float radius,
            float ringThickness,
            float heightOffset)
        {
            GameObject impactWave = CreateMeshObject(
                "Ground Impact Wave",
                marker,
                GetTorusMesh(
                    Mathf.Max(0.2f, radius),
                    Mathf.Max(0.005f, ringThickness),
                    48,
                    6),
                materials.Lightning);
            impactWave.transform.localPosition = Vector3.up * Mathf.Max(0f, heightOffset);
            impactWave.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            impactWave.transform.localScale = Vector3.zero;
            DisableRendererShadows(impactWave);
            impactWave.SetActive(false);
            return impactWave.transform;
        }

        public static Transform CreateGroundExploderVisual(Transform parent, DuneVectorMaterials materials, float scale)
        {
            GameObject rootObject = new GameObject("Kinematic Spiked Exploder Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            GameObject wheel = CreateMeshObject(
                "Spiked Hollow Wheel",
                root,
                GetGroundExploderWheelMesh(),
                materials.GroundEnemyBody);
            wheel.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            GameObject warningRing = CreateMeshObject(
                "Warning Ring",
                root,
                GetTorusMesh(0.7f, 0.065f, 40, 6),
                materials.GroundEnemyWarning);
            warningRing.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            DisableRendererShadows(warningRing);

            for (int i = 0; i < 2; i++)
            {
                GameObject ring = CreateMeshObject(
                    $"Telegraph Ring {i + 1}",
                    root,
                    GetTorusMesh(1.25f + (i * 0.32f), 0.055f, 40, 6),
                    materials.GroundEnemyWarning);
                ring.transform.localPosition = new Vector3(0f, -1.04f + (i * 0.025f), 0f);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ring.transform.localScale = Vector3.zero;
                DisableRendererShadows(ring);
            }

            Transform flash = CreatePart(
                PrimitiveType.Sphere,
                "Explosion Flash",
                root,
                Vector3.up * 0.18f,
                Vector3.zero,
                Quaternion.identity,
                materials.GroundEnemyWarning);
            DisableRendererShadows(flash.gameObject);
            return root;
        }

        private static Mesh GetGroundExploderWheelMesh()
        {
            const string key = "ground-exploder-spiked-wheel";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            const int spikeCount = 9;
            const int segmentsPerSpike = 8;
            const int segmentCount = spikeCount * segmentsPerSpike;
            const float innerRadius = 0.67f;
            const float outerRadius = 1.08f;
            const float halfDepth = 0.16f;
            float[] spikeLengths = { 0.48f, 0.7f, 0.42f, 0.62f, 0.5f, 0.76f, 0.45f, 0.66f, 0.54f };

            Vector3[] vertices = new Vector3[segmentCount * 4];
            int[] triangles = new int[segmentCount * 24];
            for (int i = 0; i < segmentCount; i++)
            {
                int spike = i / segmentsPerSpike;
                int withinSpike = i % segmentsPerSpike;
                float spike01 = withinSpike / (float)segmentsPerSpike;
                float triangularSpike = Mathf.Max(0f, 1f - Mathf.Abs(spike01 - 0.5f) / 0.22f);
                float radius = outerRadius + (spikeLengths[spike] * triangularSpike);
                float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                int vertex = i * 4;
                vertices[vertex] = new Vector3(direction.x * innerRadius, direction.y * innerRadius, halfDepth);
                vertices[vertex + 1] = new Vector3(direction.x * radius, direction.y * radius, halfDepth);
                vertices[vertex + 2] = new Vector3(direction.x * innerRadius, direction.y * innerRadius, -halfDepth);
                vertices[vertex + 3] = new Vector3(direction.x * radius, direction.y * radius, -halfDepth);
            }

            int triangle = 0;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % segmentCount;
                int a = i * 4;
                int b = next * 4;
                int[] faces =
                {
                    a, a + 1, b + 1, a, b + 1, b,
                    a + 2, b + 3, a + 3, a + 2, b + 2, b + 3,
                    a + 1, a + 3, b + 3, a + 1, b + 3, b + 1,
                    a, b + 2, a + 2, a, b, b + 2,
                };
                for (int face = 0; face < faces.Length; face++)
                {
                    triangles[triangle++] = faces[face];
                }
            }

            Mesh mesh = new Mesh { name = "Kinematic Spiked Hollow Exploder" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
        }

        private static Mesh GetEnemyBodyMesh()
        {
            const string key = "sky-piercer-body";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            const float depth = 0.2f;
            Vector3[] vertices =
            {
                new Vector3(0f, 0.88f, depth),
                new Vector3(-1f, 0.28f, depth),
                new Vector3(0f, -1.9f, depth),
                new Vector3(1f, 0.28f, depth),
                new Vector3(0f, 0.88f, -depth),
                new Vector3(-1f, 0.28f, -depth),
                new Vector3(0f, -1.9f, -depth),
                new Vector3(1f, 0.28f, -depth),
            };
            int[] triangles =
            {
                0, 1, 3, 1, 2, 3,
                4, 7, 5, 5, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0,
            };

            Mesh mesh = new Mesh { name = "Sky Piercer Pointed Body" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
        }

        private static void DisableRendererShadows(GameObject gameObject)
        {
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        public static Mesh CreateTorusMesh(float majorRadius, float tubeRadius, int majorSegments, int tubeSegments)
        {
            int vertexCount = (majorSegments + 1) * (tubeSegments + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[majorSegments * tubeSegments * 6];

            int vertex = 0;
            for (int major = 0; major <= majorSegments; major++)
            {
                float majorAngle = (major / (float)majorSegments) * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(majorAngle), Mathf.Sin(majorAngle), 0f);
                for (int tube = 0; tube <= tubeSegments; tube++)
                {
                    float tubeAngle = (tube / (float)tubeSegments) * Mathf.PI * 2f;
                    Vector3 normal = new Vector3(radial.x * Mathf.Cos(tubeAngle), radial.y * Mathf.Cos(tubeAngle), Mathf.Sin(tubeAngle));
                    vertices[vertex] = radial * majorRadius + normal * tubeRadius;
                    normals[vertex] = normal.normalized;
                    uvs[vertex] = new Vector2(major / (float)majorSegments, tube / (float)tubeSegments);
                    vertex++;
                }
            }

            int triangle = 0;
            int row = tubeSegments + 1;
            for (int major = 0; major < majorSegments; major++)
            {
                for (int tube = 0; tube < tubeSegments; tube++)
                {
                    int a = (major * row) + tube;
                    int b = a + row;
                    triangles[triangle++] = a;
                    triangles[triangle++] = b;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b + 1;
                }
            }

            Mesh mesh = new Mesh { name = "Procedural Torus" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh GetDroneWingMesh(
            bool right,
            float innerOffset,
            float span,
            float rootChord,
            float tipChord,
            float sweep,
            float thickness,
            float forwardOffset)
        {
            string key = $"drone-wing:{right}:{innerOffset:0.000}:{span:0.000}:{rootChord:0.000}:{tipChord:0.000}:{sweep:0.000}:{thickness:0.000}:{forwardOffset:0.000}";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            float side = right ? 1f : -1f;
            float innerX = side * innerOffset;
            float outerX = side * (innerOffset + span);
            float rootLeading = forwardOffset + (rootChord * 0.5f);
            float rootTrailing = forwardOffset - (rootChord * 0.5f);
            float tipCenter = forwardOffset - sweep;
            float tipLeading = tipCenter + (tipChord * 0.5f);
            float tipTrailing = tipCenter - (tipChord * 0.5f);

            Vector2 innerLeading = new Vector2(innerX, rootLeading);
            Vector2 innerTrailing = new Vector2(innerX, rootTrailing);
            Vector2 outerLeading = new Vector2(outerX, tipLeading);
            Vector2 outerTrailing = new Vector2(outerX, tipTrailing);
            Vector2[] outline = right
                ? new[] { innerLeading, outerLeading, outerTrailing, innerTrailing }
                : new[] { innerLeading, innerTrailing, outerTrailing, outerLeading };

            float halfThickness = thickness * 0.5f;
            Vector3[] vertices = new Vector3[8];
            for (int i = 0; i < outline.Length; i++)
            {
                vertices[i] = new Vector3(outline[i].x, halfThickness, outline[i].y);
                vertices[i + 4] = new Vector3(outline[i].x, -halfThickness, outline[i].y);
            }

            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0,
            };

            Mesh mesh = new Mesh { name = right ? "Right Swept Drone Wing" : "Left Swept Drone Wing" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
        }

        private static Mesh GetArcTorusMesh(
            float majorRadius,
            float tubeRadius,
            int majorSegments,
            int tubeSegments,
            float startDegrees,
            float sweepDegrees)
        {
            string key = $"arc-torus:{majorRadius:0.000}:{tubeRadius:0.000}:{majorSegments}:{tubeSegments}:{startDegrees:0.0}:{sweepDegrees:0.0}";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            int vertexCount = (majorSegments + 1) * (tubeSegments + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[majorSegments * tubeSegments * 6];

            int vertex = 0;
            for (int major = 0; major <= majorSegments; major++)
            {
                float majorDegrees = startDegrees + ((major / (float)majorSegments) * sweepDegrees);
                float majorAngle = majorDegrees * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(majorAngle), Mathf.Sin(majorAngle), 0f);
                for (int tube = 0; tube <= tubeSegments; tube++)
                {
                    float tubeAngle = (tube / (float)tubeSegments) * Mathf.PI * 2f;
                    Vector3 normal = new Vector3(
                        radial.x * Mathf.Cos(tubeAngle),
                        radial.y * Mathf.Cos(tubeAngle),
                        Mathf.Sin(tubeAngle));
                    vertices[vertex] = (radial * majorRadius) + (normal * tubeRadius);
                    normals[vertex] = normal.normalized;
                    uvs[vertex] = new Vector2(major / (float)majorSegments, tube / (float)tubeSegments);
                    vertex++;
                }
            }

            int triangle = 0;
            int row = tubeSegments + 1;
            for (int major = 0; major < majorSegments; major++)
            {
                for (int tube = 0; tube < tubeSegments; tube++)
                {
                    int a = (major * row) + tube;
                    int b = a + row;
                    triangles[triangle++] = a;
                    triangles[triangle++] = b;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b + 1;
                }
            }

            Mesh mesh = new Mesh { name = "Open Arc Torus" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
        }

        private static Mesh GetTorusMesh(float majorRadius, float tubeRadius, int majorSegments, int tubeSegments)
        {
            string key = $"torus:{majorRadius:0.000}:{tubeRadius:0.000}:{majorSegments}:{tubeSegments}";
            if (!MeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = CreateTorusMesh(majorRadius, tubeRadius, majorSegments, tubeSegments);
                mesh.name = key;
                MeshCache[key] = mesh;
            }
            return mesh;
        }

        private static Mesh GetPyramidMesh()
        {
            const string key = "pyramid";
            if (!MeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = CreatePyramidMesh();
                MeshCache[key] = mesh;
            }
            return mesh;
        }

        private static Mesh GetStormPyramidMesh(float width, float height, float cornerCut)
        {
            string key = $"storm-pyramid:{width:0.000}:{height:0.000}:{cornerCut:0.000}";
            if (!MeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = CreateStormPyramidMesh(width, height, cornerCut);
                MeshCache[key] = mesh;
            }
            return mesh;
        }

        private static Mesh CreateStormPyramidMesh(float width, float height, float cornerCut)
        {
            float halfWidth = width * 0.5f;
            Vector3[] perimeter =
            {
                new Vector3(-halfWidth + cornerCut, 0f, -halfWidth),
                new Vector3(halfWidth - cornerCut, 0f, -halfWidth),
                new Vector3(halfWidth, 0f, -halfWidth + cornerCut),
                new Vector3(halfWidth, 0f, halfWidth - cornerCut),
                new Vector3(halfWidth - cornerCut, 0f, halfWidth),
                new Vector3(-halfWidth + cornerCut, 0f, halfWidth),
                new Vector3(-halfWidth, 0f, halfWidth - cornerCut),
                new Vector3(-halfWidth, 0f, -halfWidth + cornerCut),
            };

            List<Vector3> vertices = new List<Vector3>(perimeter.Length * 4);
            List<int> triangles = new List<int>(perimeter.Length * 6);
            Vector3 tip = new Vector3(0f, -height, 0f);
            for (int i = 0; i < perimeter.Length; i++)
            {
                int first = vertices.Count;
                vertices.Add(perimeter[i]);
                vertices.Add(perimeter[(i + 1) % perimeter.Length]);
                vertices.Add(tip);
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }

            Vector3 topCenter = Vector3.zero;
            for (int i = 0; i < perimeter.Length; i++)
            {
                int first = vertices.Count;
                vertices.Add(topCenter);
                vertices.Add(perimeter[(i + 1) % perimeter.Length]);
                vertices.Add(perimeter[i]);
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }

            Mesh mesh = new Mesh { name = "Faceted Storm Pyramid" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreatePyramidMesh()
        {
            Vector3[] vertices =
            {
                new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
                new Vector3(0f, 1.35f, 0f),
            };
            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0,
            };
            Mesh mesh = new Mesh { name = "Procedural Pyramid" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return gameObject;
        }

        private static Transform CreatePart(PrimitiveType primitive, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material, bool keepCollider = false)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (!keepCollider)
            {
                Collider collider = part.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                    UnityEngine.Object.Destroy(collider);
                }
            }

            return part.transform;
        }

        private static void CreateTrail(
            Transform parent,
            Vector3 localPosition,
            Material material,
            DroneVisualTuning settings)
        {
            GameObject trailObject = new GameObject("Speed Trail");
            trailObject.transform.SetParent(parent, false);
            trailObject.transform.localPosition = localPosition;
            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = settings.TrailDuration;
            trail.startWidth = settings.TrailStartWidth;
            trail.endWidth = settings.TrailEndWidth;
            trail.minVertexDistance = settings.TrailMinimumVertexDistance;
            trail.emitting = true;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneVisualAnimator : MonoBehaviour
    {
        private Transform[] _bladeRotors;
        private Transform[] _glowRings;
        private Vector3[] _glowBaseScales;
        private DroneVisualTuning _settings;

        public void Initialize(Transform[] bladeRotors, Transform[] glowRings, DroneVisualTuning settings)
        {
            _bladeRotors = bladeRotors;
            _glowRings = glowRings;
            _settings = settings;
            _glowBaseScales = new Vector3[_glowRings.Length];
            for (int i = 0; i < _glowRings.Length; i++)
            {
                _glowBaseScales[i] = _glowRings[i] != null ? _glowRings[i].localScale : Vector3.one;
            }
        }

        private void Update()
        {
            if (_settings == null)
            {
                return;
            }

            float rotationStep = _settings.RotorSpinSpeed * Time.deltaTime;
            for (int i = 0; i < _bladeRotors.Length; i++)
            {
                Transform bladeRotor = _bladeRotors[i];
                if (bladeRotor != null)
                {
                    float direction = (i & 1) == 0 ? 1f : -1f;
                    bladeRotor.Rotate(Vector3.up, rotationStep * direction, Space.Self);
                }
            }

            float pulse = 1f + (Mathf.Sin(Time.time * _settings.RotorPulseSpeed) * _settings.RotorPulseAmount);
            for (int i = 0; i < _glowRings.Length; i++)
            {
                if (_glowRings[i] != null)
                {
                    _glowRings[i].localScale = _glowBaseScales[i] * pulse;
                }
            }
        }
    }
}
