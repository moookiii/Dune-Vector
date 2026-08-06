using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Mewlist
{
    /// <summary>
    /// Runs the legacy Massive Clouds command-buffer renderer at the correct point in URP.
    /// The integration shipped with Massive Clouds 4.1.1 targeted URP 7 and cannot be
    /// imported directly into current Unity versions.
    /// </summary>
    [DisallowMultipleRendererFeature("Massive Clouds")]
    public sealed class MassiveCloudsUniversalRendererFeature : ScriptableRendererFeature
    {
        private MassiveCloudsUniversalRenderPass renderPass;

        public override void Create()
        {
            renderPass ??= new MassiveCloudsUniversalRenderPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!renderPass.Prepare(renderingData.cameraData.camera))
                return;

#if !UNITY_6000_0_OR_NEWER
            renderPass.SetCompatibilityTarget(renderer.cameraColorTargetHandle);
#endif
            renderer.EnqueuePass(renderPass);
        }

        private sealed class MassiveCloudsUniversalRenderPass : ScriptableRenderPass
        {
            private const string PassName = "Massive Clouds";

            private MassiveClouds massiveClouds;
#if !UNITY_6000_0_OR_NEWER
            private RTHandle compatibilityTarget;
#endif

            public MassiveCloudsUniversalRenderPass()
            {
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public bool Prepare(Camera camera)
            {
                massiveClouds = camera != null ? camera.GetComponent<MassiveClouds>() : null;

                // Scene view cameras do not carry the demo component, so use the scene's
                // main camera configuration just as the vendor's original URP pass did.
                if (massiveClouds == null && camera != null && camera.cameraType == CameraType.SceneView)
                {
                    Camera mainCamera = Camera.main;
                    massiveClouds = mainCamera != null ? mainCamera.GetComponent<MassiveClouds>() : null;
                }

                return massiveClouds != null && massiveClouds.isActiveAndEnabled;
            }

#if !UNITY_6000_0_OR_NEWER
            public void SetCompatibilityTarget(RTHandle target)
            {
                compatibilityTarget = target;
            }
#endif

#if UNITY_6000_0_OR_NEWER
            private sealed class PassData
            {
                internal MassiveClouds MassiveClouds;
                internal TextureHandle Color;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (massiveClouds == null || !massiveClouds.isActiveAndEnabled)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                using (IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass<PassData>(PassName, out PassData passData))
                {
                    passData.MassiveClouds = massiveClouds;
                    passData.Color = resourceData.activeColorTexture;

                    builder.UseTexture(passData.Color, AccessFlags.ReadWrite);
                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        data.MassiveClouds.BuildCommandBuffer(commandBuffer, data.Color, data.Color);
                    });
                }
            }
#endif

#if !UNITY_6000_0_OR_NEWER
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (massiveClouds == null || !massiveClouds.isActiveAndEnabled || compatibilityTarget == null)
                    return;

                CommandBuffer commandBuffer = CommandBufferPool.Get(PassName);
                massiveClouds.BuildCommandBuffer(commandBuffer, compatibilityTarget, compatibilityTarget);
                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }
#endif
        }
    }
}
