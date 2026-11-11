using System.Collections.Generic;
using UnityEngine;

// AI 대전의 봇이 길을 찾고 벽에 가렸는지 볼 때 쓰는 전투 맵 격자.
// 칸 좌표는 MapLayout과 같고(x는 열, y는 행), 월드 좌표와의 변환은 ProceduralMap·BushField와 같다.
// 물리 판정을 쓰지 않으므로 EditMode 테스트로 따로 검사한다.
public sealed class BotGrid
{
    const float CellSize = 1f;
    const float Diagonal = 1.41421356f;
    // 벽이나 맵 밖과 맞닿은 칸에 더하는 비용. 길이 벽에 바짝 붙지 않아 모서리에 덜 걸린다.
    const float WallSidePenalty = 0.4f;
    static readonly Vector2Int[] Steps =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    readonly bool[,] walkable;
    readonly int[,] bushIds;
    readonly float[] penalty;
    readonly Transform root;
    readonly List<Vector2Int> floorCells = new List<Vector2Int>();
    readonly List<Vector2Int> bushCells = new List<Vector2Int>();

    // 길찾기 한 번마다 다시 쓰는 작업 공간. 칸 번호는 y * Width + x다.
    readonly float[] cost;
    readonly int[] parent;
    readonly int[] visit;
    readonly List<int> heap = new List<int>();
    readonly List<float> heapKeys = new List<float>();
    int search;

    public int Width { get; }
    public int Height { get; }

    // root는 맵 블록을 깐 부모다. 없으면 맵 중심이 월드 원점에 있다고 본다.
    public BotGrid(MapLayout layout, Transform root) : this(Walkable(layout), BushIds(layout), root)
    {
    }

    // walkable[x, y]가 참인 칸만 걸을 수 있다. 부시는 없는 맵으로 본다.
    public BotGrid(bool[,] walkable, Transform root = null) : this(walkable, null, root)
    {
    }

