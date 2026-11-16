using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run from Tools > Wizard Duel > Bake missing weapon HUD icons, or with
// -batchmode -executeMethod WeaponHudIconBaker.BakeAll.
// Existing icon artwork is never overwritten. The scene is held in memory only;
// no existing scene or weapon prefab is saved.
public static class WeaponHudIconBaker
{
    private const string OutputFolder = "Assets/Game/UI/Art/WeaponIcons";
    private const string CatalogPath = "Assets/Game/Battle/WeaponSpawnCatalog.asset";
    private const int Size = 512;

    [MenuItem("Tools/Wizard Duel/Bake missing weapon HUD icons")]
    public static void BakeAll()
    {
        Directory.CreateDirectory(OutputFolder);
        Scene activeScene = SceneManager.GetActiveScene();
        if (!Application.isBatchMode && activeScene.isDirty)
            throw new InvalidOperationException("Save the active scene before baking weapon icons.");
        // Batch mode starts with an untitled scene, which Unity cannot add to.
        if (string.IsNullOrEmpty(activeScene.path))
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Open a saved scene before baking weapon icons.");
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        }
        Scene captureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(captureScene);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.56f, 0.59f, 0.67f);
            var lightObject = new GameObject("Icon key light");
            SceneManager.MoveGameObjectToScene(lightObject, captureScene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.8f;
            light.color = new Color(1f, 0.94f, 0.83f);
            lightObject.transform.rotation = Quaternion.Euler(35, -35, 0);

            var fillObject = new GameObject("Icon fill light");
            SceneManager.MoveGameObjectToScene(fillObject, captureScene);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 1.2f;
            fill.color = new Color(0.64f, 0.78f, 1f);
            fillObject.transform.rotation = Quaternion.Euler(145, 125, 0);

            var cameraObject = new GameObject("Icon camera");
            SceneManager.MoveGameObjectToScene(cameraObject, captureScene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.cullingMask = 1 << 30;

            var catalog = AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>(CatalogPath);
            foreach (WeaponDefinition definition in catalog.Entries)
            {
                string iconPath = OutputFolder + "/" + definition.iconFile + ".png";
                if (AssetDatabase.LoadAssetAtPath<Sprite>(iconPath) == null && !Bake(definition, camera, captureScene)) continue;
                definition.icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                EditorUtility.SetDirty(definition);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Weapon HUD icon bake finished; check warnings for prefabs with unavailable materials.");
        }
        finally
        {
            EditorSceneManager.CloseScene(captureScene, true);
        }
    }

    [MenuItem("Tools/Wizard Duel/Validate weapon HUD assets")]
    public static void ValidateHudAssets()
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Player/Prefabs/DuelWizard-1.prefab");
        var hud = player != null ? player.GetComponent<SecondaryWeaponHud>() : null;
        if (hud == null) throw new InvalidOperationException("Player prefab has no SecondaryWeaponHud.");

        var serializedHud = new SerializedObject(hud);
        var catalog = serializedHud.FindProperty("catalog").objectReferenceValue as WeaponSpawnCatalog;
        if (catalog == null) throw new InvalidOperationException("Player HUD catalog is not assigned.");
        if (serializedHud.FindProperty("fontAsset").objectReferenceValue == null)
            throw new InvalidOperationException("Player HUD font is not assigned.");

        foreach (string path in new[]
        {
            "Assets/Game/Player/Prefabs/DuelWizard-1.prefab", "Assets/Game/Player/Prefabs/DuelWizard-2.prefab"
        })
        {
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (variant == null || variant.GetComponent<SecondaryWeaponHud>() == null)
                throw new InvalidOperationException("HUD is missing from combat variant: " + path);
        }

        foreach (WeaponDefinition definition in catalog.Entries)
        {
            if (definition.prefab == null || definition.icon == null || string.IsNullOrWhiteSpace(definition.hudDisplayName))
                throw new InvalidOperationException("Weapon HUD definition is incomplete: " + definition.name);
        }
        Debug.Log("Weapon HUD asset validation passed: " + catalog.Entries.Length + " weapon definitions.");
    }

    private static bool Bake(WeaponDefinition source, Camera camera, Scene captureScene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(source.prefab));
        if (prefab == null) throw new InvalidOperationException("Weapon prefab missing: " + AssetDatabase.GetAssetPath(source.prefab));

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, captureScene);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(source.iconRotation));
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 30;

            foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(behaviour is IWeaponDisplayAnimation)) behaviour.enabled = false;
            foreach (var particle in instance.GetComponentsInChildren<ParticleSystem>(true))
                particle.gameObject.SetActive(false);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) renderer.enabled = false;

            var renderers = instance.GetComponentsInChildren<Renderer>();
            bool found = false;
            bool materialsAvailable = true;
            Bounds bounds = default;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || !material.shader.isSupported)
                    {
                        Debug.LogWarning(source.iconFile + ": " + renderer.name + " has a missing or unsupported material.");
                        materialsAvailable = false;
                    }
                }
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) throw new InvalidOperationException("No visible model renderer: " + AssetDatabase.GetAssetPath(source.prefab));
            if (!materialsAvailable)
            {
                Debug.LogWarning("Skipped " + source.iconFile + " icon: prefab has missing or unsupported materials.");
                return false;
            }

            var direction = new Vector3(1f, 0.36f, -1f).normalized;
            camera.transform.position = bounds.center + direction * Mathf.Max(10f, bounds.size.magnitude * 3f);
            camera.transform.LookAt(bounds.center, Vector3.up);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(100f, bounds.size.magnitude * 10f);

            float extent = 0f;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector3 projected = camera.transform.InverseTransformPoint(corner);
                extent = Mathf.Max(extent, Mathf.Abs(projected.x), Mathf.Abs(projected.y));
            }
            camera.orthographicSize = Mathf.Max(0.05f, extent * 1.18f);

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.antiAliasing = 4;
            var previous = RenderTexture.active;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply();
                string path = OutputFolder + "/" + source.iconFile + ".png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                // Detailed source art is shown at roughly 50 UI pixels. Mipmaps
                // prefilter it before reduction; compression adds visible blocks.
                importer.mipmapEnabled = source.iconMipmaps;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = source.iconMipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
                importer.SaveAndReimport();
                Debug.Log("Baked " + path + " from " + AssetDatabase.GetAssetPath(source.prefab));
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
        return true;
    }

}
