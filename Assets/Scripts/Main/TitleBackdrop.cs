using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 시작 화면 뒤에서 맵을 주기적으로 새로 만들고 카메라를 천천히 돌린다.
// 안개와 배경색은 씬의 Lighting 설정에 있고, 여기서는 맵 크기에 맞춰 거리만 바꾼다.
public class TitleBackdrop : MonoBehaviour
{
    // 길쭉한 맵은 멀리서 보면 가는 띠처럼 보여서 배경에서는 뺀다.
    static readonly MapShape[] Shapes = { MapShape.Circle, MapShape.Donut, MapShape.Rectangle };

    [SerializeField] ProceduralMap map;
    [SerializeField] Camera viewCamera;
    [Tooltip("맵 아래에 깔리는 바닥 빛. 맵 크기에 맞춰 늘어난다.")]
    [SerializeField] Transform ground;
    [SerializeField] float regenerateInterval = 12f;
    [Tooltip("맵이 바닥 아래로 숨는 깊이와 오르내리는 시간.")]
    [SerializeField] float sinkDepth = 10f;
    [SerializeField] float moveDuration = 0.8f;
    [SerializeField] float orbitSpeed = 3f;
    [SerializeField, Range(20f, 80f)] float pitch = 52f;
    [Tooltip("맵 중심이 놓일 화면 위치. (0, 0)은 왼쪽 아래, (1, 1)은 오른쪽 위.")]
    [SerializeField] Vector2 screenAnchor = new Vector2(0.67f, 0.42f);
    [Tooltip("카메라 거리는 맵 반지름에 이 값을 곱한 만큼이다.")]
    [SerializeField] float distanceScale = 3.8f;

    Vector3 center;
    float yaw;
    float distance;
    UniversalRenderPipelineAsset pipeline;
    float originalShadowDistance;

    void OnEnable()
    {
        center = map.transform.position;
        yaw = Random.Range(0f, 360f);
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null) originalShadowDistance = pipeline.shadowDistance;
        StartCoroutine(Cycle());
    }

    void OnDisable()
    {
        // 그림자 거리는 프로젝트 에셋 값이라 꺼질 때 꼭 되돌린다.
        if (pipeline != null) pipeline.shadowDistance = originalShadowDistance;
        // 씬을 닫을 때는 카메라가 먼저 사라질 수 있다.
        if (viewCamera != null) viewCamera.ResetProjectionMatrix();
    }

    IEnumerator Cycle()
    {
        for (int i = Random.Range(0, Shapes.Length); ; i++)
        {
            map.Generate(new MapGenerationSettings
            {
                Seed = Random.Range(1, int.MaxValue),
                Shape = Shapes[i % Shapes.Length],
                ObstacleDensity = Random.Range(0.1f, 0.22f),
                Symmetry = MapSymmetry.Rotate180,
                NoiseStrength = Random.Range(0.3f, 0.7f),
                NoiseScale = Random.Range(0.5f, 1.25f)
            });

            // 칸 크기가 2라서 대각선 칸 수가 곧 반지름이다. 카메라가 돌아도 맵이 잘리지 않게 대각선을 쓴다.
            Vector2Int size = map.GeneratedLayout.Size;
            float radius = Mathf.Sqrt(size.x * size.x + size.y * size.y);
            distance = radius * distanceScale;
            ground.localScale = new Vector3(radius, 1f, radius);
            RenderSettings.fogStartDistance = distance - 6f;
            RenderSettings.fogEndDistance = distance + 90f;
            // 멀리서 내려다보면 기본 그림자 거리로는 그림자가 안 생겨서 잠시 늘린다.
            if (pipeline != null) pipeline.shadowDistance = Mathf.Max(originalShadowDistance, distance + radius * 1.5f);

            yield return Move(-sinkDepth, 0f);
            yield return new WaitForSeconds(regenerateInterval);
            yield return Move(0f, -sinkDepth);
        }
    }

    // 맵 전체를 바닥 아래에서 올리거나 내린다.
    IEnumerator Move(float fromY, float toY)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / moveDuration)
        {
            map.transform.position = center + Vector3.up * Mathf.Lerp(fromY, toY, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        map.transform.position = center + Vector3.up * toY;
    }

    void LateUpdate()
    {
        yaw += orbitSpeed * Time.deltaTime;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        viewCamera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);

        // 렌즈를 옮겨 맵 중심이 화면 가운데가 아닌 screenAnchor에 오게 한다.
        viewCamera.ResetProjectionMatrix();
        Matrix4x4 projection = viewCamera.projectionMatrix;
        projection[0, 2] = 1f - 2f * screenAnchor.x;
        projection[1, 2] = 1f - 2f * screenAnchor.y;
        viewCamera.projectionMatrix = projection;
    }
}
