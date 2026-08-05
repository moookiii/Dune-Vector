using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace DuneVector
{
    public sealed class DuneVectorMusicWorldGlitchFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material material;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        private WorldGlitchPass _pass;

        public override void Create()
        {
            DuneVectorMusicGlitchRuntime.FeatureAvailable = true;
            _pass = new WorldGlitchPass
            {
                renderPassEvent = injectionPoint,
            };
        }

        protected override void Dispose(bool disposing)
        {
            DuneVectorMusicGlitchRuntime.FeatureAvailable = false;
            base.Dispose(disposing);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null
                || DuneVectorMusicGlitchRuntime.Intensity <= 0f
                || renderingData.cameraData.cameraType != CameraType.Game
                || renderingData.cameraData.renderType != CameraRenderType.Base)
            {
                return;
            }
            _pass.Setup(material);
            renderer.EnqueuePass(_pass);
        }

        private sealed class WorldGlitchPass : ScriptableRenderPass
        {
            private static readonly ProfilerMarker RecordMarker = new ProfilerMarker("MusicVisualizer.URPGlitchRecord");
            private Material _material;

            public void Setup(Material passMaterial)
            {
                _material = passMaterial;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || DuneVectorMusicGlitchRuntime.Intensity <= 0f)
                {
                    return;
                }

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                {
                    return;
                }

                using (RecordMarker.Auto())
                {
                    TextureHandle source = resources.activeColorTexture;
                    TextureDesc destinationDescriptor = renderGraph.GetTextureDesc(source);
                    destinationDescriptor.name = "DuneVector Music World Glitch Color";
                    destinationDescriptor.clearBuffer = false;
                    TextureHandle destination = renderGraph.CreateTexture(destinationDescriptor);
                    RenderGraphUtils.BlitMaterialParameters parameters = new RenderGraphUtils.BlitMaterialParameters(
                        source,
                        destination,
                        _material,
                        0);
                    renderGraph.AddBlitPass(parameters, "MusicVisualizer.WorldGlitch");
                    resources.cameraColor = destination;
                }
            }
        }
    }
}
