using System.Collections.Generic;
using UnityEngine;

// 부시 칸 하나를 덮는 갈대 덤불(이슈 #93). 칸 좌표로 정한 시드로 칸마다 메시를 만든다.
// 갈대 포기를 칸 전체에 같은 간격으로 깔아서 옆 부시 칸과 끊김 없이 이어지고, 덩어리 바깥으로 열린 변과 모서리 쪽 포기는
// 짧고 바깥으로 기울게 해서 여러 칸이 하나의 갈대밭처럼 보인다. 색은 ReedAssetsBuilder가 만든 그라데이션 텍스처의 열로 고른다.
// 편집 모드에서 맵을 만들고 지울 때(테스트, 맵 생성 미리보기)도 OnDestroy가 불려 만든 메시가 남지 않게 ExecuteAlways로 둔다.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[DisallowMultipleComponent]
public sealed class ReedPatch : MonoBehaviour
{
    // 색 텍스처는 세로 그라데이션 열 ColorColumns개다. 앞 BladeColors열은 잎과 줄기, 나머지는 이삭이다. v = 0이 밑동, 1이 끝이다.
    public const int ColorColumns = 8;
    public const int BladeColors = 4;

    [Tooltip("칸 한 변에 놓는 갈대 포기 수. 칸마다 이 수의 제곱만큼 놓는다.")]
    [SerializeField, Range(2, 6)] int tuftsPerSide = 4;
    [Tooltip("포기 하나의 잎 수(최소, 최대).")]
    [SerializeField] Vector2Int bladesPerTuft = new Vector2Int(6, 9);
    [Tooltip("덩어리 안쪽 잎 길이(최소, 최대).")]
    [SerializeField] Vector2 bladeLength = new Vector2(0.8f, 1.3f);
    [Tooltip("이삭이 달린 줄기 길이(최소, 최대). 잎보다 조금 높이 솟는다.")]
    [SerializeField] Vector2 stalkLength = new Vector2(1.15f, 1.45f);
    [Tooltip("잎 밑동 너비. 안티에일리어싱이 없어 너무 가늘면 카메라가 움직일 때 깜빡인다.")]
    [SerializeField] float bladeWidth = 0.085f;
    [Tooltip("이삭 가닥 길이와 가장 넓은 곳의 너비. 이삭은 줄기 끝에서 한쪽으로 처지는 가닥 여러 개다.")]
    [SerializeField] Vector2 plumeSize = new Vector2(0.4f, 0.07f);
    [Tooltip("포기마다 이삭 줄기가 달릴 확률.")]
    [SerializeField, Range(0f, 1f)] float plumeChance = 0.8f;
    [Tooltip("덩어리 가장자리에 놓인 포기의 키 비율.")]
    [SerializeField, Range(0.2f, 1f)] float edgeHeight = 0.55f;
    [Tooltip("열린 변에서 이 거리(칸 단위)만큼 안쪽부터 포기가 다 자란다.")]
    [SerializeField, Range(0.1f, 1f)] float edgeFalloff = 0.55f;

    // 맵 기준으로 카메라를 향하는 방향. 전투 카메라는 -Z 쪽에서 내려다보므로 잎 앞면을 이쪽으로 맞춰 빛을 고르게 받게 한다.
    static readonly Vector3 TowardCamera = new Vector3(0f, 0.75f, -0.66f).normalized;

    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> triangles = new List<int>();
    Mesh built;
    float cellSize = 1f;

    public BushNeighbors Neighbors { get; private set; } = BushNeighbors.All;

    // 칸 모양에 맞춰 메시를 새로 만든다. 같은 시드와 이웃이면 언제나 같은 메시다.
    // 블록은 무작위로 90도씩 돌아 있으므로, 맵 기준 방향(북쪽 = 맵의 +Z)으로 만든 뒤 블록 회전을 되돌려 넣는다.
    // cellSize는 맵 칸 한 변 길이다. 잎 길이(높이)는 칸 크기와 상관없이 그대로 둔다.
    public void Build(int seed, BushNeighbors neighbors, float cellSize = 1f)
    {
        Neighbors = neighbors;
        this.cellSize = cellSize;
        vertices.Clear();
        normals.Clear();
        uvs.Clear();
        triangles.Clear();
        var random = new System.Random(seed);
        Quaternion toLocal = Quaternion.Inverse(transform.localRotation);
        float step = 1f / tuftsPerSide;
        for (int i = 0; i < tuftsPerSide; i++)
        {
            for (int j = 0; j < tuftsPerSide; j++)
            {
                // 칸을 나눈 격자 안에서만 흔들어 이웃 칸 포기와의 간격도 칸 안과 같게 둔다.
                var center = new Vector2(
                    -0.5f + (i + 0.5f + Range(random, -0.35f, 0.35f)) * step,
                    -0.5f + (j + 0.5f + Range(random, -0.35f, 0.35f)) * step);
                float edge = EdgeDistance(center, neighbors, out Vector2 outward);
                float grown = Smooth(edge / edgeFalloff);
                AddTuft(random, center, Mathf.Lerp(edgeHeight, 1f, grown), outward * (1f - grown), toLocal);
            }
        }

        if (built == null) built = new Mesh { name = $"{name} Reeds" };
        built.Clear();
        built.SetVertices(vertices);
        built.SetNormals(normals);
        built.SetUVs(0, uvs);
        built.SetTriangles(triangles, 0);
        built.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = built;
    }

