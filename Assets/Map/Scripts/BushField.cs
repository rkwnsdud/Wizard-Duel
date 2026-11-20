using System;
using System.Collections.Generic;
using UnityEngine;

// 부시 칸 둘레 여덟 칸 중 부시인 칸. 북쪽은 맵의 +Z(레이아웃 윗줄)다. 덤불 모양(ReedPatch)이 덩어리 가장자리를 알 때 쓴다.
[Flags]
public enum BushNeighbors
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
    NorthEast = 1 << 4,
    SouthEast = 1 << 5,
    SouthWest = 1 << 6,
    NorthWest = 1 << 7,
    All = North | East | South | West | NorthEast | SouthEast | SouthWest | NorthWest
}

// 깔린 맵의 부시 칸과 덩어리 번호. 가시성은 물리 판정 대신 이 칸 좌표로 계산한다.
// 번호는 레이아웃 문자만으로 정해지므로(BushRegions) 같은 맵을 만든 모든 클라이언트가 같은 번호를 쓴다.
// 지금 싸우는 맵 하나만 Active로 등록한다. 로비 화면 미리보기 맵은 등록하지 않는다.
public sealed class BushField
{
    public static BushField Active { get; private set; }

    readonly string[] layout;
    readonly Transform root;
    readonly float cellSize;
    int[,] ids;
    List<BushBlock>[] blocks;
    readonly Dictionary<Vector2Int, BushBlock> bushByCell = new Dictionary<Vector2Int, BushBlock>();
    // 덤불 모양 시드에 섞는 레이아웃 해시. 같은 맵이면 모든 클라이언트가 같은 갈대를 본다.
    readonly int layoutHash;

    // layout은 맵이 블록을 깐 문자 레이아웃이고, root는 블록을 깐 부모다.
    // 칸 (x, z)의 중심은 root 기준 ((x - (행 길이 - 1) / 2) * cellSize, 0, ((행 수 - 1) / 2 - z) * cellSize)이다.
    public BushField(string[] layout, Transform root, float cellSize)
    {
        this.layout = (string[])layout.Clone();
        this.root = root;
        this.cellSize = cellSize;
        RebuildRegions();
        unchecked
        {
            int hash = (int)2166136261;
            foreach (string row in layout)
                foreach (char cell in row) hash = (hash ^ cell) * 16777619;
            layoutHash = hash;
        }
    }

    // 덩어리 수. 번호는 0부터 Count - 1까지다.
    public int Count { get; private set; }

    public static void Activate(BushField field) => Active = field;

