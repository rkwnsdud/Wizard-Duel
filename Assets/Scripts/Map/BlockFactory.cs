using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BlockFactory : MonoBehaviour
{
    [SerializeField] Block[] floorBlocks;
    [SerializeField] Block[] wallBlocks;
    [SerializeField] Block[] spawnBlocks = new Block[0];
    [Tooltip("부시 칸(*)에 바닥 위로 까는 덤불. 부시가 없는 손 레이아웃만 쓰는 씬이면 비워 둬도 된다.")]
    [SerializeField] BushBlock[] bushBlocks = new BushBlock[0];

    System.Random random;
    readonly List<Vector3>[] spawnCells = { new List<Vector3>(), new List<Vector3>() };

    // 렌더러를 합치는 맵(전투 맵)에서만 쓴다. 합칠 블록은 렌더러를 뗀 템플릿으로 만들어 콜라이더만 남긴다.
    MapRendererCombiner combiner;
    readonly Dictionary<Block, Block> colliderTemplates = new Dictionary<Block, Block>();
    GameObject templateHolder;

    public Transform[] SpawnPoints { get; private set; }

    public void Initialize(int seed) => Initialize(seed, null);

    // combiner를 넘기면 바닥·벽 블록의 메시는 combiner에 모으고 블록에는 콜라이더만 만든다.
    // 블록을 깔 부모(CreateCell의 parent)는 combiner.Build에 넘길 부모와 같아야 한다. 다 깔고 나면 ReleaseTemplates를 부른다.
    public void Initialize(int seed, MapRendererCombiner combiner)
    {
        ValidateConfiguration();
        ReleaseTemplates();
        this.combiner = combiner;
        random = new System.Random(seed);
        SpawnPoints = new Transform[2];
        foreach (List<Vector3> cells in spawnCells) cells.Clear();
    }

    public void ValidateConfiguration()
    {
        ValidateBlockArray(floorBlocks, "Floor Blocks");
        ValidateBlockArray(wallBlocks, "Wall Blocks");
        if (bushBlocks != null)
            for (int i = 0; i < bushBlocks.Length; i++)
                if (bushBlocks[i] == null)
                    throw new InvalidOperationException($"BlockFactory Bush Blocks element {i} is not assigned.");
        if (spawnBlocks == null || spawnBlocks.Length < 2)
            throw new InvalidOperationException("BlockFactory requires two spawn block prefabs (Player 1 and Player 2).");

        for (int i = 0; i < 2; i++)
        {
            if (spawnBlocks[i] == null)
                throw new InvalidOperationException($"BlockFactory Spawn Blocks element {i} is not assigned.");
            if (spawnBlocks[i].transform.Find("Spawn Position") == null)
                throw new InvalidOperationException(
                    $"Spawn block '{spawnBlocks[i].name}' must contain a child named 'Spawn Position'.");
        }
    }

    static void ValidateBlockArray(Block[] blocks, string label)
    {
        if (blocks == null || blocks.Length == 0)
            throw new InvalidOperationException($"BlockFactory {label} must contain at least one prefab.");
        for (int i = 0; i < blocks.Length; i++)
            if (blocks[i] == null)
                throw new InvalidOperationException($"BlockFactory {label} element {i} is not assigned.");
    }

    // . = 바닥, # = 바닥과 벽, * = 바닥과 부시, B/O = 파랑/주황 스폰 영역, 공백 = 빈 공간
    // 스폰 칸은 모아 두었다가 CreateSpawns에서 영역 가운데에 스폰 타일 하나로 깐다.
    public IEnumerable<IBlock> CreateCell(char cell, Vector3 position, Transform parent)
    {
        int spawnIndex = cell == 'B' ? 0 : cell == 'O' ? 1 : -1;
        if (spawnIndex >= 0) spawnCells[spawnIndex].Add(position);
        if (cell != '.' && cell != '#' && cell != BushRegions.Symbol) yield break;

        yield return CreateBlock(floorBlocks, position, parent);
        if (cell == '#')
            yield return CreateBlock(wallBlocks, position, parent);
        if (cell == BushRegions.Symbol)
        {
            if (bushBlocks == null || bushBlocks.Length == 0)
                throw new InvalidOperationException("BlockFactory Bush Blocks must contain at least one prefab for '*' cells.");
            yield return CreateBlock(bushBlocks, position, parent);
        }
    }

    // 모든 칸을 깐 뒤 부른다. 스폰 타일 프리팹은 스폰 영역 전체를 덮는 크기다.
    public IEnumerable<IBlock> CreateSpawns(Transform parent)
    {
        for (int i = 0; i < spawnCells.Length; i++)
        {
            if (spawnCells[i].Count == 0) continue;
            Vector3 center = Vector3.zero;
            foreach (Vector3 position in spawnCells[i]) center += position;
            center /= spawnCells[i].Count;

            IBlock spawn = spawnBlocks[i].Create(parent, center, Quaternion.identity);
            SpawnPoints[i] = spawn.Transform.Find("Spawn Position");
            yield return spawn;
        }
    }

    IBlock CreateBlock(Block[] variants, Vector3 position, Transform parent)
    {
        Block block = variants[random.Next(variants.Length)];
        Quaternion rotation = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
        if (combiner != null)
        {
            // Block.Create가 놓는 자리와 같다: 프리팹의 위치에 칸 위치를 더하고, 회전은 바꾸고, 스케일은 프리팹 그대로다.
            Transform source = block.transform;
            Matrix4x4 local = Matrix4x4.TRS(source.localPosition + position, rotation, source.localScale);
            if (combiner.Add(block.gameObject, local)) block = ColliderTemplate(block);
        }
        return block.Create(parent, position, rotation);
    }

    // 블록 프리팹에서 메시 렌더러와, 그것만 있던 자식 오브젝트를 뗀 사본. 칸마다 렌더러를 만들었다 지우지 않도록 변형마다 하나만 만든다.
    // 꺼진 오브젝트 아래에 두어 사본 자체는 충돌하지 않고, 여기서 만든 블록은 켜진 부모 아래에 놓이므로 바로 충돌한다.
    Block ColliderTemplate(Block block)
    {
        if (colliderTemplates.TryGetValue(block, out Block template)) return template;
        if (templateHolder == null)
        {
            templateHolder = new GameObject("Collider Templates");
            templateHolder.SetActive(false);
            templateHolder.transform.SetParent(transform, false);
        }
        template = Instantiate(block, templateHolder.transform, false);
        template.name = block.name;
        // 사본에 지울 예정인 렌더러가 남아 있으면 그대로 복제되므로 플레이 중에도 바로 지운다.
        foreach (MeshRenderer renderer in template.GetComponentsInChildren<MeshRenderer>(true)) DestroyImmediate(renderer);
        foreach (MeshFilter filter in template.GetComponentsInChildren<MeshFilter>(true)) DestroyImmediate(filter);
        Transform[] children = template.GetComponentsInChildren<Transform>(true);
        // 자식이 부모보다 뒤에 오므로 뒤에서부터 보면 비게 된 부모도 이어서 지워진다.
        for (int i = children.Length - 1; i > 0; i--)
            if (children[i].childCount == 0 && children[i].GetComponents<Component>().Length == 1)
                DestroyImmediate(children[i].gameObject);
        colliderTemplates.Add(block, template);
        return template;
    }

    // 맵을 다 깐 뒤 부른다. 다음 맵은 프리팹을 다시 읽어 템플릿을 새로 만든다.
    public void ReleaseTemplates()
    {
        combiner = null;
        colliderTemplates.Clear();
        if (templateHolder == null) return;
        if (Application.isPlaying) Destroy(templateHolder);
        else DestroyImmediate(templateHolder);
        templateHolder = null;
    }
}
