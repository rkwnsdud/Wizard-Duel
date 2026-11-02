using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BasicWandSetup
{
    const string Root = "Assets/Weapons/Basic Wand";
    static Transform Find(GameObject g, string name) => g.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    static Texture2D Texture(string name, bool srgb, bool normal = false)
    {
        string p = Root + "/Textures/" + name;
        var i = (TextureImporter)AssetImporter.GetAtPath(p);
        i.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        i.sRGBTexture = srgb; i.maxTextureSize = srgb ? 8192 : 4096;
        i.textureCompression = TextureImporterCompression.Uncompressed;
        i.mipmapEnabled = true; i.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }
    static Texture2D Read(string name)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        t.LoadImage(File.ReadAllBytes(Root + "/Textures/" + name)); return t;
    }
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode only");
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        var albedo = Texture("Image_0.jpg", true);
        var normal = Texture("Image_2.jpg", false, true);
        Texture("texture_0_roughness.png", false); Texture("texture_0_metallic.png", false);
        var rough = Read("texture_0_roughness.png"); var metal = Read("texture_0_metallic.png");
        var pixels = rough.GetPixels32(); var metals = metal.GetPixels32();
        if (pixels.Length != metals.Length) throw new Exception("Texture dimensions differ");
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(metals[i].r, 0, 0, (byte)(255 - pixels[i].r));
        var packed = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
        packed.SetPixels32(pixels); packed.Apply();
        File.WriteAllBytes(Root + "/Textures/BlackWood_MetallicSmoothness.png", packed.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(rough); UnityEngine.Object.DestroyImmediate(metal); UnityEngine.Object.DestroyImmediate(packed);
        AssetDatabase.Refresh();
        var mask = Texture("BlackWood_MetallicSmoothness.png", false);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = "BlackWood_Satin"; mat.SetTexture("_BaseMap", albedo); mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", .65f); mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", mask); mat.SetFloat("_Metallic", 0); mat.SetFloat("_Smoothness", 1);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Cull", 0);
        AssetDatabase.CreateAsset(mat, Root + "/Materials/BlackWood_Satin.mat");
        var importer = (ModelImporter)AssetImporter.GetAtPath(Root + "/Models/BasicWand_v2.fbx");
        importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
        importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.meshCompression = ModelImporterMeshCompression.Off; importer.isReadable = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Material.001"), mat);
        importer.SaveAndReimport();
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/BasicWand_v2.fbx");
        var wand = new GameObject("BasicWand");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset); model.name = "Model"; model.transform.SetParent(wand.transform, false);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        var points = model.GetComponentsInChildren<MeshFilter>().SelectMany(m => m.sharedMesh.vertices.Select(v => wand.transform.InverseTransformPoint(m.transform.TransformPoint(v)))).ToArray();
        float min = points.Min(v => v.x), max = points.Max(v => v.x), length = max - min;
        float Radius(Vector3 v) => v.y*v.y + v.z*v.z;
        float left = points.Where(v => v.x < min + length*.1f).Max(Radius);
        float right = points.Where(v => v.x > max - length*.1f).Max(Radius);
        Vector3 axis = left < right ? Vector3.left : Vector3.right;
        float tipX = left < right ? min : max;
        float gripX = left < right ? max - length*.18f : min + length*.18f;
        var tipPoints = points.Where(v => Mathf.Abs(v.x-tipX) < length*.002f).ToArray();
        var tip = new Vector3(tipX, tipPoints.Average(v=>v.y), tipPoints.Average(v=>v.z));
        float scale = .40f / length;
        model.transform.localRotation = Quaternion.FromToRotation(axis, Vector3.forward) * model.transform.localRotation;
        model.transform.localScale *= scale;
        model.transform.localPosition = -(model.transform.localRotation * new Vector3(gripX, 0, 0)) * scale;
        var fire = new GameObject("FirePoint"); fire.transform.SetParent(wand.transform, false);
        fire.transform.localPosition = model.transform.localPosition + model.transform.localRotation * tip * scale + Vector3.forward*.003f;
        var prefab = PrefabUtility.SaveAsPrefabAsset(wand, Root + "/Prefabs/BasicWand.prefab");
        UnityEngine.Object.DestroyImmediate(wand);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity", OpenSceneMode.Single);
        var report = new StringBuilder(); report.AppendLine("Basic Wand: 0.40 m, grip at 18% from butt; FirePoint +Z.");
        report.AppendLine($"FBX bounds X {min} .. {max}, tip axis {axis}; {points.Length} imported vertices.");
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim");
        if (!idle) throw new Exception("Idle clip missing");
        var sockets = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="WeaponSocket").ToArray();
        if(sockets.Length != 2) throw new Exception("Expected two PlayerTest sockets");
        foreach (var socket in sockets)
        {
            var player = socket.root.gameObject;
            var clone = UnityEngine.Object.Instantiate(player);
            var cloneSocket = Find(clone, "WeaponSocket");
            idle.SampleAnimation(Find(clone,"Wizard").gameObject, 0);
            string[] names = {"RightIndexProximal","RightIndexDistal","RightMiddleProximal","RightMiddleDistal","RightRingProximal","RightRingDistal","RightLittleProximal","RightLittleDistal"};
            Vector3 grip = Vector3.zero; foreach(var name in names) grip += Find(clone,name).position; grip /= names.Length;
            Vector3 localPosition = cloneSocket.InverseTransformPoint(grip);
            Quaternion localRotation = Quaternion.Inverse(cloneSocket.rotation) * Quaternion.LookRotation(clone.transform.forward, clone.transform.up);
            float localScale = 1f / cloneSocket.lossyScale.x;
            UnityEngine.Object.DestroyImmediate(clone);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(socket, false); instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation; instance.transform.localScale = Vector3.one * localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            foreach(var r in socket.GetComponentsInChildren<Renderer>(true).Where(r=>!r.transform.IsChildOf(instance.transform)))
            {
                r.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                report.AppendLine(player.name + " hidden renderer: " + r.name);
            }
            report.AppendLine($"{player.name}/RightHand/WeaponSocket/BasicWand position={localPosition.ToString("F6")} rotation={localRotation.eulerAngles.ToString("F6")} scale={localScale:F6}");
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Docs/BasicWand/setup-report.txt", report.ToString());
        Debug.Log("BASIC_WAND_SETUP_SUCCESS\n" + report);
    }
    public static void PreserveSourceOrientationAndVerify()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/BasicWand_v2.fbx");
        var contents = PrefabUtility.LoadPrefabContents(Root + "/Prefabs/BasicWand.prefab");
        contents.transform.Find("Model").localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.forward) * source.transform.localRotation;
        PrefabUtility.SaveAsPrefabAsset(contents, Root + "/Prefabs/BasicWand.prefab");
        PrefabUtility.UnloadPrefabContents(contents);
        Verify();
    }
    public static void Verify()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/BasicWand_v2.fbx");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/BasicWand.prefab");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity", OpenSceneMode.Single);
        var wands = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="BasicWand").ToArray();
        if(wands.Length != 2) throw new Exception("Missing wand instance");
        var report = new StringBuilder();
        report.AppendLine("Source root rotation: " + source.transform.localRotation);
        foreach(var wand in wands)
        {
            if(wand.parent.name!="WeaponSocket" || wand.parent.parent.name!="RightHand") throw new Exception("Wrong socket");
            if(wand.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) throw new Exception("Unexpected runtime script");
            var model = wand.Find("Model");
            var points = model.GetComponentsInChildren<MeshFilter>().SelectMany(m=>m.sharedMesh.vertices.Select(v=>wand.InverseTransformPoint(m.transform.TransformPoint(v)))).ToArray();
            float length = points.Max(v=>v.z)-points.Min(v=>v.z);
            if(Mathf.Abs(length-.4f)>.001f) throw new Exception("Incorrect wand length: " + length);
            var fire = wand.Find("FirePoint");
            if(Mathf.Abs(fire.localPosition.z-points.Max(v=>v.z)-.003f)>.001f) throw new Exception("FirePoint is not at tip");
            foreach(var r in model.GetComponentsInChildren<Renderer>())
                if(!r.sharedMaterial || r.sharedMaterial.shader.name!="Universal Render Pipeline/Lit" || !r.sharedMaterial.GetTexture("_BaseMap") || !r.sharedMaterial.GetTexture("_BumpMap") || !r.sharedMaterial.GetTexture("_MetallicGlossMap")) throw new Exception("Material reference missing");
            report.AppendLine(wand.root.name + ": right-hand socket OK; length=" + length + "; FirePoint=" + fire.localPosition.ToString("F6") + "; material references OK");
        }
        report.AppendLine("No scene saved; no Play Mode; no new scene.");
        File.WriteAllText("Docs/BasicWand/verification.txt", report.ToString());
        Debug.Log("BASIC_WAND_VERIFY_SUCCESS\n" + report);
    }
}
