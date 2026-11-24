#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class TeleporterFieldTests
{
    static readonly string[] Layout =
    {
        "#########",
        "#11.....#",
        "#11.....#",
        "#.......#",
        "#.....11#",
        "#.....11#",
        "#########"
    };

    // 쌍 두 개. 1번 쌍은 두 번째 색을 쓴다.
    static readonly string[] TwoPairs =
    {
        "#########",
        "#11...22#",
        "#11...22#",
        "#.......#",
        "#22...11#",
        "#22...11#",
        "#########"
    };

    GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
        root = null;
    }

    [Test]
    public void CentersAndFootprintsUseTheMapCellMapping()
    {
        root = new GameObject("Teleporter Field Root");
        root.transform.position = new Vector3(10f, 0f, -3f);
        var field = new TeleporterField(Layout, root.transform, 1f);

        Assert.That(field.Count, Is.EqualTo(1));
        // 9x7 맵의 칸 (x, y) 중심은 root 기준 (x - 4, 0, 3 - y)다. (1,1)~(2,2) 타일의 가운데는 (-2.5, 0, 1.5)다.
        Assert.That(field.CenterOf(0, 0), Is.EqualTo(root.transform.TransformPoint(new Vector3(-2.5f, 0f, 1.5f))));
        Assert.That(field.CenterOf(0, 1), Is.EqualTo(root.transform.TransformPoint(new Vector3(2.5f, 0f, -1.5f))));

        Vector3 center = field.CenterOf(0, 1);
        Assert.That(field.TryFind(center + new Vector3(0.2f, 1f, -0.1f), out int pair, out int side, out float distance), Is.True);
        Assert.That((pair, side), Is.EqualTo((0, 1)));
        Assert.That(distance, Is.EqualTo(new Vector2(0.2f, -0.1f).magnitude).Within(1e-4f));
        Assert.That(field.TryFind(center + new Vector3(0.99f, 0f, 0.99f), out _, out _, out _), Is.True);
        Assert.That(field.TryFind(center + new Vector3(1.01f, 0f, 0f), out _, out _, out _), Is.False);
        Assert.That(field.TryFind(root.transform.position, out _, out _, out _), Is.False);
    }

    [Test]
    public void ActiveFieldIsClearedOnlyByItsOwner()
    {
        var first = new TeleporterField(Layout, null, 1f);
        var second = new TeleporterField(Layout, null, 1f);
        TeleporterField.Activate(first);
        TeleporterField.Deactivate(second);
        Assert.That(TeleporterField.Active, Is.SameAs(first));
        TeleporterField.Deactivate(first);
        Assert.That(TeleporterField.Active, Is.Null);
    }

    [Test]
    public void GlowFollowsReadyFlashCooldownAndRecovery()
    {
        var settings = new TeleporterGlowSettings();
        float none = float.NegativeInfinity;

        TeleporterGlow.Evaluate(settings, 10f, none, none, out float level, out float ready);
        Assert.That(ready, Is.EqualTo(1f));
        Assert.That(level, Is.InRange(settings.readyIntensity * (1f - settings.pulse), settings.readyIntensity * (1f + settings.pulse)));

        // 발동한 순간 가장 밝고, flashDuration 뒤에는 쿨타임 빛으로 어두워진다.
        TeleporterGlow.Evaluate(settings, 10f, 10f, 15f, out level, out ready);
        Assert.That(level, Is.EqualTo(settings.flashIntensity).Within(1e-4f));
        Assert.That(level, Is.GreaterThan(1f), "The flash is bright enough to bloom.");
        TeleporterGlow.Evaluate(settings, 10f + settings.flashDuration, 10f, 15f, out level, out ready);
        Assert.That(level, Is.EqualTo(settings.cooldownIntensity));
        Assert.That(ready, Is.EqualTo(0f));
        Assert.That(TeleporterGlow.Tint(settings, ready), Is.EqualTo(settings.cooldownTint));

        // 쿨타임이 끝나면 recoverTime 동안 다시 밝아진다.
        TeleporterGlow.Evaluate(settings, 15f + settings.recoverTime * 0.5f, 10f, 15f, out level, out ready);
        Assert.That(ready, Is.EqualTo(0.5f).Within(1e-4f));
        TeleporterGlow.Evaluate(settings, 15f + settings.recoverTime + 0.01f, 10f, 15f, out level, out ready);
        Assert.That(ready, Is.EqualTo(1f));
    }

    [Test]
    public void TilePrefabLightsOnlyTheHighlightSlotsWithThePairColor()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<TeleporterBlock>("Assets/Map/Blocks/Teleporters/Teleporter.prefab");
        Assert.That(prefab, Is.Not.Null);
        root = new GameObject("Teleporter Field Root");
        var field = new TeleporterField(TwoPairs, root.transform, 1f);
        Assert.That(field.Count, Is.EqualTo(2));
        TeleporterBlock block = Object.Instantiate(prefab, root.transform);
        Assert.That(block.GetComponentInChildren<Collider>(), Is.Null, "The floor block under the tile keeps the collider.");
        Assert.That(block.HighlightSlotCount, Is.GreaterThan(0));
        var glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map/Blocks/Teleporters/Materials/Teleporter Glow.mat");
        Assert.That(glow.IsKeywordEnabled("_EMISSION"), Is.True);
        // GI 플래그에 발광이 없으면 에디터에서 렌더할 때 URP가 _EMISSION을 꺼 타일이 빛나지 않는다.
        Assert.That(glow.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive, Is.Not.EqualTo(MaterialGlobalIlluminationFlags.None));

        block.Bind(field, 1, 0);
        Assert.That(block.PairColor, Is.Not.EqualTo(new Color32(0x9d, 0x6b, 0xff, 0xff)), "Pair 1 uses the second pair color.");
        field.SetState(1, 0f, float.PositiveInfinity);
        block.Refresh(100f);
        Assert.That(block.Ready, Is.EqualTo(0f));
        Assert.That(block.Level, Is.EqualTo(block.Glow.cooldownIntensity));

        var properties = new MaterialPropertyBlock();
        int lit = 0;
        foreach (Renderer renderer in block.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                renderer.GetPropertyBlock(properties, i);
                bool highlight = materials[i].name == "Teleporter Glow";
                Assert.That(properties.isEmpty, Is.EqualTo(!highlight), $"{renderer.name} slot {i} ({materials[i].name})");
                if (!highlight) continue;
                lit++;
                Vector4 emission = properties.GetVector("_EmissionColor");
                Color linear = block.PairColor.linear * block.Glow.cooldownIntensity;
                Assert.That(emission.x, Is.EqualTo(linear.r).Within(1e-4f));
                Assert.That(emission.z, Is.EqualTo(linear.b).Within(1e-4f));
            }
        }
        Assert.That(lit, Is.EqualTo(block.HighlightSlotCount));
    }
}
#endif
