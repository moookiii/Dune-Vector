#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mewlist
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    public partial class MassiveClouds : MonoBehaviour
    {
        public enum ColorMode
        {
            Constant,
            FogColor,
            Ambient
        }

        public enum LightSourceMode
        {
            Auto,
            Manual,
        }

        public enum AmbientMode
        {
            Auto,
            Manual,
        }

        [SerializeField] private List<MassiveCloudsProfile> profiles;

        [Range(1 / 16.0f, 1f)] [SerializeField]
        private float resolution = 0.5f;

        [Range(1 / 16.0f, 1f)] [SerializeField]
        public float volumetricShadowResolution = 0.5f;

        [SerializeField] private bool lerp = false;
        [Range(0f, 5f)] [SerializeField] private float duration = 1f;

        [SerializeField] private bool save = false;
        [SerializeField] private ColorMode fogColorMode = ColorMode.Ambient;
        [SerializeField] private Color fogColor = new Color32(200, 200, 230, 255);
        [SerializeField] private float fogLuminanceFix = 0f;
        [SerializeField] private ColorMode shadowColorMode = ColorMode.Ambient;
        [SerializeField] private Color shadowColor = new Color32(200, 200, 230, 255);

        [SerializeField] private ColorMode cloudColorMode = ColorMode.Ambient;
#if UNITY_2018_1_OR_NEWER
        [ColorUsage(false, true), SerializeField]
        private Color cloudColor = new Color32(200, 200, 230, 255);
#else
        [ColorUsage(false, true, 0f, 8f, 0.125f, 3f), SerializeField]
        private Color cloudColor = new Color32(200, 200, 230, 255);
#endif
        [SerializeField] private float cloudLuminanceFix = 0f;

        [SerializeField] private LightSourceMode sunMode = LightSourceMode.Auto;
        [SerializeField] private Light sunReference = null;
        [SerializeField] private float sunIntensityScale = 1f;
        [SerializeField] private MassiveCloudsLight sun = new MassiveCloudsLight();

        [SerializeField] private LightSourceMode moonMode = LightSourceMode.Auto;
        [SerializeField] private Transform moonReference = null;
        [SerializeField] private MassiveCloudsLight moon = new MassiveCloudsLight();

        [SerializeField] private AmbientMode ambientMode = AmbientMode.Auto;
        [SerializeField] private MassiveCloudsAmbient ambient = new MassiveCloudsAmbient();

        [SerializeField] private List<MassiveCloudsParameter> parameters;
        private List<int> sortedIndex = new List<int>();

        private List<MassiveCloudsProfile> currentProfiles = new List<MassiveCloudsProfile>();
        private List<MassiveCloudsParameter> currentParameters = new List<MassiveCloudsParameter>();
        private CommandBuffer commandBuffer;

        private readonly List<MassiveCloudsMixer> mixers = new List<MassiveCloudsMixer>();

        public MassiveCloudsLight Sun
        {
            get { return sun; }
        }

        public MassiveCloudsLight Moon
        {
            get { return moon; }
        }

        public Light SunSource
        {
            get { return sunReference; }
            set { sunReference = value; }
        }

        public Color CloudColor
        {
            get
            {
                switch (cloudColorMode)
                {
                    case ColorMode.FogColor: return RenderSettings.fogColor;
                    case ColorMode.Ambient:
                    {
                        float h, s, v;
                        Color.RGBToHSV(ambient.SkyColor, out h, out s, out v);
                        var factor = Mathf.Pow(2, -cloudLuminanceFix);
                        return Color.white * v * 2 / factor;
                    }

                    case ColorMode.Constant:
                    default: return cloudColor;
                }
            }
        }

        public Color FogColor
        {
            get
            {
                switch (fogColorMode)
                {
                    case ColorMode.FogColor: return RenderSettings.fogColor;
                    case ColorMode.Ambient:
                    {
                        var factor = Mathf.Pow(2, -fogLuminanceFix);
                        return ambient.EquatorColor / factor;
                    }
                    case ColorMode.Constant:
                    default: return fogColor;
                }
            }
        }

        public Color ShadowColor
        {
            get
            {
                switch (shadowColorMode)
                {
                    case ColorMode.FogColor: return RenderSettings.fogColor / 2;
                    case ColorMode.Ambient: return ambient.EquatorColor / 2;
                    case ColorMode.Constant:
                    default: return shadowColor;
                }
            }
        }

        public void SetEnvironmentKey(EnvironmentKey key)
        {
            if (fogColorMode == ColorMode.Constant) fogColor = key.Fog;
            if (ambientMode == AmbientMode.Manual)
            {
                ambient.SkyColor = key.AmbientSky;
                ambient.EquatorColor = key.AmbientEquator;
                ambient.GroundColor = key.AmbientGround;
            };
            if (sunMode == LightSourceMode.Manual)
            {
                sun.Intensity = key.CloudSunIntensity;
                sun.Color = key.Light;
            }
            if (moonMode == LightSourceMode.Manual)
            {
                moon.Intensity = key.CloudMoonIntensity;
                moon.Color = key.Light;
            }
        }

        public List<MassiveCloudsProfile> Profiles
        {
            get { return profiles; }
        }

        public List<MassiveCloudsParameter> Parameters
        {
            get { return parameters; }
        }

        public Camera TargetCamera
        {
            get { return GetComponent<Camera>(); }
        }

        private bool Initialized
        {
            get { return mixers.Count == profiles.Count; }
        }

        public void SetOffset(Vector3 pos)
        {
            foreach (var massiveCloudsMixer in mixers)
                massiveCloudsMixer.Material.SetScrollOffset(pos);
        }

        public void SetProfiles(List<MassiveCloudsProfile> profiles)
        {
            this.profiles = profiles;
            Build();
        }

        public void SetParameters(List<MassiveCloudsParameter> parameters)
        {
            this.parameters = parameters;
        }

        public void Build()
        {
            if (profiles == null) return;
            // Mixers
            if (mixers.Count != profiles.Count)
            {
                var toRemove = Mathf.Max(0, mixers.Count - profiles.Count);
                var toAdd = Mathf.Max(0, profiles.Count - mixers.Count);
                for (var i = 0; i < toRemove; i++)
                {
                    mixers.RemoveAt(mixers.Count - 1);
                }

                for (var i = 0; i < toAdd; i++)
                {
                    var mixer = new MassiveCloudsMixer();
                    mixers.Add(mixer);
                }
            }

            // Parameters
            if (parameters.Count != profiles.Count)
            {
                parameters.Clear();
                foreach (var profile in profiles)
                    parameters.Add(profile == null ? default(MassiveCloudsParameter) : profile.Parameter);
            }

            if (currentParameters.Count != parameters.Count)
            {
                currentParameters.Clear();
                foreach (var _ in profiles)
                    currentParameters.Add(default(MassiveCloudsParameter));
            }

            // Sorted Index
            if (sortedIndex.Count != profiles.Count)
            {
                sortedIndex.Clear();
                sortedIndex.AddRange(Enumerable.Range(0, profiles.Count));
            }

            // Current Profiles
            if (currentProfiles.Count != profiles.Count)
            {
                currentProfiles.Clear();
                currentProfiles.AddRange(profiles);
            }

            // Parameters
            for (var i = 0; i < profiles.Count; i++)
            {
                if (currentProfiles[i] == profiles[i]) continue;
                currentProfiles[i] = profiles[i];
                if (currentProfiles[i] == null)
                    parameters[i] = default(MassiveCloudsParameter);
                else
                    parameters[i] = currentProfiles[i].Parameter;
            }

            // Update Mixers
            for (var i = 0; i < profiles.Count; i++)
            {
                mixers[i].ChangeTo(currentProfiles[i], lerp);
                if (currentParameters[i] != parameters[i])
                {
                    mixers[i].SetParameter(parameters[i]);
                    currentParameters[i] = parameters[i];
                }
            }
        }

        public void BuildCommandBuffer(CommandBuffer commandBuffer, RenderTargetIdentifier source,
            RenderTargetIdentifier destination)
        {
            if (!enabled) return;

            var hasVolumetricShadow = false;
            foreach (var massiveCloudsParameter in parameters)
                hasVolumetricShadow = hasVolumetricShadow || massiveCloudsParameter.VolumetricShadow;

            var renderTextures = new FlippingRenderTextures(TargetCamera, commandBuffer, resolution,
                hasVolumetricShadow ? volumetricShadowResolution : 0);
            commandBuffer.Blit(source, renderTextures.From);

            for (var i = 0; i < profiles.Count; i++)
            {
                var index = sortedIndex[i];
                if (profiles[index] == null) continue;
                var m = mixers[index];
                commandBuffer.Blit(renderTextures.From, renderTextures.To, m.Material.ShadowMaterial);
                renderTextures.Flip();
            }

            if (profiles.Any() && profiles[0] != null)
            {
                commandBuffer.Blit(renderTextures.From, renderTextures.To, mixers[0].Material.HeightFogMaterial);
                renderTextures.Flip();
            }

            for (var i = 0; i < profiles.Count; i++)
            {
                commandBuffer.SetGlobalTexture("_ScreenTexture", renderTextures.From);
                var index = sortedIndex[i];
                if (profiles[index] == null) continue;
                var m = mixers[index];
                var material = m.Material.CloudMaterial;
                commandBuffer.Blit(renderTextures.From, renderTextures.ScaledId, material);
                commandBuffer.Blit(renderTextures.ScaledId, renderTextures.To, m.Material.MixMaterial);

                renderTextures.Flip();
            }

            if (hasVolumetricShadow)
            {
                var tempId = Shader.PropertyToID("_MassiveClouds_Empty");
                commandBuffer.GetTemporaryRT(tempId, 8, 8,
                    0, FilterMode.Point, RenderTextureFormat.Default);
                var fullId = Shader.PropertyToID("_MassiveClouds_Full");
                commandBuffer.GetTemporaryRT(fullId, TargetCamera.pixelWidth, TargetCamera.pixelHeight, 0,
                    FilterMode.Point, RenderTextureFormat.Default);
                // volumetric shadow
                for (var i = 0; i < profiles.Count; i++)
                {
                    int index = sortedIndex[i];
                    if (profiles[index] == null) continue;
                    if (!parameters[index].VolumetricShadow) continue;
                    commandBuffer.SetGlobalTexture("_ScreenTexture", renderTextures.From);
                    commandBuffer.SetGlobalTexture("_CloudTexture", renderTextures.ScaledId);
                    var m = mixers[index];
                    var material = m.Material.VolumetricShadowMaterial;
                    commandBuffer.Blit(tempId, renderTextures.HalfScaledId, material);
                    commandBuffer.Blit(renderTextures.HalfScaledId, fullId, material);
                    commandBuffer.Blit(fullId, renderTextures.To, m.Material.VolumetricShadowMixMaterial);

                    renderTextures.Flip();
                }
            }

            commandBuffer.Blit(renderTextures.From, destination);

            renderTextures.Release(commandBuffer);
        }

        private class DistanceComparer : IComparer<int>
        {
            private readonly MassiveClouds massiveClouds;

            public DistanceComparer(MassiveClouds massiveClouds)
            {
                this.massiveClouds = massiveClouds;
            }

            public int Compare(int lhs, int rhs)
            {
                var profiles = massiveClouds.profiles;
                var parameters = massiveClouds.parameters;
                var l = profiles[lhs] == null ? float.MaxValue : Distance(parameters[lhs]);
                var r = profiles[rhs] == null ? float.MaxValue : Distance(parameters[rhs]);
                if (l == r) return 0;
                return l < r ? 1 : -1;
            }

            private float Distance(MassiveCloudsParameter parameter)
            {
                var cameraPos = massiveClouds.TargetCamera.transform.position;
                if (parameter.Horizontal)
                {
                    if (parameter.RelativeHeight)
                        return parameter.FromHeight;
                    else
                        return Mathf.Min(
                            Mathf.Abs(cameraPos.y - parameter.FromHeight),
                            Mathf.Abs(cameraPos.y - parameter.ToHeight));
                }
                else
                {
                    return parameter.FromDistance;
                }
            }
        }

        private DistanceComparer distanceComparer;

        private Color SafeColor(Color c)
        {
            float r, g, b;
            r = float.IsNaN(c.r) ? 0 : c.r;
            g = float.IsNaN(c.g) ? 0 : c.g;
            b = float.IsNaN(c.b) ? 0 : c.b;
            return new Color(r, g, b);
        }

        private static readonly Vector3[] probeDirections = new[] {Vector3.up, Vector3.back, Vector3.down};

        private void Update()
        {
            if (profiles == null) return;
            Build();

            if (distanceComparer == null) distanceComparer = new DistanceComparer(this);
            sortedIndex.Sort(distanceComparer);

            var colors = ambient.ToArray();

            if (ambientMode == AmbientMode.Auto)
            {
                RenderSettings.ambientProbe.Evaluate(probeDirections, colors);
                ambient.SkyColor = SafeColor(colors[0]);
                ambient.EquatorColor = SafeColor(colors[1]);
                ambient.GroundColor = SafeColor(colors[2]);
            }

            for (var i = 0; i < profiles.Count; i++)
            {
                var m = mixers[i];
                m.Material.SetFogColor(FogColor, FogColor);
                switch (shadowColorMode)
                {
                    case ColorMode.FogColor:
                        m.Material.SetShaodwColor(RenderSettings.fogColor / 2);
                        break;
                    case ColorMode.Ambient:
                        m.Material.SetShaodwColor(ambient.EquatorColor / 2);
                        break;
                    case ColorMode.Constant:
                    default:
                        m.Material.SetShaodwColor(shadowColor);
                        break;
                }

                m.Material.SetBaseColor(CloudColor);
                m.Update();
                m.SetDuration(duration);
                m.Material.CloudMaterial.SetColor("_AmbientTopColor", ambient.SkyColor);
                m.Material.CloudMaterial.SetColor("_AmbientMidColor", ambient.EquatorColor);
                m.Material.CloudMaterial.SetColor("_AmbientBottomColor", ambient.GroundColor);
                m.Material.VolumetricShadowMixMaterial.SetColor("_AmbientTopColor", ambient.SkyColor);
                m.Material.VolumetricShadowMixMaterial.SetColor("_AmbientMidColor", ambient.EquatorColor);
                m.Material.VolumetricShadowMixMaterial.SetColor("_AmbientBottomColor", ambient.GroundColor);

                if (sunMode == LightSourceMode.Manual)
                {
                    m.Material.SetDirectionalLight(sun, sunIntensityScale);
                }
                else
                {
                    if (sunReference == null)
                    {
                        foreach (var l in GameObject.FindObjectsOfType<Light>())
                        {
                            if (l.type != LightType.Directional) continue;
                            sunReference = l;
                            break;
                        }
                    }

                    if (sunReference != null)
                        sun.Synchronize(sunReference);
                    m.Material.SetDirectionalLight(sun, sunIntensityScale);
                }

                if (moonMode == LightSourceMode.Manual)
                {
                    m.Material.SetNightLight(moon, 1f);
                }
                else
                {
                    if (moonReference != null)
                        moon.Synchronize(moonReference);
                    m.Material.SetNightLight(moon, 1f);
                }
            }
        }

        private void OnValidate()
        {
            Build();

            if (save) SaveToProfile();
            save = false;
        }

        public void OnEnable()
        {
            currentParameters.Clear();
            currentProfiles.Clear();
        }

        public void SaveToProfile()
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                profiles[i].Parameter = parameters[i];
#if UNITY_EDITOR
                EditorUtility.SetDirty(profiles[i]);
#endif
            }
        }
    }
}