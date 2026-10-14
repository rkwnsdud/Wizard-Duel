#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class WizardMigrationValidation
{
    const string AnimDir="Assets/Player/Animations/WizardAnimated/";
    const string Output="Logs/WizardMigration/";
    public static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    static Transform Bone(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    public static void Validate()
    {
        Directory.CreateDirectory(Output);
        var report=new StringBuilder();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Animations/WizardLocomotion.controller");
        Require(controller.parameters.Select(p=>p.name).OrderBy(n=>n).SequenceEqual(new[]{"Attack","MoveSpeed","MoveX","MoveZ"}.OrderBy(n=>n)),"Parameters changed");
        var original=AnimationUtility.GetAnimationEvents(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/Wizard_StaffCast.anim"));
        var events=AnimationUtility.GetAnimationEvents(AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+"WizardStaffCast.anim"));
        Require(original.Length==events.Length,"Event count changed");
        for(int i=0;i<events.Length;i++)
        {
            var a=original[i]; var b=events[i];
            Require(a.time==b.time && a.functionName==b.functionName && a.stringParameter==b.stringParameter && a.floatParameter==b.floatParameter && a.intParameter==b.intParameter && a.objectReferenceParameter==b.objectReferenceParameter && a.messageOptions==b.messageOptions,"Event payload changed");
            report.AppendLine($"EVENT {b.functionName} time={b.time} string='{b.stringParameter}' float={b.floatParameter} int={b.intParameter} object={b.objectReferenceParameter} options={b.messageOptions}");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        foreach(var name in new[]{"Player","Player-1","Player-2"})
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/"+name+".prefab");
            var go=UnityEngine.Object.Instantiate(asset); go.transform.position=Vector3.zero; go.transform.rotation=Quaternion.identity;
            var wizard=go.transform.Find("Wizard"); Require(wizard && wizard.parent==go.transform,"Wizard is not direct child");
            Require(go.GetComponentsInChildren<Animator>(true).Length==1,"Duplicate Animator");
            var animator=wizard.GetComponent<Animator>(); Require(animator && animator.avatar && animator.avatar.isValid && !animator.applyRootMotion,"Invalid avatar or root motion");
            Require(wizard.GetComponent<WizardAnimationEvent>(),"Missing event receiver");
            foreach(var type in new[]{typeof(PlayerMovement),typeof(PlayerAim),typeof(PlayerAttack),typeof(PlayerHealth),typeof(CharacterController),typeof(AudioSource)})
                Require(go.GetComponent(type),"Missing root component "+type.Name);
            var socket=Bone(go,"WeaponSocket"); Require(socket.parent.name=="RightHand","Socket is not under RightHand");
            var weapon=socket.Find("LightningStaff").GetComponent<LightningWeapon>();
            var attack=new SerializedObject(go.GetComponent<PlayerAttack>()); var w=new SerializedObject(weapon);
            Require(attack.FindProperty("currentWeapon").objectReferenceValue==weapon,"currentWeapon changed");
            Require(w.FindProperty("firePoint").objectReferenceValue==weapon.transform.Find("FirePoint"),"FirePoint changed");
            Require(w.FindProperty("audioSource").objectReferenceValue==go.GetComponent<AudioSource>(),"Audio reference changed");
            Require(w.FindProperty("boltPrefab").objectReferenceValue && w.FindProperty("thunderSound").objectReferenceValue,"Missing weapon resource");
            Require(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==1,"Unexpected body renderer count");
            var renderer=go.GetComponentInChildren<SkinnedMeshRenderer>();
            Require(renderer.sharedMaterial.shader.name=="Universal Render Pipeline/Lit","Body material not URP");
            if(name!="Player") Require(PrefabUtility.GetPrefabAssetType(asset)==PrefabAssetType.Variant,"Variant inheritance lost");
            if(name=="Player-2") Require(renderer.sharedMaterial.name=="Wizard_Red","Red variant missing");
            report.AppendLine($"PREFAB {name}: components/references/one Animator/one body/variant OK; avatar={animator.avatar.name}, human={animator.avatar.isHuman}; material={renderer.sharedMaterial.name}; modelScale={wizard.Find("WizardModel").localScale.x:F6}");
            foreach(var clipName in new[]{"WizardCombatIdle","WizardWalkForward","WizardWalkBackward","WizardWalkLeft","WizardWalkRight","WizardStaffCast"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+clipName+".anim");
                foreach(var b in AnimationUtility.GetCurveBindings(clip)) Require(wizard.Find(b.path),"Unbound animation path: "+b.path);
            }
            AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+"WizardCombatIdle.anim").SampleAnimation(wizard.gameObject,0);
            WizardMigration.Preview(wizard.gameObject,name+"-idle",new Vector3(3,1.7f,5));
            if(name=="Player")
            {
                foreach(var entry in new[]{("WizardWalkForward",Vector3.forward),("WizardWalkBackward",Vector3.back),("WizardWalkLeft",Vector3.left),("WizardWalkRight",Vector3.right)})
                    FootMetrics(wizard.gameObject,entry.Item1,entry.Item2,report);
                foreach(var entry in new[]{("WizardCombatIdle",0f,"idle"),("WizardWalkForward",0.267f,"walk"),("WizardStaffCast",0.2f,"cast"),("WizardWalkLeft",0.25f,"left"),("WizardWalkRight",0.25f,"right")})
                {
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+entry.Item1+".anim").SampleAnimation(wizard.gameObject,entry.Item2);
                    WizardMigration.Preview(wizard.gameObject,entry.Item3+"-front",new Vector3(3,1.7f,5));
                    WizardMigration.Preview(wizard.gameObject,entry.Item3+"-back",new Vector3(-3,1.7f,-5));
                    if(entry.Item3=="cast")
                    {
                        var fire=weapon.transform.Find("FirePoint");
                        float dot=Vector3.Dot(fire.forward,go.transform.forward);
                        report.AppendLine($"CAST at 0.2s: FirePoint={fire.position:F5}; staff axis dot aim={dot:F6}");
                        Require(dot>0.98f,"Staff does not point along aim at release");
                        WizardMigration.Preview(wizard.gameObject,"cast-grip",new Vector3(1.4f,1.6f,2),true);
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(go);
        }
        report.AppendLine("EDIT_VALIDATION_OK");
        File.WriteAllText(Output+"validation.txt",report.ToString());
        Debug.Log("WIZARD_EDIT_VALIDATION_OK");
    }

    static void FootMetrics(GameObject wizard,string name,Vector3 direction,StringBuilder report)
    {
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir+name+".anim");
        var feet=new[]{Bone(wizard,"LeftToeBase"),Bone(wizard,"RightToeBase")};
        const int count=120; var samples=new Vector3[2,count+1];
        float minBody=float.PositiveInfinity;
        var renderer=wizard.GetComponentInChildren<SkinnedMeshRenderer>(); var mesh=new Mesh();
        for(int i=0;i<=count;i++)
        {
            clip.SampleAnimation(wizard,clip.length*i/count);
            for(int f=0;f<2;f++) samples[f,i]=feet[f].position;
            if(i%10==0)
            {
                renderer.BakeMesh(mesh);
                minBody=Mathf.Min(minBody,mesh.vertices.Min(v=>(renderer.transform.position+renderer.transform.rotation*v).y));
            }
        }
        var slip=new List<float>(); var strideSpeed=new List<float>();
        for(int f=0;f<2;f++)
        {
            float minY=Enumerable.Range(0,count+1).Min(i=>samples[f,i].y);
            for(int i=1;i<=count;i++)
            {
                Vector3 velocity=(samples[f,i]-samples[f,i-1])/(clip.length/count);
                if(samples[f,i].y>minY+0.03f || Vector3.Dot(velocity,direction)>=0) continue;
                velocity.y=0; slip.Add((5*direction+1.35f*velocity).magnitude); strideSpeed.Add(-Vector3.Dot(velocity,direction));
            }
        }
        report.AppendLine($"WALK {name}: duration={clip.length:F6}s, rate=1.35, contact samples={slip.Count}, estimated contact sliding at root 5m/s mean={(slip.Count>0?slip.Average():-1):F3}m/s; nominal planted-foot speed at 1x={(strideSpeed.Count>0?strideSpeed.Average():-1):F3}m/s; min sampled bodyY={minBody:F4}m; toe loop error L={Vector3.Distance(samples[0,0],samples[0,count]):F6}, R={Vector3.Distance(samples[1,0],samples[1,count]):F6}");
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    [InitializeOnLoadMethod]
    static void RegisterPlayValidation()
    {
        EditorApplication.playModeStateChanged-=PlayState;
        EditorApplication.playModeStateChanged+=PlayState;
    }
    public static void PlayValidate()
    {
        Directory.CreateDirectory(Output);
        const string baseline="Assets/WizardMigrationEditor/OriginalPlayer.prefab";
        if(!File.Exists(baseline) && File.Exists(Output+"Player.prefab")) { File.Copy(Output+"Player.prefab",baseline); AssetDatabase.ImportAsset(baseline); }
        if(File.Exists(Output+"play-pass.txt")) File.Move(Output+"play-pass.txt",Output+"play-pass-previous-"+DateTime.UtcNow.Ticks+".txt");
        SessionState.SetBool("WizardMigration.PlayValidation",true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    static void PlayState(PlayModeStateChange state)
    {
        if(!SessionState.GetBool("WizardMigration.PlayValidation",false)) return;
        if(state==PlayModeStateChange.EnteredPlayMode) new GameObject("WizardValidation").AddComponent<WizardMigrationPlayProbe>();
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool("WizardMigration.PlayValidation",false);
            EditorApplication.Exit(File.Exists(Output+"play-pass.txt") ? 0 : 1);
        }
    }
}

public sealed class WizardMigrationEventProbe : MonoBehaviour
{
    public int fires,ends;
    public Vector3 lastFirePosition;
    public void FireMagic(){fires++;lastFirePosition=GetComponentsInChildren<Transform>().Single(t=>t.name=="FirePoint").position;}
    public void EndAttack(){ends++;}
}

public sealed class WizardMigrationPlayProbe : MonoBehaviour
{
    readonly StringBuilder report=new StringBuilder();
    readonly HashSet<int> bolts=new HashSet<int>();
    Keyboard keyboard;
    InputSettings originalSettings;
    Transform playerRoot;
    Mouse mouse;
    Vector2 mousePosition;
    void Update()
    {
        foreach(var bolt in FindObjectsByType<LightningBolt>(FindObjectsSortMode.None))
        {
            if(!bolts.Add(bolt.GetInstanceID())) continue;
            var receiver=playerRoot.Find("Wizard").GetComponent<WizardMigrationEventProbe>();
            Check(Vector3.Dot(bolt.transform.forward,playerRoot.forward)>0.999f,"Real bolt follows Player aim");
            Check(Vector3.Distance(bolt.transform.position,receiver.lastFirePosition)<0.02f,"Real bolt starts at FirePoint");
        }
    }
    IEnumerator Start()
    {
        Time.captureFramerate=60;
        Application.runInBackground=true;
        originalSettings=InputSystem.settings;
        InputSystem.settings=Instantiate(originalSettings);
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
        camera.transform.position=new Vector3(0,5,-5); camera.transform.LookAt(Vector3.zero);
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale=Vector3.one*20;
        keyboard=InputSystem.AddDevice<Keyboard>();
        mouse=InputSystem.AddDevice<Mouse>();
        int baselineErrors=0;
        void BaselineLog(string message,string stack,LogType type) { if(message.Contains("LightningPreview requires a PreviewVisual")) baselineErrors++; }
        Application.logMessageReceived+=BaselineLog;
        var baselineAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/WizardMigrationEditor/OriginalPlayer.prefab");
        if(baselineAsset) { var baseline=Instantiate(baselineAsset); DestroyImmediate(baseline); }
        Application.logMessageReceived-=BaselineLog;
        report.AppendLine(baselineAsset ? "BASELINE Original Player PreviewVisual missing-reference errors="+baselineErrors : "BASELINE not repeated: original prefab snapshot is unavailable");
        var player=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Player.prefab"));
        playerRoot=player.transform;
        player.transform.SetPositionAndRotation(new Vector3(0,0.05f,0),Quaternion.identity);
        player.GetComponent<PlayerAim>().enabled=false;
        var animator=player.transform.Find("Wizard").GetComponent<Animator>();
        var events=animator.gameObject.AddComponent<WizardMigrationEventProbe>();
        var attack=player.GetComponent<PlayerAttack>();
        for(int i=0;i<20;i++) yield return null;
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Initial Idle state");
        var directions=new[]{new[]{Key.W},new[]{Key.S},new[]{Key.A},new[]{Key.D},new[]{Key.W,Key.A},new[]{Key.W,Key.D},new[]{Key.S,Key.A},new[]{Key.S,Key.D}};
        foreach(var keys in directions)
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
            keyboard.MakeCurrent();
            InputSystem.Update();
            for(int i=0;i<12;i++) yield return null;
            var start=player.transform.position;
            for(int i=0;i<30;i++) yield return null;
            Vector3 displacement=player.transform.position-start; displacement.y=0;
            Check(Mathf.Abs(displacement.magnitude-2.5f)<0.15f,"5m/s movement "+string.Join("+",keys)+" distance="+displacement.magnitude.ToString("F3"));
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"),"Walk state "+string.Join("+",keys));
            report.AppendLine($"DIRECTION {string.Join("+",keys)} delta={displacement:F3} MoveX={animator.GetFloat("MoveX"):F3} MoveZ={animator.GetFloat("MoveZ"):F3}");
        }
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        for(int i=0;i<20;i++) yield return null;
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),"Walk -> Idle");
        var aim=player.GetComponent<PlayerAim>(); aim.enabled=true;
        foreach(var direction in new[]{Vector3.forward,Vector3.right})
        {
            mousePosition=camera.WorldToScreenPoint(player.transform.position+direction*5);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=mousePosition}); mouse.MakeCurrent(); InputSystem.Update();
            for(int i=0;i<5;i++) yield return null;
            Check(Vector3.Angle(player.transform.forward,direction)<1,"Mouse aim "+direction);
        }
        aim.enabled=false;
        for(int n=0;n<2;n++)
        {
            if(n==1)
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                for(int i=0;i<15;i++) yield return null;
            }
            player.transform.rotation=Quaternion.Euler(0,n==0 ? 0 : 90,0);
            int before=bolts.Count;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=mousePosition,buttons=1}); InputSystem.Update();
            for(int i=0;i<12;i++) yield return null;
            Check(!attack.IsAttacking,"Charge held before release "+n);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=mousePosition,buttons=0}); InputSystem.Update();
            Check(attack.IsAttacking,"Attack committed "+n);
            for(int i=0;i<10;i++) yield return null;
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Cast"),"Entered Cast "+n);
            var position=player.transform.position;
            for(int i=0;i<20;i++) yield return null;
            Check(Vector3.Distance(position,player.transform.position)<0.01f,"Movement held during Cast "+n);
            for(int i=0;i<40;i++) yield return null;
            Check(!attack.IsAttacking,"EndAttack released flag "+n);
            Check(events.fires==n+1 && events.ends==n+1,"Exactly one fire/end event per attack "+n);
            Check(bolts.Count-before==1,"Exactly one real LightningBolt instantiated "+n);
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName(n==0 ? "Idle" : "Walk"),"Cast -> locomotion "+n);
            report.AppendLine($"ATTACK {n}: fires={events.fires} ends={events.ends} total unique bolts={bolts.Count}");
        }
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        Check(!animator.applyRootMotion && animator.avatar.isValid,"Root motion off and valid avatar at runtime");
        File.WriteAllText("Logs/WizardMigration/play-pass.txt",report+"PLAY_VALIDATION_OK\n");
        Debug.Log("WIZARD_PLAY_VALIDATION_OK");
        InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse); Time.captureFramerate=0; EditorApplication.ExitPlaymode();
    }
    void OnDestroy() { if(originalSettings) InputSystem.settings=originalSettings; }
    void Check(bool condition,string message)
    {
        report.AppendLine((condition ? "PASS " : "FAIL ")+message);
        if(condition) return;
        File.WriteAllText("Logs/WizardMigration/play-fail.txt",report.ToString());
        Debug.LogError(message); StopAllCoroutines();
        if(keyboard!=null) InputSystem.RemoveDevice(keyboard);
        Time.captureFramerate=0; EditorApplication.ExitPlaymode();
        throw new Exception(message);
    }
}
#endif
