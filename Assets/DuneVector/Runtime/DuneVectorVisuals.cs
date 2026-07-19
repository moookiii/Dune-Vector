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
            GameObject primary = CreateMeshObject("Ring Visual", visualRoot.transform, GetTorusMesh(majorRadius, 0.22f, 40, 8), material);
            DisableRendererShadows(primary);
            if (type == TraversalRingType.Flight)
            {
                GameObject secondary = CreateMeshObject("Flight Ring Inner", visualRoot.transform, GetTorusMesh(majorRadius - 0.48f, 0.09f, 40, 6), material);
                secondary.transform.localRotation = Quaternion.Euler(0f, 0f, 9f);
                DisableRendererShadows(secondary);
            }
            else
            {
                GameObject chevron = CreatePart(PrimitiveType.Cube, "Boost Chevron", visualRoot.transform, new Vector3(0f, -majorRadius - 0.16f, 0f), new Vector3(1.4f, 0.13f, 0.22f), Quaternion.Euler(0f, 0f, 0f), material).gameObject;
                chevron.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                DisableRendererShadows(chevron);
                Collider chevronCollider = chevron.GetComponent<Collider>();
                if (chevronCollider != null)
                {
                    chevronCollider.enabled = false;
                }
            }
            return visualRoot.transform;
        }

        public static Transform CreateJobRingVisual(Transform parent, bool isPickup, DuneVectorMaterials materials, float radius)
        {
            Material material = isPickup ? materials.PickupRing : materials.DeliveryRing;
            GameObject visualRoot = new GameObject(isPickup ? "Pickup Ring Visual" : "Delivery Ring Visual");
            visualRoot.transform.SetParent(parent, false);

            GameObject primary = CreateMeshObject("Outer Ring", visualRoot.transform, GetTorusMesh(radius, 0.26f, 48, 8), material);
            DisableRendererShadows(primary);

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
