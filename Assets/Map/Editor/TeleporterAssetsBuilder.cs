using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// 텔레포트 타일(이슈 #131) 임시 모델을 만든다: 일반 머티리얼(바탕 돌, 테두리 돌), 하이라이트 머티리얼, 메시 둘, Teleporter 프리팹.
// 모양은 웹 시안의 A안 "룬 석판"이다. 바탕 돌판 위에 돌 테두리를 두르고, 하이라이트로 바깥 고리를 상감한 뒤
// 안쪽 고리·룬 8개·가운데 마름모를 돌아가는 무늬(Spinner)로 둔다. 타일 가운데가 원점이고 y = 0이 바닥 윗면이다.
// 모델을 바꿀 때는 빛낼 부분에 Teleporter Glow 머티리얼만 쓰면 TeleporterBlock이 그 슬롯을 찾아 빛을 바꾼다.
public static class TeleporterAssetsBuilder
{
    public const string Folder = "Assets/Map/Blocks/Teleporters";
    public const string PrefabPath = Folder + "/Teleporter.prefab";
    public const string StoneMaterialPath = Folder + "/Materials/Teleporter Stone.mat";
    public const string TrimMaterialPath = Folder + "/Materials/Teleporter Trim.mat";
    public const string GlowMaterialPath = Folder + "/Materials/Teleporter Glow.mat";
    public const string SlabMeshPath = Folder + "/Meshes/Teleporter Slab.asset";
    public const string SpinnerMeshPath = Folder + "/Meshes/Teleporter Spinner.asset";

    // A안 값.
    const float SlabHeight = 0.05f;   // 돌판 윗면 높이
    const float SlabSize = 1.96f;     // 2×2칸에서 가장자리를 2cm씩 남긴다
    const float SlabDepth = 0.1f;     // 바닥 블록처럼 바닥 아래로 내려가는 깊이
    const float BorderWidth = 0.12f, BorderHeight = 0.03f;
    const float OuterRadius = 0.75f, RingWidth = 0.05f;
    const float InnerRadius = OuterRadius * 0.58f;
    const int Runes = 8;
    const float Inlay = 0.012f;       // 하이라이트 상감 두께
    const float Proud = 0.006f;       // 상감이 돌 위로 솟는 높이
    const float DiamondHeight = 0.03f;
    const int Segments = 64;

    static readonly Color Obsidian = new Color(0.11f, 0.12f, 0.14f);
    static readonly Color Slate = new Color(0.2f, 0.26f, 0.29f);
    static readonly Color Violet = new Color32(0x9d, 0x6b, 0xff, 0xff);

