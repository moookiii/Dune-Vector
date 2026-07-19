using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuneVector
{
    public sealed class DuneVectorMaterials : IDisposable
    {
        public Material Sand { get; }
        public Material DroneBody { get; }
        public Material DroneAccent { get; }
        public Material DroneDark { get; }
        public Material Cactus { get; }
        public Material Sandstone { get; }
        public Material BoostRing { get; }
        public Material FlightRing { get; }
        public Material Trail { get; }
        public Material Cloud { get; }
        public Material Package { get; }
        public Material PickupRing { get; }
        public Material DeliveryRing { get; }
        public Material EnemyBody { get; }
        public Material EnemyCore { get; }
        public Material GroundEnemyBody { get; }
        public Material GroundEnemyWarning { get; }
        public Material StormPyramidBody { get; }
        public Material StormPyramidCore { get; }
        public Material Lightning { get; }
        public Material LightningWarning { get; }

        private readonly List<Material> _ownedMaterials = new List<Material>();

        public DuneVectorMaterials()
        {
            Sand = CreateLit("Sand - Warm Rough", new Color(0.62f, 0.36f, 0.16f), 0.14f, 0f);
            DroneBody = CreateLit("Drone - Ivory", new Color(0.75f, 0.78f, 0.78f), 0.72f, 0.7f);
            DroneAccent = CreateLit("Drone - Cyan Emission", new Color(0.015f, 0.12f, 0.16f), 0.78f, 0.45f, new Color(0.0f, 1.6f, 2.8f));
            DroneDark = CreateLit("Drone - Graphite", new Color(0.018f, 0.025f, 0.033f), 0.64f, 0.85f);
            Cactus = CreateLit("Cactus - Stylized", new Color(0.08f, 0.31f, 0.16f), 0.25f, 0f);
            Sandstone = CreateLit("Pyramid - Sandstone", new Color(0.58f, 0.31f, 0.13f), 0.18f, 0f);
            BoostRing = CreateLit("Ring - Boost Amber", new Color(0.42f, 0.09f, 0.008f), 0.65f, 0.4f, new Color(3.6f, 0.72f, 0.025f));
            FlightRing = CreateLit("Ring - Flight Cyan", new Color(0.004f, 0.19f, 0.32f), 0.7f, 0.5f, new Color(0.0f, 2.0f, 3.6f));
            Trail = CreateLit("Drone - Trail", new Color(0.0f, 0.06f, 0.08f), 0.6f, 0.1f, new Color(0.0f, 0.8f, 1.4f));
            Cloud = CreateLit("Cloud - Sunlit", new Color(0.82f, 0.88f, 0.94f), 0.08f, 0f);
            Package = CreateLit("Delivery Package", new Color(0.72f, 0.24f, 0.035f), 0.34f, 0.05f, new Color(1.4f, 0.2f, 0.01f));
            PickupRing = CreateLit("Job Ring - Pickup", new Color(0.32f, 0.015f, 0.48f), 0.72f, 0.32f, new Color(2.8f, 0.05f, 4.2f));
            DeliveryRing = CreateLit("Job Ring - Delivery", new Color(0.015f, 0.42f, 0.12f), 0.68f, 0.28f, new Color(0.05f, 3.8f, 0.45f));
            EnemyBody = CreateLit("Sky Piercer - Body", new Color(0.13f, 0.025f, 0.035f), 0.48f, 0.72f, new Color(1.7f, 0.035f, 0.06f));
            EnemyCore = CreateLit("Sky Piercer - Core", new Color(0.008f, 0.004f, 0.012f), 0.82f, 0.18f, new Color(3.2f, 0.02f, 0.55f));
            GroundEnemyBody = CreateLit("Ground Exploder - Body", new Color(0.055f, 0.045f, 0.04f), 0.5f, 0.78f, new Color(0.16f, 0.025f, 0.005f));
            GroundEnemyWarning = CreateLit("Ground Exploder - Warning", new Color(0.46f, 0.055f, 0.008f), 0.62f, 0.3f, new Color(5.2f, 0.32f, 0.015f));
            StormPyramidBody = CreateLit("Storm Pyramid - Body", new Color(0.025f, 0.035f, 0.09f), 0.58f, 0.82f, new Color(0.08f, 0.12f, 0.55f));
            StormPyramidCore = CreateLit("Storm Pyramid - Core", new Color(0.01f, 0.08f, 0.14f), 0.76f, 0.22f, new Color(0.15f, 3.6f, 6.5f));
            Lightning = CreateLit("Storm Pyramid - Lightning", new Color(0.55f, 0.86f, 1f), 0.92f, 0f, new Color(7.5f, 12f, 18f));
            LightningWarning = CreateLit("Storm Pyramid - Warning", new Color(0.18f, 0.42f, 0.62f), 0.7f, 0f, new Color(0.45f, 2.8f, 5.8f));
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
            ConfigureLitColors(Lightning, settings.LightningColor, settings.LightningEmission);
            ConfigureLitColors(LightningWarning, settings.WarningColor, settings.WarningEmission);
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
    }

    public static class DuneVectorVisuals
    {
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();

        public static Transform CreateDroneVisual(Transform parent, DuneVectorMaterials materials)
        {
            GameObject visualObject = new GameObject("DroneVisualRoot");
            Transform visual = visualObject.transform;
            visual.SetParent(parent, false);
            visual.localPosition = new Vector3(0f, 0.92f, 0f);

            CreatePart(PrimitiveType.Sphere, "Core", visual, Vector3.zero, new Vector3(1.15f, 0.42f, 1.55f), Quaternion.identity, materials.DroneBody);
            CreatePart(PrimitiveType.Cube, "Forward Spine", visual, new Vector3(0f, 0.02f, 0.55f), new Vector3(0.38f, 0.25f, 2.55f), Quaternion.identity, materials.DroneDark);
            CreatePart(PrimitiveType.Cube, "Left Wing", visual, new Vector3(-1.0f, -0.02f, 0f), new Vector3(1.65f, 0.13f, 0.48f), Quaternion.Euler(0f, -8f, 0f), materials.DroneBody);
            CreatePart(PrimitiveType.Cube, "Right Wing", visual, new Vector3(1.0f, -0.02f, 0f), new Vector3(1.65f, 0.13f, 0.48f), Quaternion.Euler(0f, 8f, 0f), materials.DroneBody);
            CreatePart(PrimitiveType.Sphere, "Canopy", visual, new Vector3(0f, 0.27f, 0.2f), new Vector3(0.55f, 0.2f, 0.68f), Quaternion.identity, materials.DroneAccent);

            Vector3[] rotorPositions =
            {
                new Vector3(-1.58f, 0.03f, 0.38f),
                new Vector3(1.58f, 0.03f, 0.38f),
                new Vector3(-1.35f, 0.03f, -0.48f),
                new Vector3(1.35f, 0.03f, -0.48f),
            };

            for (int i = 0; i < rotorPositions.Length; i++)
            {
                Transform rotor = CreatePart(PrimitiveType.Cylinder, $"Rotor {i + 1}", visual, rotorPositions[i], new Vector3(0.5f, 0.035f, 0.5f), Quaternion.identity, materials.DroneDark);
                CreatePart(PrimitiveType.Cylinder, "Glow", rotor, new Vector3(0f, 0.52f, 0f), new Vector3(0.72f, 0.018f, 0.72f), Quaternion.identity, materials.DroneAccent);
            }

            CreateTrail(visual, new Vector3(-0.48f, -0.03f, -1.18f), materials.Trail);
            CreateTrail(visual, new Vector3(0.48f, -0.03f, -1.18f), materials.Trail);
            return visual;
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

        public static Transform CreateRingVisual(Transform parent, TraversalRingType type, DuneVectorMaterials materials, float majorRadius)
        {
            Material material = type == TraversalRingType.GroundBoost ? materials.BoostRing : materials.FlightRing;
            GameObject visualRoot = new GameObject("Ring Visual Root");
            visualRoot.transform.SetParent(parent, false);
            const float arcStart = -65f;
            const float arcSweep = 310f;
            const float tubeRadius = 0.31f;
            GameObject primary = CreateMeshObject(
                "Open Outer Arc",
                visualRoot.transform,
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
                    visualRoot.transform,
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
                    visualRoot.transform,
                    dashPosition,
                    new Vector3(0.58f, 0.1f, 0.15f),
                    Quaternion.Euler(0f, 0f, angleDegrees + 90f),
                    material);
                DisableRendererShadows(dash.gameObject);
            }
            return visualRoot.transform;
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

        public static Transform CreateStormPyramidVisual(Transform parent, DuneVectorMaterials materials, float scale)
        {
            GameObject rootObject = new GameObject("Storm Pyramid Visual");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * scale;

            GameObject body = CreateMeshObject("Inverted Pyramid Body", root, GetPyramidMesh(), materials.StormPyramidBody);
            body.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            body.transform.localScale = new Vector3(2.4f, 2.8f, 2.4f);

            float[] bandHeights = { -0.5f, -1.35f, -2.2f };
            float[] bandWidths = { 4.15f, 3.05f, 1.95f };
            for (int i = 0; i < bandHeights.Length; i++)
            {
                Transform band = CreatePart(
                    PrimitiveType.Cube,
                    $"Electrical Band {i + 1}",
                    root,
                    new Vector3(0f, bandHeights[i], 0f),
                    new Vector3(bandWidths[i], 0.065f, bandWidths[i]),
                    Quaternion.identity,
                    materials.StormPyramidCore);
                DisableRendererShadows(band.gameObject);
            }

            Transform core = CreatePart(
                PrimitiveType.Sphere,
                "Storm Core",
                root,
                new Vector3(0f, 0.12f, 0f),
                new Vector3(0.72f, 0.22f, 0.72f),
                Quaternion.identity,
                materials.StormPyramidCore);
            DisableRendererShadows(core.gameObject);

            GameObject halo = CreateMeshObject(
                "Charge Halo",
                root,
                GetTorusMesh(1.65f, 0.075f, 44, 6),
                materials.LightningWarning);
            halo.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            halo.transform.localScale = Vector3.zero;
            DisableRendererShadows(halo);

            GameObject originObject = new GameObject("Lightning Origin");
            originObject.transform.SetParent(root, false);
            originObject.transform.localPosition = new Vector3(0f, -3.82f, 0f);
            return root;
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

        private static void CreateTrail(Transform parent, Vector3 localPosition, Material material)
        {
            GameObject trailObject = new GameObject("Speed Trail");
            trailObject.transform.SetParent(parent, false);
            trailObject.transform.localPosition = localPosition;
            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = 0.34f;
            trail.startWidth = 0.075f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.18f;
            trail.emitting = true;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }
    }
}
