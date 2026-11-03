using UnityEngine;

public enum MapCell
{
    Void,
    Floor,
    Wall
}

public sealed class MapLayout
{
    // 스폰 타일 하나가 차지하는 정사각형 영역의 한 변(칸 수). SpawnA/B는 그 영역의 최소 좌표 칸이다.
    public const int SpawnSize = 2;

    readonly MapCell[,] cells;

    public int Width => cells.GetLength(0);
    public int Height => cells.GetLength(1);
    public Vector2Int Size => new Vector2Int(Width, Height);
    public Vector2Int SpawnA { get; }
    public Vector2Int SpawnB { get; }
    public MapShape Shape { get; }

    // 경계 벽을 제외한 장애물 수 / 장애물 배치 전 내부 바닥 수.
    public float ActualObstacleDensity { get; }

    // x는 열, y는 행이며 ToLayout()[y][x]와 같은 좌표를 사용한다.
    public MapCell this[int x, int y] => cells[x, y];

    internal MapLayout(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB,
        MapShape shape, float actualObstacleDensity)
    {
        this.cells = cells;
        SpawnA = spawnA;
        SpawnB = spawnB;
        Shape = shape;
        ActualObstacleDensity = actualObstacleDensity;
    }

    public string[] ToLayout()
    {
        var layout = new string[Height];
        for (int y = 0; y < Height; y++)
        {
            var row = new char[Width];
            for (int x = 0; x < Width; x++)
            {
                row[x] = cells[x, y] == MapCell.Floor ? '.'
                    : cells[x, y] == MapCell.Wall ? '#' : ' ';
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
}
