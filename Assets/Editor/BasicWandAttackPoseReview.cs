using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BasicWandAttackPoseReview
{
    const string Folder = "Assets/Weapons/Basic Wand/Animations/";
    const string Output = "Docs/BasicWand/Attack/NeutralWrist";
    static Transform Find(GameObject actor, string name) => actor.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

    [MenuItem("Tools/Basic Wand/Review Attack Grip And Arm")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only.");
        Directory.CreateDirectory(Output);
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PlayerTest.unity");
        var report = new StringBuilder("Actual Unity clip sampling; scene instances inspected in a preview scene.\n");
        try
        {
            foreach (var actor in scene.GetRootGameObjects().Where(g => g.name == "Player" || g.name == "Dummy Player"))
            {
                foreach (var script in actor.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                var wizard = Find(actor, "Wizard"); wizard.localRotation = Quaternion.identity;
                var animator = wizard.GetComponent<Animator>(); if (animator) animator.enabled = false;
                var hand = Find(actor, "RightHand"); var elbow = Find(actor, "RightForeArm");
                var wand = Find(actor, "BasicWand"); var socket = Find(actor, "WeaponSocket");
                if (socket.parent != hand || wand.parent != socket) throw new Exception("Unexpected grip hierarchy.");
                foreach (var r in wand.GetComponentsInChildren<MeshRenderer>(true)) { r.enabled = true; for (var t = r.transform; t; t = t.parent) t.gameObject.SetActive(true); }
                var grip = actor.GetComponentInChildren<BasicWandGripSkin>(true);
                if (grip && grip.GripMesh) grip.CharacterSkin.sharedMesh = grip.GripMesh;
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "WizardCombatIdle_BasicWandGrip.anim");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "BasicWandAttack" + (actor.name == "Player" ? "" : "_DummyGrip") + ".anim");
                idle.SampleAnimation(wizard.gameObject, 0);
                var fingers = hand.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Right") && t != hand).ToArray();
                var fingerMatrices = fingers.Select(t => hand.worldToLocalMatrix * t.localToWorldMatrix).ToArray();
                var wandMatrix = hand.worldToLocalMatrix * wand.localToWorldMatrix;
                var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled).ToArray();
                // Only vertices entirely weighted to the hand/fingers are contact geometry.
                var indices = skins.Select(s => Enumerable.Range(0, s.sharedMesh.vertexCount).Where(i => {
                    var w = s.sharedMesh.boneWeights[i];
                    return w.weight0 + w.weight1 + w.weight2 + w.weight3 > .999f && new[] { (w.boneIndex0,w.weight0), (w.boneIndex1,w.weight1), (w.boneIndex2,w.weight2), (w.boneIndex3,w.weight3) }
                        .All(p => p.Item2 <= 0 || s.bones[p.Item1] == hand || fingers.Contains(s.bones[p.Item1]));
                }).ToArray()).ToArray();
                Vector3[][] HandVertices() => skins.Select((s,k) => { var vertices = SkinVertices(s); return indices[k].Select(i => hand.InverseTransformPoint(vertices[i])).ToArray(); }).ToArray();
                var idleVertices = HandVertices();
                float maxWrist = 0, maxGrip = 0, maxVertex = 0, aim = 0;
                {
                    for (int frame = 0; frame <= 130; frame++)
                    {
                        float t = frame * .005f; clip.SampleAnimation(wizard.gameObject, t);
                        maxWrist = Mathf.Max(maxWrist, Vector3.Angle(hand.up, hand.position - elbow.position));
                        var m = hand.worldToLocalMatrix * wand.localToWorldMatrix;
                        for (int j = 0; j < 16; j++) maxGrip = Mathf.Max(maxGrip, Mathf.Abs(m[j] - wandMatrix[j]));
                        for (int i = 0; i < fingers.Length; i++)
                        {
                            m = hand.worldToLocalMatrix * fingers[i].localToWorldMatrix;
                            for (int j = 0; j < 16; j++) maxGrip = Mathf.Max(maxGrip, Mathf.Abs(m[j] - fingerMatrices[i][j]));
                        }
                        var verts = HandVertices();
                        for (int s = 0; s < verts.Length; s++) for (int i = 0; i < verts[s].Length; i++) maxVertex = Mathf.Max(maxVertex, Vector3.Distance(verts[s][i], idleVertices[s][i]));
                        if (frame == 40) aim = Vector3.Angle(wand.forward,
                            Quaternion.AngleAxis(actor.name == "Player" ? -32f : -36f, actor.transform.right) * actor.transform.forward);
                    }
                    report.AppendLine($"{actor.name}: 131 samples (5 ms), wrist <= {maxWrist:F5} deg, release presentation error {aim:F5} deg, finger/socket matrix drift {maxGrip:E3}, {indices.Sum(a=>a.Length)} hand/finger vertices, max Idle-relative surface drift {maxVertex:E3} m.");
                    File.WriteAllText(Output + "/unity-validation.txt", report.ToString());
                    if (maxWrist > 25f || aim > .1f || maxGrip > .0001f || maxVertex > .0001f) throw new Exception("Grip/arm sampling failed: " + report);
                    float[] times = { 0, .08f, .16f, .20f, .26f, .35f, .45f, .55f, .60f, .65f };
                    var sheet = new Texture2D(640 * 5, 640 * 2, TextureFormat.RGB24, false);
                    try
                    {
                        for (int i = 0; i < times.Length; i++)
                        {
                            clip.SampleAnimation(wizard.gameObject, times[i]);
                            var image = Render(actor, false, new Vector3(3, 1, 4));
                            sheet.SetPixels((i % 5) * 640, (1 - i / 5) * 640, 640, 640, image.GetPixels()); UnityEngine.Object.DestroyImmediate(image);
                        }
                        sheet.Apply(); File.WriteAllBytes(Output + "/" + actor.name.Replace(" ", "-") + "-sequence.png", sheet.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(sheet); }
                    clip.SampleAnimation(wizard.gameObject, .20f);
                    foreach (var view in new[] { Vector3.forward, Vector3.right, new Vector3(-1, .3f, 1) })
                    {
                        var image = Render(actor, true, view);
                        File.WriteAllBytes(Output + "/" + actor.name.Replace(" ", "-") + "-grip-" + (view == Vector3.forward ? "front" : view == Vector3.right ? "side" : "inner") + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
                    }
                }
            }
            report.AppendLine("Sequence order: top row 0 / .08 / .16 / .20 / .26 s; bottom row .35 / .45 / .55 / .60 / .65 s. Surface drift measures preservation of Idle contact, not a new collision fit.");
            File.WriteAllText(Output + "/unity-validation.txt", report.ToString());
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // Evaluate skinning directly: Edit Mode BakeMesh may return the last rendered
    // pose when sampling an Animator disabled for an isolated preview.
    static Vector3[] SkinVertices(SkinnedMeshRenderer skin)
    {
        var vertices = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights;
        var bind = skin.sharedMesh.bindposes;
        var matrices = skin.bones.Select((b,i) => b.localToWorldMatrix * bind[i]).ToArray();
        return vertices.Select((v,i) => {
            var w=weights[i]; return matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1 + matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
        }).ToArray();
    }

    static Texture2D Render(GameObject actor, bool close, Vector3 view)
    {
        var objects = new List<GameObject>(); var meshes = new List<Mesh>();
        var ambient = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode;
        GameObject Temporary(string n) { var g = new GameObject(n) { hideFlags = HideFlags.HideAndDontSave, layer = 31 }; objects.Add(g); return g; }
        try
        {
            var bounds = new Bounds(); bool first = true;
            void Add(Mesh mesh, Transform source, Material[] materials)
            {
                meshes.Add(mesh); mesh.vertices = mesh.vertices.Select(v => actor.transform.InverseTransformPoint(source.TransformPoint(v))).ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                if (first) { bounds = mesh.bounds; first = false; } else bounds.Encapsulate(mesh.bounds);
                var g = Temporary("WandPoseReviewMesh"); g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
            foreach (var s in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled)) { var m = UnityEngine.Object.Instantiate(s.sharedMesh); m.vertices = SkinVertices(s).Select(v=>s.transform.InverseTransformPoint(v)).ToArray(); Add(m,s.transform,s.sharedMaterials); }
            foreach (var f in Find(actor,"BasicWand").GetComponentsInChildren<MeshFilter>(true)) { var r=f.GetComponent<MeshRenderer>(); if(r && r.enabled) Add(UnityEngine.Object.Instantiate(f.sharedMesh),f.transform,r.sharedMaterials); }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.white;
            var light = Temporary("WandPoseReviewLight").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.cullingMask=1<<31; light.transform.rotation=Quaternion.Euler(25,-35,0);
            var camera = Temporary("WandPoseReviewCamera").AddComponent<Camera>(); camera.cullingMask=1<<31; camera.orthographic=true;
            var target = close ? actor.transform.InverseTransformPoint(Find(actor,"RightHand").position) : bounds.center + Vector3.up * bounds.size.y * .18f;
            camera.orthographicSize = close ? .19f : bounds.size.y * .40f;
            camera.transform.position = target + view.normalized * 4; camera.transform.LookAt(target); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.22f,.24f,.27f);
            var rt=new RenderTexture(640,640,24); var image=new Texture2D(640,640,TextureFormat.RGB24,false); var previous=RenderTexture.active;
            try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,640,640),0,0); image.Apply(); return image; }
            finally { camera.targetTexture=null; RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(rt); }
        }
        finally { RenderSettings.ambientLight=ambient; RenderSettings.ambientMode=mode; foreach(var g in objects)UnityEngine.Object.DestroyImmediate(g); foreach(var m in meshes)UnityEngine.Object.DestroyImmediate(m); }
    }
}
