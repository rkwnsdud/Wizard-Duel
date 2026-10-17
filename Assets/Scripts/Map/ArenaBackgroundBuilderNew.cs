using System.Collections.Generic;
using UnityEngine;

// 전장 바깥을 빙 둘러싸는 던전 배경. 벽은 쓰지 않는다.
[DisallowMultipleComponent]
public class ArenaBackgroundBuilderNew : MonoBehaviour
{
    [SerializeField] ProceduralMap map;
    [SerializeField] Transform backgroundRoot;
    [SerializeField] GameObject farGroundPrefab;
    [SerializeField] GameObject[] floorFillVariants;
    [SerializeField] float apronY = -0.02f;
    [SerializeField] GameObject[] pillarVariants;
    [SerializeField] GameObject[] torchVariants;
    [SerializeField] GameObject[] extraDecorVariants;
    [SerializeField] float cellSize = 2f;
    [SerializeField] int rimOffsetCells = 2;
    [SerializeField] int decoOffsetCells = 4;
    [SerializeField] float farGroundY = -0.15f;
    [SerializeField] float farGroundSize = 240f;

    void Start()
    {
        if (map == null) map = FindFirstObjectByType<ProceduralMap>();
        if (backgroundRoot == null) backgroundRoot = transform;
        Rebuild();
    }

