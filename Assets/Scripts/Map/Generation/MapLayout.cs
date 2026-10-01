using UnityEngine;

public enum MapCell
{
    Void,
    Floor,
    Wall
}

public sealed class MapLayout
{
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
                if (SpawnA.x == x && SpawnA.y == y) row[x] = 'B';
                if (SpawnB.x == x && SpawnB.y == y) row[x] = 'O';
            }
            layout[y] = new string(row);
        }
        return layout;
    }
}
