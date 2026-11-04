using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BasicWandDummyPreviewFix
{
    [MenuItem("Tools/Basic Wand/Fix Dummy Hand Preview")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode only; no automatic Play Mode.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/PlayerTest.unity")throw new Exception("Open PlayerTest first; no scene is reloaded.");
        var actors=scene.GetRootGameObjects();var dummy=actors.Single(g=>g.name=="Dummy Player");var player=actors.Single(g=>g.name=="Player");
        var playerSkin=player.GetComponentInChildren<BasicWandGripSkin>(true);var playerMesh=playerSkin.CharacterSkin.sharedMesh;
        var playerPose=player.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t,t=>new[]{t.localPosition.x,t.localPosition.y,t.localPosition.z,t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w,t.localScale.x,t.localScale.y,t.localScale.z});
        var owner=dummy.GetComponentInChildren<BasicWandGripSkin>(true);
        var wand=dummy.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="BasicWand");var model=wand.GetComponentInChildren<MeshRenderer>(true);
        bool wandActive=wand.gameObject.activeSelf,rendererEnabled=model.enabled;
        var report=new StringBuilder();
        report.AppendLine("Dummy before: wand active="+wandActive+", model enabled="+rendererEnabled+", mesh="+owner.CharacterSkin.sharedMesh.name);
        Undo.RecordObject(owner,"Keep Dummy repaired hand visible in edit mode");Undo.RecordObject(owner.CharacterSkin,"Dummy repaired hand preview");
        owner.SetEditorPreviewWhenHidden(true);EditorUtility.SetDirty(owner);PrefabUtility.RecordPrefabInstancePropertyModifications(owner.CharacterSkin);
        if(owner.CharacterSkin.sharedMesh!=owner.GripMesh)throw new Exception("Dummy repaired mesh did not apply");
        if(wand.gameObject.activeSelf!=wandActive||model.enabled!=rendererEnabled)throw new Exception("Wand visibility changed");
        var copy=UnityEngine.Object.Instantiate(dummy);copy.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var test=copy.GetComponentInChildren<BasicWandGripSkin>(true);var w=copy.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="BasicWand");
            w.gameObject.SetActive(false);test.RefreshSkin();
            if(test.CharacterSkin.sharedMesh!=test.GripMesh)throw new Exception("Hidden-wand edit preview did not remain repaired");
            test.enabled=false;
            if(test.CharacterSkin.sharedMesh!=test.OriginalMesh)throw new Exception("Disabled preview did not restore original");
            test.enabled=true;test.SetEditorPreviewWhenHidden(false);
            if(test.CharacterSkin.sharedMesh!=test.OriginalMesh)throw new Exception("Default hidden-wand behavior changed");
            test.SetEditorPreviewWhenHidden(true);
            if(test.CharacterSkin.sharedMesh!=test.GripMesh)throw new Exception("Re-enabled preview failed");
        }
        finally{UnityEngine.Object.DestroyImmediate(copy);}
        if(playerSkin.CharacterSkin.sharedMesh!=playerMesh)throw new Exception("Player mesh changed");
        foreach(var pair in playerPose)
        {
            var t=pair.Key;var now=new[]{t.localPosition.x,t.localPosition.y,t.localPosition.z,t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w,t.localScale.x,t.localScale.y,t.localScale.z};
            if(!now.SequenceEqual(pair.Value))throw new Exception("Player transform changed: "+t.name);
        }
        // Render the actual dummy configuration, without forcing its wand visible.
        BasicWandGripReview.RenderCurrentDummy();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        report.AppendLine("Dummy after: repaired mesh="+owner.CharacterSkin.sharedMesh.name+"; hidden-wand edit preview enabled. Wand visibility preserved.");
        report.AppendLine("Hidden wand / component disable / preview disable / re-enable checks PASS on temporary copy.");
        report.AppendLine("Player mesh and every Player transform unchanged: PASS. Runtime wand/Idle checks remain in force. No Play Mode or new scene.");
        File.WriteAllText("Docs/BasicWand/Idle/Review/dummy-preview-validation.txt",report.ToString());Debug.Log("BASIC_WAND_DUMMY_PREVIEW_FIXED");
    }
}
