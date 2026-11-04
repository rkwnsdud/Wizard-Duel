using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BasicWandGripSetup
{
    const string Folder="Assets/Weapons/Basic Wand/Animations";
    static Transform Find(GameObject g,string n)=>g.GetComponentsInChildren<Transform>(true).First(t=>t.name==n);
    [MenuItem("Tools/Basic Wand/Inspect Arm")]
    public static void InspectArm()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/PlayerTest.unity")throw new Exception("Open PlayerTest first");
        var original=scene.GetRootGameObjects().First(g=>g.name=="Player");
        var copy=UnityEngine.Object.Instantiate(original);copy.name="TemporaryArmInspection";
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim");
        idle.SampleAnimation(Find(copy,"Wizard").gameObject,0);
        var sb=new StringBuilder();
        foreach(var name in new[]{"RightShoulder","RightArm","RightForeArm","RightHand","RightIndexProximal","RightLittleProximal"})
        {
            var t=Find(copy,name);sb.AppendLine(name+" world="+copy.transform.InverseTransformPoint(t.position).ToString("F5")+" rot="+t.localRotation.ToString("F5")+" childDir="+(t.childCount>0?(t.GetChild(0).position-t.position).normalized.ToString("F5"):"none"));
        }
        UnityEngine.Object.DestroyImmediate(copy);
        Directory.CreateDirectory("Docs/BasicWand/Grip");File.WriteAllText("Docs/BasicWand/Grip/arm.txt",sb.ToString());
        Debug.Log("GRIP_ARM_INSPECT_SUCCESS");
    }
    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity");
        var wand=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t=>t.name=="BasicWand");
        var player=wand.root.gameObject;
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim");
        idle.SampleAnimation(Find(player,"Wizard").gameObject,0);
        var hand=Find(player,"RightHand"); var sb=new StringBuilder();
        foreach(var t in hand.GetComponentsInChildren<Transform>())
            if(!t.IsChildOf(Find(player,"WeaponSocket"))) sb.AppendLine(t.name+" pos="+hand.InverseTransformPoint(t.position).ToString("F6")+" rot="+t.localEulerAngles.ToString("F3")+" local="+t.localPosition.ToString("F6"));
        sb.AppendLine("wand pos="+hand.InverseTransformPoint(wand.position)+" axis="+hand.InverseTransformDirection(wand.forward));
        Directory.CreateDirectory("Docs/BasicWand/Grip");File.WriteAllText("Docs/BasicWand/Grip/bones.txt",sb.ToString());
        Debug.Log("GRIP_INSPECT_SUCCESS");
    }
    public static void Apply()
    {
        BasicWandIdleSetup.Apply();
    }
    public static void RefineAndPreview(){Apply();Preview();}
    [MenuItem("Tools/Basic Wand/Apply Relaxed Grip")]
    public static void ApplyMenu(){Apply();}
    [MenuItem("Tools/Basic Wand/Preview Grip")]
    public static void PreviewMenu(){Preview();}
    public static void Preview()
    {
        if(EditorSceneManager.GetActiveScene().path!="Assets/Scenes/PlayerTest.unity")throw new Exception("Open PlayerTest first");
        var wand=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t=>t.name=="BasicWand");
        var player=wand.root.gameObject;var wizard=Find(player,"Wizard");
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/WizardCombatIdle_BasicWandGrip.anim");idle.SampleAnimation(wizard.gameObject,0);
        var hidden=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>!r.transform.IsChildOf(player.transform)&&r.enabled).ToArray();
        foreach(var other in hidden)other.enabled=false;
        var baked=new System.Collections.Generic.List<GameObject>();var skins=player.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).ToArray();
        foreach(var r in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if(!r.enabled)continue;
            var mesh=new Mesh();r.BakeMesh(mesh);var go=new GameObject("TemporaryHandPreview");go.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;r.enabled=false;baked.Add(go);
        }
        var ambientMode=RenderSettings.ambientMode;var ambientLight=RenderSettings.ambientLight;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
        var light=new GameObject("TemporaryLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(30,-35,0);
        var hand=Find(player,"RightHand");Vector3 center=hand.TransformPoint(new Vector3(-.02f,.145f,.026f));
        for(int i=0;i<3;i++)
        {
            var cam=new GameObject("TemporaryCamera").AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=i==2?1.15f:.17f;
            if(i==2){center=player.transform.TransformPoint(new Vector3(0,1.05f,0));cam.transform.position=center+player.transform.forward*2;cam.transform.LookAt(center);}
            else {cam.transform.position=center+hand.TransformDirection(i==0?new Vector3(-.5f,.25f,-.4f):new Vector3(-.5f,.15f,.4f));cam.transform.LookAt(center,hand.TransformDirection(Vector3.up));}
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.13f,.15f,.18f);
            var rt=new RenderTexture(1000,1000,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1000,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1000,1000),0,0);tex.Apply();File.WriteAllBytes("Docs/BasicWand/Grip/view-"+i+".png",tex.EncodeToPNG());RenderTexture.active=null;
            UnityEngine.Object.DestroyImmediate(cam.gameObject);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
        }
        UnityEngine.Object.DestroyImmediate(light.gameObject);
        foreach(var go in baked){UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(go);}
        foreach(var r in skins)r.enabled=true;
        foreach(var r in hidden)r.enabled=true;
        RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambientLight;
        Debug.Log("GRIP_PREVIEW_SUCCESS");
    }
    static void SetRotation(AnimationClip clip,string path,Quaternion q)
    {
        foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path==path&&b.propertyName.Contains("Rotation")))AnimationUtility.SetEditorCurve(clip,binding,null);
        for(int i=0;i<4;i++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[i]),AnimationCurve.Constant(0,clip.length,q[i]));
    }
    public static void Validate()
    {
        BasicWandIdleSetup.Validate();
    }
    [MenuItem("Tools/Basic Wand/Validate Grip %#g")]
    public static void ValidateMenu(){Validate();}
    [MenuItem("Tools/Basic Wand/Diagnose Grip")]
    public static void DiagnoseGrip()
    {
        var scene=EditorSceneManager.GetActiveScene();
        var player=scene.GetRootGameObjects().First(g=>g.name=="Player");
        var copy=UnityEngine.Object.Instantiate(player);
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/WizardCombatIdle_BasicWandGrip.anim");
        idle.SampleAnimation(Find(copy,"Wizard").gameObject,0);
        var hand=Find(copy,"RightHand");var wand=Find(copy,"BasicWand");
        var sb=new StringBuilder();sb.AppendLine("Wand local to hand="+hand.InverseTransformPoint(wand.position).ToString("F4")+" axis="+hand.InverseTransformDirection(wand.forward).ToString("F4"));
        foreach(var finger in new[]{"Thumb","Index","Middle","Ring","Little"})
        foreach(var joint in new[]{"Proximal","Intermediate","Distal"})
        {
            var t=Find(copy,"Right"+finger+joint);
            Vector3 tip=t.position;
            if(joint=="Distal")tip=t.TransformPoint(new Vector3(0,.018f,0));
            Vector3 along=wand.forward;Vector3 delta=tip-wand.position;
            float axial=Vector3.Dot(delta,along),radius=Vector3.Cross(delta,along).magnitude;
            sb.AppendLine(finger+joint+" hand="+hand.InverseTransformPoint(tip).ToString("F4")+" axial="+axial.ToString("F4")+" radial="+radius.ToString("F4"));
        }
        UnityEngine.Object.DestroyImmediate(copy);
        File.WriteAllText("Docs/BasicWand/Grip/diagnosis.txt",sb.ToString());Debug.Log("GRIP_DIAGNOSIS_SUCCESS");
    }
}
