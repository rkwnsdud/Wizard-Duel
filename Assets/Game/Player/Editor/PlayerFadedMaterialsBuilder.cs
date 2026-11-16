using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// 전투 프리팹(DuelWizard-1·2)의 불투명 머티리얼마다 부시용 캐릭터 반투명본 에셋을 만들고 PlayerBushVisibility 목록에 넣는다(이슈 #93).
// 반투명본이 에셋으로 프리팹에 걸려 있어야 빌드가 WizardDuel/CharacterFaded 셰이더와 그 키워드 조합의 변형을 남긴다.
// 반투명본은 CharacterFadeFeature가 깊이를 먼저 써 줘야 한 덩어리로 섞이므로, PC 렌더러에 이 기능이 없으면 함께 붙인다.
// 프리팹의 무기나 머티리얼을 바꾸면 이 메뉴를 다시 실행한다. PlayerBushVisibilityTests가 빠진 짝과 기능을 잡는다.
public static class PlayerFadedMaterialsBuilder
{
    // PlayerBushVisibility는 DuelWizard-1에 붙이고 DuelWizard-2는 그대로 물려받는다.
    public const string BasePrefab = "Assets/Game/Player/Prefabs/DuelWizard-1.prefab";
    public static readonly string[] Prefabs = { BasePrefab, "Assets/Game/Player/Prefabs/DuelWizard-2.prefab" };
    public const string Folder = "Assets/Game/Player/Art/Faded";
    public const string ShaderPath = "Assets/Game/Player/Art/Shaders/CharacterFaded.shader";
    // 품질 설정 PC가 쓰는 렌더러. 프로젝트의 품질 단계는 PC 하나뿐이다.
    public const string RendererPath = "Assets/Settings/PC_Renderer.asset";

    [MenuItem("Tools/Wizard Duel/Rebuild Faded Player Materials")]
    public static void Rebuild()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) throw new InvalidOperationException($"Missing character fade shader {ShaderPath}.");
        List<Material> sources = CollectSources();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Player/Art", "Faded");
        var pairs = new List<PlayerBushVisibility.FadedMaterial>();
        var paths = new HashSet<string>();
        foreach (Material source in sources)
        {
            string path = $"{Folder}/{source.name}_Faded.mat";
            if (!paths.Add(path))
                throw new InvalidOperationException($"Two faded materials would share {path}. Rename one of the '{source.name}' materials.");
            pairs.Add(new PlayerBushVisibility.FadedMaterial { source = source, faded = WriteFaded(source, shader, path) });
        }
        AssetDatabase.SaveAssets();

        GameObject root = PrefabUtility.LoadPrefabContents(BasePrefab);
        try
        {
            if (!root.TryGetComponent(out PlayerBushVisibility visibility))
                visibility = root.AddComponent<PlayerBushVisibility>();
            var serialized = new SerializedObject(visibility);
            Transform healthBar = root.transform.Find("HealthBarUI");
            serialized.FindProperty("healthBar").objectReferenceValue = healthBar != null ? healthBar.GetComponent<Canvas>() : null;
            SerializedProperty list = serialized.FindProperty("fadedMaterials");
            list.arraySize = pairs.Count;
            for (int i = 0; i < pairs.Count; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("source").objectReferenceValue = pairs[i].source;
                element.FindPropertyRelative("faded").objectReferenceValue = pairs[i].faded;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, BasePrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        bool added = EnsureRendererFeature();
        Debug.Log($"[Bush] Rebuilt {pairs.Count} faded player materials in {Folder}: {string.Join(", ", sources.ConvertAll(m => m.name))}" +
                  (added ? $". Added {nameof(CharacterFadeFeature)} to {RendererPath}." : string.Empty));
    }

    // 전투 프리팹의 모든 렌더러(꺼 둔 무기 포함)와 피격 머티리얼 가운데 반투명본으로 바꿀 불투명 머티리얼. 에셋 경로 순서다.
    public static List<Material> CollectSources()
    {
        var sources = new List<Material>();
        void Add(Material material)
        {
            if (PlayerBushVisibility.CanFade(material) && !sources.Contains(material)) sources.Add(material);
        }
        foreach (string path in Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"Missing battle prefab {path}.");
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials) Add(material);
            foreach (PlayerHitEffect hit in prefab.GetComponentsInChildren<PlayerHitEffect>(true))
                Add(new SerializedObject(hit).FindProperty("hitMaterial").objectReferenceValue as Material);
        }
        sources.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
        return sources;
    }

    // PC 렌더러에 CharacterFadeFeature가 없으면 하위 에셋으로 붙인다. 붙였으면 true.
    // URP 렌더러 에디터가 하는 것처럼 기능 목록과 같은 순서로 하위 에셋 번호 목록(m_RendererFeatureMap)도 다시 맞춘다.
    public static bool EnsureRendererFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (data == null) throw new InvalidOperationException($"Missing renderer {RendererPath}.");
        if (data.rendererFeatures.Exists(feature => feature is CharacterFadeFeature)) return false;

        var fade = ScriptableObject.CreateInstance<CharacterFadeFeature>();
        fade.name = "Character Fade";
        AssetDatabase.AddObjectToAsset(fade, data);
        var serialized = new SerializedObject(data);
        SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = fade;
        SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
        map.arraySize = features.arraySize;
        for (int i = 0; i < features.arraySize; i++)
        {
            Object feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
            map.GetArrayElementAtIndex(i).longValue =
                feature != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id) ? id : 0;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        data.SetDirty();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return true;
    }

    // 이미 있는 에셋은 GUID를 지키도록 내용만 덮어쓴다.
    static Material WriteFaded(Material source, Shader shader, string path)
    {
        Material copy = PlayerBushVisibility.CreateFadedCopy(source, shader);
        var asset = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (asset == null)
        {
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
        EditorUtility.CopySerialized(copy, asset);
        Object.DestroyImmediate(copy);
        EditorUtility.SetDirty(asset);
        return asset;
    }
}
