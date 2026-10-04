using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MapLayoutGeneratorTests
{
    static readonly Vector2Int[] directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
    };

    static IEnumerable<TestCaseData> ValidMaps()
    {
        foreach (MapShape shape in Enum.GetValues(typeof(MapShape)))
        foreach (MapSymmetry symmetry in Enum.GetValues(typeof(MapSymmetry)))
        foreach (float density in new[] { 0f, 0.15f, 1f })
        foreach (int seed in new[] { 0, 7301, -11 })
        foreach (bool even in new[] { false, true })
        {
            Vector2Int size = shape == MapShape.Elongated
                ? new Vector2Int(even ? 48 : 47, even ? 18 : 17)
                : new Vector2Int(even ? 32 : 31, even ? 30 : 29);
            yield return new TestCaseData(shape, symmetry, density, seed, size)
                .SetName($"Connected_{shape}_{symmetry}_density{density}_seed{seed}_{size.x}x{size.y}");
        }
    }

    static IEnumerable<TestCaseData> NoisyMaps()
    {
        foreach (MapShape shape in new[] { MapShape.Circle, MapShape.Rectangle, MapShape.Elongated, MapShape.Donut })
        foreach (MapSymmetry symmetry in Enum.GetValues(typeof(MapSymmetry)))
        foreach (float strength in new[] { 0f, 0.5f, 1f })
        foreach (float scale in new[] { 0.25f, 0.75f, 2f })
        foreach (bool even in new[] { false, true })
        {
            Vector2Int size = shape == MapShape.Elongated
                ? new Vector2Int(even ? 48 : 47, even ? 18 : 17)
                : new Vector2Int(even ? 32 : 31, even ? 30 : 29);
            yield return new TestCaseData(shape, symmetry, strength, scale, size)
                .SetName($"Noise_{shape}_{symmetry}_strength{strength}_scale{scale}_{size.x}x{size.y}");
        }
    }

    [TestCaseSource(nameof(NoisyMaps))]
    public void Generate_NoisePreservesBoundariesConnectivitySpawnsAndHole(
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
            Shape = shape, Size = new Vector2Int(51, 41), ObstacleDensity = 0f,
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
            Shape = MapShape.Elongated, Size = new Vector2Int(49, 31), ObstacleDensity = 0f,
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
        MapLayout empty = MapLayoutGenerator.Generate(settings);
        int initialFloors = 0;
        int obstacles = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (empty[x, y] != MapCell.Floor)
                Assert.That(map[x, y], Is.EqualTo(empty[x, y]), "Shape or boundary changed during obstacle placement.");
            else
            {
                initialFloors++;
                if (map[x, y] == MapCell.Wall) obstacles++;
            }
        }
        Assert.That(map.SpawnA, Is.EqualTo(empty.SpawnA));
        Assert.That(map.SpawnB, Is.EqualTo(empty.SpawnB));
        Assert.That(map.ActualObstacleDensity, Is.EqualTo((float)obstacles / initialFloors).Within(0.000001f));
        Assert.That(map.ActualObstacleDensity, Is.InRange(0f, density));
        if (density == 0f) Assert.That(obstacles, Is.Zero);
        if (density == 1f) Assert.That(map.ActualObstacleDensity, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.That(CountEnclosedVoids(map), Is.EqualTo(map.Shape == MapShape.Donut ? 1 : 0));
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
        Assert.That(settings.Size, Is.Null);
        Assert.That(settings.Shape, Is.EqualTo(shape));
    }

    [TestCase(MapShape.Circle)]
    [TestCase(MapShape.Rectangle)]
    [TestCase(MapShape.Elongated)]
    [TestCase(MapShape.Donut)]
    public void Generate_AutomaticSizesStayInShapeRanges(MapShape shape)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed, Shape = shape, ObstacleDensity = 0f
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
                Seed = seed, Size = new Vector2Int(35, 35), Shape = shape, ObstacleDensity = 0f
            });
            outlines.Add(string.Join("\n", map.ToLayout()).Replace('B', '.').Replace('O', '.'));
        }
        Assert.That(outlines.Count, Is.GreaterThan(1));
    }

    [Test]
    public void Generate_ShapesHaveDistinctGeometry()
    {
        var settings = new MapGenerationSettings { Size = new Vector2Int(35, 35), ObstacleDensity = 0f };
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
            Size = new Vector2Int(31, 31), Shape = MapShape.Donut, ObstacleDensity = 0f
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
                    : map[x, y] == MapCell.Wall ? '#' : ' '));
            }
        }
        Assert.That(blueSpawns, Is.EqualTo(1));
        Assert.That(orangeSpawns, Is.EqualTo(1));
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
                Seed = seed, Shape = MapShape.Random, Size = new Vector2Int(30, 20), ObstacleDensity = 0f
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
        Assert.That(SafeSpawn(map, map.SpawnA), Is.True, "Spawn A must have a clear 3x3 area.");
        Assert.That(SafeSpawn(map, map.SpawnB), Is.True, "Spawn B must have a clear 3x3 area.");
        int[,] distances = Distances(map, map.SpawnA);
        int reachable = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (distances[x, y] >= 0) reachable++;
            if (map[x, y] == MapCell.Floor)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Assert.That(x + dx, Is.InRange(0, map.Width - 1));
                    Assert.That(y + dy, Is.InRange(0, map.Height - 1));
                    Assert.That(map[x + dx, y + dy], Is.Not.EqualTo(MapCell.Void), "Floor touches an unsealed edge.");
                }
            }
            if (symmetry == MapSymmetry.Rotate180)
                Assert.That(map[x, y], Is.EqualTo(map[map.Width - 1 - x, map.Height - 1 - y]));
        }
        Assert.That(reachable, Is.EqualTo(CountFloors(map)), "All floor cells must be connected.");
        Assert.That(distances[map.SpawnB.x, map.SpawnB.y], Is.GreaterThan(0));
        if (symmetry == MapSymmetry.Rotate180)
            Assert.That(map.SpawnB, Is.EqualTo(Opposite(map, map.SpawnA)));
    }

    static bool SafeSpawn(MapLayout map, Vector2Int spawn)
    {
        if (spawn.x <= 0 || spawn.y <= 0 || spawn.x >= map.Width - 1 || spawn.y >= map.Height - 1) return false;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (map[spawn.x + dx, spawn.y + dy] != MapCell.Floor) return false;
        return true;
    }

    static Vector2Int Opposite(MapLayout map, Vector2Int cell)
    {
        return new Vector2Int(map.Width - 1 - cell.x, map.Height - 1 - cell.y);
    }

    static int CountFloors(MapLayout map)
    {
        int count = 0;
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map[x, y] == MapCell.Floor) count++;
        return count;
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
                    || distances[next.x, next.y] >= 0 || map[next.x, next.y] != MapCell.Floor) continue;
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
