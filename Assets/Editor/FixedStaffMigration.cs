using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Explicit, repeatable migration. Never runs automatically during project startup.
[InitializeOnLoad]
public static class FixedStaffMigration
{
    static FixedStaffMigration() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/FixedStaffRequest.txt";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            if (command == "apply") Apply(); else if (command == "audit") Audit(); else if (command == "pattern") Pattern(); else Inspect();
            File.WriteAllText("Logs/LightningStaffFixed/result.txt", command + " OK");
        }
        catch (Exception e) { File.WriteAllText("Logs/LightningStaffFixed/result.txt", e.ToString()); Debug.LogException(e); }
    }
    public const string Root = "Assets/Weapons/Lightning Weapon/LightningStaff/FixedStaff/";
    const string Fbx = Root + "LightningStaff_Fixed.fbx";
    const string Textures = Root + "LightningStaff_Fixed.fbm/";
    const string Player = "Assets/Player/Player.prefab";
    static void Pattern()
    {
        ConfigureCrystal(Material("Crystal_Cyan_Emission"), Texture("Texture_0.png", true), Texture("Texture_2.jpg", false, true));
        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var staff = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="LightningStaff" && t.gameObject.activeInHierarchy);
        var renderer = staff.Find("Model").GetComponentInChildren<Renderer>(true);
        File.WriteAllText("Logs/LightningStaffFixed/actual-materials.txt", string.Join("\n", renderer.sharedMaterials.Select(m=>m.name+" base="+AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap"))+" emission="+AssetDatabase.GetAssetPath(m.GetTexture("_EmissionMap")))));
        var cam = new GameObject("Temporary crystal inspection").AddComponent<Camera>();
        try
        {
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.17f,.17f,.17f);
            cam.orthographic = true; cam.orthographicSize = .20f; cam.nearClipPlane = .01f;
            var filter = renderer.GetComponent<MeshFilter>();
            var target = filter.transform.TransformPoint(filter.sharedMesh.GetSubMesh(1).bounds.center);
            var direction = filter.transform.TransformDirection(Vector3.up).normalized;
            var up = filter.transform.TransformDirection(Vector3.forward).normalized;
            cam.transform.position = target + direction; cam.transform.LookAt(target, up);
            LightningStaffVerification.Capture(cam, "Logs/LightningStaffFixed/pattern-front.png");
            cam.transform.position = target - direction; cam.transform.LookAt(target, up);
            LightningStaffVerification.Capture(cam, "Logs/LightningStaffFixed/pattern-back.png");
        }
        finally { UnityEngine.Object.DestroyImmediate(cam.gameObject); }
    }
    static void ConfigureCrystal(Material crystal, Texture2D albedo, Texture2D normal)
    {
        crystal.SetTexture("_BaseMap", albedo);
        crystal.SetColor("_BaseColor", Color.white);
        crystal.SetTexture("_BumpMap", normal);
        crystal.SetFloat("_BumpScale", .25f);
        crystal.EnableKeyword("_NORMALMAP");
        crystal.SetFloat("_Metallic", 0);
        crystal.SetTexture("_MetallicGlossMap", null);
        crystal.DisableKeyword("_METALLICSPECGLOSSMAP");
        crystal.SetFloat("_Smoothness", .25f);
        crystal.SetFloat("_SpecularHighlights", 0);
        crystal.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        // Use authored BASE COLOR detail for self-lighting, never the old patchy emission map.
        crystal.SetTexture("_EmissionMap", albedo);
        crystal.SetColor("_EmissionColor", new Color(.5f,.5f,.5f,1));
        crystal.EnableKeyword("_EMISSION");
        crystal.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        EditorUtility.SetDirty(crystal);
    }
    static void Audit()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        importer.isReadable = true; importer.SaveAndReimport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var report = new System.Text.StringBuilder();
        foreach (var filter in asset.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var texcoords = mesh.uv;
            var mats = filter.GetComponent<Renderer>().sharedMaterials;
            report.AppendLine($"Mesh {mesh.name} vertices={mesh.vertexCount} submeshes={mesh.subMeshCount}");
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetTriangles(s).Distinct().ToArray();
                report.AppendLine($"Slot {s} {mats[s].name} vertices={indices.Length}");
                using (var writer = new StreamWriter($"Logs/LightningStaffFixed/submesh-{s}.csv"))
                {
                    writer.WriteLine("x,y,z,u,v");
                    foreach (int i in indices) { var p = vertices[i]; var uv = texcoords[i]; writer.WriteLine(FormattableString.Invariant($"{p.x},{p.y},{p.z},{uv.x},{uv.y}")); }
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    var uvs = new System.Collections.Generic.List<Vector2>(); mesh.GetUVs(channel, uvs);
                    if (uvs.Count == 0) continue;
                    report.AppendLine($"UV{channel} range {indices.Min(i=>uvs[i].x)}..{indices.Max(i=>uvs[i].x)}, {indices.Min(i=>uvs[i].y)}..{indices.Max(i=>uvs[i].y)}");
                }
            }
        }
        File.WriteAllText("Logs/LightningStaffFixed/uv-audit.txt", report.ToString());
        importer.isReadable = false; importer.SaveAndReimport();
    }
    public static void Inspect()
    {
        Directory.CreateDirectory("Logs/LightningStaffFixed");
        var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var report = "Fixed bounds " + LightningStaffReplacementTool.LocalBounds(asset).ToString("F6") + "\n";
        foreach (var t in asset.GetComponentsInChildren<Transform>(true))
            report += t.name + " position=" + t.localPosition.ToString("F6") + " rotation=" + t.localEulerAngles + " scale=" + t.localScale + "\n";
        foreach(var r in asset.GetComponentsInChildren<Renderer>(true))
            report += r.name + " materials=" + string.Join(",",r.sharedMaterials.Select(m=>m.name)) + "\n";
        File.WriteAllText("Logs/LightningStaffFixed/import.txt",report);
    }

    static Texture2D Texture(string name, bool srgb, bool normal = false)
    {
        string path = Textures + name;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = srgb;
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Material(string name)
    {
        string path = Root + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        return m;
    }

    [MenuItem("Tools/Wizard Duel/Apply Fixed Lightning Staff")]
    public static void Apply()
    {
        Inspect();
        var albedo = Texture("Texture_0.png", true);
        var normal = Texture("Texture_2.jpg", false, true);
        Texture("Texture_1.png", false);
        var packedSource = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        ImageConversion.LoadImage(packedSource, File.ReadAllBytes(Textures + "Texture_1.png"));
        var pixels = packedSource.GetPixels32();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(pixels[i].b, 0, 0, (byte)(255 - pixels[i].g));
        packedSource.SetPixels32(pixels); packedSource.Apply();
        string maskPath = Root + "Staff_MetallicSmoothness.png";
        File.WriteAllBytes(maskPath, packedSource.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(packedSource);
        AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
        var maskImporter = (TextureImporter)AssetImporter.GetAtPath(maskPath);
        maskImporter.sRGBTexture = false;
        maskImporter.alphaSource = TextureImporterAlphaSource.FromInput;
        maskImporter.SaveAndReimport();

        var staffMaterial = Material("Staff_Original");
        staffMaterial.SetTexture("_BaseMap", albedo);
        staffMaterial.SetColor("_BaseColor", Color.white);
        staffMaterial.SetTexture("_BumpMap", normal);
        staffMaterial.EnableKeyword("_NORMALMAP");
        staffMaterial.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
        staffMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
        staffMaterial.SetFloat("_Metallic", 0.75f);
        staffMaterial.SetFloat("_Smoothness", 0.34f);
        staffMaterial.SetTexture("_EmissionMap", null);
        staffMaterial.SetColor("_EmissionColor", Color.black);
        staffMaterial.DisableKeyword("_EMISSION");

        var crystal = Material("Crystal_Cyan_Emission");
        ConfigureCrystal(crystal, albedo, normal);
        EditorUtility.SetDirty(staffMaterial); EditorUtility.SetDirty(crystal);
        var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Staff_Original"), staffMaterial);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Crystal_Cyan_Emission"), crystal);
        importer.SaveAndReimport();

        var root = PrefabUtility.LoadPrefabContents(Player);
        try
        {
            var staff = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "LightningStaff");
            var old = staff.Find("Model");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var source = PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject);
            if (source == asset) { AssetDatabase.SaveAssets(); return; }
            var oldBounds = LightningStaffReplacementTool.LocalBounds(source);
            var newBounds = LightningStaffReplacementTool.LocalBounds(asset);
            // Refuse an unverified coordinate conversion instead of moving the socket or FirePoint.
            if (Vector3.Distance(oldBounds.size, newBounds.size) > oldBounds.size.magnitude * 0.02f ||
                Vector3.Distance(oldBounds.center, newBounds.center) > oldBounds.size.magnitude * 0.02f)
                throw new InvalidOperationException("FBX coordinate system needs inspection before replacement. Old=" + oldBounds.ToString("F6") + " new=" + newBounds.ToString("F6"));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, staff);
            model.name = "Model";
            model.transform.SetLocalPositionAndRotation(old.localPosition, old.localRotation);
            model.transform.localScale = old.localScale;
            model.transform.SetSiblingIndex(old.GetSiblingIndex());
            var materials = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
            if (!materials.Contains(staffMaterial) || !materials.Contains(crystal))
                throw new InvalidOperationException("Both fixed FBX material slots must be mapped before saving.");
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, Player);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("Fixed staff applied. Render verification still required.");
    }
}
