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
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        private WorldGlitchPass _pass;
        private bool _registered;

        public override void Create()
        {
            if (!_registered)
            {
                DuneVectorMusicGlitchRuntime.RegisterFeature();
                _registered = true;
            }
            _pass = new WorldGlitchPass
            {
                renderPassEvent = injectionPoint,
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (_registered)
            {
                DuneVectorMusicGlitchRuntime.UnregisterFeature();
                _registered = false;
            }
            base.Dispose(disposing);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null
                || !DuneVectorMusicGlitchRuntime.IsActive
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

            public WorldGlitchPass()
            {
                requiresIntermediateTexture = true;
            }

            public void Setup(Material passMaterial)
            {
                _material = passMaterial;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || !DuneVectorMusicGlitchRuntime.IsActive)
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
