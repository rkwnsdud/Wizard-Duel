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
    [Tooltip("맵 한 칸의 월드 크기. ProceduralMap/GridMap의 cellSize(1m)와 같아야 배경이 맵 가장자리에 붙는다.")]
    [SerializeField] float cellSize = 1f;
    [Tooltip("맵 가장자리에서 몇 칸까지 바닥을 채울지. 바닥 타일 기준으로 세며 4~5칸을 권장한다.")]
    [SerializeField] int rimOffsetCells = 5;
    [Tooltip("맵 가장자리에서 몇 칸까지 장식을 둘지. 바닥보다 넓게 6~7칸을 권장한다.")]
    [SerializeField] int decoOffsetCells = 7;
    [SerializeField] float farGroundY = -0.15f;
    [SerializeField] float farGroundSize = 240f;
    // CreatePrimitive 평면의 빌트인 머티리얼은 URP 빌드에서 마젠타로 보일 수 있어, URP Lit으로 직접 만든다.
    static Material sharedGroundMaterial;
    static bool groundShaderMissing;
    // 이번 Rebuild로 깐 바닥 타일과 장식. 맵이 블록과 함께 내놓아(ProceduralMap.Pieces) 물결로 같이 움직인다.
    // FarGround는 조각들이 가라앉아 사라지는 바닥이라 넣지 않는다.
    // 렌더러를 합쳐 만든 배경(전투 맵)에서는 합쳐진 조각은 오브젝트가 없어 여기에 들어가지 않는다.
    readonly List<Transform> pieces = new List<Transform>();
    // 렌더러를 합쳐 만든 배경에서만 쓴다. 합친 오브젝트와 런타임 메시를 들고 있다가 다시 만들 때 지운다.
    MapRendererCombiner combiner;
    bool combining;
    // 마지막으로 배경을 만든 레이아웃. Start에서 같은 맵의 배경을 다시 만들지 않으려고 기억한다.
    MapLayout builtLayout;
    bool built;
    public IReadOnlyList<Transform> Pieces => pieces;
    public enum BackgroundYawMode { Fixed, Random90, Random360 }
    [System.Serializable]
    public struct BackgroundRotationSettings
    {
        public BackgroundYawMode yawMode;
        [Tooltip("구버전 호환용. 현재는 무시된다. 원본 보정(수동 배치 자세)을 항상 유지한다.")]
        public bool forceUpright;
        [Tooltip("Yaw*원본 뒤에 곱해지는 미세조정. X에 -90/90을 넣어 눕힘/세움을 조정한다.")]
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
    [SerializeField] BackgroundRotationSettings floorRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = true, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] BackgroundRotationSettings pillarRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = true, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] BackgroundRotationSettings torchRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = false, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] BackgroundRotationSettings extraRotation = new BackgroundRotationSettings { yawMode = BackgroundYawMode.Random90, forceUpright = true, eulerOffset = new Vector3(0f, 0f, 0f), yOffset = 0f };
    [SerializeField] float decoGroundY = 0f;
    [Tooltip("특정 프리팹만 따로 회전시키고 싶을 때 여기에 추가한다. 같은 프리팹 항목이 있으면 카테고리 설정보다 우선한다.")]
    [SerializeField] List<BackgroundRotationOverride> rotationOverrides;

    void Start()
    {
        // 씬에 맵이 하나뿐인 구형 NetworkDemo 호환: 부모에서 먼저 찾고, 그래도 없으면 하나일 때만 자동 연결한다.
        // Main처럼 맵이 둘(타이틀/전투)이면 자동 연결하지 않는다. 인스펙터에서 map을 직접 지정해야 한다.
        if (map == null) map = GetComponentInParent<ProceduralMap>();
        if (map == null) map = FindSingleMap();
        if (backgroundRoot == null) backgroundRoot = transform;
        // 맵이 먼저 생성되며 이미 이 배경을 깔았으면 다시 만들지 않는다. 다시 만들면
        // TitleBackdrop이 물결로 붙잡은 조각이 사라지고, 새 조각이 맵보다 먼저 제자리에 선다.
        if (built && builtLayout == (map != null ? map.GeneratedLayout : null)) return;
        Rebuild();
    }
