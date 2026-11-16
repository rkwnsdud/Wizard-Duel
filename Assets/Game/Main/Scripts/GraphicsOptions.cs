using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum GraphicsQuality { Low, Medium, High }

// 설정 화면의 그래픽 값. 바꾸면 바로 적용하고 PlayerPrefs에 저장해 다음에 켤 때도 그대로 쓴다.
// 화면 모드와 해상도는 Unity 플레이어가 스스로 기억하므로 여기 두지 않고 GraphicsOptionsPanel이 Screen에서 바로 읽고 바꾼다.
// 품질 단계는 URP 에셋의 렌더 스케일, 그림자 해상도·단계, SSAO를 바꾼다. 그림자 거리는 TitleBackdrop이 장면마다 정하므로 건드리지 않는다.
public class GraphicsOptions
{
    public const int Unlimited = 0;
    // 최대 프레임 버튼 순서. Unlimited는 제한하지 않는다.
    public static readonly int[] FrameLimits = { 60, 120, 144, Unlimited };
    // 안티에일리어싱 버튼 순서. MSAA 샘플 수이고 1이면 끈다.
    public static readonly int[] MsaaSamples = { 1, 2, 4 };
    // 해상도 목록에서 뺄 만큼 작은 화면. UI 기준 해상도(1280x720)보다 낮으면 글자가 너무 작아진다.
    public const int MinScreenHeight = 720;

    const string QualityKey = "Graphics.Quality";
    const string MsaaKey = "Graphics.Msaa";
    const string VSyncKey = "Graphics.VSync";
    const string FrameLimitKey = "Graphics.FrameLimit";

    // 기본값은 이 설정을 넣기 전 모습과 같다. 높음은 URP 에셋 값 그대로이고, 클라이언트는 60프레임으로 묶여 있었다.
    public GraphicsQuality Quality = GraphicsQuality.High;
    public int Msaa = 1;
    public bool VSync;
    public int FrameLimit = 60;

    public readonly struct QualityPreset
    {
        public readonly float RenderScale;
        public readonly int ShadowResolution;
        public readonly int ShadowCascades;
        public readonly bool AmbientOcclusion;

        public QualityPreset(float renderScale, int shadowResolution, int shadowCascades, bool ambientOcclusion)
        {
            RenderScale = renderScale;
            ShadowResolution = shadowResolution;
            ShadowCascades = shadowCascades;
            AmbientOcclusion = ambientOcclusion;
        }
    }

    // 높음은 Assets/Settings/PC_RPAsset 값과 같다(GraphicsOptionsTests가 확인한다).
    public static QualityPreset PresetOf(GraphicsQuality quality) => quality switch
    {
        GraphicsQuality.Low => new QualityPreset(0.75f, 1024, 1, false),
        GraphicsQuality.Medium => new QualityPreset(1f, 1024, 2, true),
        _ => new QualityPreset(1f, 2048, 4, true)
    };

    static GraphicsOptions current;

    // 지금 쓰는 값의 복사본. 처음 부를 때 저장된 값을 읽는다.
    public static GraphicsOptions Current => (current ??= Load()).Clone();

    // 지금 적용된 품질이 SSAO를 쓰는지. TitleBackdrop이 화면을 섞는 동안 껐던 SSAO를 다시 켤 때 따른다.
    // 적용한 적이 없으면(서버, batchmode) 에셋 그대로 켠다.
    public static bool AmbientOcclusion { get; private set; } = true;

    public GraphicsOptions Clone() => (GraphicsOptions)MemberwiseClone();

    // 버튼에 없는 값(예전 버전이 저장했거나 손으로 고친 값)은 기본값으로 돌린다.
    public GraphicsOptions Validated()
    {
        var defaults = new GraphicsOptions();
        GraphicsOptions options = Clone();
        if (!Enum.IsDefined(typeof(GraphicsQuality), options.Quality)) options.Quality = defaults.Quality;
        if (Array.IndexOf(MsaaSamples, options.Msaa) < 0) options.Msaa = defaults.Msaa;
        if (Array.IndexOf(FrameLimits, options.FrameLimit) < 0) options.FrameLimit = defaults.FrameLimit;
        return options;
    }

    // 설정 화면에서 값을 바꿨다. 저장하고 바로 적용한다.
    public static void Change(GraphicsOptions options)
    {
        current = options.Validated();
        Save(current);
        Apply(current);
    }

    static GraphicsOptions Load()
    {
        var defaults = new GraphicsOptions();
        return new GraphicsOptions
        {
            Quality = (GraphicsQuality)PlayerPrefs.GetInt(QualityKey, (int)defaults.Quality),
            Msaa = PlayerPrefs.GetInt(MsaaKey, defaults.Msaa),
            VSync = PlayerPrefs.GetInt(VSyncKey, defaults.VSync ? 1 : 0) == 1,
            FrameLimit = PlayerPrefs.GetInt(FrameLimitKey, defaults.FrameLimit)
        }.Validated();
    }

    static void Save(GraphicsOptions options)
    {
        PlayerPrefs.SetInt(QualityKey, (int)options.Quality);
        PlayerPrefs.SetInt(MsaaKey, options.Msaa);
        PlayerPrefs.SetInt(VSyncKey, options.VSync ? 1 : 0);
        PlayerPrefs.SetInt(FrameLimitKey, options.FrameLimit);
        PlayerPrefs.Save();
    }

