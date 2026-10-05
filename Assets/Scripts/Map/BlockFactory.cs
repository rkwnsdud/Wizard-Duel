using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BlockFactory : MonoBehaviour
{
    [SerializeField] Block[] floorBlocks;
    [SerializeField] Block[] wallBlocks;
    [SerializeField] Block[] spawnBlocks = new Block[0];

    System.Random random;

    public Transform[] SpawnPoints { get; private set; }

    public void Initialize(int seed)
    {
        ValidateConfiguration();
        random = new System.Random(seed);
        SpawnPoints = new Transform[2];
    }

    public void ValidateConfiguration()
    {
        ValidateBlockArray(floorBlocks, "Floor Blocks");
        ValidateBlockArray(wallBlocks, "Wall Blocks");
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

    // . = 바닥, # = 바닥과 벽, B/O = 파랑/주황 스폰, 공백 = 빈 공간
    public IEnumerable<IBlock> CreateCell(char cell, Vector3 position, Transform parent)
    {
        int spawnIndex = cell == 'B' ? 0 : cell == 'O' ? 1 : -1;
        if (cell != '.' && cell != '#' && spawnIndex < 0) yield break;

        IBlock floor = CreateBlock(floorBlocks, position, parent, spawnIndex);
        if (spawnIndex >= 0)
            SpawnPoints[spawnIndex] = floor.Transform.Find("Spawn Position");
        yield return floor;

        if (cell == '#')
            yield return CreateBlock(wallBlocks, position, parent);
    }

    IBlock CreateBlock(Block[] variants, Vector3 position, Transform parent, int spawnIndex = -1)
    {
        Block block = variants[random.Next(variants.Length)];
        Quaternion rotation = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
        if (spawnIndex >= 0)
        {
            block = spawnBlocks[spawnIndex];
            rotation = Quaternion.identity;
        }

        return block.Create(parent, position, rotation);
    }
}
