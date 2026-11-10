#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// PlayerBushVisibility가 규칙대로 렌더러, 머티리얼, 체력바, 부시를 바꾸는지 내 화면 기준으로 검사한다.
// 부시 안에서 보이는 캐릭터는 불투명 머티리얼을 반투명본(WizardDuel/CharacterFaded)으로 바꿔 한 덩어리로 CharacterAlpha만큼 비친다.
// EditMode라 LateUpdate가 돌지 않으므로 내 캐릭터 인스턴스의 UpdateVisibility를 직접 부른다.
public class PlayerBushVisibilityTests
{
    // 덩어리 0은 왼쪽 위 2×2, 덩어리 1은 오른쪽 세로 2칸이다. (3, 1)과 (4, 3)은 바닥이다.
    static readonly string[] Layout =
    {
        "#######",
        "#**...#",
        "#**..*#",
        "#....*#",
        "#######"
    };

    readonly List<Object> created = new List<Object>();
    readonly List<PlayerBushVisibility> players = new List<PlayerBushVisibility>();
    Transform root;
    BushField field;
    Shader fadedShader;
    Material body;
    Material bodyFaded;
    Material hit;
    Material hitFaded;
    Material bushFaded;
    int fadedBefore;
    BushBlock bushA;
    BushBlock bushB;

    [SetUp]
    public void SetUp()
    {
        root = NewObject("Map").transform;
        root.position = new Vector3(5f, 0f, -2f);
        field = new BushField(Layout, root, 1f);
        fadedShader = AssetDatabase.LoadAssetAtPath<Shader>(PlayerFadedMaterialsBuilder.ShaderPath);
        body = NewMaterial("Body");
        bodyFaded = Track(PlayerBushVisibility.CreateFadedCopy(body, fadedShader));
        hit = NewMaterial("Hit");
        hitFaded = Track(PlayerBushVisibility.CreateFadedCopy(hit, fadedShader));
        fadedBefore = PlayerBushVisibility.FadedCount;
        bushFaded = NewMaterial("Bush Faded");
        bushA = NewBush(1, 1);
        NewBush(2, 2);
        bushB = NewBush(5, 2);
    }

    [TearDown]
    public void TearDown()
    {
        // EditMode에서는 OnDisable이 돌지 않으므로, 반투명 플레이어 수(FadedCount)가 다음 테스트로 넘어가지 않게 직접 되돌린다.
        if (players.Count > 0) players[0].UpdateVisibility(null, players);
        players.Clear();
        foreach (Object item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [Test]
    public void HidesAnOpponentInABushFromOutside()
    {
        PlayerBushVisibility me = NewPlayer("Me", 3, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);

        Refresh(me, opponent);

        Assert.That(opponent.Look, Is.EqualTo(BushLook.Hidden));
        Assert.That(Body(opponent).forceRenderingOff, Is.True);
        Assert.That(Bar(opponent).enabled, Is.False);
        Assert.That(me.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(Body(me).sharedMaterial, Is.SameAs(body));
        Assert.That(bushB.Faded, Is.False, "The opponent's bush stays opaque.");
    }

    [Test]
    public void InsideABushIAmFadedAndSeeThroughMyBush()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 3, 1);

        Refresh(me, opponent);

        Assert.That(me.Look, Is.EqualTo(BushLook.InBush));
        Assert.That(Body(me).sharedMaterial, Is.SameAs(bodyFaded));
        Assert.That(PlayerBushVisibility.FadedCount, Is.EqualTo(fadedBefore + 1), "The depth pass runs while someone is faded.");
        Assert.That(Body(me).forceRenderingOff, Is.False);
        Assert.That(Bar(me).enabled, Is.True);
        Assert.That(bushA.Faded, Is.True);
        Assert.That(bushB.Faded, Is.False);
        Assert.That(opponent.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(Body(opponent).sharedMaterial, Is.SameAs(body));
    }

    [Test]
    public void PlayersInTheSameBushSeeEachOther()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 2, 2);

        Refresh(me, opponent);

        Assert.That(opponent.Look, Is.EqualTo(BushLook.InBush));
        Assert.That(Body(opponent).forceRenderingOff, Is.False);
        Assert.That(Body(opponent).sharedMaterial, Is.SameAs(bodyFaded));
        Assert.That(Body(me).sharedMaterial, Is.SameAs(bodyFaded));
        Assert.That(PlayerBushVisibility.FadedCount, Is.EqualTo(fadedBefore + 2));
        Assert.That(Bar(opponent).enabled, Is.True);
        Assert.That(bushA.Faded, Is.True);
    }

