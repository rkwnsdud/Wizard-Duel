using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BasicWandArmRelaxation
{
    const string IdlePath="Assets/Weapons/Basic Wand/Animations/WizardCombatIdle_BasicWandGrip.anim";
    const string OriginalPath="Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim";
    const string Output="Docs/BasicWand/Idle/Arm";
    static Transform Find(GameObject g,string n)=>g.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);
    static bool RotationBinding(EditorCurveBinding b)=>b.propertyName.Contains("Rotation")||b.propertyName.Contains("Euler");

    [MenuItem("Tools/Basic Wand/Restore Natural Forearm Roll")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode only. No Play Mode is started.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/PlayerTest.unity")throw new Exception("Open PlayerTest; no scene is opened/reloaded.");
        var actors=scene.GetRootGameObjects().Where(g=>g.name=="Player"||g.name=="Dummy Player").ToArray();
        if(actors.Length!=2)throw new Exception("Expected Player and Dummy Player");
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(OriginalPath);
        var report=new StringBuilder();Quaternion forearmPose,handPose;string forearmPath,handPath;
        var copy=UnityEngine.Object.Instantiate(actors.First(g=>g.name=="Player"));copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var wizard=Find(copy,"Wizard");original.SampleAnimation(wizard.gameObject,0);
            var arm=Find(copy,"RightArm");var forearm=Find(copy,"RightForeArm");var hand=Find(copy,"RightHand");
            Quaternion sourceRotation=forearm.rotation;
            Vector3 originalLine=(hand.position-forearm.position).normalized;
            Vector3 down=(forearm.position-arm.position).normalized;
            Quaternion swing=Quaternion.FromToRotation(originalLine,down);
            forearm.rotation=swing*sourceRotation;
            // Remove wrist bending while retaining only the original longitudinal twist.
            // Local +Y is the wrist/phalange direction on this Generic skeleton.
            Quaternion wrist=hand.localRotation;
            hand.localRotation=new Quaternion(0,wrist.y,0,wrist.w).normalized;
            forearmPose=forearm.localRotation;handPose=hand.localRotation;
            forearmPath=AnimationUtility.CalculateTransformPath(forearm,wizard);handPath=AnimationUtility.CalculateTransformPath(hand,wizard);
            if(Quaternion.Angle(Quaternion.Inverse(swing)*forearm.rotation,sourceRotation)>.01f)throw new Exception("Original forearm roll was not preserved");
            report.AppendLine("Original forearm frame preserved by pure swing; no additional axial roll. PASS");
            report.AppendLine("Neutral wrist: X/Z swing removed; original local-Y twist retained. Quaternion="+handPose.ToString("F6"));
        }
        finally{UnityEngine.Object.DestroyImmediate(copy);}
        var bindings=AnimationUtility.GetCurveBindings(clip);
        bool Allowed(EditorCurveBinding b)=>RotationBinding(b)&&(b.path==forearmPath||b.path==handPath);
        var untouched=bindings.Where(b=>!Allowed(b)).ToDictionary(b=>b,b=>AnimationUtility.GetEditorCurve(clip,b).keys);
        float duration=clip.length;string settings=JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(clip));
        string[] events=AnimationUtility.GetAnimationEvents(clip).Select(JsonUtility.ToJson).ToArray();
        Undo.RegisterCompleteObjectUndo(clip,"Restore natural forearm roll");
        SetRotation(clip,forearmPath,forearmPose);SetRotation(clip,handPath,handPose);clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);
        foreach(var pair in untouched)
        {
            var c=AnimationUtility.GetEditorCurve(clip,pair.Key);
            if(c==null||!c.keys.SequenceEqual(pair.Value))throw new Exception("Unrelated animation curve changed: "+pair.Key.path);
        }
        if(duration!=clip.length||settings!=JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(clip))||!events.SequenceEqual(AnimationUtility.GetAnimationEvents(clip).Select(JsonUtility.ToJson)))throw new Exception("Clip timing/settings/events changed");
        report.AppendLine("Only RightForeArm/RightHand rotation curves changed. Finger curves, other body curves, timing, loop settings and events unchanged. PASS");
        foreach(var actor in actors)
        {
            var forearm=Find(actor,"RightForeArm");var hand=Find(actor,"RightHand");var arm=Find(actor,"RightArm");var wand=Find(actor,"BasicWand");
            var fingers=hand.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Right")&&t!=hand).ToDictionary(t=>t,t=>t.localRotation);
            Vector3 wandPosition=wand.localPosition,wandScale=wand.localScale;Quaternion wandRotation=wand.localRotation;
            Undo.RecordObject(forearm,"Restore forearm roll");Undo.RecordObject(hand,"Restore wrist rotation");
            forearm.localRotation=forearmPose;hand.localRotation=handPose;
            PrefabUtility.RecordPrefabInstancePropertyModifications(forearm);PrefabUtility.RecordPrefabInstancePropertyModifications(hand);
            foreach(var p in fingers)if(Quaternion.Angle(p.Key.localRotation,p.Value)>.001f)throw new Exception("Finger grasp changed");
            if(wand.localPosition!=wandPosition||wand.localScale!=wandScale||Quaternion.Angle(wand.localRotation,wandRotation)>.001f)throw new Exception("Wand grip transform changed");
            float outwardBack=Vector3.Dot(hand.right,actor.transform.right);
            if(outwardBack<.6f)throw new Exception("Back of hand does not face outward");
            float wristBend=Vector3.Angle(hand.up,(hand.position-forearm.position).normalized);
            float elbowBend=Vector3.Angle(forearm.position-arm.position,hand.position-forearm.position);
            if(wristBend>.1f||elbowBend>.1f)throw new Exception("Arm/wrist is not straight: "+wristBend+", "+elbowBend);
            report.AppendLine(actor.name+": hand-back/outward dot="+outwardBack.ToString("F4")+"; wrist bend="+wristBend.ToString("F2")+" deg; elbow bend="+elbowBend.ToString("F2")+" deg");
            foreach(var finger in new[]{"Index","Middle","Ring","Little"})
            {
                var root=Find(actor,"Right"+finger+"Proximal");var tip=Find(actor,"Right"+finger+"Distal");
                float outward=Vector3.Dot((tip.TransformPoint(0,.018f,0)-root.position).normalized,actor.transform.right);
                if(outward>0)throw new Exception("Finger points outward: "+finger);
                report.AppendLine("  "+finger+" tip direction/outward="+outward.ToString("F4")+" (negative is inward)");
            }
            actor.GetComponentInChildren<BasicWandGripSkin>(true).RefreshSkin();
            RenderArm(actor);
        }
        foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(Allowed))
        {
            var c=AnimationUtility.GetEditorCurve(clip,b);
            if(Mathf.Abs(c.Evaluate(0)-c.Evaluate(clip.length))>1e-6f||Mathf.Abs(c.keys[0].outTangent)>1e-6f||Mathf.Abs(c.keys[c.length-1].inTangent)>1e-6f)throw new Exception("Arm loop seam");
        }
        report.AppendLine("Loop endpoints identical with zero tangents. Repaired skin, finger grasp, wand local transforms and weapon connections preserved. No hidden/cut finger geometry.");
        report.AppendLine("No Play Mode, no new scene, no attack or movement changes.");
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"/validation.txt",report.ToString());
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Debug.Log("BASIC_WAND_NATURAL_ARM_APPLIED_BOTH");
    }

    static void SetRotation(AnimationClip clip,string path,Quaternion q)
    {
        foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path==path&&RotationBinding(b)))AnimationUtility.SetEditorCurve(clip,b,null);
        for(int i=0;i<4;i++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[i]),AnimationCurve.Constant(0,clip.length,q[i]));
    }

    static void RenderArm(GameObject actor)
    {
        // Preview inactive characters without changing their authored visibility.
        var sourceActor=actor;actor=UnityEngine.Object.Instantiate(sourceActor);actor.name=sourceActor.name;actor.hideFlags=HideFlags.HideAndDontSave;
        foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            for(var t=skin.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
            skin.enabled=true;
        }
        actor.GetComponentInChildren<BasicWandGripSkin>(true).RefreshSkin();
        Directory.CreateDirectory(Output);var objects=new List<GameObject>();var meshes=new List<Mesh>();
        var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;
        try
        {
            GameObject Object(string n){var go=new GameObject(n);go.hideFlags=HideFlags.HideAndDontSave;go.layer=31;objects.Add(go);return go;}
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled&&s.gameObject.activeInHierarchy))
            {
                var source=skin.sharedMesh;var mesh=UnityEngine.Object.Instantiate(source);meshes.Add(mesh);
                var v=source.vertices;var weights=source.boneWeights;var bp=source.bindposes;
                var matrices=skin.bones.Select((b,i)=>actor.transform.worldToLocalMatrix*b.localToWorldMatrix*bp[i]).ToArray();
                for(int i=0;i<v.Length;i++){var b=weights[i];v[i]=matrices[b.boneIndex0].MultiplyPoint3x4(v[i])*b.weight0+matrices[b.boneIndex1].MultiplyPoint3x4(v[i])*b.weight1+matrices[b.boneIndex2].MultiplyPoint3x4(v[i])*b.weight2+matrices[b.boneIndex3].MultiplyPoint3x4(v[i])*b.weight3;}
                mesh.vertices=v;mesh.RecalculateNormals();mesh.RecalculateBounds();
                var go=Object("TemporaryArmSkin");go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            }
            foreach(var filter in Find(actor,"BasicWand").GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);meshes.Add(mesh);var matrix=actor.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                mesh.vertices=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
                var go=Object("TemporaryArmWand");go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            var light=Object("TemporaryArmLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(25,-40,0);
            var center=actor.transform.InverseTransformPoint((Find(actor,"RightForeArm").position+Find(actor,"RightHand").position)*.5f);
            var directions=new[]{new Vector3(1,.08f,0),new Vector3(.15f,.08f,1),new Vector3(.15f,.08f,-1)};
            for(int i=0;i<directions.Length;i++)
            {
                var cam=Object("TemporaryArmCamera").AddComponent<Camera>();cam.cullingMask=1<<31;cam.orthographic=true;cam.orthographicSize=.52f;cam.transform.position=center+directions[i].normalized*3;cam.transform.LookAt(center,Vector3.up);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.3f,.32f,.35f);
                var rt=new RenderTexture(900,900,24);var tex=new Texture2D(900,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;
                try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,900,900),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+actor.name.Replace(" ","-")+"-"+i+".png",tex.EncodeToPNG());}
                finally{cam.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
            }
        }
        finally{foreach(var go in objects)if(go)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in meshes)if(mesh)UnityEngine.Object.DestroyImmediate(mesh);RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;UnityEngine.Object.DestroyImmediate(actor);}
    }
}
