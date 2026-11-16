using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// 갈대 부시 에셋(이슈 #93)을 만든다: 색 그라데이션 텍스처, 불투명·반투명 머티리얼, 미리보기 메시, Reeds 프리팹.
// 칸마다 실제 모양은 실행 중에 ReedPatch가 이웃 칸에 맞춰 만들고, 프리팹의 미리보기 메시는 사방이 부시인 칸 모양이다.
// 색이나 ReedPatch 기본값을 바꾸면 ReedAssetsBuilder.Rebuild()를 다시 부른다(메뉴는 없다).
// ReedPatchTests도 이 클래스의 에셋 경로·셰이더 이름 상수를 참조한다.
public static class ReedAssetsBuilder
{
    public const string Folder = "Assets/Map/Blocks/Bushes";
    public const string PrefabPath = Folder + "/Reeds.prefab";
    public const string TexturePath = Folder + "/Materials/Reeds_Colors.png";
    public const string MaterialPath = Folder + "/Materials/Reeds.mat";
    public const string FadedMaterialPath = Folder + "/Materials/Reeds_Faded.mat";
    public const string PreviewMeshPath = Folder + "/Meshes/Reeds_Preview.asset";
    public const string FadedShaderName = "WizardDuel/ReedFaded";
    const int PreviewSeed = 93;
    const int ColumnWidth = 8;
    const int Height = 64;

    // 열마다 밑동에서 끝까지의 색(sRGB). 앞 ReedPatch.BladeColors열은 잎과 줄기, 나머지는 이삭이다.
    // 바닥 돌이 베이지색이라 밑동은 짙은 고동색으로 깔고 끝으로 갈수록 마른 갈대의 황갈색으로 밝아지게 둔다.
    static readonly Color[][] Gradients =
    {
        new[] { Rgb(0.20f, 0.13f, 0.07f), Rgb(0.45f, 0.29f, 0.14f), Rgb(0.70f, 0.52f, 0.27f), Rgb(0.85f, 0.70f, 0.43f) },
        new[] { Rgb(0.22f, 0.12f, 0.07f), Rgb(0.50f, 0.27f, 0.13f), Rgb(0.72f, 0.45f, 0.24f), Rgb(0.86f, 0.64f, 0.40f) },
        new[] { Rgb(0.19f, 0.15f, 0.08f), Rgb(0.42f, 0.34f, 0.17f), Rgb(0.64f, 0.55f, 0.30f), Rgb(0.80f, 0.72f, 0.47f) },
        new[] { Rgb(0.25f, 0.17f, 0.10f), Rgb(0.55f, 0.40f, 0.22f), Rgb(0.78f, 0.63f, 0.38f), Rgb(0.90f, 0.79f, 0.55f) },
        new[] { Rgb(0.42f, 0.30f, 0.22f), Rgb(0.62f, 0.47f, 0.34f), Rgb(0.82f, 0.70f, 0.54f), Rgb(0.92f, 0.84f, 0.70f) },
        new[] { Rgb(0.36f, 0.27f, 0.22f), Rgb(0.54f, 0.43f, 0.35f), Rgb(0.72f, 0.62f, 0.52f), Rgb(0.84f, 0.78f, 0.68f) },
        new[] { Rgb(0.45f, 0.32f, 0.18f), Rgb(0.68f, 0.52f, 0.30f), Rgb(0.85f, 0.72f, 0.47f), Rgb(0.94f, 0.86f, 0.66f) },
        new[] { Rgb(0.50f, 0.40f, 0.30f), Rgb(0.72f, 0.62f, 0.48f), Rgb(0.88f, 0.81f, 0.66f), Rgb(0.96f, 0.92f, 0.80f) },
    };

