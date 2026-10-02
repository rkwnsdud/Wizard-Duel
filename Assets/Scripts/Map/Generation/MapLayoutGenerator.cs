using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

public static class MapLayoutGenerator
{
    const int maxAttempts = 32;

    static readonly Vector2Int[] directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
    };

    public static MapLayout Generate(MapGenerationSettings settings)
    {
        Validate(settings);
        var random = new Random(settings.Seed);
        MapShape shape = settings.Shape == MapShape.Random
            ? (MapShape)random.Next((int)MapShape.Circle, (int)MapShape.Donut + 1)
            : settings.Shape;
        Vector2Int size = settings.Size ?? ChooseSize(shape, random);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            MapCell[,] cells = CreateCells(size, shape, settings, random, out int floorCount);
            List<Vector2Int> candidates = GetSpawnCandidates(cells);
            if (candidates.Count < 2) continue;

            var distances = new int[size.x, size.y];
            var queue = new Queue<Vector2Int>(floorCount);
            if (FloodFill(cells, candidates[0], distances, queue) != floorCount) continue;
            if (!TrySelectSpawns(cells, candidates, settings.Symmetry, random, distances, queue,
                out Vector2Int spawnA, out Vector2Int spawnB)) continue;

            int obstacles = PlaceObstacles(cells, spawnA, spawnB, floorCount,
                settings.ObstacleDensity, settings.Symmetry, random, distances, queue);
            return new MapLayout(cells, spawnA, spawnB, shape, (float)obstacles / floorCount);
        }

        throw new InvalidOperationException(
            $"Could not generate a connected {shape} map of size {size.x}x{size.y} " +
            $"with two safe spawns after {maxAttempts} attempts (seed {settings.Seed}).");
    }

    static void Validate(MapGenerationSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (settings.Size.HasValue && (settings.Size.Value.x <= 0 || settings.Size.Value.y <= 0))
            throw new ArgumentOutOfRangeException(nameof(settings.Size), "Map dimensions must be positive.");
        if (float.IsNaN(settings.ObstacleDensity) || float.IsInfinity(settings.ObstacleDensity)
            || settings.ObstacleDensity < 0f || settings.ObstacleDensity > 1f)
            throw new ArgumentOutOfRangeException(nameof(settings.ObstacleDensity),
                "Obstacle density must be a finite value between 0 and 1.");
        if (!Enum.IsDefined(typeof(MapShape), settings.Shape))
            throw new ArgumentOutOfRangeException(nameof(settings.Shape));
        if (!Enum.IsDefined(typeof(MapSymmetry), settings.Symmetry))
            throw new ArgumentOutOfRangeException(nameof(settings.Symmetry));
        if (float.IsNaN(settings.NoiseStrength) || float.IsInfinity(settings.NoiseStrength)
            || settings.NoiseStrength < 0f || settings.NoiseStrength > 1f)
            throw new ArgumentOutOfRangeException(nameof(settings.NoiseStrength),
                "Noise strength must be a finite value between 0 and 1.");
        if (float.IsNaN(settings.NoiseScale) || float.IsInfinity(settings.NoiseScale)
            || settings.NoiseScale < 0.25f || settings.NoiseScale > 2f)
            throw new ArgumentOutOfRangeException(nameof(settings.NoiseScale),
                "Noise scale must be a finite value between 0.25 and 2.");
    }

    static Vector2Int ChooseSize(MapShape shape, Random random)
    {
        if (shape != MapShape.Elongated)
            return new Vector2Int(random.Next(25, 42), random.Next(25, 42));

        int longSide = random.Next(41, 62);
        int shortSide = random.Next(13, 20);
        return random.Next(2) == 0
            ? new Vector2Int(longSide, shortSide) : new Vector2Int(shortSide, longSide);
    }

    static MapCell[,] CreateCells(Vector2Int size, MapShape shape, MapGenerationSettings settings,
        Random random, out int floorCount)
    {
        MapSymmetry symmetry = settings.Symmetry;
        var region = new bool[size.x, size.y];
        double centerX = (size.x - 1) * 0.5;
        double centerY = (size.y - 1) * 0.5;
        double radiusX = Math.Max(0.5, centerX * (0.88 + random.NextDouble() * 0.06));
        double radiusY = Math.Max(0.5, centerY * (0.88 + random.NextDouble() * 0.06));
        int outlineSeed = random.Next();
        int holeSeed = random.Next();
        int bendSeed = random.Next();
        double innerRatio = 0.3 + random.NextDouble() * 0.2;
        bool horizontal = size.x >= size.y;
        double majorRadius = horizontal ? radiusX : radiusY;
        double minorRadius = Math.Min(horizontal ? radiusY : radiusX, majorRadius / 3);
        // 중심선을 굽힐 여유 공간을 만들고 안전한 스폰을 위한 폭을 남긴다.
        minorRadius = Math.Max(Math.Min(3.5, minorRadius), minorRadius * (1 - 0.55 * settings.NoiseStrength));
        double bendAmplitude = Math.Min(Math.Max(0, (horizontal ? centerY : centerX) - minorRadius),
            settings.NoiseStrength * majorRadius * 0.5);
        // 작은 요철의 변위도 줄여 격자화 후 얇은 목이나 고립된 영역이 생기는 것을 완화한다.
        double contourStrength = settings.NoiseStrength * Math.Min(1, settings.NoiseScale / 0.75);

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int opposite = Mirror(new Vector2Int(x, y), size);
                if (symmetry == MapSymmetry.Rotate180 && y * size.x + x > opposite.y * size.x + opposite.x)
                {
                    // 짝수 크기에서도 부동소수점 오차 없이 정확한 회전 대칭을 유지한다.
                    region[x, y] = region[opposite.x, opposite.y];
                    continue;
                }

                double dx = x - centerX;
                double dy = y - centerY;
                double u = dx / radiusX;
                double v = dy / radiusY;
                double distance = Math.Sqrt(u * u + v * v);
                double nx = distance == 0 ? 1 : u / distance;
                double ny = distance == 0 ? 0 : v / distance;
                double outline = 1 + 0.45 * contourStrength
                    * ContourNoise(nx, ny, settings.NoiseScale, outlineSeed, symmetry);

                if (shape == MapShape.Rectangle)
                    distance = Math.Max(Math.Abs(u), Math.Abs(v));
                else if (shape == MapShape.Elongated)
                {
                    double axis = (horizontal ? dx : dy) / majorRadius;
                    double bend = ValueNoise(axis / settings.NoiseScale, 0, bendSeed);
                    if (symmetry == MapSymmetry.Rotate180)
                        bend = (bend - ValueNoise(-axis / settings.NoiseScale, 0, bendSeed)) * 0.5;
                    bend = Math.Max(-1, Math.Min(1, bend * 3)) * bendAmplitude;

                    double widthNoise = ContourNoise(axis, 0, settings.NoiseScale, outlineSeed, symmetry);
                    double localRadius = Math.Max(Math.Min(3.5, minorRadius),
                        minorRadius * (1 + 0.35 * settings.NoiseStrength * widthNoise));
                    double along = Math.Max(0, Math.Abs(horizontal ? dx : dy) - (majorRadius - minorRadius));
                    double across = (horizontal ? dy : dx) - bend;
                    distance = Math.Sqrt(along * along + across * across) / localRadius;
                    outline = 1;
                }

                region[x, y] = distance <= outline;
                if (shape == MapShape.Donut)
                {
                    double hole = innerRatio * (1 + 0.55 * contourStrength
                        * ContourNoise(nx, ny, settings.NoiseScale, holeSeed, symmetry));
                    // 안쪽과 바깥쪽을 독립적으로 변형하며 중앙 구멍이 외부로 열리지 않게 한다.
                    hole = Math.Min(hole, outline * 0.6);
                    region[x, y] &= distance >= hole;
                }
            }
        }

        FillExtraHoles(region, shape == MapShape.Donut);
        var cells = new MapCell[size.x, size.y];
        floorCount = 0;
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                if (!region[x, y]) continue;
                bool boundary = false;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= size.x || ny >= size.y || !region[nx, ny])
                            boundary = true;
                    }
                }
                cells[x, y] = boundary ? MapCell.Wall : MapCell.Floor;
                if (!boundary) floorCount++;
            }
        }
        return cells;
    }

    static void FillExtraHoles(bool[,] region, bool preserveCenterHole)
    {
        int width = region.GetLength(0);
        int height = region.GetLength(1);
        var visited = new bool[width, height];
        var queue = new Queue<Vector2Int>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
            bool center = preserveCenterHole && x == width / 2 && y == height / 2;
            if (region[x, y] || (!edge && !center)) continue;
            visited[x, y] = true;
            queue.Enqueue(new Vector2Int(x, y));
        }
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = cell + direction;
                if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height
                    || region[next.x, next.y] || visited[next.x, next.y]) continue;
                visited[next.x, next.y] = true;
                queue.Enqueue(next);
            }
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (!region[x, y] && !visited[x, y]) region[x, y] = true;
    }

    static double ContourNoise(double x, double y, double scale, int seed, MapSymmetry symmetry)
    {
        double noise = ValueNoise(x / scale, y / scale, seed);
        if (symmetry == MapSymmetry.Rotate180)
            noise = (noise + ValueNoise(-x / scale, -y / scale, seed)) * 0.5;
        return Math.Max(-1, Math.Min(1, noise * 1.8));
    }

    static double ValueNoise(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x);
        int iy = (int)Math.Floor(y);
        double tx = x - ix;
        double ty = y - iy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = NoiseValue(ix, iy, seed);
        double b = NoiseValue(ix + 1, iy, seed);
        double c = NoiseValue(ix, iy + 1, seed);
        double d = NoiseValue(ix + 1, iy + 1, seed);
        double top = a + (b - a) * tx;
        double bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    static double NoiseValue(int x, int y, int seed)
    {
        unchecked
        {
            uint hash = (uint)seed ^ (uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash / (double)uint.MaxValue * 2 - 1;
        }
    }

    static List<Vector2Int> GetSpawnCandidates(MapCell[,] cells)
    {
        var candidates = new List<Vector2Int>();
        for (int y = 1; y < cells.GetLength(1) - 1; y++)
        {
            for (int x = 1; x < cells.GetLength(0) - 1; x++)
            {
                bool safe = true;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (cells[x + dx, y + dy] != MapCell.Floor) safe = false;
                if (safe) candidates.Add(new Vector2Int(x, y));
            }
        }
        return candidates;
    }

    static bool TrySelectSpawns(MapCell[,] cells, List<Vector2Int> candidates, MapSymmetry symmetry,
        Random random, int[,] distances, Queue<Vector2Int> queue, out Vector2Int spawnA, out Vector2Int spawnB)
    {
        spawnA = default;
        spawnB = default;
        if (symmetry == MapSymmetry.None)
        {
            FloodFill(cells, candidates[random.Next(candidates.Count)], distances, queue);
            spawnA = Farthest(candidates, distances);
            FloodFill(cells, spawnA, distances, queue);
            spawnB = Farthest(candidates, distances);
            return spawnA != spawnB;
        }

        var size = new Vector2Int(cells.GetLength(0), cells.GetLength(1));
        int bestDistance = -1;
        foreach (Vector2Int candidate in candidates)
        {
            Vector2Int opposite = Mirror(candidate, size);
            if (candidate.y * size.x + candidate.x >= opposite.y * size.x + opposite.x) continue;
            FloodFill(cells, candidate, distances, queue);
            if (distances[opposite.x, opposite.y] <= bestDistance) continue;
            bestDistance = distances[opposite.x, opposite.y];
            spawnA = candidate;
            spawnB = opposite;
        }
        return bestDistance > 0;
    }

    static Vector2Int Farthest(List<Vector2Int> candidates, int[,] distances)
    {
        Vector2Int farthest = candidates[0];
        foreach (Vector2Int candidate in candidates)
            if (distances[candidate.x, candidate.y] > distances[farthest.x, farthest.y]) farthest = candidate;
        return farthest;
    }

    static int PlaceObstacles(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB, int floorCount,
        float density, MapSymmetry symmetry, Random random, int[,] distances, Queue<Vector2Int> queue)
    {
        int target = (int)Math.Floor((double)density * floorCount);
        if (target == 0) return 0;

        var size = new Vector2Int(cells.GetLength(0), cells.GetLength(1));
        var candidates = new List<Vector2Int>();
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                var cell = new Vector2Int(x, y);
                if (cells[x, y] != MapCell.Floor || NearSpawn(cell, spawnA) || NearSpawn(cell, spawnB)) continue;
                Vector2Int opposite = Mirror(cell, size);
                if (symmetry == MapSymmetry.Rotate180 && y * size.x + x > opposite.y * size.x + opposite.x) continue;
                candidates.Add(cell);
            }
        }
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            Vector2Int temp = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = temp;
        }

        int placed = 0;
        foreach (Vector2Int cell in candidates)
        {
            if (placed == target) break;
            Vector2Int opposite = symmetry == MapSymmetry.Rotate180 ? Mirror(cell, size) : cell;
            int count = opposite == cell ? 1 : 2;
            if (placed + count > target) continue;

            cells[cell.x, cell.y] = MapCell.Wall;
            cells[opposite.x, opposite.y] = MapCell.Wall;
            if (FloodFill(cells, spawnA, distances, queue) == floorCount - placed - count)
                placed += count;
            else
            {
                cells[cell.x, cell.y] = MapCell.Floor;
                cells[opposite.x, opposite.y] = MapCell.Floor;
            }
        }
        return placed;
    }

    static bool NearSpawn(Vector2Int cell, Vector2Int spawn)
    {
        return Math.Abs(cell.x - spawn.x) <= 1 && Math.Abs(cell.y - spawn.y) <= 1;
    }

    static Vector2Int Mirror(Vector2Int cell, Vector2Int size)
    {
        return new Vector2Int(size.x - 1 - cell.x, size.y - 1 - cell.y);
    }

    static int FloodFill(MapCell[,] cells, Vector2Int start, int[,] distances, Queue<Vector2Int> queue)
    {
        int width = cells.GetLength(0);
        int height = cells.GetLength(1);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                distances[x, y] = -1;

        queue.Clear();
        queue.Enqueue(start);
        distances[start.x, start.y] = 0;
        int count = 0;
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            count++;
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = cell + direction;
                if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height
                    || cells[next.x, next.y] != MapCell.Floor || distances[next.x, next.y] >= 0) continue;
                distances[next.x, next.y] = distances[cell.x, cell.y] + 1;
                queue.Enqueue(next);
            }
        }
        return count;
    }
}
