using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace Mewlist
{
    public struct FlippingRenderTextures
    {
        readonly int firstId;
        readonly int secondId;
        readonly int scaledId;
        readonly int halfScaledId;
        private bool flipped;
        private readonly RenderTextureFormat format;
        private readonly RenderTextureFormat formatAlpha;

        public int From
        {
            get { return flipped ? firstId : secondId; }
        }

        public int To
        {
            get { return flipped ? secondId : firstId; }
        }

        public int ScaledId
        {
            get { return scaledId; }
        }

        public int HalfScaledId
        {
            get { return halfScaledId; }
        }

        public FlippingRenderTextures(
            Camera targetCamera,
            CommandBuffer commandBuffer,
            float resolution,
            float volumetricShadowResolution)
        {
            firstId = Shader.PropertyToID("_MassiveClouds");
            secondId = Shader.PropertyToID("_MassiveCloudsScreen");
            scaledId = Shader.PropertyToID("_MassiveCloudsScaled");
            halfScaledId = Shader.PropertyToID("_MassiveCloudsHalfScaled");
            flipped = false;
            formatAlpha = format = targetCamera.allowHDR
                ? RenderTextureFormat.DefaultHDR
                : RenderTextureFormat.Default;

            CreateRenderTextures(targetCamera, commandBuffer, resolution, volumetricShadowResolution);
        }

        public FlippingRenderTextures(
            Camera targetCamera,
            RenderTextureFormat format,
            RenderTextureFormat formatAlpha,
            CommandBuffer commandBuffer,
            float resolution,
            float volumetricShadowResolution)
        {
            firstId = Shader.PropertyToID(targetCamera.name + "_MassiveClouds");
            secondId = Shader.PropertyToID(targetCamera.name + "_MassiveCloudsScreen");
            scaledId = Shader.PropertyToID("_MassiveCloudsScaled");
            halfScaledId = Shader.PropertyToID("_MassiveCloudsHalfScaled");
            flipped = false;
            this.format = format;
            this.formatAlpha = formatAlpha;

            CreateRenderTextures(targetCamera, commandBuffer, resolution, volumetricShadowResolution);
        } 

        private void CreateRenderTextures(
            Camera targetCamera,
            CommandBuffer commandBuffer,
            float resolution,
            float volumetricShadowResolution)
        {
            if (XRSettings.enabled)
            {
                var w = XRSettings.eyeTextureDesc.width;
                var h = XRSettings.eyeTextureDesc.height;
                commandBuffer.GetTemporaryRT(firstId, w, h, 0, FilterMode.Point, formatAlpha);
                commandBuffer.GetTemporaryRT(secondId, w, h, 0, FilterMode.Point, formatAlpha);
            }
            else
            {
                commandBuffer.GetTemporaryRT(firstId, targetCamera.pixelWidth, targetCamera.pixelHeight, 0,
                    FilterMode.Point, formatAlpha);
                commandBuffer.GetTemporaryRT(secondId, targetCamera.pixelWidth, targetCamera.pixelHeight, 0,
                    FilterMode.Point, formatAlpha);
            }

            commandBuffer.GetTemporaryRT(scaledId,
                Mathf.RoundToInt(targetCamera.pixelWidth * resolution),
                Mathf.RoundToInt(targetCamera.pixelHeight * resolution),
                0, FilterMode.Trilinear, formatAlpha);
            if (volumetricShadowResolution > 0)
                commandBuffer.GetTemporaryRT(halfScaledId,
                    Mathf.RoundToInt(targetCamera.pixelWidth * resolution * volumetricShadowResolution),
                    Mathf.RoundToInt(targetCamera.pixelHeight * resolution * volumetricShadowResolution),
                    0, FilterMode.Trilinear, format);
        }

        public void Release(CommandBuffer commandBuffer)
        {
            commandBuffer.ReleaseTemporaryRT(firstId);
            commandBuffer.ReleaseTemporaryRT(secondId);
            commandBuffer.ReleaseTemporaryRT(scaledId);
            commandBuffer.ReleaseTemporaryRT(halfScaledId);
        }

        public void Flip()
        {
            flipped = !flipped;
        }
    }
}