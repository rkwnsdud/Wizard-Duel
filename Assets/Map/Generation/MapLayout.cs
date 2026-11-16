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
    readonly int[,] bushIds;

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

    // x는 열, y는 행이며 ToLayout()[y][x]와 같은 좌표를 사용한다.
    public MapCell this[int x, int y] => cells[x, y];

    internal MapLayout(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB,
        MapShape shape, float actualObstacleDensity, float actualBushDensity)
    {
        this.cells = cells;
        SpawnA = spawnA;
        SpawnB = spawnB;
        Shape = shape;
        ActualObstacleDensity = actualObstacleDensity;
        ActualBushDensity = actualBushDensity;
        bushIds = BushRegions.Label(cells, out int bushCount);
        BushCount = bushCount;
    }

    // (x, y) 칸이 속한 부시 덩어리 번호. 부시가 아니면 -1이다. 번호는 BushRegions.Label(ToLayout())과 같다.
    public int BushId(int x, int y) => bushIds[x, y];

    public string[] ToLayout()
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
                    MapCell.Wall => '#',
                    MapCell.Bush => BushRegions.Symbol,
                    _ => ' '
                };
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
