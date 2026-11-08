using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BasicWandGripReview
{
    const string Output="Docs/BasicWand/Idle/Review";
    static Transform Find(GameObject g,string n)=>g.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);
    static bool HandBone(string n)=>n=="RightHand"||new[]{"Index","Middle","Ring","Little","Thumb"}.Any(f=>n.StartsWith("Right"+f));

    public static void RenderCurrentDummy()
    {
        var dummy=EditorSceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Dummy Player");
        Render(dummy,"Dummy-Player-actual-preview",false,false);
    }

    public static void RenderApplied()
    {
        foreach(var actor in EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(g=>g.name=="Player"||g.name=="Dummy Player"))
        {
            var copy=UnityEngine.Object.Instantiate(actor);copy.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var renderer=Find(copy,"BasicWand").GetComponentInChildren<MeshRenderer>(true);
                for(var t=renderer.transform;t!=null;t=t.parent)t.gameObject.SetActive(true);
                renderer.enabled=true;
                var owner=Find(copy,"Wizard").GetComponent<BasicWandGripSkin>();owner.enabled=true;owner.RefreshSkin();
                Render(copy,actor.name.Replace(" ","-")+"-applied");
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
    }

    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode only; Play Mode is never started or stopped by this tool.");
        Directory.CreateDirectory(Output);var report=new StringBuilder();
        var scene=EditorSceneManager.GetActiveScene();
        var actors=scene.GetRootGameObjects().Where(g=>g.name=="Player"||g.name=="Dummy Player").ToArray();
        foreach(var actor in actors)
        {
            report.AppendLine(actor.name+" current scene:");
            var rightHand=Find(actor,"RightHand");
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=skin.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var poses=mesh.bindposes;
                var rows=new System.Collections.Generic.List<System.Tuple<float,string>>();
                for(int i=0;i<vertices.Length;i++)
                {
                    var w=weights[i];var ids=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var values=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                    if(!ids.Where((id,k)=>values[k]>0).Any(id=>HandBone(skin.bones[id].name)))continue;
                    Vector3 world=Vector3.zero;var desc=new StringBuilder();
                    for(int k=0;k<4;k++)if(values[k]>0)
                    {
                        var p=poses[ids[k]].MultiplyPoint3x4(vertices[i]);world+=skin.bones[ids[k]].TransformPoint(p)*values[k];desc.Append(skin.bones[ids[k]].name+" weight="+values[k]+" bindLocal="+p.ToString("F5")+"; ");
                    }
                    var point=rightHand.InverseTransformPoint(world);rows.Add(Tuple.Create(point.y,"vertex "+i+" hand="+point.ToString("F5")+" "+desc));
                }
                foreach(var row in rows.OrderBy(r=>r.Item1).Take(10))report.AppendLine(row.Item2);
                File.WriteAllLines(Output+"/"+actor.name.Replace(" ","-")+"-vertices.txt",rows.Select(r=>r.Item2));
            }
            foreach(var t in actor.GetComponentsInChildren<Transform>(true).Where(t=>HandBone(t.name)))
            {
                var original=PrefabUtility.GetCorrespondingObjectFromOriginalSource(t);
                report.AppendLine(t.name+" localPosition="+t.localPosition.ToString("F6")+" localScale="+t.localScale.ToString("F6")+" localEuler="+t.localEulerAngles.ToString("F3")+" sourcePosition="+(original?original.localPosition.ToString("F6"):"none")+" sourceEuler="+(original?original.localEulerAngles.ToString("F3"):"none"));
            }
            Render(actor,actor.name.Replace(" ","-")+"-scene");
            Render(actor,actor.name.Replace(" ","-")+"-labels",true);
            var copy=UnityEngine.Object.Instantiate(actor);copy.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var wizard=Find(copy,"Wizard");
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim").SampleAnimation(wizard.gameObject,0);
                Render(copy,actor.name.Replace(" ","-")+"-original");
                AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Weapons/Basic Wand/Animations/WizardCombatIdle_BasicWandGrip.anim").SampleAnimation(wizard.gameObject,0);
                report.AppendLine(actor.name+" sampled idle middle:");
                foreach(var t in Find(copy,"RightMiddleProximal").GetComponentsInChildren<Transform>())report.AppendLine(t.name+" localEuler="+t.localEulerAngles.ToString("F3"));
                Render(copy,actor.name.Replace(" ","-")+"-idle");
                foreach(var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var originalMesh=skin.sharedMesh;var weights=originalMesh.boneWeights;var verts=originalMesh.vertices;
                    if(verts.Length<2200||skin.bones[weights[2].boneIndex0].name!="RightRingDistal")continue;
                    var tri=originalMesh.triangles;
                    for(int i=0;i<tri.Length;i+=3)if(tri[i]==2||tri[i+1]==2||tri[i+2]==2)
                        report.AppendLine("Triangle at vertex 2: "+string.Join(" / ",new[]{tri[i],tri[i+1],tri[i+2]}.Select(v=>v+":"+skin.bones[weights[v].boneIndex0].name)));
                    int middle=Array.FindIndex(skin.bones,b=>b.name=="RightMiddleDistal");var mesh=UnityEngine.Object.Instantiate(originalMesh);int changed=0;
                    for(int i=0;i<verts.Length;i++)if((verts[i]-verts[2]).sqrMagnitude<1e-12f){weights[i]=new BoneWeight{boneIndex0=middle,weight0=1};changed++;}
                    mesh.boneWeights=weights;skin.sharedMesh=mesh;
                    try{Render(copy,actor.name.Replace(" ","-")+"-weight-preview");report.AppendLine(actor.name+" weight-only preview: "+changed+" duplicated corner vertices corrected; no vertex positions or bone transforms changed.");}
                    finally{skin.sharedMesh=originalMesh;UnityEngine.Object.DestroyImmediate(mesh);}
                }

            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        File.WriteAllText(Output+"/inspection.txt",report.ToString());Debug.Log("BASIC_WAND_REVIEW_BOTH_OK");
    }



    static void Render(GameObject actor,string name,bool labels=false,bool includeHiddenWand=true)
    {
        var hand=Find(actor,"RightHand");var objects=new System.Collections.Generic.List<GameObject>();var meshes=new System.Collections.Generic.List<Mesh>();
        var materials=new System.Collections.Generic.List<Material>();
        var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;
        try
        {
            GameObject Object(string n){var g=new GameObject(n);g.hideFlags=HideFlags.HideAndDontSave;g.layer=31;objects.Add(g);return g;}
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var src=skin.sharedMesh;var verts=src.vertices;var bw=src.boneWeights;var bp=src.bindposes;
                var matrix=skin.bones.Select((b,i)=>hand.worldToLocalMatrix*b.localToWorldMatrix*bp[i]).ToArray();
                var valid=new bool[verts.Length];
                for(int i=0;i<verts.Length;i++)
                {
                    var b=bw[i];valid[i]=HandBone(skin.bones[b.boneIndex0].name);
                    verts[i]=matrix[b.boneIndex0].MultiplyPoint3x4(verts[i])*b.weight0+matrix[b.boneIndex1].MultiplyPoint3x4(verts[i])*b.weight1+matrix[b.boneIndex2].MultiplyPoint3x4(verts[i])*b.weight2+matrix[b.boneIndex3].MultiplyPoint3x4(verts[i])*b.weight3;
                }
                var mesh=new Mesh();meshes.Add(mesh);mesh.vertices=verts;mesh.uv=src.uv;mesh.subMeshCount=labels?8:src.subMeshCount;
                for(int s=0;s<(labels?8:src.subMeshCount);s++)
                {
                    var tri=labels?src.triangles:src.GetTriangles(s);var keep=new System.Collections.Generic.List<int>();
                    for(int i=0;i<tri.Length;i+=3)if(valid[tri[i]]&&valid[tri[i+1]]&&valid[tri[i+2]])
                    {
                        int group=0;var n=skin.bones[bw[tri[i]].boneIndex0].name;
                        if(labels){var names=new[]{"Index","MiddleProximal","MiddleIntermediate","MiddleDistal","Ring","Little","Thumb"};for(int k=0;k<7;k++)if(n.StartsWith("Right"+names[k]))group=k+1;if(group!=s)continue;}
                        keep.Add(tri[i]);keep.Add(tri[i+1]);keep.Add(tri[i+2]);
                    }
                    mesh.SetTriangles(keep,s);
                }
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                var g=Object("TemporaryHandReview");g.AddComponent<MeshFilter>().sharedMesh=mesh;
                if(labels)
                {
                    var palette=new[]{Color.gray,Color.cyan,Color.green,Color.yellow,Color.magenta,Color.blue,new Color(.5f,0,1),Color.red};
                    var mats=palette.Select(c=>{var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.SetColor("_BaseColor",c);materials.Add(m);return m;}).ToArray();g.AddComponent<MeshRenderer>().sharedMaterials=mats;
                }
                else g.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            }
            foreach(var filter in Find(actor,"BasicWand").GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(!includeHiddenWand&&(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy))continue;
                var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);meshes.Add(mesh);
                var m=hand.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                mesh.vertices=mesh.vertices.Select(m.MultiplyPoint3x4).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
                var g=Object("TemporaryWandReview");g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterials=filter.GetComponent<MeshRenderer>().sharedMaterials;
            }
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            var light=Object("TemporaryReviewLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(25,-50,0);
            Vector3 center=new Vector3(-.01f,.125f,.035f);
            var directions=new[]{new Vector3(-1,.1f,.2f),new Vector3(1,.1f,-.2f),new Vector3(-.2f,.1f,1)};
            for(int i=0;i<directions.Length;i++)
            {
                var cam=Object("TemporaryReviewCamera").AddComponent<Camera>();cam.cullingMask=1<<31;cam.orthographic=true;cam.orthographicSize=.115f;cam.transform.position=center+directions[i].normalized*.6f;cam.transform.LookAt(center,Vector3.up);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.38f,.40f,.43f);
                var rt=new RenderTexture(800,800,24);var tex=new Texture2D(800,800,TextureFormat.RGB24,false);var active=RenderTexture.active;
                try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,800,800),0,0);tex.Apply();File.WriteAllBytes(Output+"/"+name+"-"+i+".png",tex.EncodeToPNG());}
                finally{RenderTexture.active=active;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
            }
        }
        finally
        {
            foreach(var g in objects)if(g)UnityEngine.Object.DestroyImmediate(g);
            foreach(var m in meshes)if(m)UnityEngine.Object.DestroyImmediate(m);
            foreach(var m in materials)if(m)UnityEngine.Object.DestroyImmediate(m);
            RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;
        }
    }
}