#if UNITY_EDITOR
    // 인스펙터에서 회전값 바꿀 때마다 Play 재시작 없이 확인용. 실배치는 Start/Generate에서 Rebuild.
    [ContextMenu("Rebuild Background (Editor)")]
    void RebuildEditor()
    {
        Rebuild();
    }
#endif

    public void Rebuild() => Rebuild(false);

    // combineRenderers를 켜면 바닥 타일과 정적 장식을 오브젝트로 만들지 않고 청크·머티리얼별 메시로 합친다(MapRendererCombiner).
    // 자리와 종류는 칸마다 따로 굴리므로 합쳐도, 깔 범위가 달라도 같은 칸은 같다. 조각을 움직이는 미리보기 맵에서는 켜지 않는다.
    public void Rebuild(bool combineRenderers)
    {
        if (map == null) map = GetComponentInParent<ProceduralMap>();
        if (map == null) map = FindSingleMap();
        if (backgroundRoot == null) backgroundRoot = transform;
        combiner?.Clear();
        combining = combineRenderers;
        if (combining && combiner == null) combiner = new MapRendererCombiner();
        for (int i = backgroundRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = backgroundRoot.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        pieces.Clear();
        built = true;
        builtLayout = map != null ? map.GeneratedLayout : null;
        EnsureFarGround();
        if (map == null || map.GeneratedLayout == null) return;
        MapLayout layout = map.GeneratedLayout;
        int seed = 12345 ^ (layout.Width * 390451) ^ (layout.Height * 606347);
        seed = seed ^ (layout.SpawnA.x * 73856093) ^ (layout.SpawnB.y * 297121507);
        // 플레이 영역 근처 빈칸은 바깥/안쪽(도넛 구멍 등) 구분 없이 전부 채운다.
        // 예전에는 맵 바깥에서 닿는 칸만 골라서 일부 모양에서 한쪽이 비어 보였다.
        // 바닥(rim)은 360도 전체를 빠짐없이 두르고, 장식(deco)은 대칭으로만 놓는다.
        List<Vector2Int> rimCells = CollectRimCells(layout, rimOffsetCells);
        List<Vector2Int> decoCells = CollectRimCells(layout, decoOffsetCells);
        bool hasFloor = floorFillVariants != null && floorFillVariants.Length > 0;
        foreach (Vector2Int c in rimCells)
        {
            var rng = new CellRandom(seed, c, FloorChannel);
            GameObject prefab = null;
            if (hasFloor)
            {
                prefab = floorFillVariants[rng.Next(floorFillVariants.Length)];
                if (prefab == null) continue;
            }
            BackgroundRotationSettings placement = ApplyOverride(prefab, floorRotation);
            float yaw = RollYaw(ref rng, placement.yawMode);
            PlaceApronAt(c, layout, yaw, prefab, hasFloor, placement);
        }
        foreach (Vector2Int c in decoCells) PlaceDeco(c, layout, seed);
        if (combining) combiner.Build(backgroundRoot);
    }

    void OnDestroy()
    {
        // 런타임에 만든 메시는 오브젝트가 사라져도 남으므로 직접 지운다.
        combiner?.Clear();
    }

    // Instantiate(prefab, position, rotation, backgroundRoot)로 만든 오브젝트의 backgroundRoot 기준 행렬. 스케일은 프리팹 그대로다.
    Matrix4x4 LocalMatrix(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        return Matrix4x4.TRS(backgroundRoot.InverseTransformPoint(position),
            Quaternion.Inverse(backgroundRoot.rotation) * rotation, prefab.transform.localScale);
    }

    // 칸마다 따로 만드는 난수. seed·칸 좌표·용도로만 정해져서 깔 범위가 달라도 같은 칸은 같은 값을 낸다.
    // 로비 화면 미리보기 배경(5/7)과 전투 배경(7/10)은 범위가 달라도 겹치는 칸이 같아야 맵을 바꿔 끼울 때 배경이 바뀌어 보이지 않는다.
    // 할당 없이 정수 연산만 쓰므로 클라이언트마다 같은 결과가 나온다.
    struct CellRandom
    {
        uint state;

        public CellRandom(int seed, Vector2Int cell, uint channel)
        {
            unchecked
            {
                state = Mix((uint)seed * 0x9E3779B1u);
                // 맵 바깥 칸은 좌표가 음수다. 비트를 그대로 섞는다.
                state = Mix(state ^ (uint)cell.x);
                state = Mix(state ^ (uint)cell.y);
                state = Mix(state ^ channel);
            }
        }

        // murmur3 fmix32
        static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        uint NextUInt()
        {
            unchecked
            {
                state += 0x9E3779B9u;
                return Mix(state);
            }
        }

        public int Next(int max) => (int)(NextUInt() % (uint)max);
        public double NextDouble() => NextUInt() / 4294967296.0;
    }
    // 같은 칸이라도 바닥과 장식은 다른 난수를 쓴다.
    const uint FloorChannel = 1;
    const uint DecoChannel = 2;

    static Vector2Int MirrorCell(Vector2Int c, MapLayout layout)
    {
        return new Vector2Int(layout.Width - 1 - c.x, layout.Height - 1 - c.y);
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
            // 합칠 수 있으면 오브젝트를 만들지 않는다. 만들자마자 지우던 콜라이더도 생기지 않는다.
            if (combining && combiner.Add(prefab, LocalMatrix(prefab, pos, rot))) return;
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
            if (r2 != null) r2.sharedMaterial = GroundMaterial();
        }
        go.name = "Apron_" + c.x + "_" + c.y;
        StripColliders(go);
        pieces.Add(go.transform);
    }
    void PlaceDeco(Vector2Int c, MapLayout layout, int seed)
    {
        // 미러쌍당 장식 유무/종류/방향을 1회만 굴린다. 양쪽이 다르면 공정성이 깨진다.
        // 쌍의 두 칸은 행 우선으로 앞선 칸(대표)의 난수를 같이 쓰고, 대표가 아닌 칸은 반대 방향(+180°)으로 둔다.
        // 미러 칸이 깔 범위 안에 있는지와 관계없이 대표가 정해져서, 대칭이 없는 맵에서도 같은 칸은 범위와 관계없이 같다.
        Vector2Int m = MirrorCell(c, layout);
        bool mirrored = m.y < c.y || (m.y == c.y && m.x < c.x);
        var rng = new CellRandom(seed, mirrored ? m : c, DecoChannel);
        double r = rng.NextDouble();
        GameObject prefab = null;
        BackgroundRotationSettings settings = pillarRotation;
        if (r < 0.40 && pillarVariants != null && pillarVariants.Length > 0)
        {
            prefab = pillarVariants[rng.Next(pillarVariants.Length)];
            settings = pillarRotation;
        }
        else if (r < 0.52 && torchVariants != null && torchVariants.Length > 0)
        {
            prefab = torchVariants[rng.Next(torchVariants.Length)];
            settings = torchRotation;
        }
        else if (extraDecorVariants != null && extraDecorVariants.Length > 0)
        {
            prefab = extraDecorVariants[rng.Next(extraDecorVariants.Length)];
            settings = extraRotation;
        }
        if (prefab == null) return;
        settings = ApplyOverride(prefab, settings);
        float yaw = RollYaw(ref rng, settings.yawMode);
        if (mirrored) yaw = (yaw + 180f) % 360f;
        SpawnDecoAt(c, layout, prefab, yaw, settings);
    }
    void SpawnDecoAt(Vector2Int c, MapLayout layout, GameObject prefab, float yaw, BackgroundRotationSettings settings)
    {
        Vector3 pos = CellToWorld(c, layout);
        // 예외없이 원본 보정대로: 수동 드래그 자세(X-90 같은 축 보정 포함)를 살리고 Yaw만 곱한다.
        Quaternion rot = ResolveRotation(prefab.transform.rotation, yaw, settings);
        // 애니메이터·파티클·조명·스크립트가 없는 정적 장식은 만들지 않고 합친다. GroundSnap과 같은 계산으로 바닥에 맞춘다.
        if (combining && combiner.TryGetFirstRendererMinY(prefab,
                backgroundRoot.localToWorldMatrix * LocalMatrix(prefab, pos, rot), out float minY))
        {
            Vector3 snapped = pos;
            snapped.y += decoGroundY + settings.yOffset - minY;
            // 뒤집힌 스케일처럼 합치지 못하는 자리면 아래에서 원래대로 만든다.
            if (combiner.Add(prefab, LocalMatrix(prefab, snapped, rot))) return;
        }
        var go = Instantiate(prefab, pos, rot, backgroundRoot);
        go.name = "Deco_" + c.x + "_" + c.y;
        StripColliders(go);
        GroundSnap(go, decoGroundY + settings.yOffset);
        // 땅에 맞춘 뒤의 위치가 물결이 돌아올 제자리다.
        pieces.Add(go.transform);
    }
    static float RollYaw(ref CellRandom rng, BackgroundYawMode mode)
    {
        switch (mode)
        {
            case BackgroundYawMode.Random360: return (float)(rng.NextDouble() * 360.0);
            case BackgroundYawMode.Random90: return rng.Next(4) * 90f;
            default: return 0f;
        }
    }
    static Quaternion ResolveRotation(Quaternion prefabRotation, float yaw, BackgroundRotationSettings settings)
    {
        // 수동 배치 자세를 최우선으로 살린다: Yaw(월드Y) * 프리팹원본. 원본을 버리지 않는다.
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        q *= prefabRotation; // auto-correct
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
    List<Vector2Int> CollectRimCells(MapLayout layout, int offset)
    {
        var list = new List<Vector2Int>();
        // 맵 바깥까지 확장해서 둘러싼다. 예전에는 0~Width 안에서만 골라서
        // 전장 가장자리에서 잘렸다. 바깥 좌표도 CellToWorld로 배치된다.
        for (int y = -offset; y < layout.Height + offset; y++)
            for (int x = -offset; x < layout.Width + offset; x++)
            {
                if (x >= 0 && y >= 0 && x < layout.Width && y < layout.Height
                    && layout[x, y] != MapCell.Void) continue;
                if (IsNearPlayable(layout, x, y, offset)) list.Add(new Vector2Int(x, y));
            }
        return list;
    }
    bool IsNearPlayable(MapLayout layout, int x, int y, int offset)
    {
        // (x, y)는 맵 바깥일 수 있다. 주변 offset 안에 플레이 영역이 있으면 true.
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
        if (rend != null) rend.sharedMaterial = GroundMaterial();
    }

    // URP Lit 머티리얼을 코드로 만든다. CreatePrimitive 기본 머티리얼은 URP 빌드에서
    // 셰이더가 걷혀 마젠타(보라색)로 보이는 원인이 된다. 카메라 배경색/포그색과 맞춤.
    // 전용 서버 빌드(Dedicated Server Optimizations)에는 셰이더가 없어 null을 돌려준다. 그리지 않으니 머티리얼 없이 둔다.
    static Material GroundMaterial()
    {
        if (sharedGroundMaterial != null || groundShaderMissing) return sharedGroundMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            // 맵을 만들 때마다 같은 셰이더를 다시 찾지 않는다.
            groundShaderMissing = true;
            return null;
        }
        sharedGroundMaterial = new Material(shader);
        sharedGroundMaterial.color = new Color(0.05490196f, 0.07450981f, 0.12156863f);
        return sharedGroundMaterial;
    }
    static void StripColliders(GameObject go)
    {
        foreach (Collider col in go.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }

    // 맵이 하나뿐인 씬(NetworkDemo) 호환용. Main처럼 맵이 둘이면 null을 돌려준다.
    static ProceduralMap FindSingleMap()
    {
#if UNITY_2022_2_OR_NEWER
        ProceduralMap[] maps = FindObjectsByType<ProceduralMap>(FindObjectsSortMode.None);
#else
        ProceduralMap[] maps = FindObjectsOfType<ProceduralMap>();
#endif
        return maps != null && maps.Length == 1 ? maps[0] : null;
    }
}
