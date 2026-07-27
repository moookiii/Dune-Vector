using Mewlist;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

[System.Serializable]
class MassiveCloudsPass : CustomPass
{
    public MassiveClouds MassiveClouds;

    private Material fullscreenPassMaterial;

    protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
    {
        var shader = Shader.Find("Hidden/FullScreen/MassiveCloudsCustomPass");
        if (shader != null)
            fullscreenPassMaterial = CoreUtils.CreateEngineMaterial(shader);
    }

    protected override void Execute(CustomPassContext ctx)
    {
        if (MassiveClouds == null || fullscreenPassMaterial == null)
            return;

        ResolveMSAAColorBuffer(ctx);

        var format = RenderTextureFormat.ARGB64;
        var formatAlpha = RenderTextureFormat.DefaultHDR;

        MassiveClouds.BuildCommandBufferHDRP(ctx.cmd, ctx.hdCamera, ctx.cameraColorBuffer,
            format, formatAlpha, fullscreenPassMaterial);
    }

    protected override void Cleanup()
    {
        CoreUtils.Destroy(fullscreenPassMaterial);
        fullscreenPassMaterial = null;
    }
}
