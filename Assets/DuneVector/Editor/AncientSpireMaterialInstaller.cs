using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuneVector.Editor
{
    /// <summary>
    /// Textures the Ancient Spire authored by
    /// ArtSource/Blender/AncientSpire/build_ancient_spire.py.
    ///
    /// The GLB is exported without embedded images on purpose: every map the
    /// spire uses is already an asset in Assets/DuneVector/Resources, and
    /// embedding them added ~125 MB of duplicate 2K/4K JPEGs to a Resources
    /// folder that ships in full with every build. This tool rebuilds the same
    /// material set as URP Lit materials that point at those existing textures,
    /// then remaps the model importer onto them.
    ///
    /// Every colour, tint, tiling and smoothness value it writes is read from
    /// Dune Vector Runtime Settings; nothing designer-facing is authored here.
    /// </summary>
    public static class AncientSpireMaterialInstaller
    {
        private const string ModelPath = "Assets/DuneVector/Resources/AncientSpire.glb";
        private const string MaterialFolder = "Assets/DuneVector/Art/AncientSpire/Materials";
        private const string TextureFolder = "Assets/DuneVector/Resources";
        private const string RuntimeSettingsPath =
            "Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset";

        private enum Family
        {
            Stone,
            Detail,
            Metal,
            Sand,
            Untextured,
        }

        /// <summary>
        /// One entry per material the Blender generator emits. `TextureSet` is the
        /// ambientCG folder under Resources; the maps inside it follow that pack's
        /// `<name>_<Map>.jpg` convention.
        /// </summary>
        private struct MaterialSpec
        {
            public string Name;
            public string TextureSet;
            public bool HasOcclusion;
            public Family Tiling;
        }

        private static readonly MaterialSpec[] Specs =
        {
            new MaterialSpec { Name = "AncientSpire_Stone", TextureSet = "Rock062_2K-JPG", HasOcclusion = true, Tiling = Family.Stone },
            new MaterialSpec { Name = "AncientSpire_StoneDark", TextureSet = "Rock029_2K-JPG", HasOcclusion = true, Tiling = Family.Stone },
            new MaterialSpec { Name = "AncientSpire_Carved", TextureSet = "Concrete025_2K-JPG", HasOcclusion = true, Tiling = Family.Detail },
            new MaterialSpec { Name = "AncientSpire_Cloth", TextureSet = "Concrete025_2K-JPG", HasOcclusion = true, Tiling = Family.Detail },
            new MaterialSpec { Name = "AncientSpire_Bronze", TextureSet = "Metal049B_4K-JPG", HasOcclusion = false, Tiling = Family.Metal },
            new MaterialSpec { Name = "AncientSpire_Plate", TextureSet = "MetalPlates005_4K-JPG", HasOcclusion = false, Tiling = Family.Metal },
            new MaterialSpec { Name = "AncientSpire_Sand", TextureSet = "Ground093C_2K-JPG", HasOcclusion = true, Tiling = Family.Sand },
            new MaterialSpec { Name = "AncientSpire_Accent", TextureSet = null, HasOcclusion = false, Tiling = Family.Untextured },
            new MaterialSpec { Name = "AncientSpire_Relic", TextureSet = null, HasOcclusion = false, Tiling = Family.Untextured },
            new MaterialSpec { Name = "AncientSpire_Interior", TextureSet = null, HasOcclusion = false, Tiling = Family.Untextured },
        };

        [MenuItem("Tools/Dune Vector/Install Ancient Spire Materials")]
        public static void Install()
        {
            DuneVectorRuntimeSettings settings =
                AssetDatabase.LoadAssetAtPath<DuneVectorRuntimeSettings>(RuntimeSettingsPath);
            if (settings == null)
            {
                Debug.LogError("Ancient Spire materials: could not load " + RuntimeSettingsPath);
                return;
            }

            AssetImporter importer = AssetImporter.GetAtPath(ModelPath);
            if (importer == null)
            {
                Debug.LogError("Ancient Spire materials: could not load the model at " + ModelPath);
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Ancient Spire materials: the URP Lit shader is not available in this project.");
                return;
            }

            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            List<string> missing = new List<string>();
            int written = 0;

            for (int i = 0; i < Specs.Length; i++)
            {
                MaterialSpec spec = Specs[i];
                string path = MaterialFolder + "/" + spec.Name + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;

                ConfigureMaterial(material, spec, settings, missing);
                EditorUtility.SetDirty(material);
                written++;

                // glTFast exposes its imported materials for remapping the same way
                // the FBX importer does, keyed by source name.
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.Name),
                    material);
            }

            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();
            AssetDatabase.Refresh();

            if (missing.Count > 0)
            {
                Debug.LogWarning("Ancient Spire materials: " + missing.Count +
                    " texture(s) were not found and were left unassigned:\n" + string.Join("\n", missing));
            }
            Debug.Log("Ancient Spire materials: wrote " + written + " material(s) to " + MaterialFolder +
                " and remapped " + ModelPath + ".");
        }

        private static void ConfigureMaterial(
            Material material,
            MaterialSpec spec,
            DuneVectorRuntimeSettings settings,
            List<string> missing)
        {
            Color tint;
            float metallic;
            float smoothness;
            Color emission = Color.black;

            switch (spec.Name)
            {
                case "AncientSpire_Stone":
                    tint = settings.SpireStoneTextureTint;
                    metallic = 0f;
                    smoothness = settings.LandmarkStoneSmoothness;
                    break;
                case "AncientSpire_StoneDark":
                    tint = settings.SpireStoneDarkTextureTint;
                    metallic = 0f;
                    smoothness = settings.LandmarkStoneSmoothness;
                    break;
                case "AncientSpire_Carved":
                    tint = settings.SpireCarvedTextureTint;
                    metallic = 0f;
                    smoothness = settings.LandmarkStoneSmoothness;
                    break;
                case "AncientSpire_Cloth":
                    tint = settings.SpireClothTextureTint;
                    metallic = 0f;
                    smoothness = settings.LandmarkStoneSmoothness;
                    break;
                case "AncientSpire_Bronze":
                    tint = settings.SpireMetalTextureTint;
                    metallic = settings.LandmarkMetallic;
                    smoothness = settings.LandmarkMetalSmoothness;
                    break;
                case "AncientSpire_Plate":
                    tint = settings.SpirePlateTextureTint;
                    metallic = settings.LandmarkMetallic;
                    smoothness = settings.LandmarkMetalSmoothness;
                    break;
                case "AncientSpire_Sand":
                    tint = settings.SpireSandTextureTint;
                    metallic = 0f;
                    smoothness = settings.LandmarkStoneSmoothness;
                    break;
                case "AncientSpire_Relic":
                    tint = settings.LandmarkAccentColor;
                    metallic = 0f;
                    smoothness = settings.SpireRelicSmoothness;
                    emission = settings.LandmarkAccentEmission;
                    break;
                case "AncientSpire_Accent":
                    tint = settings.LandmarkAccentColor;
                    metallic = 0f;
                    smoothness = settings.LandmarkMetalSmoothness;
                    emission = settings.LandmarkAccentEmission;
                    break;
                default:
                    tint = settings.LandmarkInteriorColor;
                    metallic = 0f;
                    smoothness = 0f;
                    break;
            }

            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            if (emission != Color.black)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", emission);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                material.SetColor("_EmissionColor", Color.black);
            }

            if (string.IsNullOrEmpty(spec.TextureSet))
            {
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_BumpMap", null);
                material.SetTexture("_OcclusionMap", null);
                material.DisableKeyword("_NORMALMAP");
                material.DisableKeyword("_OCCLUSIONMAP");
                return;
            }

            float tiling = TilingFor(spec.Tiling, settings);
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));

            Texture2D albedo = LoadMap(spec.TextureSet, "Color", missing);
            material.SetTexture("_BaseMap", albedo);
            // URP's inspector edits _BaseMap but the lit shader still samples
            // _MainTex on some paths; keeping both bound avoids a white model.
            material.SetTexture("_MainTex", albedo);
            material.SetTextureScale("_MainTex", new Vector2(tiling, tiling));

            Texture2D normal = LoadMap(spec.TextureSet, "NormalGL", missing);
            if (normal != null)
            {
                EnsureNormalMapImport(normal);
                material.SetTexture("_BumpMap", normal);
                material.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
                material.EnableKeyword("_NORMALMAP");
            }

            if (!spec.HasOcclusion)
            {
                material.SetTexture("_OcclusionMap", null);
                material.DisableKeyword("_OCCLUSIONMAP");
                return;
            }

            Texture2D occlusion = LoadMap(spec.TextureSet, "AmbientOcclusion", missing);
            if (occlusion != null)
            {
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetTextureScale("_OcclusionMap", new Vector2(tiling, tiling));
                material.SetFloat("_OcclusionStrength", 1f);
                material.EnableKeyword("_OCCLUSIONMAP");
            }
        }

        private static float TilingFor(Family family, DuneVectorRuntimeSettings settings)
        {
            switch (family)
            {
                case Family.Detail:
                    return settings.SpireDetailTextureTiling;
                case Family.Metal:
                    return settings.SpireMetalTextureTiling;
                case Family.Sand:
                    return settings.SpireSandTextureTiling;
                default:
                    return settings.SpireStoneTextureTiling;
            }
        }

        private static Texture2D LoadMap(string textureSet, string map, List<string> missing)
        {
            string path = TextureFolder + "/" + textureSet + "/" + textureSet + "_" + map + ".jpg";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                missing.Add(path);
            }
            return texture;
        }

        /// <summary>
        /// The ambientCG packs import as plain colour textures. A normal map read
        /// as colour data lights the spire inside out, so the importer is switched
        /// over the first time it is used.
        /// </summary>
        private static void EnsureNormalMapImport(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            {
                return;
            }

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        [MenuItem("Tools/Dune Vector/Verify Ancient Spire Materials")]
        public static void Verify()
        {
            AssetImporter importer = AssetImporter.GetAtPath(ModelPath);
            if (importer == null)
            {
                Debug.LogError("Ancient Spire materials: could not load the model at " + ModelPath);
                return;
            }

            long bytes = new FileInfo(ModelPath).Length;
            Dictionary<AssetImporter.SourceAssetIdentifier, Object> remaps = importer.GetExternalObjectMap();
            List<string> unmapped = new List<string>();
            for (int i = 0; i < Specs.Length; i++)
            {
                AssetImporter.SourceAssetIdentifier key =
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), Specs[i].Name);
                if (!remaps.TryGetValue(key, out Object mapped) || mapped == null)
                {
                    unmapped.Add(Specs[i].Name);
                }
            }

            if (unmapped.Count > 0)
            {
                Debug.LogWarning("Ancient Spire materials: " + unmapped.Count +
                    " material(s) are still using the imported defaults:\n" + string.Join("\n", unmapped));
                return;
            }

            Debug.Log("Ancient Spire materials: all " + Specs.Length + " materials remapped. Model is " +
                (bytes / 1048576f).ToString("0.0") + " MB.");
        }
    }
}
