using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class BasicWandAnimatorReview
{
    const string Folder = "Assets/Weapons/Basic Wand/Animations/";
    static Transform Find(GameObject g, string name) => g.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

    [MenuItem("Tools/Basic Wand/Review Animator Pose %&#l")]
    public static void Review()
    {
        var report = new StringBuilder();
        var source = EditorSceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Player");
        if (EditorApplication.isPlaying)
        {
            var animator = Find(source, "Wizard").GetComponent<Animator>();
            report.AppendLine("LIVE controller=" + animator.runtimeAnimatorController.name + "; Idle=" + animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
            foreach (var info in animator.GetCurrentAnimatorClipInfo(0)) report.AppendLine("LIVE clip=" + info.clip.name + "; weight=" + info.weight);
            foreach (var n in new[] { "RightArm", "RightForeArm", "RightHand", "RightIndexProximal", "RightMiddleProximal" })
                report.AppendLine(n + "=" + Find(source, n).localRotation.ToString("F6"));
            var owner = Find(source, "BasicWand").GetComponent<BasicWandIdlePose>();
            report.AppendLine("owner enabled=" + owner.isActiveAndEnabled + "; visible=" + Find(source, "BasicWand").GetComponentInChildren<MeshRenderer>(true).enabled);
            Directory.CreateDirectory("Docs/BasicWand/Attack/RuntimePose");
            File.WriteAllText("Docs/BasicWand/Attack/RuntimePose/live-review.txt", report.ToString());
            Debug.Log("BASIC_WAND_LIVE_REVIEW_DONE"); return;
        }
        foreach (bool useOverride in new[] { false, true })
        {
            var actor = UnityEngine.Object.Instantiate(source); actor.hideFlags = HideFlags.HideAndDontSave;
            PlayableGraph graph = default;
            try
            {
                var wizard = Find(actor, "Wizard"); var animator = wizard.GetComponent<Animator>();
                foreach (var script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                wizard.localRotation = Quaternion.identity;
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "WizardCombatIdle_BasicWandGrip.anim");
                idle.SampleAnimation(wizard.gameObject, .3f);
                string[] names = { "RightArm", "RightForeArm", "RightHand", "RightIndexProximal", "RightMiddleProximal" };
                var expected = names.Select(n => Find(actor, n).localRotation).ToArray();
                var controller = useOverride
                    ? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Folder + "BasicWandGrip.overrideController")
                    : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Player/Animations/WizardLocomotion.controller");
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("BasicWandEditModeReview"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, controller);
                var output = AnimationPlayableOutput.Create(graph, "Wizard", animator); output.SetSourcePlayable(playable);
                graph.Play(); graph.Evaluate(0); graph.Evaluate(.3f);
                var state = playable.GetCurrentAnimatorStateInfo(0);
                report.AppendLine("controller=" + controller.name + "; Idle=" + state.IsName("Idle") + "; state=" + state.shortNameHash);
                foreach (var info in playable.GetCurrentAnimatorClipInfo(0)) report.AppendLine("clip=" + info.clip.name + "; weight=" + info.weight);
                for (int i = 0; i < names.Length; i++)
                    report.AppendLine(names[i] + " expected=" + expected[i].ToString("F6") + " evaluated=" + Find(actor, names[i]).localRotation.ToString("F6") +
                        "; delta=" + Quaternion.Angle(expected[i], Find(actor, names[i]).localRotation));
                graph.Destroy(); graph = default;
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Player/Animations/WizardLocomotion.controller");
                animator.Rebind(); animator.Update(0); animator.Update(.3f);
                animator.runtimeAnimatorController = controller;
                animator.Update(0); animator.Update(.3f);
                report.AppendLine("NATIVE SWAP:");
                for (int i = 0; i < names.Length; i++) report.AppendLine(names[i] + "=" + Find(actor, names[i]).localRotation.ToString("F6"));
                animator.Rebind(); animator.Update(0); animator.Update(.3f);
                report.AppendLine("NATIVE REBIND:");
                for (int i = 0; i < names.Length; i++) report.AppendLine(names[i] + "=" + Find(actor, names[i]).localRotation.ToString("F6"));
                if (useOverride)
                {
                    var grip = actor.GetComponentInChildren<BasicWandGripSkin>(true);
                    grip.CharacterSkin.sharedMesh = grip.GripMesh;
                    var orb = actor.GetComponentInChildren<LightOrbWeapon>(true);
                    orb.enabled = true;
                    // Explicit editor preview reproduces the old LateUpdate conflict.
                    orb.ApplyPalmUpPose();
                    report.AppendLine("OLD ORB OVERLAY REPRODUCTION:");
                    for (int i = 0; i < names.Length; i++) report.AppendLine(names[i] + "=" + Find(actor, names[i]).localRotation.ToString("F6"));
                    Render(actor, "before-orb-overlay");
                    orb.RestorePalmPose();
                    var attack = actor.GetComponent<PlayerAttack>(); attack.enabled = true;
                    typeof(PlayerAttack).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(attack, null);
                    var wand = Find(actor, "BasicWand"); var owner = wand.GetComponent<BasicWandIdlePose>(); owner.enabled = true;
                    var renderer = wand.GetComponentInChildren<MeshRenderer>(true); renderer.enabled = true;
                    if (attack.IsWeaponPoseActive(orb)) throw new Exception("Orb pose still owns the visible wand");
                    report.AppendLine("PASS: visible wand rejects selected Orb's procedural pose.");
                    // Enabling components can reset Animator state in edit mode.
                    // Evaluate the wand again before comparing the restored bones.
                    animator.runtimeAnimatorController = controller;
                    animator.Rebind(); animator.Update(0); animator.Update(.3f);
                    report.AppendLine("RESTORED WAND POSE:");
                    for (int i = 0; i < names.Length; i++)
                    {
                        float delta = Quaternion.Angle(expected[i], Find(actor, names[i]).localRotation);
                        if (delta > .05f) throw new Exception("Pose was not restored: " + names[i]);
                        report.AppendLine(names[i] + " delta=" + delta);
                    }
                    renderer.enabled = true;
                    Render(actor, "after-wand-idle");
                    renderer.enabled = false;
                    if (!attack.IsWeaponPoseActive(orb)) throw new Exception("Hiding wand did not restore Orb pose authority");
                    report.AppendLine("PASS: hidden wand restores selected Orb's procedural pose authority.");
                }
            }
            finally { if (graph.IsValid()) graph.Destroy(); UnityEngine.Object.DestroyImmediate(actor); }
        }
        Directory.CreateDirectory("Docs/BasicWand/Attack/RuntimePose");
        File.WriteAllText("Docs/BasicWand/Attack/RuntimePose/animator-review.txt", report.ToString());
        Debug.Log("BASIC_WAND_ANIMATOR_REVIEW_DONE");
    }

    static void Render(GameObject actor, string name)
    {
        Directory.CreateDirectory("Docs/BasicWand/Attack/RuntimePose");
        var objects = new System.Collections.Generic.List<GameObject>();
        var meshes = new System.Collections.Generic.List<Mesh>();
        var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
        try
        {
            GameObject Temporary(string n) { var g = new GameObject(n) { hideFlags = HideFlags.HideAndDontSave, layer = 31 }; objects.Add(g); return g; }
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled && s.gameObject.activeInHierarchy))
            {
                var mesh = new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                mesh.vertices = mesh.vertices.Select(v => actor.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var g = Temporary("PoseReviewSkin"); g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            }
            foreach (var filter in Find(actor, "BasicWand").GetComponentsInChildren<MeshFilter>(true))
            {
                var original = filter.GetComponent<MeshRenderer>(); if (!original.enabled) continue;
                var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh); meshes.Add(mesh);
                mesh.vertices = mesh.vertices.Select(v => actor.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var g = Temporary("PoseReviewWand"); g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterials = original.sharedMaterials;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.white;
            var light = Temporary("PoseReviewLight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(25, -35, 0);
            var cam = Temporary("PoseReviewCamera").AddComponent<Camera>(); cam.cullingMask = 1 << 31;
            cam.orthographic = true; cam.orthographicSize = 1.65f;
            cam.transform.position = new Vector3(0, 1.5f, 5); cam.transform.LookAt(new Vector3(0, 1.5f, 0));
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.26f, .28f, .31f);
            var rt = new RenderTexture(900, 1100, 24); var tex = new Texture2D(900, 1100, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 900, 1100), 0, 0); tex.Apply();
                File.WriteAllBytes("Docs/BasicWand/Attack/RuntimePose/" + name + ".png", tex.EncodeToPNG());
            }
            finally { cam.targetTexture = null; RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); }
        }
        finally
        {
            RenderSettings.ambientMode = mode; RenderSettings.ambientLight = ambient;
            foreach (var g in objects) UnityEngine.Object.DestroyImmediate(g);
            foreach (var m in meshes) UnityEngine.Object.DestroyImmediate(m);
        }
    }
}
