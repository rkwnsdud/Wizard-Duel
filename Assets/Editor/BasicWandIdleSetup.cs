using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit edit-mode tools only. No scene creation, Play Mode or automatic application.
public static class BasicWandIdleSetup
{
    const string Folder = "Assets/Weapons/Basic Wand/Animations";
    const string Source = "Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim";
    static Vector3 Grip;
    static Vector3 Axis;
    static Transform Find(GameObject g, string name) => g.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    [Serializable] public class Point { public string bone; public Vector3 p; }
    [Serializable] public class Inspection { public List<Point> hand = new List<Point>(); public List<Vector3> wand = new List<Vector3>(); }

    [MenuItem("Tools/Basic Wand/Inspect Idle Contact %#i")]
    public static void Inspect()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode only");
        var scene = EditorSceneManager.GetActiveScene();
        var player = scene.GetRootGameObjects().Single(g => g.name == "Player");
        var copy = UnityEngine.Object.Instantiate(player);copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            AssetDatabase.LoadAssetAtPath<AnimationClip>(Source).SampleAnimation(Find(copy,"Wizard").gameObject,0);
            var hand = Find(copy,"RightHand");
            var data = new Inspection();
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try
                {
                    var verts = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights;
                    var matrices=skin.bones.Select((b,i)=>hand.worldToLocalMatrix*b.localToWorldMatrix*skin.sharedMesh.bindposes[i]).ToArray();
                    for(int i=0;i<verts.Length;i++)
                    {
                        var b=weights[i]; var name=skin.bones[b.boneIndex0].name;
                        if(name=="RightHand" || name.StartsWith("RightThumb") || name.StartsWith("RightIndex") || name.StartsWith("RightMiddle") || name.StartsWith("RightRing") || name.StartsWith("RightLittle"))
                            data.hand.Add(new Point {bone=name,p=matrices[b.boneIndex0].MultiplyPoint3x4(verts[i])*b.weight0+matrices[b.boneIndex1].MultiplyPoint3x4(verts[i])*b.weight1+matrices[b.boneIndex2].MultiplyPoint3x4(verts[i])*b.weight2+matrices[b.boneIndex3].MultiplyPoint3x4(verts[i])*b.weight3});
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            var wand=Find(copy,"BasicWand");
            foreach(var filter in wand.GetComponentsInChildren<MeshFilter>())
                foreach(var p in filter.sharedMesh.vertices)
                    data.wand.Add(wand.InverseTransformPoint(filter.transform.TransformPoint(p)));
            Directory.CreateDirectory("Docs/BasicWand/Idle");
            File.WriteAllText("Docs/BasicWand/Idle/contact-inspection.json",JsonUtility.ToJson(data));
            Debug.Log("BASIC_WAND_IDLE_INSPECT_OK hand="+data.hand.Count+" wand="+data.wand.Count);
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }

    sealed class SkinPoint
    {
        public Transform[] bones;
        public Vector3[] local;
        public float[] weights;
        public int joint;
        public Vector3 Position(Transform hand)
        {
            Vector3 p=Vector3.zero;
            for(int i=0;i<weights.Length;i++) if(weights[i]>0) p+=bones[i].TransformPoint(local[i])*weights[i];
            return hand.InverseTransformPoint(p);
        }
    }

    static List<SkinPoint> Points(GameObject player, Transform[] joints)
    {
        var result=new List<SkinPoint>();
        foreach(var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=skin.sharedMesh;var v=mesh.vertices;var bw=mesh.boneWeights;var bp=mesh.bindposes;
            var vertices=new Dictionary<int,SkinPoint>();
            for(int i=0;i<v.Length;i++)
            {
                int j=Array.IndexOf(joints,skin.bones[bw[i].boneIndex0]);
                if(j<0)continue;
                var b=bw[i];var ids=new[]{b.boneIndex0,b.boneIndex1,b.boneIndex2,b.boneIndex3};
                var point=new SkinPoint{joint=j,bones=ids.Select(k=>skin.bones[k]).ToArray(),
                    local=ids.Select(k=>bp[k].MultiplyPoint3x4(v[i])).ToArray(),weights=new[]{b.weight0,b.weight1,b.weight2,b.weight3}};
                result.Add(point);vertices[i]=point;
            }
            var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
            {
                if(!vertices.TryGetValue(triangles[i],out var a)||!vertices.TryGetValue(triangles[i+1],out var b)||!vertices.TryGetValue(triangles[i+2],out var c))continue;
                // Include pad interiors and edges, not only low-poly corner vertices.
                foreach(var points in new[]{new[]{a,b},new[]{b,c},new[]{c,a},new[]{a,b,c}})
                    result.Add(new SkinPoint{joint=points.Max(p=>p.joint),bones=points.SelectMany(p=>p.bones).ToArray(),local=points.SelectMany(p=>p.local).ToArray(),weights=points.SelectMany(p=>p.weights.Select(w=>w/points.Length)).ToArray()});
            }
        }
        return result;
    }

    // The profile is measured from the unmodified wand mesh, in hand-local units.

    static float Radius(float[] profile,float axial)
    {
        int i=Mathf.Clamp(Mathf.RoundToInt((axial+.08f)/.001f),0,profile.Length-1);
        return profile[i];
    }

    static void NaturalGrip(GameObject copy,Transform hand,Transform wand,StringBuilder report)
    {
        string[] names={"Index","Middle","Ring","Little","Thumb"};
        string[] parts={"Proximal","Intermediate","Distal"};
        var fingers=names.Select(n=>parts.Select(p=>Find(copy,"Right"+n+p)).ToArray()).ToArray();
        var originals=fingers.Select(f=>f.Select(t=>t.localRotation).ToArray()).ToArray();
        // Preserve the authored finger planes and spacing. Only local flexion is fitted.
        var curls=new[]{new Vector3(-8,8,8),new Vector3(0,8,18),new Vector3(7,10,14),new Vector3(14,10,10),Vector3.zero};
        Vector3 thumbAim=Vector3.zero;
        Action pose=()=>{for(int f=0;f<5;f++)for(int j=0;j<3;j++)fingers[f][j].localRotation=originals[f][j]*Quaternion.Euler(f==4&&j==0?thumbAim:new Vector3(0,0,curls[f][j]));};
        pose();
        var centers=fingers.Take(4).Select(f=>hand.InverseTransformPoint((f[0].position+f[2].position)*.5f)).ToArray();
        Grip=centers.Aggregate(Vector3.zero,(a,b)=>a+b)/4f;
        Axis=(centers[0]-centers[3]).normalized;
        // Principal axis of the natural grip cavity, not a preselected shaft orientation.
        for(int k=0;k<12;k++){Vector3 a=Vector3.zero;foreach(var c in centers){var d=c-Grip;a+=d*Vector3.Dot(d,Axis);}Axis=a.normalized;}
        if(Vector3.Dot(Axis,Vector3.up)<0)Axis=-Axis;
        Vector3 initialGrip=Grip,initialAxis=Axis;
        var samples=fingers.Select(f=>Points(copy,f)).ToArray();
        var palm=Points(copy,new[]{hand});
        var profile=new float[161];
        foreach(var filter in wand.GetComponentsInChildren<MeshFilter>())foreach(var p in filter.sharedMesh.vertices)
        {
            var v=wand.InverseTransformPoint(filter.transform.TransformPoint(p));int b=Mathf.RoundToInt((v.z+.08f)/.001f);
            if(b>=0&&b<profile.Length)profile[b]=Mathf.Max(profile[b],new Vector2(v.x,v.y).magnitude);
        }
        for(int i=0;i<profile.Length;i++)if(profile[i]==0)profile[i]=.012f;
        float scale=wand.localScale.x;
        Func<Vector3,float> gap=p=>
        {
            var d=p-Grip;float t=Vector3.Dot(d,Axis);
            return Vector3.Cross(d,Axis).magnitude-Radius(profile,t/scale)*scale;
        };
        Func<float> cost=()=>
        {
            pose();float e=0;
            for(int f=0;f<5;f++)
            {
                float nearest=float.MaxValue,penetration=0;
                foreach(var p in samples[f])
                {
                    float g=gap(p.Position(hand));
                    if(p.joint==2)nearest=Mathf.Min(nearest,g);
                    penetration=Mathf.Max(penetration,Mathf.Max(0,.0003f-g));
                }
                e+=penetration*penetration*100f;
                if(nearest<float.MaxValue)e+=Mathf.Pow(nearest-.001f,2)*3f;
                e+=(curls[f]-new[]{new Vector3(-8,8,8),new Vector3(0,8,18),new Vector3(7,10,14),new Vector3(14,10,10),Vector3.zero}[f]).sqrMagnitude*.00000006f;
            }
            float palmPenetration=0;
            foreach(var p in palm)palmPenetration=Mathf.Max(palmPenetration,Mathf.Max(0,.001f-gap(p.Position(hand))));
            e+=palmPenetration*palmPenetration*100f;
            e+=(Grip-initialGrip).sqrMagnitude*.1f+(Axis-initialAxis).sqrMagnitude*.0001f+thumbAim.sqrMagnitude*.00000003f;
            return e;
        };
        foreach(float step in new[]{8f,4f,2f,1f,.5f})for(int pass=0;pass<5;pass++)
        {
            bool changed=false;
            // Joint flexion stays within a modest anatomical range; no sideways twist.
            for(int f=0;f<4;f++)for(int j=0;j<3;j++)
            {
                float start=curls[f][j],best=start,ce=cost();
                foreach(float sign in new[]{-1f,1f})
                {
                    var c=curls[f];c[j]=Mathf.Clamp(start+sign*step,j==0?-12f:0f,j==0?20f:25f);curls[f]=c;float e=cost();if(e<ce){ce=e;best=c[j];}
                }
                var final=curls[f];final[j]=best;curls[f]=final;changed|=best!=start;
            }
            for(int a=0;a<3;a++)
            {
                float start=thumbAim[a],best=start,ce=cost();
                foreach(float sign in new[]{-1f,1f}){var c=thumbAim;c[a]=Mathf.Clamp(start+sign*step,-25,25);thumbAim=c;float e=cost();if(e<ce){ce=e;best=c[a];}}
                thumbAim[a]=best;changed|=best!=start;
            }
            for(int a=0;a<3;a++)
            {
                Vector3 start=Grip,best=start;float ce=cost();
                foreach(float sign in new[]{-1f,1f}){Grip=start;Grip[a]+=sign*step*.0005f;float e=cost();if(e<ce){ce=e;best=Grip;}}
                Grip=best;changed|=best!=start;
                start=Axis;best=start;ce=cost();
                foreach(float sign in new[]{-1f,1f}){Axis=Quaternion.AngleAxis(sign*step,a==0?Vector3.right:a==1?Vector3.up:Vector3.forward)*start;float e=cost();if(e<ce){ce=e;best=Axis;}}
                Axis=best;changed|=best!=start;
            }
            if(!changed)break;
        }
        pose();wand.localPosition=Grip;wand.localRotation=Quaternion.LookRotation(Axis,Vector3.up);
        for(int f=0;f<5;f++)report.AppendLine(names[f]+": original-local flexion delta="+curls[f].ToString("F2")+(f==4?" thumb opposition="+thumbAim.ToString("F2"):"; sideways rotation preserved"));
        report.AppendLine("Handle fit to natural finger cavity; axis="+Axis.ToString("F5"));
    }



    static void Rotation(AnimationClip clip,Transform bone,Transform root)
    {
        string path=AnimationUtility.CalculateTransformPath(bone,root);
        foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path==path&&(b.propertyName.Contains("Rotation")||b.propertyName.Contains("Euler"))))AnimationUtility.SetEditorCurve(clip,b,null);
        for(int i=0;i<4;i++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[i]),AnimationCurve.Constant(0,clip.length,bone.localRotation[i]));
    }

    [MenuItem("Tools/Basic Wand/Apply Idle Only %#j")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode only");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/PlayerTest.unity")throw new InvalidOperationException("Open PlayerTest first; this tool never opens/reloads a scene.");
        var wands=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="BasicWand").ToArray();
        if(wands.Length==0)throw new InvalidOperationException("No BasicWand instance");
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Source);
        var baseline=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Player/Animations/WizardLocomotion.controller");
        var copy=UnityEngine.Object.Instantiate(wands[0].root.gameObject);copy.hideFlags=HideFlags.HideAndDontSave;
        var report=new StringBuilder();
        try
        {
            var wizard=Find(copy,"Wizard");source.SampleAnimation(wizard.gameObject,0);
            var hand=Find(copy,"RightHand");var wand=Find(copy,"BasicWand");
            NaturalGrip(copy,hand,wand,report);
            var forearm=Find(copy,"RightForeArm");var arm=Find(copy,"RightArm");
            Vector3 armLine=(forearm.position-arm.position).normalized;
            // Continue the upper arm in a straight line without adding axial roll.
            Vector3 down=armLine;
            forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,down)*forearm.rotation;
            Quaternion wrist=hand.localRotation;
            hand.localRotation=new Quaternion(0,wrist.y,0,wrist.w).normalized;
            var bones=hand.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Right")&&t!=hand).Concat(new[]{forearm,hand}).ToArray();
            var clip=UnityEngine.Object.Instantiate(source);clip.name="WizardCombatIdle_BasicWandGrip";
            foreach(var bone in bones)Rotation(clip,bone,wizard);
            clip.EnsureQuaternionContinuity();
            string path=Folder+"/"+clip.name+".anim";
            var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(saved){EditorUtility.CopySerialized(clip,saved);UnityEngine.Object.DestroyImmediate(clip);clip=saved;EditorUtility.SetDirty(clip);}else AssetDatabase.CreateAsset(clip,path);
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(Folder+"/BasicWandGrip.overrideController");
            if(!controller){controller=new AnimatorOverrideController();AssetDatabase.CreateAsset(controller,Folder+"/BasicWandGrip.overrideController");}
            controller.runtimeAnimatorController=baseline;
            var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();controller.GetOverrides(overrides);
            controller.ApplyOverrides(overrides.Select(p=>new KeyValuePair<AnimationClip,AnimationClip>(p.Key,p.Key==source?clip:null)).ToList());
            EditorUtility.SetDirty(controller);
            foreach(var instance in wands)
            {
                var player=instance.root.gameObject;var animator=Find(player,"Wizard").GetComponent<Animator>();
                // Save the baseline in the scene: an initially inactive/hidden wand cannot affect other weapons.
                Undo.RecordObject(animator,"Basic Wand idle only");animator.runtimeAnimatorController=baseline;PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                foreach(var bone in bones)
                {
                    var target=Find(player,bone.name);Undo.RecordObject(target,"Basic Wand idle pose");target.localRotation=bone.localRotation;PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
                Undo.RecordObject(instance,"Basic Wand grip alignment");instance.localPosition=wand.localPosition;instance.localRotation=wand.localRotation;PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                var owner=instance.GetComponent<BasicWandIdlePose>();if(!owner)owner=Undo.AddComponent<BasicWandIdlePose>(instance.gameObject);
                Undo.RecordObject(owner,"Basic Wand idle ownership");owner.Configure(animator,baseline,controller,instance.GetComponentInChildren<MeshRenderer>());EditorUtility.SetDirty(owner);
                report.AppendLine(player.name+": elbow bend="+Vector3.Angle(armLine,down).ToString("F2")+" deg; wrist longitudinal bend="+Vector3.Angle(hand.up,down).ToString("F2")+" deg; wand/down="+Vector3.Angle(instance.forward,-player.transform.up).ToString("F2")+" deg");
            }
            report.AppendLine("Wand socket position="+wand.localPosition.ToString("F6")+" rotation Euler="+wand.localEulerAngles.ToString("F3")+" quaternion="+wand.localRotation.ToString("F6"));
            report.AppendLine("Only WizardCombatIdle is overridden. Existing unused movement/cast copies preserved, unreferenced. No Play Mode, no new scene.");
            Directory.CreateDirectory("Docs/BasicWand/Idle");File.WriteAllText("Docs/BasicWand/Idle/fit-report.txt",report.ToString());
            UnityEngine.Object.DestroyImmediate(copy);copy=null;
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Validate();Debug.Log("BASIC_WAND_IDLE_APPLY_OK");
        }
        finally { if(copy)UnityEngine.Object.DestroyImmediate(copy); }
    }

    [MenuItem("Tools/Basic Wand/Validate Idle Only")]
    public static void Validate()
    {
        var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Source);
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/WizardCombatIdle_BasicWandGrip.anim");
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(Folder+"/BasicWandGrip.overrideController");
        var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();controller.GetOverrides(pairs);
        if(pairs.Count(p=>p.Value&&p.Value!=p.Key)!=1||controller[source]!=clip)throw new Exception("Only idle must be replaced");
        var report=new StringBuilder("Idle-only override: PASS\n");
        foreach(var binding in AnimationUtility.GetCurveBindings(source))
        {
            bool allowed=binding.path.Contains("/RightHand")||binding.path.EndsWith("/RightForeArm");
            if(allowed&&(binding.propertyName.Contains("Rotation")||binding.propertyName.Contains("Euler")))continue;
            var a=AnimationUtility.GetEditorCurve(source,binding);var b=AnimationUtility.GetEditorCurve(clip,binding);
            if(b==null||!a.keys.SequenceEqual(b.keys)||a.preWrapMode!=b.preWrapMode||a.postWrapMode!=b.postWrapMode)throw new Exception("Unrelated curve changed: "+binding.path+" "+binding.propertyName);
        }
        foreach(var b in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path.Contains("/RightHand")||b.path.EndsWith("/RightForeArm")))
        {
            var c=AnimationUtility.GetEditorCurve(clip,b);
            if(b.propertyName.Contains("Rotation")&&(Mathf.Abs(c.Evaluate(0)-c.Evaluate(clip.length))>1e-6f||Mathf.Abs(c.keys[0].outTangent)>1e-6f||Mathf.Abs(c.keys[c.length-1].inTangent)>1e-6f))throw new Exception("Pose seam: "+b.path);
        }
        if(source.length!=clip.length||JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(source))!=JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(clip)))throw new Exception("Timing/settings changed");
        if(!AnimationUtility.GetAnimationEvents(source).Select(JsonUtility.ToJson).SequenceEqual(AnimationUtility.GetAnimationEvents(clip).Select(JsonUtility.ToJson)))throw new Exception("Events changed");
        report.AppendLine("Unrelated curves, duration, loop settings, events: PASS");
        report.AppendLine("Right forearm/wrist/finger start=end and zero endpoint tangents: PASS");
        report.AppendLine("Movement and attack resolve to original clips: PASS");
        var originalPlayer=EditorSceneManager.GetActiveScene().GetRootGameObjects().First(g=>g.name=="Player");
        var probe=UnityEngine.Object.Instantiate(originalPlayer);probe.hideFlags=HideFlags.HideAndDontSave;
        AnimatorOverrideController other=null;
        try
        {
            var wand=Find(probe,"BasicWand");var owner=wand.GetComponent<BasicWandIdlePose>();
            var animator=Find(probe,"Wizard").GetComponent<Animator>();var model=wand.GetComponentInChildren<MeshRenderer>();
            var baseline=controller.runtimeAnimatorController;
            if(animator.runtimeAnimatorController!=baseline)throw new Exception("Scene must store baseline controller");
            owner.SendMessage("OnEnable");
            if(animator.runtimeAnimatorController!=controller)throw new Exception("Visible wand did not apply idle");
            model.enabled=false;owner.SendMessage("Update");
            if(animator.runtimeAnimatorController!=baseline)throw new Exception("Hidden wand did not restore baseline");
            model.enabled=true;owner.SendMessage("Update");
            owner.SendMessage("OnDisable");
            if(animator.runtimeAnimatorController!=baseline)throw new Exception("Disabled wand did not restore baseline");
            other=new AnimatorOverrideController(baseline);animator.runtimeAnimatorController=other;
            owner.SendMessage("Update");owner.SendMessage("OnDisable");
            if(animator.runtimeAnimatorController!=other)throw new Exception("Another weapon's controller was overwritten");
            report.AppendLine("Edit-mode controller checks (visible, hidden, disabled, external owner): PASS; no Play Mode");
        }
        finally { UnityEngine.Object.DestroyImmediate(probe);if(other)UnityEngine.Object.DestroyImmediate(other); }
        File.WriteAllText("Docs/BasicWand/Idle/validation.txt",report.ToString());Debug.Log("BASIC_WAND_IDLE_VALIDATE_OK");
    }
}
