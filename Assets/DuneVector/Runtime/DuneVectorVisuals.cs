using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public sealed class DuneVectorMaterials : IDisposable
    {
        private const int MaximumGeoglyphPlacements = 8;

        private sealed class GeoglyphMaterialBatch
        {
            public Material Material;
            public readonly List<Rect> WorldBounds = new List<Rect>();
            public Color[] OriginalLineColors;
            public Color OriginalBloomEmissionColor;
        }

        public Material Sand { get; }
        public Material GeoglyphOverlay { get; }
        public Material[] GeoglyphOverlays { get; }
        public Material[] TerrainMaterials { get; }
        public Material DroneBody { get; }
        public Material DroneAccent { get; }
        public Material RivalDroneTop { get; }
        public Material NeutralDroneTop { get; }
        public Material DroneDark { get; }
        public Material Cactus { get; }
        public Material CactusBlossom { get; }
        public IReadOnlyList<Material> Shrubs => _shrubMaterials;
        public Material Sandstone { get; }
        public Material AncientSpireStone { get; }
        public Material AncientSpireAccent { get; }
        public Material AncientSpireDark { get; }
        public GameObject PyramidPrefab { get; }
        public GameObject ObeliskPrefab { get; }
        public Material LandmarkStone { get; }
        public Material LandmarkMetal { get; }
        public Material LandmarkSecondary { get; }
        public Material LandmarkInterior { get; }
        public Material LandmarkAccent { get; }
        public Material MegagateStone { get; }
        public Material MegagateMetal { get; }
        public Material MegagateSignal { get; }
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
        public GameObject PackageModel { get; }
        public DeliveryTuning PackageVisualTuning { get; }
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
        public Material PlayerStrikeOrbTrail { get; }
        public Material PlayerStrikeOrbLightning { get; }
        public Material PlayerStrikeOrbLightningWarning { get; }
        public Material PlayerStrikeOrbExplosionWhite { get; }
        public Material PlayerStrikeOrbExplosionBlue { get; }
        public Material VesperKiteBody { get; }
        public Material VesperKiteAurora { get; }
        public Material VesperPilgrim { get; }
        public Material VesperPilgrimReflected { get; }
        public Material VesperTether { get; }
        public Material Lightning { get; }
        public Material LightningWarning { get; }
        public RingTuning RingPortalTuning { get; }
        public PyramidTuning PyramidLodTuning { get; }
        public PyramidTuning ObeliskLodTuning { get; }
        public FlyingEnemyTuning FlyingEnemyVisualTuning { get; }
        public GameObject FlyingEnemyModel { get; }

        private readonly List<Material> _ownedMaterials = new List<Material>();
        private readonly List<Material> _shrubMaterials = new List<Material>();
        private readonly List<GeoglyphMaterialBatch> _geoglyphBatches = new List<GeoglyphMaterialBatch>();
        private readonly Dictionary<Material, Material> _portalHaloMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _portalRearLineMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _portalFrontLineMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _portalParticleMaterials = new Dictionary<Material, Material>();
        private readonly Material[] _sandOnlyTerrainMaterials;

        public DuneVectorMaterials(
            DuneVectorRuntimeSettings runtimeSettings,
            RingTuning ringTuning = null,
            DeliveryTuning deliveryTuning = null,
            CloudTuning cloudTuning = null,
            DynamicCourierTuning dynamicCourierTuning = null,
            CactusTuning cactusTuning = null,
            DesertShrubTuning shrubTuning = null,
            DroneVisualTuning droneVisualTuning = null,
            GeoglyphSystemTuning geoglyphTuning = null,
            LandmarkSystemTuning landmarkTuning = null,
            PlayerStrikeOrbTuning playerStrikeOrbTuning = null,
            PyramidTuning pyramidLodTuning = null,
            PyramidTuning obeliskLodTuning = null,
            FlyingEnemyTuning flyingEnemyTuning = null,
            VesperKiteTuning vesperKiteTuning = null)
        {
            if (runtimeSettings == null)
            {
                throw new ArgumentNullException(nameof(runtimeSettings));
            }

            RingTuning rings = ringTuning ?? new RingTuning();
            RingPortalTuning = rings;
            DeliveryTuning delivery = deliveryTuning ?? new DeliveryTuning();
            PackageVisualTuning = delivery;
            if (!delivery.UseProceduralPackageFallback &&
                !string.IsNullOrWhiteSpace(delivery.PackageModelResourcePath))
            {
                PackageModel = Resources.Load<GameObject>(delivery.PackageModelResourcePath);
                if (PackageModel == null)
                {
                    Debug.LogError(
                        $"Package model was not found at Resources/{delivery.PackageModelResourcePath}. " +
                        "Using the procedural fallback.");
                }
            }
            CloudTuning clouds = cloudTuning ?? new CloudTuning();
            DynamicCourierTuning couriers = dynamicCourierTuning ?? new DynamicCourierTuning();
            CactusTuning cacti = cactusTuning ?? new CactusTuning();
            DroneVisualTuning droneVisuals = droneVisualTuning ?? new DroneVisualTuning();
            PlayerStrikeOrbTuning strikeOrbs = playerStrikeOrbTuning ?? new PlayerStrikeOrbTuning();
            VesperKiteTuning vesperKites = vesperKiteTuning ?? new VesperKiteTuning();
            PyramidLodTuning = pyramidLodTuning ?? new PyramidTuning();
            ObeliskLodTuning = obeliskLodTuning ?? new PyramidTuning();
            FlyingEnemyVisualTuning = flyingEnemyTuning ?? new FlyingEnemyTuning();
            if (!FlyingEnemyVisualTuning.UseProceduralVisualFallback)
            {
                FlyingEnemyModel = Resources.Load<GameObject>(FlyingEnemyVisualTuning.KunaiResourcePath);
                if (FlyingEnemyModel == null)
                {
                    Debug.LogError(
                        $"Flying enemy model was not found at Resources/{FlyingEnemyVisualTuning.KunaiResourcePath}. " +
                        "Using the procedural fallback.");
                }
            }
            Sand = CreateSandMaterial(runtimeSettings);
            _sandOnlyTerrainMaterials = new[] { Sand };
            GeoglyphOverlays = CreateGeoglyphOverlays(geoglyphTuning);
            GeoglyphOverlay = GeoglyphOverlays.Length > 0 ? GeoglyphOverlays[0] : null;
            TerrainMaterials = new Material[GeoglyphOverlays.Length + 1];
            TerrainMaterials[0] = Sand;
            for (int i = 0; i < GeoglyphOverlays.Length; i++)
            {
                TerrainMaterials[i + 1] = GeoglyphOverlays[i];
            }
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
            Cactus = CreateLit("Cactus - Ribbed Saguaro", cacti.BodyColor, cacti.Smoothness, 0f);
            CactusBlossom = CreateLit("Cactus - Blossom", cacti.BlossomColor, cacti.BlossomSmoothness, 0f);
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
            AncientSpireStone = CreateSpireSurfaceVariant(
                "Ancient Spire - Concrete Sandstone",
                Sandstone,
                landmarkTuning);
            AncientSpireAccent = CreateSpireSurfaceVariant(
                "Ancient Spire - Concrete Energy",
                DroneAccent,
                landmarkTuning);
            AncientSpireDark = CreateSpireSurfaceVariant(
                "Ancient Spire - Concrete Graphite",
                DroneDark,
                landmarkTuning);
            PyramidPrefab = Resources.Load<GameObject>("PyramidPrefab");
            if (PyramidPrefab == null)
            {
                Debug.LogError(
                    "World generation pyramids require " +
                    "Assets/DuneVector/Resources/PyramidPrefab.prefab.");
            }
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
                MegagateStone = CreateLit(
                    "Megagate - Monumental Stone",
                    landmarkTuning.MegagateStoneTextureTint,
                    landmarkTuning.LandmarkStoneSmoothness,
                    0f);
                ConfigureSurfaceTexture(
                    MegagateStone,
                    landmarkTuning.MegagateStoneTexture,
                    landmarkTuning.MegagateStoneTextureTiling);
                MegagateMetal = CreateLit(
                    "Megagate - Oxidized Armor",
                    landmarkTuning.MegagateMetalTextureTint,
                    landmarkTuning.MegagateMetalSmoothness,
                    landmarkTuning.MegagateMetallic,
                    landmarkTuning.MegagateMetalEmission);
                ConfigureSurfaceTexture(
                    MegagateMetal,
                    landmarkTuning.MegagateMetalTexture,
                    landmarkTuning.MegagateMetalTextureTiling,
                    true);
                MegagateSignal = CreateLit(
                    "Megagate - Cyan Signal Surface",
                    landmarkTuning.MegagateSignalTextureTint,
                    landmarkTuning.LandmarkMetalSmoothness,
                    landmarkTuning.LandmarkMetallic,
                    landmarkTuning.LandmarkAccentEmission);
                ConfigureSurfaceTexture(
                    MegagateSignal,
                    landmarkTuning.MegagateSignalTexture,
                    landmarkTuning.MegagateSignalTextureTiling,
                    true);
            }
            else
            {
                LandmarkStone = Sandstone;
                LandmarkMetal = DroneBody;
                LandmarkSecondary = DroneBody;
                LandmarkInterior = DroneDark;
                LandmarkAccent = DroneAccent;
                MegagateStone = Sandstone;
                MegagateMetal = DroneBody;
                MegagateSignal = DroneAccent;
            }
            ObeliskPrefab = Resources.Load<GameObject>("obelisk");
            if (ObeliskPrefab == null)
            {
                Debug.LogError(
                    "World generation obelisks require " +
                    "Assets/DuneVector/Resources/obelisk.glb.");
            }
            BoostRing = CreatePortal("Portal - Boost Amber", rings.BoostRingEmissionColor, rings);
            FlightRing = CreatePortal("Portal - Flight Cyan", rings.FlightRingEmissionColor, rings);
            UpperFlightRing = CreatePortal("Portal - Upper Flight Violet", rings.UpperFlightRingEmissionColor, rings);
            HealthRing = CreatePortal("Portal - Health Green", rings.HealthRingEmissionColor, rings);
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
            CoinRing = CreatePortal("Portal - Coin Gold", rings.CoinRingEmissionColor, rings);
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
            PickupRing = CreatePortal("Portal - Job Pickup", delivery.PickupRingEmissionColor, rings);
            DeliveryRing = CreatePortal("Portal - Job Delivery", delivery.DeliveryRingEmissionColor, rings);
            EnemyBody = CreateLit("Sky Piercer - Body", new Color(0.13f, 0.025f, 0.035f), 0.48f, 0.72f, new Color(1.7f, 0.035f, 0.06f));
            EnemyCore = CreateLit("Sky Piercer - Core", new Color(0.008f, 0.004f, 0.012f), 0.82f, 0.18f, new Color(3.2f, 0.02f, 0.55f));
            GroundEnemyBody = CreateLit("Ground Exploder - Body", new Color(0.055f, 0.045f, 0.04f), 0.5f, 0.78f, new Color(0.16f, 0.025f, 0.005f));
            GroundEnemyWarning = CreateLit("Ground Exploder - Warning", new Color(0.46f, 0.055f, 0.008f), 0.62f, 0.3f, new Color(5.2f, 0.32f, 0.015f));
            StormPyramidBody = CreateLit("Storm Pyramid - Body", new Color(0.025f, 0.035f, 0.09f), 0.58f, 0.82f, new Color(0.08f, 0.12f, 0.55f));
            StormPyramidCore = CreateLit("Storm Pyramid - Core", new Color(0.01f, 0.08f, 0.14f), 0.76f, 0.22f, new Color(0.15f, 3.6f, 6.5f));
            PlayerStrikeOrbBody = CreateLit("Strike Orb - Body", strikeOrbs.BodyColor, 0.64f, 0.76f, strikeOrbs.BodyEmission);
            PlayerStrikeOrbCore = CreateStrikeOrbGradient(strikeOrbs);
            PlayerStrikeOrbTrail = CreateUnlit(
                "Strike Orb - Satellite Trails",
                strikeOrbs.OrbTrailColor,
                strikeOrbs.OrbTrailEmission);
            PlayerStrikeOrbLightning = CreateUnlit(
                "Strike Orb - Lightning",
                strikeOrbs.LightningColor,
                strikeOrbs.LightningEmission * strikeOrbs.LightningBloomIntensity);
            PlayerStrikeOrbLightningWarning = CreateUnlit(
                "Strike Orb - Lightning Warning",
                strikeOrbs.WarningColor,
                strikeOrbs.WarningEmission * strikeOrbs.WarningBloomIntensity);
            PlayerStrikeOrbExplosionWhite = CreateUnlit(
                "Strike Orb Explosion - White",
                strikeOrbs.FlyThroughExplosionWhiteColor,
                strikeOrbs.FlyThroughExplosionWhiteEmission);
            PlayerStrikeOrbExplosionBlue = CreateUnlit(
                "Strike Orb Explosion - Blue",
                strikeOrbs.FlyThroughExplosionBlueColor,
                strikeOrbs.FlyThroughExplosionBlueEmission);
            VesperKiteBody = CreateLit(
                "Vesper Kite - Obsidian Body",
                vesperKites.BodyColor,
                vesperKites.BodySmoothness,
                vesperKites.BodyMetallic,
                vesperKites.BodyEmission);
            VesperKiteAurora = CreateUnlit(
                "Vesper Kite - Aurora Fabric",
                vesperKites.AuroraColor,
                vesperKites.AuroraEmission);
            VesperPilgrim = CreateUnlit(
                "Vesper Kite - Redshift Pilgrim",
                vesperKites.PilgrimColor,
                vesperKites.PilgrimEmission);
            VesperPilgrimReflected = CreateUnlit(
                "Vesper Kite - Reflected Pilgrim",
                vesperKites.ReflectedColor,
                vesperKites.ReflectedEmission);
            VesperTether = CreateUnlit(
                "Vesper Kite - Pilgrim Tether",
                vesperKites.TetherColor,
                vesperKites.TetherEmission);
            Lightning = CreateUnlit("Storm Pyramid - Lightning", new Color(0.55f, 0.86f, 1f), new Color(7.5f, 12f, 18f));
            LightningWarning = CreateUnlit("Storm Pyramid - Warning", new Color(0.18f, 0.42f, 0.62f), new Color(0.45f, 2.8f, 5.8f));
        }

        private Material CreateSandMaterial(DuneVectorRuntimeSettings settings)
        {
            Shader shader = Shader.Find("DuneVector/URP Sand Macro Variation");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Dune Vector requires the DuneVector/URP Sand Macro Variation shader.");
            }

            Material material = new Material(shader)
            {
                name = "Sand - Textured Dunes",
                enableInstancing = true,
            };

            if (settings.DuneTexture != null)
            {
                Vector2 tiling = Vector2.one / Mathf.Max(0.01f, settings.DuneTextureTileSize);
                material.SetTexture("_BaseMap", settings.DuneTexture);
                material.SetTextureScale("_BaseMap", tiling);
            }

            material.SetFloat("_DVSandVariationEnabled", settings.DuneColorVariationEnabled ? 1f : 0f);
            material.SetColor("_DVSandLightColor", settings.DuneSandLightColor);
            material.SetColor("_DVSandMidColor", settings.DuneSandMidColor);
            material.SetColor("_DVSandDarkColor", settings.DuneSandDarkColor);
            material.SetFloat("_DVSandMacroPatternSize", settings.DuneMacroColorPatternSize);
            material.SetFloat("_DVSandSecondaryPatternSize", settings.DuneSecondaryColorPatternSize);
            material.SetVector("_DVSandMacroNoiseOffset", settings.DuneMacroColorNoiseOffset);
            material.SetVector("_DVSandBrightnessNoiseOffset", settings.DuneSecondaryBrightnessNoiseOffset);
            material.SetVector("_DVSandSaturationNoiseOffset", settings.DuneSecondarySaturationNoiseOffset);
            material.SetFloat("_DVSandDarkThreshold", settings.DuneMacroDarkThreshold);
            material.SetFloat("_DVSandLightThreshold", settings.DuneMacroLightThreshold);
            material.SetFloat("_DVSandTransitionSoftness", settings.DuneMacroColorTransitionSoftness);
            material.SetFloat("_DVSandMacroBlendStrength", settings.DuneMacroColorBlendStrength);
            material.SetVector(
                "_DVSandBrightnessRange",
                new Vector4(
                    settings.DuneBrightnessMultiplierMinimum,
                    settings.DuneBrightnessMultiplierMaximum,
                    0f,
                    0f));
            material.SetVector(
                "_DVSandSaturationRange",
                new Vector4(
                    settings.DuneSaturationMultiplierMinimum,
                    settings.DuneSaturationMultiplierMaximum,
                    0f,
                    0f));
            material.SetFloat(
                "_DVSandDarkSaturationMultiplier",
                settings.DuneDarkRegionSaturationMultiplier);
            material.SetFloat("_Smoothness", settings.DuneSurfaceSmoothness);
            material.SetFloat("_DVSandSmoothnessVariation", settings.DuneSmoothnessVariation);
            material.SetFloat("_Metallic", settings.DuneSurfaceMetallic);

            _ownedMaterials.Add(material);
            return material;
        }

        public void SetTerrainLogicalOrigin(double originOffsetX, double originOffsetZ)
        {
            Vector4 origin = new Vector4((float)originOffsetX, (float)originOffsetZ, 0f, 0f);
            for (int i = 0; i < GeoglyphOverlays.Length; i++)
            {
                GeoglyphOverlays[i].SetVector("_DVGeoglyphOriginOffset", origin);
            }
        }

        public Material[] GetTerrainMaterials(Vector2Int chunkCoordinate, float chunkSize)
        {
            if (_geoglyphBatches.Count == 0)
            {
                return _sandOnlyTerrainMaterials;
            }

            Rect chunkBounds = new Rect(
                chunkCoordinate.x * chunkSize,
                chunkCoordinate.y * chunkSize,
                chunkSize,
                chunkSize);
            List<Material> chunkMaterials = null;
            for (int batchIndex = 0; batchIndex < _geoglyphBatches.Count; batchIndex++)
            {
                GeoglyphMaterialBatch batch = _geoglyphBatches[batchIndex];
                for (int boundsIndex = 0; boundsIndex < batch.WorldBounds.Count; boundsIndex++)
                {
                    if (!batch.WorldBounds[boundsIndex].Overlaps(chunkBounds, true))
                    {
                        continue;
                    }
                    chunkMaterials ??= new List<Material> { Sand };
                    chunkMaterials.Add(batch.Material);
                    break;
                }
            }
            return chunkMaterials != null ? chunkMaterials.ToArray() : _sandOnlyTerrainMaterials;
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
            ConfigureStrikeOrbGradient(PlayerStrikeOrbCore, settings);
            ConfigureLitColors(
                PlayerStrikeOrbTrail,
                settings.OrbTrailColor,
                settings.OrbTrailEmission);
            ConfigureLitColors(
                PlayerStrikeOrbLightning,
                settings.LightningColor,
                settings.LightningEmission * settings.LightningBloomIntensity);
            ConfigureLitColors(
                PlayerStrikeOrbLightningWarning,
                settings.WarningColor,
                settings.WarningEmission * settings.WarningBloomIntensity);
            ConfigureLitColors(
                PlayerStrikeOrbExplosionWhite,
                settings.FlyThroughExplosionWhiteColor,
                settings.FlyThroughExplosionWhiteEmission * settings.FlyThroughExplosionBloomIntensity);
            ConfigureLitColors(
                PlayerStrikeOrbExplosionBlue,
                settings.FlyThroughExplosionBlueColor,
                settings.FlyThroughExplosionBlueEmission * settings.FlyThroughExplosionBloomIntensity);
        }

        private static void ConfigureLitColors(Material material, Color baseColor, Color emission)
        {
            if (material == null)
            {
                return;
            }

            bool supportsEmission = material.HasProperty("_EmissionColor");
            if (material.HasProperty("_BaseColor"))
            {
                // URP/Unlit has no separate emission input. Feed the HDR emission
                // through its base color so post-processing can bloom the effect.
                material.SetColor("_BaseColor", supportsEmission ? baseColor : emission);
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
                material.EnableKeyword("_EMISSION");
            }
        }

        private Material CreateLit(string name, Color baseColor, float smoothness, float metallic, Color? emission = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                throw new InvalidOperationException("Dune Vector requires a URP-compatible Lit shader.");
            }

            Material material = new Material(shader) { name = name };
            material.enableInstancing = true;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", baseColor);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }
            if (emission.HasValue && material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission.Value);
                material.EnableKeyword("_EMISSION");
            }

            _ownedMaterials.Add(material);
            return material;
        }

        private Material CreateUnlit(string name, Color baseColor, Color emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                throw new InvalidOperationException("Dune Vector requires the URP Unlit shader for attack effects.");
            }

            Material material = new Material(shader) { name = name };
            material.enableInstancing = true;
            Color unlitColor = emission.maxColorComponent > baseColor.maxColorComponent
                ? new Color(emission.r, emission.g, emission.b, baseColor.a)
                : baseColor;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", unlitColor);
            }
            _ownedMaterials.Add(material);
            return material;
        }

        public void SetGeoglyphOverlayMaterial(
            Material sourceMaterial,
            Vector2 surfaceTextureTiling,
            Vector2 surfaceTextureOffset,
            Color bloomEmission,
            bool enabled)
        {
            Color lineColor = GetMaterialColor(sourceMaterial, "_Color", "_BaseColor", Color.white);
            Texture surfaceTexture = null;
            Vector4 surfaceTextureTransform = new Vector4(
                surfaceTextureTiling.x,
                surfaceTextureTiling.y,
                surfaceTextureOffset.x,
                surfaceTextureOffset.y);
            if (TryGetMaterialTextureProperty(sourceMaterial, out string textureProperty))
            {
                surfaceTexture = sourceMaterial.GetTexture(textureProperty);
            }
            bool useSurfaceTexture = enabled && surfaceTexture != null;

            for (int index = 0; index < _geoglyphBatches.Count; index++)
            {
                GeoglyphMaterialBatch batch = _geoglyphBatches[index];
                if (batch?.Material == null || batch.OriginalLineColors == null)
                {
                    continue;
                }

                Vector4[] colors = new Vector4[batch.OriginalLineColors.Length];
                for (int colorIndex = 0; colorIndex < colors.Length; colorIndex++)
                {
                    Color original = batch.OriginalLineColors[colorIndex];
                    Color color = enabled
                        ? new Color(lineColor.r, lineColor.g, lineColor.b, original.a)
                        : original;
                    colors[colorIndex] = color;
                }
                batch.Material.SetVectorArray("_DVGeoglyphLineColor", colors);
                if (surfaceTexture != null)
                {
                    batch.Material.SetTexture("_DVGeoglyphSurfaceTexture", surfaceTexture);
                }
                batch.Material.SetVector(
                    "_DVGeoglyphSurfaceTextureTransform",
                    surfaceTextureTransform);
                batch.Material.SetFloat(
                    "_DVGeoglyphSurfaceTextureEnabled",
                    useSurfaceTexture ? 1f : 0f);
                batch.Material.SetColor(
                    "_DVGeoglyphBloomEmissionColor",
                    enabled ? bloomEmission : batch.OriginalBloomEmissionColor);
            }
        }

        private static void ConfigureSurfaceTexture(
            Material material,
            Texture2D texture,
            float tiling,
            bool useAsEmissionMap = false)
        {
            if (material == null || texture == null)
            {
                return;
            }

            Vector2 textureScale = Vector2.one * Mathf.Max(0.001f, tiling);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", textureScale);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", textureScale);
            }
            if (useAsEmissionMap && material.HasProperty("_EmissionMap"))
            {
                material.SetTexture("_EmissionMap", texture);
                material.SetTextureScale("_EmissionMap", textureScale);
                material.EnableKeyword("_EMISSION");
            }
        }

        private Material CreateSpireSurfaceVariant(
            string name,
            Material source,
            LandmarkSystemTuning tuning)
        {
            Material material = new Material(source)
            {
                name = name,
                enableInstancing = true,
            };
            if (tuning != null)
            {
                ConfigureSurfaceTexture(material, tuning.SpireConcreteTexture, 1f);
            }
            _ownedMaterials.Add(material);
            return material;
        }

        private static bool TryGetMaterialTextureProperty(
            Material material,
            out string textureProperty)
        {
            if (material != null && material.HasProperty("_BaseMap"))
            {
                textureProperty = "_BaseMap";
                return true;
            }
            if (material != null && material.HasProperty("_MainTex"))
            {
                textureProperty = "_MainTex";
                return true;
            }

            textureProperty = null;
            return false;
        }

        private Material CreateStrikeOrbGradient(PlayerStrikeOrbTuning settings)
        {
            Shader shader = Resources.Load<Shader>("DuneVectorStrikeOrbGradient");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Strike orb satellites require Assets/DuneVector/Resources/DuneVectorStrikeOrbGradient.shader.");
            }

            Material material = new Material(shader)
            {
                name = "Strike Orb - Satellites",
                enableInstancing = true,
            };
            ConfigureStrikeOrbGradient(material, settings);
            _ownedMaterials.Add(material);
            return material;
        }

        private static void ConfigureStrikeOrbGradient(
            Material material,
            PlayerStrikeOrbTuning settings)
        {
            if (material == null || settings == null)
            {
                return;
            }

            material.SetColor("_InnerColor", settings.OrbInnerColor);
            material.SetColor("_OuterColor", settings.OrbOuterColor);
            material.SetFloat("_GradientWidth", settings.OrbGradientWidth);
        }

        private Material CreatePortal(string name, Color color, RingTuning settings)
        {
            Shader shader = Shader.Find("DuneVector/URP Portal Energy");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "Portal rings require Assets/DuneVector/Runtime/DuneVectorPortalEnergy.shader.");
            }

            Material material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_PortalColor", color);
            material.SetFloat("_Opacity", settings.PortalLineOpacity);
            material.SetFloat("_BloomIntensity", settings.PortalBloomIntensity);
            material.SetFloat("_CoreMode", 0f);
            material.SetFloat("_DistanceFade", 1f);
            material.SetFloat("_DistanceBloomBoost", 1f);
            material.SetFloat("_LineEdgeSoftness", settings.PortalLineEdgeSoftness);
            material.SetFloat("_ScreenSpaceAntiAliasing", settings.PortalScreenSpaceAntiAliasing);
            material.SetFloat("_TravelPulseCount", settings.PortalTravelPulseCount);
            material.SetFloat("_TravelPulseSpeed", settings.PortalTravelPulseSpeed);
            material.SetFloat("_TravelPulseWidth", settings.PortalTravelPulseWidth);
            material.SetFloat("_TravelPulseBrightness", settings.PortalTravelPulseBrightness);
            material.SetFloat("_TravelPulseRingPhaseOffset", settings.PortalTravelPulseRingPhaseOffset);
            material.SetFloat("_OuterRimBrightness", settings.PortalOuterRimBrightnessMultiplier);
            material.SetFloat("_StructuralLineBrightness", settings.PortalStructuralLineBrightnessMultiplier);
            material.SetFloat("_RuneBrightness", settings.PortalRuneBrightnessMultiplier);
            material.SetFloat("_InnerRingBrightness", settings.PortalInnerRingBrightnessMultiplier);
            material.SetFloat("_FeatureBrightness", 1f);
            material.SetFloat("_ActivationBloomBoost", 1f);
            material.SetFloat("_PulseSpeed", settings.PortalPulseSpeed);
            material.SetFloat("_PulseAmount", settings.PortalPulseAmount);
            _ownedMaterials.Add(material);
            return material;
        }

        public Material CreatePortalHaloMaterial(Material lineMaterial)
        {
            if (_portalHaloMaterials.TryGetValue(lineMaterial, out Material existing) && existing != null)
            {
                return existing;
            }

            Material material = new Material(lineMaterial)
            {
                name = $"{lineMaterial.name} - Soft Halo",
                enableInstancing = true,
            };
            material.SetFloat("_Opacity", RingPortalTuning.PortalHaloOpacity);
            material.SetFloat("_CoreMode", 2f);
            _ownedMaterials.Add(material);
            _portalHaloMaterials.Add(lineMaterial, material);
            return material;
        }

        public Material CreatePortalDepthLineMaterial(Material lineMaterial, bool frontLayer)
        {
            Dictionary<Material, Material> materials = frontLayer
                ? _portalFrontLineMaterials
                : _portalRearLineMaterials;
            if (materials.TryGetValue(lineMaterial, out Material existing) && existing != null)
            {
                return existing;
            }

            Material material = new Material(lineMaterial)
            {
                name = $"{lineMaterial.name} - {(frontLayer ? "Front" : "Rear")} Depth Layer",
                enableInstancing = true,
            };
            float opacityMultiplier = frontLayer
                ? RingPortalTuning.PortalFrontLayerOpacityMultiplier
                : RingPortalTuning.PortalRearLayerOpacityMultiplier;
            material.SetFloat(
                "_Opacity",
                RingPortalTuning.PortalLineOpacity * Mathf.Clamp01(opacityMultiplier));
            _ownedMaterials.Add(material);
            materials.Add(lineMaterial, material);
            return material;
        }

        public Material CreatePortalParticleMaterial(Material lineMaterial)
        {
            if (_portalParticleMaterials.TryGetValue(lineMaterial, out Material existing) && existing != null)
            {
                return existing;
            }

            Material material = new Material(lineMaterial)
            {
                name = $"{lineMaterial.name} - Edge Sparks",
                enableInstancing = true,
            };
            material.SetFloat("_Opacity", 1f);
            material.SetFloat("_BloomIntensity", RingPortalTuning.PortalSparkBloomIntensity);
            material.SetFloat("_CoreMode", 3f);
            material.SetFloat("_FeatureBrightness", 1f);
            _ownedMaterials.Add(material);
            _portalParticleMaterials.Add(lineMaterial, material);
            return material;
        }

        private Material[] CreateGeoglyphOverlays(GeoglyphSystemTuning tuning)
        {
            if (tuning == null || !tuning.Enabled || tuning.Placements == null)
            {
                return Array.Empty<Material>();
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
                return Array.Empty<Material>();
            }

            Shader shader = Shader.Find("DuneVector/URP World Geoglyph Overlay");
            if (shader == null)
            {
                Debug.LogError("Geoglyph artwork requires Assets/DuneVector/Runtime/DuneVectorGeoglyphOverlay.shader.");
                return Array.Empty<Material>();
            }

            List<Material> materials = new List<Material>();
            for (int batchStart = 0; batchStart < placements.Count; batchStart += MaximumGeoglyphPlacements)
            {
                int batchCount = Mathf.Min(MaximumGeoglyphPlacements, placements.Count - batchStart);
                materials.Add(CreateGeoglyphOverlayBatch(
                    shader,
                    placements,
                    batchStart,
                    batchCount,
                    tuning.BloomEmissionColor));
            }
            return materials.ToArray();
        }

        private Material CreateGeoglyphOverlayBatch(
            Shader shader,
            List<GeoglyphArtworkPlacement> placements,
            int batchStart,
            int count,
            Color bloomEmissionColor)
        {
            Vector4[] transforms = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] rotations = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] masks = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] slopes = new Vector4[MaximumGeoglyphPlacements];
            Vector4[] colors = new Vector4[MaximumGeoglyphPlacements];
            Material material = new Material(shader)
            {
                name = $"Terrain - Persistent World Geoglyphs {(_geoglyphBatches.Count + 1)}",
            };
            material.enableInstancing = true;
            GeoglyphMaterialBatch batch = new GeoglyphMaterialBatch
            {
                Material = material,
                OriginalLineColors = new Color[MaximumGeoglyphPlacements],
                OriginalBloomEmissionColor = bloomEmissionColor,
            };

            for (int i = 0; i < count; i++)
            {
                GeoglyphArtworkPlacement placement = placements[batchStart + i];
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
                batch.OriginalLineColors[i] = placement.LineColor;
                material.SetTexture($"_DVGeoglyphMask{i}", placement.Mask);

                Vector2 halfSize = placement.WorldSize * 0.5f;
                float absoluteCosine = Mathf.Abs(Mathf.Cos(radians));
                float absoluteSine = Mathf.Abs(Mathf.Sin(radians));
                Vector2 boundsHalfSize = new Vector2(
                    (absoluteCosine * halfSize.x) + (absoluteSine * halfSize.y),
                    (absoluteSine * halfSize.x) + (absoluteCosine * halfSize.y));
                boundsHalfSize += Vector2.one * Mathf.Max(0f, placement.MaximumSlopeCorrection);
                batch.WorldBounds.Add(new Rect(
                    placement.WorldCenter - boundsHalfSize,
                    boundsHalfSize * 2f));
            }

            material.SetInt("_DVGeoglyphCount", count);
            material.SetVectorArray("_DVGeoglyphTransform", transforms);
            material.SetVectorArray("_DVGeoglyphRotation", rotations);
            material.SetVectorArray("_DVGeoglyphMaskSettings", masks);
            material.SetVectorArray("_DVGeoglyphSlope", slopes);
            material.SetVectorArray("_DVGeoglyphLineColor", colors);
            material.SetColor("_DVGeoglyphBloomEmissionColor", bloomEmissionColor);
            material.SetVector("_DVGeoglyphOriginOffset", Vector4.zero);
            _ownedMaterials.Add(material);
            _geoglyphBatches.Add(batch);
            return material;
        }

        private static Color GetMaterialColor(
            Material material,
            string primaryProperty,
            string secondaryProperty,
            Color fallback)
        {
            if (material == null)
            {
                return fallback;
            }
            if (material.HasProperty(primaryProperty))
            {
                return material.GetColor(primaryProperty);
            }
            return material.HasProperty(secondaryProperty)
                ? material.GetColor(secondaryProperty)
                : fallback;
        }
    }

    public static class DuneVectorVisuals
    {
        private enum PortalLineLayer
        {
            All,
            Rear,
            Center,
            Front,
        }

        private const string CactusResourcePath = "cacti";
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private static GameObject[] _cactusModels;

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

            GameObject dronePrefab = settings.UsePrefabVisual
                ? Resources.Load<GameObject>(settings.PrefabResourcePath)
                : null;
            if (dronePrefab != null)
            {
                GameObject model = UnityEngine.Object.Instantiate(dronePrefab, visual);
                model.name = dronePrefab.name;
                model.transform.localPosition = settings.PrefabLocalPosition;
                model.transform.localRotation = Quaternion.Euler(settings.PrefabLocalEulerAngles);
                model.transform.localScale = settings.PrefabLocalScale;
                DisableImportedAnimationPlayback(model);

                CreateTrail(
                    visual,
                    new Vector3(-settings.TrailPosition.x, settings.TrailPosition.y, settings.TrailPosition.z),
                    materials.Trail,
                    settings);
                CreateTrail(visual, settings.TrailPosition, materials.Trail, settings);

                Transform[] propellers = FindPropellerRotors(model.transform);
                DroneVisualAnimator prefabAnimator = visualObject.AddComponent<DroneVisualAnimator>();
                prefabAnimator.Initialize(propellers, Array.Empty<Transform>(), settings, false);
                return visual;
            }

            if (settings.UsePrefabVisual)
            {
                Debug.LogError(
                    $"Drone visual prefab was not found at Resources/{settings.PrefabResourcePath}. " +
                    "Using the procedural fallback.");
            }

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

        private static void DisableImportedAnimationPlayback(GameObject model)
        {
            Animator[] animators = model.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                animators[i].enabled = false;
            }

            Animation[] animations = model.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++)
            {
                animations[i].enabled = false;
            }
        }

        private static void EnableImportedAnimationPlayback(
            GameObject model,
            VesperKiteTuning settings)
        {
            Animator[] animators = model.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.speed = Mathf.Max(0f, settings.PrefabAnimationSpeed);
                animator.Rebind();

                int stateHash = Animator.StringToHash(
                    settings.PrefabAnimationStateName);
                if (animator.HasState(0, stateHash))
                {
                    animator.Play(stateHash, 0, 0f);
                }
                else
                {
                    Debug.LogWarning(
                        $"Vesper Kite Animator does not contain state " +
                        $"'{settings.PrefabAnimationStateName}'.");
                }

                animator.Update(0f);
            }
        }

        private static Transform[] FindPropellerRotors(Transform root)
        {
            const int propellerCount = 4;
            Transform[] results = new Transform[propellerCount];
            Transform[] namedFallbacks = new Transform[propellerCount];
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int descendantIndex = 0; descendantIndex < descendants.Length; descendantIndex++)
            {
                Transform descendant = descendants[descendantIndex];
                for (int propellerIndex = 0; propellerIndex < propellerCount; propellerIndex++)
                {
                    int propellerNumber = propellerIndex + 1;
                    if (descendant.name.StartsWith(
                        $"prop_{propellerNumber}_jnt",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        results[propellerIndex] = descendant;
                        break;
                    }

                    if (HasPropellerName(descendant, propellerNumber))
                    {
                        namedFallbacks[propellerIndex] = descendant;
                    }
                }
            }

            for (int i = 0; i < results.Length; i++)
            {
                if (results[i] == null)
                {
                    results[i] = namedFallbacks[i];
                }

                if (results[i] == null)
                {
                    Debug.LogError(
                        $"Drone prefab is missing Propeller.{i + 1} and its prop_{i + 1}_jnt skeletal joint.");
                }
            }

            return results;
        }

        private static bool HasPropellerName(Transform candidate, int propellerNumber)
        {
            string expectedName = $"Propeller.{propellerNumber}";
            if (string.Equals(candidate.name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            MeshFilter meshFilter = candidate.GetComponent<MeshFilter>();
            if (meshFilter != null
                && meshFilter.sharedMesh != null
                && string.Equals(meshFilter.sharedMesh.name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            SkinnedMeshRenderer skinnedRenderer = candidate.GetComponent<SkinnedMeshRenderer>();
            return skinnedRenderer != null
                && skinnedRenderer.sharedMesh != null
                && string.Equals(skinnedRenderer.sharedMesh.name, expectedName, StringComparison.OrdinalIgnoreCase);
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

        public static Transform CreateCactus(
            Transform parent,
            Vector3 localPosition,
            float height,
            float thickness,
            float yaw,
            int arms,
            int seed,
            CactusTuning settings,
            Material bodyMaterial,
            Material blossomMaterial)
        {
            Transform resourceCactus = CreateResourceCactus(parent, localPosition, height, yaw, seed, settings);
            if (resourceCactus != null)
            {
                return resourceCactus;
            }

            GameObject rootObject = new GameObject("Cactus");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);

            CactusTuning cacti = settings ?? new CactusTuning();
            float leanDegrees = DuneVectorMath.HashRange(seed, arms, seed, 13, 0f, Mathf.Max(0f, cacti.MaximumLeanDegrees));
            float leanAngle = DuneVectorMath.HashRange(seed, arms, seed, 19, 0f, 360f) * Mathf.Deg2Rad;
            Vector3 trunkLean = new Vector3(Mathf.Cos(leanAngle), 0f, Mathf.Sin(leanAngle))
                * (Mathf.Tan(leanDegrees * Mathf.Deg2Rad) * height);
            Vector3 trunkStart = Vector3.zero;
            Vector3 trunkTip = (Vector3.up * height) + trunkLean;
            Mesh trunkMesh = GetCactusSegmentMesh(cacti, cacti.TrunkTipScale);
            Mesh armMesh = GetCactusSegmentMesh(cacti, cacti.ArmTipScale);
            CreateCactusSegment("Ribbed Trunk", root, trunkStart, trunkTip, thickness, trunkMesh, bodyMaterial, true);

            for (int i = 0; i < arms; i++)
            {
                float attachmentMinimum = Mathf.Clamp01(Mathf.Min(cacti.ArmAttachmentHeightRange.x, cacti.ArmAttachmentHeightRange.y));
                float attachmentMaximum = Mathf.Clamp01(Mathf.Max(cacti.ArmAttachmentHeightRange.x, cacti.ArmAttachmentHeightRange.y));
                float attachmentFraction = DuneVectorMath.HashRange(seed, i, arms, 23, attachmentMinimum, attachmentMaximum);
                Vector3 shoulder = Vector3.Lerp(trunkStart, trunkTip, attachmentFraction);

                float evenAngle = arms > 0 ? (i * (360f / arms)) : 0f;
                float armAngle = (evenAngle + DuneVectorMath.HashRange(
                    seed,
                    i,
                    arms,
                    29,
                    -Mathf.Max(0f, cacti.ArmAzimuthJitter),
                    Mathf.Max(0f, cacti.ArmAzimuthJitter))) * Mathf.Deg2Rad;
                Vector3 outward = new Vector3(Mathf.Cos(armAngle), 0f, Mathf.Sin(armAngle));

                float reachMinimum = Mathf.Max(0.1f, Mathf.Min(cacti.ArmReachInThicknesses.x, cacti.ArmReachInThicknesses.y));
                float reachMaximum = Mathf.Max(reachMinimum, Mathf.Max(cacti.ArmReachInThicknesses.x, cacti.ArmReachInThicknesses.y));
                float reach = thickness * DuneVectorMath.HashRange(seed, i, arms, 31, reachMinimum, reachMaximum);
                float riseMinimum = Mathf.Max(0.05f, Mathf.Min(cacti.ArmRiseAsHeight.x, cacti.ArmRiseAsHeight.y));
                float riseMaximum = Mathf.Max(riseMinimum, Mathf.Max(cacti.ArmRiseAsHeight.x, cacti.ArmRiseAsHeight.y));
                float rise = height * DuneVectorMath.HashRange(seed, i, arms, 37, riseMinimum, riseMaximum);
                float armThickness = thickness * Mathf.Max(0.2f, cacti.ArmThicknessMultiplier);

                Vector3 elbow = shoulder + (outward * reach) + (Vector3.up * reach * cacti.ArmShoulderLift);
                Vector3 armTip = elbow + (Vector3.up * rise) + (outward * reach * cacti.ArmOutwardLean);
                CreateCactusSegment($"Arm {i + 1} Shoulder", root, shoulder, elbow, armThickness, armMesh, bodyMaterial, true);
                CreateCactusJoint($"Arm {i + 1} Elbow", root, elbow, armThickness, cacti.ArmJointScale, bodyMaterial);
                CreateCactusSegment($"Arm {i + 1} Upturn", root, elbow, armTip, armThickness, armMesh, bodyMaterial, true);

                if (blossomMaterial != null && DuneVectorMath.Hash01(seed, i, arms, 43) < Mathf.Clamp01(cacti.BlossomChance))
                {
                    CreateCactusBlossom($"Arm {i + 1} Blossom", root, armTip, armThickness, cacti, blossomMaterial);
                }
            }

            if (blossomMaterial != null && DuneVectorMath.Hash01(seed, arms, seed, 47) < Mathf.Clamp01(cacti.BlossomChance))
            {
                CreateCactusBlossom("Crown Blossom", root, trunkTip, thickness, cacti, blossomMaterial);
            }

            return root;
        }

        private static Transform CreateResourceCactus(
            Transform parent,
            Vector3 localPosition,
            float height,
            float yaw,
            int seed,
            CactusTuning settings)
        {
            GameObject[] models = GetCactusModels();
            if (models.Length == 0)
            {
                return null;
            }

            int modelIndex = (int)((uint)seed % (uint)models.Length);
            GameObject model = UnityEngine.Object.Instantiate(models[modelIndex], parent, false);
            model.name = $"Cactus ({models[modelIndex].name})";
            Transform root = model.transform;
            root.localPosition = localPosition;

            CactusTuning cacti = settings ?? new CactusTuning();
            float maximumLean = Mathf.Max(0f, cacti.MaximumLeanDegrees);
            float lean = DuneVectorMath.HashRange(seed, modelIndex, seed, 13, 0f, maximumLean);
            float leanDirection = DuneVectorMath.HashRange(seed, modelIndex, seed, 19, 0f, 360f) * Mathf.Deg2Rad;
            root.localRotation = Quaternion.Euler(
                Mathf.Cos(leanDirection) * lean,
                yaw,
                Mathf.Sin(leanDirection) * lean);
            root.localScale = Vector3.one;

            MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            if (!TryCalculateLocalMeshBounds(root, renderers, out Bounds localBounds))
            {
                UnityEngine.Object.Destroy(model);
                return null;
            }

            float modelHeight = Mathf.Max(0.0001f, localBounds.size.y);
            float uniformScale = Mathf.Max(0.1f, height) / modelHeight;
            root.localScale = Vector3.one * uniformScale;

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                worldBounds.Encapsulate(renderers[i].bounds);
            }
            float intendedGroundHeight = parent != null
                ? parent.TransformPoint(localPosition).y
                : localPosition.y;
            root.position += Vector3.up * (intendedGroundHeight - worldBounds.min.y);

            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != null)
                    {
                        materials[materialIndex].enableInstancing = true;
                    }
                }
            }

            CapsuleCollider collider = model.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = localBounds.center;
            collider.height = localBounds.size.y;
            collider.radius = Mathf.Max(localBounds.extents.x, localBounds.extents.z);

            DuneVectorPhotographableMarker.Register(
                model,
                DuneVectorCompendiumSubjectIds.ForCactus(models[modelIndex].name),
                PhotographableSubjectCategory.Misc,
                localBounds);
            DuneVectorSpatialInstancing.Capture(
                model,
                false,
                retainPhotographyRenderer: true);
            return root;
        }

        private static GameObject[] GetCactusModels()
        {
            if (_cactusModels != null)
            {
                return _cactusModels;
            }

            _cactusModels = Resources.LoadAll<GameObject>(CactusResourcePath);
            Array.Sort(_cactusModels, (left, right) =>
                string.CompareOrdinal(left != null ? left.name : string.Empty, right != null ? right.name : string.Empty));
            return _cactusModels;
        }

        private static bool TryCalculateLocalMeshBounds(
            Transform root,
            IReadOnlyList<MeshRenderer> renderers,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;
            for (int i = 0; i < renderers.Count; i++)
            {
                MeshRenderer renderer = renderers[i];
                MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds rendererBounds = DuneVectorSpatialInstancing.TransformBounds(
                    worldToRoot * renderer.transform.localToWorldMatrix,
                    filter.sharedMesh.bounds);
                if (!hasBounds)
                {
                    bounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }
            return hasBounds;
        }

        private static void CreateCactusSegment(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float diameter,
            Mesh mesh,
            Material material,
            bool addCollider)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            GameObject segment = CreateMeshObject(name, parent, mesh, material);
            segment.transform.localPosition = (start + end) * 0.5f;
            segment.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            segment.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            if (addCollider)
            {
                CapsuleCollider collider = segment.AddComponent<CapsuleCollider>();
                collider.direction = 1;
                collider.center = Vector3.zero;
                collider.radius = 0.5f;
                collider.height = 2f;
            }
        }

        private static void CreateCactusJoint(
            string name,
            Transform parent,
            Vector3 position,
            float diameter,
            float jointScale,
            Material material)
        {
            CreatePart(
                PrimitiveType.Sphere,
                name,
                parent,
                position,
                Vector3.one * diameter * Mathf.Max(0.5f, jointScale),
                Quaternion.identity,
                material,
                true);
        }

        private static void CreateCactusBlossom(
            string name,
            Transform parent,
            Vector3 position,
            float thickness,
            CactusTuning settings,
            Material material)
        {
            float size = thickness * Mathf.Max(0.05f, settings.BlossomSizeInThicknesses);
            CreatePart(
                PrimitiveType.Sphere,
                name,
                parent,
                position + (Vector3.up * size * Mathf.Clamp01(settings.BlossomLiftInSizes)),
                new Vector3(size, size * Mathf.Max(0.1f, settings.BlossomHeightScale), size),
                Quaternion.identity,
                material);
        }

        public static Transform CreatePyramid(
            Transform parent,
            Vector3 localPosition,
            float scale,
            float yaw,
            GameObject model,
            Material fallbackMaterial,
            PyramidTuning pyramidLodTuning)
        {
            GameObject root = new GameObject("Small Pyramid");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = Vector3.one;
            DuneVectorPhotographableMarker.Register(
                root,
                DuneVectorCompendiumSubjectIds.Pyramid,
                PhotographableSubjectCategory.Misc);

            Mesh mesh = GetPyramidMesh();
            float targetHalfExtent = Mathf.Max(0.1f, scale);

            if (model == null)
            {
                root.transform.localScale = Vector3.one * targetHalfExtent;
                MeshCollider collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                MeshFilter filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = fallbackMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                return root.transform;
            }

            GameObject visual = UnityEngine.Object.Instantiate(model, root.transform, false);
            visual.name = "Miniature Hieroglyphic Pyramid Visual";

            Collider[] importedColliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < importedColliders.Length; i++)
            {
                importedColliders[i].enabled = false;
                UnityEngine.Object.Destroy(importedColliders[i]);
            }

            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            if (!TryCalculateLocalMeshBounds(root.transform, renderers, out Bounds bounds))
            {
                UnityEngine.Object.Destroy(visual);
                root.transform.localScale = Vector3.one * targetHalfExtent;
                MeshCollider collider = root.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                MeshFilter filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = fallbackMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                return root.transform;
            }

            float horizontalHalfExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
            if (horizontalHalfExtent > 0.0001f)
            {
                visual.transform.localScale *= targetHalfExtent / horizontalHalfExtent;
                TryCalculateLocalMeshBounds(root.transform, renderers, out bounds);
            }
            visual.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            Bounds photographyBounds = new Bounds(
                new Vector3(0f, bounds.extents.y, 0f),
                bounds.size);
            DuneVectorPhotographableMarker.Register(
                root,
                DuneVectorCompendiumSubjectIds.Pyramid,
                PhotographableSubjectCategory.Misc,
                photographyBounds);

            GameObject colliderObject = new GameObject("Pyramid Collider");
            colliderObject.transform.SetParent(root.transform, false);
            float colliderHeight = Mathf.Max(0.0001f, mesh.bounds.size.y);
            colliderObject.transform.localScale = new Vector3(
                targetHalfExtent,
                bounds.size.y / colliderHeight,
                targetHalfExtent);
            MeshCollider modelCollider = colliderObject.AddComponent<MeshCollider>();
            modelCollider.sharedMesh = mesh;

            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != null)
                    {
                        materials[materialIndex].enableInstancing = true;
                    }
                }
            }

            DuneVectorSpatialInstancing.Capture(
                root,
                false,
                pyramidLodTuning,
                retainPhotographyRenderer: true);
            return root.transform;
        }

        public static Transform CreateObelisk(
            Transform parent,
            Vector3 localPosition,
            float scale,
            float yaw,
            GameObject model,
            PyramidTuning lodTuning)
        {
            if (model == null)
            {
                return null;
            }

            GameObject root = new GameObject("Small Obelisk");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            GameObject visual = UnityEngine.Object.Instantiate(model, root.transform, false);
            visual.name = "Obelisk Visual";

            Collider[] importedColliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < importedColliders.Length; i++)
            {
                importedColliders[i].enabled = false;
                UnityEngine.Object.Destroy(importedColliders[i]);
            }

            MeshRenderer[] renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            if (!TryCalculateLocalMeshBounds(root.transform, renderers, out Bounds bounds))
            {
                UnityEngine.Object.Destroy(root);
                return null;
            }

            float targetHalfExtent = Mathf.Max(0.1f, scale);
            float horizontalHalfExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
            if (horizontalHalfExtent > 0.0001f)
            {
                visual.transform.localScale *= targetHalfExtent / horizontalHalfExtent;
                TryCalculateLocalMeshBounds(root.transform, renderers, out bounds);
            }
            visual.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            TryCalculateLocalMeshBounds(root.transform, renderers, out bounds);

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;

            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].receiveShadows = true;
                Material[] materials = renderers[i].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != null)
                    {
                        materials[materialIndex].enableInstancing = true;
                    }
                }
            }

            DuneVectorSpatialInstancing.Capture(root, false, lodTuning);
            return root.transform;
        }

        public static float CalculatePortalVisualRadius(float authoredRadius, RingTuning settings)
        {
            float minimum = Mathf.Max(0.5f, settings.PortalMinimumVisualRadius);
            float maximum = Mathf.Max(minimum, settings.PortalMaximumVisualRadius);
            return Mathf.Clamp(authoredRadius * settings.PortalVisualRadiusMultiplier, minimum, maximum);
        }

        public static double CalculateGroundedPortalCenterHeight(
            double groundHeight,
            float authoredRadius,
            RingTuning settings)
        {
            float visualRadius = CalculatePortalVisualRadius(authoredRadius, settings);
            float outerLineHalfThickness = Mathf.Max(0.01f, settings.PortalOuterLineThickness) * 0.5f;
            return groundHeight + visualRadius + outerLineHalfThickness;
        }

        public static Mesh GetInnermostPortalRingMesh(
            float radius,
            float thicknessMultiplier,
            RingTuning settings)
        {
            float portalRadius = Mathf.Max(0.25f, radius);
            float lineThickness = Mathf.Max(0.01f, settings.PortalInnerLineThickness) *
                Mathf.Max(1f, thicknessMultiplier);
            float innermostRadius = Mathf.Max(
                lineThickness * 2f,
                portalRadius * Mathf.Clamp(settings.PortalInnermostRingRadiusFraction, 0.1f, 0.9f));
            int segments = Mathf.Clamp(settings.PortalCircleSegments, 24, 192);
            string key = $"portal-innermost-ring:{innermostRadius:0.000}:" +
                $"{lineThickness:0.000}:{segments}";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            AddPortalRing(
                vertices,
                uvs,
                triangles,
                innermostRadius,
                lineThickness,
                segments);

            Mesh mesh = new Mesh { name = "Procedural Portal Innermost Ring" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
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
            float visualRadius = CalculatePortalVisualRadius(majorRadius, settings);
            GameObject portalPrefab = GetPortalPrefab(type, settings);
            Renderer[] prefabRenderers = portalPrefab != null
                ? CreatePortalPrefabGeometry(geometryParent, portalPrefab, visualRadius, settings)
                : null;
            CreatePortalGeometry(
                geometryParent,
                materials,
                material,
                visualRadius,
                settings,
                prefabRenderers);

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

        private static GameObject GetPortalPrefab(TraversalRingType type, RingTuning settings)
        {
            if (settings == null || settings.UseProceduralPortalFallbackForAll)
            {
                return null;
            }

            return type switch
            {
                TraversalRingType.GroundBoost => settings.GroundBoostPortalPrefab,
                TraversalRingType.Flight => settings.FlightPortalPrefab,
                TraversalRingType.UpperFlight => settings.UpperFlightPortalPrefab,
                _ => null,
            };
        }

        private static Renderer[] CreatePortalPrefabGeometry(
            Transform parent,
            GameObject portalPrefab,
            float radius,
            RingTuning settings)
        {
            GameObject scaleAnchorObject = new GameObject("Portal Prefab Scale Anchor");
            Transform scaleAnchor = scaleAnchorObject.transform;
            scaleAnchor.SetParent(parent, false);
            float authoredRadius = Mathf.Max(0.01f, settings.PortalPrefabAuthoredRadius);
            float fittedScale = (radius / authoredRadius)
                * Mathf.Max(0.01f, settings.PortalPrefabScaleMultiplier);
            scaleAnchor.localScale = Vector3.one * fittedScale;

            GameObject prefabVisual = UnityEngine.Object.Instantiate(portalPrefab, scaleAnchor, false);
            prefabVisual.name = $"{portalPrefab.name} Visual";
            prefabVisual.transform.localPosition = Vector3.zero;

            Collider[] colliders = prefabVisual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            return prefabVisual.GetComponentsInChildren<Renderer>(true);
        }

        private static void CreatePortalGeometry(
            Transform parent,
            DuneVectorMaterials materials,
            Material lineMaterial,
            float radius,
            RingTuning settings,
            Renderer[] additionalRenderers = null)
        {
            float lineLayerDepth = Mathf.Max(0f, settings.PortalLineLayerDepth);
            Material haloMaterial = materials.CreatePortalHaloMaterial(lineMaterial);
            GameObject rearHalo = CreateMeshObject(
                "Recessed Portal Halo",
                parent,
                GetPortalLineMesh(
                    radius,
                    settings,
                    settings.PortalHaloWidthMultiplier,
                    PortalLineLayer.Rear,
                    false),
                haloMaterial);
            rearHalo.transform.localPosition = Vector3.back * lineLayerDepth;
            DisableRendererShadows(rearHalo);
            MeshRenderer rearHaloRenderer = rearHalo.GetComponent<MeshRenderer>();
            rearHaloRenderer.sortingOrder = -2;

            GameObject centerHalo = CreateMeshObject(
                "Center Portal Halo",
                parent,
                GetPortalLineMesh(
                    radius,
                    settings,
                    settings.PortalHaloWidthMultiplier,
                    PortalLineLayer.Center,
                    false),
                haloMaterial);
            DisableRendererShadows(centerHalo);
            MeshRenderer centerHaloRenderer = centerHalo.GetComponent<MeshRenderer>();
            centerHaloRenderer.sortingOrder = -2;

            GameObject frontHalo = CreateMeshObject(
                "Forward Portal Halo",
                parent,
                GetPortalLineMesh(
                    radius,
                    settings,
                    settings.PortalHaloWidthMultiplier,
                    PortalLineLayer.Front,
                    false),
                haloMaterial);
            frontHalo.transform.localPosition = Vector3.forward * lineLayerDepth;
            DisableRendererShadows(frontHalo);
            MeshRenderer frontHaloRenderer = frontHalo.GetComponent<MeshRenderer>();
            frontHaloRenderer.sortingOrder = -2;

            GameObject rearLinework = CreateMeshObject(
                "Recessed Portal Linework",
                parent,
                GetPortalLineMesh(radius, settings, 1f, PortalLineLayer.Rear),
                materials.CreatePortalDepthLineMaterial(lineMaterial, false));
            rearLinework.transform.localPosition = Vector3.back * lineLayerDepth;
            DisableRendererShadows(rearLinework);
            MeshRenderer rearLineRenderer = rearLinework.GetComponent<MeshRenderer>();
            rearLineRenderer.sortingOrder = -1;

            GameObject centerLinework = CreateMeshObject(
                "Center Portal Linework",
                parent,
                GetPortalLineMesh(radius, settings, 1f, PortalLineLayer.Center),
                lineMaterial);
            DisableRendererShadows(centerLinework);
            MeshRenderer centerLineRenderer = centerLinework.GetComponent<MeshRenderer>();

            GameObject frontLinework = CreateMeshObject(
                "Forward Portal Linework",
                parent,
                GetPortalLineMesh(radius, settings, 1f, PortalLineLayer.Front),
                materials.CreatePortalDepthLineMaterial(lineMaterial, true));
            frontLinework.transform.localPosition = Vector3.forward * lineLayerDepth;
            DisableRendererShadows(frontLinework);
            MeshRenderer frontLineRenderer = frontLinework.GetComponent<MeshRenderer>();
            frontLineRenderer.sortingOrder = 1;

            ParticleSystemRenderer sparkRenderer = CreatePortalEdgeSparks(
                parent,
                materials.CreatePortalParticleMaterial(lineMaterial),
                radius,
                settings);

            GameObject activationPulse = CreateMeshObject(
                "Activation Outward Pulse",
                parent,
                GetPortalActivationPulseMesh(
                    radius,
                    settings.PortalActivationPulseLineThickness,
                    settings.PortalCircleSegments),
                lineMaterial);
            activationPulse.transform.localPosition = Vector3.forward * lineLayerDepth;
            DisableRendererShadows(activationPulse);
            MeshRenderer activationPulseRenderer = activationPulse.GetComponent<MeshRenderer>();
            activationPulseRenderer.sortingOrder = 3;
            activationPulse.SetActive(false);

            List<Renderer> portalRenderers = new List<Renderer>
            {
                rearHaloRenderer,
                centerHaloRenderer,
                frontHaloRenderer,
                rearLineRenderer,
                centerLineRenderer,
                frontLineRenderer,
                sparkRenderer,
                activationPulseRenderer,
            };
            if (additionalRenderers != null)
            {
                portalRenderers.AddRange(additionalRenderers);
            }

            DuneVectorPortalVisual portalVisual = parent.gameObject.AddComponent<DuneVectorPortalVisual>();
            portalVisual.Initialize(
                portalRenderers.ToArray(),
                activationPulseRenderer,
                activationPulse.transform,
                settings);
        }

        private static Mesh GetPortalActivationPulseMesh(float radius, float thickness, int segmentCount)
        {
            int segments = Mathf.Clamp(segmentCount, 24, 192);
            float lineThickness = Mathf.Max(0.01f, thickness);
            string key = $"portal-activation-pulse:{radius:0.000}:{lineThickness:0.000}:{segments}";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            AddPortalRing(vertices, uvs, triangles, radius, lineThickness, segments, 3f);

            Mesh mesh = new Mesh { name = "Portal Activation Outward Pulse" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
        }

        private static ParticleSystemRenderer CreatePortalEdgeSparks(
            Transform parent,
            Material material,
            float radius,
            RingTuning settings)
        {
            GameObject sparkObject = new GameObject("Sparse Portal Edge Sparks");
            Transform sparkTransform = sparkObject.transform;
            sparkTransform.SetParent(parent, false);
            sparkTransform.localPosition = Vector3.forward * Mathf.Max(0f, settings.PortalLineLayerDepth);

            ParticleSystem particles = sparkObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = Mathf.Max(1, settings.PortalSparkMaximumParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.01f, settings.PortalSparkMinimumLifetime),
                Mathf.Max(settings.PortalSparkMinimumLifetime, settings.PortalSparkMaximumLifetime));
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0f, settings.PortalSparkMinimumSpeed),
                Mathf.Max(settings.PortalSparkMinimumSpeed, settings.PortalSparkMaximumSpeed));
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.001f, settings.PortalSparkMinimumSize),
                Mathf.Max(settings.PortalSparkMinimumSize, settings.PortalSparkMaximumSize));

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Mathf.Max(0f, settings.PortalSparkEmissionRate);

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius + Mathf.Max(0f, settings.PortalSparkEdgeOffset);
            shape.radiusThickness = 0f;
            shape.randomDirectionAmount = Mathf.Clamp01(settings.PortalSparkDirectionRandomness);

            Gradient fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(settings.PortalSparkFadeStart)),
                    new GradientAlphaKey(0f, 1f),
                });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = fade;

            ParticleSystemRenderer renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = Mathf.Max(0f, settings.PortalSparkLengthScale);
            renderer.velocityScale = Mathf.Max(0f, settings.PortalSparkVelocityScale);
            renderer.cameraVelocityScale = 0f;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 2;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            return renderer;
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

            GameObject pivotObject = new GameObject("Collectible Icon");
            Transform pivot = pivotObject.transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localOffset;
            pivot.localRotation = Quaternion.Euler(localEulerAngles);

            GameObject modelObject = UnityEngine.Object.Instantiate(model, pivot, false);
            modelObject.name = "Collectible Model";
            Transform modelTransform = modelObject.transform;
            modelTransform.localPosition = Vector3.zero;
            modelTransform.localRotation = Quaternion.identity;
            modelTransform.localScale = Vector3.one;

            Renderer[] renderers = modelObject.GetComponentsInChildren<Renderer>(true);
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

            Collider[] colliders = modelObject.GetComponentsInChildren<Collider>(true);
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
                    modelTransform.localScale = Vector3.one * (Mathf.Max(0.1f, targetSize) / largestDimension);

                    Bounds scaledBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        scaledBounds.Encapsulate(renderers[i].bounds);
                    }
                    modelTransform.position += pivot.position - scaledBounds.center;
                }
            }
            return pivot;
        }

        public static Transform CreateJobRingVisual(Transform parent, bool isPickup, DuneVectorMaterials materials, float radius)
        {
            Material material = isPickup ? materials.PickupRing : materials.DeliveryRing;
            GameObject visualRoot = new GameObject(isPickup ? "Pickup Ring Visual" : "Delivery Ring Visual");
            visualRoot.transform.SetParent(parent, false);

            float visualRadius = CalculatePortalVisualRadius(radius, materials.RingPortalTuning);
            CreatePortalGeometry(
                visualRoot.transform,
                materials,
                material,
                visualRadius,
                materials.RingPortalTuning);

            return visualRoot.transform;
        }

        public static Transform CreatePackageVisual(Transform parent, DuneVectorMaterials materials, float scale)
        {
            GameObject rootObject = new GameObject("Delivery Package");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);

            DeliveryTuning settings = materials.PackageVisualTuning;
            if (!settings.UseProceduralPackageFallback && materials.PackageModel != null)
            {
                CreateImportedPackageModel(root, materials.PackageModel, settings);
            }
            else
            {
                CreatePart(PrimitiveType.Cube, "Package Body", root, Vector3.zero, new Vector3(1.2f, 0.82f, 1f), Quaternion.identity, materials.Package);
                CreatePart(PrimitiveType.Cube, "Package Strap A", root, new Vector3(0f, 0.43f, 0f), new Vector3(0.18f, 0.05f, 1.04f), Quaternion.identity, materials.DroneDark);
                CreatePart(PrimitiveType.Cube, "Package Strap B", root, new Vector3(0f, 0.43f, 0f), new Vector3(1.24f, 0.05f, 0.18f), Quaternion.identity, materials.DroneDark);
            }

            root.localScale = Vector3.one * scale;
            return root;
        }

        private static void CreateImportedPackageModel(
            Transform root,
            GameObject packageModel,
            DeliveryTuning settings)
        {
            GameObject pivotObject = new GameObject("Package Model Pivot");
            Transform pivot = pivotObject.transform;
            pivot.SetParent(root, false);
            pivot.localPosition = settings.PackageModelLocalOffset;
            pivot.localRotation = Quaternion.Euler(settings.PackageModelLocalEulerAngles);

            GameObject modelObject = UnityEngine.Object.Instantiate(packageModel, pivot, false);
            modelObject.name = packageModel.name;
            Transform modelTransform = modelObject.transform;
            modelTransform.localPosition = Vector3.zero;
            Vector3 prefabLocalScale = modelTransform.localScale;
            DisableImportedAnimationPlayback(modelObject);

            Collider[] colliders = modelObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
                UnityEngine.Object.Destroy(colliders[i]);
            }

            Renderer[] renderers = modelObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float targetSize = Mathf.Max(
                settings.PackageDropColliderSize.x,
                settings.PackageDropColliderSize.y,
                settings.PackageDropColliderSize.z);
            if (largestDimension > 0.0001f)
            {
                modelTransform.localScale = prefabLocalScale *
                    (Mathf.Max(0.1f, targetSize) / largestDimension);

                Bounds scaledBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    scaledBounds.Encapsulate(renderers[i].bounds);
                }
                modelTransform.position += pivot.position - scaledBounds.center;
            }
        }

        public static Transform CreateFlyingEnemyVisual(Transform parent, DuneVectorMaterials materials, float scale)
        {
            GameObject rootObject = new GameObject("Sky Piercer Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            FlyingEnemyTuning settings = materials.FlyingEnemyVisualTuning;
            if (!settings.UseProceduralVisualFallback && materials.FlyingEnemyModel != null)
            {
                GameObject model = UnityEngine.Object.Instantiate(materials.FlyingEnemyModel, root);
                model.name = materials.FlyingEnemyModel.name;
                model.transform.localPosition = settings.KunaiLocalPosition;
                model.transform.localRotation = Quaternion.Euler(settings.KunaiLocalEulerAngles);
                model.transform.localScale = settings.KunaiLocalScale;
                DisableImportedAnimationPlayback(model);
                return root;
            }

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

        public static Transform CreateVesperKiteVisual(
            Transform parent,
            DuneVectorMaterials materials,
            VesperKiteTuning settings)
        {
            GameObject rootObject = new GameObject("Vesper Kite Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * settings.VisualScale;

            if (!settings.UseProceduralVisualFallback)
            {
                GameObject prefab = Resources.Load<GameObject>(settings.PrefabResourcePath);
                if (prefab != null)
                {
                    GameObject model = UnityEngine.Object.Instantiate(prefab, root);
                    model.name = prefab.name;
                    model.transform.localPosition = settings.PrefabLocalPosition;
                    model.transform.localRotation =
                        Quaternion.Euler(settings.PrefabLocalEulerAngles);
                    model.transform.localScale = settings.PrefabLocalScale;
                    EnableImportedAnimationPlayback(model, settings);
                    return root;
                }

                Debug.LogWarning(
                    $"Vesper Kite prefab was not found at Resources/{settings.PrefabResourcePath}. " +
                    "Using the procedural fallback.");
            }

            CreatePart(
                PrimitiveType.Sphere,
                "Obsidian Body",
                root,
                Vector3.zero,
                settings.BodyScale,
                Quaternion.identity,
                materials.VesperKiteBody);

            CreatePart(
                PrimitiveType.Sphere,
                "Left Wing",
                root,
                Vector3.left * settings.WingOffset,
                settings.WingScale,
                Quaternion.Euler(
                    0f,
                    -settings.WingSweepDegrees,
                    settings.WingDihedralDegrees),
                materials.VesperKiteBody);
            CreatePart(
                PrimitiveType.Sphere,
                "Right Wing",
                root,
                Vector3.right * settings.WingOffset,
                settings.WingScale,
                Quaternion.Euler(
                    0f,
                    settings.WingSweepDegrees,
                    -settings.WingDihedralDegrees),
                materials.VesperKiteBody);

            Vector3 auroraScale = settings.WingScale * settings.AuroraScaleMultiplier;
            Transform leftAurora = CreatePart(
                PrimitiveType.Sphere,
                "Left Aurora Veil",
                root,
                (Vector3.left * settings.WingOffset) + (Vector3.up * settings.AuroraVerticalOffset),
                auroraScale,
                Quaternion.Euler(
                    0f,
                    -settings.WingSweepDegrees,
                    settings.WingDihedralDegrees),
                materials.VesperKiteAurora);
            Transform rightAurora = CreatePart(
                PrimitiveType.Sphere,
                "Right Aurora Veil",
                root,
                (Vector3.right * settings.WingOffset) + (Vector3.up * settings.AuroraVerticalOffset),
                auroraScale,
                Quaternion.Euler(
                    0f,
                    settings.WingSweepDegrees,
                    -settings.WingDihedralDegrees),
                materials.VesperKiteAurora);
            DisableRendererShadows(leftAurora.gameObject);
            DisableRendererShadows(rightAurora.gameObject);

            CreatePart(
                PrimitiveType.Sphere,
                "Ribbon Tail",
                root,
                Vector3.back * settings.TailScale.z,
                settings.TailScale,
                Quaternion.identity,
                materials.VesperKiteAurora);

            Transform core = CreatePart(
                PrimitiveType.Sphere,
                "Vesper Core",
                root,
                settings.CoreOffset,
                settings.CoreScale,
                Quaternion.identity,
                materials.VesperKiteAurora);
            DisableRendererShadows(core.gameObject);

            return root;
        }

        public static Transform CreateVesperPilgrimVisual(
            Transform parent,
            DuneVectorMaterials materials,
            VesperKiteTuning settings)
        {
            GameObject rootObject = new GameObject("Redshift Pilgrim Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * settings.PilgrimVisualScale;

            Transform core = CreatePart(
                PrimitiveType.Sphere,
                "Pilgrim Core",
                root,
                Vector3.zero,
                settings.PilgrimCoreScale,
                Quaternion.identity,
                materials.VesperPilgrim);
            DisableRendererShadows(core.gameObject);

            GameObject ring = CreateMeshObject(
                "Pilgrim Vow Ring",
                root,
                GetTorusMesh(
                    settings.PilgrimRingRadius,
                    settings.PilgrimRingThickness,
                    36,
                    6),
                materials.VesperPilgrim);
            DisableRendererShadows(ring);

            int nodeCount = Mathf.Clamp(settings.PilgrimNodeCount, 2, 8);
            for (int i = 0; i < nodeCount; i++)
            {
                float degrees = (360f * i) / nodeCount;
                float radians = degrees * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
                Transform node = CreatePart(
                    PrimitiveType.Cube,
                    $"Pilgrim Vow Node {i + 1}",
                    root,
                    radial * settings.PilgrimRingRadius,
                    settings.PilgrimNodeScale,
                    Quaternion.Euler(0f, 0f, degrees),
                    materials.VesperPilgrim);
                DisableRendererShadows(node.gameObject);
            }

            GameObject centerEffectsObject = new GameObject("Pilgrim Center Effects");
            Transform centerEffects = centerEffectsObject.transform;
            centerEffects.SetParent(root, false);
            CreateVesperPilgrimCenterEffect(
                centerEffects,
                "Pilgrim Purple Rift Effect",
                settings.PilgrimRiftEffectResourcePath,
                settings.PilgrimRiftEffectLocalPosition,
                settings.PilgrimRiftEffectLocalEulerAngles,
                settings.PilgrimRiftEffectLocalScale);
            return root;
        }

        private static void CreateVesperPilgrimCenterEffect(
            Transform parent,
            string instanceName,
            string resourcePath,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            if (parent == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"Vesper missile center effect was not found at Resources/{resourcePath}.");
                return;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = instanceName;
            Transform instanceTransform = instance.transform;
            instanceTransform.localPosition = localPosition;
            instanceTransform.localRotation = Quaternion.Euler(localEulerAngles);
            instanceTransform.localScale = Vector3.Scale(
                prefab.transform.localScale,
                localScale);
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
            int finCount = Mathf.Max(3, settings.CrownFinCount);
            bool createdPrefabHull = false;
            if (settings.UsePrefabModel && settings.StormPyramidPrefab != null)
            {
                UnityEngine.Object clone = UnityEngine.Object.Instantiate(
                    (UnityEngine.Object)settings.StormPyramidPrefab,
                    armorRotor);
                GameObject model = clone as GameObject;
                if (model != null)
                {
                    model.name = "Storm Pyramid Prefab Hull";
                    model.transform.localPosition = settings.PrefabLocalPosition;
                    model.transform.localRotation = Quaternion.Euler(settings.PrefabLocalEulerAngles);
                    model.transform.localScale = settings.PrefabLocalScale;
                    createdPrefabHull = true;
                }
                else if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone);
                }
            }

            if (!createdPrefabHull)
            {
                CreateMeshObject(
                    "Faceted Inverted Pyramid Hull",
                    armorRotor,
                    GetStormPyramidMesh(bodyWidth, bodyHeight, cornerCut),
                    materials.StormPyramidBody);
            }

            if (!createdPrefabHull)
            {
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
            }

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

            if (settings.RingPrefab != null)
            {
                GameObject ring = UnityEngine.Object.Instantiate(settings.RingPrefab, root, false);
                ring.name = "Superman Ring";
                ring.transform.localPosition = settings.RingPrefabLocalPosition;
                ring.transform.localRotation = Quaternion.Euler(settings.RingPrefabLocalEulerAngles);
                ring.transform.localScale = Vector3.one * settings.RingPrefabScale;
                DisableRendererShadowsRecursively(ring);
            }

            PlayerStrikeOrbSatelliteTuning[] orbitingOrbs = settings.OrbitingOrbs;
            if (orbitingOrbs != null)
            {
                for (int i = 0; i < orbitingOrbs.Length; i++)
                {
                    PlayerStrikeOrbSatelliteTuning orb = orbitingOrbs[i];
                    if (orb == null)
                    {
                        continue;
                    }

                    CreateOrbitingOrb(
                        root,
                        $"Orbiting Orb Pivot {i + 1}",
                        $"Orbiting Orb {i + 1}",
                        orb.OrbitTilt,
                        orb.StartAngle,
                        settings.OrbitRadius,
                        settings.OrbitingOrbRadius,
                        materials.PlayerStrikeOrbCore,
                        materials.PlayerStrikeOrbTrail,
                        settings);
                }
            }

            GameObject halo = CreateMeshObject(
                "Charge Halo",
                root,
                GetTorusMesh(settings.ChargeHaloRadius, settings.ChargeHaloThickness, 48, 6),
                materials.PlayerStrikeOrbLightningWarning);
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
            Material orbMaterial,
            Material trailMaterial,
            PlayerStrikeOrbTuning settings)
        {
            GameObject pivotObject = new GameObject(pivotName);
            Transform pivot = pivotObject.transform;
            pivot.SetParent(parent, false);
            pivot.localRotation = Quaternion.Euler(tilt, 0f, startAngle);

            if (settings.UseModularSphereMissileVisual
                && settings.ModularSphereMissilePrefab != null)
            {
                GameObject missile = UnityEngine.Object.Instantiate(
                    settings.ModularSphereMissilePrefab,
                    pivot,
                    false);
                missile.name = orbName;
                missile.transform.localPosition = Vector3.right * orbitRadius;
                missile.transform.localRotation = Quaternion.identity;
                missile.transform.localScale =
                    Vector3.one * settings.ModularSphereMissileScale;
                DisableRendererShadowsRecursively(missile);
                return;
            }

            Transform orb = CreatePart(
                PrimitiveType.Sphere,
                orbName,
                pivot,
                Vector3.right * orbitRadius,
                Vector3.one * orbRadius,
                Quaternion.identity,
                orbMaterial);
            DisableRendererShadows(orb.gameObject);

            TrailRenderer trail = orb.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = settings.OrbTrailDuration;
            trail.startWidth = settings.OrbTrailStartWidth * settings.VisualScale;
            trail.endWidth = settings.OrbTrailEndWidth * settings.VisualScale;
            trail.minVertexDistance =
                settings.OrbTrailMinimumVertexDistance * settings.VisualScale;
            trail.numCornerVertices = settings.OrbTrailCornerVertices;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = true;
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
            return CreateStormStrikeMarker(
                parent,
                materials.LightningWarning,
                materials.Lightning,
                radius);
        }

        public static Transform CreateStormStrikeMarker(
            Transform parent,
            Material warningMaterial,
            Material lightningMaterial,
            float radius)
        {
            GameObject rootObject = new GameObject("Lightning Strike Marker");
            Transform root = rootObject.transform;
            root.SetParent(parent, true);

            GameObject outerRing = CreateMeshObject(
                "Outer Warning Ring",
                root,
                GetTorusMesh(Mathf.Max(0.2f, radius), 0.09f, 48, 6),
                warningMaterial);
            outerRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(outerRing);

            GameObject innerRing = CreateMeshObject(
                "Inner Warning Ring",
                root,
                GetTorusMesh(Mathf.Max(0.12f, radius * 0.58f), 0.055f, 42, 6),
                warningMaterial);
            innerRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(innerRing);

            Transform impactFlash = CreatePart(
                PrimitiveType.Sphere,
                "Strike Impact Flash",
                root,
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                lightningMaterial);
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
            GameObject impactWaveObject = new GameObject("Ground Impact Wave");
            Transform impactWave = impactWaveObject.transform;
            impactWave.SetParent(marker, false);
            impactWave.localPosition = Vector3.up * Mathf.Max(0f, heightOffset);

            float safeRadius = Mathf.Max(0.01f, radius);
            float safeThickness = Mathf.Min(
                Mathf.Max(0.005f, ringThickness),
                safeRadius * 0.5f);
            GameObject expandingRim = CreateMeshObject(
                "Expanding Impact Rim",
                impactWave,
                GetTorusMesh(
                    safeRadius - safeThickness,
                    safeThickness,
                    48,
                    6),
                materials.Lightning);
            expandingRim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            DisableRendererShadows(expandingRim);

            Transform groundFlash = CreatePart(
                PrimitiveType.Cylinder,
                "Expanding Ground Flash",
                impactWave,
                Vector3.zero,
                new Vector3(
                    safeRadius * 2f,
                    Mathf.Max(0.002f, safeThickness * 0.08f),
                    safeRadius * 2f),
                Quaternion.identity,
                materials.Lightning);
            DisableRendererShadows(groundFlash.gameObject);

            impactWave.localScale = Vector3.zero;
            impactWaveObject.SetActive(false);
            return impactWave;
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

            CreateGroundExplosionVisual(root, materials);
            return root;
        }

        public static Transform CreateGroundExplosionVisual(
            Transform parent,
            DuneVectorMaterials materials)
        {
            Transform flash = CreatePart(
                PrimitiveType.Sphere,
                "Explosion Flash",
                parent,
                Vector3.up * 0.18f,
                Vector3.zero,
                Quaternion.identity,
                materials.GroundEnemyWarning);
            DisableRendererShadows(flash.gameObject);
            return flash;
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

        private static void DisableRendererShadowsRecursively(GameObject gameObject)
        {
            Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
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

        private static Mesh GetPortalLineMesh(
            float radius,
            RingTuning settings,
            float thicknessMultiplier,
            PortalLineLayer layer,
            bool includeRunes = true)
        {
            int concentricCount = Mathf.Clamp(settings.PortalConcentricRingCount, 1, 6);
            int circleSegments = Mathf.Clamp(settings.PortalCircleSegments, 24, 192);
            int spokeCount = Mathf.Clamp(settings.PortalSpokeCount, 3, 32);
            int glyphCount = Mathf.Clamp(settings.PortalGlyphCount, 3, 32);
            int rayCount = Mathf.Clamp(settings.PortalExteriorRayCount, 0, 24);
            float clampedThicknessMultiplier = Mathf.Max(1f, thicknessMultiplier);
            string key = $"portal-lines-v5:{layer}:{includeRunes}:{radius:0.000}:{clampedThicknessMultiplier:0.000}:" +
                $"{settings.PortalOuterLineThickness:0.000}:" +
                $"{settings.PortalInnerLineThickness:0.000}:{circleSegments}:{concentricCount}:" +
                $"{settings.PortalInnermostRingRadiusFraction:0.000}:{spokeCount}:" +
                $"{settings.PortalSpokeInnerRadiusFraction:0.000}:{settings.PortalSpokeThickness:0.000}:{glyphCount}:" +
                $"{settings.PortalGlyphRadiusFraction:0.000}:{settings.PortalGlyphStrokeThickness:0.000}:" +
                $"{settings.PortalGlyphSizeFraction:0.000}:{settings.PortalRuneSizeVariation:0.000}:" +
                $"{settings.PortalRuneSpacingVariation:0.000}:" +
                $"{settings.PortalRuneRotationVariationDegrees:0.000}:" +
                $"{settings.PortalRuneBrightnessVariation:0.000}:{rayCount}:" +
                $"{settings.PortalExteriorRayLengthFraction:0.000}";
            if (MeshCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            float outerThickness = Mathf.Max(0.01f, settings.PortalOuterLineThickness) * clampedThicknessMultiplier;
            float innerThickness = Mathf.Max(0.01f, settings.PortalInnerLineThickness) * clampedThicknessMultiplier;
            float innermostRadius = Mathf.Max(
                outerThickness * 2f,
                radius * Mathf.Clamp(settings.PortalInnermostRingRadiusFraction, 0.1f, 0.9f));

            if (layer == PortalLineLayer.All || layer == PortalLineLayer.Center)
            {
                AddPortalRing(vertices, uvs, triangles, radius, outerThickness, circleSegments, 3f);
            }
            if (layer == PortalLineLayer.All || layer == PortalLineLayer.Rear)
            {
                AddPortalRing(vertices, uvs, triangles, innermostRadius, innerThickness, circleSegments);
            }
            for (int ringIndex = 0; ringIndex < concentricCount; ringIndex++)
            {
                float interpolation = (ringIndex + 1f) / (concentricCount + 1f);
                float ringRadius = Mathf.Lerp(innermostRadius, radius, interpolation);
                PortalLineLayer ringLayer = ringIndex % 2 == 0
                    ? PortalLineLayer.Rear
                    : PortalLineLayer.Front;
                if (layer == PortalLineLayer.All || layer == ringLayer)
                {
                    AddPortalRing(vertices, uvs, triangles, ringRadius, innerThickness, circleSegments);
                }
            }

            float spokeOuterRadius = radius - (outerThickness * 1.5f);
            float requestedSpokeInnerRadius = radius * Mathf.Clamp(
                settings.PortalSpokeInnerRadiusFraction,
                0.1f,
                0.9f);
            float spokeInnerRadius = Mathf.Min(
                spokeOuterRadius - innerThickness,
                Mathf.Max(innermostRadius + (innerThickness * 1.5f), requestedSpokeInnerRadius));
            if (layer == PortalLineLayer.All || layer == PortalLineLayer.Center)
            {
                for (int spokeIndex = 0; spokeIndex < spokeCount; spokeIndex++)
                {
                    float angle = ((spokeIndex + 0.5f) / spokeCount) * Mathf.PI * 2f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float shortened = spokeIndex % 3 == 0 ? 0.18f : 0f;
                    Vector2 start = direction * Mathf.Lerp(spokeInnerRadius, spokeOuterRadius, shortened);
                    Vector2 end = direction * spokeOuterRadius;
                    AddPortalStroke(
                        vertices,
                        uvs,
                        triangles,
                        start,
                        end,
                        Mathf.Max(0.01f, settings.PortalSpokeThickness) * clampedThicknessMultiplier);
                }
            }

            float glyphRadius = radius * Mathf.Clamp(settings.PortalGlyphRadiusFraction, 0.1f, 0.95f);
            float glyphSize = Mathf.Max(0.03f, radius * settings.PortalGlyphSizeFraction);
            float glyphThickness = Mathf.Max(0.01f, settings.PortalGlyphStrokeThickness) * clampedThicknessMultiplier;
            int variationSeed = Mathf.RoundToInt(radius * 100f);
            if (includeRunes)
            {
                for (int glyphIndex = 0; glyphIndex < glyphCount; glyphIndex++)
                {
                    PortalLineLayer glyphLayer = (glyphIndex % 3) switch
                    {
                        0 => PortalLineLayer.Rear,
                        1 => PortalLineLayer.Center,
                        _ => PortalLineLayer.Front,
                    };
                    if (layer != PortalLineLayer.All && layer != glyphLayer)
                    {
                        continue;
                    }
                    float slotAngle = (Mathf.PI * 2f) / glyphCount;
                    float spacingVariation = DuneVectorMath.HashRange(
                        glyphIndex,
                        glyphCount,
                        variationSeed,
                        101,
                        -1f,
                        1f);
                    float angle = (((glyphIndex + 0.25f) / glyphCount) * Mathf.PI * 2f)
                        + (spacingVariation * slotAngle * settings.PortalRuneSpacingVariation);
                    Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 tangent = new Vector2(-radial.y, radial.x);
                    float rotationVariation = DuneVectorMath.HashRange(
                        glyphIndex,
                        glyphCount,
                        variationSeed,
                        211,
                        -settings.PortalRuneRotationVariationDegrees,
                        settings.PortalRuneRotationVariationDegrees) * Mathf.Deg2Rad;
                    float rotationCosine = Mathf.Cos(rotationVariation);
                    float rotationSine = Mathf.Sin(rotationVariation);
                    Vector2 runeTangent = (tangent * rotationCosine) + (radial * rotationSine);
                    Vector2 runeRadial = (-tangent * rotationSine) + (radial * rotationCosine);
                    Vector2 center = radial * glyphRadius;
                    float sizeVariation = DuneVectorMath.HashRange(
                        glyphIndex,
                        glyphCount,
                        variationSeed,
                        307,
                        -settings.PortalRuneSizeVariation,
                        settings.PortalRuneSizeVariation);
                    float brightnessVariation = DuneVectorMath.HashRange(
                        glyphIndex,
                        glyphCount,
                        variationSeed,
                        401,
                        -settings.PortalRuneBrightnessVariation,
                        settings.PortalRuneBrightnessVariation);
                    float runeFeatureMarker = -2f - brightnessVariation;
                    AddPortalRune(
                        vertices,
                        uvs,
                        triangles,
                        glyphIndex,
                        center,
                        runeTangent,
                        runeRadial,
                        glyphSize * (1f + sizeVariation),
                        glyphThickness,
                        runeFeatureMarker);
                }
            }

            float rayLength = Mathf.Max(0f, radius * settings.PortalExteriorRayLengthFraction);
            if (layer == PortalLineLayer.All || layer == PortalLineLayer.Front)
            {
                for (int rayIndex = 0; rayIndex < rayCount; rayIndex++)
                {
                    float angle = ((rayIndex + 0.15f) / Mathf.Max(1, rayCount)) * Mathf.PI * 2f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float variation = 0.55f + (((rayIndex * 37) % 11) / 20f);
                    AddPortalStroke(
                        vertices,
                        uvs,
                        triangles,
                        direction * (radius + outerThickness),
                        direction * (radius + outerThickness + (rayLength * variation)),
                        innerThickness);
                }
            }

            Mesh mesh = new Mesh { name = $"Procedural Transparent Portal Linework - {layer}" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            MeshCache[key] = mesh;
            return mesh;
        }

        private static void AddPortalRune(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            int runeIndex,
            Vector2 center,
            Vector2 tangent,
            Vector2 radial,
            float size,
            float thickness,
            float featureMarker)
        {
            Vector2 Point(float x, float y) => center + (tangent * x * size) + (radial * y * size);
            void Stroke(float ax, float ay, float bx, float by) => AddPortalStroke(
                vertices,
                uvs,
                triangles,
                Point(ax, ay),
                Point(bx, by),
                thickness,
                featureMarker);
            void Node(float x, float y) => AddPortalRuneNode(
                vertices,
                uvs,
                triangles,
                Point(x, y),
                thickness * 1.8f,
                featureMarker);

            switch (runeIndex % 8)
            {
                case 0: // Triangle constellation.
                    Stroke(-0.78f, -0.62f, 0f, 0.82f);
                    Stroke(0f, 0.82f, 0.78f, -0.62f);
                    Stroke(0.78f, -0.62f, -0.78f, -0.62f);
                    Node(0f, 0.82f);
                    break;
                case 1: // Diamond with a trailing star point.
                    Stroke(0f, 0.92f, 0.72f, 0f);
                    Stroke(0.72f, 0f, 0f, -0.92f);
                    Stroke(0f, -0.92f, -0.72f, 0f);
                    Stroke(-0.72f, 0f, 0f, 0.92f);
                    Stroke(0f, -0.92f, 0f, -1.25f);
                    Node(0f, -1.25f);
                    break;
                case 2: // Branching fork.
                    Stroke(0f, -0.98f, 0f, 0.05f);
                    Stroke(0f, 0.05f, -0.76f, 0.82f);
                    Stroke(0f, 0.05f, 0.76f, 0.82f);
                    Stroke(0f, 0.42f, 0.42f, 0.12f);
                    Node(-0.76f, 0.82f);
                    Node(0.76f, 0.82f);
                    break;
                case 3: // Lightning zigzag.
                    Stroke(-0.82f, 0.78f, -0.28f, 0.18f);
                    Stroke(-0.28f, 0.18f, 0.24f, 0.68f);
                    Stroke(0.24f, 0.68f, 0.08f, -0.12f);
                    Stroke(0.08f, -0.12f, 0.82f, -0.78f);
                    break;
                case 4: // Uneven celestial cross.
                    Stroke(0f, -0.98f, 0f, 0.98f);
                    Stroke(-0.82f, 0.18f, 0.7f, 0.18f);
                    Stroke(0.38f, 0.18f, 0.7f, 0.62f);
                    Node(-0.82f, 0.18f);
                    Node(0f, -0.98f);
                    break;
                case 5: // Stepped hook.
                    Stroke(-0.82f, 0.78f, -0.22f, 0.78f);
                    Stroke(-0.22f, 0.78f, -0.22f, 0.08f);
                    Stroke(-0.22f, 0.08f, 0.42f, 0.08f);
                    Stroke(0.42f, 0.08f, 0.42f, -0.72f);
                    Stroke(0.42f, -0.72f, 0.82f, -0.72f);
                    break;
                case 6: // Box rune with split center.
                    Stroke(-0.72f, -0.82f, -0.72f, 0.82f);
                    Stroke(-0.72f, 0.82f, 0.72f, 0.82f);
                    Stroke(0.72f, 0.82f, 0.72f, -0.82f);
                    Stroke(0.72f, -0.82f, -0.72f, -0.82f);
                    Stroke(-0.72f, -0.82f, 0.72f, 0.82f);
                    Node(0f, 0f);
                    break;
                default: // Branching constellation path.
                    Stroke(-0.78f, -0.72f, -0.18f, -0.12f);
                    Stroke(-0.18f, -0.12f, 0.18f, 0.62f);
                    Stroke(0.18f, 0.62f, 0.76f, 0.84f);
                    Stroke(-0.18f, -0.12f, 0.68f, -0.58f);
                    Stroke(0.18f, 0.62f, -0.52f, 0.82f);
                    Node(-0.78f, -0.72f);
                    Node(0.76f, 0.84f);
                    Node(0.68f, -0.58f);
                    Node(-0.52f, 0.82f);
                    break;
            }
        }

        private static void AddPortalRuneNode(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector2 center,
            float size,
            float featureMarker)
        {
            Vector2 horizontal = new Vector2(size, 0f);
            Vector2 vertical = new Vector2(0f, size);
            AddPortalQuad(
                vertices,
                uvs,
                triangles,
                center - horizontal,
                center + vertical,
                center + horizontal,
                center - vertical,
                featureMarker);
        }

        private static void AddPortalRing(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            float radius,
            float thickness,
            int segments,
            float featureMarker = 2f)
        {
            float inner = Mathf.Max(0f, radius - (thickness * 0.5f));
            float outer = radius + (thickness * 0.5f);
            for (int segment = 0; segment < segments; segment++)
            {
                float angleA = (segment / (float)segments) * Mathf.PI * 2f;
                float angleB = ((segment + 1f) / segments) * Mathf.PI * 2f;
                Vector2 directionA = new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA));
                Vector2 directionB = new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB));
                AddPortalQuad(
                    vertices,
                    uvs,
                    triangles,
                    directionA * inner,
                    directionA * outer,
                    directionB * outer,
                    directionB * inner,
                    featureMarker);
            }
        }

        private static void AddPortalStroke(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector2 start,
            Vector2 end,
            float thickness,
            float featureMarker = 0f)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude < 0.000001f)
            {
                return;
            }
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
            AddPortalQuad(
                vertices,
                uvs,
                triangles,
                start - normal,
                start + normal,
                end + normal,
                end - normal,
                featureMarker);
        }

        private static void AddPortalQuad(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            float featureMarker = 0f)
        {
            int firstVertex = vertices.Count;
            vertices.Add(new Vector3(a.x, a.y, 0f));
            vertices.Add(new Vector3(b.x, b.y, 0f));
            vertices.Add(new Vector3(c.x, c.y, 0f));
            vertices.Add(new Vector3(d.x, d.y, 0f));
            bool markedFeature = Mathf.Abs(featureMarker) > 0.001f;
            float startU = markedFeature ? featureMarker : 0f;
            float endU = markedFeature ? featureMarker : 1f;
            uvs.Add(new Vector2(startU, 0f));
            uvs.Add(new Vector2(startU, 1f));
            uvs.Add(new Vector2(endU, 1f));
            uvs.Add(new Vector2(endU, 0f));
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
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

        private static Mesh GetCactusSegmentMesh(CactusTuning settings, float tipScale)
        {
            int ribs = Mathf.Clamp(settings.RibCount, 3, 12);
            int heightSegments = Mathf.Clamp(settings.HeightSegments, 4, 12);
            float ribDepth = Mathf.Clamp(settings.RibDepth, 0f, 0.35f);
            float capLength = Mathf.Clamp(settings.RoundedCapLength, 0.1f, 0.45f);
            float clampedTipScale = Mathf.Clamp(tipScale, 0.4f, 1f);
            string key = $"cactus-segment:{ribs}:{heightSegments}:{ribDepth:0.000}:{capLength:0.000}:{clampedTipScale:0.000}";
            if (!MeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = CreateCactusSegmentMesh(ribs, heightSegments, ribDepth, capLength, clampedTipScale);
                mesh.name = key;
                MeshCache[key] = mesh;
            }
            return mesh;
        }

        private static Mesh CreateCactusSegmentMesh(int ribs, int heightSegments, float ribDepth, float capLength, float tipScale)
        {
            int radialSegments = ribs * 2;
            int ringCount = heightSegments - 1;
            Vector3[] vertices = new Vector3[2 + (ringCount * radialSegments)];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[radialSegments * (heightSegments - 1) * 6];

            vertices[0] = new Vector3(0f, -1f, 0f);
            uvs[0] = new Vector2(0.5f, 0f);
            int topPole = vertices.Length - 1;
            vertices[topPole] = new Vector3(0f, 1f, 0f);
            uvs[topPole] = new Vector2(0.5f, 1f);

            for (int ring = 0; ring < ringCount; ring++)
            {
                float t = (ring + 1f) / heightSegments;
                float y = Mathf.Lerp(-1f, 1f, t);
                float capProfile = t < capLength
                    ? Mathf.Sin((t / capLength) * Mathf.PI * 0.5f)
                    : (t > 1f - capLength
                        ? Mathf.Sin(((1f - t) / capLength) * Mathf.PI * 0.5f)
                        : 1f);
                float taper = Mathf.Lerp(1f, tipScale, t * t * (3f - (2f * t)));
                for (int radial = 0; radial < radialSegments; radial++)
                {
                    float radialT = radial / (float)radialSegments;
                    float angle = radialT * Mathf.PI * 2f;
                    float groove = (radial & 1) == 0 ? 1f : 1f - ribDepth;
                    float radius = 0.5f * capProfile * taper * groove;
                    int vertex = 1 + (ring * radialSegments) + radial;
                    vertices[vertex] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                    uvs[vertex] = new Vector2(radialT, t);
                }
            }

            int triangle = 0;
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int next = (radial + 1) % radialSegments;
                triangles[triangle++] = 0;
                triangles[triangle++] = 1 + radial;
                triangles[triangle++] = 1 + next;

                for (int ring = 0; ring < ringCount - 1; ring++)
                {
                    int lower = 1 + (ring * radialSegments) + radial;
                    int lowerNext = 1 + (ring * radialSegments) + next;
                    int upper = lower + radialSegments;
                    int upperNext = lowerNext + radialSegments;
                    triangles[triangle++] = lower;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = lowerNext;
                    triangles[triangle++] = lowerNext;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = upperNext;
                }

                int topRing = 1 + ((ringCount - 1) * radialSegments);
                triangles[triangle++] = topRing + radial;
                triangles[triangle++] = topPole;
                triangles[triangle++] = topRing + next;
            }

            Mesh mesh = new Mesh { name = "Ribbed Cactus Segment" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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
        private bool _alternateDirections;

        public void Initialize(Transform[] bladeRotors, Transform[] glowRings, DroneVisualTuning settings)
        {
            Initialize(bladeRotors, glowRings, settings, true);
        }

        public void Initialize(
            Transform[] bladeRotors,
            Transform[] glowRings,
            DroneVisualTuning settings,
            bool alternateDirections)
        {
            _bladeRotors = bladeRotors;
            _glowRings = glowRings;
            _settings = settings;
            _alternateDirections = alternateDirections;
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
                    float direction = _alternateDirections && (i & 1) != 0 ? -1f : 1f;
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
