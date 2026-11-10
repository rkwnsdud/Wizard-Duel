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

    public Transform[] SpawnPoints { get; private set; }

    public void Initialize(int seed)
    {
        ValidateConfiguration();
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
        return block.Create(parent, position, rotation);
    }
}
