using System.Collections.Generic;
using UnityEngine;

public enum MapCell
{
    Void,
    Floor,
    Wall,
    // 바닥 위에 깐 덤불. 이동과 투사체는 막지 않고 시야만 가린다.
    Bush
}

public sealed class MapLayout
{
    // 스폰 타일 하나가 차지하는 정사각형 영역의 한 변(칸 수). SpawnA/B는 그 영역의 최소 좌표 칸이다.
    public const int SpawnSize = 2;

    // 스폰 영역 둘레로 장애물과 외곽 벽을 두지 않는 칸 수. 안전 구역은 SpawnSize + 2 * SpawnClearance칸 정사각형이다.
    public const int SpawnClearance = 1;

    // 스폰 영역 둘레로 부시를 두지 않는 칸 수. 안전 구역보다 2칸 더 넓어서 나오자마자 부시에 숨지 못한다.
    public const int BushSpawnClearance = SpawnClearance + 2;

    readonly MapCell[,] cells;
    readonly bool[,] destructibleWalls;
    readonly int[,] bushIds;
    readonly TeleporterPair[] teleporters;
    // 텔레포트 타일 칸에는 쌍 번호 * 2 + 쪽(0은 A, 1은 B)을, 나머지 칸에는 -1을 넣는다.
    readonly int[,] teleporterIds;

    public int Width => cells.GetLength(0);
    public int Height => cells.GetLength(1);
    public Vector2Int Size => new Vector2Int(Width, Height);
    public Vector2Int SpawnA { get; }
    public Vector2Int SpawnB { get; }
    public MapShape Shape { get; }

    // 경계 벽을 제외한 장애물(벽과 엄폐물) 수 / 장애물 배치 전 내부 바닥 수. 부시는 세지 않는다.
    public float ActualObstacleDensity { get; }

    // 부시 칸 수 / 장애물 배치 전 내부 바닥 수.
    public float ActualBushDensity { get; }

    // 부시 덩어리 수. 덩어리 번호는 0부터 BushCount - 1까지다.
    public int BushCount { get; }

    // 텔레포트 타일 쌍. 목록 순서가 쌍 번호이고, 번호는 TeleporterRegions.Parse(ToLayout())와 같다.
    // 타일 칸은 MapCell.Floor 그대로라서 이동·경로 찾기는 바닥으로 본다.
    public IReadOnlyList<TeleporterPair> Teleporters => teleporters;

    // x는 열, y는 행이며 ToLayout()[y][x]와 같은 좌표를 사용한다.
    public MapCell this[int x, int y] => cells[x, y];
    public bool IsDestructibleWall(int x, int y) => destructibleWalls[x, y];

    internal MapLayout(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB,
        MapShape shape, float actualObstacleDensity, float actualBushDensity,
        bool[,] destructibleWalls, TeleporterPair[] teleporters = null)
    {
        this.cells = cells;
        this.destructibleWalls = destructibleWalls;
        SpawnA = spawnA;
        SpawnB = spawnB;
        Shape = shape;
        ActualObstacleDensity = actualObstacleDensity;
        ActualBushDensity = actualBushDensity;
        bushIds = BushRegions.Label(cells, out int bushCount);
        BushCount = bushCount;
        this.teleporters = teleporters ?? new TeleporterPair[0];
        teleporterIds = new int[Width, Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                teleporterIds[x, y] = -1;
        for (int pair = 0; pair < this.teleporters.Length; pair++)
            for (int side = 0; side < 2; side++)
            {
                Vector2Int tile = this.teleporters[pair][side];
                for (int dy = 0; dy < TeleporterRegions.Size; dy++)
                    for (int dx = 0; dx < TeleporterRegions.Size; dx++)
                        teleporterIds[tile.x + dx, tile.y + dy] = pair * 2 + side;
            }
    }

    // (x, y) 칸이 텔레포트 타일이면 그 쌍 번호와 쪽(0은 A, 1은 B)을 준다.
    public bool TryGetTeleporter(int x, int y, out int pair, out int side)
    {
        int id = x >= 0 && y >= 0 && x < Width && y < Height ? teleporterIds[x, y] : -1;
        pair = id >= 0 ? id / 2 : -1;
        side = id >= 0 ? id % 2 : -1;
        return id >= 0;
    }

    public bool IsTeleporter(int x, int y) => TryGetTeleporter(x, y, out _, out _);

    // (x, y) 칸이 속한 부시 덩어리 번호. 부시가 아니면 -1이다. 번호는 BushRegions.Label(ToLayout())과 같다.
    public int BushId(int x, int y) => bushIds[x, y];

    public string[] ToLayout()
    {
        return ToLayout(false);
    }

    // Runtime block construction and fingerprints include the deterministic wall variant.
    public string[] ToBlockLayout() => ToLayout(true);

    string[] ToLayout(bool includeDestructibleWalls)
    {
        var layout = new string[Height];
        for (int y = 0; y < Height; y++)
        {
            var row = new char[Width];
            for (int x = 0; x < Width; x++)
            {
                row[x] = cells[x, y] switch
                {
                    MapCell.Floor => '.',
                    MapCell.Wall => includeDestructibleWalls && destructibleWalls[x, y] ? 'D' : '#',
                    MapCell.Bush => BushRegions.Symbol,
                    _ => ' '
                };
                if (TryGetTeleporter(x, y, out int pair, out _)) row[x] = TeleporterRegions.Symbol(pair);
                var cell = new Vector2Int(x, y);
                if (InSpawn(cell, SpawnA)) row[x] = 'B';
                if (InSpawn(cell, SpawnB)) row[x] = 'O';
            }
            layout[y] = new string(row);
        }
        return layout;
    }

    // cell이 spawn에서 시작하는 스폰 영역 안에 있는지.
    public static bool InSpawn(Vector2Int cell, Vector2Int spawn)
    {
        return cell.x >= spawn.x && cell.y >= spawn.y
            && cell.x < spawn.x + SpawnSize && cell.y < spawn.y + SpawnSize;
    }

    // cell이 spawn 영역과 그 둘레 SpawnClearance칸을 합친 안전 구역 안에 있는지.
    public static bool InSpawnZone(Vector2Int cell, Vector2Int spawn)
    {
        return InSpawnZone(cell, spawn, SpawnClearance);
    }

    // cell이 spawn 영역과 그 둘레 clearance칸을 합친 정사각형 안에 있는지.
    public static bool InSpawnZone(Vector2Int cell, Vector2Int spawn, int clearance)
    {
        return cell.x >= spawn.x - clearance && cell.y >= spawn.y - clearance
            && cell.x < spawn.x + SpawnSize + clearance && cell.y < spawn.y + SpawnSize + clearance;
    }
}