    // 맵이 지워지거나 다시 만들어질 때 부른다. 다른 맵이 이미 등록돼 있으면 그대로 둔다.
    public static void Deactivate(BushField field)
    {
        if (field != null && Active == field) Active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => Active = null;

    // 맵이 칸 (x, z)에 깐 블록을 넘긴다. 부시 블록만 그 칸의 덩어리에 모으고, 이웃 부시 칸에 맞춰 덤불 모양을 정한다.
    public void Add(int x, int z, IBlock block)
    {
        if (!(block is BushBlock bush)) return;
        bushByCell[new Vector2Int(x, z)] = bush;
        int id = IdAt(x, z);
        if (id >= 0) blocks[id].Add(bush);
        bush.FitTo(SeedAt(x, z), NeighborsAt(x, z), cellSize);
    }

    public bool RemoveCell(int x, int z)
    {
        if (IdAt(x, z) < 0) return false;
        char[] row = layout[z].ToCharArray();
        row[x] = '.';
        layout[z] = new string(row);
        RebuildRegions();
        return true;
    }

    void RebuildRegions()
    {
        ids = BushRegions.Label(layout, out int count);
        Count = count;
        blocks = new List<BushBlock>[count];
        for (int i = 0; i < count; i++) blocks[i] = new List<BushBlock>();
        foreach (KeyValuePair<Vector2Int, BushBlock> entry in bushByCell)
        {
            int id = IdAt(entry.Key.x, entry.Key.y);
            if (id < 0 || entry.Value == null) continue;
            blocks[id].Add(entry.Value);
            entry.Value.FitTo(SeedAt(entry.Key.x, entry.Key.y), NeighborsAt(entry.Key.x, entry.Key.y), cellSize);
        }
    }

    // 칸 둘레 여덟 칸 중 부시인 칸. 같은 덩어리는 8방향으로 이어지므로 모두 이 칸과 같은 덩어리다.
    public BushNeighbors NeighborsAt(int x, int z)
    {
        BushNeighbors neighbors = BushNeighbors.None;
        if (IdAt(x, z - 1) >= 0) neighbors |= BushNeighbors.North;
        if (IdAt(x + 1, z) >= 0) neighbors |= BushNeighbors.East;
        if (IdAt(x, z + 1) >= 0) neighbors |= BushNeighbors.South;
        if (IdAt(x - 1, z) >= 0) neighbors |= BushNeighbors.West;
        if (IdAt(x + 1, z - 1) >= 0) neighbors |= BushNeighbors.NorthEast;
        if (IdAt(x + 1, z + 1) >= 0) neighbors |= BushNeighbors.SouthEast;
        if (IdAt(x - 1, z + 1) >= 0) neighbors |= BushNeighbors.SouthWest;
        if (IdAt(x - 1, z - 1) >= 0) neighbors |= BushNeighbors.NorthWest;
        return neighbors;
    }

    // 칸마다 다른 덤불 모양 시드. 레이아웃과 칸 좌표만으로 정해진다.
    public int SeedAt(int x, int z)
    {
        unchecked { return ((layoutHash * 31 + x) * 31 + z) * -1640531535; }
    }

    // 칸의 덩어리 번호. 부시가 아니거나 맵 밖이면 -1이다.
    public int IdAt(int x, int z)
    {
        if (z < 0 || z >= layout.Length || x < 0 || x >= layout[z].Length) return -1;
        return ids[x, z];
    }

    // 월드 좌표가 놓인 칸의 덩어리 번호. 플레이어 발 위치를 넘긴다. 부시 밖이거나 맵이 사라졌으면 -1이다.
    public int BushAt(Vector3 world)
    {
        if (root == null || layout.Length == 0) return -1;
        Vector3 local = root.InverseTransformPoint(world);
        int z = Mathf.RoundToInt((layout.Length - 1) * 0.5f - local.z / cellSize);
        if (z < 0 || z >= layout.Length) return -1;
        int x = Mathf.RoundToInt(local.x / cellSize + (layout[z].Length - 1) * 0.5f);
        return IdAt(x, z);
    }

    public IReadOnlyList<BushBlock> BlocksOf(int id) => blocks[id];

    // 덩어리 무게중심에 가장 가까운 부시 칸의 월드 좌표(바닥 높이). 오프라인 테스트가 더미를 부시 안에 세울 때 쓴다.
    public Vector3 CenterOf(int id)
    {
        Vector2 sum = Vector2.zero;
        int cells = 0;
        for (int z = 0; z < layout.Length; z++)
            for (int x = 0; x < layout[z].Length; x++)
                if (ids[x, z] == id)
                {
                    sum += CellLocal(x, z);
                    cells++;
                }
        if (cells == 0) throw new System.ArgumentOutOfRangeException(nameof(id), id, "No bush cluster has this id.");
        Vector2 centroid = sum / cells;
        Vector2 best = Vector2.zero;
        float bestDistance = float.PositiveInfinity;
        for (int z = 0; z < layout.Length; z++)
            for (int x = 0; x < layout[z].Length; x++)
            {
                if (ids[x, z] != id) continue;
                Vector2 cell = CellLocal(x, z);
                float distance = (cell - centroid).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = cell;
            }
        return root.TransformPoint(new Vector3(best.x, 0f, best.y));
    }

    // 칸 중심의 root 기준 (x, z) 좌표.
    Vector2 CellLocal(int x, int z) =>
        new Vector2((x - (layout[z].Length - 1) * 0.5f) * cellSize, ((layout.Length - 1) * 0.5f - z) * cellSize);

    public void SetFaded(int id, bool faded)
    {
        if (id < 0 || id >= blocks.Length) return;
        foreach (BushBlock block in blocks[id])
            if (block != null) block.SetFaded(faded);
    }
}
