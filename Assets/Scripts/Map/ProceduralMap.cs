using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BlockFactory))]
[DisallowMultipleComponent]
public class ProceduralMap : MonoBehaviour, ISpawnPointProvider
{
    [SerializeField] bool generateOnAwake = true;
    [SerializeField] int seed = 7301;
    [SerializeField] bool automaticSize = true;
    [Tooltip("자동 크기를 끄면 적용되는 크기입니다. Circle/Rectangle/Donut은 최소 25x25, Elongated는 한 변 25·다른 변 13칸 이상이어야 합니다.")]
    [SerializeField] Vector2Int size = new Vector2Int(31, 31);
    [SerializeField] MapShape shape = MapShape.Random;
    [SerializeField, Range(0f, 1f)] float obstacleDensity = 0.15f;
    [SerializeField] MapSymmetry symmetry = MapSymmetry.Rotate180;
    [SerializeField, Range(0f, 1f)] float noiseStrength = 0.5f;
    [SerializeField, Range(0.25f, 2f)] float noiseScale = 0.75f;

    const float cellSize = 2f;
    readonly List<IBlock> blocks = new List<IBlock>();

    public MapLayout GeneratedLayout { get; private set; }
    public IReadOnlyList<IBlock> Blocks => blocks;
    public Transform[] SpawnPoints { get; private set; }

    void Awake()
    {
        if (!generateOnAwake) return;
        // 비활성 오브젝트에 외부 설정으로 먼저 생성했다면 중복 생성하지 않는다.
        if (GeneratedLayout != null) return;
        Generate(CreateSettings());
    }

    public MapGenerationSettings CreateSettings()
    {
        return new MapGenerationSettings
        {
            Seed = seed,
            Size = automaticSize ? (Vector2Int?)null : size,
            Shape = shape,
            ObstacleDensity = obstacleDensity,
            Symmetry = symmetry,
            NoiseStrength = noiseStrength,
            NoiseScale = noiseScale
        };
    }

    public void Generate(MapGenerationSettings settings)
    {
        MapLayout generatedLayout = MapLayoutGenerator.Generate(settings);
        BlockFactory blockFactory = GetComponent<BlockFactory>();
        blockFactory.ValidateConfiguration();
        blockFactory.Initialize(settings.Seed);
        DestroyBlocks();

        GeneratedLayout = generatedLayout;
        SpawnPoints = blockFactory.SpawnPoints;
        string[] layout = generatedLayout.ToLayout();
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
    }

    // 맵을 비운다. 다음 Generate 전까지 레이아웃과 스폰 지점도 없다.
    public void Clear()
    {
        DestroyBlocks();
        GeneratedLayout = null;
        SpawnPoints = null;
    }

    void DestroyBlocks()
    {
        foreach (IBlock block in blocks)
        {
            Block instance = (Block)block;
            if (instance == null) continue;
            // Destroy가 프레임 끝에 처리되기 전에도 이전 맵의 충돌은 비활성화한다.
            instance.gameObject.SetActive(false);
            Destroy(instance.gameObject);
        }
        blocks.Clear();
    }
}
