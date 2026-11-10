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
    [Tooltip("벽과 엄폐물 칸 수 / 내부 바닥 칸 수. 부시는 세지 않는다.")]
    [SerializeField, Range(0f, 1f)] float obstacleDensity = 0.15f;
    [Tooltip("부시 칸 수 / 내부 바닥 칸 수. 벽과 엄폐물 다음에 두므로 이 값만 바꾸면 벽은 그대로다. 0이면 부시가 없다.")]
    [SerializeField, Range(0f, 1f)] float bushDensity = 0.05f;
    [SerializeField] MapSymmetry symmetry = MapSymmetry.Rotate180;
    [SerializeField, Range(0f, 1f)] float noiseStrength = 0.5f;
    [SerializeField, Range(0.25f, 2f)] float noiseScale = 0.75f;
    [Tooltip("이 맵이 갱신되면 같이 갱신할 배경. 같은 오브젝트 하위에 두면 자동으로 찾는다.")]
    [SerializeField] ArenaBackgroundBuilderNew background;

    const float cellSize = 1f;
    readonly List<IBlock> blocks = new List<IBlock>();
    readonly List<Transform> pieces = new List<Transform>();

    public MapLayout GeneratedLayout { get; private set; }
    // 깐 부시 칸과 덩어리. 맵을 지우면 null이다. 전투 맵이면 DuelArena가 BushField.Active로 등록한다.
    public BushField Bushes { get; private set; }
    public IReadOnlyList<IBlock> Blocks => blocks;
    // 맵을 이루는 조각 전체. 앞쪽은 Blocks와 같은 순서의 블록이고, 그 뒤에 배경이 깐 바닥 타일과 장식이 온다.
    // 배경도 맵의 일부라서 시작 화면·로비 화면의 물결은 이 목록을 함께 움직인다.
    public IReadOnlyList<Transform> Pieces => pieces;
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
            BushDensity = bushDensity,
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
        // 맵 블록을 다 깔고 나서 배경을 갱신한다. (서버/클라 모두 map.Generate를
        // 거치므로 NetworkDemo에서 따로 호출할 필요가 없다.)
        // 같은 오브젝트에 붙은 배경(=자식)만 갱신한다. 같은 씬의 다른 맵(타이틀/전투) 배경을 건드리지 않는다.
        // TitleBackdrop.HideMap처럼 맵이 꺼진 뒤 첫 생성이라면 자식이 비활성일 수 있으니 비활성 포함으로 찾는다.
        if (background == null) background = GetComponentInChildren<ArenaBackgroundBuilderNew>(true);
        if (background != null) background.Rebuild();
        // 배경 오브젝트 자체가 꺼져 있으면(HideMap) 다음에 켜질 때 Rebuild하도록 표시만 해 둔다.
        // Rebuild는 자식 파괴/생성이라 비활성 상태에서도 안전하게 돈다.

        foreach (IBlock block in blocks) pieces.Add(block.Transform);
        if (background != null) pieces.AddRange(background.Pieces);
    }

    // 맵을 비운다. 다음 Generate 전까지 레이아웃과 스폰 지점도 없다.
    public void Clear()
    {
        DestroyBlocks();
        GeneratedLayout = null;
        SpawnPoints = null;
    }

    void OnDestroy() => BushField.Deactivate(Bushes);

    void DestroyBlocks()
    {
        BushField.Deactivate(Bushes);
        Bushes = null;
        foreach (IBlock block in blocks)
        {
            Block instance = (Block)block;
            if (instance == null) continue;
            // Destroy가 프레임 끝에 처리되기 전에도 이전 맵의 충돌은 비활성화한다.
            instance.gameObject.SetActive(false);
            // 에디트 모드(EditMode 테스트)에서는 Destroy가 지우지 못하고 오류만 남긴다. 배경 빌더와 같이 바로 지운다.
            if (Application.isPlaying) Destroy(instance.gameObject);
            else DestroyImmediate(instance.gameObject);
        }
        blocks.Clear();
        pieces.Clear();
    }
}
