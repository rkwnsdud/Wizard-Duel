#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 전투 맵의 렌더러 합치기(이슈 #105). 플레이 모드 없이 Main 씬 전투 맵과 같은 프리팹·배경 범위로 맵을 만들어,
// 합친 맵이 합치지 않은 맵과 같은 것을 그리고 콜라이더와 부시는 그대로인지 본다.
public class MapRendererCombineTests
{
    // Main 씬 Duel Arena의 Battle Background 값.
    const int RimOffsetCells = 7;
    const int DecoOffsetCells = 10;
    const float Tolerance = 0.001f;

    // 가벼운 테스트 묶음에 들어가므로 만들 수 있는 가장 작은 정사각 맵을 쓰고, 합치지 않은 맵과 합친 맵을 한 번씩만 만들어 같이 쓴다.
    static readonly MapGenerationSettings Settings = new MapGenerationSettings
    {
        Seed = 7301, Size = new Vector2Int(25, 25), Shape = MapShape.Rectangle
    };
    // 맵을 따로 만들어야 하는 테스트는 더 작은 길쭉한 맵을 쓴다.
    static readonly MapGenerationSettings SmallSettings = new MapGenerationSettings
    {
        Seed = 7301, Size = new Vector2Int(25, 13), Shape = MapShape.Elongated
    };

    ProceduralMap plain;
    ProceduralMap combined;
    readonly List<ProceduralMap> ownMaps = new List<ProceduralMap>();
    readonly List<Object> created = new List<Object>();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        plain = NewBattleMap("Combine Test Map (plain)");
        plain.Generate(Settings);
        combined = NewBattleMap("Combine Test Map (combined)");
        combined.Generate(Settings, true);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        subMeshVertices.Clear();
        Remove(plain);
        Remove(combined);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (ProceduralMap map in ownMaps) Remove(map);
        ownMaps.Clear();
        foreach (Object item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [Test]
    public void Combining_CutsTheRendererCountToHundreds()
    {
        int before = plain.GetComponentsInChildren<Renderer>(true).Length;
        int after = combined.GetComponentsInChildren<Renderer>(true).Length;

        Assert.That(before, Is.GreaterThan(5000), "The uncombined map is the baseline this change targets.");
        Assert.That(after, Is.LessThan(600));
        Assert.That(after, Is.LessThan(before / 10));
    }

    [Test]
    public void Combining_DrawsTheSameGeometryInEveryChunk()
    {
        AssertSameChunks(plain, combined);
    }

    // 배경은 월드 좌표로 놓은 조각을 배경 루트 기준으로 되돌려 합친다. 맵이 원점이 아니고 기울어 있어도 같은 자리에 그려야 한다.
    [Test]
    public void Combining_DrawsTheSameGeometryWhenTheMapIsMovedAndRotated()
    {
        ProceduralMap movedPlain = NewOwnMap("Moved Map (plain)");
        ProceduralMap movedCombined = NewOwnMap("Moved Map (combined)");
        foreach (ProceduralMap map in new[] { movedPlain, movedCombined })
        {
            Transform parent = NewObject("Moved Parent").transform;
            parent.SetPositionAndRotation(new Vector3(13.5f, 2f, -7.25f), Quaternion.Euler(8f, 37f, -5f));
            map.transform.SetParent(parent, false);
            map.transform.localPosition = new Vector3(-3f, 0.5f, 4f);
            map.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
        }
        movedPlain.Generate(SmallSettings);
        movedCombined.Generate(SmallSettings, true);

        AssertSameChunks(movedPlain, movedCombined);
    }

    [Test]
    public void Combining_KeepsChunksSmallEnoughToCull()
    {
        MeshRenderer[] chunks = CombinedRenderers(combined).ToArray();
        Assert.That(chunks, Is.Not.Empty);
        foreach (MeshRenderer chunk in chunks)
        {
            // 조각은 칸 중심으로 청크를 정하므로 한 칸쯤은 청크 밖으로 나갈 수 있다.
            Assert.That(chunk.bounds.size.x, Is.LessThanOrEqualTo(MapRendererCombiner.DefaultChunkSize + 2.5f), chunk.name);
            Assert.That(chunk.bounds.size.z, Is.LessThanOrEqualTo(MapRendererCombiner.DefaultChunkSize + 2.5f), chunk.name);
        }
    }

    // 한 청크의 정점이 16비트 인덱스 한계(65,535개)를 넘으면 32비트 인덱스로 합쳐야 한다.
    [Test]
    public void Combiner_UsesUInt32IndicesPastTheUInt16VertexLimit()
    {
        GameObject wall = Load<GameObject>("Assets/map/Walls/Wall-1.prefab");
        Transform parent = NewObject("UInt32 Parent").transform;
        Transform reference = NewObject("UInt32 Reference").transform;
        // 청크를 크게 잡아 모든 벽이 한 메시로 합쳐지게 한다.
        var combiner = new MapRendererCombiner(1000f);
        try
        {
            const int count = 80;
            long indices = 0;
            int vertices = 0;
            Bounds expected = default;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                var position = new Vector3(i % 10 * 1.5f, 0f, i / 10 * 1.5f);
                Quaternion rotation = Quaternion.Euler(0f, i % 4 * 90f, 0f);
                Assert.That(combiner.Add(wall, Matrix4x4.TRS(position, rotation, wall.transform.localScale)), Is.True);
                GameObject instance = Object.Instantiate(wall, reference, false);
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    Bounds bounds = Move(filter.sharedMesh.bounds, reference.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                    if (any) expected.Encapsulate(bounds);
                    else expected = bounds;
                    any = true;
                    vertices += filter.sharedMesh.vertexCount;
                    indices += filter.sharedMesh.GetIndexCount(0);
                }
            }
            combiner.Build(parent);

            Assert.That(vertices, Is.GreaterThan(65535), "The test must cross the 16-bit limit.");
            Assert.That(combiner.Meshes, Has.Count.EqualTo(1));
            Mesh mesh = combiner.Meshes[0];
            Assert.That(mesh.indexFormat, Is.EqualTo(UnityEngine.Rendering.IndexFormat.UInt32));
            Assert.That(mesh.vertexCount, Is.EqualTo(vertices));
            Assert.That((long)mesh.GetIndexCount(0), Is.EqualTo(indices));
            Assert.That(Vector3.Distance(mesh.bounds.min, expected.min), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(mesh.bounds.max, expected.max), Is.LessThan(Tolerance));
        }
        finally
        {
            combiner.Clear();
        }
    }