    void OnDestroy()
    {
        if (built == null) return;
        if (Application.isPlaying) Destroy(built);
        else DestroyImmediate(built);
    }

    // 칸 안 점에서 덩어리 바깥(이웃이 부시가 아닌 변이나 모서리)까지 거리와 그쪽 방향. 사방이 부시면 1을 넘는 값이다.
    public static float EdgeDistance(Vector2 point, BushNeighbors neighbors, out Vector2 outward)
    {
        float best = float.MaxValue;
        Vector2 direction = Vector2.zero;
        void Consider(float distance, Vector2 toward)
        {
            if (distance >= best) return;
            best = distance;
            direction = toward;
        }

        if (!Has(neighbors, BushNeighbors.North)) Consider(0.5f - point.y, Vector2.up);
        if (!Has(neighbors, BushNeighbors.South)) Consider(point.y + 0.5f, Vector2.down);
        if (!Has(neighbors, BushNeighbors.East)) Consider(0.5f - point.x, Vector2.right);
        if (!Has(neighbors, BushNeighbors.West)) Consider(point.x + 0.5f, Vector2.left);
        // 두 변은 막혔는데 대각선 칸만 비었으면 그 꼭짓점이 덩어리 바깥이다.
        ConsiderCorner(BushNeighbors.North, BushNeighbors.East, BushNeighbors.NorthEast, new Vector2(0.5f, 0.5f));
        ConsiderCorner(BushNeighbors.South, BushNeighbors.East, BushNeighbors.SouthEast, new Vector2(0.5f, -0.5f));
        ConsiderCorner(BushNeighbors.South, BushNeighbors.West, BushNeighbors.SouthWest, new Vector2(-0.5f, -0.5f));
        ConsiderCorner(BushNeighbors.North, BushNeighbors.West, BushNeighbors.NorthWest, new Vector2(-0.5f, 0.5f));

        void ConsiderCorner(BushNeighbors a, BushNeighbors b, BushNeighbors diagonal, Vector2 corner)
        {
            if (!Has(neighbors, a) || !Has(neighbors, b) || Has(neighbors, diagonal)) return;
            Vector2 toCorner = corner - point;
            Consider(toCorner.magnitude, toCorner.normalized);
        }

        outward = direction;
        return best == float.MaxValue ? 2f : Mathf.Max(0f, best);
    }

    static bool Has(BushNeighbors neighbors, BushNeighbors flag) => (neighbors & flag) != 0;

    void AddTuft(System.Random random, Vector2 center, float scale, Vector2 lean, Quaternion toLocal)
    {
        int color = random.Next(BladeColors);
        int blades = random.Next(bladesPerTuft.x, bladesPerTuft.y + 1);
        for (int b = 0; b < blades; b++)
        {
            // 포기 가운데에서 바깥으로 벌어지게 기울이고, 가장자리 포기는 덩어리 바깥으로 조금 더 기울인다.
            // 짧은 가장자리 잎까지 많이 휘면 바닥에 누운 부스러기처럼 보이므로 끝이 수평을 넘지 않게 둔다.
            float angle = (b + Range(random, -0.3f, 0.3f)) / blades * Mathf.PI * 2f;
            var spread = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 root = center + spread * Range(random, 0f, 0.05f);
            Vector2 toward = (spread + lean).normalized;
            float tilt = Range(random, 4f, 18f) + lean.magnitude * 10f;
            AddBlade(root, toward, tilt, Range(random, 18f, 45f), Range(random, bladeLength.x, bladeLength.y) * scale,
                bladeWidth * Range(random, 0.8f, 1.15f), ColumnU(color), 1f, toLocal);
        }

        if (scale < 0.7f || random.NextDouble() > plumeChance) return;
        int stalks = random.Next(1, 4);
        for (int s = 0; s < stalks; s++)
        {
            float angle = Range(random, 0f, Mathf.PI * 2f);
            var toward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 root = center + toward * Range(random, 0f, 0.05f);
            float length = Range(random, stalkLength.x, stalkLength.y) * scale;
            Vector3 top = AddBlade(root, (toward + lean).normalized, Range(random, 0f, 8f), Range(random, 5f, 15f), length,
                bladeWidth * 0.45f, ColumnU(color), 0.85f, toLocal);
            AddPlume(random, top, toward, plumeSize.x * scale, plumeSize.y,
                ColumnU(BladeColors + random.Next(ColorColumns - BladeColors)), toLocal);
        }
    }

