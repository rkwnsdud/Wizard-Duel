using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BlockFactory))]
public class GridMap : MonoBehaviour, ISpawnPointProvider
{
    [SerializeField] bool generateOnAwake = true;
    [SerializeField] int seed = 7301;

    [SerializeField] string[] layout =
    {
        "###########",
        "#.........#",
        "#..##.....#",
        "#.........#",
        "#.........#",
        "#.........#",
        "#.....##..#",
        "#.........#",
        "###########"
    };

    const float cellSize = 1f;

    readonly List<IBlock> blocks = new List<IBlock>();

    public IReadOnlyList<IBlock> Blocks => blocks;
    public Transform[] SpawnPoints { get; private set; }

    void Awake()
    {
        if (!generateOnAwake) return;

        BlockFactory blockFactory = GetComponent<BlockFactory>();
        blockFactory.ValidateConfiguration();
        blockFactory.Initialize(seed);
        SpawnPoints = blockFactory.SpawnPoints;
        for (int z = 0; z < layout.Length; z++)
        {
            for (int x = 0; x < layout[z].Length; x++)
            {
                Vector3 position = new Vector3(
                    (x - (layout[z].Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - z) * cellSize);
                blocks.AddRange(blockFactory.CreateCell(layout[z][x], position, transform));
            }
        }
        blocks.AddRange(blockFactory.CreateSpawns(transform));
    }
}
