using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

public static class LightningStaffBuilder
{
    [MenuItem("Tools/Lightning Staff/Create URP Prefab")]
    static void Build()
    {
        string[] guids = AssetDatabase.FindAssets("LightningStaff t:Model");
        string path = null;
        foreach (var guid in guids) { string p = AssetDatabase.GUIDToAssetPath(guid); if (p.EndsWith("LightningStaff.obj", StringComparison.OrdinalIgnoreCase)) { path = p; break; } }
        if (path == null) { EditorUtility.DisplayDialog("Lightning Staff", "Copy the LightningStaff folder into Assets first.", "OK"); return; }
        string folder = Path.GetDirectoryName(path).Replace('\\','/') + "/Generated";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'), "Generated");
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mats = new Dictionary<string,Material>();
        string[] names = { "BlackWood", "Bronze", "CrystalBlue", "CrystalLight" };
        Color[] colors = { new Color(.025f,.021f,.019f),new Color(.38f,.25f,.12f),new Color(.015f,.42f,.9f),new Color(.08f,.72f,1f) };
        for (int i=0;i<names.Length;i++) {
            string mp=folder+"/"+names[i]+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(mp);
            if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,mp);}
            m.color=colors[i]; m.SetFloat("_Metallic",i==1?.55f:0f); m.SetFloat("_Smoothness",i>1?.35f:.15f);
            if(i>1){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",colors[i]*1.6f);}
            EditorUtility.SetDirty(m);mats[names[i]]=m;
        }
        var root=new GameObject("LightningStaff");
        var source=new List<Vector3>();var verts=new List<Vector3>();var indices=new List<int>();
        string objName="Part",material="BlackWood"; int part=0;
        Action flush=()=>{
            if(indices.Count==0)return;
            var mesh=new Mesh { name=objName };
            mesh.SetVertices(verts);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string meshPath=folder+"/Part_"+(part++).ToString("D2")+".asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(existing!=null){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,meshPath);
            var go=new GameObject(objName);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=mats[material]; verts.Clear();indices.Clear();
        };
        foreach(string raw in File.ReadAllLines(path)){
            string line=raw.Trim();if(line.Length==0 || line.StartsWith("#"))continue;
            string[] w=line.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
            if(w[0]=="v")source.Add(new Vector3(float.Parse(w[1],CultureInfo.InvariantCulture),float.Parse(w[2],CultureInfo.InvariantCulture),-float.Parse(w[3],CultureInfo.InvariantCulture)));
            else if(w[0]=="o"){flush();objName=w[1];}
            else if(w[0]=="usemtl"){flush();material=w[1];}
            else if(w[0]=="f"){
                int start=verts.Count;
                // Mirrored Z plus reversed winding converts the source into Unity coordinates.
                for(int k=1;k<w.Length;k++)verts.Add(source[int.Parse(w[k].Split('/')[0])-1]);
                for(int k=1;k<w.Length-2;k++){indices.Add(start);indices.Add(start+k+1);indices.Add(start+k);}
            }
        }
        flush();
        var tip=new GameObject("SpellOrigin");tip.transform.SetParent(root.transform,false);tip.transform.localPosition=new Vector3(0,.83f,-.08f);
        string prefabPath=folder+"/LightningStaff.prefab";var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
        UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();Selection.activeObject=prefab;EditorGUIUtility.PingObject(prefab);
        Debug.Log("Lightning staff prefab created: "+prefabPath);
    }
}
