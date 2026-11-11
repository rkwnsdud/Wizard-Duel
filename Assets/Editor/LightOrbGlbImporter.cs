using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

// Rebuild cached materials with missing metal texture/environment bindings.
[ScriptedImporter(18, "glb")]
public sealed class LightOrbGlbImporter : ScriptedImporter
{
    [Serializable] class Root { public Node[] nodes; public GMesh[] meshes; public Accessor[] accessors; public BufferView[] bufferViews; public Image[] images; public TextureRef[] textures; }
    [Serializable] class Node { public string name; public int mesh = -1; public float[] translation, rotation, scale; }
    [Serializable] class GMesh { public string name; public Primitive[] primitives; }
    [Serializable] class Primitive { public Attributes attributes; public int indices = -1; public int material = -1; }
    [Serializable] class Attributes { public int POSITION = -1, NORMAL = -1, TEXCOORD_0 = -1; }
    [Serializable] class Accessor { public int bufferView = -1, byteOffset, componentType, count; public string type; }
    [Serializable] class BufferView { public int byteOffset, byteLength, byteStride; }
    [Serializable] class Image { public string mimeType; public int bufferView = -1; }
    [Serializable] class TextureRef { public int source = -1; }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        ctx.DependsOnArtifact("Assets/Weapons/Light Orb/OrbVisual/LightOrbCrystal.shader");
        ctx.DependsOnArtifact("Assets/Weapons/Light Orb/OrbVisual/LightOrbMetal.shader");
        byte[] file = File.ReadAllBytes(ctx.assetPath);
        if (file.Length < 28 || BitConverter.ToUInt32(file, 0) != 0x46546c67) throw new InvalidDataException("Invalid GLB header.");
        int jsonLength = BitConverter.ToInt32(file, 12);
        string json = System.Text.Encoding.UTF8.GetString(file, 20, jsonLength).TrimEnd('\0', ' ');
        int binHeader = (20 + jsonLength + 3) & ~3;
        if (BitConverter.ToUInt32(file, binHeader + 4) != 0x004e4942) throw new InvalidDataException("GLB BIN chunk is missing.");
        int binStart = binHeader + 8;
        Root gltf = JsonUtility.FromJson<Root>(json);

        var root = new GameObject("LightOrb_Source");
        ctx.AddObjectToAsset("Root", root); ctx.SetMainObject(root);

        Texture2D albedo = ReadImage(gltf, 0, file, binStart, "Metal_Albedo", false);
        Texture2D mrSource = ReadImage(gltf, 1, file, binStart, "Metal_GLTF_MetallicRoughness", true);
        Texture2D normal = ReadImage(gltf, 2, file, binStart, "Metal_Normal", true);
        Texture2D mask = RepackMetallicSmoothness(mrSource);
        ctx.AddObjectToAsset(albedo.name, albedo); ctx.AddObjectToAsset(mrSource.name, mrSource);
        ctx.AddObjectToAsset(normal.name, normal); ctx.AddObjectToAsset(mask.name, mask);
        Material metal = MakeMetal(albedo, normal, mask); ctx.AddObjectToAsset(metal.name, metal);
        Cubemap crystalCube=MakeCrystalStudioCube(); ctx.AddObjectToAsset(crystalCube.name,crystalCube);
        metal.SetTexture("_OrbEnvironment", crystalCube);
        crystalCube=MakeCrystalStudioCube(true); ctx.AddObjectToAsset(crystalCube.name,crystalCube);
        Material[] crystals = new Material[4];
        for (int i = 0; i < 4; i++) { crystals[i] = MakeCrystal(i,crystalCube); ctx.AddObjectToAsset(crystals[i].name, crystals[i]); }

