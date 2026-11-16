#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 설정 화면의 그래픽 값(GraphicsOptions). 프로젝트 URP 에셋은 건드리지 않고 새로 만든 에셋에 적용해 본다.
public class GraphicsOptionsTests
{
    const string ProjectPipelinePath = "Assets/Settings/PC_RPAsset.asset";

    UniversalRenderPipelineAsset pipeline;

    [SetUp]
    public void SetUp() => pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(pipeline);

    [Test]
    public void DefaultsKeepTheLookFromBeforeTheOptionsExisted()
    {
        var options = new GraphicsOptions();
        var project = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ProjectPipelinePath);
        GraphicsOptions.QualityPreset high = GraphicsOptions.PresetOf(options.Quality);

        Assert.That(options.Quality, Is.EqualTo(GraphicsQuality.High));
        Assert.That(high.RenderScale, Is.EqualTo(project.renderScale));
        Assert.That(high.ShadowResolution, Is.EqualTo(project.mainLightShadowmapResolution));
        Assert.That(high.ShadowCascades, Is.EqualTo(project.shadowCascadeCount));
        Assert.That(options.Msaa, Is.EqualTo(project.msaaSampleCount));
        Assert.That(options.VSync, Is.False);
        Assert.That(options.FrameLimit, Is.EqualTo(60), "Clients were capped at 60 fps before the options existed.");
    }

    [Test]
    public void LowerQualityNeverCostsMore()
    {
        GraphicsOptions.QualityPreset low = GraphicsOptions.PresetOf(GraphicsQuality.Low);
        GraphicsOptions.QualityPreset medium = GraphicsOptions.PresetOf(GraphicsQuality.Medium);
        GraphicsOptions.QualityPreset high = GraphicsOptions.PresetOf(GraphicsQuality.High);

        Assert.That(low.RenderScale, Is.LessThanOrEqualTo(medium.RenderScale));
        Assert.That(medium.RenderScale, Is.LessThanOrEqualTo(high.RenderScale));
        Assert.That(low.ShadowResolution, Is.LessThanOrEqualTo(medium.ShadowResolution));
        Assert.That(medium.ShadowResolution, Is.LessThanOrEqualTo(high.ShadowResolution));
        Assert.That(low.ShadowCascades, Is.LessThanOrEqualTo(medium.ShadowCascades));
        Assert.That(medium.ShadowCascades, Is.LessThanOrEqualTo(high.ShadowCascades));
        Assert.That(low.AmbientOcclusion, Is.False, "Low turns SSAO off.");
    }

    [TestCase(GraphicsQuality.Low, 2)]
    [TestCase(GraphicsQuality.Medium, 4)]
    [TestCase(GraphicsQuality.High, 1)]
    public void ApplyWritesQualityAndAntiAliasingToThePipeline(GraphicsQuality quality, int msaa)
    {
        GraphicsOptions.ApplyTo(pipeline, new GraphicsOptions { Quality = quality, Msaa = msaa });

        GraphicsOptions.QualityPreset preset = GraphicsOptions.PresetOf(quality);
        Assert.That(pipeline.renderScale, Is.EqualTo(preset.RenderScale));
        Assert.That(pipeline.mainLightShadowmapResolution, Is.EqualTo(preset.ShadowResolution));
        Assert.That(pipeline.shadowCascadeCount, Is.EqualTo(preset.ShadowCascades));
        Assert.That(pipeline.msaaSampleCount, Is.EqualTo(msaa));
    }

    [Test]
    public void ValuesThatNoButtonShowsFallBackToDefaults()
    {
        var defaults = new GraphicsOptions();
        GraphicsOptions options = new GraphicsOptions { Quality = (GraphicsQuality)7, Msaa = 3, FrameLimit = 75, VSync = true }.Validated();

        Assert.That(options.Quality, Is.EqualTo(defaults.Quality));
        Assert.That(options.Msaa, Is.EqualTo(defaults.Msaa));
        Assert.That(options.FrameLimit, Is.EqualTo(defaults.FrameLimit));
        Assert.That(options.VSync, Is.True, "Valid values stay.");
    }

    [Test]
    public void ScreenSizesMergeRefreshRatesAndSortAscending()
    {
        var modes = new[]
        {
            Mode(1920, 1080, 144), Mode(1280, 720, 60), Mode(1920, 1080, 60), Mode(2560, 1440, 60), Mode(1600, 900, 60)
        };

        List<Vector2Int> sizes = GraphicsOptions.ScreenSizes(modes, new Vector2Int(1920, 1080));

        Assert.That(sizes, Is.EqualTo(new[]
        {
            new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440)
        }));
    }

    [Test]
    public void ScreenSizesDropTinyModesButKeepTheCurrentWindow()
    {
        var modes = new[] { Mode(800, 600, 60), Mode(1024, 768, 60), Mode(1920, 1080, 60) };

        List<Vector2Int> sizes = GraphicsOptions.ScreenSizes(modes, new Vector2Int(1500, 640));

        Assert.That(sizes, Is.EqualTo(new[] { new Vector2Int(1024, 768), new Vector2Int(1500, 640), new Vector2Int(1920, 1080) }));
    }

    static Resolution Mode(int width, int height, int hz) => new Resolution
    {
        width = width,
        height = height,
        refreshRateRatio = new RefreshRate { numerator = (uint)hz, denominator = 1 }
    };
}
#endif
