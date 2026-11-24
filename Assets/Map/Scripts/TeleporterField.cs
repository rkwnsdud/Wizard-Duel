using System.Collections.Generic;
using UnityEngine;

// 깔린 맵의 텔레포트 타일 쌍과 쌍마다의 빛 상태. 쌍 번호는 레이아웃 숫자로 정해지므로(TeleporterRegions)
// 같은 맵을 만든 모든 클라이언트가 같은 번호를 쓴다. 지금 싸우는 맵 하나만 Active로 등록한다.
// 쿨타임은 서버가 정하고, 클라이언트는 받은 값을 SetState로 넣는다. 타일 블록은 매 프레임 이 상태를 읽어 빛을 바꾼다.
public sealed class TeleporterField
{
    public static TeleporterField Active { get; private set; }

    readonly Transform root;
    readonly float cellSize;
    readonly List<TeleporterPair> pairs;
    // 쌍 번호 * 2 + 쪽의 root 기준 타일 가운데(바닥 높이).
    readonly Vector3[] centers;
    readonly float[] flashStarts;
    readonly float[] cooldownEnds;

    // layout은 맵이 블록을 깐 문자 레이아웃이고, root는 블록을 깐 부모다. 칸 좌표 변환은 BushField와 같다.
    public TeleporterField(string[] layout, Transform root, float cellSize)
    {
        this.root = root;
        this.cellSize = cellSize;
        pairs = TeleporterRegions.Parse(layout);
        centers = new Vector3[pairs.Count * 2];
        flashStarts = new float[pairs.Count];
        cooldownEnds = new float[pairs.Count];
        for (int pair = 0; pair < pairs.Count; pair++)
        {
            for (int side = 0; side < 2; side++)
            {
                Vector2Int tile = pairs[pair][side];
                string row = layout[tile.y];
                // 2×2 타일 네 칸 중심의 평균이 타일 가운데다.
                centers[pair * 2 + side] = new Vector3(
                    (tile.x + 0.5f - (row.Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - tile.y - 0.5f) * cellSize);
            }
            flashStarts[pair] = cooldownEnds[pair] = float.NegativeInfinity;
        }
    }

    // 쌍 수. 번호는 0부터 Count - 1까지다.
    public int Count => pairs.Count;
    public IReadOnlyList<TeleporterPair> Pairs => pairs;

    // 타일 가운데에서 가장자리까지의 거리. 이 안이면 타일 위에 서 있다.
    public float HalfExtent => TeleporterRegions.Size * cellSize * 0.5f;

    public static void Activate(TeleporterField field) => Active = field;

    // 맵이 지워지거나 다시 만들어질 때 부른다. 다른 맵이 이미 등록돼 있으면 그대로 둔다.
    public static void Deactivate(TeleporterField field)
    {
        if (field != null && Active == field) Active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() => Active = null;

    // 타일 가운데의 root 기준 좌표(바닥 높이). side는 0이면 A, 1이면 B다.
    public Vector3 LocalCenterOf(int pair, int side) => centers[pair * 2 + side];

    // 타일 가운데의 월드 좌표(바닥 높이).
    public Vector3 CenterOf(int pair, int side)
    {
        Vector3 local = LocalCenterOf(pair, side);
        return root != null ? root.TransformPoint(local) : local;
    }

    // world가 어느 타일 위(가운데에서 가로·세로 HalfExtent 안)에 있는지. 타일 밖이거나 맵이 사라졌으면 false다.
    public bool TryFind(Vector3 world, out int pair, out int side, out float distance)
    {
        pair = side = -1;
        distance = float.PositiveInfinity;
        // 맵 오브젝트가 지워졌으면(유니티 null) 타일도 없다. root를 넘기지 않은 필드는 월드 원점 기준이다.
        if (!ReferenceEquals(root, null) && root == null) return false;
        Vector3 local = root != null ? root.InverseTransformPoint(world) : world;
        float half = HalfExtent;
        for (int i = 0; i < centers.Length; i++)
        {
            float dx = local.x - centers[i].x, dz = local.z - centers[i].z;
            if (Mathf.Abs(dx) >= half || Mathf.Abs(dz) >= half) continue;
            pair = i / 2;
            side = i % 2;
            Vector3 center = CenterOf(pair, side);
            distance = new Vector2(world.x - center.x, world.z - center.z).magnitude;
            return true;
        }
        return false;
    }

    // 쌍의 빛 상태를 넣는다. flashStart는 발동 시각, cooldownEnd는 다시 쓸 수 있는 시각이다(Time.time 기준).
    public void SetState(int pair, float flashStart, float cooldownEnd)
    {
        if (pair < 0 || pair >= pairs.Count) return;
        flashStarts[pair] = flashStart;
        cooldownEnds[pair] = cooldownEnd;
    }

    public void GetState(int pair, out float flashStart, out float cooldownEnd)
    {
        bool valid = pair >= 0 && pair < pairs.Count;
        flashStart = valid ? flashStarts[pair] : float.NegativeInfinity;
        cooldownEnd = valid ? cooldownEnds[pair] : float.NegativeInfinity;
    }
}
