using System;
using System.Collections.Generic;
using UnityEngine;

// 텔레포트 타일 한 쌍. A와 B는 2×2 타일의 최소 좌표 칸(x는 열, y는 행)이다.
// 레이아웃에서 행 우선(y, 그다음 x)으로 먼저 나오는 타일이 A다. 생성된 MapLayout과 손으로 쓴 레이아웃이 같은 순서를 쓴다.
public readonly struct TeleporterPair : IEquatable<TeleporterPair>
{
    public TeleporterPair(Vector2Int a, Vector2Int b)
    {
        bool swap = b.y < a.y || (b.y == a.y && b.x < a.x);
        A = swap ? b : a;
        B = swap ? a : b;
    }

    public Vector2Int A { get; }
    public Vector2Int B { get; }

    // 0이면 A, 1이면 B.
    public Vector2Int this[int side] => side == 0 ? A : B;

    public bool Equals(TeleporterPair other) => A == other.A && B == other.B;
    public override bool Equals(object obj) => obj is TeleporterPair other && Equals(other);
    public override int GetHashCode() => A.GetHashCode() * 397 ^ B.GetHashCode();
    public override string ToString() => $"({A.x},{A.y})-({B.x},{B.y})";
}

// 레이아웃 문자열의 텔레포트 타일. 쌍 번호 i의 두 타일은 숫자 '1' + i로 2×2칸씩 적는다.
// 타일 칸은 걸을 수 있는 바닥이고, 블록은 바닥 위에 타일 모양을 한 번 더 깐다.
public static class TeleporterRegions
{
    // 타일 하나가 차지하는 정사각형의 한 변(칸 수).
    public const int Size = 2;

    // 레이아웃 숫자로 적을 수 있는 쌍 수.
    public const int MaxPairs = 9;

    public static char Symbol(int pair)
    {
        if (pair < 0 || pair >= MaxPairs) throw new ArgumentOutOfRangeException(nameof(pair));
        return (char)('1' + pair);
    }

    public static bool IsSymbol(char cell) => cell >= '1' && cell < '1' + MaxPairs;

    // 칸 문자의 쌍 번호. 텔레포트 칸이 아니면 -1이다.
    public static int PairOf(char cell) => IsSymbol(cell) ? cell - '1' : -1;

    // tile(최소 좌표 칸)에서 시작하는 2×2 타일 안에 cell이 있는지.
    public static bool Contains(Vector2Int tile, Vector2Int cell) =>
        cell.x >= tile.x && cell.y >= tile.y && cell.x < tile.x + Size && cell.y < tile.y + Size;

    // layout[y][x]의 숫자 칸을 쌍으로 묶는다. 쌍 번호는 숫자 순서(1부터)이고, 숫자마다 2×2 타일이 정확히 두 개 있어야 한다.
    // 숫자가 1부터 빠짐없이 이어지지 않거나 타일 모양이 2×2가 아니면 FormatException을 던진다. 숫자가 없으면 빈 목록이다.
    public static List<TeleporterPair> Parse(IReadOnlyList<string> layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        var tiles = new List<Vector2Int>[MaxPairs];
        var claimed = new HashSet<Vector2Int>();
        for (int y = 0; y < layout.Count; y++)
        {
            string row = layout[y] ?? string.Empty;
            for (int x = 0; x < row.Length; x++)
            {
                int pair = PairOf(row[x]);
                if (pair < 0 || claimed.Contains(new Vector2Int(x, y))) continue;
                for (int dy = 0; dy < Size; dy++)
                    for (int dx = 0; dx < Size; dx++)
                    {
                        if (At(layout, x + dx, y + dy) != row[x] || !claimed.Add(new Vector2Int(x + dx, y + dy)))
                            throw new FormatException($"Teleporter '{row[x]}' at ({x},{y}) is not a {Size}x{Size} tile.");
                    }
                (tiles[pair] ??= new List<Vector2Int>()).Add(new Vector2Int(x, y));
            }
        }

        var pairs = new List<TeleporterPair>();
        for (int pair = 0; pair < MaxPairs; pair++)
        {
            if (tiles[pair] == null)
            {
                for (int later = pair + 1; later < MaxPairs; later++)
                    if (tiles[later] != null)
                        throw new FormatException($"Teleporter '{Symbol(later)}' is used but '{Symbol(pair)}' is missing.");
                break;
            }
            if (tiles[pair].Count != 2)
                throw new FormatException($"Teleporter '{Symbol(pair)}' has {tiles[pair].Count} tiles; a pair needs exactly 2.");
            pairs.Add(new TeleporterPair(tiles[pair][0], tiles[pair][1]));
        }
        return pairs;
    }

    static char At(IReadOnlyList<string> layout, int x, int y) =>
        y >= 0 && y < layout.Count && layout[y] != null && x >= 0 && x < layout[y].Length ? layout[y][x] : ' ';
}
