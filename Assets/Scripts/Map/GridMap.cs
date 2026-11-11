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
    // 깐 부시 칸과 덩어리. generateOnAwake가 꺼져 있으면 null이다.
    public BushField Bushes { get; private set; }

    void Awake()
    {
        if (!generateOnAwake) return;

        BlockFactory blockFactory = GetComponent<BlockFactory>();
        blockFactory.ValidateConfiguration();
        blockFactory.Initialize(seed);
        SpawnPoints = blockFactory.SpawnPoints;
        Bushes = new BushField(layout, transform, cellSize);
        for (int z = 0; z < layout.Length; z++)
        {
            for (int x = 0; x < layout[z].Length; x++)
            {
                Vector3 position = new Vector3(
                    (x - (layout[z].Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - z) * cellSize);
                foreach (IBlock block in blockFactory.CreateCell(layout[z][x], position, transform))
                {
                    blocks.Add(block);
                    Bushes.Add(x, z, block);
                }
            }
        }
        blocks.AddRange(blockFactory.CreateSpawns(transform));
        // 손 레이아웃 맵은 테스트 씬의 전투 맵이므로 바로 등록한다.
        BushField.Activate(Bushes);
    }

    void OnDestroy() => BushField.Deactivate(Bushes);
}
