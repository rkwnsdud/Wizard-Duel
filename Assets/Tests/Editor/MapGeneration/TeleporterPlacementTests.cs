using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TeleporterPlacementTests
{
    // 생성기의 튜닝 값(MapLayoutGenerator)과 같다. 거리 하한은 조건을 가장 낮춘 배치 단계의 값이다.
    const int Spacing = 6;
    const int MinTravel = 8;
    const float TravelShare = 0.25f;

    static readonly Vector2Int[] steps = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    static IEnumerable<TestCaseData> Maps()
    {
        int[] seeds = { 7301, 11, -42 };
        foreach (MapShape shape in new[] { MapShape.Circle, MapShape.Rectangle, MapShape.Elongated, MapShape.Donut })
        foreach (MapSymmetry symmetry in Enum.GetValues(typeof(MapSymmetry)))
        {
            int seed = seeds[((int)shape + (int)symmetry) % seeds.Length];
            yield return new TestCaseData(shape, symmetry, seed).SetName($"Teleporters_{shape}_{symmetry}_seed{seed}");
        }
    }

    [TestCaseSource(nameof(Maps))]
    public void Generate_PlacesPairsThatFollowThePlacementRules(MapShape shape, MapSymmetry symmetry, int seed)
    {
        MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
        {
            Seed = seed, Shape = shape, Symmetry = symmetry, TeleporterPairs = 3
        });
        Assert.That(map.Teleporters.Count, Is.InRange(1, 3), "A default-sized map has room for at least one pair.");

        int[,] fromA = Walk(map, map.SpawnA);
        int minTravel = Math.Max(MinTravel, (int)Math.Ceiling(fromA[map.SpawnB.x, map.SpawnB.y] * TravelShare));
        var tiles = new List<Vector2Int>();
        for (int pair = 0; pair < map.Teleporters.Count; pair++)
        {
            TeleporterPair teleporter = map.Teleporters[pair];
            Assert.That(teleporter.A.y < teleporter.B.y || teleporter.A.y == teleporter.B.y && teleporter.A.x < teleporter.B.x,
                "A comes first in row-major order.");
            for (int side = 0; side < 2; side++)
            {
                Vector2Int tile = teleporter[side];
                for (int dy = 0; dy < TeleporterRegions.Size; dy++)
                    for (int dx = 0; dx < TeleporterRegions.Size; dx++)
                    {
                        var cell = new Vector2Int(tile.x + dx, tile.y + dy);
                        Assert.That(map[cell.x, cell.y], Is.EqualTo(MapCell.Floor), $"Tile cell {cell}");
                        Assert.That(map.TryGetTeleporter(cell.x, cell.y, out int foundPair, out int foundSide), Is.True);
                        Assert.That((foundPair, foundSide), Is.EqualTo((pair, side)));
                        Assert.That(MapLayout.InSpawnZone(cell, map.SpawnA, MapLayout.BushSpawnClearance), Is.False);
                        Assert.That(MapLayout.InSpawnZone(cell, map.SpawnB, MapLayout.BushSpawnClearance), Is.False);
                    }
                tiles.Add(tile);
            }
            int[,] fromTile = Walk(map, teleporter.A);
            Assert.That(fromTile[teleporter.B.x, teleporter.B.y], Is.GreaterThanOrEqualTo(minTravel), $"Pair {pair} travel");
            if (symmetry == MapSymmetry.Rotate180)
                Assert.That(teleporter.B, Is.EqualTo(new Vector2Int(map.Width - 2 - teleporter.A.x, map.Height - 2 - teleporter.A.y)));
        }
        // 타일을 모두 막아도 스폰 A에서 닿던 칸에는 그대로 닿는다. 타일을 밟아야만 지나갈 수 있는 길은 없다.
        int[,] around = Walk(map, map.SpawnA, tiles);
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (fromA[x, y] < 0 || map.IsTeleporter(x, y)) continue;
                Assert.That(around[x, y], Is.GreaterThanOrEqualTo(0), $"{cell} is only reachable across a teleporter tile");
            }

        for (int i = 0; i < tiles.Count; i++)
            for (int j = i + 1; j < tiles.Count; j++)
                Assert.That(Math.Max(Math.Abs(tiles[i].x - tiles[j].x), Math.Abs(tiles[i].y - tiles[j].y)),
                    Is.GreaterThanOrEqualTo(Spacing), $"Tiles {tiles[i]} and {tiles[j]}");

        Assert.That(TeleporterRegions.Parse(map.ToLayout()), Is.EqualTo(map.Teleporters));
    }

    [TestCase(MapShape.Random, 7301)]
    [TestCase(MapShape.Circle, 5)]
    [TestCase(MapShape.Elongated, -3)]
    [TestCase(MapShape.Donut, 99)]
    public void Generate_TeleportersLeaveTheRestOfTheMapUnchanged(MapShape shape, int seed)
    {
        var settings = new MapGenerationSettings { Seed = seed, Shape = shape, TeleporterPairs = 0 };
        MapLayout without = MapLayoutGenerator.Generate(settings);
        settings.TeleporterPairs = 3;
        MapLayout with = MapLayoutGenerator.Generate(settings);

        Assert.That(without.Teleporters, Is.Empty);
        Assert.That(with.Teleporters, Is.Not.Empty);
        Assert.That(with.Size, Is.EqualTo(without.Size));
        Assert.That(with.SpawnA, Is.EqualTo(without.SpawnA));
        Assert.That(with.SpawnB, Is.EqualTo(without.SpawnB));
        Assert.That(with.ActualObstacleDensity, Is.EqualTo(without.ActualObstacleDensity));
        Assert.That(with.ActualBushDensity, Is.EqualTo(without.ActualBushDensity));
        string[] withRows = with.ToLayout();
        for (int y = 0; y < withRows.Length; y++)
        {
            char[] row = withRows[y].ToCharArray();
            for (int x = 0; x < row.Length; x++)
                if (TeleporterRegions.IsSymbol(row[x])) row[x] = '.';
            withRows[y] = new string(row);
        }
        Assert.That(withRows, Is.EqualTo(without.ToLayout()));
    }

    [Test]
    public void Generate_PlacesTheSameTeleportersForTheSameInputs()
    {
        var settings = new MapGenerationSettings { Seed = 1234, TeleporterPairs = 3, Symmetry = MapSymmetry.None };
        Assert.That(MapLayoutGenerator.Generate(settings).Teleporters,
            Is.EqualTo(MapLayoutGenerator.Generate(settings).Teleporters));
    }

    [TestCase(-1)]
    [TestCase(TeleporterRegions.MaxPairs + 1)]
    public void Generate_RejectsTeleporterPairsOutOfRange(int pairs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MapLayoutGenerator.Generate(new MapGenerationSettings { TeleporterPairs = pairs }));
    }

    [Test]
    public void Parse_PairsTilesByDigitInRowMajorOrder()
    {
        var pairs = TeleporterRegions.Parse(new[]
        {
            "#########",
            "#22...11#",
            "#22...11#",
            "#.......#",
            "#11..22.#",
            "#11..22.#",
            "#########"
        });
        Assert.That(pairs.Count, Is.EqualTo(2));
        Assert.That(pairs[0], Is.EqualTo(new TeleporterPair(new Vector2Int(6, 1), new Vector2Int(1, 4))));
        Assert.That(pairs[0].A, Is.EqualTo(new Vector2Int(6, 1)));
        Assert.That(pairs[1].A, Is.EqualTo(new Vector2Int(1, 1)));
        Assert.That(pairs[1].B, Is.EqualTo(new Vector2Int(5, 4)));
        Assert.That(TeleporterRegions.Parse(new[] { "#...#", "#.*.#" }), Is.Empty);
    }

    [Test]
    public void Parse_RejectsMalformedTiles()
    {
        // 2×2가 아닌 모양, 타일이 셋인 쌍, 하나뿐인 쌍, 1 없이 쓴 2.
        Assert.Throws<FormatException>(() => TeleporterRegions.Parse(new[] { "11.11", "1..11" }));
        Assert.Throws<FormatException>(() => TeleporterRegions.Parse(new[] { "11.11.11", "11.11.11" }));
        Assert.Throws<FormatException>(() => TeleporterRegions.Parse(new[] { "11...", "11..." }));
        Assert.Throws<FormatException>(() => TeleporterRegions.Parse(new[] { "22.22", "22.22" }));
    }

    // start에서 상하좌우로 걸어서 가는 칸 수. 바닥과 부시를 지나고 blocked 타일 칸은 지나지 않는다. 닿지 않는 칸은 -1이다.
    static int[,] Walk(MapLayout map, Vector2Int start, List<Vector2Int> blocked = null)
    {
        var distances = new int[map.Width, map.Height];
        for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                distances[x, y] = -1;
        var queue = new Queue<Vector2Int>();
        distances[start.x, start.y] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int step in steps)
            {
                Vector2Int next = cell + step;
                if (next.x < 0 || next.y < 0 || next.x >= map.Width || next.y >= map.Height || distances[next.x, next.y] >= 0) continue;
                if (map[next.x, next.y] != MapCell.Floor && map[next.x, next.y] != MapCell.Bush) continue;
                if (blocked != null && blocked.Exists(tile => TeleporterRegions.Contains(tile, next))) continue;
                distances[next.x, next.y] = distances[cell.x, cell.y] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }
}