    public static void Rebuild()
    {
        EnsureFolder(Folder + "/Materials");
        EnsureFolder(Folder + "/Meshes");
        Texture2D texture = WriteTexture();
        Material opaque = WriteOpaqueMaterial(texture);
        Material faded = WriteFadedMaterial(texture);
        WritePrefab(opaque, faded);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Bush] Rebuilt reed assets: {PrefabPath}");
    }

    static Texture2D WriteTexture()
    {
        int width = ColumnWidth * ReedPatch.ColorColumns;
        var texture = new Texture2D(width, Height, TextureFormat.RGBA32, false);
        for (int column = 0; column < ReedPatch.ColorColumns; column++)
        {
            Color[] stops = Gradients[column];
            for (int y = 0; y < Height; y++)
            {
                float t = y / (Height - 1f) * (stops.Length - 1);
                int index = Mathf.Min(Mathf.FloorToInt(t), stops.Length - 2);
                Color color = Color.Lerp(stops[index], stops[index + 1], t - index);
                for (int x = 0; x < ColumnWidth; x++) texture.SetPixel(column * ColumnWidth + x, y, color);
            }
        }
        File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        // 열 하나가 8픽셀이라 밉맵을 만들면 옆 열 색이 섞인다. 화면에 늘 비슷한 크기로 보이므로 밉맵 없이 둔다.
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    static Material WriteOpaqueMaterial(Texture2D texture)
    {
        Material material = LoadOrCreate(MaterialPath, Shader.Find("Universal Render Pipeline/Lit"));
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);
        // 잎은 얇은 띠라 양면으로 그린다. 노멀은 ReedPatch가 카메라와 위쪽으로 맞춰 두어 뒷면도 같은 밝기다.
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.doubleSidedGI = true;
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetFloat("_EnvironmentReflections", 0f);
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material WriteFadedMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find(FadedShaderName);
        if (shader == null) throw new FileNotFoundException($"Shader '{FadedShaderName}' was not found.");
        Material material = LoadOrCreate(FadedMaterialPath, shader);
        material.shaderKeywords = new string[0];
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, BushVisibilityRules.BushAlpha));
        material.SetFloat("_Smoothness", 0.1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_Surface", 1f);
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    static void WritePrefab(Material opaque, Material faded)
    {
        bool exists = File.Exists(PrefabPath);
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Reeds");
        try
        {
            // 예전 돔 덤불 모델 같은 자식은 지우고 루트 하나에 메시를 둔다.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            BushBlock block = GetOrAdd<BushBlock>(root);
            var serialized = new SerializedObject(block);
            serialized.FindProperty("fadedMaterial").objectReferenceValue = faded;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 프리팹에 저장된 예전 모양 값이 남지 않게 ReedPatch 기본값으로 되돌린다.
            ReedPatch patch = GetOrAdd<ReedPatch>(root);
            var defaults = new GameObject("Reed Defaults", typeof(MeshFilter), typeof(ReedPatch));
            try { EditorUtility.CopySerialized(defaults.GetComponent<ReedPatch>(), patch); }
            finally { Object.DestroyImmediate(defaults); }
            MeshFilter filter = GetOrAdd<MeshFilter>(root);
            MeshRenderer renderer = GetOrAdd<MeshRenderer>(root);
            renderer.sharedMaterial = opaque;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            patch.Build(PreviewSeed, BushNeighbors.All);
            Mesh built = filter.sharedMesh;
            filter.sharedMesh = WritePreviewMesh(built);
            Object.DestroyImmediate(built);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    static Mesh WritePreviewMesh(Mesh source)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(PreviewMeshPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(source, existing);
            existing.name = "Reeds_Preview";
            EditorUtility.SetDirty(existing);
            return existing;
        }
        Mesh copy = Object.Instantiate(source);
        copy.name = "Reeds_Preview";
        AssetDatabase.CreateAsset(copy, PreviewMeshPath);
        return copy;
    }

    static Material LoadOrCreate(string path, Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }
        return material;
    }

    static T GetOrAdd<T>(GameObject gameObject) where T : Component =>
        gameObject.TryGetComponent(out T component) ? component : gameObject.AddComponent<T>();

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Color Rgb(float r, float g, float b) => new Color(r, g, b, 1f);
}