    public void Rebuild()
    {
        if (map == null) map = FindFirstObjectByType<ProceduralMap>();
        if (backgroundRoot == null) backgroundRoot = transform;
        for (int i = backgroundRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = backgroundRoot.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        EnsureFarGround();
        if (map == null || map.GeneratedLayout == null) return;
        MapLayout layout = map.GeneratedLayout;
        int seed = 12345 ^ (layout.Width * 390451) ^ (layout.Height * 606347);
        seed = seed ^ (layout.SpawnA.x * 73856093) ^ (layout.SpawnB.y * 297121507);
        var rand = new System.Random(seed);
        bool[,] outerVoid = ComputeOuterVoid(layout);
        List<Vector2Int> rimCells = CollectRimCells(layout, rimOffsetCells, outerVoid);
        List<Vector2Int> decoCells = CollectRimCells(layout, decoOffsetCells, outerVoid);
        bool hasFloor = floorFillVariants != null && floorFillVariants.Length > 0;
        foreach (Vector2Int c in rimCells)
        {
            Vector3 pos = CellToWorld(c, layout);
            pos.y = apronY;
            float yaw = rand.Next(4) * 90f;
            GameObject go = null;
            if (hasFloor)
            {
                GameObject prefab = floorFillVariants[rand.Next(floorFillVariants.Length)];
                if (prefab == null) continue;
                Quaternion rot = prefab.transform.rotation * Quaternion.Euler(0f, yaw, 0f);
                go = Instantiate(prefab, pos, rot, backgroundRoot);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Plane);
                go.transform.SetParent(backgroundRoot, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                float s = cellSize / 10f;
                go.transform.localScale = new Vector3(s, 1f, s);
                var r2 = go.GetComponent<Renderer>();
                if (r2 != null) r2.material.color = new Color(0.19f, 0.18f, 0.20f);
            }
            go.name = "Apron_" + c.x + "_" + c.y;
            StripColliders(go);
        }
        for (int i = 0; i < decoCells.Count; i += 2)
        {
            Vector2Int a = decoCells[i];
            PlaceDeco(a, layout, rand);
            Vector2Int m = new Vector2Int(layout.Width - 1 - a.x, layout.Height - 1 - a.y);
            if (m.x != a.x || m.y != a.y) PlaceDeco(m, layout, rand);
        }
    }

    void PlaceDeco(Vector2Int c, MapLayout layout, System.Random rand)
    {
        if (layout[c.x, c.y] != MapCell.Void) return;
        double r = rand.NextDouble();
        GameObject prefab = null;
        if (r < 0.40 && pillarVariants != null && pillarVariants.Length > 0)
            prefab = pillarVariants[rand.Next(pillarVariants.Length)];
        else if (r < 0.52 && torchVariants != null && torchVariants.Length > 0)
            prefab = torchVariants[rand.Next(torchVariants.Length)];
        else if (extraDecorVariants != null && extraDecorVariants.Length > 0)
            prefab = extraDecorVariants[rand.Next(extraDecorVariants.Length)];
        if (prefab == null) return;
        Vector3 pos = CellToWorld(c, layout);
        float yaw = rand.Next(4) * 90f;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;
        var go = Instantiate(prefab, pos, rot, backgroundRoot);
        go.name = "Deco_" + c.x + "_" + c.y;
        StripColliders(go);
        if (go.transform.up.y < 0.6f) go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        GroundSnap(go);
    }
    List<Vector2Int> CollectRimCells(MapLayout layout, int offset, bool[,] outerVoid)
    {
        var list = new List<Vector2Int>();
        for (int y = 0; y < layout.Height; y++)
            for (int x = 0; x < layout.Width; x++)
            {
                if (layout[x, y] != MapCell.Void) continue;
                if (outerVoid != null && !outerVoid[x, y]) continue;
                if (IsNearPlayable(layout, x, y, offset)) list.Add(new Vector2Int(x, y));
            }
        return list;
    }
    bool[,] ComputeOuterVoid(MapLayout layout)
    {
        bool[,] outer = new bool[layout.Width, layout.Height];
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < layout.Width; x++)
        {
            TryEnqueueOuter(layout, outer, queue, new Vector2Int(x, 0));
            TryEnqueueOuter(layout, outer, queue, new Vector2Int(x, layout.Height - 1));
        }
        for (int y = 0; y < layout.Height; y++)
        {
            TryEnqueueOuter(layout, outer, queue, new Vector2Int(0, y));
            TryEnqueueOuter(layout, outer, queue, new Vector2Int(layout.Width - 1, y));
        }
        Vector2Int[] dirs = { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            foreach (Vector2Int d in dirs)
            {
                Vector2Int n = cur + d;
                if (n.x < 0 || n.y < 0 || n.x >= layout.Width || n.y >= layout.Height) continue;
                TryEnqueueOuter(layout, outer, queue, n);
            }
        }
        return outer;
    }
    static void TryEnqueueOuter(MapLayout layout, bool[,] outer, Queue<Vector2Int> queue, Vector2Int c)
    {
        if (c.x < 0 || c.y < 0 || c.x >= layout.Width || c.y >= layout.Height) return;
        if (outer[c.x, c.y]) return;
        if (layout[c.x, c.y] != MapCell.Void) return;
        outer[c.x, c.y] = true;
        queue.Enqueue(c);
    }
    bool IsNearPlayable(MapLayout layout, int x, int y, int offset)
    {
        for (int dy = -offset; dy <= offset; dy++)
            for (int dx = -offset; dx <= offset; dx++)
            {
                int nx = x + dx; int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= layout.Width || ny >= layout.Height) continue;
                if (layout[nx, ny] != MapCell.Void) return true;
            }
        return false;
    }
    Vector3 CellToWorld(Vector2Int c, MapLayout layout)
    {
        float wx = (c.x - (layout.Width - 1) * 0.5f) * cellSize;
        float wz = ((layout.Height - 1) * 0.5f - c.y) * cellSize;
        return new Vector3(wx, 0f, wz);
    }
    void GroundSnap(GameObject go)
    {
        if (go == null) return;
        Renderer rend = go.GetComponentInChildren<Renderer>();
        if (rend == null) return;
        float minY = rend.bounds.min.y;
        Vector3 p = go.transform.position;
        p.y += (0f - minY);
        go.transform.position = p;
    }
    void EnsureFarGround()
    {
        if (farGroundPrefab != null)
        {
            var go = Instantiate(farGroundPrefab, new Vector3(0f, farGroundY, 0f), Quaternion.identity, backgroundRoot);
            go.name = "FarGround";
            StripColliders(go);
            return;
        }
        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.transform.SetParent(backgroundRoot, false);
        plane.transform.position = new Vector3(0f, farGroundY, 0f);
        float s = farGroundSize / 10f;
        plane.transform.localScale = new Vector3(s, 1f, s);
        plane.name = "FarGround (Auto)";
        var col = plane.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
        var rend = plane.GetComponent<Renderer>();
        if (rend != null) rend.material.color = new Color(0.12f, 0.11f, 0.13f);
    }
    static void StripColliders(GameObject go)
    {
        foreach (Collider col in go.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }
}