    // 밑동에서 끝으로 갈수록 휘고 좁아지는 잎 하나. 잎 면은 기우는 방향을 향한다. 끝 위치를 돌려준다.
    Vector3 AddBlade(Vector2 root, Vector2 toward, float tiltDegrees, float bendDegrees, float length, float width,
        float u, float topV, Quaternion toLocal)
    {
        const int segments = 3;
        var lean = new Vector3(toward.x, 0f, toward.y);
        var side = new Vector3(-toward.y, 0f, toward.x);
        Vector3 point = new Vector3(root.x, 0f, root.y);
        float step = length / segments;
        for (int k = 0; k <= segments; k++)
        {
            float t = (float)k / segments;
            float angle = (tiltDegrees + bendDegrees * t * t) * Mathf.Deg2Rad;
            Vector3 tangent = lean * Mathf.Sin(angle) + Vector3.up * Mathf.Cos(angle);
            float half = width * 0.5f * (1f - t * 0.92f);
            AddPair(point, side * half, tangent, u, t * topV, toLocal);
            if (k > 0) AddQuad();
            if (k < segments)
            {
                float next = (tiltDegrees + bendDegrees * (t + 0.5f / segments) * (t + 0.5f / segments)) * Mathf.Deg2Rad;
                point += (lean * Mathf.Sin(next) + Vector3.up * Mathf.Cos(next)) * step;
            }
        }
        return point;
    }

    // 줄기 끝에 달린 갈대 이삭. 가닥 여러 개가 줄기 끝 근처에서 갈라져 한쪽으로 처져서 깃털 뭉치처럼 보인다.
    void AddPlume(System.Random random, Vector3 top, Vector2 toward, float length, float width, float u, Quaternion toLocal)
    {
        const int segments = 3;
        float[] profile = { 0.45f, 1f, 0.7f, 0.1f };
        int strands = random.Next(4, 7);
        for (int s = 0; s < strands; s++)
        {
            float turn = Range(random, -0.6f, 0.6f);
            var direction = new Vector2(
                toward.x * Mathf.Cos(turn) - toward.y * Mathf.Sin(turn),
                toward.x * Mathf.Sin(turn) + toward.y * Mathf.Cos(turn));
            var lean = new Vector3(direction.x, 0f, direction.y);
            var side = new Vector3(-direction.y, 0f, direction.x);
            Vector3 point = top + Vector3.down * Range(random, 0f, 0.12f);
            float strandLength = length * Range(random, 0.7f, 1.1f);
            float start = Range(random, 5f, 25f);
            float droop = Range(random, 45f, 95f);
            float strandWidth = width * Range(random, 0.8f, 1.2f);
            for (int k = 0; k <= segments; k++)
            {
                float t = (float)k / segments;
                float angle = (start + droop * t * t) * Mathf.Deg2Rad;
                Vector3 tangent = lean * Mathf.Sin(angle) + Vector3.up * Mathf.Cos(angle);
                AddPair(point, side * (strandWidth * 0.5f * profile[k]), tangent, u, Mathf.Lerp(0.2f, 1f, t), toLocal);
                if (k > 0) AddQuad();
                point += tangent * (strandLength / segments);
            }
        }
    }

    void AddPair(Vector3 center, Vector3 halfSide, Vector3 tangent, float u, float v, Quaternion toLocal)
    {
        // 잎 앞면을 카메라 쪽으로 맞추고 위쪽을 많이 섞어, 양면으로 그려도 앞뒤 밝기가 같고 잎끼리 밝기가 크게 튀지 않게 한다.
        Vector3 face = Vector3.Cross(tangent, halfSide).normalized;
        if (Vector3.Dot(face, TowardCamera) < 0f) face = -face;
        Vector3 normal = toLocal * (Vector3.up * 0.6f + face * 0.4f).normalized;
        vertices.Add(toLocal * OnCell(center - halfSide));
        vertices.Add(toLocal * OnCell(center + halfSide));
        normals.Add(normal);
        normals.Add(normal);
        uvs.Add(new Vector2(u, v));
        uvs.Add(new Vector2(u, v));
    }

    Vector3 OnCell(Vector3 point) => new Vector3(point.x * cellSize, point.y, point.z * cellSize);

    void AddQuad()
    {
        int last = vertices.Count - 1;
        int a = last - 3, b = last - 2, c = last - 1, d = last;
        triangles.Add(a); triangles.Add(c); triangles.Add(b);
        triangles.Add(b); triangles.Add(c); triangles.Add(d);
    }

    static float ColumnU(int column) => (column + 0.5f) / ColorColumns;

    static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
