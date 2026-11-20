using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

public static class MapLayoutGenerator
{
    const int maxAttempts = 32;

    // 장애물 구조물 튜닝 값. 바꾸면 같은 시드의 맵이 달라지므로 LobbyJoinRequest.CurrentVersion도 올린다.
    const float coverShare = 0.4f;     // 장애물 예산 중 독립 엄폐물(기둥, 짧은 벽)에 쓰는 몫
    const float growChance = 0.5f;     // 벽 단계에서 새 벽 대신 세운 벽의 끝을 늘릴 확률
    const float turnChance = 0.35f;    // 벽 끝을 늘릴 때 직각으로 꺾을 확률
    const float bentWallChance = 0.4f; // 새 벽이 곧은 벽 대신 L자 벽일 확률
    const float pillarChance = 0.5f;   // 엄폐물이 짧은 벽 대신 2×2 기둥일 확률
    const int minWallLength = 3, maxWallLength = 6;
    const int minArmLength = 2, maxArmLength = 4;
    const int minGrowLength = 2, maxGrowLength = 5;
    const int minCoverLength = 2, maxCoverLength = 4;
    const int maxRunLength = 7;        // 한 방향으로 곧게 이어지는 벽의 최대 길이
    const int maxCorridorLength = 2;   // 양옆이 벽인 1칸 폭 바닥이 곧게 이어질 수 있는 최대 길이
    const int wallClearance = 2;       // 새 벽 둘레로 비어 있어야 하는 바닥 칸 수 (벽 단계 시도의 앞쪽 절반)
    const int coverClearance = 1;      // 새 엄폐물 둘레로 비어 있어야 하는 바닥 칸 수
    const int placementTries = 4000;   // 단계마다 구조물 자리를 찾는 최대 시도 횟수

    // 부시 튜닝 값. 바꾸면 같은 시드의 맵이 달라지므로 LobbyJoinRequest.CurrentVersion도 올린다.
    // 부시 칸 수는 MapGenerationSettings.BushDensity로 받는다. 부시는 벽과 엄폐물이 다 선 뒤에 같은 난수열로 두므로
    // 부시 밀도만 바꾸면 벽과 엄폐물은 그대로다.
    const int minBushSize = 4;                 // 가장 작은 덩어리(2×2)의 칸 수
    const int bushGap = 2;                     // 서로 다른 덩어리 사이에 비워 두는 칸 수(대각선 포함). 1칸 틈은 화면에서 한 덩어리처럼 보인다
    const int centerBushTolerance = 1;         // 중앙 부시 씨앗: 두 스폰까지의 경로 거리 차이 상한
    const int centerMinSize = 5, centerMaxSize = 8; // 중앙 부시 칸 수. 대칭 맵에서 짝과 합치면 합친 크기다. 5칸부터라 2×2 핵만 남지 않는다
    const int openFieldMinDistance = 2;        // 빈터 칸: 가장 가까운 벽에서 떨어져야 하는 최소 칸 수(8방향)
    const float openFieldPercentile = 0.75f;   // 넓게 퍼진 부시 씨앗: 벽까지 거리가 이 분위 이상인 칸
    const float innerWallChance = 0.75f;       // 벽 따라 늘어진 부시 씨앗을 외곽 벽 대신 안쪽 구조물 옆에서 고를 확률
    const int centerBushTries = 200;           // 중앙 부시 자리를 찾는 최대 시도 횟수
    const int bushTries = 600;                 // 나머지 부시 덩어리 자리를 찾는 최대 시도 횟수
    const int shapeFailLimit = 40;             // 한 모양이 이만큼 연달아 자리를 못 찾으면 그 모양은 더 쓰지 않는다
    // 덩어리(Patch): 2×2·3×2·2×3·3×3 사각형에 가끔 한두 칸을 덧붙인다.
    const float patchBumpChance = 0.35f;
    // 넓게 퍼진 부시(Spread): 빈터의 2×2 핵에서 핵과 먼 자리도 자주 골라 spreadTargetMin~spreadMaxSize칸을 목표로 넓게 키운다.
    // 좁은 맵은 빈터가 작아 다 못 자라므로 spreadMinSize칸 이상이면 그대로 둔다. 그래야 좁은 맵에도 벽에서 떨어진 부시가 생긴다.
    const int spreadMinSize = 6, spreadTargetMin = 8, spreadMaxSize = 14;
    const int sprawlReach = 3;                 // 핵에서 먼 자리에 더하는 가중치의 상한(칸 거리)
    // 벽 따라 늘어진 부시(WallStrip): 벽에서 stripDepth칸 안쪽만 써서 벽을 따라 늘어진다.
    const int stripMinSize = 6, stripMaxSize = 10;
    const int stripMinLength = 4;              // 긴 변의 최소 칸 수
    const int stripDepth = 2;                  // 벽에서 떨어질 수 있는 최대 칸 수(8방향 거리)
    const int stripWallWeight = 6, stripFillWeight = 3; // 벽에 닿은 자리, 덩어리 두 칸 이상과 닿은 자리의 가중치(나머지는 1)
    // 길게 이어진 부시(Line): 1칸이나 2칸 폭의 곧은 띠. 가끔 한 번 꺾여 L자가 된다.
    const int lineMinLength = 5, lineMaxLength = 8;         // 1칸 폭 띠의 길이
    const int wideLineMinLength = 4, wideLineMaxLength = 6; // 2칸 폭 띠의 길이
    const float wideLineChance = 0.6f;
    const float lineBendChance = 0.4f;
    const int lineArmMin = 3;                  // 꺾인 띠에서 한 팔의 최소 칸 수(모서리 포함)