    [MenuItem("Tools/Wizard Duel/Rebuild Teleporter Tile Assets")]
    public static void Rebuild()
    {
        EnsureFolder(Folder + "/Materials");
        EnsureFolder(Folder + "/Meshes");
        Material stone = WriteLit(StoneMaterialPath, Obsidian, 0.12f, false);
        Material trim = WriteLit(TrimMaterialPath, Slate, 0.12f, false);
        Material glow = WriteLit(GlowMaterialPath, Violet, 0.6f, true);
        Mesh slab = WriteMesh(SlabMeshPath, BuildSlab());
        Mesh spinner = WriteMesh(SpinnerMeshPath, BuildSpinner());
        WritePrefab(stone, trim, glow, slab, spinner);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Teleporter] Rebuilt teleporter tile assets: {PrefabPath}");
    }

    // 0: 바탕 돌, 1: 테두리 돌, 2: 바깥 고리(하이라이트).
    static Mesh BuildSlab()
    {
        var builder = new MeshBuilder(3);
        float half = SlabSize * 0.5f;
        builder.Box(0, new Vector3(0f, (SlabHeight - SlabDepth) * 0.5f, 0f), Vector3.right, half, half,
            -SlabDepth, SlabHeight);
        float edge = SlabSize * 0.5f - BorderWidth * 0.5f;
        float top = SlabHeight + BorderHeight;
        builder.Box(1, new Vector3(0f, 0f, edge), Vector3.right, half, BorderWidth * 0.5f, SlabHeight, top);
        builder.Box(1, new Vector3(0f, 0f, -edge), Vector3.right, half, BorderWidth * 0.5f, SlabHeight, top);
        builder.Box(1, new Vector3(edge, 0f, 0f), Vector3.right, BorderWidth * 0.5f, half - BorderWidth, SlabHeight, top);
        builder.Box(1, new Vector3(-edge, 0f, 0f), Vector3.right, BorderWidth * 0.5f, half - BorderWidth, SlabHeight, top);
        float y = SlabHeight - Inlay + Proud;
        builder.Annulus(2, OuterRadius - RingWidth, OuterRadius, y, y + Inlay);
        return builder.ToMesh("Teleporter Slab");
    }

    // 쓸 수 있을 때 도는 하이라이트 무늬: 안쪽 고리, 룬 막대, 가운데 마름모.
    static Mesh BuildSpinner()
    {
        var builder = new MeshBuilder(1);
        float y = SlabHeight - Inlay + Proud;
        builder.Annulus(0, InnerRadius - RingWidth * 0.55f, InnerRadius, y, y + Inlay);
        float r0 = InnerRadius + 0.05f, r1 = OuterRadius - RingWidth - 0.05f;
        float length = Mathf.Max(0.04f, r1 - r0), middle = Mathf.Max(r0, (r0 + r1) * 0.5f);
        float width = RingWidth * 0.6f;
        for (int i = 0; i < Runes; i++)
        {
            float angle = i / (float)Runes * Mathf.PI * 2f;
            var along = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 center = along * middle;
            builder.Box(0, center, along, length * 0.5f, width * 0.5f, y, y + Inlay);
            // 짝수 번째 룬에는 가로 막대를 더해 십자로 만든다.
            if (i % 2 == 0)
                builder.Box(0, center, new Vector3(-along.z, 0f, along.x), length * 0.3f, width * 0.5f, y, y + Inlay);
        }
        float diamond = InnerRadius * 0.75f / Mathf.Sqrt(2f) * 0.5f;
        float diamondY = SlabHeight + 0.012f;
        builder.Box(0, Vector3.zero, new Vector3(1f, 0f, 1f).normalized, diamond, diamond,
            diamondY - DiamondHeight * 0.5f, diamondY + DiamondHeight * 0.5f);
        return builder.ToMesh("Teleporter Spinner");
    }

    static void WritePrefab(Material stone, Material trim, Material glow, Mesh slab, Mesh spinner)
    {
        bool exists = File.Exists(PrefabPath);
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("Teleporter");
        try
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            GameObject slabObject = Child(root, "Slab", slab, new[] { stone, trim, glow }, ShadowCastingMode.On);
            GameObject spinnerObject = Child(root, "Spinner", spinner, new[] { glow }, ShadowCastingMode.Off);
            slabObject.transform.SetSiblingIndex(0);

            TeleporterBlock block = root.TryGetComponent(out TeleporterBlock found) ? found : root.AddComponent<TeleporterBlock>();
            var serialized = new SerializedObject(block);
            serialized.FindProperty("highlightMaterial").objectReferenceValue = glow;
            serialized.FindProperty("spinner").objectReferenceValue = spinnerObject.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    static GameObject Child(GameObject root, string name, Mesh mesh, Material[] materials, ShadowCastingMode shadows)
    {
        var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        child.transform.SetParent(root.transform, false);
        child.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = shadows;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return child;
    }

    // color는 sRGB다. 하이라이트 머티리얼은 발광을 켜 두고, 색과 세기는 실행 중 TeleporterBlock이 쌍 색으로 덮어쓴다.
    static Material WriteLit(string path, Color color, float smoothness, bool emissive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader) material.shader = shader;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        if (emissive)
        {
            Color linear = color.linear * 0.6f;
            material.SetVector("_EmissionColor", new Vector4(linear.r, linear.g, linear.b, 1f));
            material.EnableKeyword("_EMISSION");
            // None이면 에디터에서 렌더할 때 URP가 발광이 없는 머티리얼로 보고 _EMISSION을 꺼 버린다.
            // 빛은 실행 중에 바뀌므로 굽지 않는 실시간 발광으로 둔다.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static Mesh WriteMesh(string path, Mesh source)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(source, existing);
            existing.name = source.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(source);
            return existing;
        }
        AssetDatabase.CreateAsset(source, path);
        return source;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // 평평한 면만 쓰는 간단한 메시 조립기. 면마다 꼭짓점을 따로 두어 모서리가 각지게 보인다.
    sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int>[] triangles;

        public MeshBuilder(int submeshes)
        {
            triangles = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) triangles[i] = new List<int>();
        }

        // center(xz)를 가운데로 along 방향 반길이 halfAlong, 옆 방향 반길이 halfSide, 높이 y0~y1인 상자. 밑면은 그리지 않는다.
        public void Box(int submesh, Vector3 center, Vector3 along, float halfAlong, float halfSide, float y0, float y1)
        {
            along.y = 0f;
            along.Normalize();
            var side = new Vector3(-along.z, 0f, along.x);
            Vector3 Corner(float a, float s, float y) => new Vector3(center.x, y, center.z) + along * (a * halfAlong) + side * (s * halfSide);
            Vector3 a0 = Corner(-1, -1, y0), a1 = Corner(1, -1, y0), a2 = Corner(1, 1, y0), a3 = Corner(-1, 1, y0);
            Vector3 b0 = Corner(-1, -1, y1), b1 = Corner(1, -1, y1), b2 = Corner(1, 1, y1), b3 = Corner(-1, 1, y1);
            Quad(submesh, b0, b1, b2, b3, Vector3.up);
            Quad(submesh, a0, a1, b1, b0, -side);
            Quad(submesh, a1, a2, b2, b1, along);
            Quad(submesh, a2, a3, b3, b2, side);
            Quad(submesh, a3, a0, b0, b3, -along);
        }

        // 가운데가 빈 평평한 고리. 윗면과 안팎 옆면만 그린다.
        public void Annulus(int submesh, float inner, float outer, float y0, float y1)
        {
            for (int i = 0; i < Segments; i++)
            {
                float t0 = i / (float)Segments * Mathf.PI * 2f, t1 = (i + 1) / (float)Segments * Mathf.PI * 2f;
                var d0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0));
                var d1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                Vector3 up0 = Vector3.up * y1, down0 = Vector3.up * y0;
                Quad(submesh, d0 * inner + up0, d0 * outer + up0, d1 * outer + up0, d1 * inner + up0, Vector3.up);
                Quad(submesh, d0 * outer + down0, d1 * outer + down0, d1 * outer + up0, d0 * outer + up0, (d0 + d1).normalized);
                Quad(submesh, d1 * inner + down0, d0 * inner + down0, d0 * inner + up0, d1 * inner + up0, -(d0 + d1).normalized);
            }
        }

        // 네 꼭짓점을 둘레 순서로 받는다. normal 쪽에서 볼 때 앞면이 되도록 감는 방향을 맞춘다.
        void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int start = vertices.Count;
            foreach (Vector3 vertex in new[] { a, b, c, d })
            {
                vertices.Add(vertex);
                normals.Add(normal);
                uvs.Add(Mathf.Abs(normal.y) > 0.5f ? new Vector2(vertex.x, vertex.z) : new Vector2(vertex.x + vertex.z, vertex.y));
            }
            bool front = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            int[] order = front ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
            foreach (int index in order) triangles[submesh].Add(start + index);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
