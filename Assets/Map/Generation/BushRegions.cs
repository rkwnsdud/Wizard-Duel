using System;
using System.Collections.Generic;

// 부시 칸을 덩어리로 묶어 번호를 매긴다. 대각선까지 8방향으로 이어진 부시 칸이 한 덩어리다.
// 번호는 행 우선(y, 그다음 x)으로 처음 만나는 칸 순서대로 0부터 매긴다.
// 생성된 MapLayout과 손으로 쓴 GridMap 레이아웃이 같은 규칙으로 번호를 얻으므로, 모든 클라이언트가 같은 번호를 쓴다.
public static class BushRegions
{
    // 레이아웃 문자열에서 부시 칸을 나타내는 문자.
    public const char Symbol = '*';

    // 부시 칸에는 덩어리 번호를, 나머지 칸에는 -1을 넣은 [x, y] 배열을 돌려준다.
    public static int[,] Label(MapCell[,] cells, out int count)
    {
        if (cells == null) throw new ArgumentNullException(nameof(cells));
        return Label(cells.GetLength(0), cells.GetLength(1), (x, y) => cells[x, y] == MapCell.Bush, out count);
    }

    // layout[y][x]가 Symbol인 칸을 부시로 본다. 행 길이가 다르면 가장 긴 행에 맞추고 없는 칸은 부시가 아니다.
    public static int[,] Label(IReadOnlyList<string> layout, out int count)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        int width = 0;
        foreach (string row in layout) width = Math.Max(width, row?.Length ?? 0);
        return Label(width, layout.Count,
            (x, y) => layout[y] != null && x < layout[y].Length && layout[y][x] == Symbol, out count);
    }

    static int[,] Label(int width, int height, Func<int, int, bool> isBush, out int count)
    {
        var ids = new int[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                ids[x, y] = -1;

        count = 0;
        var queue = new Queue<(int x, int y)>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (ids[x, y] >= 0 || !isBush(x, y)) continue;
                ids[x, y] = count;
                queue.Enqueue((x, y));
                while (queue.Count > 0)
                {
                    (int cx, int cy) = queue.Dequeue();
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height
                                || ids[nx, ny] >= 0 || !isBush(nx, ny)) continue;
                            ids[nx, ny] = count;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                count++;
            }
        }
        return ids;
    }
}