        for (int i = 0; i < gltf.nodes.Length; i++)
        {
            Node node = gltf.nodes[i]; if (node.mesh < 0) continue;
            var go = new GameObject(node.name); go.transform.SetParent(root.transform, false); ApplyTransform(go.transform, node);
            Primitive p = gltf.meshes[node.mesh].primitives[0]; Mesh mesh = BuildMesh(gltf, p, file, binStart, gltf.meshes[node.mesh].name);
            ctx.AddObjectToAsset("Mesh_" + i, mesh); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = i == 0 ? metal : crystals[i - 1];
            if (i > 0)
            {
                BakeCrystalEdges(mesh);
                Texture2D exactPlanes;
                int planeCount;
                Cubemap facets = BakeFacetPlanes(mesh, out exactPlanes, out planeCount);
                ctx.AddObjectToAsset("FacetPlanes_" + i, facets);
                ctx.AddObjectToAsset("ExactFacetPlanes_" + i, exactPlanes);
                crystals[i - 1].SetTexture("_FacetPlanes", facets);
                crystals[i - 1].SetTexture("_ExactFacetPlanes", exactPlanes);
                crystals[i - 1].SetFloat("_PlaneCount", planeCount);
                crystals[i - 1].SetVector("_VolumeCenter", mesh.bounds.center);
                crystals[i - 1].SetFloat("_VolumeRadius", mesh.bounds.extents.magnitude);
            }
            renderer.shadowCastingMode = i == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off; renderer.receiveShadows = i == 0;
        }
        float sharedRadius = crystals[0].GetFloat("_VolumeRadius");
        foreach(var material in crystals) material.SetFloat("_ReadabilityRadius",sharedRadius);
    }

    // Preserve positions/topology. Mark real facet edges, excluding coplanar
    // triangulation diagonals, for a narrow optical bevel in the shader.
    static void BakeCrystalEdges(Mesh mesh)
    {
        var vertices=mesh.vertices; var triangles=mesh.triangles;
        var adjacent=new System.Collections.Generic.Dictionary<string,Vector3>();
        var bary=new Vector3[vertices.Length]; var masks=new Vector3[vertices.Length];
        string Point(Vector3 v) => Mathf.RoundToInt(v.x*100000)+","+Mathf.RoundToInt(v.y*100000)+","+Mathf.RoundToInt(v.z*100000);
        string Edge(Vector3 a,Vector3 b) { string x=Point(a),y=Point(b); return string.CompareOrdinal(x,y)<0 ? x+"/"+y : y+"/"+x; }
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
            Vector3 n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).normalized;
            for(int e=0;e<3;e++)
            {
                string key=Edge(vertices[triangles[t+(e+1)%3]],vertices[triangles[t+(e+2)%3]]);
                if(!adjacent.ContainsKey(key)) adjacent[key]=n;
                else if(Vector3.Dot(adjacent[key],n)>.999f) adjacent[key]=Vector3.zero;
            }
        }
        for(int t=0;t<triangles.Length;t+=3)
        {
            Vector3 mask=Vector3.zero;
            for(int e=0;e<3;e++) mask[e]=adjacent[Edge(vertices[triangles[t+(e+1)%3]],vertices[triangles[t+(e+2)%3]])].sqrMagnitude>.1f?1:0;
            for(int e=0;e<3;e++) { int index=triangles[t+e]; bary[index]= e==0?Vector3.right:e==1?Vector3.up:Vector3.forward; masks[index]=mask; }
        }
        mesh.SetUVs(2,new System.Collections.Generic.List<Vector3>(bary));
        mesh.SetUVs(3,new System.Collections.Generic.List<Vector3>(masks));
    }

    static Texture2D ReadImage(Root g, int textureIndex, byte[] file, int binStart, string name, bool linear)
    {
        Image image = g.images[g.textures[textureIndex].source]; BufferView view = g.bufferViews[image.bufferView];
        byte[] encoded = new byte[view.byteLength]; Buffer.BlockCopy(file, binStart + view.byteOffset, encoded, 0, encoded.Length);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        if (!texture.LoadImage(encoded, false)) throw new InvalidDataException("Could not decode embedded " + name);
        return texture;
    }

    static Texture2D RepackMetallicSmoothness(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(pixels[i].b, 0, 0, (byte)(255 - pixels[i].g));
        var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, true) { name = "Metal_URP_MetallicSmoothness", wrapMode = source.wrapMode, filterMode = FilterMode.Trilinear };
        result.SetPixels32(pixels); result.Apply(true, false); return result;
    }

    static Material MakeMetal(Texture2D albedo, Texture2D normal, Texture2D mask)
    {
        var m = new Material(LoadOrbShader("LightOrbMetal")) { name = "PureLightOrb_Metal" };
        m.SetTexture("_BaseMap", albedo); m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
        m.SetTexture("_MetallicGlossMap", mask); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP");
        m.SetFloat("_Cull", 0f); return m;
    }

    static Material MakeCrystal(int layer,Cubemap crystalCube)
    {
        // L1 is the largest shell and L4 is the compact centre. Keeping the
        // outer shells restrained prevents four blended surfaces becoming a
        // single opaque white ball while the centre remains readable.
        float[] opacity = { .002f, .003f, .005f, .009f };
        float[] smoothness = { .91f, .93f, .952f, .975f };
        float[] reflectionStrength = { .18f, .27f, .43f, .92f };
        float[] cubeBlend = { .86f, .86f, .88f, .90f };
        // All four layers occupy the same lighting environment. Tiny offsets
        // only prevent exact overlap; they are not separate lighting rigs.
        float[] reflectionRotation = { 0f, 2.5f, -2.5f, 1f };
        var m = new Material(LoadOrbShader("LightOrbCrystal")) { name = "PureLightOrb_Crystal_L" + (layer + 1), renderQueue = 3030 - layer * 10 };
        m.SetFloat("_IOR", 1.46f);
        m.SetFloat("_OuterShellAccent", layer==0 ? 1f : 0f);
        m.SetFloat("_MiddleShellAccent", layer==1 ? 1f : 0f);
        m.SetFloat("_GameplayReadability", 1f);
        m.SetFloat("_GameplayLayerWeight", new[] { .12f,.3f,.6f,1f }[layer]);
        m.SetFloat("_RefractionStrength", new[] {.16f,.22f,.34f,.65f}[layer]);
        m.SetFloat("_SparkleStrength", new[] { .65f,.75f,.85f,1f }[layer]);
        m.SetFloat("_VolumeContribution", new[] { .12f, .18f, .30f, .72f }[layer]);
        m.SetFloat("_Absorption", .025f);
        m.SetColor("_BaseColor", new Color(.96f, .988f, 1f, 1f)); m.SetColor("_WarmColor", new Color(1f, .83f, .55f, 1f));
        m.SetColor("_EdgeColor", new Color(.72f, .9f, 1f, 1f)); m.SetFloat("_Opacity", opacity[layer]);
        m.SetFloat("_FresnelPower", 4.8f - layer * .45f); m.SetFloat("_FacetContrast", .52f + layer * .07f); m.SetFloat("_Smoothness", smoothness[layer]);
        m.SetTexture("_CrystalCube", crystalCube); m.SetFloat("_CrystalCubeBlend", cubeBlend[layer]);
        m.SetFloat("_ReflectionStrength", reflectionStrength[layer]); m.SetFloat("_ReflectionRotation", reflectionRotation[layer]);
        return m;
    }

    static Shader LoadOrbShader(string name)
    {
        string path = "Assets/Weapons/Light Orb/OrbVisual/" + name + ".shader";
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null) throw new InvalidDataException("Required orb shader failed to import: " + path);
        return shader;
    }

    // Store actual mesh facet planes, not a second visible mesh. A radial ray
    // from the centre selects the first outward plane of this convex crystal.
    // Point filtering preserves facet boundaries. No runtime mesh allocation.
    static Cubemap BakeFacetPlanes(Mesh mesh, out Texture2D exactPlanes, out int planeCount)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3 centre = mesh.bounds.center;
        var planes = new System.Collections.Generic.List<Vector4>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]] - centre;
            Vector3 b = vertices[triangles[t + 1]] - centre;
            Vector3 c = vertices[triangles[t + 2]] - centre;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, a) < 0f) n = -n;
            float distance = Vector3.Dot(n, a);
            if (distance > 0.00001f)
            {
                Vector4 plane = new Vector4(n.x,n.y,n.z,distance);
                if (!planes.Exists(p => ((Vector3)p-n).sqrMagnitude < .000001f && Mathf.Abs(p.w-distance) < .00001f))
                    planes.Add(plane);
            }
        }
        // The inner polyhedra have few planes, allowing exact convex ray exits.
        // Outer high-poly shells retain the bounded-cost directional lookup.
        planeCount = planes.Count <= 64 ? planes.Count : 0;
        exactPlanes = new Texture2D(Mathf.Max(1,planeCount),1,TextureFormat.RGBAFloat,false,true)
        { name = mesh.name + "_ExactPlanes", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var exactPixels = new Color[Mathf.Max(1,planeCount)];
        for (int p = 0; p < planeCount; p++) exactPixels[p] = new Color(planes[p].x,planes[p].y,planes[p].z,planes[p].w);
        exactPlanes.SetPixels(exactPixels); exactPlanes.Apply(false,false);
        const int size = 128;
        var cube = new Cubemap(size, TextureFormat.RGBAHalf, false)
        { name = mesh.name + "_FacetPlanes", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
        {
            if (face == CubemapFace.Unknown) continue;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Vector3 d = CubeDirection(face, (x + .5f) / size, (y + .5f) / size);
                float closest = float.MaxValue;
                Vector4 selected = Vector4.zero;
                foreach (Vector4 p in planes)
                {
                    float denominator = p.x * d.x + p.y * d.y + p.z * d.z;
                    if (denominator <= .00001f) continue;
                    float distance = p.w / denominator;
                    if (distance < closest) { closest = distance; selected = p; }
                }
                pixels[y * size + x] = new Color(selected.x, selected.y, selected.z, selected.w);
            }
            cube.SetPixels(pixels, face);
        }
        cube.Apply(false, false);
        return cube;
    }

    static Vector3 CubeDirection(CubemapFace face, float u, float v)
    {
        float x = 2f * u - 1f, y = 2f * v - 1f;
        switch (face)
        {
            case CubemapFace.PositiveX: return new Vector3(1, -y, -x).normalized;
            case CubemapFace.NegativeX: return new Vector3(-1, -y, x).normalized;
            case CubemapFace.PositiveY: return new Vector3(x, 1, y).normalized;
            case CubemapFace.NegativeY: return new Vector3(x, -1, -y).normalized;
            case CubemapFace.PositiveZ: return new Vector3(x, -y, 1).normalized;
            default: return new Vector3(-x, -y, -1).normalized;
        }
    }

    static Cubemap MakeCrystalStudioCube(bool crystal=false)
    {
        const int size=128;var cube=new Cubemap(size,TextureFormat.RGBAHalf,true){name=crystal?"PureLightOrb_CrystalJewelryHDR":"PureLightOrb_CrystalStudioHDR"};var pixels=new Color[size*size];
        foreach(CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
        {
            if(face==CubemapFace.Unknown)continue;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size,v=(y+.5f)/size;
                Vector3 direction = CubeDirection(face, u, v);
                float ambient = .19f + .42f * Mathf.Pow(direction.y * .5f + .5f, 2f)
                    + .28f * Mathf.Pow(Mathf.Max(0f, Vector3.Dot(direction, new Vector3(-.7f,.2f,.68f).normalized)), 4f);
                Color color = new Color(ambient * .94f, ambient * .98f, ambient * 1.06f, 1f);

                // Panels retain a hard silhouette so facets stay crisp, but
                // carry an internal luminance gradient instead of a clipped,
                // uniform HDR white rectangle.
                // Two world-oriented softboxes, rather than repeating a pair
                // of rectangles independently on all six cubemap faces.
                float panelX = direction.x / Mathf.Max(.001f, direction.z);
                float panelY = direction.y / Mathf.Max(.001f, direction.z);
                bool large=direction.z>0f&&panelX>-.85f&&panelX<-.18f&&panelY>-.20f&&panelY<1.3f;
                float sideX = direction.z / Mathf.Max(.001f, -direction.x);
                float sideY = direction.y / Mathf.Max(.001f, -direction.x);
                bool small=direction.x<0f&&sideX>-.8f&&sideX<-.1f&&sideY>.1f&&sideY<.9f;
                float rearX = direction.x / Mathf.Max(.001f, -direction.z);
                float rearY = direction.y / Mathf.Max(.001f, -direction.z);
                bool rearPanel = direction.z < 0f && rearX > -.9f && rearX < .8f && rearY > -.55f && rearY < 1.1f;
                if(large)
                {
                    float panelU=(panelX+.85f)/.67f, panelV=(panelY+.20f)/1.5f;
                    float luminance=1.0f+3.0f*(.25f+.75f*panelV)*(.58f+.42f*(1f-panelU));
                    color=new Color(luminance*1.03f,luminance*.98f,luminance*.90f,1f);
                }
                else if(small)
                {
                    float panelU=(sideX+.8f)/.7f, panelV=(sideY-.1f)/.8f;
                    float luminance=.8f+1.8f*(.35f+.65f*panelU)*(.55f+.45f*panelV);
                    color=new Color(luminance*.88f,luminance*.98f,luminance*1.10f,1f);
                }
                else if (rearPanel)
                {
                    float panelU = (rearX+.9f)/1.7f, panelV = (rearY+.55f)/1.65f;
                    float luminance = .48f + 1.7f * (.3f+.7f*panelU) * (.35f+.65f*panelV);
                    color = new Color(luminance*.98f,luminance,luminance*1.025f,1);
                }
                else
                {
                    // A broad rear card breaks up the interior into readable
                    // facets without painting all low-reflection areas grey.
                    float rear = Mathf.Max(0f, Vector3.Dot(direction, new Vector3(.5f,.65f,-.57f).normalized));
                    float card = 1.6f * Mathf.Pow(rear, 24f);
                    color += new Color(card, card * .98f, card * .94f, 0f);
                }
                if(crystal)
                {
                    // Dark studio gaps preserve clear faces; differently sized
                    // cards supply structured reflections instead of a grey dome.
                    color *= .42f;
                    float cool=3.2f*Mathf.Pow(Mathf.Max(0,Vector3.Dot(direction,new Vector3(.8f,.3f,.5f).normalized)),32);
                    float warm=2.8f*Mathf.Pow(Mathf.Max(0,Vector3.Dot(direction,new Vector3(-.5f,-.2f,-.8f).normalized)),22);
                    float top=2.2f*Mathf.Pow(Mathf.Max(0,Vector3.Dot(direction,new Vector3(.15f,.95f,-.25f).normalized)),18);
                    color+=new Color(cool*.88f+warm+top,cool*.96f+warm*.94f+top,cool+warm*.8f+top*1.04f,0);
                    color.a=1;
                }
                pixels[y*size+x]=color;
            }
            cube.SetPixels(pixels,face);
        }
        cube.Apply(true,false);return cube;
    }

    static Mesh BuildMesh(Root g, Primitive p, byte[] data, int binStart, string name)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = ReadVec3(g, p.attributes.POSITION, data, binStart); if (p.attributes.NORMAL >= 0) mesh.normals = ReadVec3(g, p.attributes.NORMAL, data, binStart);
        if (p.attributes.TEXCOORD_0 >= 0) mesh.uv = ReadVec2(g, p.attributes.TEXCOORD_0, data, binStart);
        int[] indices = ReadIndices(g, p.indices, data, binStart); for (int i = 0; i + 2 < indices.Length; i += 3) { int t = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = t; }
        mesh.triangles = indices; if (p.attributes.NORMAL < 0) mesh.RecalculateNormals();
        if (p.attributes.TEXCOORD_0 >= 0) mesh.RecalculateTangents();
        mesh.RecalculateBounds(); return mesh;
    }
    static Vector3[] ReadVec3(Root g, int ai, byte[] d, int bin)
    {
        Accessor a=g.accessors[ai]; BufferView v=g.bufferViews[a.bufferView]; int stride=v.byteStride>0?v.byteStride:12, start=bin+v.byteOffset+a.byteOffset; var r=new Vector3[a.count];
        for(int i=0;i<r.Length;i++){int o=start+i*stride;r[i]=new Vector3(BitConverter.ToSingle(d,o),BitConverter.ToSingle(d,o+4),-BitConverter.ToSingle(d,o+8));} return r;
    }
    static Vector2[] ReadVec2(Root g, int ai, byte[] d, int bin)
    {
        Accessor a=g.accessors[ai]; BufferView v=g.bufferViews[a.bufferView]; int stride=v.byteStride>0?v.byteStride:8,start=bin+v.byteOffset+a.byteOffset;var r=new Vector2[a.count];
        for(int i=0;i<r.Length;i++){int o=start+i*stride;r[i]=new Vector2(BitConverter.ToSingle(d,o),1f-BitConverter.ToSingle(d,o+4));}return r;
    }
    static int[] ReadIndices(Root g,int ai,byte[] d,int bin)
    {
        Accessor a=g.accessors[ai];BufferView v=g.bufferViews[a.bufferView];int size=a.componentType==5125?4:a.componentType==5123?2:1,stride=v.byteStride>0?v.byteStride:size,start=bin+v.byteOffset+a.byteOffset;var r=new int[a.count];
        for(int i=0;i<r.Length;i++){int o=start+i*stride;r[i]=size==4?(int)BitConverter.ToUInt32(d,o):size==2?BitConverter.ToUInt16(d,o):d[o];}return r;
    }
    static void ApplyTransform(Transform t,Node n){if(n.translation?.Length==3)t.localPosition=new Vector3(n.translation[0],n.translation[1],-n.translation[2]);if(n.rotation?.Length==4)t.localRotation=new Quaternion(-n.rotation[0],-n.rotation[1],n.rotation[2],n.rotation[3]);if(n.scale?.Length==3)t.localScale=new Vector3(n.scale[0],n.scale[1],n.scale[2]);}
}
