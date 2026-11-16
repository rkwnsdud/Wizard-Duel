using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 부시 안에서 반투명하게 그리는 캐릭터(WizardDuel/CharacterFaded)의 깊이를 반투명 패스 전에 한꺼번에 쓴다(이슈 #93).
// 캐릭터와 무기의 모든 파츠 깊이가 먼저 들어가야, 반투명 패스가 픽셀마다 가장 앞의 면만 한 번 섞어 속이 비치지 않는다.
// 셰이더의 CharacterFadeDepth 패스만 그리므로 다른 오브젝트에는 영향이 없다. 반투명 캐릭터가 없으면 패스를 넣지 않는다.
// PC_Renderer에 붙어 있다. 렌더러 에셋을 새로 만들면 이 기능도 다시 붙인다(PlayerBushVisibilityTests가 확인한다).
public sealed class CharacterFadeFeature : ScriptableRendererFeature
{
    public const string DepthPassTag = "CharacterFadeDepth";

    DepthPass pass;

    public override void Create() => pass = new DepthPass();

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (PlayerBushVisibility.FadedCount == 0 || renderingData.cameraData.cameraType == CameraType.Preview) return;
        renderer.EnqueuePass(pass);
    }

    sealed class DepthPass : ScriptableRenderPass
    {
        static readonly ShaderTagId Tag = new ShaderTagId(DepthPassTag);

        class PassData
        {
            public RendererListHandle list;
        }

        public DepthPass() => renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources = frame.Get<UniversalResourceData>();
            var rendering = frame.Get<UniversalRenderingData>();
            var camera = frame.Get<UniversalCameraData>();
            var lights = frame.Get<UniversalLightData>();
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(Tag, rendering, camera, lights, SortingCriteria.CommonOpaque);
            var filtering = new FilteringSettings(RenderQueueRange.transparent, camera.camera.cullingMask);
            RendererListHandle list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));

            using (var builder = graph.AddRasterRenderPass<PassData>("Character Fade Depth", out PassData data))
            {
                data.list = list;
                builder.UseRendererList(list);
                // 불투명 물체의 깊이와 비교해야 벽 뒤에 선 캐릭터가 벽을 뚫고 보이지 않는다.
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.list));
            }
        }
    }
}
