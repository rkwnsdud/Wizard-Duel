using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LightningStaffReplacementTool
{
    private const string Root = "Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff";
    private const string ModelPath = Root + "/LightningStaff_New.fbx";
    private const string AlbedoPath = Root + "/LightningStaff_Albedo.png";
    private const string NormalPath = Root + "/LightningStaff_Normal.jpg";
    private const string MetallicPath = Root + "/LightningStaff_Metallic.png";
    private const string RoughnessPath = Root + "/LightningStaff_Roughness.png";
    private const string EmissionPath = Root + "/LightningStaff_Emission.png";
    private const string AdjustedEmissionPath = Root + "/LightningStaff_Emission_Adjusted.png";
    private const string MaskPath = Root + "/LightningStaff_MetallicSmoothness.png";
    private const string MaterialPath = Root + "/LightningStaff_URP.mat";
    private const string PlayerPath = "Assets/Player/Player.prefab";

    [InitializeOnLoadMethod]
    private static void BuildAdjustedMaterialOnce()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!File.Exists(AdjustedEmissionPath) || material == null || material.GetColor("_EmissionColor").maxColorComponent < 7f)
            EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Wizard Duel/Rebuild Lightning Staff")]
    public static void Build()
    {
        ConfigureTexture(AlbedoPath, true, false);
        ConfigureTexture(NormalPath, false, true);
        ConfigureTexture(MetallicPath, false, false, true);
        ConfigureTexture(RoughnessPath, false, false, true);
        ConfigureTexture(EmissionPath, true, false);
        CreateAdjustedEmission();
        CreateMask();
        CreateMaterial();
        ConfigureModel();
        ReplaceModel();
        var modelImporter = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        modelImporter.isReadable = false;
        modelImporter.SaveAndReimport();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
    }

    private static void ConfigureTexture(string path, bool srgb, bool normal, bool readable = false)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = srgb;
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.isReadable = readable;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.maxTextureSize = path == AlbedoPath || path == NormalPath || path == EmissionPath ? 4096 : 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static void CreateMask()
    {
        // Decode source pixels as linear data, avoiding imported compression and a GPU blit.
        var metallic = ReadSource(MetallicPath);
        var roughness = ReadSource(RoughnessPath);
        int width = metallic.width;
        int height = metallic.height;
        if (roughness.width != width || roughness.height != height)
            throw new InvalidOperationException("Metallic and roughness dimensions differ.");

        Color32[] metalPixels = metallic.GetPixels32();
        Color32[] roughPixels = roughness.GetPixels32();
        var packed = new Color32[metalPixels.Length];
        for (int i = 0; i < packed.Length; i++)
        {
            // URP Lit reads metallic from R and smoothness from A.
            byte metal = metalPixels[i].r;
            if (metalPixels[i].g != metal || metalPixels[i].b != metal ||
                roughPixels[i].r != roughPixels[i].g || roughPixels[i].r != roughPixels[i].b)
                throw new InvalidOperationException("Expected grayscale metallic and roughness source maps.");
            byte smoothness = (byte)(255 - roughPixels[i].r);
            packed[i] = new Color32(metal, 0, 0, smoothness);
        }

        var output = new Texture2D(width, height, TextureFormat.RGBA32, true, true);
        output.SetPixels32(packed);
        output.Apply(true);
        File.WriteAllBytes(MaskPath, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(output);
        UnityEngine.Object.DestroyImmediate(roughness);
        UnityEngine.Object.DestroyImmediate(metallic);
        AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static void CreateAdjustedEmission()
    {
        // Preserve the authored UV mask, but grade its lit pixels from a blue edge
        // to a cool white core. The original texture remains untouched.
        var source = ReadSource(EmissionPath);
        Color32[] input = source.GetPixels32();
        var outputPixels = new Color32[input.Length];
        Color edge = new Color(0.02f, 0.18f, 0.85f, 1f);
        Color core = new Color(0.82f, 0.94f, 1f, 1f);
        for (int i = 0; i < input.Length; i++)
        {
            float mask = Mathf.Max(input[i].r, Mathf.Max(input[i].g, input[i].b)) / 255f;
            if (mask < 0.015f)
            {
                outputPixels[i] = new Color32(0, 0, 0, 255);
                continue;
            }

            // Keep dim authored pixels blue and move only the brightest crystal
            // detail toward blue-white. Multiplication by mask keeps UV borders clean.
            // The authored map is intentionally very dark. Expand that useful
            // range so its blue crystal facets reach HDR without lifting black UVs.
            float coreWeight = Mathf.SmoothStep(0.025f, 0.28f, mask);
            Color graded = Color.Lerp(edge, core, coreWeight) * Mathf.Lerp(0.75f, 1f, Mathf.Sqrt(mask));
            outputPixels[i] = graded;
        }

        var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false);
        output.SetPixels32(outputPixels);
        output.Apply(true);
        File.WriteAllBytes(AdjustedEmissionPath, output.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(output);
        UnityEngine.Object.DestroyImmediate(source);
        AssetDatabase.ImportAsset(AdjustedEmissionPath, ImportAssetOptions.ForceUpdate);
        ConfigureTexture(AdjustedEmissionPath, true, false);
    }

    private static Texture2D ReadSource(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            throw new InvalidOperationException("Cannot decode " + path);
        return texture;
    }

    private static void CreateMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        material.name = "LightningStaff_URP";
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
        // These are multipliers for the packed texture channels. Keeping the base
        // color neutral avoids solving the glare by unnecessarily darkening albedo.
        material.SetFloat("_Metallic", 0.75f);
        material.SetFloat("_Smoothness", 0.34f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AdjustedEmissionPath));
        material.SetColor("_EmissionColor", new Color(8.0f, 8.0f, 8.0f, 1f));
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureModel()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.isReadable = true; // Needed only while calculating the crystal and shaft landmarks.
        importer.SaveAndReimport();
    }

    private static void ReplaceModel()
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Transform staff = root.transform.GetComponentsInChildren<Transform>(true).First(t => t.name == "LightningStaff");
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Transform oldModel = staff.Find("Model");
            // Reuse the instance on repeated runs to retain its local file IDs.
            bool reuse = oldModel != null && PrefabUtility.GetCorrespondingObjectFromSource(oldModel.gameObject) == modelAsset;
            var model = reuse ? oldModel.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, staff);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            Bounds bounds = LocalBounds(model);
            Vector3 axis = bounds.size.y >= bounds.size.x && bounds.size.y >= bounds.size.z ? Vector3.up :
                bounds.size.z >= bounds.size.x ? Vector3.forward : Vector3.right;
            Vector3 crystal = EmissionCentroid(model);
            if (Vector3.Dot(crystal - bounds.center, axis) < 0) axis = -axis;
            Quaternion rotation = Quaternion.FromToRotation(axis, Vector3.up);
            const float targetLength = 1.62f;
            float length = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = targetLength / (length * staff.lossyScale.x);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localRotation = rotation;
            var gripNames = new[] { "RightIndexProximal", "RightIndexDistal", "RightMiddleProximal", "RightMiddleDistal", "RightRingProximal", "RightRingDistal", "RightLittleProximal", "RightLittleDistal" };
            var bones = root.GetComponentsInChildren<Transform>(true);
            Vector3 grip = Vector3.zero;
            foreach (string name in gripNames) grip += bones.Single(t => t.name == name).position;
            grip /= gripNames.Length;
            // Place a straight shaft section (47% from the butt) in the closed fingers.
            Vector3 anchor = bounds.center - axis * (length * 0.03f);
            model.transform.localPosition = staff.InverseTransformPoint(grip) - rotation * (anchor * scale);

            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            Transform firePoint = staff.Find("FirePoint");
            firePoint.position = model.transform.TransformPoint(crystal);
            firePoint.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            if (!reuse && oldModel != null) UnityEngine.Object.DestroyImmediate(oldModel.gameObject);
            model.name = "Model";
            Directory.CreateDirectory("Logs/LightningStaff");
            File.WriteAllText("Logs/LightningStaff/fit.txt", $"Source local bounds: {bounds.ToString("F6")}\nSource head axis: {axis}\nModel rotation: {rotation.eulerAngles}\nWorld staff length: {targetLength}\nScale: {scale:F6}\nGrip world: {grip:F6}\nGrip source: {anchor:F6}\nCrystal source: {crystal:F6}\nFirePoint staff-local: {firePoint.localPosition:F6}\n");
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static Bounds LocalBounds(GameObject go)
    {
        Bounds bounds = default;
        bool first = true;
        foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                p = go.transform.InverseTransformPoint(filter.transform.TransformPoint(p));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
        }
        if (first) throw new InvalidOperationException("No mesh in staff model.");
        return bounds;
    }

    private static Vector3 EmissionCentroid(GameObject go)
    {
        var emission = ReadSource(EmissionPath);
        Vector3 sum = Vector3.zero;
        float total = 0;
        try
        {
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                var vertices = filter.sharedMesh.vertices;
                var uv = filter.sharedMesh.uv;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Color color = emission.GetPixelBilinear(uv[i].x, uv[i].y);
                    float weight = Mathf.Max(color.r, color.g, color.b);
                    if (weight < 0.1f) continue;
                    sum += go.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i])) * weight;
                    total += weight;
                }
            }
            if (total < 0.001f) throw new InvalidOperationException("Cannot locate the crystal using the emission map.");
            return sum / total;
        }
        finally { UnityEngine.Object.DestroyImmediate(emission); }
    }

    public static void Validate()
    {
        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Transform staff = root.transform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LightningStaff");
            var weapon = staff != null ? staff.GetComponent<LightningWeapon>() : null;
            Transform firePoint = staff != null ? staff.Find("FirePoint") : null;
            Transform model = staff != null ? staff.Find("Model") : null;
            if (staff == null || weapon == null || firePoint == null || model == null)
                throw new Exception("Lightning staff hierarchy is incomplete.");
            if (model.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new Exception("New staff model has no renderer.");
            Debug.Log($"LIGHTNING_STAFF_VALID: model={model.name}, renderers={model.GetComponentsInChildren<Renderer>(true).Length}, firePoint={firePoint.localPosition}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
