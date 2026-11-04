using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BasicWandSkinRepair
{
    const string MeshPath="Assets/Weapons/Basic Wand/Grip/Wizard_BasicWandGrip.asset";
    static readonly int[] Corners={2,2089,2184};
    static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);

    [MenuItem("Tools/Basic Wand/Apply Approved Skin Repair")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit mode only; no Play Mode is started.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/PlayerTest.unity")throw new InvalidOperationException("Open PlayerTest; no scene is opened or reloaded by this tool.");
        var actors=scene.GetRootGameObjects().Where(g=>g.name=="Player"||g.name=="Dummy Player").ToArray();
        if(actors.Length!=2)throw new Exception("Expected Player and Dummy Player");
        Directory.CreateDirectory("Assets/Weapons/Basic Wand/Grip");AssetDatabase.Refresh();
        var report=new StringBuilder();Mesh repaired=null,source=null;string[] boneOrder=null;
        var owners=new List<BasicWandGripSkin>();
        foreach(var actor in actors)
        {
            var wizard=Find(actor,"Wizard");var animator=wizard.GetComponent<Animator>();var wand=Find(actor,"BasicWand");
            var existing=wizard.GetComponent<BasicWandGripSkin>();
            var skin=existing?existing.CharacterSkin:actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(s=>s.sharedMesh.vertexCount>2184&&s.bones.Any(b=>b.name=="RightMiddleDistal"));
            var original=existing?existing.OriginalMesh:skin.sharedMesh;
            var names=skin.bones.Select(b=>b.name).ToArray();int middle=Array.IndexOf(names,"RightMiddleDistal");
            int ring=Array.IndexOf(names,"RightRingDistal");
            if(source==null)
            {
                source=original;boneOrder=names;
                var weights=source.boneWeights;var verts=source.vertices;
                var duplicates=Enumerable.Range(0,verts.Length).Where(i=>(verts[i]-verts[2]).sqrMagnitude<1e-12f).ToArray();
                if(!duplicates.SequenceEqual(Corners))throw new Exception("Unexpected duplicate corner vertices");
                foreach(int i in Corners)if(weights[i].weight0!=1||weights[i].boneIndex0!=ring)throw new Exception("Source corner no longer has the diagnosed ring-tip weight");
                var copy=UnityEngine.Object.Instantiate(source);copy.name="Wizard_BasicWandGrip";
                foreach(int i in Corners)weights[i]=new BoneWeight{boneIndex0=middle,weight0=1};
                copy.boneWeights=weights;
                repaired=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if(repaired){EditorUtility.CopySerialized(copy,repaired);UnityEngine.Object.DestroyImmediate(copy);EditorUtility.SetDirty(repaired);}
                else{repaired=copy;AssetDatabase.CreateAsset(repaired,MeshPath);}
                ValidateMesh(source,repaired,middle,report);
                report.AppendLine("Original mesh: "+AssetDatabase.GetAssetPath(source)+" / "+source.name);
            }
            else if(source!=original||!boneOrder.SequenceEqual(names))throw new Exception("Actors do not share the same source mesh and bone order");
            var owner=existing?existing:Undo.AddComponent<BasicWandGripSkin>(wizard.gameObject);
            Undo.RecordObject(owner,"Basic Wand skin repair ownership");Undo.RecordObject(skin,"Basic Wand repaired skin");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>("Assets/Weapons/Basic Wand/Animations/BasicWandGrip.overrideController");
            owner.Configure(skin,original,repaired,wand.GetComponentInChildren<MeshRenderer>(true),animator,controller);
            EditorUtility.SetDirty(owner);PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            owners.Add(owner);
            report.AppendLine(actor.name+": repaired copy assigned; original mesh stored for restoration.");
        }
        foreach(var owner in owners)ValidateOwnership(owner,report);
        BasicWandGripReview.RenderApplied();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("Docs/BasicWand/Idle/Review");
        report.AppendLine("No original FBX, bind poses, bones, character vertex positions, UVs, materials, wand Transform, animations or events modified. No Play Mode or new scene.");
        File.WriteAllText("Docs/BasicWand/Idle/Review/applied-validation.txt",report.ToString());
        Debug.Log("BASIC_WAND_SKIN_REPAIR_APPLIED_BOTH");
    }

    static void ValidateMesh(Mesh a,Mesh b,int middle,StringBuilder report)
    {
        if(!a.vertices.SequenceEqual(b.vertices)||!a.normals.SequenceEqual(b.normals)||!a.tangents.SequenceEqual(b.tangents)||!a.colors32.SequenceEqual(b.colors32)||!a.bindposes.SequenceEqual(b.bindposes)||a.subMeshCount!=b.subMeshCount||a.blendShapeCount!=b.blendShapeCount||a.bounds!=b.bounds)throw new Exception("Mesh attributes changed");
        for(int channel=0;channel<8;channel++)
        {
            var x=new List<Vector4>();var y=new List<Vector4>();a.GetUVs(channel,x);b.GetUVs(channel,y);
            if(!x.SequenceEqual(y))throw new Exception("UV channel changed: "+channel);
        }
        for(int i=0;i<a.subMeshCount;i++)if(a.GetTopology(i)!=b.GetTopology(i)||!a.GetIndices(i).SequenceEqual(b.GetIndices(i)))throw new Exception("Topology changed");
        var before=a.boneWeights;var after=b.boneWeights;
        var changed=Enumerable.Range(0,before.Length).Where(i=>!before[i].Equals(after[i])).ToArray();
        if(!changed.SequenceEqual(Corners)||Corners.Any(i=>after[i].boneIndex0!=middle||after[i].weight0!=1))throw new Exception("Unexpected weight changes");
        report.AppendLine("Mesh validation PASS: only vertices 2, 2089, 2184 changed; RightRingDistal -> RightMiddleDistal, weight 1.0.");
        report.AppendLine("Vertex positions, normals, tangents, colors, UV0..UV7, topology, bounds, bind poses and blend shape count unchanged.");
    }

    static void ValidateOwnership(BasicWandGripSkin realOwner,StringBuilder report)
    {
        var originalActor=realOwner.transform.root.gameObject;
        // Test a hidden temporary copy so existing visibility choices are preserved.
        var actor=UnityEngine.Object.Instantiate(originalActor);actor.hideFlags=HideFlags.HideAndDontSave;
        Mesh external=null;
        try
        {
            var owner=Find(actor,"Wizard").GetComponent<BasicWandGripSkin>();
            owner.SetEditorPreviewWhenHidden(false);
            var renderer=Find(actor,"BasicWand").GetComponentInChildren<MeshRenderer>(true);
            for(var t=renderer.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
            owner.enabled=true;renderer.enabled=true;owner.RefreshSkin();var skin=owner.CharacterSkin;
            if(skin.sharedMesh!=owner.GripMesh)throw new Exception("Visible wand has no repaired mesh: "+actor.name+" active="+owner.isActiveAndEnabled+" mesh="+skin.sharedMesh.name);
            renderer.enabled=false;owner.RefreshSkin();
            if(skin.sharedMesh!=owner.OriginalMesh)throw new Exception("Hidden wand failed to restore original");
            renderer.enabled=true;owner.RefreshSkin();owner.enabled=false;
            if(skin.sharedMesh!=owner.OriginalMesh)throw new Exception("Disabled component failed to restore original");
            owner.enabled=true;owner.RefreshSkin();
            if(skin.sharedMesh!=owner.GripMesh)throw new Exception("Re-enable failed to restore grip copy");
            var wand=Find(actor,"BasicWand").gameObject;wand.SetActive(false);owner.RefreshSkin();
            if(skin.sharedMesh!=owner.OriginalMesh)throw new Exception("Inactive wand failed to restore original");
            wand.SetActive(true);owner.RefreshSkin();
            external=UnityEngine.Object.Instantiate(owner.OriginalMesh);skin.sharedMesh=external;owner.RefreshSkin();owner.enabled=false;
            if(skin.sharedMesh!=external)throw new Exception("Another mesh owner's override was overwritten");
        }
        finally{UnityEngine.Object.DestroyImmediate(actor);if(external)UnityEngine.Object.DestroyImmediate(external);}
        realOwner.RefreshSkin();
        var realRenderer=Find(originalActor,"BasicWand").GetComponentInChildren<MeshRenderer>(true);
        bool shouldRepair=realOwner.isActiveAndEnabled&&((realRenderer.enabled&&realRenderer.gameObject.activeInHierarchy)||realOwner.PreviewWhenWandHiddenInEditor);
        if(realOwner.CharacterSkin.sharedMesh!=(shouldRepair?realOwner.GripMesh:realOwner.OriginalMesh))throw new Exception("Authored actor mesh state is incorrect");
        report.AppendLine(originalActor.name+": visible / hidden / disabled / inactive / re-enabled / external override restoration checks PASS on temporary copy; authored visibility preserved. Applied scene state="+(shouldRepair?"repaired mesh":"original mesh (wand hidden or inactive)"));
    }
}
