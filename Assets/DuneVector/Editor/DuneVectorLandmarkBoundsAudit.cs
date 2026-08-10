using System.Text;
using UnityEditor;
using UnityEngine;

namespace DuneVector.EditorTools
{
    /// <summary>
    /// Reports the measured world bounds of every prefab-backed landmark so authored tuning
    /// (photography frames, exclusion radii) can be checked against the real silhouettes.
    /// </summary>
    public static class DuneVectorLandmarkBoundsAudit
    {
        [MenuItem("Dune Vector/Audit Landmark Prefab Bounds")]
        public static void Audit()
        {
            string[] paths =
            {
                "ruinsPrefab",
                "DC-10/DC-10_Prefab",
                "desert_obelisk_Prefab",
                "DesertMegagatePrefab",
                "turbine/turbinePrefab",
                "desert_shop_Prefab",
                "RuinedRingsPrefab",
                "Nano Beacon from my 3D Graphic Novel/BeaconPrefab",
            };

            StringBuilder report = new StringBuilder();
            report.AppendLine("LANDMARK PREFAB BOUNDS AUDIT");
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject prefab = Resources.Load<GameObject>(paths[i]);
                if (prefab == null)
                {
                    report.AppendLine($"{paths[i]}: MISSING");
                    continue;
                }

                GameObject instance = Object.Instantiate(prefab);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = prefab.transform.localRotation;
                instance.transform.localScale = prefab.transform.localScale;

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                bool has = false;
                Bounds bounds = new Bounds();
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] == null || renderers[r] is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    if (!has)
                    {
                        bounds = renderers[r].bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[r].bounds);
                    }
                }

                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                if (has)
                {
                    float horizontalRadius = Mathf.Max(
                        Mathf.Max(Mathf.Abs(bounds.max.x), Mathf.Abs(bounds.min.x)),
                        Mathf.Max(Mathf.Abs(bounds.max.z), Mathf.Abs(bounds.min.z)));
                    report.AppendLine(
                        $"{paths[i]}: scale={prefab.transform.localScale} renderers={renderers.Length} " +
                        $"colliders={colliders.Length} center={bounds.center} size={bounds.size} " +
                        $"min={bounds.min} max={bounds.max} horizontalRadius={horizontalRadius:F2}");
                }
                else
                {
                    report.AppendLine($"{paths[i]}: NO RENDERERS (colliders={colliders.Length})");
                }

                Object.DestroyImmediate(instance);
            }

            Debug.Log(report.ToString());
            System.IO.File.WriteAllText("LandmarkBoundsAudit.txt", report.ToString());
        }
    }
}