    static readonly Vector2Int[] directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
    };

    // 2×2 핵에서 씨앗 칸이 차지할 수 있는 자리.
    static readonly Vector2Int[] coreOffsets =
    {
        new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1)
    };

    // 덩어리 부시의 사각형 크기(가로×세로)와 고를 가중치. 넓이 순서로 둔다. 고른 사각형이 안 들어가면 앞쪽 작은 사각형으로 내려간다.
    static readonly Vector2Int[] patchSizes =
    {
        new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(2, 3), new Vector2Int(3, 3)
    };
    static readonly int[] patchWeights = { 1, 2, 2, 1 };

    public static MapLayout Generate(MapGenerationSettings settings)
    {
        Validate(settings);
        var random = new Random(settings.Seed);
        MapShape shape = ChooseShape(settings, random);
        Vector2Int size = settings.Size ?? ChooseSize(shape, random);
        ValidateMinimumSize(size, shape);

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
            int bushes = PlaceBushes(cells, spawnA, spawnB, floorCount,
                settings.BushDensity, settings.Symmetry, random, distances, queue);
            bool[,] destructibleWalls = SelectDestructibleWalls(cells, settings.Symmetry, random);
            return new MapLayout(cells, spawnA, spawnB, shape,
                (float)obstacles / floorCount, (float)bushes / floorCount, destructibleWalls);
        }

        throw new InvalidOperationException(
            $"Could not generate a connected {shape} map of size {size.x}x{size.y} " +
            $"with two safe spawns after {maxAttempts} attempts (seed {settings.Seed}).");
    }

    // Preserve the generated obstacle silhouettes while marking a seed-stable subset
    // of their wall cells as destructible. Boundary walls are excluded.
    static bool[,] SelectDestructibleWalls(MapCell[,] cells, MapSymmetry symmetry, Random random)
    {
        const double destructibleShare = 0.25;
        int width = cells.GetLength(0), height = cells.GetLength(1);
        var candidates = new List<Vector2Int>();
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
                if (cells[x, y] == MapCell.Wall) candidates.Add(new Vector2Int(x, y));

        // Shuffle with the map RNG so all peers derive the same destructible cells.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var selected = new bool[width, height];
        int desired = (int)Math.Round(candidates.Count * destructibleShare);
        if (desired <= 0) return selected;
        int converted = 0;
        foreach (Vector2Int cell in candidates)
        {
            if (cells[cell.x, cell.y] != MapCell.Wall) continue;
            Vector2Int opposite = new Vector2Int(width - 1 - cell.x, height - 1 - cell.y);
            if (symmetry == MapSymmetry.Rotate180 && cells[opposite.x, opposite.y] != MapCell.Wall) continue;
            selected[cell.x, cell.y] = true;
            converted++;
            if (symmetry == MapSymmetry.Rotate180 && opposite != cell)
            {
                selected[opposite.x, opposite.y] = true;
                converted++;
            }
            if (converted >= desired) break;
        }
        return selected;
    }

    static MapShape ChooseShape(MapGenerationSettings settings, Random random)
    {
        if (settings.Shape != MapShape.Random) return settings.Shape;
        if (!settings.Size.HasValue)
            return (MapShape)random.Next((int)MapShape.Circle, (int)MapShape.Donut + 1);

        var compatible = new List<MapShape>();
        for (int value = (int)MapShape.Circle; value <= (int)MapShape.Donut; value++)
        {
            MapShape candidate = (MapShape)value;
            if (CanFitSize(settings.Size.Value, candidate)) compatible.Add(candidate);
        }
        if (compatible.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(settings.Size), settings.Size,
                $"Size {settings.Size.Value.x}x{settings.Size.Value.y} is below the minimum supported size for every map shape.");
        return compatible[random.Next(compatible.Count)];
    }

    static void ValidateMinimumSize(Vector2Int size, MapShape shape)
    {
        if (CanFitSize(size, shape)) return;
        string minimum = shape == MapShape.Elongated ? "at least 25x13 (in either orientation)" : "at least 25x25";
        throw new ArgumentOutOfRangeException(nameof(size), size,
            $"{shape} maps require a size of {minimum} to provide connected floor space and two safe spawns.");
    }

    static bool CanFitSize(Vector2Int size, MapShape shape)
    {
        if (shape == MapShape.Elongated)
            return (size.x >= 25 && size.y >= 13) || (size.y >= 25 && size.x >= 13);
        return size.x >= 25 && size.y >= 25;
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
        if (float.IsNaN(settings.BushDensity) || float.IsInfinity(settings.BushDensity)
            || settings.BushDensity < 0f || settings.BushDensity > 1f)
            throw new ArgumentOutOfRangeException(nameof(settings.BushDensity),
                "Bush density must be a finite value between 0 and 1.");
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

    // 스폰 안전 구역이 모두 바닥인 자리를 스폰 영역의 최소 좌표 칸으로 모은다.
    static List<Vector2Int> GetSpawnCandidates(MapCell[,] cells)
    {
        const int clearance = MapLayout.SpawnClearance;
        var candidates = new List<Vector2Int>();
        for (int y = clearance; y <= cells.GetLength(1) - MapLayout.SpawnSize - clearance; y++)
        {
            for (int x = clearance; x <= cells.GetLength(0) - MapLayout.SpawnSize - clearance; x++)
            {
                bool safe = true;
                for (int dy = -clearance; dy < MapLayout.SpawnSize + clearance; dy++)
                    for (int dx = -clearance; dx < MapLayout.SpawnSize + clearance; dx++)
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
            return !SpawnsOverlap(spawnA, spawnB);
        }

        var size = new Vector2Int(cells.GetLength(0), cells.GetLength(1));
        int bestDistance = -1;
        foreach (Vector2Int candidate in candidates)
        {
            Vector2Int opposite = MirrorSpawn(candidate, size);
            if (candidate.y * size.x + candidate.x >= opposite.y * size.x + opposite.x
                || SpawnsOverlap(candidate, opposite)) continue;
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

    // 벽과 엄폐물을 세운다. 부시 설정은 받지 않으므로 부시 밀도가 달라도 같은 시드면 같은 자리에 선다.
    static int PlaceObstacles(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB, int floorCount,
        float density, MapSymmetry symmetry, Random random, int[,] distances, Queue<Vector2Int> queue)
    {
        int target = (int)Math.Floor((double)density * floorCount);
        if (target == 0) return 0;
        var placer = new ObstaclePlacer(cells, spawnA, spawnB, floorCount, symmetry, random, distances, queue);
        // 이어진 벽을 먼저 세우고, 남은 예산으로 그 사이에 독립 엄폐물을 흩어 둔다.
        placer.PlaceWalls((int)Math.Round(target * (1 - coverShare)));
        placer.PlaceCover(target);
        return placer.Placed;
    }

    // 부시는 벽과 엄폐물이 다 선 뒤에 그 사이 바닥에 둔다. 이 단계는 벽 단계 뒤에 난수를 쓰므로 벽을 바꾸지 않는다.
    static int PlaceBushes(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB, int floorCount,
        float density, MapSymmetry symmetry, Random random, int[,] distances, Queue<Vector2Int> queue)
    {
        if (density <= 0f) return 0;
        int budget = (int)Math.Round((double)density * floorCount);
        // 작은 맵에서 반올림한 칸 수가 덩어리 하나(대칭이면 한 쌍)에 못 미치면 부시가 하나도 생기지 않는다.
        // 부시 밀도가 0이 아니면 적어도 한 단위는 두도록 올린다.
        int bushUnit = symmetry == MapSymmetry.Rotate180 ? 2 * minBushSize : minBushSize;
        if (budget < bushUnit) budget = bushUnit;
        var bushes = new BushPlacer(cells, spawnA, spawnB, symmetry, random, distances, queue);
        bushes.Place(budget);
        return bushes.Placed;
    }

    // 장애물을 한 칸씩이 아니라 벽·기둥 같은 구조물 단위로 세운다.
    // 대칭 맵에서는 구조물과 그 180도 짝을 함께 세우거나 함께 거부한다.
    sealed class ObstaclePlacer
    {
        readonly MapCell[,] cells;
        readonly Vector2Int size;
        readonly Vector2Int spawnA;
        readonly Vector2Int spawnB;
        readonly int floorCount;
        readonly MapSymmetry symmetry;
        readonly Random random;
        readonly int[,] distances;
        readonly Queue<Vector2Int> queue;
        readonly List<Vector2Int> anchors = new List<Vector2Int>();
        readonly List<Vector2Int> wallCells = new List<Vector2Int>();
        readonly List<Vector2Int> shape = new List<Vector2Int>();
        readonly List<Vector2Int> group = new List<Vector2Int>();

        public int Placed { get; private set; }

        public ObstaclePlacer(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB, int floorCount,
            MapSymmetry symmetry, Random random, int[,] distances, Queue<Vector2Int> queue)
        {
            this.cells = cells;
            size = new Vector2Int(cells.GetLength(0), cells.GetLength(1));
            this.spawnA = spawnA;
            this.spawnB = spawnB;
            this.floorCount = floorCount;
            this.symmetry = symmetry;
            this.random = random;
            this.distances = distances;
            this.queue = queue;
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (IsFloor(cell) && !InSpawnZone(cell)) anchors.Add(cell);
                }
            }
        }

        // 곧은 벽과 L자 벽을 세우고, 세운 벽의 끝을 늘이거나 꺾어 긴 벽과 T자 벽을 만든다.
        public void PlaceWalls(int budget)
        {
            for (int i = 0; i < placementTries && Placed < budget && anchors.Count > 0; i++)
            {
                shape.Clear();
                if (wallCells.Count > 0 && random.NextDouble() < growChance)
                {
                    if (TryGrow(wallCells[random.Next(wallCells.Count)], out Vector2Int side)
                        && TryPlace(budget, side, 0)) wallCells.AddRange(group);
                    continue;
                }

                Vector2Int anchor = anchors[random.Next(anchors.Count)];
                Vector2Int direction = directions[random.Next(directions.Length)];
                if (random.NextDouble() < bentWallChance)
                {
                    Vector2Int turn = random.Next(2) == 0
                        ? new Vector2Int(-direction.y, direction.x) : new Vector2Int(direction.y, -direction.x);
                    AddLine(anchor, direction, random.Next(minArmLength, maxArmLength + 1));
                    AddLine(anchor + turn, turn, random.Next(minArmLength, maxArmLength + 1) - 1);
                }
                else
                    AddLine(anchor, direction, random.Next(minWallLength, maxWallLength + 1));
                // 벽끼리는 넉넉히 띄우되, 좁은 맵에서 자리가 없으면 남은 시도는 엄폐물만큼만 띄운다.
                int clearance = i < placementTries / 2 ? wallClearance : coverClearance;
                if (TryPlace(budget, Vector2Int.zero, clearance)) wallCells.AddRange(group);
            }
        }

        // 둘레가 비어 있는 2×2 기둥과 짧은 벽을 벽 사이사이에 따로 세운다.
        public void PlaceCover(int budget)
        {
            for (int i = 0; i < placementTries && Placed < budget && anchors.Count > 0; i++)
            {
                shape.Clear();
                Vector2Int anchor = anchors[random.Next(anchors.Count)];
                if (random.NextDouble() < pillarChance)
                {
                    AddLine(anchor, Vector2Int.right, 2);
                    AddLine(anchor + Vector2Int.up, Vector2Int.right, 2);
                }
                else
                    AddLine(anchor, directions[random.Next(directions.Length)],
                        random.Next(minCoverLength, maxCoverLength + 1));
                TryPlace(budget, Vector2Int.zero, coverClearance);
            }
        }

        void AddLine(Vector2Int start, Vector2Int direction, int length)
        {
            for (int i = 0; i < length; i++) shape.Add(start + direction * i);
        }

        // 직교 이웃 벽이 하나뿐인 벽 끝에서 바깥으로 벽을 늘린다. side는 늘린 벽의 옆면 방향이다.
        bool TryGrow(Vector2Int end, out Vector2Int side)
        {
            side = default;
            Vector2Int forward = default;
            int neighbors = 0;
            foreach (Vector2Int direction in directions)
            {
                if (!IsWall(end + direction)) continue;
                neighbors++;
                forward = -direction;
            }
            if (neighbors != 1) return false;

            int run = 0;
            for (Vector2Int cell = end; IsWall(cell); cell -= forward) run++;
            int length = random.Next(minGrowLength, maxGrowLength + 1);
            // 곧게만 늘리면 맵을 가로지르는 긴 막대가 되므로 일정 길이를 넘으면 꺾는다.
            if (random.NextDouble() < turnChance || run + length > maxRunLength)
                forward = random.Next(2) == 0
                    ? new Vector2Int(-forward.y, forward.x) : new Vector2Int(forward.y, -forward.x);
            AddLine(end + forward, forward, length);
            side = new Vector2Int(forward.y, forward.x);
            return true;
        }

        // shape과 그 대칭 짝을 벽으로 세워 보고, 규칙을 하나라도 어기면 되돌린다.
        // side가 0이면 새 구조물이라 둘레 clearance칸이 비어 있어야 하고, 아니면 늘린 벽이라 옆면만 비어 있으면 된다.
        bool TryPlace(int budget, Vector2Int side, int clearance)
        {
            group.Clear();
            group.AddRange(shape);
            if (symmetry == MapSymmetry.Rotate180)
            {
                foreach (Vector2Int cell in shape)
                {
                    Vector2Int opposite = Mirror(cell, size);
                    if (!group.Contains(opposite)) group.Add(opposite);
                }
            }
            if (Placed + group.Count > budget) return false;
            foreach (Vector2Int cell in shape)
                if (!IsFloor(cell) || InSpawnZone(cell)) return false;
            if (side == Vector2Int.zero && !HasClearance(clearance)) return false;

            foreach (Vector2Int cell in group) cells[cell.x, cell.y] = MapCell.Wall;
            if (BreaksRules(side) || FloodFill(cells, spawnA, distances, queue) != floorCount - Placed - group.Count)
            {
                foreach (Vector2Int cell in group) cells[cell.x, cell.y] = MapCell.Floor;
                return false;
            }
            Placed += group.Count;
            return true;
        }

        // 새 구조물 둘레 clearance칸이 모두 바닥이고, 대칭 짝과도 그만큼 떨어져 있는지.
        // 형태 단계의 맵은 대칭이므로 짝의 둘레는 따로 볼 필요가 없다.
        bool HasClearance(int clearance)
        {
            foreach (Vector2Int cell in shape)
            {
                for (int dy = -clearance; dy <= clearance; dy++)
                {
                    for (int dx = -clearance; dx <= clearance; dx++)
                    {
                        Vector2Int next = cell + new Vector2Int(dx, dy);
                        if (!shape.Contains(next) && !IsFloor(next)) return false;
                    }
                }
            }
            for (int i = shape.Count; i < group.Count; i++)
                foreach (Vector2Int cell in shape)
                    if (Math.Abs(group[i].x - cell.x) <= clearance
                        && Math.Abs(group[i].y - cell.y) <= clearance) return false;
            return true;
        }

        bool BreaksRules(Vector2Int side)
        {
            foreach (Vector2Int cell in group)
            {
                // 늘린 벽은 끝 칸만 다른 벽에 닿을 수 있다. 옆면이 붙으면 두꺼운 덩어리가 된다.
                if (side != Vector2Int.zero && (IsWall(cell + side) || IsWall(cell - side))) return true;
                if (CreatesDiagonalPinch(cells, size, cell) || CreatesSolidBlock(cells, size, cell)
                    || CreatesLongCorridor(cell)) return true;
            }
            return false;
        }

        // cell 옆 바닥이 벽 사이에 끼어 1칸 폭 복도가 길게 이어지는지. 짧은 1칸 틈은 문처럼 쓰이므로 허용한다.
        bool CreatesLongCorridor(Vector2Int cell)
        {
            foreach (Vector2Int across in directions)
            {
                Vector2Int floor = cell + across;
                if (IsFloor(floor) && CorridorLength(floor, across) > maxCorridorLength) return true;
            }
            return false;
        }

        // start에서 across 축 양옆이 벽인 바닥이 그 수직 방향으로 몇 칸 이어지는지.
        int CorridorLength(Vector2Int start, Vector2Int across)
        {
            var along = new Vector2Int(across.y, across.x);
            int length = 0;
            for (Vector2Int cell = start; IsPinched(cell, across); cell += along) length++;
            for (Vector2Int cell = start - along; IsPinched(cell, across); cell -= along) length++;
            return length;
        }

        bool IsPinched(Vector2Int cell, Vector2Int across)
        {
            return IsFloor(cell) && IsWall(cell + across) && IsWall(cell - across);
        }

        bool InSpawnZone(Vector2Int cell)
        {
            return MapLayout.InSpawnZone(cell, spawnA) || MapLayout.InSpawnZone(cell, spawnB);
        }

        bool IsFloor(Vector2Int cell) => InBounds(cell) && cells[cell.x, cell.y] == MapCell.Floor;
        bool IsWall(Vector2Int cell) => InBounds(cell) && cells[cell.x, cell.y] == MapCell.Wall;
        bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < size.x && cell.y < size.y;
    }

    // 벽과 엄폐물 사이 바닥에 부시를 덩어리 단위로 둔다. 부시는 길을 막지 않으므로 연결성은 보지 않는다.
    // 중앙 부시를 먼저 두고, 남은 예산은 덩어리·넓게 퍼진 부시·벽 따라 늘어진 부시·길게 이어진 부시를 섞어 쓴다.
    // 덩어리끼리는 대각선까지 bushGap칸 띄워서, 8방향으로 이어진 칸 = 한 덩어리(BushRegions)가 되게 한다.
    // 대칭 맵에서는 덩어리와 180도 짝을 함께 둔다. 중앙 부시만 맵 가운데에서 짝과 닿으면 하나로 합친다.
    sealed class BushPlacer
    {
        // 덩어리 모양. 남은 예산으로 하나씩 꺼내 쓰고, 다 쓰면 다시 섞어 채운다.
        enum BushShape { Patch, Spread, WallStrip, Line }

        // 칸을 고를 수 있는 구역.
        enum Area
        {
            Any,
            Open,    // 벽에서 openFieldMinDistance칸 이상 떨어진 빈터
            NearWall // 벽에서 stripDepth칸 안
        }

        // 덩어리를 키울 때 이웃 칸을 고르는 가중치.
        enum GrowMode
        {
            Round,  // 이미 붙은 칸과 많이 닿는 자리: 둥글게 자란다
            Sprawl, // 처음 핵에서 먼 자리도 자주 고른다: 넓게 퍼진다
            WallHug // 벽에 닿은 자리를 훨씬 자주 고른다: 벽을 따라 늘어진다
        }

        static readonly BushShape[] shapes = { BushShape.Patch, BushShape.Spread, BushShape.WallStrip, BushShape.Line };

        readonly MapCell[,] cells;
        readonly Vector2Int size;
        readonly Vector2Int spawnA;
        readonly Vector2Int spawnB;
        readonly bool symmetric;
        readonly Random random;
        readonly List<Vector2Int> anySeeds = new List<Vector2Int>();
        readonly List<Vector2Int> centerSeeds = new List<Vector2Int>();
        readonly List<Vector2Int> fallbackCenterSeeds = new List<Vector2Int>();
        readonly List<Vector2Int> innerWallSeeds = new List<Vector2Int>();
        readonly List<Vector2Int> outerWallSeeds = new List<Vector2Int>();
        readonly List<Vector2Int> openSeeds = new List<Vector2Int>();
        readonly List<Vector2Int> lineSeeds = new List<Vector2Int>();
        readonly List<Vector2Int> cluster = new List<Vector2Int>();
        readonly HashSet<Vector2Int> members = new HashSet<Vector2Int>();
        readonly List<Vector2Int> frontier = new List<Vector2Int>();
        readonly List<int> weights = new List<int>();
        readonly HashSet<Vector2Int> listed = new HashSet<Vector2Int>();
        readonly Queue<Vector2Int> queue;
        int[,] wallDistances;

        public int Placed { get; private set; }

        public BushPlacer(MapCell[,] cells, Vector2Int spawnA, Vector2Int spawnB, MapSymmetry symmetry,
            Random random, int[,] distances, Queue<Vector2Int> queue)
        {
            this.cells = cells;
            size = new Vector2Int(cells.GetLength(0), cells.GetLength(1));
            this.spawnA = spawnA;
            this.spawnB = spawnB;
            symmetric = symmetry == MapSymmetry.Rotate180;
            this.random = random;
            this.queue = queue;
            CollectSeeds(distances);
        }

        // 중앙 부시를 먼저 두고(대칭이면 한 쌍, 아니면 두 덩어리), 남은 예산은 섞은 모양 순서대로 쓴다.
        // 한 모양이 shapeFailLimit번 연달아 자리를 못 찾거나 남은 예산보다 크면 그 모양은 더 쓰지 않는다.
        public void Place(int budget)
        {
            if (budget < minBushSize) return;
            int wantedCenters = symmetric ? 1 : 2;
            int centers = 0;
            // 큰길 쪽 칸에 자리가 없으면 남은 시도는 거리 띠 전체에서 고른다.
            for (int i = 0; i < centerBushTries && centers < wantedCenters; i++)
            {
                List<Vector2Int> seeds = i < centerBushTries / 2 ? centerSeeds : fallbackCenterSeeds;
                if (seeds.Count > 0 && TryPlaceCenter(Pick(seeds), budget)) centers++;
            }

            var bag = new List<BushShape>();
            var fails = new int[shapes.Length];
            var spent = new bool[shapes.Length];
            int needed = symmetric ? 2 * minBushSize : minBushSize;
            for (int i = 0; i < bushTries && budget - Placed >= needed; i++)
            {
                if (bag.Count == 0 && !Refill(bag, spent)) break;
                BushShape shape = bag[bag.Count - 1];
                int remaining = budget - Placed;
                int cap = symmetric ? remaining / 2 : remaining;
                if (cap < MinSize(shape)) spent[(int)shape] = true;
                else if (TryPlaceShape(shape, cap))
                {
                    fails[(int)shape] = 0;
                    bag.RemoveAt(bag.Count - 1);
                    continue;
                }
                else if (++fails[(int)shape] >= shapeFailLimit) spent[(int)shape] = true;
                if (spent[(int)shape]) bag.RemoveAt(bag.Count - 1);
            }
        }

        // 아직 쓸 수 있는 모양을 모두 넣고 섞는다. 넣을 모양이 없으면 false.
        bool Refill(List<BushShape> bag, bool[] spent)
        {
            foreach (BushShape shape in shapes)
                if (!spent[(int)shape] && SeedsOf(shape).Count > 0) bag.Add(shape);
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            return bag.Count > 0;
        }

        static int MinSize(BushShape shape)
        {
            switch (shape)
            {
                case BushShape.Spread: return spreadMinSize;
                case BushShape.WallStrip: return stripMinSize;
                case BushShape.Line: return lineMinLength;
                default: return minBushSize;
            }
        }

        List<Vector2Int> SeedsOf(BushShape shape)
        {
            switch (shape)
            {
                case BushShape.Spread: return openSeeds;
                case BushShape.WallStrip: return innerWallSeeds.Count > 0 ? innerWallSeeds : outerWallSeeds;
                case BushShape.Line: return lineSeeds.Count > 0 ? lineSeeds : anySeeds;
                default: return anySeeds;
            }
        }

        // 벽을 다 세운 맵에서 씨앗 후보를 한 번만 모은다. 자리가 아직 비었는지는 덩어리를 둘 때 다시 본다.
        void CollectSeeds(int[,] fromB)
        {
            var fromA = new int[size.x, size.y];
            FloodFill(cells, spawnA, fromA, queue);
            FloodFill(cells, spawnB, fromB, queue);
            wallDistances = WallDistances();

            var openDistances = new List<int>();
            int minSum = int.MaxValue, maxSum = int.MinValue;
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!CanHoldBush(cell)) continue;
                    anySeeds.Add(cell);
                    openDistances.Add(wallDistances[x, y]);
                    if (IsCentral(cell, fromA, fromB))
                    {
                        int sum = fromA[x, y] + fromB[x, y];
                        minSum = Math.Min(minSum, sum);
                        maxSum = Math.Max(maxSum, sum);
                    }
                    if (TouchesWall(cell)) (TouchesInnerWall(cell) ? innerWallSeeds : outerWallSeeds).Add(cell);
                    if (wallDistances[x, y] >= openFieldMinDistance) lineSeeds.Add(cell);
                }
            }
            if (anySeeds.Count == 0) return;
            if (maxSum < minSum) minSum = maxSum = -1; // 같은 거리 띠가 없다.

            openDistances.Sort();
            int openThreshold = Math.Max(openFieldMinDistance,
                openDistances[Math.Min(openDistances.Count - 1, (int)(openDistances.Count * openFieldPercentile))]);
            // 같은 거리 띠에서도 두 스폰을 잇는 큰길 쪽(경로 거리 합이 짧은 칸)에 둔다.
            int centerLimit = minSum + (maxSum - minSum) / 2;
            foreach (Vector2Int cell in anySeeds)
            {
                if (IsCentral(cell, fromA, fromB))
                {
                    fallbackCenterSeeds.Add(cell);
                    if (fromA[cell.x, cell.y] + fromB[cell.x, cell.y] <= centerLimit) centerSeeds.Add(cell);
                }
                if (wallDistances[cell.x, cell.y] >= openThreshold) openSeeds.Add(cell);
            }
        }

        static bool IsCentral(Vector2Int cell, int[,] fromA, int[,] fromB)
        {
            int a = fromA[cell.x, cell.y], b = fromB[cell.x, cell.y];
            return a >= 0 && b >= 0 && Math.Abs(a - b) <= centerBushTolerance;
        }

        // 모든 벽에서 동시에 퍼져 나가며 잰 8방향 칸 거리. 바닥만 지난다. 벽에 닿지 않는 바닥은 -1이다.
        int[,] WallDistances()
        {
            var result = new int[size.x, size.y];
            queue.Clear();
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    bool wall = cells[x, y] == MapCell.Wall;
                    result[x, y] = wall ? 0 : -1;
                    if (wall) queue.Enqueue(new Vector2Int(x, y));
                }
            }
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector2Int next = cell + new Vector2Int(dx, dy);
                        if (!IsFloor(next) || result[next.x, next.y] >= 0) continue;
                        result[next.x, next.y] = result[cell.x, cell.y] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return result;
        }

        // 두 스폰 사이 길목의 중앙 부시. 씨앗을 품은 2×2 핵에서 둥글게, 또는 넓게 퍼지게 키운다.
        bool TryPlaceCenter(Vector2Int seed, int budget)
        {
            int remaining = budget - Placed;
            if (remaining < minBushSize || !TryCenterCore(seed, out bool merged)) return false;
            // 짝을 따로 두면 예산을 두 배로 쓴다.
            int cap = Math.Min(centerMaxSize, merged || !symmetric ? remaining : remaining / 2);
            if (cluster.Count > cap || (!merged && cap < minBushSize)) return false;
            int target = Math.Min(random.Next(centerMinSize, centerMaxSize + 1), cap);
            // 맵마다 하나뿐인 부시라 둥근 덩어리와 넓게 퍼진 모양을 섞어 쓴다.
            Grow(target, cap, merged, Area.Any, random.Next(2) == 0 ? GrowMode.Round : GrowMode.Sprawl, seed);
            Commit(merged);
            return true;
        }

        // 씨앗 칸을 품는 2×2 핵을 찾는다. 대칭 맵에서 핵이 자기 짝과 닿으면 짝까지 합쳐 하나의 덩어리로 둔다.
        // 닿지는 않지만 bushGap칸보다 가까우면 두 덩어리가 하나처럼 보이므로 다른 자리를 본다.
        bool TryCenterCore(Vector2Int seed, out bool merged)
        {
            merged = false;
            int start = random.Next(coreOffsets.Length);
            for (int i = 0; i < coreOffsets.Length; i++)
            {
                Vector2Int origin = seed - coreOffsets[(start + i) % coreOffsets.Length];
                Clear();
                bool fits = true;
                foreach (Vector2Int offset in coreOffsets)
                {
                    Vector2Int cell = origin + offset;
                    if (!CanHoldBush(cell))
                    {
                        fits = false;
                        break;
                    }
                    Add(cell);
                }
                if (!fits) continue;
                if (!symmetric) return true;

                bool touches = false, near = false;
                foreach (Vector2Int cell in cluster)
                {
                    touches |= NearMirror(cell, 1);
                    near |= NearMirror(cell, bushGap);
                }
                if (!near) return true;
                if (!touches) continue;

                for (int j = 0; j < coreOffsets.Length; j++)
                {
                    Vector2Int opposite = Mirror(cluster[j], size);
                    if (!members.Contains(opposite)) Add(opposite);
                }
                // 대각선으로만 닿으면 한 덩어리처럼 보이지 않으므로 다른 핵 자리를 본다.
                if (!IsConnected()) continue;
                merged = true;
                return true;
            }
            Clear();
            return false;
        }

        // 고른 모양을 씨앗 하나로 한 번 지어 본다. 다 지으면 맵에 칠한다. cap은 이 덩어리(짝 제외)에 쓸 수 있는 칸 수다.
        bool TryPlaceShape(BushShape shape, int cap)
        {
            Vector2Int seed = shape == BushShape.WallStrip ? PickWallSeed() : Pick(SeedsOf(shape));
            bool built;
            switch (shape)
            {
                case BushShape.Spread: built = BuildSpread(seed, cap); break;
                case BushShape.WallStrip: built = BuildStrip(seed, cap); break;
                case BushShape.Line: built = BuildLine(seed, cap); break;
                default: built = BuildPatch(seed, cap); break;
            }
            if (!built || cluster.Count > cap)
            {
                Clear();
                return false;
            }
            Commit(false);
            return true;
        }

        // 덩어리: 씨앗을 품는 2×2·3×2·2×3·3×3 사각형. 고른 크기가 안 들어가면 작은 사각형으로 내려간다.
        // 가끔 옆에 한두 칸을 덧붙여 반듯한 사각형만 나오지 않게 한다.
        bool BuildPatch(Vector2Int seed, int cap)
        {
            int total = 0;
            for (int i = 0; i < patchSizes.Length; i++)
                if (CellCount(patchSizes[i]) <= cap) total += patchWeights[i];
            if (total == 0) return false;
            int pick = random.Next(total), choice = 0;
            while (CellCount(patchSizes[choice]) > cap || pick >= patchWeights[choice])
            {
                if (CellCount(patchSizes[choice]) <= cap) pick -= patchWeights[choice];
                choice++;
            }

            for (int s = choice; s >= 0; s--)
            {
                Vector2Int dims = patchSizes[s];
                int positions = CellCount(dims);
                if (positions > cap) continue;
                int start = random.Next(positions);
                for (int p = 0; p < positions; p++)
                {
                    int k = (start + p) % positions;
                    if (!TryRect(seed - new Vector2Int(k % dims.x, k / dims.x), dims, Area.Any)) continue;
                    if (cluster.Count < cap && random.NextDouble() < patchBumpChance)
                        Grow(Math.Min(cap, cluster.Count + random.Next(1, 3)), cap, false, Area.Any, GrowMode.Round, seed);
                    return true;
                }
            }
            return false;
        }

        // 넓게 퍼진 부시: 빈터의 2×2 핵에서 핵과 먼 자리도 자주 골라 넓고 들쭉날쭉하게 키운다.
        bool BuildSpread(Vector2Int seed, int cap)
        {
            if (!TryCore(seed, Area.Open)) return false;
            int target = Math.Min(random.Next(spreadTargetMin, spreadMaxSize + 1), cap);
            Grow(target, cap, false, Area.Open, GrowMode.Sprawl, seed);
            return cluster.Count >= spreadMinSize;
        }

        // 벽 따라 늘어진 부시: 벽에 붙은 씨앗과 벽을 따라 옆 칸에서 시작해, 벽에 닿은 자리를 훨씬 자주 골라 키운다.
        // 벽에서 stripDepth칸 안에서만 자라고, 긴 변이 stripMinLength칸에 못 미치면 버린다.
        bool BuildStrip(Vector2Int seed, int cap)
        {
            int start = random.Next(directions.Length);
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int toWall = directions[(start + i) % directions.Length];
                if (!IsWall(seed + toWall)) continue;
                Vector2Int along = Perpendicular(toWall) * (random.Next(2) == 0 ? 1 : -1);
                Clear();
                if (!TryAdd(seed, Area.NearWall)) return false;
                if (!TryAdd(seed + along, Area.NearWall) && !TryAdd(seed - along, Area.NearWall)) continue;
                int target = Math.Min(random.Next(stripMinSize, stripMaxSize + 1), cap);
                Grow(target, cap, false, Area.NearWall, GrowMode.WallHug, seed);
                if (cluster.Count >= stripMinSize && LongSide() >= stripMinLength) return true;
            }
            return false;
        }

        // 길게 이어진 부시: 씨앗에서 한 방향으로 뻗는 1칸 또는 2칸 폭의 띠. 가끔 한 번 꺾여 L자가 된다.
        // 2칸 폭 띠는 첫 팔을 옆으로, 꺾인 팔을 뒤쪽으로 두껍게 해서 모서리가 빈틈없이 이어지게 한다.
        bool BuildLine(Vector2Int seed, int cap)
        {
            bool wide = cap >= 2 * wideLineMinLength && random.NextDouble() < wideLineChance;
            if (!wide && cap < lineMinLength) return false;
            int length = wide
                ? Math.Min(random.Next(wideLineMinLength, wideLineMaxLength + 1), cap / 2)
                : Math.Min(random.Next(lineMinLength, lineMaxLength + 1), cap);
            // 꺾인 띠에서 첫 팔의 칸 수(모서리 포함). 두 팔 모두 모서리를 포함해 lineArmMin칸 이상이다.
            int bend = length - 2 >= lineArmMin && random.NextDouble() < lineBendChance
                ? random.Next(lineArmMin, length - 1) : length;
            int start = random.Next(directions.Length);
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int along = directions[(start + i) % directions.Length];
                Vector2Int side = Perpendicular(along) * (random.Next(2) == 0 ? 1 : -1);
                // 2칸 폭 띠가 두께 쪽으로 꺾이면 모서리가 뭉툭한 덩어리가 되므로 반대쪽으로 꺾는다.
                Vector2Int turn = wide ? -side : Perpendicular(along) * (random.Next(2) == 0 ? 1 : -1);
                if (TryLine(seed, along, side, turn, length, bend, wide)) return true;
            }
            return false;
        }

        bool TryLine(Vector2Int seed, Vector2Int along, Vector2Int side, Vector2Int turn, int length, int bend, bool wide)
        {
            Clear();
            for (int i = 0; i < bend; i++)
            {
                Vector2Int cell = seed + along * i;
                if (!TryAdd(cell, Area.Any) || (wide && !TryAdd(cell + side, Area.Any))) return false;
            }
            Vector2Int corner = seed + along * (bend - 1);
            for (int j = 1; j <= length - bend; j++)
            {
                Vector2Int cell = corner + turn * j;
                if (!TryAdd(cell, Area.Any) || (wide && !TryAdd(cell - along, Area.Any))) return false;
            }
            return true;
        }

        // 씨앗을 품는 2×2 핵을 찾는다.
        bool TryCore(Vector2Int seed, Area area)
        {
            int start = random.Next(coreOffsets.Length);
            for (int i = 0; i < coreOffsets.Length; i++)
                if (TryRect(seed - coreOffsets[(start + i) % coreOffsets.Length], new Vector2Int(2, 2), area)) return true;
            return false;
        }

        bool TryRect(Vector2Int origin, Vector2Int dims, Area area)
        {
            Clear();
            for (int y = 0; y < dims.y; y++)
                for (int x = 0; x < dims.x; x++)
                    if (!TryAdd(origin + new Vector2Int(x, y), area)) return false;
            return true;
        }

        // 짝과 따로 두는 덩어리에 cell을 붙인다. 이미 붙어 있으면 그대로 true다.
        bool TryAdd(Vector2Int cell, Area area)
        {
            if (members.Contains(cell)) return true;
            if (!CanHoldBush(cell, area) || (symmetric && NearMirror(cell, bushGap))) return false;
            Add(cell);
            return true;
        }

        // 덩어리에 붙은 바닥을 하나씩 붙여 target칸까지 키운다. 이웃 칸을 고르는 가중치는 mode가 정한다.
        // merged면 짝과 합친 덩어리라 고른 칸의 짝도 함께 붙이고, 아니면 짝과 bushGap칸 넘게 떨어진 칸만 붙인다.
        void Grow(int target, int cap, bool merged, Area area, GrowMode mode, Vector2Int origin)
        {
            while (cluster.Count < target)
            {
                frontier.Clear();
                weights.Clear();
                listed.Clear();
                int total = 0;
                for (int i = 0; i < cluster.Count; i++)
                {
                    foreach (Vector2Int direction in directions)
                    {
                        Vector2Int next = cluster[i] + direction;
                        if (members.Contains(next) || listed.Contains(next) || !CanHoldBush(next, area)) continue;
                        if (merged)
                        {
                            if (cluster.Count + (Mirror(next, size) == next ? 1 : 2) > cap) continue;
                        }
                        else if (symmetric && NearMirror(next, bushGap)) continue;

                        int weight = GrowWeight(next, mode, origin);
                        listed.Add(next);
                        frontier.Add(next);
                        weights.Add(weight);
                        total += weight;
                    }
                }
                if (frontier.Count == 0) return;

                int pick = random.Next(total);
                int index = 0;
                while (pick >= weights[index]) pick -= weights[index++];
                Vector2Int chosen = frontier[index];
                Add(chosen);
                if (merged && !members.Contains(Mirror(chosen, size))) Add(Mirror(chosen, size));
            }
        }

        int GrowWeight(Vector2Int cell, GrowMode mode, Vector2Int origin)
        {
            int neighbors = ClusterNeighbors(cell);
            switch (mode)
            {
                case GrowMode.Sprawl:
                    return neighbors + Math.Min(Math.Abs(cell.x - origin.x) + Math.Abs(cell.y - origin.y), sprawlReach);
                case GrowMode.WallHug:
                    return TouchesWall(cell) ? stripWallWeight : neighbors >= 2 ? stripFillWeight : 1;
                default:
                    return neighbors;
            }
        }

        // 다 지은 덩어리를 맵에 칠한다. 대칭 맵에서 짝과 합치지 않은 덩어리는 180도 짝도 함께 칠한다.
        void Commit(bool merged)
        {
            foreach (Vector2Int cell in cluster) cells[cell.x, cell.y] = MapCell.Bush;
            Placed += cluster.Count;
            if (symmetric && !merged)
            {
                foreach (Vector2Int cell in cluster)
                {
                    Vector2Int opposite = Mirror(cell, size);
                    cells[opposite.x, opposite.y] = MapCell.Bush;
                }
                Placed += cluster.Count;
            }
            Clear();
        }

        void Add(Vector2Int cell)
        {
            cluster.Add(cell);
            members.Add(cell);
        }

        void Clear()
        {
            cluster.Clear();
            members.Clear();
        }

        // 지금 덩어리에 cell을 붙였을 때 그 180도 짝 덩어리가 cell에서 radius칸(대각선 포함) 안에 들어오는지.
        // 짝의 칸 m이 cell 둘레에 있다는 것은 Mirror(m)이 덩어리(또는 cell 자신)라는 뜻이다.
        bool NearMirror(Vector2Int cell, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector2Int opposite = Mirror(cell + new Vector2Int(dx, dy), size);
                    if (opposite == cell || members.Contains(opposite)) return true;
                }
            }
            return false;
        }

        // 덩어리 칸이 상하좌우로 모두 이어져 있는지.
        bool IsConnected()
        {
            listed.Clear();
            queue.Clear();
            queue.Enqueue(cluster[0]);
            listed.Add(cluster[0]);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = cell + direction;
                    if (!members.Contains(next) || !listed.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            return listed.Count == cluster.Count;
        }

        int ClusterNeighbors(Vector2Int cell)
        {
            int count = 0;
            foreach (Vector2Int direction in directions)
                if (members.Contains(cell + direction)) count++;
            return count;
        }

        // 덩어리를 감싸는 사각형의 긴 변 칸 수.
        int LongSide()
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (Vector2Int cell in cluster)
            {
                minX = Math.Min(minX, cell.x);
                maxX = Math.Max(maxX, cell.x);
                minY = Math.Min(minY, cell.y);
                maxY = Math.Max(maxY, cell.y);
            }
            return Math.Max(maxX - minX, maxY - minY) + 1;
        }

        // 부시를 둘 수 있는 바닥인지에 구역 조건을 더한다.
        bool CanHoldBush(Vector2Int cell, Area area)
        {
            if (!CanHoldBush(cell)) return false;
            int distance = wallDistances[cell.x, cell.y];
            switch (area)
            {
                case Area.Open: return distance >= openFieldMinDistance;
                case Area.NearWall: return distance >= 0 && distance <= stripDepth;
                default: return true;
            }
        }

        // 부시를 둘 수 있는 바닥인지. 스폰 둘레, 다른 덩어리 둘레 bushGap칸, 양옆이 벽인 1칸 틈(문)은 뺀다.
        bool CanHoldBush(Vector2Int cell)
        {
            if (!IsFloor(cell)
                || MapLayout.InSpawnZone(cell, spawnA, MapLayout.BushSpawnClearance)
                || MapLayout.InSpawnZone(cell, spawnB, MapLayout.BushSpawnClearance)) return false;
            if ((IsWall(cell + Vector2Int.left) && IsWall(cell + Vector2Int.right))
                || (IsWall(cell + Vector2Int.up) && IsWall(cell + Vector2Int.down))) return false;
            for (int dy = -bushGap; dy <= bushGap; dy++)
                for (int dx = -bushGap; dx <= bushGap; dx++)
                    if (IsBush(cell + new Vector2Int(dx, dy))) return false;
            return true;
        }

        bool TouchesWall(Vector2Int cell)
        {
            foreach (Vector2Int direction in directions)
                if (IsWall(cell + direction)) return true;
            return false;
        }

        // 외곽 벽은 빈 공간과 맞닿아 있고, 바닥에 세운 구조물은 둘레가 모두 맵 안이다.
        bool TouchesInnerWall(Vector2Int cell)
        {
            foreach (Vector2Int direction in directions)
            {
                Vector2Int wall = cell + direction;
                if (!IsWall(wall)) continue;
                bool inner = true;
                for (int dy = -1; dy <= 1 && inner; dy++)
                    for (int dx = -1; dx <= 1 && inner; dx++)
                    {
                        Vector2Int next = wall + new Vector2Int(dx, dy);
                        inner = InBounds(next) && cells[next.x, next.y] != MapCell.Void;
                    }
                if (inner) return true;
            }
            return false;
        }

        Vector2Int PickWallSeed()
        {
            bool inner = innerWallSeeds.Count > 0
                && (outerWallSeeds.Count == 0 || random.NextDouble() < innerWallChance);
            return Pick(inner ? innerWallSeeds : outerWallSeeds);
        }

        Vector2Int Pick(List<Vector2Int> seeds) => seeds[random.Next(seeds.Count)];

        static int CellCount(Vector2Int dims) => dims.x * dims.y;
        static Vector2Int Perpendicular(Vector2Int direction) => new Vector2Int(-direction.y, direction.x);

        bool IsFloor(Vector2Int cell) => InBounds(cell) && cells[cell.x, cell.y] == MapCell.Floor;
        bool IsWall(Vector2Int cell) => InBounds(cell) && cells[cell.x, cell.y] == MapCell.Wall;
        bool IsBush(Vector2Int cell) => InBounds(cell) && cells[cell.x, cell.y] == MapCell.Bush;
        bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < size.x && cell.y < size.y;
    }

    // 180도 돌리면 영역의 최소 좌표 칸은 반대편 영역의 최대 좌표 칸이 된다.
    static Vector2Int MirrorSpawn(Vector2Int spawn, Vector2Int size)
    {
        return Mirror(spawn, size) - Vector2Int.one * (MapLayout.SpawnSize - 1);
    }

    // (x, y)에 둔 벽이 대각선 이웃 벽과 모서리만 맞닿는지 검사한다.
    // wall-wall 대각선 + floor-floor 직교 = 가운데 틈 너비 0m. FloodFill은 상하좌우만 세므로 막힌 길로 보지만,
    // 화면에서는 지나갈 수 있는 틈처럼 보이고 거친 벽 메시가 서로 겹치므로 만들지 않는다.
    static bool CreatesDiagonalPinch(MapCell[,] cells, Vector2Int size, Vector2Int cell)
    {
        int width = size.x, height = size.y;
        int[] dx = { 1, 1, -1, -1 };
        int[] dy = { 1, -1, 1, -1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = cell.x + dx[i], ny = cell.y + dy[i];
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            if (cells[nx, ny] != MapCell.Wall) continue;
            // 대각선이 벽이면 나머지 두 직교칸이 모두 바닥일 때 핀치가 된다.
            int ox1 = cell.x + dx[i], oy1 = cell.y;
            int ox2 = cell.x, oy2 = cell.y + dy[i];
            bool floor1 = ox1 >= 0 && oy1 >= 0 && ox1 < width && oy1 < height && cells[ox1, oy1] == MapCell.Floor;
            bool floor2 = ox2 >= 0 && oy2 >= 0 && ox2 < width && oy2 < height && cells[ox2, oy2] == MapCell.Floor;
            if (floor1 && floor2) return true;
        }
        return false;
    }

    // cell을 포함하는 3×3 창 중 모두 벽인 곳이 있는지. 두꺼운 벽 덩어리는 엄폐물이 아니라 막힌 땅처럼 보인다.
    static bool CreatesSolidBlock(MapCell[,] cells, Vector2Int size, Vector2Int cell)
    {
        for (int cy = cell.y - 1; cy <= cell.y + 1; cy++)
        {
            for (int cx = cell.x - 1; cx <= cell.x + 1; cx++)
            {
                bool solid = true;
                for (int y = cy - 1; y <= cy + 1 && solid; y++)
                    for (int x = cx - 1; x <= cx + 1 && solid; x++)
                        solid = x >= 0 && y >= 0 && x < size.x && y < size.y && cells[x, y] == MapCell.Wall;
                if (solid) return true;
            }
        }
        return false;
    }

    static bool SpawnsOverlap(Vector2Int a, Vector2Int b)
    {
        return Math.Abs(a.x - b.x) < MapLayout.SpawnSize && Math.Abs(a.y - b.y) < MapLayout.SpawnSize;
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
