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
        random = new System.Random(seed);
        SpawnPoints = new Transform[spawnBlocks.Length];
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
