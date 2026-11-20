using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MapLayoutGeneratorTests
{
    // DuelSettings는 다른 어셈블리라 로비 화면 최대 부시 밀도(DuelSettings.MaxBushDensity)를 여기 따로 둔다.
    const float DuelSettingsMaxBushDensity = 0.2f;

    static readonly Vector2Int[] directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
    };

    // 모양×대칭×밀도는 모두 보고, 시드와 홀짝 크기는 조합마다 번갈아 쓴다. 전부 곱하면 느리기만 하다.
    static IEnumerable<TestCaseData> ValidMaps()
    {
        float[] densities = { 0f, 0.15f, 1f };
        int[] seeds = { 0, 7301, -11 };
        foreach (MapShape shape in Enum.GetValues(typeof(MapShape)))
        foreach (MapSymmetry symmetry in Enum.GetValues(typeof(MapSymmetry)))
        for (int d = 0; d < densities.Length; d++)
        {
            float density = densities[d];
            int seed = seeds[((int)shape + (int)symmetry + d) % seeds.Length];
            bool even = ((int)shape + d) % 2 == 1;
            Vector2Int size = shape == MapShape.Elongated
                ? new Vector2Int(even ? 48 : 47, even ? 18 : 17)
                : new Vector2Int(even ? 32 : 31, even ? 30 : 29);
            yield return new TestCaseData(shape, symmetry, density, seed, size)
                .SetName($"Connected_{shape}_{symmetry}_density{density}_seed{seed}_{size.x}x{size.y}");
        }
    }

    static IEnumerable<TestCaseData> NoisyMaps()
    {
        // (세기, 크기) 쌍. 세기 0은 크기를 무시하므로 한 번만 본다.
        var noises = new[] { (0f, 0.75f), (0.5f, 0.25f), (1f, 0.75f), (1f, 2f) };
        foreach (MapShape shape in new[] { MapShape.Circle, MapShape.Rectangle, MapShape.Elongated, MapShape.Donut })
        foreach (MapSymmetry symmetry in Enum.GetValues(typeof(MapSymmetry)))
        for (int n = 0; n < noises.Length; n++)
        {
            (float strength, float scale) = noises[n];
            bool even = ((int)shape + n) % 2 == 1;
            Vector2Int size = shape == MapShape.Elongated
                ? new Vector2Int(even ? 48 : 47, even ? 18 : 17)
                : new Vector2Int(even ? 32 : 31, even ? 30 : 29);
            yield return new TestCaseData(shape, symmetry, strength, scale, size)
                .SetName($"Noise_{shape}_{symmetry}_strength{strength}_scale{scale}_{size.x}x{size.y}");
        }
    }

    [TestCaseSource(nameof(NoisyMaps))]
    public void Generate_NoisyDenseMapsStayValidAndKeepBushRules(
        MapShape shape, MapSymmetry symmetry, float strength, float scale, Vector2Int size)
    {
        MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
        {
            Shape = shape, Symmetry = symmetry, Size = size, ObstacleDensity = 0.75f,
            NoiseStrength = strength, NoiseScale = scale
        });
        Assert.That(map.Size, Is.EqualTo(size));
        Assert.That(map.Shape, Is.EqualTo(shape));
        AssertValid(map, symmetry);
        Assert.That(CountEnclosedVoids(map), Is.EqualTo(shape == MapShape.Donut ? 1 : 0));
        Assert.That(map.ActualObstacleDensity, Is.InRange(0f, 0.75f));
        AssertBushRules(map, symmetry);
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_ZeroNoiseStrengthIgnoresNoiseScale(MapShape shape)
    {
        var settings = new MapGenerationSettings
        {
            Shape = shape, Size = new Vector2Int(35, 35), NoiseStrength = 0f, NoiseScale = 0.25f
        };
        MapLayout smallScale = MapLayoutGenerator.Generate(settings);
        settings.NoiseScale = 2f;
        MapLayout largeScale = MapLayoutGenerator.Generate(settings);
        Assert.That(largeScale.ToLayout(), Is.EqualTo(smallScale.ToLayout()));
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_NoiseStrengthAndScaleBothChangeGeometryAndAreRepeatable(MapShape shape)
    {
        var settings = new MapGenerationSettings
        {
            Shape = shape, Size = new Vector2Int(51, 41), ObstacleDensity = 0f, BushDensity = 0f,
            NoiseStrength = 0f, NoiseScale = 0.75f
        };
        MapLayout regular = MapLayoutGenerator.Generate(settings);
        settings.NoiseStrength = 1f;
        MapLayout noisy = MapLayoutGenerator.Generate(settings);
        MapLayout repeat = MapLayoutGenerator.Generate(settings);
        Assert.That(repeat.ToLayout(), Is.EqualTo(noisy.ToLayout()));
        Assert.That(CountDifferences(regular, noisy), Is.GreaterThan(regular.Width));
        settings.NoiseScale = 2f;
        MapLayout broadNoise = MapLayoutGenerator.Generate(settings);
        Assert.That(CountDifferences(noisy, broadNoise), Is.GreaterThan(0));
    }

    [TestCase(MapSymmetry.None)]
    [TestCase(MapSymmetry.Rotate180)]
    public void Generate_ElongatedNoiseBendsTheCenterline(MapSymmetry symmetry)
    {
        var settings = new MapGenerationSettings
        {
            Shape = MapShape.Elongated, Size = new Vector2Int(49, 31), ObstacleDensity = 0f, BushDensity = 0f,
            Symmetry = symmetry, NoiseStrength = 0f
        };
        MapLayout regular = MapLayoutGenerator.Generate(settings);
        settings.NoiseStrength = 1f;
        MapLayout bent = MapLayoutGenerator.Generate(settings);
        Assert.That(ColumnCenterRange(regular), Is.EqualTo(0));
        Assert.That(ColumnCenterRange(bent), Is.GreaterThan(2));
    }

    [TestCase(-0.01f)]
    [TestCase(1.01f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Generate_RejectsInvalidNoiseStrength(float strength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { NoiseStrength = strength }));
    }

    [TestCase(0f)]
    [TestCase(0.24f)]
    [TestCase(2.01f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Generate_RejectsInvalidNoiseScale(float scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { NoiseScale = scale }));
    }

    [TestCaseSource(nameof(ValidMaps))]
    public void Generate_PreservesShapeBoundariesConnectivityAndSafeSpawns(
        MapShape shape, MapSymmetry symmetry, float density, int seed, Vector2Int size)
    {
        var settings = new MapGenerationSettings
        {
            Seed = seed, Size = size, Shape = shape, Symmetry = symmetry, ObstacleDensity = density
        };
        MapLayout map = MapLayoutGenerator.Generate(settings);
        Assert.That(map.Size, Is.EqualTo(size));
        Assert.That(map.Shape, shape == MapShape.Random ? Is.Not.EqualTo(MapShape.Random) : Is.EqualTo(shape));
        AssertValid(map, symmetry);

        settings.ObstacleDensity = 0f;
        settings.BushDensity = 0f;
        MapLayout empty = MapLayoutGenerator.Generate(settings);
        int initialFloors = 0;
        int obstacles = 0;
        int bushes = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (empty[x, y] != MapCell.Floor)
                Assert.That(map[x, y], Is.EqualTo(empty[x, y]), "Shape or boundary changed during obstacle placement.");
            else
            {
                initialFloors++;
                if (map[x, y] == MapCell.Wall) obstacles++;
                else if (map[x, y] == MapCell.Bush) bushes++;
            }
        }
        Assert.That(map.SpawnA, Is.EqualTo(empty.SpawnA));
        Assert.That(map.SpawnB, Is.EqualTo(empty.SpawnB));
        Assert.That(map.ActualObstacleDensity, Is.EqualTo((float)obstacles / initialFloors).Within(0.000001f));
        Assert.That(map.ActualObstacleDensity, Is.InRange(0f, density));
        Assert.That(map.ActualBushDensity, Is.EqualTo((float)bushes / initialFloors).Within(0.000001f));
        if (density == 0f) Assert.That(obstacles, Is.Zero);
        if (density == 1f) Assert.That(map.ActualObstacleDensity, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(CountEnclosedVoids(map), Is.EqualTo(map.Shape == MapShape.Donut ? 1 : 0));

        foreach (List<Vector2Int> piece in ObstaclePieces(map, empty))
        {
            Assert.That(piece.Count, Is.GreaterThan(1), $"Single-cell obstacle at {piece[0]}.");
            foreach (Vector2Int cell in piece)
                Assert.That(InSolidBlock(map, cell), Is.False, $"Obstacle at {cell} is part of a solid 3x3 wall block.");
        }
        Assert.That(LongestObstacleCorridor(map, empty), Is.LessThanOrEqualTo(2),
            "Obstacles must not form long one-cell corridors.");
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_MixesConnectedWallsWithStandaloneCover(MapShape shape)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            // 작은 맵에서도 벽 단계와 엄폐물 단계에 예산이 넉넉하도록 로비 최대 밀도로 본다.
            var settings = new MapGenerationSettings { Seed = seed, Shape = shape, ObstacleDensity = 0.25f };
            MapLayout map = MapLayoutGenerator.Generate(settings);
            settings.ObstacleDensity = 0f;
            settings.BushDensity = 0f;
            MapLayout empty = MapLayoutGenerator.Generate(settings);

            int walls = 0, cover = 0;
            foreach (List<Vector2Int> piece in ObstaclePieces(map, empty))
            {
                if (piece.Count >= 5) walls++;
                else if (IsStandaloneCover(map, piece)) cover++;
            }
            Assert.That(walls, Is.GreaterThan(0), $"Seed {seed} has no connected wall.");
            Assert.That(cover, Is.GreaterThan(0), $"Seed {seed} has no standalone pillar or short wall.");
        }
    }

    [TestCaseSource(nameof(ValidMaps))]
    public void Generate_PlacesSeparatedBushClustersAwayFromSpawns(
        MapShape shape, MapSymmetry symmetry, float density, int seed, Vector2Int size)
    {
        var settings = new MapGenerationSettings
        {
            Seed = seed, Size = size, Shape = shape, Symmetry = symmetry,
            ObstacleDensity = density, BushDensity = density * 0.5f
        };
        MapLayout map = MapLayoutGenerator.Generate(settings);
        AssertBushRules(map, symmetry);
        // 부시 밀도가 0이 아니면 덩어리 하나(대칭이면 한 쌍)는 둔다. 벽이 바닥을 거의 다 덮은 맵은 자리가 없을 수 있다.
        if (settings.BushDensity == 0f) Assert.That(map.BushCount, Is.Zero);
        else if (density <= 0.25f) Assert.That(map.BushCount, Is.GreaterThan(0), "A nonzero bush density should place bushes.");
    }

    // 기본 부시 밀도 0.05에서 부시가 바닥의 약 5%를 덮는지 본다. 덩어리 단위로 두므로 몇 칸은 못 채울 수 있다.
    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_DefaultBushDensityCoversAboutFivePercentOfTheFloor(MapShape shape)
    {
        double densitySum = 0;
        const int seeds = 16;
        for (int seed = 0; seed < seeds; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings { Seed = seed, Shape = shape });
            Assert.That(map.BushCount, Is.GreaterThan(0), $"Seed {seed} has no bush.");
            densitySum += map.ActualBushDensity;
        }
        Assert.That(densitySum / seeds, Is.InRange(0.04, 0.0525));
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_BushDensitySetsTheBushCells(MapShape shape)
    {
        foreach (float bushDensity in new[] { 0f, 0.1f, DuelSettingsMaxBushDensity })
        {
            double densitySum = 0;
            const int seeds = 8;
            for (int seed = 0; seed < seeds; seed++)
            {
                var settings = new MapGenerationSettings { Seed = seed, Shape = shape, BushDensity = bushDensity };
                MapLayout map = MapLayoutGenerator.Generate(settings);
                AssertBushRules(map, settings.Symmetry);
                settings.ObstacleDensity = 0f;
                settings.BushDensity = 0f;
                int floors = CountFloors(MapLayoutGenerator.Generate(settings));
                int bushes = 0;
                for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    if (map[x, y] == MapCell.Bush) bushes++;
                string where = $"Seed {seed} bush density {bushDensity}";
                Assert.That(map.ActualBushDensity, Is.EqualTo((float)bushes / floors).Within(0.000001f), where);
                Assert.That(bushes, Is.LessThanOrEqualTo(Math.Max((int)Math.Round(bushDensity * floors), bushDensity > 0f ? 8 : 0)),
                    $"{where} places more bushes than asked.");
                if (bushDensity == 0f) Assert.That(bushes, Is.Zero, $"{where} must not place bushes.");
                else Assert.That(bushes, Is.GreaterThan(0), $"{where} has no bushes.");
                densitySum += map.ActualBushDensity;
            }
            Assert.That(densitySum / seeds, Is.InRange(bushDensity * 0.8, bushDensity + 0.0025), $"Mean bush density for {bushDensity}.");
        }
    }

    // 부시는 벽과 엄폐물이 다 선 뒤에 두므로, 부시 밀도만 바꾸면 벽·엄폐물·스폰·장애물 밀도는 그대로다.
    [TestCase(MapShape.Circle, MapSymmetry.Rotate180)]
    [TestCase(MapShape.Rectangle, MapSymmetry.Rotate180)]
    [TestCase(MapShape.Elongated, MapSymmetry.Rotate180)]
    [TestCase(MapShape.Donut, MapSymmetry.Rotate180)]
    [TestCase(MapShape.Circle, MapSymmetry.None)]
    [TestCase(MapShape.Donut, MapSymmetry.None)]
    public void Generate_BushDensityKeepsWallsFixed(MapShape shape, MapSymmetry symmetry)
    {
        foreach (float obstacleDensity in new[] { 0f, 0.15f, 0.25f })
        for (int seed = 0; seed < 2; seed++)
        {
            var settings = new MapGenerationSettings
            {
                Seed = seed, Shape = shape, Symmetry = symmetry, ObstacleDensity = obstacleDensity, BushDensity = 0f
            };
            MapLayout bare = MapLayoutGenerator.Generate(settings);
            foreach (float bushDensity in new[] { 0.05f, DuelSettingsMaxBushDensity })
            {
                settings.BushDensity = bushDensity;
                MapLayout map = MapLayoutGenerator.Generate(settings);
                string where = $"Seed {seed} obstacles {obstacleDensity} bushes {bushDensity}";
                Assert.That(map.Size, Is.EqualTo(bare.Size), where);
                Assert.That(map.SpawnA, Is.EqualTo(bare.SpawnA), where);
                Assert.That(map.SpawnB, Is.EqualTo(bare.SpawnB), where);
                Assert.That(map.ActualObstacleDensity, Is.EqualTo(bare.ActualObstacleDensity), where);
                for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    MapCell expected = bare[x, y];
                    MapCell actual = map[x, y];
                    if (actual == MapCell.Bush)
                        Assert.That(expected, Is.EqualTo(MapCell.Floor), $"{where}: bush at ({x}, {y}) is not on floor.");
                    else Assert.That(actual, Is.EqualTo(expected), $"{where}: cell ({x}, {y}) changed.");
                }
            }
        }
    }

    // 부시가 2×2 덩어리만이 아니라 여러 모양으로 생기는지 본다. 모양마다 맵 몇 장 안에 한 번은 나와야 한다.
    // 꼭 맞는 2×2는 사각형 가중치가 가장 낮고 칸을 덧붙이기도 해서 맵 네댓 장에 한 번꼴이라 24장을 본다.
    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_MixesBushShapes(MapShape shape)
    {
        var found = new HashSet<string>();
        for (int seed = 0; seed < 24; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed, Shape = shape, ObstacleDensity = 0.08f, BushDensity = 0.12f
            });
            foreach (List<Vector2Int> bush in BushClusters(map)) found.UnionWith(BushKinds(map, bush));
        }
        Assert.That(found, Is.SupersetOf(new[] { "2x2", "2x3", "line", "wall", "wide" }));
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_PlacesAContestedBushBetweenTheSpawns(MapShape shape)
    {
        int contested = 0;
        const int seeds = 16;
        for (int seed = 0; seed < seeds; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings { Seed = seed, Shape = shape });
            int[,] fromA = Distances(map, map.SpawnA);
            int[,] fromB = Distances(map, map.SpawnB);
            bool found = false;
            for (int y = 0; y < map.Height && !found; y++)
            for (int x = 0; x < map.Width && !found; x++)
                found = map[x, y] == MapCell.Bush && Math.Abs(fromA[x, y] - fromB[x, y]) <= 1;
            if (found) contested++;
        }
        Assert.That(contested, Is.GreaterThanOrEqualTo(seeds * 4 / 5),
            "Most symmetric maps should have a bush the same path distance from both spawns.");
    }

    // 길쭉한 맵과 도넛은 통로가 좁아 기본 부시 밀도에서는 덩어리 두세 개가 대부분 벽 옆에 선다.
    // 부시 밀도를 넉넉히 줘서, 자리가 있으면 벽 옆과 빈터 양쪽에 모두 생기는지 본다.
    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_GrowsBushesAlongWallsAndInOpenGround(MapShape shape)
    {
        int alongWalls = 0, inOpen = 0;
        for (int seed = 0; seed < 8; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed, Shape = shape, ObstacleDensity = 0.125f, BushDensity = 0.125f
            });
            foreach (List<Vector2Int> bush in BushClusters(map))
            {
                int touching = 0;
                foreach (Vector2Int cell in bush)
                foreach (Vector2Int direction in directions)
                    if (IsCell(map, cell + direction, MapCell.Wall))
                    {
                        touching++;
                        break;
                    }
                if (touching >= 2) alongWalls++;
                if (touching == 0) inOpen++;
            }
        }
        Assert.That(alongWalls, Is.GreaterThan(0), "No bush grows along a wall.");
        Assert.That(inOpen, Is.GreaterThan(0), "No bush stands in open ground.");
    }

    [Test]
    public void BushRegions_JoinsDiagonalNeighborsAndNumbersInRowMajorOrder()
    {
        string[] layout =
        {
            "*..**",
            ".*.",
            "....*",
            "**..*",
        };
        int[,] ids = BushRegions.Label(layout, out int count);
        Assert.That(count, Is.EqualTo(4));
        Assert.That(ids.GetLength(0), Is.EqualTo(5));
        Assert.That(ids.GetLength(1), Is.EqualTo(4));
        Assert.That(ids[0, 0], Is.EqualTo(0));
        Assert.That(ids[1, 1], Is.EqualTo(0), "Diagonal bush cells are one bush.");
        Assert.That(ids[3, 0], Is.EqualTo(1));
        Assert.That(ids[4, 0], Is.EqualTo(1));
        Assert.That(ids[4, 2], Is.EqualTo(2));
        Assert.That(ids[4, 3], Is.EqualTo(2));
        Assert.That(ids[0, 3], Is.EqualTo(3));
        Assert.That(ids[1, 3], Is.EqualTo(3));
        Assert.That(ids[2, 0], Is.EqualTo(-1));
        Assert.That(ids[4, 1], Is.EqualTo(-1), "Cells past a short row are not bushes.");
    }

    [TestCase(MapShape.Random)]
    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_IsRepeatableForTheSameInputs(MapShape shape)
    {
        var settings = new MapGenerationSettings { Shape = shape };
        MapLayout first = MapLayoutGenerator.Generate(settings);
        MapLayout second = MapLayoutGenerator.Generate(settings);
        Assert.That(second.ToLayout(), Is.EqualTo(first.ToLayout()));
        Assert.That(second.Shape, Is.EqualTo(first.Shape));
        Assert.That(second.Size, Is.EqualTo(first.Size));
        Assert.That(second.ActualObstacleDensity, Is.EqualTo(first.ActualObstacleDensity));
        Assert.That(second.ActualBushDensity, Is.EqualTo(first.ActualBushDensity));
        Assert.That(settings.Size, Is.Null);
        Assert.That(settings.Shape, Is.EqualTo(shape));
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_AutomaticSizesStayInShapeRanges(MapShape shape)
    {
        for (int seed = 0; seed < 3; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed, Shape = shape, ObstacleDensity = 0f, BushDensity = 0f
            });
            if (shape == MapShape.Elongated)
            {
                Assert.That(Math.Max(map.Width, map.Height), Is.InRange(41, 61));
                Assert.That(Math.Min(map.Width, map.Height), Is.InRange(13, 19));
            }
            else
            {
                Assert.That(map.Width, Is.InRange(25, 41));
                Assert.That(map.Height, Is.InRange(25, 41));
            }
            AssertValid(map, MapSymmetry.Rotate180);
        }
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_DifferentSeedsVaryTheOutline(MapShape shape)
    {
        var outlines = new HashSet<string>();
        for (int seed = 0; seed < 8; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed, Size = new Vector2Int(35, 35), Shape = shape, ObstacleDensity = 0f, BushDensity = 0f
            });
            outlines.Add(string.Join("\n", map.ToLayout()).Replace('B', '.').Replace('O', '.'));
        }
        Assert.That(outlines.Count, Is.GreaterThan(1));
    }

    [Test]
    public void Generate_ShapesHaveDistinctGeometry()
    {
        var settings = new MapGenerationSettings { Size = new Vector2Int(35, 35), ObstacleDensity = 0f, BushDensity = 0f };
        settings.Shape = MapShape.Circle;
        MapLayout circle = MapLayoutGenerator.Generate(settings);
        settings.Shape = MapShape.Rectangle;
        MapLayout rectangle = MapLayoutGenerator.Generate(settings);
        settings.Shape = MapShape.Donut;
        MapLayout donut = MapLayoutGenerator.Generate(settings);
        settings.Shape = MapShape.Elongated;
        MapLayout elongated = MapLayoutGenerator.Generate(settings);
        Assert.That(CountFloors(rectangle), Is.GreaterThan(CountFloors(circle)));
        Assert.That(circle[17, 17], Is.EqualTo(MapCell.Floor));
        Assert.That(donut[17, 17], Is.EqualTo(MapCell.Void));

        int minX = elongated.Width, maxX = 0, minY = elongated.Height, maxY = 0;
        for (int y = 0; y < elongated.Height; y++)
        for (int x = 0; x < elongated.Width; x++)
        {
            if (elongated[x, y] == MapCell.Void) continue;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        Assert.That(maxX - minX + 1, Is.GreaterThan(2 * (maxY - minY + 1)));
    }

    [Test]
    public void Generate_SymmetricSpawnsUseTheMostDistantSafePair()
    {
        MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
        {
            Size = new Vector2Int(31, 31), Shape = MapShape.Donut, ObstacleDensity = 0f, BushDensity = 0f
        });
        int[,] selectedDistances = Distances(map, map.SpawnA);
        int selected = selectedDistances[map.SpawnB.x, map.SpawnB.y];
        for (int y = 1; y < map.Height - 1; y++)
        for (int x = 1; x < map.Width - 1; x++)
        {
            var candidate = new Vector2Int(x, y);
            if (!SafeSpawn(map, candidate)) continue;
            Vector2Int opposite = Opposite(map, candidate);
            int[,] distances = Distances(map, candidate);
            Assert.That(distances[opposite.x, opposite.y], Is.LessThanOrEqualTo(selected));
        }
    }

    [Test]
    public void Generate_DoesNotConsumeUnityRandomState()
    {
        UnityEngine.Random.State state = UnityEngine.Random.state;
        try
        {
            float expected = UnityEngine.Random.value;
            UnityEngine.Random.state = state;
            MapLayoutGenerator.Generate(new MapGenerationSettings());
            Assert.That(UnityEngine.Random.value, Is.EqualTo(expected));
        }
        finally
        {
            UnityEngine.Random.state = state;
        }
    }

    [Test]
    public void ToLayout_UsesGridMapSymbolsAndRowCoordinates()
    {
        MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings { Shape = MapShape.Donut });
        string[] rows = map.ToLayout();
        Assert.That(rows.Length, Is.EqualTo(map.Height));
        int blueSpawns = 0, orangeSpawns = 0;
        for (int y = 0; y < map.Height; y++)
        {
            Assert.That(rows[y].Length, Is.EqualTo(map.Width));
            for (int x = 0; x < map.Width; x++)
            {
                char symbol = rows[y][x];
                if (symbol == 'B') blueSpawns++;
                else if (symbol == 'O') orangeSpawns++;
                else Assert.That(symbol, Is.EqualTo(map[x, y] == MapCell.Floor ? '.'
                    : map[x, y] == MapCell.Wall ? '#' : map[x, y] == MapCell.Bush ? '*' : ' '));
            }
        }
        Assert.That(blueSpawns, Is.EqualTo(MapLayout.SpawnSize * MapLayout.SpawnSize));
        Assert.That(orangeSpawns, Is.EqualTo(MapLayout.SpawnSize * MapLayout.SpawnSize));
        Assert.That(rows[map.SpawnA.y][map.SpawnA.x], Is.EqualTo('B'));
        Assert.That(rows[map.SpawnB.y][map.SpawnB.x], Is.EqualTo('O'));
    }

    [Test]
    public void Generate_RejectsNullSettings()
    {
        Assert.Throws<ArgumentNullException>(() => MapLayoutGenerator.Generate(null));
    }

    [TestCase(0, 31)]
    [TestCase(31, 0)]
    [TestCase(-1, 31)]
    [TestCase(31, -1)]
    public void Generate_RejectsNonpositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { Size = new Vector2Int(width, height) }));
    }

    [TestCase(-0.01f)]
    [TestCase(1.01f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Generate_RejectsInvalidDensity(float density)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { ObstacleDensity = density }));
    }

    [TestCase(-0.01f)]
    [TestCase(1.01f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Generate_RejectsInvalidBushDensity(float bushDensity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { BushDensity = bushDensity }));
    }

    [Test]
    public void Generate_RejectsUnknownShapeAndSymmetry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { Shape = (MapShape)99 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(
            new MapGenerationSettings { Symmetry = (MapSymmetry)99 }));
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_RejectsSizeBelowShapeMinimumWithoutChangingSettings(MapShape shape)
    {
        var settings = new MapGenerationSettings { Size = new Vector2Int(5, 5), Shape = shape };
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => MapLayoutGenerator.Generate(settings));
        Assert.That(error.Message, Does.Contain(shape.ToString()).And.Contain("require a size"));
        Assert.That(settings.Size, Is.EqualTo(new Vector2Int(5, 5)));
        Assert.That(settings.Shape, Is.EqualTo(shape));
    }

    [Test]
    public void Generate_RandomShapeWithFixedSizeChoosesACompatibleShape()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed, Shape = MapShape.Random, Size = new Vector2Int(30, 20), ObstacleDensity = 0f, BushDensity = 0f
            });
            Assert.That(map.Shape, Is.EqualTo(MapShape.Elongated));
        }
    }

    [TestCase(MapShape.Circle, 25, 25)]
    [TestCase(MapShape.Rectangle, 25, 25)]
    [TestCase(MapShape.Donut, 25, 25)]
    [TestCase(MapShape.Elongated, 41, 13)]
    [TestCase(MapShape.Elongated, 13, 41)]
    [TestCase(MapShape.Elongated, 25, 13)]
    [TestCase(MapShape.Elongated, 13, 25)]
    public void Generate_SupportedMinimumSizesProduceValidMaps(MapShape shape, int width, int height)
    {
        foreach (int seed in new[] { 0, 1, 7301 })
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed,
                Shape = shape,
                Size = new Vector2Int(width, height),
                ObstacleDensity = 0.15f
            });
            AssertValid(map, MapSymmetry.Rotate180);
        }
    }

    static int CountDifferences(MapLayout first, MapLayout second)
    {
        int count = 0;
        for (int y = 0; y < first.Height; y++)
        for (int x = 0; x < first.Width; x++)
            if (first[x, y] != second[x, y]) count++;
        return count;
    }

    static double ColumnCenterRange(MapLayout map)
    {
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        for (int x = 0; x < map.Width; x++)
        {
            int first = -1, last = -1;
            for (int y = 0; y < map.Height; y++)
            {
                if (map[x, y] == MapCell.Void) continue;
                if (first < 0) first = y;
                last = y;
            }
            if (first < 0) continue;
            double center = (first + last) * 0.5;
            minimum = Math.Min(minimum, center);
            maximum = Math.Max(maximum, center);
        }
        return maximum - minimum;
    }

    static void AssertValid(MapLayout map, MapSymmetry symmetry)
    {
        Assert.That(map.SpawnA, Is.Not.EqualTo(map.SpawnB));
        Assert.That(SafeSpawn(map, map.SpawnA), Is.True, "Spawn A must have a clear 4x4 safe zone.");
        Assert.That(SafeSpawn(map, map.SpawnB), Is.True, "Spawn B must have a clear 4x4 safe zone.");
        int[,] distances = Distances(map, map.SpawnA);
        int reachable = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (distances[x, y] >= 0) reachable++;
            if (map.IsDestructibleWall(x, y))
            {
                Assert.That(map[x, y], Is.EqualTo(MapCell.Wall), $"Destructible cell {x},{y} must be a wall.");
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    bool insideArena = InBounds(map, new Vector2Int(nx, ny)) && map[nx, ny] != MapCell.Void;
                    Assert.That(insideArena, Is.True,
                        $"Destructible wall at {x},{y} touches the arena perimeter at {nx},{ny}.");
                }
            }
            if (Walkable(map, x, y))
            {
                // 칸마다 Assert.That을 부르면 느리다. 어긋난 칸에서만 실패를 알린다.
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!InBounds(map, new Vector2Int(x + dx, y + dy)) || map[x + dx, y + dy] == MapCell.Void)
                        Assert.Fail($"Floor at {x},{y} touches an unsealed edge.");
            }
            if (symmetry == MapSymmetry.Rotate180)
                Assert.That(map[x, y], Is.EqualTo(map[map.Width - 1 - x, map.Height - 1 - y]));
            if (x + 1 < map.Width && y + 1 < map.Height)
            {
                bool falling = map[x, y] == MapCell.Wall && map[x + 1, y + 1] == MapCell.Wall
                    && Walkable(map, x + 1, y) && Walkable(map, x, y + 1);
                bool rising = map[x + 1, y] == MapCell.Wall && map[x, y + 1] == MapCell.Wall
                    && Walkable(map, x, y) && Walkable(map, x + 1, y + 1);
                Assert.That(falling || rising, Is.False, $"Walls touch only diagonally at {x},{y}.");
            }
        }
        Assert.That(reachable, Is.EqualTo(CountFloors(map)), "All floor and bush cells must be connected.");
        Assert.That(distances[map.SpawnB.x, map.SpawnB.y], Is.GreaterThan(0));
        if (symmetry == MapSymmetry.Rotate180)
            Assert.That(map.SpawnB, Is.EqualTo(Opposite(map, map.SpawnA)));
    }

    // 부시 덩어리 번호와 생성 규칙을 확인한다.
    static void AssertBushRules(MapLayout map, MapSymmetry symmetry)
    {
        int[,] fromLayout = BushRegions.Label(map.ToLayout(), out int layoutCount);
        Assert.That(layoutCount, Is.EqualTo(map.BushCount), "Layout labels must match the generated bush count.");
        int nextId = 0;
        var mirrorIds = new Dictionary<int, int>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int id = map.BushId(x, y);
            Assert.That(fromLayout[x, y], Is.EqualTo(id), $"Layout label differs at {x},{y}.");
            if (map[x, y] != MapCell.Bush)
            {
                Assert.That(id, Is.EqualTo(-1));
                continue;
            }
            Assert.That(id, Is.InRange(0, nextId), "Bush ids must follow row-major order.");
            if (id == nextId) nextId++;

            var cell = new Vector2Int(x, y);
            Assert.That(MapLayout.InSpawnZone(cell, map.SpawnA, MapLayout.BushSpawnClearance)
                || MapLayout.InSpawnZone(cell, map.SpawnB, MapLayout.BushSpawnClearance), Is.False,
                $"Bush at {cell} is too close to a spawn.");
            bool doorway = (IsCell(map, cell + Vector2Int.left, MapCell.Wall) && IsCell(map, cell + Vector2Int.right, MapCell.Wall))
                || (IsCell(map, cell + Vector2Int.up, MapCell.Wall) && IsCell(map, cell + Vector2Int.down, MapCell.Wall));
            Assert.That(doorway, Is.False, $"Bush at {cell} fills a one-cell gap between walls.");
            // 서로 다른 덩어리는 대각선까지 2칸 이상 떨어져 있어야 화면에서도 따로 보인다.
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                Vector2Int near = cell + new Vector2Int(dx, dy);
                if (IsCell(map, near, MapCell.Bush))
                    Assert.That(map.BushId(near.x, near.y), Is.EqualTo(id), $"Bushes at {cell} and {near} are too close.");
            }

            if (symmetry != MapSymmetry.Rotate180) continue;
            int mirrorId = map.BushId(map.Width - 1 - x, map.Height - 1 - y);
            if (mirrorIds.TryGetValue(id, out int expected))
                Assert.That(mirrorId, Is.EqualTo(expected), $"Bush {id} is not mirrored as one bush.");
            else mirrorIds[id] = mirrorId;
        }
        Assert.That(nextId, Is.EqualTo(map.BushCount));

        foreach (List<Vector2Int> bush in BushClusters(map))
        {
            string where = $"Bush {map.BushId(bush[0].x, bush[0].y)} at {bush[0]}";
            Assert.That(bush.Count, Is.InRange(4, 14), $"{where} has {bush.Count} cells.");
            // 2×2 칸이 없는 덩어리는 1칸 폭 띠뿐이고, 띠는 5칸 이상이다.
            Assert.That(HasCore(bush) || bush.Count >= 5, Is.True, $"{where} is a short bush without a 2x2 block.");
            // 8방향 덩어리가 상하좌우로도 하나로 이어져 있어야 덩어리끼리 대각선으로만 붙은 곳이 없다.
            Assert.That(ConnectedCount(bush), Is.EqualTo(bush.Count), $"{where} touches another bush diagonally.");
        }
    }

    // 부시 덩어리 번호별 칸 목록. 각 목록은 행 우선 순서다.
    static List<List<Vector2Int>> BushClusters(MapLayout map)
    {
        var bushes = new List<List<Vector2Int>>();
        for (int i = 0; i < map.BushCount; i++) bushes.Add(new List<Vector2Int>());
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.BushId(x, y) >= 0) bushes[map.BushId(x, y)].Add(new Vector2Int(x, y));
        return bushes;
    }

    // 덩어리가 어떤 모양에 드는지. 2x2·2x3은 꽉 찬 사각형(3x2 포함), line은 1칸 폭이거나 짧은 변 2칸에 긴 변 5칸 이상,
    // wall은 칸 60% 이상이 벽에 닿고 긴 변 4칸 이상, wide는 8칸 이상에 두 변 모두 3칸 이상이다. 여러 모양에 들 수도 있다.
    static IEnumerable<string> BushKinds(MapLayout map, List<Vector2Int> bush)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, touching = 0;
        foreach (Vector2Int cell in bush)
        {
            minX = Math.Min(minX, cell.x);
            maxX = Math.Max(maxX, cell.x);
            minY = Math.Min(minY, cell.y);
            maxY = Math.Max(maxY, cell.y);
            foreach (Vector2Int direction in directions)
                if (IsCell(map, cell + direction, MapCell.Wall))
                {
                    touching++;
                    break;
                }
        }
        int width = maxX - minX + 1, height = maxY - minY + 1;
        int shortSide = Math.Min(width, height), longSide = Math.Max(width, height);
        bool full = bush.Count == width * height;
        if (full && shortSide == 2 && longSide == 2) yield return "2x2";
        if (full && shortSide == 2 && longSide == 3) yield return "2x3";
        if (!HasCore(bush) || (shortSide <= 2 && longSide >= 5)) yield return "line";
        if (touching >= 0.6 * bush.Count && longSide >= 4) yield return "wall";
        if (bush.Count >= 8 && shortSide >= 3) yield return "wide";
    }

    static bool HasCore(List<Vector2Int> bush)
    {
        var set = new HashSet<Vector2Int>(bush);
        foreach (Vector2Int cell in bush)
            if (set.Contains(cell + Vector2Int.right) && set.Contains(cell + Vector2Int.up)
                && set.Contains(cell + Vector2Int.one)) return true;
        return false;
    }

    // bush[0]에서 상하좌우로 이어진 칸 수.
    static int ConnectedCount(List<Vector2Int> bush)
    {
        var set = new HashSet<Vector2Int>(bush);
        var visited = new HashSet<Vector2Int> { bush[0] };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(bush[0]);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = cell + direction;
                if (set.Contains(next) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        return visited.Count;
    }

    // spawn은 스폰 영역의 최소 좌표 칸이다. 스폰 영역과 둘레 SpawnClearance칸이 모두 바닥이어야 한다.
    static bool SafeSpawn(MapLayout map, Vector2Int spawn)
    {
        const int clearance = MapLayout.SpawnClearance;
        if (spawn.x < clearance || spawn.y < clearance || spawn.x > map.Width - MapLayout.SpawnSize - clearance
            || spawn.y > map.Height - MapLayout.SpawnSize - clearance) return false;
        for (int dy = -clearance; dy < MapLayout.SpawnSize + clearance; dy++)
        for (int dx = -clearance; dx < MapLayout.SpawnSize + clearance; dx++)
            if (map[spawn.x + dx, spawn.y + dy] != MapCell.Floor) return false;
        return true;
    }

    // 밀도 0 맵에서 바닥이던 벽 칸을 8방향으로 이어 장애물 덩어리로 묶는다.
    static List<List<Vector2Int>> ObstaclePieces(MapLayout map, MapLayout empty)
    {
        var pieces = new List<List<Vector2Int>>();
        var visited = new bool[map.Width, map.Height];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (visited[x, y] || !IsObstacle(map, empty, x, y)) continue;
            var piece = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(x, y));
            visited[x, y] = true;
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                piece.Add(cell);
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cell.x + dx, ny = cell.y + dy;
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height
                        || visited[nx, ny] || !IsObstacle(map, empty, nx, ny)) continue;
                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
            pieces.Add(piece);
        }
        return pieces;
    }

    static bool IsObstacle(MapLayout map, MapLayout empty, int x, int y)
    {
        return empty[x, y] == MapCell.Floor && map[x, y] == MapCell.Wall;
    }

    // 둘레가 모두 바닥인 2x2 기둥이나 1xn(n <= 4) 벽인지.
    static bool IsStandaloneCover(MapLayout map, List<Vector2Int> piece)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector2Int cell in piece)
        {
            minX = Math.Min(minX, cell.x);
            minY = Math.Min(minY, cell.y);
            maxX = Math.Max(maxX, cell.x);
            maxY = Math.Max(maxY, cell.y);
        }
        int width = maxX - minX + 1, height = maxY - minY + 1;
        bool pillar = width == 2 && height == 2;
        bool shortWall = Math.Min(width, height) == 1 && Math.Max(width, height) <= 4;
        if (piece.Count != width * height || (!pillar && !shortWall)) return false;
        for (int y = minY - 1; y <= maxY + 1; y++)
        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            bool inside = x >= minX && x <= maxX && y >= minY && y <= maxY;
            if (!inside && !Walkable(map, x, y)) return false;
        }
        return true;
    }

    // 양옆이 벽이고 그중 하나가 장애물인 1칸 폭 바닥이 곧게 이어지는 가장 긴 길이.
    static int LongestObstacleCorridor(MapLayout map, MapLayout empty)
    {
        int longest = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        foreach (Vector2Int across in new[] { Vector2Int.right, Vector2Int.up })
        {
            var cell = new Vector2Int(x, y);
            Vector2Int a = cell + across, b = cell - across;
            if (!Pinched(map, cell, across) || (!IsObstacle(map, empty, a.x, a.y) && !IsObstacle(map, empty, b.x, b.y)))
                continue;
            var along = new Vector2Int(across.y, across.x);
            int length = 0;
            for (Vector2Int next = cell; Pinched(map, next, across); next += along) length++;
            for (Vector2Int next = cell - along; Pinched(map, next, across); next -= along) length++;
            longest = Math.Max(longest, length);
        }
        return longest;
    }

    static bool Pinched(MapLayout map, Vector2Int cell, Vector2Int across)
    {
        return InBounds(map, cell) && Walkable(map, cell.x, cell.y) && IsCell(map, cell + across, MapCell.Wall)
            && IsCell(map, cell - across, MapCell.Wall);
    }

    static bool IsCell(MapLayout map, Vector2Int cell, MapCell type)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < map.Width && cell.y < map.Height && map[cell.x, cell.y] == type;
    }

    // cell을 포함하는 3x3 창 중 모두 벽인 곳이 있는지.
    static bool InSolidBlock(MapLayout map, Vector2Int cell)
    {
        for (int cy = cell.y - 1; cy <= cell.y + 1; cy++)
        for (int cx = cell.x - 1; cx <= cell.x + 1; cx++)
        {
            bool solid = true;
            for (int y = cy - 1; y <= cy + 1; y++)
            for (int x = cx - 1; x <= cx + 1; x++)
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || map[x, y] != MapCell.Wall) solid = false;
            if (solid) return true;
        }
        return false;
    }

    // 180도 돌린 스폰 영역의 최소 좌표 칸.
    static Vector2Int Opposite(MapLayout map, Vector2Int spawn)
    {
        return new Vector2Int(map.Width - MapLayout.SpawnSize - spawn.x, map.Height - MapLayout.SpawnSize - spawn.y);
    }

    // 걸어 다닐 수 있는 칸(바닥과 부시) 수.
    static int CountFloors(MapLayout map)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (Walkable(map, x, y)) count++;
        return count;
    }

    // 부시는 바닥 위에 깔려 이동을 막지 않는다.
    static bool Walkable(MapLayout map, int x, int y)
    {
        return map[x, y] == MapCell.Floor || map[x, y] == MapCell.Bush;
    }

    static bool InBounds(MapLayout map, Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < map.Width && cell.y < map.Height;
    }

    static int[,] Distances(MapLayout map, Vector2Int start)
    {
        var distances = new int[map.Width, map.Height];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            distances[x, y] = -1;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        distances[start.x, start.y] = 0;
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = cell + direction;
                if (next.x < 0 || next.y < 0 || next.x >= map.Width || next.y >= map.Height
                    || distances[next.x, next.y] >= 0 || !Walkable(map, next.x, next.y)) continue;
                distances[next.x, next.y] = distances[cell.x, cell.y] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }

    static int CountEnclosedVoids(MapLayout map)
    {
        var visited = new bool[map.Width, map.Height];
        int enclosed = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (visited[x, y] || map[x, y] != MapCell.Void) continue;
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(x, y));
            visited[x, y] = true;
            bool touchesEdge = false;
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (cell.x == 0 || cell.y == 0 || cell.x == map.Width - 1 || cell.y == map.Height - 1) touchesEdge = true;
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = cell + direction;
                    if (next.x < 0 || next.y < 0 || next.x >= map.Width || next.y >= map.Height
                        || visited[next.x, next.y] || map[next.x, next.y] != MapCell.Void) continue;
                    visited[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }
            if (!touchesEdge) enclosed++;
        }
        return enclosed;
    }
}
