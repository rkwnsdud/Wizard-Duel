using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LightningEffect : MonoBehaviour
{
    [Header("Endpoints")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [Min(0.1f)][SerializeField] private float duration = 1.5f;

    [Header("Lightning")]
    [SerializeField] private Material lightningBeamMaterial;
    [Min(1)][SerializeField] private int lightningArcCount = 3;
    [Min(2)][SerializeField] private int lightningSegmentCount = 14;
    [Min(0f)][SerializeField] private float lightningJitter = 0.3f;
    [Min(0.01f)][SerializeField] private float lightningRefreshInterval = 0.08f;
    [Min(0.01f)][SerializeField] private float lightningBeamWidth = 0.18f;

    private Mesh beamMesh;
    private MeshRenderer beamRenderer;

    // 한 번 생성하고 계속 재사용
    private Vector3[][] arcPoints;
    private Vector3[] vertices;
    private Vector2[] uvs;
    private int[] triangles;

    private int pointCount;
    private int verticesPerArc;
    private int trianglesPerArc;

    private float nextRefreshTime;

    public void SetEndpoints(Vector3 start, Vector3 end)
    {
        if (startPoint == null || endPoint == null)
            return;

        startPoint.position = start;
        endPoint.position = end;

        if (beamMesh != null)
        {
            nextRefreshTime = -1f;
            UpdateLightning();
        }
    }

    private IEnumerator Start()
    {
        if (startPoint == null ||
            endPoint == null ||
            lightningBeamMaterial == null)
        {
            Debug.LogError(
                "LightningEffect: StartPoint, EndPoint, Material이 필요합니다.",
                this
            );

            Destroy(gameObject);
            yield break;
        }

        // 값 보호
        duration = Mathf.Max(0.1f, duration);
        lightningArcCount = Mathf.Max(1, lightningArcCount);
        lightningSegmentCount = Mathf.Max(2, lightningSegmentCount);
        lightningJitter = Mathf.Max(0f, lightningJitter);
        lightningRefreshInterval = Mathf.Max(0.01f, lightningRefreshInterval);
        lightningBeamWidth = Mathf.Max(0.01f, lightningBeamWidth);

        CreateMesh();
        CreateBuffers();

        nextRefreshTime = -1f;
        UpdateLightning();

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            UpdateLightning();
            yield return null;
        }

        Destroy(gameObject);
    }

    private void CreateMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();

        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();

        beamRenderer = GetComponent<MeshRenderer>();

        if (beamRenderer == null)
            beamRenderer = gameObject.AddComponent<MeshRenderer>();

        beamMesh = new Mesh
        {
            name = "LightningRibbon"
        };

        beamMesh.MarkDynamic();

        meshFilter.sharedMesh = beamMesh;
        beamRenderer.sharedMaterial = lightningBeamMaterial;
        beamRenderer.sortingOrder = 1;
    }

    private void CreateBuffers()
    {
        pointCount = lightningSegmentCount + 1;
        verticesPerArc = pointCount * 2;
        trianglesPerArc = lightningSegmentCount * 6;

        arcPoints = new Vector3[lightningArcCount][];

        for (int i = 0; i < lightningArcCount; i++)
            arcPoints[i] = new Vector3[pointCount];

        vertices =
            new Vector3[verticesPerArc * lightningArcCount];

        uvs =
            new Vector2[vertices.Length];

        triangles =
            new int[trianglesPerArc * lightningArcCount];

        // UV는 모양이 바뀌어도 동일
        CreateUVs();

        // Triangle 구조 역시 매 프레임 동일
        CreateTriangles();
    }

    private void CreateUVs()
    {
        for (int arc = 0; arc < lightningArcCount; arc++)
        {
            int offset = arc * verticesPerArc;

            for (int i = 0; i < pointCount; i++)
            {
                float t = (float)i / (pointCount - 1);

                int vertex = offset + i * 2;

                uvs[vertex] = new Vector2(t, 0f);
                uvs[vertex + 1] = new Vector2(t, 1f);
            }
        }
    }

    private void CreateTriangles()
    {
        for (int arc = 0; arc < lightningArcCount; arc++)
        {
            int vertexOffset = arc * verticesPerArc;
            int triangleOffset = arc * trianglesPerArc;

            for (int segment = 0;
                 segment < lightningSegmentCount;
                 segment++)
            {
                int vertex =
                    vertexOffset + segment * 2;

                int triangle =
                    triangleOffset + segment * 6;

                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 2;
                triangles[triangle + 2] = vertex + 1;

                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
        }
    }

    private void UpdateLightning()
    {
        if (Time.time < nextRefreshTime)
            return;

        nextRefreshTime =
            Time.time + lightningRefreshInterval;

        Vector3 start = startPoint.position;
        Vector3 end = endPoint.position;

        Vector3 direction = end - start;

        Vector3 forward =
            direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;

        // 번개가 흔들릴 평면
        Vector3 side =
            Vector3.Cross(forward, Vector3.up);

        if (side.sqrMagnitude < 0.0001f)
            side = Vector3.Cross(forward, Vector3.right);

        side.Normalize();

        Vector3 up =
            Vector3.Cross(side, forward).normalized;

        // 각 번개의 지그재그 위치 생성
        for (int arc = 0; arc < lightningArcCount; arc++)
        {
            Vector3[] points = arcPoints[arc];

            points[0] = start;

            for (int segment = 1;
                 segment < lightningSegmentCount;
                 segment++)
            {
                float t =
                    (float)segment / lightningSegmentCount;

                // 양 끝에서는 흔들림 0,
                // 가운데에서 가장 크게 흔들림
                float taper =
                    Mathf.Sin(t * Mathf.PI);

                Vector2 random =
                    Random.insideUnitCircle *
                    lightningJitter *
                    taper;

                points[segment] =
                    Vector3.Lerp(start, end, t)
                    + side * random.x
                    + up * random.y;
            }

            points[lightningSegmentCount] = end;
        }

        RebuildVertices();
    }

    private void RebuildVertices()
    {
        Camera cam = Camera.main;

        float halfWidth =
            lightningBeamWidth * 0.5f;

        for (int arc = 0; arc < lightningArcCount; arc++)
        {
            Vector3[] points = arcPoints[arc];

            int offset =
                arc * verticesPerArc;

            for (int i = 0; i < pointCount; i++)
            {
                Vector3 tangent;

                if (i == 0)
                    tangent = points[1] - points[0];

                else if (i == pointCount - 1)
                    tangent = points[i] - points[i - 1];

                else
                    tangent = points[i + 1] - points[i - 1];

                if (tangent.sqrMagnitude > 0.0001f)
                    tangent.Normalize();
                else
                    tangent = Vector3.forward;

                Vector3 towardCamera =
                    cam != null
                        ? cam.transform.position - points[i]
                        : Vector3.up;

                Vector3 widthDirection =
                    Vector3.Cross(
                        tangent,
                        towardCamera
                    );

                if (widthDirection.sqrMagnitude < 0.0001f)
                    widthDirection =
                        Vector3.Cross(tangent, Vector3.up);

                if (widthDirection.sqrMagnitude < 0.0001f)
                    widthDirection =
                        Vector3.Cross(tangent, Vector3.right);

                widthDirection.Normalize();

                int vertex =
                    offset + i * 2;

                vertices[vertex] =
                    transform.InverseTransformPoint(
                        points[i] -
                        widthDirection * halfWidth
                    );

                vertices[vertex + 1] =
                    transform.InverseTransformPoint(
                        points[i] +
                        widthDirection * halfWidth
                    );
            }
        }

        // Mesh 갱신
        beamMesh.Clear();

        beamMesh.vertices = vertices;
        beamMesh.uv = uvs;
        beamMesh.triangles = triangles;

        beamMesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        if (beamMesh != null)
            Destroy(beamMesh);
    }
}