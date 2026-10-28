using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Only the LightOrbRefraction shader tag is drawn. Other transparent objects
// keep the standard URP path. Each layer reads a separate background snapshot.
public sealed class LightOrbRefractionFeature : ScriptableRendererFeature
{
    RefractionPass pass;
    public override void Create() => pass = new RefractionPass();
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (LightOrbSpin.ActiveCount == 0 || data.cameraData.cameraType == CameraType.Preview) return;
        renderer.EnqueuePass(pass);
    }
    sealed class RefractionPass : ScriptableRenderPass
    {
        static readonly int BackgroundId = Shader.PropertyToID("_OrbLayerBackground");
        static readonly ShaderTagId Tag = new ShaderTagId("LightOrbRefraction");
        class CopyData { public TextureHandle source; }
        class DrawData { public RendererListHandle list; }
        public RefractionPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources = frame.Get<UniversalResourceData>();
            var rendering = frame.Get<UniversalRenderingData>();
            var camera = frame.Get<UniversalCameraData>();
            var lights = frame.Get<UniversalLightData>();
            if (resources.isActiveTargetBackBuffer) return;
            var color = resources.activeColorTexture;
            var desc = graph.GetTextureDesc(color);
            desc.name = "PureLight layer background";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            for (int layer = 0; layer < 4; layer++)
            {
                desc.name = "PureLight background " + layer;
                var snapshot = graph.CreateTexture(desc);
                using (var builder = graph.AddRasterRenderPass<CopyData>("Orb copy layer " + layer, out var data))
                {
                    data.source = color;
                    builder.UseTexture(color, AccessFlags.Read);
                    builder.SetRenderAttachment(snapshot, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(snapshot, BackgroundId);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((CopyData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1,1,0,0), 0, false));
                }
                var drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.CommonTransparent);
                int queue = 3000 + layer * 10;
                var filtering = new FilteringSettings(new RenderQueueRange(queue,queue), camera.camera.cullingMask);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,filtering));
                using (var builder = graph.AddRasterRenderPass<DrawData>("Orb refract layer " + layer, out var data))
                {
                    data.list = list;
                    builder.UseRendererList(list);
                    builder.UseTexture(snapshot, AccessFlags.Read);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture,AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc((DrawData d, RasterGraphContext ctx) => ctx.cmd.DrawRendererList(d.list));
                }
            }
        }
    }
}