    [Test]
    public void Combining_LeavesCollidersWhereTheyWere()
    {
        List<string> before = Colliders(plain);

        Assert.That(before.Count, Is.GreaterThan(500));
        CollectionAssert.AreEqual(before, Colliders(combined));
        Assert.That(combined.Blocks.Count, Is.EqualTo(plain.Blocks.Count));
        CollectionAssert.AreEqual(plain.SpawnPoints.Select(spawn => spawn.position).ToArray(),
            combined.SpawnPoints.Select(spawn => spawn.position).ToArray());
        Assert.That(combined.Pieces.Take(combined.Blocks.Count), Is.EqualTo(combined.Blocks.Select(block => block.Transform)));
    }

    [Test]
    public void Combining_LeavesBushRenderersOnTheirBlocks()
    {
        List<Renderer> before = BushRenderers(plain);
        List<Renderer> bushes = BushRenderers(combined);

        Assert.That(before.Count, Is.GreaterThan(0), "This seed has bushes.");
        Assert.That(bushes.Count, Is.EqualTo(before.Count));
        var bushMaterials = new HashSet<Material>(bushes.SelectMany(bush => bush.sharedMaterials));
        Assert.That(bushMaterials, Is.Not.Empty);
        foreach (Renderer bush in bushes)
        {
            Assert.That(bush.enabled, Is.True);
            Assert.That(bush.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
        }
        // 부시 머티리얼이 합친 청크에 섞여 들어가면 반투명 전환이 그 조각에는 먹지 않는다.
        foreach (MeshRenderer chunk in CombinedRenderers(combined))
            Assert.That(bushMaterials.Contains(chunk.sharedMaterial), Is.False, chunk.name);
    }

    [Test]
    public void Combining_LeavesOnlyCollidersOnFloorAndWallBlocks()
    {
        foreach (IBlock block in combined.Blocks)
        {
            if (block is BushBlock || combined.SpawnPoints.Any(spawn => spawn.IsChildOf(block.Transform))) continue;
            Assert.That(block.Transform.GetComponentsInChildren<Renderer>(true), Is.Empty, block.Transform.name);
            Assert.That(block.Transform.GetComponentsInChildren<Collider>(true), Has.Length.EqualTo(1), block.Transform.name);
            Assert.That(block.Transform.gameObject.activeInHierarchy, Is.True);
        }
        Assert.That(combined.transform.Find("Collider Templates"), Is.Null, "Templates are released once the map is laid.");
    }

    [Test]
    public void Generate_ReleasesColliderTemplatesWhenLayingBlocksFails()
    {
        ProceduralMap map = NewOwnMap("Failing Map");
        // 부시 프리팹이 없으면 부시 칸에서 예외가 난다.
        var serialized = new SerializedObject(map.GetComponent<BlockFactory>());
        serialized.FindProperty("bushBlocks").arraySize = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.Throws<System.InvalidOperationException>(() => map.Generate(SmallSettings, true));
        Assert.That(map.transform.Find("Collider Templates"), Is.Null);
    }

    [Test]
    public void Regenerating_DestroysThePreviousCombinedMeshes()
    {
        ProceduralMap map = NewOwnMap("Regenerated Map");
        map.Generate(SmallSettings, true);
        Mesh[] first = CombinedMeshes(map);
        Assert.That(first, Is.Not.Empty);

        map.Generate(new MapGenerationSettings { Seed = 12, Size = SmallSettings.Size, Shape = SmallSettings.Shape }, true);
        Mesh[] second = CombinedMeshes(map);
        foreach (Mesh mesh in first) Assert.That(mesh == null, Is.True, "Regenerating must destroy the old runtime meshes.");
        Assert.That(second, Is.Not.Empty);
        Assert.That(CombinedRoots(map).Count, Is.EqualTo(2), "One combined root for the blocks and one for the background.");

        // 미리보기 맵처럼 합치지 않고 다시 만들면 합친 것이 남지 않는다.
        map.Generate(SmallSettings);
        foreach (Mesh mesh in second) Assert.That(mesh == null, Is.True);
        Assert.That(CombinedRoots(map), Is.Empty);
    }

    [Test]
    public void Clearing_DestroysTheCombinedMeshes()
    {
        ProceduralMap map = NewOwnMap("Cleared Map");
        map.Generate(SmallSettings, true);
        Mesh[] meshes = CombinedMeshes(map);
        Assert.That(meshes, Is.Not.Empty);

        // DuelArena.Clear와 같은 순서.
        map.Clear();
        map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true).Rebuild();

        foreach (Mesh mesh in meshes) Assert.That(mesh == null, Is.True);
        Assert.That(CombinedRoots(map), Is.Empty);
    }

