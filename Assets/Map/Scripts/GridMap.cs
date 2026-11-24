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
    readonly Dictionary<Vector2Int, IEnvironmentDestructible> destructibles = new Dictionary<Vector2Int, IEnvironmentDestructible>();

    public IReadOnlyList<IBlock> Blocks => blocks;
    public Transform[] SpawnPoints { get; private set; }
    // 깐 부시 칸과 덩어리. generateOnAwake가 꺼져 있으면 null이다.
    public BushField Bushes { get; private set; }
    // 깐 텔레포트 타일 쌍(레이아웃의 숫자 칸). generateOnAwake가 꺼져 있으면 null이다.
    public TeleporterField Teleporters { get; private set; }
    public IEnumerable<IEnvironmentDestructible> Destructibles => destructibles.Values;
    public bool ApplyEnvironmentDestruction(int x, int z, bool burning)
    {
        var key = new Vector2Int(x, z);
        if (!destructibles.TryGetValue(key, out IEnvironmentDestructible block) || block == null || block.IsDestroyed ||
            (burning ? !(block is BushBlock) : !(block is DestructibleWallBlock))) return false;
        destructibles.Remove(key);
        if (block is BushBlock) Bushes?.RemoveCell(x, z);
        block.ApplyDestroyed(burning);
        return true;
    }

    void Awake()
    {
        if (!generateOnAwake) return;

        BlockFactory blockFactory = GetComponent<BlockFactory>();
        blockFactory.ValidateConfiguration();
        blockFactory.Initialize(seed);
        SpawnPoints = blockFactory.SpawnPoints;
        Bushes = new BushField(layout, transform, cellSize);
        Teleporters = new TeleporterField(layout, transform, cellSize);
        destructibles.Clear();
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
                    if (block is Component component &&
                        component.GetComponent<IEnvironmentDestructible>() is IEnvironmentDestructible destructible)
                    {
                        destructible.SetCell(x, z);
                        destructibles[new Vector2Int(x, z)] = destructible;
                    }
                }
            }
        }
        blocks.AddRange(blockFactory.CreateSpawns(transform));
        blocks.AddRange(blockFactory.CreateTeleporters(Teleporters, transform));
        // 손 레이아웃 맵은 테스트 씬의 전투 맵이므로 바로 등록한다.
        BushField.Activate(Bushes);
        TeleporterField.Activate(Teleporters);
    }

    void OnDestroy()
    {
        BushField.Deactivate(Bushes);
        TeleporterField.Deactivate(Teleporters);
        destructibles.Clear();
    }
}