    BotGrid(bool[,] walkable, int[,] bushIds, Transform root)
    {
        this.walkable = walkable;
        this.bushIds = bushIds;
        this.root = root;
        Width = walkable.GetLength(0);
        Height = walkable.GetLength(1);
        penalty = new float[Width * Height];
        cost = new float[Width * Height];
        parent = new int[Width * Height];
        visit = new int[Width * Height];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                if (!walkable[x, y]) continue;
                bool besideWall = !IsWalkable(x + 1, y) || !IsWalkable(x - 1, y) || !IsWalkable(x, y + 1) || !IsWalkable(x, y - 1);
                penalty[y * Width + x] = besideWall ? WallSidePenalty : 0f;
                (BushId(x, y) >= 0 ? bushCells : floorCells).Add(new Vector2Int(x, y));
            }
    }

    // 바닥과 부시만 걸을 수 있다. 빈 칸은 바닥이 없어 떨어진다.
    static bool[,] Walkable(MapLayout layout)
    {
        var cells = new bool[layout.Width, layout.Height];
        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
                cells[x, y] = layout[x, y] == MapCell.Floor || layout[x, y] == MapCell.Bush;
        return cells;
    }

    static int[,] BushIds(MapLayout layout)
    {
        var ids = new int[layout.Width, layout.Height];
        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
                ids[x, y] = layout.BushId(x, y);
        return ids;
    }

    int BushId(int x, int y) => bushIds != null ? bushIds[x, y] : -1;

    public bool IsWalkable(Vector2Int cell) => IsWalkable(cell.x, cell.y);

    public bool IsWalkable(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && walkable[x, y];

    // 월드 좌표가 놓인 칸. 맵 밖이어도 그대로 계산한 좌표를 돌려준다.
    public Vector2Int CellAt(Vector3 world)
    {
        Vector3 local = root != null ? root.InverseTransformPoint(world) : world;
        return new Vector2Int(
            Mathf.RoundToInt(local.x / CellSize + (Width - 1) * 0.5f),
            Mathf.RoundToInt((Height - 1) * 0.5f - local.z / CellSize));
    }

    // 칸 중심의 월드 좌표(바닥 높이).
    public Vector3 CenterOf(Vector2Int cell)
    {
        var local = new Vector3((cell.x - (Width - 1) * 0.5f) * CellSize, 0f, ((Height - 1) * 0.5f - cell.y) * CellSize);
        return root != null ? root.TransformPoint(local) : local;
    }

    // 월드 좌표가 놓인 부시 덩어리 번호. 부시 밖이거나 맵 밖이면 -1이다. 번호는 BushField와 같다.
    public int BushAt(Vector3 world)
    {
        Vector2Int cell = CellAt(world);
        return cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height ? BushId(cell.x, cell.y) : -1;
    }

    // 숨은 상대를 찾아 가 볼 곳. from이 서 있는 덩어리가 아닌 부시 칸을 고르고, 그런 부시가 없으면 아무 바닥 칸이나 고른다.
    public Vector3 RandomSearchPoint(Vector3 from)
    {
        int current = BushAt(from);
        for (int attempt = 0; attempt < 8 && bushCells.Count > 0; attempt++)
        {
            Vector2Int cell = bushCells[Random.Range(0, bushCells.Count)];
            if (BushId(cell.x, cell.y) != current) return CenterOf(cell);
        }
        return floorCells.Count > 0 ? CenterOf(floorCells[Random.Range(0, floorCells.Count)]) : from;
    }

    // cell에서 가장 가까운 걸을 수 있는 칸. maxDistance칸 안에 없으면 false다.
    public bool TryNearestWalkable(Vector2Int cell, int maxDistance, out Vector2Int nearest)
    {
        nearest = cell;
        float best = float.PositiveInfinity;
        for (int dx = -maxDistance; dx <= maxDistance; dx++)
            for (int dy = -maxDistance; dy <= maxDistance; dy++)
            {
                if (!IsWalkable(cell.x + dx, cell.y + dy)) continue;
                float distance = dx * dx + dy * dy;
                if (distance >= best) continue;
                best = distance;
                nearest = new Vector2Int(cell.x + dx, cell.y + dy);
            }
        return best < float.PositiveInfinity;
    }

    // from에서 to까지 걸어갈 칸을 path에 순서대로 담는다(from 제외, to 포함). 길이 없으면 path를 비우고 false다.
    // 여덟 방향으로 걷되, 벽 모서리를 대각선으로 가로지르지 않는다.
    public bool FindPath(Vector2Int from, Vector2Int to, List<Vector2Int> path)
    {
        path.Clear();
        if (!IsWalkable(from) || !IsWalkable(to)) return false;
        if (from == to) return true;

        search++;
        heap.Clear();
        heapKeys.Clear();
        int start = from.y * Width + from.x;
        int goal = to.y * Width + to.x;
        cost[start] = 0f;
        parent[start] = -1;
        visit[start] = search;
        Push(start, Estimate(from, to));
        while (heap.Count > 0)
        {
            int current = Pop(out float key);
            int cx = current % Width, cy = current / Width;
            if (current == goal) break;
            // 더 싼 길로 다시 넣은 칸의 옛 항목은 건너뛴다.
            if (key > cost[current] + Estimate(new Vector2Int(cx, cy), to) + 0.0001f) continue;
            for (int s = 0; s < Steps.Length; s++)
            {
                int nx = cx + Steps[s].x, ny = cy + Steps[s].y;
                if (!IsWalkable(nx, ny)) continue;
                bool diagonal = s >= 4;
                if (diagonal && (!IsWalkable(cx + Steps[s].x, cy) || !IsWalkable(cx, cy + Steps[s].y))) continue;
                int next = ny * Width + nx;
                float nextCost = cost[current] + (diagonal ? Diagonal : 1f) + penalty[next];
                if (visit[next] == search && nextCost >= cost[next]) continue;
                visit[next] = search;
                cost[next] = nextCost;
                parent[next] = current;
                Push(next, nextCost + Estimate(new Vector2Int(nx, ny), to));
            }
        }
        if (visit[goal] != search) return false;
        for (int cell = goal; cell != start; cell = parent[cell])
            path.Add(new Vector2Int(cell % Width, cell / Width));
        path.Reverse();
        return true;
    }

    // 두 월드 좌표 사이를 반지름 radius인 원이 벽이나 맵 밖에 닿지 않고 곧게 지나갈 수 있는지.
    // 길을 따라갈 때 건너뛸 수 있는 칸과, 쏜 공격이 벽에 막히는지를 가늠할 때 쓴다.
    public bool HasClearLine(Vector3 from, Vector3 to, float radius)
    {
        Vector3 a = root != null ? root.InverseTransformPoint(from) : from;
        Vector3 b = root != null ? root.InverseTransformPoint(to) : to;
        a.y = b.y = 0f;
        float length = Vector3.Distance(a, b);
        int samples = Mathf.Max(1, Mathf.CeilToInt(length / (CellSize * 0.2f)));
        for (int i = 0; i <= samples; i++)
        {
            Vector3 point = Vector3.Lerp(a, b, (float)i / samples);
            if (!LocalWalkable(point.x - radius, point.z - radius) || !LocalWalkable(point.x + radius, point.z - radius) ||
                !LocalWalkable(point.x - radius, point.z + radius) || !LocalWalkable(point.x + radius, point.z + radius))
                return false;
        }
        return true;
    }

    bool LocalWalkable(float x, float z) => IsWalkable(
        Mathf.RoundToInt(x / CellSize + (Width - 1) * 0.5f), Mathf.RoundToInt((Height - 1) * 0.5f - z / CellSize));

    // 여덟 방향 이동에서 남은 거리의 하한.
    static float Estimate(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(from.x - to.x), dy = Mathf.Abs(from.y - to.y);
        return Mathf.Max(dx, dy) + (Diagonal - 1f) * Mathf.Min(dx, dy);
    }

    // 키가 가장 작은 칸을 먼저 꺼내는 이진 힙.
    void Push(int cell, float key)
    {
        heap.Add(cell);
        heapKeys.Add(key);
        int i = heap.Count - 1;
        while (i > 0)
        {
            int up = (i - 1) / 2;
            if (heapKeys[up] <= heapKeys[i]) break;
            Swap(i, up);
            i = up;
        }
    }

    int Pop(out float key)
    {
        int top = heap[0];
        key = heapKeys[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heapKeys[0] = heapKeys[last];
        heap.RemoveAt(last);
        heapKeys.RemoveAt(last);
        for (int i = 0; ;)
        {
            int left = i * 2 + 1, right = left + 1, smallest = i;
            if (left < heap.Count && heapKeys[left] < heapKeys[smallest]) smallest = left;
            if (right < heap.Count && heapKeys[right] < heapKeys[smallest]) smallest = right;
            if (smallest == i) break;
            Swap(i, smallest);
            i = smallest;
        }
        return top;
    }

    void Swap(int a, int b)
    {
        (heap[a], heap[b]) = (heap[b], heap[a]);
        (heapKeys[a], heapKeys[b]) = (heapKeys[b], heapKeys[a]);
    }
}
