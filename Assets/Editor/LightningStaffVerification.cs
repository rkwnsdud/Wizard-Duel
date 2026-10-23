using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Task-scoped commands are consumed by the already-running editor. No second editor is launched.
[InitializeOnLoad]
public static class LightningStaffVerification
{
    const string Request = "Temp/LightningStaffRequest.txt";
    const string Output = "Logs/LightningStaffInspection.txt";
    static double realPlayCaptureAt;
    static LightningStaffVerification()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += PlayState;
    }
    static void Poll()
    {
        if (SessionState.GetBool("StaffVerification.RealPlay", false) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            if (realPlayCaptureAt == 0) realPlayCaptureAt = EditorApplication.timeSinceStartup + 8;
            if (EditorApplication.timeSinceStartup >= realPlayCaptureAt)
            {
                SessionState.SetBool("StaffVerification.RealPlay", false);
                try
                {
                    File.WriteAllText("Docs/LightningStaff/Final-runtime.txt", InspectActual());
                    var scene = SceneManager.GetSceneByPath("Assets/Scenes/PlayerTest.unity");
                    var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).First(c => c.isActiveAndEnabled && c.CompareTag("MainCamera"));
                    Capture(camera, "Docs/LightningStaff/Final-PlayerTest-game-camera.png");
                }
                catch (Exception e) { File.WriteAllText("Docs/LightningStaff/Final-play-error.txt", e.ToString()); Debug.LogException(e); }
                finally { EditorApplication.ExitPlaymode(); realPlayCaptureAt = 0; }
            }
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "build") { LightningStaffReplacementTool.Build(); Inspect(); }
            else if (command == "preview") Preview();
            else if (command == "uvdump") DumpUV();
            else if (command == "maskcompare") { ImportFilled(); Preview("Mask-before"); Preview("Mask-after"); }
            else if (command == "applyfilled") ApplyFilled();
            else if (command == "finalverify") { EnsurePlayerTest(); File.WriteAllText("Docs/LightningStaff/Final-renderer.txt", InspectActual()); Preview("Final-applied"); }
            else if (command == "realplay") { EnsurePlayerTest(); SessionState.SetBool("StaffVerification.Play", false); SessionState.SetBool("StaffVerification.RealPlay", true); realPlayCaptureAt = 0; EditorApplication.EnterPlaymode(); }
            else if (command == "scene") CaptureLoadedPlayerTest();
            else if (command == "play") BeginPlay();
            else if (command == "stop") EditorApplication.ExitPlaymode();
            else throw new Exception("Unknown staff verification command: " + command);
        }
        catch (Exception e) { File.WriteAllText(Output, e.ToString()); Debug.LogException(e); }
    }
    public static void BeginPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Editor is already playing.");
        SessionState.SetBool("StaffVerification.Play", true);
        SessionState.SetBool("StaffVerification.OptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt("StaffVerification.Options", (int)EditorSettings.enterPlayModeOptions);
        // Use Unity's normal domain/scene reload for a faithful PlayerTest check.
        EditorSettings.enterPlayModeOptionsEnabled = false;
        EditorApplication.EnterPlaymode();
    }
    static void EnsurePlayerTest()
    {
        if (SceneManager.GetSceneByPath("Assets/Scenes/PlayerTest.unity").isLoaded) return;
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new Exception("Unsaved scene preserved; refusing scene switch.");
        EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity", OpenSceneMode.Single);
    }
    static string InspectActual()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/PlayerTest.unity");
        var staff = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).First(t => t.name == "LightningStaff" && PathOf(t).Contains("WeaponSocket"));
        var renderer = staff.Find("Model").GetComponentInChildren<Renderer>(true);
        var m = renderer.sharedMaterial;
        return $"Playing={EditorApplication.isPlaying}\nSceneDirty={scene.isDirty}\nRenderer={PathOf(renderer.transform)}\nMaterial={AssetDatabase.GetAssetPath(m)}\nShader={m.shader.name}\nLocalEmission={m.IsKeywordEnabled(new LocalKeyword(m.shader, "_EMISSION"))}\nGI={m.globalIlluminationFlags}\nHDR={m.GetColor("_EmissionColor")}\nMap={AssetDatabase.GetAssetPath(m.GetTexture("_EmissionMap"))}\n";
    }
    static void DumpUV()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff/LightningStaff_New.fbx");
        var mesh = asset.GetComponentInChildren<MeshFilter>().sharedMesh;
        using (var data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var vertices = new Unity.Collections.NativeArray<Vector3>(mesh.vertexCount, Unity.Collections.Allocator.Temp))
        using (var uv = new Unity.Collections.NativeArray<Vector2>(mesh.vertexCount, Unity.Collections.Allocator.Temp))
        using (var writer = new BinaryWriter(File.Create("Docs/LightningStaff/mesh-uv-audit.bin")))
        {
            data[0].GetVertices(vertices); data[0].GetUVs(0, uv);
            writer.Write(mesh.vertexCount);
            for (int i = 0; i < mesh.vertexCount; i++) { writer.Write(vertices[i].x); writer.Write(vertices[i].y); writer.Write(vertices[i].z); writer.Write(uv[i].x); writer.Write(uv[i].y); }
            var sub = data[0].GetSubMesh(0);
            using (var indices = new Unity.Collections.NativeArray<int>(sub.indexCount, Unity.Collections.Allocator.Temp))
            { data[0].GetIndices(indices, 0); writer.Write(indices.Length); foreach (var i in indices) writer.Write(i); }
        }
    }
    static void PlayState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool("StaffVerification.Play", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            foreach (var s in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
                foreach (var root in s.GetRootGameObjects()) root.SetActive(false);
            var stage = SceneManager.CreateScene("Lightning staff verification (temporary)");
            SceneManager.SetActiveScene(stage);
            LightningStaffPlayProbe.CaptureFrame = (camera, path) => Capture(camera, path);
            new GameObject("Lightning Staff Verification").AddComponent<LightningStaffPlayProbe>();
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool("StaffVerification.OptionsEnabled", true);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt("StaffVerification.Options", 0);
            SessionState.SetBool("StaffVerification.Play", false);
        }
    }
    const string StaffAssets = "Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff/";
    static void ImportFilled()
    {
        string path = StaffAssets + "LightningStaff_Emission_CrystalFilled.png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var source = (TextureImporter)AssetImporter.GetAtPath(StaffAssets + "LightningStaff_Emission_Adjusted.png");
        var target = (TextureImporter)AssetImporter.GetAtPath(path);
        var settings = new TextureImporterSettings(); source.ReadTextureSettings(settings); target.SetTextureSettings(settings);
        target.maxTextureSize = source.maxTextureSize;
        target.textureCompression = source.textureCompression;
        target.compressionQuality = source.compressionQuality;
        target.SaveAndReimport();
    }
    static void ApplyFilled()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(StaffAssets + "LightningStaff_URP.mat");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(StaffAssets + "LightningStaff_Emission_CrystalFilled.png");
        if (texture == null || !material.IsKeywordEnabled("_EMISSION")) throw new Exception("Missing texture or emission disabled; refusing apply.");
        material.SetTexture("_EmissionMap", texture);
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        File.WriteAllText("Docs/LightningStaff/Applied-material.txt", $"Editor preview verification (not play mode)\nShader={material.shader.name}\nEmission={material.IsKeywordEnabled(new LocalKeyword(material.shader, "_EMISSION"))}\nHDR={material.GetColor("_EmissionColor")}\nMap={AssetDatabase.GetAssetPath(material.GetTexture("_EmissionMap"))}\n");
    }
    public static void Preview(string variant = null)
    {
        string folder = "Docs/LightningStaff" + (variant == null ? "" : "/" + variant);
        Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        Material comparisonMaterial = null;
        try
        {
            var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Player.prefab"));
            SceneManager.MoveGameObjectToScene(player, scene);
            if (variant == "Mask-before" || variant == "Mask-after")
            {
                var renderer = player.GetComponentsInChildren<Transform>().First(t => t.name == "LightningStaff").Find("Model").GetComponentInChildren<Renderer>();
                comparisonMaterial = new Material(renderer.sharedMaterial);
                comparisonMaterial.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(StaffAssets + (variant == "Mask-after" ? "LightningStaff_Emission_CrystalFilled.png" : "LightningStaff_Emission_Adjusted.png")));
                renderer.sharedMaterial = comparisonMaterial;
            }
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var b in player.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;
            var wizard = player.transform.Find("Wizard");
            var cam = new GameObject("Verification camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.15f, 0.19f);
            cam.orthographic = true;
            cam.orthographicSize = 1.25f;
            cam.nearClipPlane = 0.01f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            var vol = new GameObject("Existing bloom profile").AddComponent<Volume>();
            SceneManager.MoveGameObjectToScene(vol.gameObject, scene);
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Scenes/PlayerTest/Global Volume Profile.asset");
            var light = new GameObject("Verification key").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -35, 0);
            var fill = new GameObject("Verification fill").AddComponent<Light>();
            SceneManager.MoveGameObjectToScene(fill.gameObject, scene);
            fill.type = LightType.Directional; fill.intensity = 0.65f;
            fill.transform.rotation = Quaternion.Euler(30, 145, 0);
            var report = new StringBuilder();
            foreach (var pose in new[] { ("WizardCombatIdle", 0.15f, "Idle"), ("WizardWalkForward", 0.267f, "Walk"), ("WizardStaffCast", 0.2f, "Cast") })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/" + pose.Item1 + ".anim");
                clip.SampleAnimation(wizard.gameObject, pose.Item2);
                var hand = player.GetComponentsInChildren<Transform>().Single(t => t.name == "RightHand");
                var fire = player.GetComponentsInChildren<Transform>().Single(t => t.name == "FirePoint");
                report.AppendLine($"{pose.Item3}: hand={hand.position:F5}, fire={fire.position:F5}, staffAxis={fire.forward:F5}, actualAim={player.transform.forward:F5}");
                cam.transform.position = new Vector3(3, 1.9f, 5);
                cam.transform.LookAt(new Vector3(0, 1.02f, 0));
                cam.orthographicSize = 1.25f;
                Capture(cam, folder + "/" + pose.Item3 + "-preview.png", player);
                cam.transform.position = hand.position + new Vector3(1.3f, 0.8f, 1.8f);
                cam.transform.LookAt(hand.position);
                cam.orthographicSize = 0.32f;
                Capture(cam, folder + "/" + pose.Item3 + "-grip.png", player);
                cam.transform.position = fire.position + new Vector3(0.65f, 0.35f, 0.9f);
                cam.transform.LookAt(fire.position);
                cam.orthographicSize = 0.20f;
                Capture(cam, folder + "/" + pose.Item3 + "-crystal.png", player);
                if (pose.Item3 == "Idle")
                {
                    var staffRenderer = player.GetComponentsInChildren<Transform>().First(t => t.name == "LightningStaff").Find("Model").GetComponentInChildren<Renderer>();
                    var original = staffRenderer.sharedMaterial;
                    var probe = new Material(original);
                    try
                    {
                        staffRenderer.sharedMaterial = probe;
                        probe.DisableKeyword("_NORMALMAP");
                        Capture(cam, folder + "/Diagnostic-no-normal.png", player);
                        probe.SetTexture("_BaseMap", Texture2D.whiteTexture);
                        Capture(cam, folder + "/Diagnostic-white-base.png", player);
                        probe.SetColor("_BaseColor", Color.black);
                        probe.DisableKeyword("_METALLICSPECGLOSSMAP");
                        probe.SetFloat("_Metallic", 0);
                        probe.SetFloat("_SpecularHighlights", 0);
                        probe.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        Capture(cam, folder + "/Diagnostic-emission-only.png", player);
                    }
                    finally { staffRenderer.sharedMaterial = original; UnityEngine.Object.DestroyImmediate(probe); }
                }
            }
            File.WriteAllText("Logs/LightningStaff/poses.txt", report.ToString());
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); if (comparisonMaterial != null) UnityEngine.Object.DestroyImmediate(comparisonMaterial); }
    }

    public static void CaptureLoadedPlayerTest()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/PlayerTest.unity");
        if (scene.IsValid() && scene.isLoaded && scene.isDirty)
            throw new InvalidOperationException("PlayerTest has unsaved changes; refusing to reload it.");
        scene = EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity", OpenSceneMode.Single);
        if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("PlayerTest scene is not loaded.");
        var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var staff = transforms.First(t => t.name == "LightningStaff");
        var fire = staff.Find("FirePoint");
        var renderer = staff.Find("Model").GetComponentInChildren<Renderer>(true);
        var material = renderer.sharedMaterial;
        var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.isActiveAndEnabled);
        var oldPos = camera.transform.position; var oldRot = camera.transform.rotation;
        bool oldOrtho = camera.orthographic; float oldSize = camera.orthographicSize;
        try
        {
            camera.orthographic = true; camera.orthographicSize = 0.20f;
            camera.transform.position = fire.position + new Vector3(0.65f, 0.35f, 0.9f);
            camera.transform.LookAt(fire.position);
            Capture(camera, "Docs/LightningStaff/PlayerTest-crystal.png");
            bool emissionEnabled = material.IsKeywordEnabled("_EMISSION");
            var localKeyword = new LocalKeyword(material.shader, "_EMISSION");
            bool localEmission = material.IsKeywordEnabled(localKeyword);
            string enabledKeywords = string.Join(",", material.enabledKeywords.Select(k => k.name));
            Color emissionColor = material.GetColor("_EmissionColor");
            string emissionMap = AssetDatabase.GetAssetPath(material.GetTexture("_EmissionMap"));
            File.WriteAllText("Logs/LightningStaff/scene-material.txt",
                $"Renderer={renderer.name}\nMaterial={AssetDatabase.GetAssetPath(material)}\nShader={material.shader.name}\nLocalKeywordValid={localKeyword.isValid}\nEmissionKeyword={emissionEnabled}\nLocalEmission={localEmission}\nEnabledKeywords={enabledKeywords}\nEmissionColor={emissionColor}\nEmissionMap={emissionMap}\n");
        }
        finally
        {
            camera.transform.SetPositionAndRotation(oldPos, oldRot);
            camera.orthographic = oldOrtho; camera.orthographicSize = oldSize;
        }
    }

    public static void Capture(Camera camera, string path, GameObject bakeRoot = null)
    {
        var baked = new System.Collections.Generic.List<GameObject>();
        var skins = bakeRoot != null ? bakeRoot.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray() : Array.Empty<SkinnedMeshRenderer>();
        foreach (var skin in skins)
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            var go = new GameObject("Temporary baked skin");
            SceneManager.MoveGameObjectToScene(go, camera.gameObject.scene);
            go.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false; baked.Add(go);
        }
        var previous = RenderTexture.active;
        var oldTarget = camera.targetTexture;
        var rt = new RenderTexture(1600, 1200, 24, RenderTextureFormat.ARGBHalf);
        var texture = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt);
            foreach (var go in baked) { UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(go); }
            foreach (var skin in skins) skin.enabled = true;
        }
    }
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    public static void Inspect()
    {
        var b = new StringBuilder();
        b.AppendLine("Editor playing=" + EditorApplication.isPlaying);
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            b.AppendLine($"Scene {s.path}, dirty={s.isDirty}");
        }
        foreach (string path in new[] { "Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff/LightningStaff_New.fbx", "Assets/Weapons/Lightning Weapon/LightningStaff/LightningStaff_Model.fbx", "Assets/Player/Player.prefab" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            b.AppendLine("ASSET " + path);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (path.EndsWith("Player.prefab") && !PathOf(t).Contains("LightningStaff") && !t.name.ToLowerInvariant().Contains("hand")) continue;
                b.AppendLine($"{PathOf(t)} p={t.localPosition:F5} r={t.localEulerAngles:F3} s={t.localScale:F5} world={t.position:F5} lossy={t.lossyScale:F5}");
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null) b.AppendLine($"Mesh {mf.sharedMesh.name}, bounds={mf.sharedMesh.bounds.ToString("F5")}, vertices={mf.sharedMesh.vertexCount}");
                var smr = t.GetComponent<SkinnedMeshRenderer>();
                if (smr != null) b.AppendLine($"Skin {smr.sharedMesh.name}, bounds={smr.sharedMesh.bounds.ToString("F5")}");
            }
            if (!path.EndsWith("Player.prefab")) continue;
            foreach (var clip in go.GetComponentInChildren<Animator>().runtimeAnimatorController.animationClips.Distinct())
                b.AppendLine("Clip " + clip.name + " " + clip.length + " " + string.Join(",", AnimationUtility.GetAnimationEvents(clip).Select(e => e.functionName + "@" + e.time)));
        }
        File.WriteAllText(Output, b.ToString());
        Debug.Log("Lightning staff inspection complete.");
    }
}
