#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class WizardMigration
{
    const string ModelPath = "Assets/Player/Models/WizardAnimated/Wizard_Animated.fbx";
    const string PlayerPath = "Assets/Player/Player.prefab";
    const string Output = "Logs/WizardMigration/";
    const string ModelDir = "Assets/Player/Models/WizardAnimated/";
    const string AnimDir = "Assets/Player/Animations/WizardAnimated/";
    static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(ModelPath)
        .OfType<AnimationClip>().Single(c => !c.name.StartsWith("__") && c.name.Contains(name));

    public static void Inspect()
    {
        Directory.CreateDirectory(Output);
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips) { c.loopTime = true; c.loopPose = true; }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        var report = new StringBuilder();
        foreach (var c in clips) report.AppendLine($"IMPORT {c.name}: {c.firstFrame}..{c.lastFrame}");
        foreach (var c in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>())
            report.AppendLine($"CLIP {c.name}: duration={c.length}, fps={c.frameRate}, human={c.humanMotion}, curves={AnimationUtility.GetCurveBindings(c).Length}");
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>())
            report.AppendLine($"AVATAR {a.name}: valid={a.isValid}, human={a.isHuman}");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var old = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
        old.transform.position = Vector3.zero;
        var wizard = old.transform.Find("Wizard").gameObject;
        AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardIdle.anim").SampleAnimation(wizard, 0);
        Dump(old, report, "OLD_IDLE");
        UnityEngine.Object.DestroyImmediate(old);
        var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        Dump(model, report, "NEW_REST");
        Clip("Combat_Idle").SampleAnimation(model, 0);
        Dump(model, report, "NEW_IDLE");
        File.WriteAllText(Output + "inspection.txt", report.ToString());
        UnityEngine.Object.DestroyImmediate(model);
        Debug.Log("WIZARD_INSPECT_OK");
    }

    static void Dump(GameObject go, StringBuilder text, string label)
    {
        text.AppendLine(label);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            text.AppendLine($"{AnimationUtility.CalculateTransformPath(t, go.transform)} pos={t.position:F5} rot={t.rotation.eulerAngles:F3} localPos={t.localPosition:F5} localRot={t.localRotation.eulerAngles:F3} scale={t.lossyScale:F4}");
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            text.AppendLine($"RENDER {r.name} bounds={r.bounds} material={string.Join(",", r.sharedMaterials.Select(m=>m ? m.name : "NULL"))}");
    }

    static Transform Bone(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    static AnimationClip OldClip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/" + name + ".anim");
    static readonly (string oldName, string newName)[] Map = {
        ("hips", "Hips"), ("spine", "Spine"), ("neck", "neck"), ("head", "Head"),
        ("shoulder.R", "RightShoulder"), ("upper_arm.R", "RightArm"), ("forearm.R", "RightForeArm"), ("hand.R", "RightHand"),
        ("shoulder.L", "LeftShoulder"), ("upper_arm.L", "LeftArm"), ("forearm.L", "LeftForeArm"), ("hand.L", "LeftHand"),
        ("thigh.R", "RightUpLeg"), ("shin.R", "RightLeg"), ("foot.R", "RightFoot"),
        ("thigh.L", "LeftUpLeg"), ("shin.L", "LeftLeg"), ("foot.L", "LeftFoot")
    };

    static void SaveClip(AnimationClip clip, string name, bool loop)
    {
        clip.name = name;
        // Constant FBX channels need only endpoints; retain all genuinely animated keys.
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve=AnimationUtility.GetEditorCurve(clip,binding);
            if(curve.length>2 && curve.keys.Max(k=>k.value)-curve.keys.Min(k=>k.value)<0.000001f)
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,curve.keys[0].value));
        }
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlend = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var path = AnimDir + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing) { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); }
        else AssetDatabase.CreateAsset(clip, path);
    }

    static void NativeClip(AnimationClip source, string name, bool reverse = false)
    {
        var clip = new AnimationClip { frameRate = source.frameRate };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var b = binding; b.path = "WizardModel/" + binding.path;
            var curve = AnimationUtility.GetEditorCurve(source, binding);
            if (reverse)
                curve = new AnimationCurve(curve.keys.Reverse().Select(k => new Keyframe(source.length-k.time, k.value, -k.outTangent, -k.inTangent)).ToArray());
            AnimationUtility.SetEditorCurve(clip, b, curve);
        }
        SaveClip(clip, name, true);
    }

    static void Retarget(GameObject source, GameObject target, AnimationClip clip, string name, bool attack)
    {
        var targetModel = target.transform.Find("WizardModel").gameObject;
        OldClip("WizardIdle").SampleAnimation(source, 0);
        Clip("Combat_Idle").SampleAnimation(targetModel, 0);
        var oldBones = Map.Select(p=>Bone(source,p.oldName)).ToArray();
        var newBones = Map.Select(p=>Bone(target,p.newName)).ToArray();
        var oldReference = oldBones.Select(t=>t.rotation).ToArray();
        var newReference = newBones.Select(t=>t.rotation).ToArray();
        var gripAxis=Quaternion.Inverse(Bone(target,"RightHand").rotation)*(Bone(target,"RightIndexProximal").position-Bone(target,"RightLittleProximal").position).normalized;
        var transforms = targetModel.GetComponentsInChildren<Transform>(true);
        var curves = transforms.ToDictionary(t=>t, t=>Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray());
        int frames = Mathf.RoundToInt(clip.length * 60);
        for (int f=0; f<=frames; f++)
        {
            float time = clip.length * f / frames;
            OldClip("WizardIdle").SampleAnimation(source,0);
            clip.SampleAnimation(source,time);
            Clip("Combat_Idle").SampleAnimation(targetModel,0);
            float weight = attack ? Mathf.SmoothStep(0,1,Mathf.Min(time/0.12f,(clip.length-time)/0.16f)) : 1;
            for (int i=0;i<Map.Length;i++)
            {
                bool arm = Map[i].newName.Contains("Arm") || Map[i].newName.EndsWith("Hand");
                if (!attack && (arm || Map[i].newName.Contains("Shoulder"))) continue;
                Quaternion desired = oldBones[i].rotation * Quaternion.Inverse(oldReference[i]) * newReference[i];
                // Arms use anatomical segment directions: adding the old lowered-arm idle delta
                // to the supplied bent elbow would otherwise bend the new elbow twice.
                if (attack && arm)
                    desired = Quaternion.FromToRotation(newReference[i]*Vector3.up,oldBones[i].up) * newReference[i];
                if (attack && Map[i].newName=="RightHand")
                    desired=Quaternion.FromToRotation(desired*gripAxis,Vector3.forward)*desired;
                newBones[i].rotation = Quaternion.Slerp(newReference[i],desired,weight);
            }
            foreach (var t in transforms)
            {
                var c=curves[t]; var p=t.localPosition; var q=t.localRotation;
                float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w};
                for(int j=0;j<7;j++) c[j].AddKey(time,v[j]);
            }
        }
        var result = new AnimationClip { frameRate=60 };
        string[] properties={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
        foreach(var t in transforms)
        {
            for(int j=0;j<7;j++)
            {
                var curve=curves[t][j];
                for(int k=0;k<curve.length;k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(result,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(t,target.transform),typeof(Transform),properties[j]),curve);
            }
        }
        if(attack) AnimationUtility.SetAnimationEvents(result,AnimationUtility.GetAnimationEvents(clip));
        SaveClip(result,name,!attack);
    }

    static Texture2D ReadTexture(string suffix, bool linear)
    {
        string path=Directory.GetFiles(ModelDir+"textures", "*.png").Single(p=>p.EndsWith(suffix));
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable=true; importer.sRGBTexture=!linear; importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Materials()
    {
        var albedo=ReadTexture("texture_0.png",false);
        var metal=ReadTexture("_metallic.png",true);
        var rough=ReadTexture("_roughness.png",true);
        var packed=new Texture2D(metal.width,metal.height,TextureFormat.RGBA32,false,true);
        var pixels=metal.GetPixels32(); var rp=rough.GetPixels32();
        for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(pixels[i].r,0,0,(byte)(255-rp[i].r));
        packed.SetPixels32(pixels); packed.Apply();
        string packPath=ModelDir+"Wizard_MetallicSmoothness.png";
        File.WriteAllBytes(packPath,packed.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(packed);
        AssetDatabase.ImportAsset(packPath);
        var pi=(TextureImporter)AssetImporter.GetAtPath(packPath); pi.sRGBTexture=false; pi.alphaSource=TextureImporterAlphaSource.FromInput; pi.SaveAndReimport();
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(!shader) throw new Exception("URP Lit shader missing");
        var material=new Material(shader) { name="Wizard_Navy" };
        material.SetTexture("_BaseMap",albedo); material.SetColor("_BaseColor",Color.white);
        material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(packPath));
        material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.SetFloat("_Smoothness",1);
        material.SetFloat("_Cull",0);
        SaveMaterial(material,ModelDir+"Wizard_Navy.mat");
        var redTex=new Texture2D(albedo.width,albedo.height,TextureFormat.RGB24,false);
        var colors=albedo.GetPixels();
        for(int i=0;i<colors.Length;i++)
        {
            Color.RGBToHSV(colors[i],out float h,out float s,out float v);
            if(h>0.50f && h<0.76f && s>0.18f) colors[i]=Color.HSVToRGB(0.985f,s,v);
        }
        redTex.SetPixels(colors); redTex.Apply(); string redPath=ModelDir+"Wizard_RedAlbedo.png";
        File.WriteAllBytes(redPath,redTex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(redTex);
        AssetDatabase.ImportAsset(redPath);
        var red=new Material(AssetDatabase.LoadAssetAtPath<Material>(ModelDir+"Wizard_Navy.mat")) {name="Wizard_Red"};
        red.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(redPath));
        SaveMaterial(red,ModelDir+"Wizard_Red.mat");
        foreach(var texture in new[]{albedo,metal,rough})
        {
            var ti=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            ti.isReadable=false; ti.textureCompression=TextureImporterCompression.Compressed; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Material>(ModelDir+"Wizard_Navy.mat");
    }

    static void SaveMaterial(Material material,string path)
    {
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(existing) { EditorUtility.CopySerialized(material,existing); UnityEngine.Object.DestroyImmediate(material); }
        else AssetDatabase.CreateAsset(material,path);
    }

    static GameObject NewWizard(Material material)
    {
        var root=new GameObject("Wizard");
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.name="WizardModel"; model.transform.SetParent(root.transform,false);
        UnityEngine.Object.DestroyImmediate(model.GetComponent<Animator>());
        Clip("Combat_Idle").SampleAnimation(model,0);
        var renderer=model.GetComponentInChildren<SkinnedMeshRenderer>();
        renderer.sharedMaterial=material; renderer.updateWhenOffscreen=true;
        var mesh=new Mesh(); renderer.BakeMesh(mesh);
        var vertices=mesh.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();
        float min=vertices.Min(v=>v.y), max=vertices.Max(v=>v.y);
        float scale=2.15f/(max-min);
        model.transform.localScale=Vector3.one*scale;
        model.transform.localPosition=Vector3.up*(-min*scale+0.005f);
        UnityEngine.Object.DestroyImmediate(mesh);
        return root;
    }

    static void AttachStaff(GameObject target, Transform socket)
    {
        var hand=Bone(target,"RightHand");
        socket.SetParent(hand,false); socket.localPosition=Vector3.zero; socket.localRotation=Quaternion.identity; socket.localScale=Vector3.one;
        var staff=socket.Find("LightningStaff");
        staff.rotation=Quaternion.identity;
        staff.localScale=Vector3.one/hand.lossyScale.x;
        var shaft=Bone(staff.gameObject,"Black_Wood_Shaft").GetComponent<Renderer>();
        var crystal=Bone(staff.gameObject,"Lightning_Crystal").GetComponent<Renderer>();
        var renderers=staff.GetComponentsInChildren<Renderer>();
        float min=renderers.Min(r=>r.bounds.min.y), max=renderers.Max(r=>r.bounds.max.y);
        staff.localScale*=1.75f/(max-min);
        var axis=(Bone(target,"RightIndexProximal").position-Bone(target,"RightLittleProximal").position).normalized;
        staff.rotation=Quaternion.FromToRotation(Vector3.up,axis);
        var gripNames=new[]{"RightIndexProximal","RightIndexDistal","RightMiddleProximal","RightMiddleDistal","RightRingProximal","RightRingDistal","RightLittleProximal","RightLittleDistal"};
        Vector3 grip=Vector3.zero; foreach(var n in gripNames) grip+=Bone(target,n).position; grip/=gripNames.Length;
        staff.position+=grip-shaft.bounds.center;
        var fire=staff.Find("FirePoint");
        fire.position=crystal.bounds.center;
        fire.rotation=Quaternion.LookRotation(axis,Vector3.forward);
        fire.localScale=Vector3.one;
    }

    public static void Prepare()
    {
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).transform.Find("Wizard/WizardModel"))
            throw new InvalidOperationException("Player is already migrated. Prepare requires the original skeleton; use Validate after applying.");
        Directory.CreateDirectory(AnimDir); AssetDatabase.Refresh();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var material=AssetDatabase.LoadAssetAtPath<Material>(ModelDir+"Wizard_Navy.mat") ?? Materials();
        var old=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath)); old.transform.position=Vector3.zero;
        var source=old.transform.Find("Wizard").gameObject;
        var target=NewWizard(material);
        NativeClip(Clip("Combat_Idle"),"WizardCombatIdle");
        NativeClip(Clip("Walk_InPlace"),"WizardWalkForward");
        NativeClip(Clip("Walk_InPlace"),"WizardWalkBackward",true);
        Retarget(source,target,OldClip("Wizard_StaffCast"),"WizardStaffCast",true);
        Retarget(source,target,OldClip("WizardWalkLeft"),"WizardWalkLeft",false);
        Retarget(source,target,OldClip("WizardWalkRight"),"WizardWalkRight",false);
        AssetDatabase.SaveAssets();
        Clip("Combat_Idle").SampleAnimation(target.transform.Find("WizardModel").gameObject,0);
        var socket=Bone(old,"WeaponSocket"); AttachStaff(target,socket);
        UnityEngine.Object.DestroyImmediate(old);
        Preview(target,"idle",new Vector3(3,1.7f,5));
        Preview(target,"grip",new Vector3(1.4f,1.6f,2),true);
        foreach(var entry in new[]{("WizardStaffCast",0.2f,"cast"),("WizardWalkForward",0.267f,"walk"),("WizardWalkLeft",0.25f,"left")})
        {
            AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+entry.Item1+".anim").SampleAnimation(target,entry.Item2);
            Preview(target,entry.Item3,new Vector3(3,1.7f,5));
        }
        var report=new StringBuilder(); Dump(target,report,"PREPARED");
        File.WriteAllText(Output+"prepared.txt",report.ToString());
        UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("WIZARD_PREPARE_OK");
    }

    public static void Preview(GameObject target,string name,Vector3 cameraPosition,bool close=false)
    {
        // Explicit baking avoids same-frame skinning caches during offline Camera.Render.
        var bakedObjects=new List<GameObject>();
        var skinned=target.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.enabled).ToArray();
        foreach(var r in skinned)
        {
            var mesh=new Mesh(); r.BakeMesh(mesh);
            var go=new GameObject("PreviewBaked"); go.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
            r.enabled=false; bakedObjects.Add(go);
        }
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(0.6f,0.6f,0.6f);
        var light=new GameObject("PreviewLight").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2;
        light.transform.rotation=Quaternion.Euler(35,-35,0);
        var camera=new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.transform.position=cameraPosition;
        camera.transform.LookAt(close ? Bone(target,"RightMiddleProximal").position : new Vector3(0,1.1f,0));
        camera.orthographic=true; camera.orthographicSize=close ? 0.42f : 1.4f;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.12f,0.14f,0.18f);
        var rt=new RenderTexture(1000,1000,24); camera.targetTexture=rt; camera.Render();
        RenderTexture.active=rt; var image=new Texture2D(1000,1000,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1000,1000),0,0); image.Apply();
        File.WriteAllBytes(Output+name+".png",image.EncodeToPNG()); RenderTexture.active=null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(light.gameObject);
        foreach(var go in bakedObjects) { UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(go); }
        foreach(var r in skinned) r.enabled=true;
    }

    public static void Apply()
    {
        foreach(var name in new[]{"WizardCombatIdle","WizardWalkForward","WizardWalkBackward","WizardWalkLeft","WizardWalkRight","WizardStaffCast"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+name+".anim");
            if(!clip || AnimationUtility.GetCurveBindings(clip).Any(b=>!b.path.StartsWith("WizardModel")))
                throw new InvalidOperationException("Run Prepare successfully before Apply: "+name);
        }
        var material=AssetDatabase.LoadAssetAtPath<Material>(ModelDir+"Wizard_Navy.mat");
        var player=PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var wizard=player.transform.Find("Wizard").gameObject;
            if(!wizard.transform.Find("WizardModel"))
            {
            var socket=Bone(player,"WeaponSocket"); socket.SetParent(player.transform,true);
            PrefabUtility.UnpackPrefabInstance(wizard,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var child in wizard.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var prepared=NewWizard(material);
            var model=prepared.transform.Find("WizardModel"); model.SetParent(wizard.transform,false);
            UnityEngine.Object.DestroyImmediate(prepared);
            AttachStaff(wizard,socket);
            var avatar=AvatarBuilder.BuildGenericAvatar(wizard,""); avatar.name="WizardPlayerAvatar";
            if(!avatar.isValid) throw new Exception("New Generic Avatar is invalid");
            var existingAvatar=AssetDatabase.LoadAssetAtPath<Avatar>(ModelDir+"WizardPlayerAvatar.asset");
            if(existingAvatar) { EditorUtility.CopySerialized(avatar,existingAvatar); UnityEngine.Object.DestroyImmediate(avatar); avatar=existingAvatar; }
            else AssetDatabase.CreateAsset(avatar,ModelDir+"WizardPlayerAvatar.asset");
            var animator=wizard.GetComponent<Animator>(); animator.avatar=avatar; animator.applyRootMotion=false;
            if(!wizard.GetComponent<WizardAnimationEvent>()) throw new Exception("Animation event receiver missing");
            var attack=new SerializedObject(player.GetComponent<PlayerAttack>());
            if(attack.FindProperty("currentWeapon").objectReferenceValue!=socket.GetComponentInChildren<LightningWeapon>()) throw new Exception("Weapon reference changed");
            PrefabUtility.SaveAsPrefabAsset(player,PlayerPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }

        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Animations/WizardLocomotion.controller");
        var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();
        AnimationClip Load(string n)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+n+".anim");
        states.Single(s=>s.name=="Idle").motion=Load("WizardCombatIdle");
        states.Single(s=>s.name=="Cast").motion=Load("WizardStaffCast");
        var tree=(BlendTree)states.Single(s=>s.name=="Walk").motion;
        var children=tree.children;
        foreach(int i in Enumerable.Range(0,children.Length))
        {
            var p=children[i].position;
            children[i].motion=Load(p==Vector2.zero ? "WizardCombatIdle" : p.y>0 ? "WizardWalkForward" : p.y<0 ? "WizardWalkBackward" : p.x<0 ? "WizardWalkLeft" : "WizardWalkRight");
            children[i].timeScale=p==Vector2.zero ? 1 : 1.35f;
        }
        tree.children=children; EditorUtility.SetDirty(tree); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // Only Player-2's replaced body renderer overrides are migrated; networking stays inherited.
        var variant=PrefabUtility.LoadPrefabContents("Assets/Player/Player-2.prefab");
        try
        {
            var modifications=PrefabUtility.GetPropertyModifications(variant);
            // The preflight confirmed these are the old RobeRed overrides, not weapon materials.
            var oldRed=AssetDatabase.LoadAssetAtPath<Material>("Assets/Player/RobeRed.mat");
            PrefabUtility.SetPropertyModifications(variant,modifications.Where(m=>!(m.propertyPath.StartsWith("m_Materials.Array") && m.objectReference==oldRed)).ToArray());
            var renderer=variant.transform.Find("Wizard/WizardModel").GetComponentInChildren<SkinnedMeshRenderer>();
            renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(ModelDir+"Wizard_Red.mat");
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            PrefabUtility.SaveAsPrefabAsset(variant,"Assets/Player/Player-2.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(variant); }
        AssetDatabase.SaveAssets();
        Debug.Log("WIZARD_APPLY_OK");
    }

    public static void ProbeHumanoid()
    {
        const string path="Assets/WizardMigrationEditor/HumanoidProbe.fbx";
        if(File.Exists(path)) throw new InvalidOperationException("Probe already exists; inspect it before replacing it.");
        File.Copy(ModelPath,path); AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        var mapping=new Dictionary<string,string> {
            {"Hips","Hips"},{"Spine","Spine02"},{"Chest","Spine01"},{"UpperChest","Spine"},{"Neck","neck"},{"Head","Head"},
            {"LeftUpperLeg","LeftUpLeg"},{"LeftLowerLeg","LeftLeg"},{"LeftFoot","LeftFoot"},{"LeftToes","LeftToeBase"},
            {"RightUpperLeg","RightUpLeg"},{"RightLowerLeg","RightLeg"},{"RightFoot","RightFoot"},{"RightToes","RightToeBase"},
            {"LeftShoulder","LeftShoulder"},{"LeftUpperArm","LeftArm"},{"LeftLowerArm","LeftForeArm"},{"LeftHand","LeftHand"},
            {"RightShoulder","RightShoulder"},{"RightUpperArm","RightArm"},{"RightLowerArm","RightForeArm"},{"RightHand","RightHand"}
        };
        foreach(var digit in new[]{"Thumb","Index","Middle","Ring","Little"})
            foreach(var joint in new[]{"Proximal","Intermediate","Distal"})
                mapping.Add("Right"+digit+joint,"Right"+digit+joint);
        var description=importer.humanDescription;
        description.human=mapping.Select(p=>new HumanBone {humanName=HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones),p.Key)],boneName=p.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray();
        description.skeleton=model.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
        description.upperArmTwist=0.5f; description.lowerArmTwist=0.5f; description.upperLegTwist=0.5f; description.lowerLegTwist=0.5f;
        description.armStretch=0.05f; description.legStretch=0.05f; description.feetSpacing=0;
        importer.animationType=ModelImporterAnimationType.Human; importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.humanDescription=description; importer.optimizeGameObjects=false; importer.SaveAndReimport();
        var report=new StringBuilder();
        foreach(var pair in mapping) report.AppendLine(pair.Key+" -> "+pair.Value);
        foreach(var avatar in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>()) report.AppendLine($"AVATAR valid={avatar.isValid} human={avatar.isHuman}");
        foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__"))) report.AppendLine($"CLIP {clip.name} humanMotion={clip.humanMotion} duration={clip.length}");
        report.AppendLine("Original attack humanMotion="+OldClip("Wizard_StaffCast").humanMotion);
        File.WriteAllText(Output+"humanoid.txt",report.ToString());
        Debug.Log("WIZARD_HUMANOID_PROBE_OK");
    }

    public static void ResumePrepare()
    {
        ProbeHumanoid();
        Prepare();
    }

    public static void ApplyAndValidate()
    {
        Apply();
        WizardMigrationValidation.Validate();
    }

    public static void RefineAndValidate()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
        player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        var wizard=player.transform.Find("Wizard").gameObject;
        var renderer=wizard.GetComponentInChildren<SkinnedMeshRenderer>();
        var hips=Bone(wizard,"Hips"); var mesh=new Mesh();
        foreach(var name in new[]{"WizardWalkLeft","WizardWalkRight"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+name+".anim");
            var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
            int frames=Mathf.RoundToInt(clip.length*60);
            for(int i=0;i<=frames;i++)
            {
                float time=clip.length*i/frames; clip.SampleAnimation(wizard,time); renderer.BakeMesh(mesh);
                float min=mesh.vertices.Min(v=>(renderer.transform.position+renderer.transform.rotation*v).y);
                hips.position+=Vector3.up*(0.005f-min);
                var p=hips.localPosition;
                curves[0].AddKey(time,p.x); curves[1].AddKey(time,p.y); curves[2].AddKey(time,p.z);
            }
            for(int j=0;j<3;j++)
            {
                for(int k=0;k<curves[j].length;k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curves[j],k,AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curves[j],k,AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(hips,wizard.transform),typeof(Transform),"m_LocalPosition."+"xyz"[j]),curves[j]);
            }
            EditorUtility.SetDirty(clip);
        }
        AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+"WizardCombatIdle.anim").SampleAnimation(wizard,0);
        var avatar=AvatarBuilder.BuildGenericAvatar(wizard,""); avatar.name="WizardPlayerAvatar";
        if(!avatar.isValid) throw new Exception("Generic Avatar with no root-motion node is invalid");
        EditorUtility.CopySerialized(avatar,AssetDatabase.LoadAssetAtPath<Avatar>(ModelDir+"WizardPlayerAvatar.asset"));
        UnityEngine.Object.DestroyImmediate(avatar); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(player);
        var albedo=ReadTexture("texture_0.png",false); var colors=albedo.GetPixels();
        for(int i=0;i<colors.Length;i++)
        {
            Color.RGBToHSV(colors[i],out float h,out float s,out float v);
            if(h>0.50f && h<0.76f && s>0.18f) colors[i]=Color.HSVToRGB(0.985f,Mathf.Max(s,0.65f),Mathf.Min(0.85f,v*1.6f+0.10f));
        }
        var red=new Texture2D(albedo.width,albedo.height,TextureFormat.RGB24,false); red.SetPixels(colors); red.Apply();
        string redPath=ModelDir+"Wizard_RedAlbedo.png"; File.WriteAllBytes(redPath,red.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(red); AssetDatabase.ImportAsset(redPath);
        var ti=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(albedo)); ti.isReadable=false; ti.textureCompression=TextureImporterCompression.Compressed; ti.SaveAndReimport();
        AssetDatabase.SaveAssets();
        WizardMigrationValidation.Validate();
    }

    public static void RenderSequences()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
        player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        var wizard=player.transform.Find("Wizard").gameObject;
        foreach(var name in new[]{"WizardWalkForward","WizardWalkLeft","WizardWalkRight","WizardStaffCast"})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+name+".anim");
            var sheet=new Texture2D(1500,1000,TextureFormat.RGB24,false);
            for(int i=0;i<6;i++)
            {
                clip.SampleAnimation(wizard,clip.length*i/5);
                string frame=name+"-back-"+i;
                Preview(wizard,frame,new Vector3(-3,1.7f,-5));
                var image=new Texture2D(2,2); image.LoadImage(File.ReadAllBytes(Output+frame+".png"));
                var source=image.GetPixels32(); var resized=new Color32[500*500];
                for(int y=0;y<500;y++) for(int x=0;x<500;x++) resized[y*500+x]=source[y*2*1000+x*2];
                sheet.SetPixels32((i%3)*500,(1-i/3)*500,500,500,resized);
                UnityEngine.Object.DestroyImmediate(image);
            }
            sheet.Apply(); File.WriteAllBytes(Output+name+"-sequence.png",sheet.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(sheet);
        }
        UnityEngine.Object.DestroyImmediate(player);
        Debug.Log("WIZARD_SEQUENCES_OK");
    }
}
#endif