    [Test]
    public void PlayersInDifferentBushesStayHidden()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 3);

        Refresh(me, opponent);

        Assert.That(opponent.Look, Is.EqualTo(BushLook.Hidden));
        Assert.That(bushA.Faded, Is.True);
        Assert.That(bushB.Faded, Is.False);
    }

    [Test]
    public void AnAttackRevealsTheOpponentAndTheirBush()
    {
        PlayerBushVisibility me = NewPlayer("Me", 3, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);
        Refresh(me, opponent);

        opponent.Reveal();
        Refresh(me, opponent);

        Assert.That(opponent.IsRevealed, Is.True);
        Assert.That(opponent.Look, Is.EqualTo(BushLook.InBush));
        Assert.That(Body(opponent).forceRenderingOff, Is.False);
        Assert.That(Body(opponent).sharedMaterial, Is.SameAs(bodyFaded));
        Assert.That(Bar(opponent).enabled, Is.True);
        Assert.That(bushB.Faded, Is.True);
    }

    [Test]
    public void LeavingTheBushRestoresEverythingAtOnce()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);
        Refresh(me, opponent);

        Move(me, 3, 1);
        Move(opponent, 4, 3);
        Refresh(me, opponent);

        Assert.That(me.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(Body(me).sharedMaterial, Is.SameAs(body));
        Assert.That(opponent.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(Body(opponent).forceRenderingOff, Is.False);
        Assert.That(Bar(opponent).enabled, Is.True);
        Assert.That(bushA.Faded, Is.False);
        Assert.That(bushB.Faded, Is.False);
        Assert.That(PlayerBushVisibility.FadedCount, Is.EqualTo(fadedBefore));
    }

    [Test]
    public void HitFlashStaysFadedOnMeAndHiddenOnTheOpponent()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);
        Refresh(me, opponent);

        // PlayerHitEffect가 깜빡임 머티리얼로 바꾼 상황.
        Body(me).sharedMaterial = hit;
        Body(opponent).sharedMaterial = hit;
        Refresh(me, opponent);

        Assert.That(Body(me).sharedMaterial, Is.SameAs(hitFaded));
        Assert.That(Body(opponent).forceRenderingOff, Is.True);
        Assert.That(Bar(opponent).enabled, Is.False);

        Move(me, 3, 1);
        Refresh(me, opponent);
        Assert.That(Body(me).sharedMaterial, Is.SameAs(hit), "PlayerHitEffect restores its own original afterwards.");
    }

    [Test]
    public void RenderersAddedInsideABushAreHiddenToo()
    {
        PlayerBushVisibility me = NewPlayer("Me", 3, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);
        Refresh(me, opponent);

        var weapon = new GameObject("New Weapon").AddComponent<MeshRenderer>();
        weapon.transform.SetParent(opponent.transform);
        weapon.sharedMaterial = body;
        Refresh(me, opponent);
        Assert.That(weapon.forceRenderingOff, Is.True);

        Move(opponent, 4, 3);
        Refresh(me, opponent);
        Assert.That(weapon.forceRenderingOff, Is.False);
    }

    [Test]
    public void TransparentMaterialsAreLeftAsTheyAre()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 3, 1);
        Material particle = NewMaterial("Particle");
        particle.SetFloat("_Surface", 1f);
        var effect = new GameObject("Effect").AddComponent<MeshRenderer>();
        effect.transform.SetParent(me.transform);
        effect.sharedMaterial = particle;

        Refresh(me, opponent);

        Assert.That(effect.sharedMaterial, Is.SameAs(particle));
    }

    [Test]
    public void AnUnlistedOpaqueMaterialGetsARuntimeFadedCopy()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 3, 1);
        Material unlisted = NewMaterial("Unlisted");
        unlisted.SetFloat("_Cull", (float)CullMode.Off);
        var weapon = new GameObject("Weapon").AddComponent<MeshRenderer>();
        weapon.transform.SetParent(me.transform);
        weapon.sharedMaterial = unlisted;

        LogAssert.Expect(LogType.Warning, new Regex("'Unlisted' has no faded pair"));
        Refresh(me, opponent);
        Material copy = Track(weapon.sharedMaterial);

        Assert.That(copy, Is.Not.SameAs(unlisted));
        Assert.That(copy.shader, Is.SameAs(fadedShader));
        Assert.That(copy.GetFloat("_Surface"), Is.EqualTo(1f));
        Assert.That(copy.renderQueue, Is.EqualTo((int)RenderQueue.Transparent - 1));
        Assert.That(copy.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off), "Faces are drawn as the source draws them.");
        Assert.That(copy.GetColor("_BaseColor").a, Is.EqualTo(BushVisibilityRules.CharacterAlpha).Within(1e-4f));

        Move(me, 3, 1);
        Refresh(me, opponent);
        Assert.That(weapon.sharedMaterial, Is.SameAs(unlisted));
    }

    [Test]
    public void NoLongerBeingTheLocalPlayerRestoresTheScreen()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);
        me.IsLocal = true;
        Refresh(me, opponent);

        Assert.That(me.Look, Is.EqualTo(BushLook.InBush), "Precondition: I am in bush A.");

        me.IsLocal = false;

        Assert.That(me.Look, Is.EqualTo(BushLook.Normal), "My own faded look is restored too.");
        Assert.That(Body(me).sharedMaterial, Is.SameAs(body));
        Assert.That(opponent.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(Body(opponent).forceRenderingOff, Is.False);
        Assert.That(Bar(opponent).enabled, Is.True);
        Assert.That(bushA.Faded, Is.False);
    }

    [Test]
    public void WithoutABattleMapEveryoneLooksNormal()
    {
        PlayerBushVisibility me = NewPlayer("Me", 1, 1);
        PlayerBushVisibility opponent = NewPlayer("Opponent", 5, 2);
        Refresh(me, opponent);

        me.UpdateVisibility(null, new[] { me, opponent });

        Assert.That(me.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(opponent.Look, Is.EqualTo(BushLook.Normal));
        Assert.That(bushA.Faded, Is.False, "Bushes faded on the previous map are restored.");
    }

    [Test]
    public void BattlePrefabsHideTheirHealthBar()
    {
        // PlayerBushVisibility는 DuelWizard-1에 붙이고 DuelWizard-2는 그대로 물려받는다.
        foreach (string path in new[] { "Assets/Player/DuelWizard-1.prefab", "Assets/Player/DuelWizard-2.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var visibility = prefab.GetComponent<PlayerBushVisibility>();
            Assert.That(visibility, Is.Not.Null, path);
            var healthBar = new SerializedObject(visibility).FindProperty("healthBar").objectReferenceValue as Canvas;
            Assert.That(healthBar, Is.Not.Null, path);
            Assert.That(healthBar.name, Is.EqualTo("HealthBarUI"), path);
        }
    }

    [Test]
    public void BattlePrefabsHaveAFadedAssetForEveryOpaqueMaterial()
    {
        List<Material> sources = PlayerFadedMaterialsBuilder.CollectSources();
        Assert.That(sources.Select(m => m.name), Is.SupersetOf(new[] { "Wizard_Navy", "Wizard_Red", "RobeRed" }));
        string[] keywordSpace = fadedShader.keywordSpace.keywordNames;

        foreach (string path in PlayerFadedMaterialsBuilder.Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var pairs = new Dictionary<Material, Material>();
            SerializedProperty list = new SerializedObject(prefab.GetComponent<PlayerBushVisibility>()).FindProperty("fadedMaterials");
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                pairs[(Material)element.FindPropertyRelative("source").objectReferenceValue] =
                    (Material)element.FindPropertyRelative("faded").objectReferenceValue;
            }
            foreach (Material source in sources)
            {
                string label = $"{path}: {source.name}. Run Tools > Wizard Duel > Rebuild Faded Player Materials.";
                Assert.That(pairs.TryGetValue(source, out Material faded) && faded != null, Is.True, label);
                Assert.That(AssetDatabase.Contains(faded), Is.True, label);
                Assert.That(faded.shader, Is.SameAs(fadedShader), label);
                Assert.That(faded.GetFloat("_Surface"), Is.EqualTo(1f), label);
                Assert.That(faded.renderQueue, Is.EqualTo((int)RenderQueue.Transparent - 1), $"{label} Faded characters draw before the faded reeds.");
                Assert.That(faded.GetColor("_BaseColor").a, Is.EqualTo(BushVisibilityRules.CharacterAlpha).Within(1e-4f), label);
                foreach (string keyword in source.shaderKeywords.Where(k => keywordSpace.Contains(k)))
                    Assert.That(faded.IsKeywordEnabled(keyword), Is.True, $"{label} keyword {keyword}");
                Assert.That(faded.GetTexture("_BaseMap"), Is.SameAs(source.GetTexture("_BaseMap")), label);
                Assert.That(faded.GetFloat("_Cull"), Is.EqualTo(source.GetFloat("_Cull")), $"{label} Faces are drawn as the source draws them.");
                Assert.That(faded.doubleSidedGI, Is.False, label);
            }
        }
    }

    [Test]
    public void TheFadedShaderWritesDepthBeforeBlendingOnce()
    {
        Assert.That(fadedShader, Is.Not.Null, PlayerFadedMaterialsBuilder.ShaderPath);
        Assert.That(fadedShader.name, Is.EqualTo(PlayerBushVisibility.FadedShaderName));
        Assert.That(ShaderUtil.ShaderHasError(fadedShader), Is.False);
        Assert.That(fadedShader.renderQueue, Is.EqualTo((int)RenderQueue.Transparent - 1));
        var lightModes = new List<string>();
        for (int i = 0; i < fadedShader.passCount; i++)
            lightModes.Add(fadedShader.FindPassTagValue(i, new ShaderTagId("LightMode")).name);
        Assert.That(lightModes, Does.Contain(CharacterFadeFeature.DepthPassTag), "CharacterFadeFeature draws this pass first.");
        Assert.That(lightModes, Does.Contain("UniversalForward"));
        // URP Lit에서 UsePass로 가져온 그림자 패스는 LightMode 값이 대문자로 나온다.
        Assert.That(lightModes.Any(m => string.Equals(m, "ShadowCaster", System.StringComparison.OrdinalIgnoreCase)), Is.True,
            $"Faded characters keep their shadow. Passes: {string.Join(", ", lightModes)}");
    }

    [Test]
    public void ThePcRendererRunsTheCharacterFadeFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PlayerFadedMaterialsBuilder.RendererPath);
        Assert.That(data, Is.Not.Null, PlayerFadedMaterialsBuilder.RendererPath);
        ScriptableRendererFeature feature = data.rendererFeatures.FirstOrDefault(f => f is CharacterFadeFeature);
        Assert.That(feature, Is.Not.Null, "Run Tools > Wizard Duel > Rebuild Faded Player Materials.");
        Assert.That(feature.isActive, Is.True);
    }

    void Refresh(PlayerBushVisibility me, PlayerBushVisibility opponent) =>
        me.UpdateVisibility(field, new[] { me, opponent });

    PlayerBushVisibility NewPlayer(string name, int x, int z)
    {
        GameObject player = NewObject(name);
        player.transform.position = Cell(x, z);
        var renderer = new GameObject("Body").AddComponent<MeshRenderer>();
        renderer.transform.SetParent(player.transform);
        renderer.sharedMaterial = body;
        var canvas = new GameObject("HealthBarUI").AddComponent<Canvas>();
        canvas.transform.SetParent(player.transform);
        var visibility = player.AddComponent<PlayerBushVisibility>();
        var serialized = new SerializedObject(visibility);
        serialized.FindProperty("healthBar").objectReferenceValue = canvas;
        SerializedProperty list = serialized.FindProperty("fadedMaterials");
        list.arraySize = 2;
        SetPair(list.GetArrayElementAtIndex(0), body, bodyFaded);
        SetPair(list.GetArrayElementAtIndex(1), hit, hitFaded);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        players.Add(visibility);
        return visibility;
    }

    static void SetPair(SerializedProperty element, Material source, Material faded)
    {
        element.FindPropertyRelative("source").objectReferenceValue = source;
        element.FindPropertyRelative("faded").objectReferenceValue = faded;
    }

    BushBlock NewBush(int x, int z)
    {
        BushBlock block = NewObject($"Bush {x},{z}").AddComponent<BushBlock>();
        var renderer = new GameObject("Mesh").AddComponent<MeshRenderer>();
        renderer.transform.SetParent(block.transform);
        renderer.sharedMaterial = NewMaterial("Leaves");
        var serialized = new SerializedObject(block);
        serialized.FindProperty("fadedMaterial").objectReferenceValue = bushFaded;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        field.Add(x, z, block);
        return block;
    }

    static Renderer Body(PlayerBushVisibility player) => player.transform.Find("Body").GetComponent<Renderer>();

    static Canvas Bar(PlayerBushVisibility player) => player.GetComponentInChildren<Canvas>(true);

    void Move(PlayerBushVisibility player, int x, int z) => player.transform.position = Cell(x, z);

    // 7×5 레이아웃의 칸 (x, z) 중심은 root 기준 (x - 3, 0, 2 - z)이다.
    Vector3 Cell(int x, int z) => root.TransformPoint(new Vector3(x - 3f, 0f, 2f - z));

    GameObject NewObject(string name) => Track(new GameObject(name));

    Material NewMaterial(string name) => Track(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name });

    T Track<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }
}
#endif
