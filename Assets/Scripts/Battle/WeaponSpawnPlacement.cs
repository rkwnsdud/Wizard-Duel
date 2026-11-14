using System.Collections.Generic;
using UnityEngine;

// Uses the generated arena's walkable topology, including donut holes and irregular edges.
public static class WeaponSpawnPlacement
{
    static readonly Vector2Int[] Steps = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    public static List<Vector2Int> Select(MapLayout map, int limit = 12, float spacing = 4f)
    {
        var result = new List<Vector2Int>();
        if (map == null) return result;
        int[,] a = Distances(map, map.SpawnA), b = Distances(map, map.SpawnB);
        var candidates = new List<Vector2Int>();
        for (int y = 1; y < map.Height - 1; y++)
        for (int x = 1; x < map.Width - 1; x++)
        {
            var cell = new Vector2Int(x, y);
            if (map[x, y] != MapCell.Floor || a[x, y] < 0 || b[x, y] < 0 ||
                MapLayout.InSpawnZone(cell, map.SpawnA, 3) || MapLayout.InSpawnZone(cell, map.SpawnB, 3)) continue;
            bool clear = true;
            foreach (var step in Steps)
                if (!Walkable(map, x + step.x, y + step.y)) clear = false;
            if (clear) candidates.Add(cell);
        }
        // Spread sites over open corridors/courtyards; prefer equal walking distance
        // for the first site. Never fall back to walls, void, bushes, or spawn zones.
        while (candidates.Count > 0 && result.Count < limit)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2Int p = candidates[i];
                float distance = float.PositiveInfinity;
                foreach (var selected in result) distance = Mathf.Min(distance, Vector2Int.Distance(p, selected));
                if (distance < spacing) continue;
                float imbalance = Mathf.Abs(a[p.x, p.y] - b[p.x, p.y]);
                float score = result.Count == 0 ? -imbalance - .01f * (a[p.x, p.y] + b[p.x, p.y])
                    : distance - .08f * imbalance;
                if (score > bestScore) { best = i; bestScore = score; }
            }
            if (best < 0) break;
            result.Add(candidates[best]);
            candidates.RemoveAt(best);
        }
        return result;
    }

    public static Vector3 LocalPosition(MapLayout map, Vector2Int cell) =>
        new Vector3(cell.x - (map.Width - 1) * .5f, 0, (map.Height - 1) * .5f - cell.y);

    // The dedicated server removes the physical map between room preparations.
    // Check the saved layout so a pickup request cannot pass through a wall.
    public static bool ClearLine(MapLayout map, Matrix4x4 worldToMap, Vector3 from, Vector3 to)
    {
        if (map == null) return false;
        Vector3 start = worldToMap.MultiplyPoint3x4(from);
        Vector3 end = worldToMap.MultiplyPoint3x4(to);
        Vector3 delta = end - start;
        int steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(delta.x, delta.z).magnitude * 20f));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 sample = start + delta * (i / (float)steps);
            int x = Mathf.RoundToInt(sample.x + (map.Width - 1) * .5f);
            int y = Mathf.RoundToInt((map.Height - 1) * .5f - sample.z);
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height ||
                map[x, y] == MapCell.Void || map[x, y] == MapCell.Wall) return false;
        }
        return true;
    }

    static bool Walkable(MapLayout map, int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height &&
        (map[x, y] == MapCell.Floor || map[x, y] == MapCell.Bush);

    static int[,] Distances(MapLayout map, Vector2Int start)
    {
        var distances = new int[map.Width, map.Height];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++) distances[x, y] = -1;
        var queue = new Queue<Vector2Int>();
        distances[start.x, start.y] = 0;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            foreach (var step in Steps)
            {
                Vector2Int next = p + step;
                if (!Walkable(map, next.x, next.y) || distances[next.x, next.y] >= 0) continue;
                distances[next.x, next.y] = distances[p.x, p.y] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }
}
