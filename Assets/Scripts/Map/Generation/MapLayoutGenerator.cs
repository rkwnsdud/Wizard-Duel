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

    static readonly Vector2Int[] directions =
    {
        Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down
    };

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
            return new MapLayout(cells, spawnA, spawnB, shape, (float)obstacles / floorCount);
        }

        throw new InvalidOperationException(
            $"Could not generate a connected {shape} map of size {size.x}x{size.y} " +
            $"with two safe spawns after {maxAttempts} attempts (seed {settings.Seed}).");
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
