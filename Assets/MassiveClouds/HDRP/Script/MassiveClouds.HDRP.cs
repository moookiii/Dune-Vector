using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Mewlist
{
    public partial class MassiveClouds
    {
        private Material blitMat;

        public void BuildCommandBufferHDRP(
            CommandBuffer commandBuffer,
            HDCamera camera,
            RenderTargetIdentifier destination,
            RenderTextureFormat format,
            RenderTextureFormat formatAlpha,
            Material blitMaterial)
        {
            if (!enabled) return;

            if (blitMat == null)
            {
                blitMat = new Material(Shader.Find("MassiveCloudsBlit"));
            }

            var hasVolumetricShadow = false;
            foreach (var massiveCloudsParameter in parameters)
                hasVolumetricShadow = hasVolumetricShadow || massiveCloudsParameter.VolumetricShadow;

            var renderTextures = new FlippingRenderTextures(camera.camera,
                format,
                formatAlpha,
                commandBuffer, resolution,
                hasVolumetricShadow ? volumetricShadowResolution : 0);
            commandBuffer.SetRenderTarget(renderTextures.From);
            commandBuffer.DrawProcedural(Matrix4x4.identity, blitMaterial, 0, MeshTopology.Triangles, 3, 1, null);

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

            commandBuffer.SetRenderTarget(destination);
            commandBuffer.SetGlobalTexture("_ResultTexture", renderTextures.From);
            commandBuffer.DrawProcedural(Matrix4x4.identity, blitMat, 0, MeshTopology.Triangles, 3, 1, null);
            renderTextures.Release(commandBuffer);
        }
    }
}