using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 맵의 움직이지 않는 조각(바닥·벽·배경 바닥·정적 장식)을 청크와 머티리얼별로 한 메시로 합쳐 렌더러 수를 줄인다(이슈 #105).
// 프리팹을 만들어 놓고 읽는 대신 프리팹 에셋의 메시와 렌더러 설정을 바로 읽어서, 합칠 조각은 렌더러 오브젝트를 아예 만들지 않는다.
// 청크로 나누는 것은 화면 밖 청크가 프러스텀 컬링으로 빠지게 하려는 것이다.
// 화면 결과가 그대로이도록 머티리얼, 레이어, 태그, 그림자 설정 등이 모두 같은 조각끼리만 합친다.
// 합친 메시는 조각마다 따로 움직일 수 없으므로 전투 맵에서만 쓴다. 시작 화면·로비 화면 미리보기 맵은 물결로 조각을 움직여서 합치지 않는다.
public sealed class MapRendererCombiner
{
    public const string RootName = "Combined Renderers";
    // 한 청크의 한 변 길이(칸). 전투 카메라가 한 번에 보는 바닥이 약 25x19칸이라 8칸이면 화면 밖 청크가 충분히 걸러진다.
    public const float DefaultChunkSize = 8f;

    // 프리팹 안에서 그려지는 메시 하나. matrix는 프리팹 루트 기준이고, styles는 서브메시마다의 렌더 설정 번호다.
    struct Part
    {
        public Mesh mesh;
        public Matrix4x4 matrix;
        public int[] styles;
    }

    // 렌더링 결과에 영향을 주는 렌더러 설정. 이 값이 모두 같은 조각끼리만 합친다.
    struct Style : IEquatable<Style>
    {
        public Material material;
        public int layer;
        public string tag;
        public ShadowCastingMode shadows;
        public bool receiveShadows;
        public bool staticShadowCaster;
        public LightProbeUsage lightProbes;
        public ReflectionProbeUsage reflectionProbes;
        public uint renderingLayers;
        public MotionVectorGenerationMode motionVectors;
        public int priority;
        public bool dynamicOcclusion;

        public Style(MeshRenderer renderer, Material material)
        {
            this.material = material;
            layer = renderer.gameObject.layer;
            tag = renderer.gameObject.tag;
            shadows = renderer.shadowCastingMode;
            receiveShadows = renderer.receiveShadows;
            staticShadowCaster = renderer.staticShadowCaster;
            lightProbes = renderer.lightProbeUsage;
            reflectionProbes = renderer.reflectionProbeUsage;
            renderingLayers = renderer.renderingLayerMask;
            motionVectors = renderer.motionVectorGenerationMode;
            priority = renderer.rendererPriority;
            dynamicOcclusion = renderer.allowOcclusionWhenDynamic;
        }

        public void Apply(MeshRenderer renderer)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = receiveShadows;
            renderer.staticShadowCaster = staticShadowCaster;
            renderer.lightProbeUsage = lightProbes;
            renderer.reflectionProbeUsage = reflectionProbes;
            renderer.renderingLayerMask = renderingLayers;
            renderer.motionVectorGenerationMode = motionVectors;
            renderer.rendererPriority = priority;
            renderer.allowOcclusionWhenDynamic = dynamicOcclusion;
        }

        public bool Equals(Style other) =>
            material == other.material && layer == other.layer && tag == other.tag && shadows == other.shadows &&
            receiveShadows == other.receiveShadows && staticShadowCaster == other.staticShadowCaster &&
            lightProbes == other.lightProbes && reflectionProbes == other.reflectionProbes &&
            renderingLayers == other.renderingLayers && motionVectors == other.motionVectors &&
            priority == other.priority && dynamicOcclusion == other.dynamicOcclusion;

        public override bool Equals(object other) => other is Style style && Equals(style);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = material != null ? material.GetInstanceID() : 0;
                hash = hash * 31 + layer;
                hash = hash * 31 + (int)shadows;
                hash = hash * 31 + (int)renderingLayers;
                return hash * 2 + (receiveShadows ? 1 : 0);
            }
        }
    }

    // 같은 청크, 같은 렌더 설정으로 합칠 메시들.
    sealed class Group
    {
        public int chunkX, chunkZ, style, vertexCount;
        public readonly List<CombineInstance> instances = new List<CombineInstance>();
    }

    readonly float chunkSize;
    // 프리팹마다 한 번만 읽는다. 합칠 수 없는 프리팹은 null로 기억한다. Clear에서 비우므로 맵을 새로 만들 때마다 다시 읽는다.
    readonly Dictionary<GameObject, Part[]> parts = new Dictionary<GameObject, Part[]>();
    readonly List<Style> styles = new List<Style>();
    readonly Dictionary<Style, int> styleIndices = new Dictionary<Style, int>();
    readonly Dictionary<long, Group> groups = new Dictionary<long, Group>();
    // 맵을 다시 만들 때 목록을 새로 할당하지 않도록 돌려 쓴다.
    readonly Stack<Group> spareGroups = new Stack<Group>();
    readonly List<Mesh> meshes = new List<Mesh>();
    readonly List<Transform> transformScratch = new List<Transform>();
    readonly List<Component> componentScratch = new List<Component>();
    // 합치지 못한다고 이미 알린 프리팹. 맵을 만들 때마다 같은 경고가 되풀이되지 않게 한 번만 알린다.
    static readonly HashSet<int> warnedPrefabs = new HashSet<int>();
    GameObject root;

    public MapRendererCombiner(float chunkSize = DefaultChunkSize)
    {
        this.chunkSize = Mathf.Max(1f, chunkSize);
    }

    // Build로 만들어 둔 합친 메시. Clear하면 비고 메시도 지워진다.
    public IReadOnlyList<Mesh> Meshes => meshes;
    // 합친 렌더러들을 담은 오브젝트. Build 전이나 Clear 뒤에는 null이다.
    public GameObject Root => root;

    // 프리팹 전체를 합칠 수 있는지. 메시 렌더러와 콜라이더, 바닥·벽 블록 말고 다른 것(애니메이터, 파티클, 조명, 스크립트 등)이
    // 하나라도 붙어 있거나, 꺼진 오브젝트·렌더러가 있거나, 메시를 읽을 수 없으면(Read/Write가 꺼진 메시는 빌드에서 합치지 못한다)
    // 합치지 않는다. 그런 프리팹은 부르는 쪽에서 원래대로 Instantiate한다.
    public bool CanCombine(GameObject prefab) => PartsOf(prefab) != null;

    // 프리팹을 local 자리에 놓은 것처럼 합칠 목록에 넣는다. local은 Build에 넘길 부모 기준의 프리팹 루트 행렬이다.
    // 합칠 수 없는 프리팹이면 아무것도 하지 않고 false를 돌려준다.
    public bool Add(GameObject prefab, Matrix4x4 local)
    {
        Part[] prefabParts = PartsOf(prefab);
        if (prefabParts == null) return false;
        // 조각의 상대 행렬은 Read에서 뒤집히지 않은 것만 받았으므로, 최종 행렬(local * 조각 행렬)이 뒤집히는 것은 local이 뒤집힐 때뿐이다.
        // 프리팹 루트의 스케일이 음수인 경우다. 합치면 면 방향이 바뀔 수 있어 거절하고 부르는 쪽이 원래대로 Instantiate하게 한다.
        if (local.determinant <= 0f) return false;
        int chunkX = Mathf.FloorToInt(local.m03 / chunkSize);
        int chunkZ = Mathf.FloorToInt(local.m23 / chunkSize);
        for (int p = 0; p < prefabParts.Length; p++)
        {
            Part part = prefabParts[p];
            Matrix4x4 matrix = local * part.matrix;
            for (int sub = 0; sub < part.styles.Length; sub++)
            {
                Group group = GroupOf(chunkX, chunkZ, part.styles[sub]);
                group.vertexCount += part.mesh.vertexCount;
                group.instances.Add(new CombineInstance { mesh = part.mesh, subMeshIndex = sub, transform = matrix });
            }
        }
        return true;
    }

    // 프리팹을 world 자리에 놓았을 때 첫 렌더러(GetComponentInChildren<Renderer>가 돌려주는 것)의 월드 bounds 최저 높이.
    // 배경 장식을 만들지 않고도 바닥에 맞출 때 쓴다. 합칠 수 없는 프리팹이면 false다.
    public bool TryGetFirstRendererMinY(GameObject prefab, Matrix4x4 world, out float minY)
    {
        minY = 0f;
        Part[] prefabParts = PartsOf(prefab);
        if (prefabParts == null) return false;
        // Renderer.bounds와 같은 방식: 메시 bounds를 행렬로 옮긴 상자를 감싸는 축 정렬 상자.
        Matrix4x4 matrix = world * prefabParts[0].matrix;
        Bounds bounds = prefabParts[0].mesh.bounds;
        Vector3 extents = bounds.extents;
        float centerY = matrix.MultiplyPoint3x4(bounds.center).y;
        float extentY = Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z;
        minY = centerY - extentY;
        return true;
    }

    // 모아 둔 조각을 청크·렌더 설정별 메시로 합쳐 parent 아래에 둔다. parent를 끄면 합친 렌더러도 같이 꺼진다.
    public void Build(Transform parent)
    {
        if (groups.Count == 0) return;
        root = new GameObject(RootName);
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        foreach (Group group in groups.Values)
        {
            Style style = styles[group.style];
            var mesh = new Mesh { name = $"Map Chunk {group.chunkX} {group.chunkZ} {style.material.name}" };
            // 16비트 인덱스는 정점 65,535개까지만 가리킨다.
            if (group.vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(group.instances.ToArray(), true, true, false);
            // 다시 읽을 일이 없으므로 CPU 쪽 사본은 버린다.
            mesh.UploadMeshData(true);
            meshes.Add(mesh);

            var chunk = new GameObject(mesh.name) { layer = style.layer, tag = style.tag };
            chunk.transform.SetParent(root.transform, false);
            chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
            style.Apply(chunk.AddComponent<MeshRenderer>());
        }
        ReleaseGroups();
    }

    // 합친 오브젝트와 런타임 메시를 지운다. 맵을 다시 만들거나 비울 때, 맵이 사라질 때 부른다.
    public void Clear()
    {
        ReleaseGroups();
        parts.Clear();
        styles.Clear();
        styleIndices.Clear();
        if (root != null)
        {
            // Destroy가 프레임 끝에 처리되기 전에도 이전 맵이 그려지지 않게 먼저 끈다.
            root.SetActive(false);
            Remove(root);
        }
        root = null;
        foreach (Mesh mesh in meshes)
            if (mesh != null) Remove(mesh);
        meshes.Clear();
    }

    static void Remove(UnityEngine.Object target)
    {
        // 에디트 모드(EditMode 테스트)에서는 Destroy가 지우지 못한다.
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }

    void ReleaseGroups()
    {
        foreach (Group group in groups.Values)
        {
            group.instances.Clear();
            group.vertexCount = 0;
            spareGroups.Push(group);
        }
        groups.Clear();
    }

    Group GroupOf(int chunkX, int chunkZ, int style)
    {
        long key = ((long)(chunkX + 0x8000) << 40) | ((long)(chunkZ + 0x8000) << 20) | (uint)style;
        if (groups.TryGetValue(key, out Group group)) return group;
        group = spareGroups.Count > 0 ? spareGroups.Pop() : new Group();
        group.chunkX = chunkX;
        group.chunkZ = chunkZ;
        group.style = style;
        groups.Add(key, group);
        return group;
    }

    Part[] PartsOf(GameObject prefab)
    {
        if (prefab == null) return null;
        if (parts.TryGetValue(prefab, out Part[] known)) return known;
        Part[] read = Read(prefab);
        parts.Add(prefab, read);
        return read;
    }

    Part[] Read(GameObject prefab)
    {
        var read = new List<Part>();
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        prefab.GetComponentsInChildren(true, transformScratch);
        foreach (Transform piece in transformScratch)
        {
            if (!piece.gameObject.activeSelf) return null;
            MeshFilter filter = null;
            MeshRenderer renderer = null;
            piece.GetComponents(componentScratch);
            foreach (Component component in componentScratch)
            {
                // 빠진 스크립트는 null로 온다.
                if (component == null) return null;
                if (component is MeshFilter meshFilter) filter = meshFilter;
                else if (component is MeshRenderer meshRenderer) renderer = meshRenderer;
                else if (component is Collider || component.GetType() == typeof(Transform)) continue;
                // 바닥·벽 블록은 표시만 하는 빈 스크립트다. 부시(BushBlock)는 렌더러의 머티리얼을 바꿔 끼우므로 합치지 않는다.
                else if (component.GetType() == typeof(FloorBlock) || component.GetType() == typeof(WallBlock)) continue;
                else return null;
            }
            if (filter == null && renderer == null) continue;
            if (filter == null || renderer == null || !renderer.enabled || renderer.probeAnchor != null) return null;
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) return null;
            if (!mesh.isReadable)
            {
                // 빌드에서는 Read/Write가 꺼진 메시를 CombineMeshes가 읽지 못한다. 그리기는 원래대로 되므로 알리기만 한다.
                if (warnedPrefabs.Add(prefab.GetInstanceID()))
                    Debug.LogWarning($"[Map] '{prefab.name}' is not combined: mesh '{mesh.name}' has Read/Write disabled.");
                return null;
            }
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length != mesh.subMeshCount) return null;
            Matrix4x4 matrix = toRoot * piece.localToWorldMatrix;
            // 뒤집힌(음수 스케일) 조각은 합치면 면 방향이 바뀔 수 있어 그대로 둔다.
            if (matrix.determinant <= 0f) return null;
            var partStyles = new int[materials.Length];
            for (int sub = 0; sub < materials.Length; sub++)
            {
                if (materials[sub] == null) return null;
                partStyles[sub] = StyleOf(new Style(renderer, materials[sub]));
            }
            read.Add(new Part { mesh = mesh, matrix = matrix, styles = partStyles });
        }
        // 그릴 것이 없는 프리팹은 합칠 것도 없다.
        return read.Count > 0 ? read.ToArray() : null;
    }

    int StyleOf(Style style)
    {
        if (styleIndices.TryGetValue(style, out int index)) return index;
        index = styles.Count;
        styles.Add(style);
        styleIndices.Add(style, index);
        return index;
    }
}
