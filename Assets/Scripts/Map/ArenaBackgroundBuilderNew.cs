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
    public enum BackgroundYawMode { Fixed, Random90, Random360 }
    [System.Serializable]
    public struct BackgroundRotationSettings
    {
        public BackgroundYawMode yawMode;
        public bool forceUpright;
        public Vector3 eulerOffset;
        public float yOffset;
    }
    [System.Serializable]
    public class BackgroundRotationOverride
    {
        public GameObject prefab;
        public BackgroundYawMode yawMode;
        public bool forceUpright;
        public Vector3 eulerOffset;
        public float yOffset;
    }
    [Header("Rotation / Grounding")]
    [SerializeField] BackgroundRotationSettings floorRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = false, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] BackgroundRotationSettings pillarRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = true, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] BackgroundRotationSettings torchRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = true, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] BackgroundRotationSettings extraRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = true, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] float decoGroundY = 0f;
    [Tooltip("특정 프리팹만 따로 회전시키고 싶을 때 여기에 추가한다. 같은 프리팹 항목이 있으면 카테고리 설정보다 우선한다.")]
    [SerializeField] List<BackgroundRotationOverride> rotationOverrides;

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
        var rimSet = new HashSet<Vector2Int>(rimCells);
        var handledRim = new HashSet<Vector2Int>();
        foreach (Vector2Int c in rimCells)
        {
            if (!handledRim.Add(c)) continue;
            Vector2Int m = MirrorCell(c, layout);
            handledRim.Add(m);
            // 180도 대칭 맵에서는 미러도 rim이지만, 안전하게 포함 여부로 분기한다.
            bool mirrored = rimSet.Contains(m);
            PlaceMirroredApron(c, m, layout, rand, hasFloor, mirrored);
        }
        var decoSet = new HashSet<Vector2Int>(decoCells);
        var handledDeco = new HashSet<Vector2Int>();
        foreach (Vector2Int a in decoCells)
        {
            if (!handledDeco.Add(a)) continue;
            Vector2Int m = MirrorCell(a, layout);
            handledDeco.Add(m);
            if (decoSet.Contains(m)) PlaceMirroredDeco(a, m, layout, rand);
            else PlaceMirroredDeco(a, a, layout, rand);
        }
    }

    static Vector2Int MirrorCell(Vector2Int c, MapLayout layout)
    {
        return new Vector2Int(layout.Width - 1 - c.x, layout.Height - 1 - c.y);
    }
    void PlaceMirroredApron(Vector2Int a, Vector2Int m, MapLayout layout, System.Random rand, bool hasFloor, bool mirrored)
    {
        // 미러쌍당 1회만 굴려서 양쪽에 같은 프리팹을 180도 회전 대칭으로 놓는다.
        GameObject prefab = null;
        if (hasFloor)
        {
            if (floorFillVariants == null || floorFillVariants.Length == 0) return;
            prefab = floorFillVariants[rand.Next(floorFillVariants.Length)];
            if (prefab == null) return;
        }
        BackgroundRotationSettings settings = ApplyOverride(prefab, floorRotation);
        float yaw = RollYaw(rand, settings.yawMode);
        float yawM = (yaw + 180f) % 360f;
        PlaceApronAt(a, layout, yaw, prefab, hasFloor, settings);
        if (mirrored && m != a) PlaceApronAt(m, layout, yawM, prefab, hasFloor, settings);
    }
    void PlaceApronAt(Vector2Int c, MapLayout layout, float yaw, GameObject prefab, bool hasFloor, BackgroundRotationSettings settings)
    {
        Vector3 pos = CellToWorld(c, layout);
        pos.y = apronY + settings.yOffset;
        GameObject go = null;
        if (hasFloor)
        {
            if (prefab == null) return;
            Quaternion rot = ResolveRotation(prefab.transform.rotation, yaw, settings);
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
    void PlaceMirroredDeco(Vector2Int a, Vector2Int m, MapLayout layout, System.Random rand)
    {
        // 미러쌍당 장식 유무/종류/방향을 1회만 굴린다. 양쪽이 다르면 공정성이 깨진다.
        bool aValid = layout[a.x, a.y] == MapCell.Void;
        bool mValid = m.x == a.x && m.y == a.y ? aValid : layout[m.x, m.y] == MapCell.Void;
        if (!aValid && !mValid) return;
        double r = rand.NextDouble();
        GameObject prefab = null;
        BackgroundRotationSettings settings = pillarRotation;
        if (r < 0.40 && pillarVariants != null && pillarVariants.Length > 0)
        {
            prefab = pillarVariants[rand.Next(pillarVariants.Length)];
            settings = pillarRotation;
        }
        else if (r < 0.52 && torchVariants != null && torchVariants.Length > 0)
        {
            prefab = torchVariants[rand.Next(torchVariants.Length)];
            settings = torchRotation;
        }
        else if (extraDecorVariants != null && extraDecorVariants.Length > 0)
        {
            prefab = extraDecorVariants[rand.Next(extraDecorVariants.Length)];
            settings = extraRotation;
        }
        if (prefab == null) return;
        settings = ApplyOverride(prefab, settings);
        float yaw = RollYaw(rand, settings.yawMode);
        float yawM = (yaw + 180f) % 360f;
        if (aValid) SpawnDecoAt(a, layout, prefab, yaw, settings);
        if (mValid && (m.x != a.x || m.y != a.y)) SpawnDecoAt(m, layout, prefab, yawM, settings);
    }
    void SpawnDecoAt(Vector2Int c, MapLayout layout, GameObject prefab, float yaw, BackgroundRotationSettings settings)
    {
        Vector3 pos = CellToWorld(c, layout);
        Quaternion rot = ResolveRotation(prefab.transform.rotation, yaw, settings);
        var go = Instantiate(prefab, pos, rot, backgroundRoot);
        go.name = "Deco_" + c.x + "_" + c.y;
        StripColliders(go);
        if (settings.forceUpright && go.transform.up.y < 0.6f)
        {
            // 누워있는 프리팹은 세운 뒤 인스펙터 오프셋을 다시 적용한다.
            go.transform.rotation = ResolveRotation(Quaternion.identity, yaw, settings);
        }
        GroundSnap(go, decoGroundY + settings.yOffset);
    }
    static float RollYaw(System.Random rand, BackgroundYawMode mode)
    {
        switch (mode)
        {
            case BackgroundYawMode.Random360: return (float)(rand.NextDouble() * 360.0);
            case BackgroundYawMode.Random90: return rand.Next(4) * 90f;
            default: return 0f;
        }
    }
    static Quaternion ResolveRotation(Quaternion prefabRotation, float yaw, BackgroundRotationSettings settings)
    {
        // 기본: Yaw * 프리팹원본 * 오프셋. forceUpright면 프리팹원본(눕힘)을 무시하고 세운다.
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        if (!settings.forceUpright) q *= prefabRotation;
        if (settings.eulerOffset != Vector3.zero) q *= Quaternion.Euler(settings.eulerOffset);
        return q;
    }
    BackgroundRotationSettings ApplyOverride(GameObject prefab, BackgroundRotationSettings settings)
    {
        if (prefab == null || rotationOverrides == null) return settings;
        for (int i = 0; i < rotationOverrides.Count; i++)
        {
            BackgroundRotationOverride o = rotationOverrides[i];
            if (o == null || o.prefab != prefab) continue;
            settings.yawMode = o.yawMode;
            settings.forceUpright = o.forceUpright;
            settings.eulerOffset = o.eulerOffset;
            settings.yOffset = o.yOffset;
            return settings;
        }
        return settings;
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
    void GroundSnap(GameObject go, float groundY)
    {
        if (go == null) return;
        Renderer rend = go.GetComponentInChildren<Renderer>();
        if (rend == null) return;
        float minY = rend.bounds.min.y;
        Vector3 p = go.transform.position;
        // 회전 적용 후 bounds 기준으로 바닥(groundY)에 정확히 맞춘다. 눕거나 기울어도 파묻히지 않는다.
        p.y += (groundY - minY);
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