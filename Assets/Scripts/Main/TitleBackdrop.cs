using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("블록이 바닥 아래로 숨는 깊이.")]
    [SerializeField] float sinkDepth = 10f;
    [Tooltip("물결이 맵 중심에서 모서리까지 퍼지는 시간.")]
    [SerializeField] float waveDuration = 1f;
    [Tooltip("블록 하나가 올라오거나 내려가는 시간.")]
    [SerializeField, Min(0.05f)] float blockDuration = 0.7f;
    [Tooltip("블록 하나가 올라오는 모양. 0은 숨은 위치, 1은 제자리이고 1을 넘는 부분이 바운스다. 내려갈 때는 거꾸로 쓴다.")]
    [SerializeField] AnimationCurve riseCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 3.5f), new Keyframe(0.55f, 1.06f), new Keyframe(0.8f, 0.98f), new Keyframe(1f, 1f));
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
    Vector3[] homes;
    float[] delays;

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

            // 맵 중심에서 먼 블록일수록 늦게 출발해 가운데부터 물결이 퍼진다.
            // 같은 칸의 바닥과 벽은 거리가 같아서 한 기둥처럼 함께 움직인다.
            IReadOnlyList<IBlock> blocks = map.Blocks;
            homes = new Vector3[blocks.Count];
            delays = new float[blocks.Count];
            for (int b = 0; b < blocks.Count; b++)
            {
                homes[b] = blocks[b].Transform.localPosition;
                delays[b] = new Vector2(homes[b].x, homes[b].z).magnitude / radius * waveDuration;
            }

            yield return Wave(true);
            yield return new WaitForSeconds(regenerateInterval);
            yield return Wave(false);
        }
    }

    // 블록을 하나씩 바닥 아래에서 올리거나 내린다. 첫 프레임에 바로 위치를 정해서 새 맵이 잠깐 보이는 일이 없다.
    IEnumerator Wave(bool rising)
    {
        IReadOnlyList<IBlock> blocks = map.Blocks;
        for (float time = 0f; ; time += Time.deltaTime)
        {
            for (int b = 0; b < blocks.Count; b++)
            {
                float t = Mathf.Clamp01((time - delays[b]) / blockDuration);
                float height = riseCurve.Evaluate(rising ? t : 1f - t);
                blocks[b].Transform.localPosition = homes[b] + Vector3.down * (sinkDepth * (1f - height));
            }
            if (time >= waveDuration + blockDuration) yield break;
            yield return null;
        }
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