    [Test]
    public void Combining_KeepsTheLayoutAndFingerprint()
    {
        Assert.That(MapFingerprint.Compute(combined.GeneratedLayout, combined.SpawnPoints),
            Is.EqualTo(MapFingerprint.Compute(plain.GeneratedLayout, plain.SpawnPoints)));
    }

    [Test]
    public void Combiner_RefusesPrefabsItMustNotMerge()
    {
        var combiner = new MapRendererCombiner();
        Material material = Load<GameObject>("Assets/map/Floors/Floor-1.prefab").GetComponentInChildren<MeshRenderer>().sharedMaterial;
        GameObject solid = NewCube(material);
        GameObject lit = NewCube(material);
        GameObject hidden = NewCube(material);
        GameObject flipped = NewCube(material);
        GameObject empty = NewObject("Empty");
        try
        {
            lit.AddComponent<Light>();
            hidden.GetComponent<MeshRenderer>().enabled = false;
            flipped.transform.localScale = new Vector3(-1f, 1f, 1f);

            Assert.That(combiner.CanCombine(solid), Is.True);
            Assert.That(combiner.CanCombine(lit), Is.False, "Anything besides mesh renderers and colliders stays as it is.");
            Assert.That(combiner.CanCombine(hidden), Is.False);
            Assert.That(combiner.CanCombine(empty), Is.False);
            Assert.That(combiner.CanCombine(Load<GameObject>("Assets/map/Bushes/Reeds.prefab")), Is.False,
                "Bush renderers swap materials at runtime.");
            Assert.That(combiner.Add(lit, Matrix4x4.identity), Is.False);
            // 루트 스케일이 음수면 프리팹 안의 상대 행렬은 멀쩡해도 놓이는 자리에서 면이 뒤집힌다.
            Assert.That(combiner.Add(flipped, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, flipped.transform.localScale)), Is.False);
            Assert.That(combiner.Add(solid, Matrix4x4.Scale(new Vector3(1f, 1f, -1f))), Is.False);
            Assert.That(combiner.Add(solid, Matrix4x4.identity), Is.True);
        }
        finally
        {
            combiner.Clear();
        }
    }

    // Main 씬 전투 맵과 같은 프리팹과 배경 범위를 쓰는 맵. 부르는 쪽에서 Clear한 뒤 오브젝트를 지운다.
    // 범위를 넘기면 그 범위로 배경을 깐다(로비 화면 미리보기 배경과 비교할 때 쓴다).
    public static ProceduralMap NewBattleMap(string name,
        int rimOffsetCells = RimOffsetCells, int decoOffsetCells = DecoOffsetCells)
    {
        var root = new GameObject(name);
        BlockFactory factory = root.AddComponent<BlockFactory>();
        var serialized = new SerializedObject(factory);
        Fill(serialized.FindProperty("floorBlocks"), Variants<Block>("Assets/map/Floors/Floor-{0}.prefab", 4));
        Fill(serialized.FindProperty("wallBlocks"), Variants<Block>("Assets/map/Walls/Wall-{0}.prefab", 4));
        Fill(serialized.FindProperty("spawnBlocks"),
            Load<Block>("Assets/map/SpawnPoints/SpawnPoint-0.prefab"), Load<Block>("Assets/map/SpawnPoints/SpawnPoint-1.prefab"));
        Fill(serialized.FindProperty("bushBlocks"), Load<BushBlock>("Assets/map/Bushes/Reeds.prefab"));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ProceduralMap map = root.AddComponent<ProceduralMap>();

        var backgroundObject = new GameObject("Battle Background");
        backgroundObject.transform.SetParent(root.transform, false);
        var background = backgroundObject.AddComponent<ArenaBackgroundBuilderNew>();
        serialized = new SerializedObject(background);
        serialized.FindProperty("map").objectReferenceValue = map;
        serialized.FindProperty("backgroundRoot").objectReferenceValue = backgroundObject.transform;
        Fill(serialized.FindProperty("floorFillVariants"), Variants<GameObject>("Assets/map/Floors/Floor-{0}.prefab", 4));
        Fill(serialized.FindProperty("pillarVariants"),
            Load<GameObject>("Assets/ThirdParty/MapParts/FBX/Bush_1x1.fbx"),
            Load<GameObject>("Assets/ThirdParty/MapParts/FBX/Chest.fbx"),
            Load<GameObject>("Assets/ThirdParty/MapParts/FBX/DeadTree_1.fbx"));
        Fill(serialized.FindProperty("torchVariants"), Load<GameObject>("Assets/ThirdParty/MapParts/FBX/Torch.fbx"));
        serialized.FindProperty("rimOffsetCells").intValue = rimOffsetCells;
        serialized.FindProperty("decoOffsetCells").intValue = decoOffsetCells;
        serialized.FindProperty("floorRotation").FindPropertyRelative("yawMode").enumValueIndex = 1;
        serialized.FindProperty("pillarRotation").FindPropertyRelative("yawMode").enumValueIndex = 1;
        serialized.FindProperty("torchRotation").FindPropertyRelative("yawMode").enumValueIndex = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return map;
    }

    // 합친 렌더러를 담은 오브젝트들(블록용, 배경용).
    public static List<Transform> CombinedRoots(ProceduralMap map) =>
        map.GetComponentsInChildren<Transform>(true).Where(child => child.name == MapRendererCombiner.RootName).ToList();

    sealed class Chunk
    {
        public Bounds bounds;
        public long indices;
        bool any;

        public void Add(Bounds added, long addedIndices)
        {
            if (any) bounds.Encapsulate(added);
            else bounds = added;
            any = true;
            indices += addedIndices;
        }
    }

    // 합치지 않은 맵의 렌더러를 합칠 때와 같은 키(청크, 렌더 설정)로 묶은 것이 합친 맵의 청크와 하나씩 같은지 본다.
    // 조각의 회전이나 행렬 곱 순서, 장식을 바닥에 맞추는 계산이 틀리면 그 청크의 bounds가 달라진다.
    static void AssertSameChunks(ProceduralMap plainMap, ProceduralMap combinedMap)
    {
        Dictionary<string, Chunk> expected = PlainChunks(plainMap);
        Dictionary<string, Chunk> actual = CombinedChunks(combinedMap);

        Assert.That(expected.Count, Is.GreaterThan(50));
        CollectionAssert.AreEquivalent(expected.Keys, actual.Keys);
        foreach (KeyValuePair<string, Chunk> pair in expected)
        {
            Chunk chunk = actual[pair.Key];
            Assert.That(chunk.indices, Is.EqualTo(pair.Value.indices), pair.Key);
            Assert.That(Vector3.Distance(chunk.bounds.min, pair.Value.bounds.min), Is.LessThan(Tolerance), pair.Key);
            Assert.That(Vector3.Distance(chunk.bounds.max, pair.Value.bounds.max), Is.LessThan(Tolerance), pair.Key);
        }
        // 합친 맵에 따로 남은 렌더러는 부시, 스폰 타일, 먼 바닥뿐이다.
        foreach (MeshRenderer renderer in StaticRenderers(combinedMap))
            Assert.That(IsCombined(renderer) || IsSpawn(combinedMap, renderer), Is.True, renderer.name);
    }

    static Dictionary<string, Chunk> PlainChunks(ProceduralMap map)
    {
        var chunks = new Dictionary<string, Chunk>();
        Transform background = map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true).transform;
        foreach (MeshRenderer renderer in StaticRenderers(map))
        {
            if (IsSpawn(map, renderer)) continue;
            // 맵이나 배경 바로 아래의 조각(블록, 배경 타일, 장식) 위치로 청크를 정한다.
            Transform piece = renderer.transform;
            while (piece.parent != map.transform && piece.parent != background) piece = piece.parent;
            Transform space = piece.parent;
            int chunkX = Mathf.FloorToInt(piece.localPosition.x / MapRendererCombiner.DefaultChunkSize);
            int chunkZ = Mathf.FloorToInt(piece.localPosition.z / MapRendererCombiner.DefaultChunkSize);
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Material[] materials = renderer.sharedMaterials;
            Matrix4x4 toSpace = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            // 합친 메시의 bounds는 옮겨 간 정점을 딱 맞게 감싼다. 조각이 비스듬히 놓이면 조각 bounds를 옮긴 상자는 그보다 커지므로
            // 정점을 하나씩 옮겨서 잰다. 합칠 때 서브메시마다 그 서브메시가 쓰는 정점만 옮겨 간다.
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                Find(chunks, Key(space == background, chunkX, chunkZ, renderer, materials[sub]))
                    .Add(Tight(mesh, sub, toSpace), mesh.GetIndexCount(sub));
        }
        return chunks;
    }

    static Dictionary<string, Chunk> CombinedChunks(ProceduralMap map)
    {
        var chunks = new Dictionary<string, Chunk>();
        Transform background = map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true).transform;
        foreach (Transform root in CombinedRoots(map))
        {
            Transform space = root.parent;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 이름은 "Map Chunk <x> <z> <머티리얼>"이다.
                string[] words = renderer.name.Split(' ');
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.subMeshCount, Is.EqualTo(1), renderer.name);
                string key = Key(space == background, int.Parse(words[2]), int.Parse(words[3]), renderer, renderer.sharedMaterial);
                Assert.That(chunks.ContainsKey(key), Is.False, "One renderer per chunk and style: " + key);
                Find(chunks, key).Add(
                    Move(mesh.bounds, space.worldToLocalMatrix * renderer.transform.localToWorldMatrix), mesh.GetIndexCount(0));
            }
        }
        return chunks;
    }

    static Chunk Find(Dictionary<string, Chunk> chunks, string key)
    {
        if (!chunks.TryGetValue(key, out Chunk chunk)) chunks.Add(key, chunk = new Chunk());
        return chunk;
    }

    static string Key(bool background, int chunkX, int chunkZ, MeshRenderer renderer, Material material) =>
        $"{(background ? "background" : "blocks")}|{chunkX}|{chunkZ}|{material.name}|layer={renderer.gameObject.layer}|" +
        $"tag={renderer.gameObject.tag}|{renderer.shadowCastingMode}|{renderer.receiveShadows}|{renderer.staticShadowCaster}|" +
        $"{renderer.lightProbeUsage}|{renderer.reflectionProbeUsage}|{renderer.renderingLayerMask}|" +
        $"{renderer.motionVectorGenerationMode}|{renderer.rendererPriority}|{renderer.allowOcclusionWhenDynamic}";

    static readonly Dictionary<(Mesh, int), Vector3[]> subMeshVertices = new Dictionary<(Mesh, int), Vector3[]>();

    // 서브메시가 쓰는 정점을 행렬로 옮긴 것을 딱 맞게 감싸는 상자.
    static Bounds Tight(Mesh mesh, int sub, Matrix4x4 matrix)
    {
        if (!subMeshVertices.TryGetValue((mesh, sub), out Vector3[] points))
        {
            Vector3[] all = mesh.vertices;
            points = mesh.subMeshCount == 1 ? all : mesh.GetIndices(sub).Distinct().Select(index => all[index]).ToArray();
            subMeshVertices.Add((mesh, sub), points);
        }
        Vector3 min = matrix.MultiplyPoint3x4(points[0]);
        Vector3 max = min;
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 point = matrix.MultiplyPoint3x4(points[i]);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    // bounds를 행렬로 옮긴 상자를 감싸는 축 정렬 상자.
    static Bounds Move(Bounds bounds, Matrix4x4 matrix)
    {
        Vector3 extents = bounds.extents;
        var moved = new Vector3(
            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
        return new Bounds(matrix.MultiplyPoint3x4(bounds.center), moved * 2f);
    }

    static bool IsCombined(Component component) =>
        component.GetComponentsInParent<Transform>(true).Any(parent => parent.name == MapRendererCombiner.RootName);

    static bool IsSpawn(ProceduralMap map, Component component) =>
        map.SpawnPoints.Any(spawn => component.transform.IsChildOf(spawn.parent));

    static IEnumerable<MeshRenderer> CombinedRenderers(ProceduralMap map) =>
        CombinedRoots(map).SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true));

    static Mesh[] CombinedMeshes(ProceduralMap map) =>
        CombinedRoots(map).SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
            .Select(filter => filter.sharedMesh).ToArray();

    // 부시와 먼 바닥(FarGround)을 뺀, 합치기 대상과 스폰 타일의 렌더러.
    static IEnumerable<MeshRenderer> StaticRenderers(ProceduralMap map) =>
        map.GetComponentsInChildren<MeshRenderer>(true).Where(renderer =>
            renderer.GetComponentInParent<BushBlock>() == null && !renderer.name.StartsWith("FarGround"));

    static List<Renderer> BushRenderers(ProceduralMap map) =>
        map.Blocks.OfType<BushBlock>().SelectMany(bush => bush.GetComponentsInChildren<Renderer>(true)).ToList();

    static List<string> Colliders(ProceduralMap map)
    {
        var colliders = new List<string>();
        foreach (Collider collider in map.GetComponentsInChildren<Collider>(true))
        {
            Transform transform = collider.transform;
            string shape = collider is BoxCollider box ? $"{box.center:F4}{box.size:F4}" : collider.GetType().Name;
            colliders.Add($"{collider.gameObject.layer}|{transform.position:F4}|{transform.eulerAngles:F2}|" +
                $"{transform.lossyScale:F4}|{shape}|{collider.isTrigger}|{collider.enabled && collider.gameObject.activeInHierarchy}");
        }
        colliders.Sort(string.CompareOrdinal);
        return colliders;
    }

    ProceduralMap NewOwnMap(string name)
    {
        ProceduralMap map = NewBattleMap(name);
        ownMaps.Add(map);
        return map;
    }

    GameObject NewObject(string name)
    {
        var gameObject = new GameObject(name);
        created.Add(gameObject);
        return gameObject;
    }

    GameObject NewCube(Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        created.Add(cube);
        return cube;
    }

    // 에디트 모드에서는 OnDestroy가 불리지 않으므로 합친 메시를 직접 비운 뒤 지운다.
    static void Remove(ProceduralMap map)
    {
        if (map == null) return;
        map.Clear();
        map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true).Rebuild();
        Object.DestroyImmediate(map.gameObject);
    }

    static T Load<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }

    static Object[] Variants<T>(string pathFormat, int count) where T : Object
    {
        var variants = new Object[count];
        for (int i = 0; i < count; i++) variants[i] = Load<T>(string.Format(pathFormat, i + 1));
        return variants;
    }

    static void Fill(SerializedProperty array, params Object[] items)
    {
        array.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }
}
#endif