    static void Apply(GraphicsOptions options)
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline) ApplyTo(pipeline, options);
        AmbientOcclusion = PresetOf(options.Quality).AmbientOcclusion;
        // 수직 동기화를 켜면 모니터 주사율에 맞춰 그리고 targetFrameRate는 쓰이지 않는다.
        QualitySettings.vSyncCount = options.VSync ? 1 : 0;
        Application.targetFrameRate = options.FrameLimit == Unlimited ? -1 : options.FrameLimit;
    }

    // 품질 단계와 안티에일리어싱을 URP 에셋에 넣는다.
    public static void ApplyTo(UniversalRenderPipelineAsset pipeline, GraphicsOptions options)
    {
        QualityPreset preset = PresetOf(options.Quality);
        pipeline.renderScale = preset.RenderScale;
        pipeline.mainLightShadowmapResolution = preset.ShadowResolution;
        pipeline.shadowCascadeCount = preset.ShadowCascades;
        pipeline.msaaSampleCount = options.Msaa;
        foreach (ScriptableRendererData data in pipeline.rendererDataList)
        {
            if (data == null) continue;
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                if (feature is ScreenSpaceAmbientOcclusion) feature.SetActive(preset.AmbientOcclusion);
        }
    }

    // 고를 수 있는 해상도. 주사율만 다른 모드는 하나로 치고 작은 것부터 늘어놓는다.
    // 너무 작은 해상도는 빼되, 지금 크기는 목록에 없어도(창을 끌어 바꾼 경우) 넣어서 지금 값을 보여 줄 수 있게 한다.
    public static List<Vector2Int> ScreenSizes(IEnumerable<Resolution> modes, Vector2Int current)
    {
        var sizes = new List<Vector2Int>();
        foreach (Resolution mode in modes)
        {
            var size = new Vector2Int(mode.width, mode.height);
            if (size.y >= MinScreenHeight && !sizes.Contains(size)) sizes.Add(size);
        }
        if (!sizes.Contains(current)) sizes.Add(current);
        sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return sizes;
    }

    // 게임을 켤 때 저장된 값을 적용한다. 화면 없이 도는 서버는 그래픽을 건드리지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySaved()
    {
        current = null;
        AmbientOcclusion = true;
        if (Headless) return;
#if UNITY_EDITOR
        RememberEditorValues();
#endif
        Apply(Current);
    }

    static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
        NetworkDuelConfig.IsDedicatedServerLaunch;

#if UNITY_EDITOR
    // 에디터에서 URP 에셋과 품질 설정은 프로젝트 파일이다. 플레이 중에 바꾼 값이 남지 않게, 플레이가 끝나면 처음 값으로 되돌린다.
    // TitleBackdrop이 꺼지며 SSAO를 다시 켜는 것보다 뒤(편집 모드에 돌아온 뒤)에 되돌린다.
    sealed class EditorValues
    {
        public UniversalRenderPipelineAsset Pipeline;
        public float RenderScale;
        public int ShadowResolution;
        public int ShadowCascades;
        public int Msaa;
        public readonly List<KeyValuePair<ScriptableRendererFeature, bool>> Occlusion =
            new List<KeyValuePair<ScriptableRendererFeature, bool>>();
        public int VSyncCount;
        public int FrameRate;
    }

    static EditorValues editorValues;

    static void RememberEditorValues()
    {
        var values = new EditorValues
        {
            Pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset,
            VSyncCount = QualitySettings.vSyncCount,
            FrameRate = Application.targetFrameRate
        };
        if (values.Pipeline != null)
        {
            values.RenderScale = values.Pipeline.renderScale;
            values.ShadowResolution = values.Pipeline.mainLightShadowmapResolution;
            values.ShadowCascades = values.Pipeline.shadowCascadeCount;
            values.Msaa = values.Pipeline.msaaSampleCount;
            foreach (ScriptableRendererData data in values.Pipeline.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                    if (feature is ScreenSpaceAmbientOcclusion)
                        values.Occlusion.Add(new KeyValuePair<ScriptableRendererFeature, bool>(feature, feature.isActive));
            }
        }
        editorValues = values;
        EditorApplication.playModeStateChanged -= RestoreEditorValues;
        EditorApplication.playModeStateChanged += RestoreEditorValues;
    }

    static void RestoreEditorValues(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= RestoreEditorValues;
        EditorValues values = editorValues;
        editorValues = null;
        AmbientOcclusion = true;
        if (values == null) return;
        QualitySettings.vSyncCount = values.VSyncCount;
        Application.targetFrameRate = values.FrameRate;
        if (values.Pipeline == null) return;
        values.Pipeline.renderScale = values.RenderScale;
        values.Pipeline.mainLightShadowmapResolution = values.ShadowResolution;
        values.Pipeline.shadowCascadeCount = values.ShadowCascades;
        values.Pipeline.msaaSampleCount = values.Msaa;
        foreach (KeyValuePair<ScriptableRendererFeature, bool> entry in values.Occlusion)
            if (entry.Key != null) entry.Key.SetActive(entry.Value);
    }
#endif
}
