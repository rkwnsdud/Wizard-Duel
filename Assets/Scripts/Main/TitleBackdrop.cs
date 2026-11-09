using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

// 화면 뒤에 맵을 띄우고 카메라를 맞춘다. 시작 화면에서는 맵을 주기적으로 새로 만들며 천천히 돌고,
// 대기실에서는 받은 설정의 맵을 미리보기로 세워 둔다. 두 화면 사이에서는 맵을 그대로 두고 카메라만 옮길 수 있다.
// 안개와 배경색은 씬의 Lighting 설정에 있고, 여기서는 맵 크기에 맞춰 거리만 바꾼다.
public class TitleBackdrop : MonoBehaviour
{
    // 길쭉한 맵은 멀리서 보면 가는 띠처럼 보여서 배경에서는 뺀다.
    static readonly MapShape[] Shapes = { MapShape.Circle, MapShape.Donut, MapShape.Rectangle };

    [SerializeField] ProceduralMap map;
    [Tooltip("시작/대기실 미리보기 맵이 갱신되면 같이 갱신할 배경. Title Map(또는 Duel Arena의 자식 배경)과 연결한다.")]
    [SerializeField] ArenaBackgroundBuilderNew background;
    [SerializeField] Camera viewCamera;
    [Tooltip("맵 아래에 깔리는 바닥 빛. 맵 크기에 맞춰 늘어난다.")]
    [SerializeField] Transform ground;
    [SerializeField] float regenerateInterval = 12f;
    [Tooltip("블록이 바닥 아래로 숨는 깊이.")]
    [SerializeField] float sinkDepth = 10f;
    [Tooltip("물결이 맵 중심에서 맵 대각선 길이만큼 퍼지는 시간. 맵 모서리에는 그 절반쯤에 닿고, 배경은 같은 빠르기로 그 바깥까지 이어진다.")]
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
    [Tooltip("카메라 거리는 맵 반지름에 이 값을 곱한 만큼이다. 값이 작을수록 시작 화면 맵이 크게 보인다.")]
    [SerializeField] float distanceScale = 3f;

    [Header("대기실 미리보기")]
    [Tooltip("대기실에서 맵 중심이 놓일 화면 위치.")]
    [SerializeField] Vector2 previewAnchor = new Vector2(0.34f, 0.5f);
    [Tooltip("대기실에서 맵이 차지할 크기. 맵 중심에서 가장자리까지를 화면 절반에 대한 비율로 적는다. 값이 클수록 카메라가 가까워진다.")]
    [SerializeField] Vector2 previewExtent = new Vector2(0.68f, 0.8f);
    [Tooltip("대기실에서 카메라가 좌우로 천천히 흔들리는 각도.")]
    [SerializeField] float previewSway = 2.5f;
    [Tooltip("시작 화면과 대기실 사이에서 카메라가 넘어가는 빠르기.")]
    [SerializeField] float viewSharpness = 2.2f;

    [Header("전투")]
    [Tooltip("카메라를 내 플레이어에게 넘긴 뒤의 투영과 안개. DuelOfflineTest 씬과 같은 에셋(Assets/Settings/DuelCamera)을 쓴다.")]
    [SerializeField] DuelCameraSettings battleCamera;

    public Transform[] SpawnPoints => map.SpawnPoints;
    // 지금 서 있는 맵을 만든 설정.
    public MapGenerationSettings Current { get; private set; }

    // 시작 화면 맵을 만들 때 쓰는 대기실 설정. 맵을 그대로 대기실로 가져가도 설정 패널과 맞도록 같은 값으로 만든다.
    DuelSettings titleSettings = new DuelSettings();
    Vector3 center;
    float yaw;
    float radius;
    float distance;
    float targetDistance;
    Vector2 anchor;
    bool preview;
    float previewYaw;
    // 맵이 보이지 않는 동안 바뀐 거리는 미끄러지지 않고 바로 맞춘다.
    bool snapDistance = true;
    // 전투 중에는 카메라를 내 플레이어를 따라가는 쪽에 넘기고 건드리지 않는다.
    bool released;
    UniversalRenderPipelineAsset pipeline;
    float originalShadowDistance;
    float originalFieldOfView;
    // 전투 카메라가 직교일 수 있어서, 시작 화면으로 돌아오면 원래 투영으로 되돌린다.
    bool originalOrthographic;
    // map.Pieces(블록과 배경 조각)의 제자리와 출발 시간. 순서는 map.Pieces와 같다.
    Vector3[] homes;
    float[] delays;
    // 가장 먼 조각이 출발하는 시간. 물결은 이 조각까지 제자리에 서면 끝난다.
    float lastDelay;

    void OnEnable()
    {
        if (map == null) return;
        // 미리보기 배경은 Title Map 하위(또는 Duel Arena 하위)에 둔다. TitleBackdrop 하위가 아니다.
        if (background == null) background = map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true);
        center = map.transform.position;
        yaw = Random.Range(0f, 360f);
        anchor = screenAnchor;
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null) originalShadowDistance = pipeline.shadowDistance;
        originalFieldOfView = viewCamera.fieldOfView;
        originalOrthographic = viewCamera.orthographic;
        ShowTitle(titleSettings, false);
    }

    void OnDisable()
    {
        // 그림자 거리는 프로젝트 에셋 값이라 꺼질 때 꼭 되돌린다.
        if (pipeline != null) pipeline.shadowDistance = originalShadowDistance;
        // 씬을 닫을 때는 카메라가 먼저 사라질 수 있다.
        if (viewCamera == null) return;
        viewCamera.ResetProjectionMatrix();
        if (released) RestoreProjection();
    }

    // 전투에 넘기면서 바꾼 시야각과 투영을 시작 화면 값으로 되돌린다.
    void RestoreProjection()
    {
        viewCamera.orthographic = originalOrthographic;
        viewCamera.fieldOfView = originalFieldOfView;
    }

    // 맵을 주기적으로 바꾸며 도는 시작 화면 배경으로 돌아간다. 새 맵은 settings로 만들고 시드와 형태만 랜덤으로 고른다.
    // keepMap이면 지금 맵을 그대로 두고 카메라만 시작 화면 자리로 옮긴 뒤, 잠시 뒤부터 새 맵으로 바꿔 간다.
    public void ShowTitle(DuelSettings settings, bool keepMap)
    {
        titleSettings = settings.Clone();
        preview = false;
        if (map == null) return;
        // 전투에서 돌아오면 감췄던 맵 자리에 새 맵을 솟게 하고, 카메라는 시작 화면 거리로 바로 옮긴다.
        if (released || !map.gameObject.activeSelf) keepMap = false;
        if (released) RestoreProjection();
        snapDistance |= released;
        released = false;
        map.gameObject.SetActive(true);
        if (background != null) background.gameObject.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(Cycle(keepMap));
    }

    // 접속을 기다리는 동안 시작 화면 카메라를 유지하고 현재 맵의 순환만 멈춘다.
    public void HoldTitle()
    {
        StopAllCoroutines();
        StartCoroutine(Settle());
    }

    // 순환을 멈추고 지금 맵을 그대로 대기실 미리보기로 쓴다. 카메라만 대기실 자리로 옮긴다.
    public void HoldPreview()
    {
        preview = true;
        StopAllCoroutines();
        Aim();
        StartCoroutine(Settle());
    }

    // 순환을 멈추고 이 설정의 맵을 바로 솟게 한다. 만들 수 없는 설정이면 이전 맵을 그대로 두고 false를 돌려준다.
    public bool ShowPreview(MapGenerationSettings settings)
    {
        preview = true;
        StopAllCoroutines();
        try
        {
            Rebuild(settings);
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
        {
            Debug.LogWarning($"미리보기 맵을 만들지 못했어요: {exception.Message}", this);
            // 물결 도중에 멈췄을 수 있으니 이전 맵을 배경까지 제자리에 세운다.
            IReadOnlyList<Transform> pieces = map.Pieces;
            for (int p = 0; homes != null && p < pieces.Count; p++) pieces[p].localPosition = homes[p];
            return false;
        }

        StartCoroutine(Wave(true));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MapLayout layout = map.GeneratedLayout;
        uint signature = 2166136261;
        foreach (string row in layout.ToLayout())
            foreach (char cell in row) signature = unchecked((signature ^ cell) * 16777619);
        Debug.Log($"[LobbyPreview] seed={settings.Seed} shape={layout.Shape} size={layout.Size} spawnA={layout.SpawnA} spawnB={layout.SpawnB} hash={signature:X8}");
#endif
        return true;
    }

    // 전투 준비가 시작되면 같은 자리에 전투 맵이 서므로 미리보기 맵을 감춘다. 카메라는 소환 전까지 그대로 둔다.
    public void HideMap()
    {
        StopAllCoroutines();
        if (map != null) map.gameObject.SetActive(false);
        if (background != null) background.gameObject.SetActive(false);
    }

    // 내 플레이어가 소환되면 카메라를 넘긴다. 렌즈 이동과 늘려 둔 그림자 거리를 되돌리고 투영과 안개를 전투에 맞춘다.
    public void ReleaseCamera()
    {
        released = true;
        StopAllCoroutines();
        if (pipeline != null) pipeline.shadowDistance = originalShadowDistance;
        battleCamera.Apply(viewCamera);
        viewCamera.ResetProjectionMatrix();
    }

    IEnumerator Cycle(bool keepMap)
    {
        for (int i = Random.Range(0, Shapes.Length); ; i++)
        {
            if (keepMap)
            {
                Aim();
                yield return Settle();
                keepMap = false;
            }
            else
            {
                MapShape shape = Shapes[i % Shapes.Length];
                try
                {
                    Rebuild(TitleMap(shape));
                }
                catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
                {
                    // 대기실에서 만들 수 없었던 설정이 남아 있을 수 있다. 그때는 설정과 상관없는 맵으로 배경을 채운다.
                    Debug.LogWarning($"설정으로 배경 맵을 만들지 못해 임의의 맵을 띄워요: {exception.Message}", this);
                    Rebuild(FallbackMap(shape));
                }
                yield return Wave(true);
            }

            yield return new WaitForSeconds(regenerateInterval);
            yield return Wave(false);
            snapDistance = true;
        }
    }

    // 시작 화면 맵 설정. 시드는 늘 새로 뽑고, 형태가 랜덤이면 멀리서도 잘 보이는 형태 중 하나를 쓴다.
    MapGenerationSettings TitleMap(MapShape shape)
    {
        MapGenerationSettings settings = titleSettings.ToMapSettings();
        settings.Seed = Random.Range(1, int.MaxValue);
        if (settings.Shape == MapShape.Random) settings.Shape = shape;
        return settings;
    }

    static MapGenerationSettings FallbackMap(MapShape shape) => new MapGenerationSettings
    {
        Seed = Random.Range(1, int.MaxValue),
        Shape = shape,
        ObstacleDensity = Random.Range(0.1f, 0.22f),
        Symmetry = MapSymmetry.Rotate180,
        NoiseStrength = Random.Range(0.3f, 0.7f),
        NoiseScale = Random.Range(0.5f, 1.25f)
    };

    // 맵을 새로 만들고 카메라 거리, 바닥, 그림자와 물결 순서를 맵 크기에 맞춘다.
    void Rebuild(MapGenerationSettings settings)
    {
        // 미리보기 배경을 먼저 켠다. 꺼진 채로 map.Generate가 Rebuild하면 자식이 비활성으로 만들어진다.
        if (background != null && !background.gameObject.activeInHierarchy)
            background.gameObject.SetActive(true);
        map.Generate(settings);
        Current = settings;

        // radius는 맵의 대각선 칸 수다(칸 크기가 1이라 대각선 길이와 같다). 카메라가 돌아도 맵이 잘리지 않을 만큼 넉넉하고,
        // 시작 화면 카메라 거리(distanceScale), 바닥 빛 크기, 물결 빠르기가 모두 이 값에 맞춰져 있다.
        Vector2Int size = map.GeneratedLayout.Size;
        radius = Mathf.Sqrt(size.x * size.x + size.y * size.y);

        // 맵 중심에서 먼 조각일수록 늦게 출발해 가운데부터 물결이 퍼진다.
        // 같은 칸의 바닥과 벽은 거리가 같아서 한 기둥처럼 함께 움직인다. 배경의 바닥 타일과 장식도
        // 같은 빠르기로 뒤따라서, 물결이 맵 가장자리에서 끊기지 않고 배경 바깥까지 이어진다.
        IReadOnlyList<Transform> pieces = map.Pieces;
        homes = new Vector3[pieces.Count];
        delays = new float[pieces.Count];
        lastDelay = 0f;
        for (int p = 0; p < pieces.Count; p++)
        {
            homes[p] = pieces[p].localPosition;
            Vector3 fromCenter = map.transform.InverseTransformPoint(pieces[p].position);
            delays[p] = new Vector2(fromCenter.x, fromCenter.z).magnitude / radius * waveDuration;
            lastDelay = Mathf.Max(lastDelay, delays[p]);
        }

        ground.localScale = new Vector3(radius, 1f, radius);
        Aim();
    }

    // 카메라가 향할 방향과 거리, 그림자 거리를 지금 맵과 화면(시작 화면 또는 대기실)에 맞춘다.
    void Aim()
    {
        if (preview)
        {
            // 파랑이 왼쪽, 빨강이 오른쪽에 오도록 두 스폰을 잇는 선을 화면 가로에 맞춘다.
            Vector3 across = map.SpawnPoints[1].position - map.SpawnPoints[0].position;
            previewYaw = Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg;
            targetDistance = FitDistance();
        }
        else targetDistance = radius * distanceScale;
        if (snapDistance) distance = targetDistance;
        snapDistance = false;
        // 멀리서 내려다보면 기본 그림자 거리로는 그림자가 안 생겨서 잠시 늘린다.
        if (pipeline != null) pipeline.shadowDistance = Mathf.Max(originalShadowDistance, targetDistance + radius * 1.5f);
    }

    // previewYaw로 볼 때 모든 블록이 previewExtent 안에 들어오는 가장 가까운 카메라 거리.
    // 맵 모양에 따라 화면에서 차지하는 크기가 크게 달라서, 대기실에서는 반지름 대신 실제 블록 위치로 맞춘다.
    // 배경은 화면 밖으로 넘쳐도 되므로 플레이 영역 블록만 센다. y축으로만 돌려서 물결 중인 높이는 결과와 상관없다.
    float FitDistance()
    {
        float tanY = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanX = tanY * viewCamera.aspect;
        float sin = Mathf.Sin(pitch * Mathf.Deg2Rad);
        float cos = Mathf.Cos(pitch * Mathf.Deg2Rad);
        Quaternion toView = Quaternion.Euler(0f, -previewYaw, 0f);
        float fit = 0f;
        IReadOnlyList<IBlock> blocks = map.Blocks;
        for (int b = 0; b < blocks.Count; b++)
        {
            // x는 화면 가로, z는 카메라에서 멀어지는 쪽이다. 가까운 쪽일수록 화면에서 크게 보인다.
            Vector3 point = toView * blocks[b].Transform.localPosition;
            float depth = point.z * cos;
            float wide = Mathf.Abs(point.x) / (previewExtent.x * tanX);
            float tall = Mathf.Abs(point.z) * sin / (previewExtent.y * tanY);
            fit = Mathf.Max(fit, Mathf.Max(wide, tall) - depth);
        }
        return fit;
    }

    // 블록과 배경 조각을 하나씩 바닥 아래에서 올리거나 내린다. 첫 프레임에 바로 위치를 정해서 새 맵이 잠깐 보이는 일이 없다.
    IEnumerator Wave(bool rising)
    {
        IReadOnlyList<Transform> pieces = map.Pieces;
        for (float time = 0f; ; time += Time.deltaTime)
        {
            for (int p = 0; p < pieces.Count; p++)
            {
                float t = Mathf.Clamp01((time - delays[p]) / blockDuration);
                float height = riseCurve.Evaluate(rising ? t : 1f - t);
                pieces[p].localPosition = homes[p] + Vector3.down * (sinkDepth * (1f - height));
            }
            if (time >= lastDelay + blockDuration) yield break;
            yield return null;
        }
    }

    // 조각을 지금 높이에서 제자리로 물결처럼 돌려놓는다. 올라오거나 가라앉던 도중이어도 끊기지 않고 이어지고,
    // 이미 제자리에 있는 조각은 움직이지 않는다.
    IEnumerator Settle()
    {
        IReadOnlyList<Transform> pieces = map.Pieces;
        float[] starts = new float[pieces.Count];
        for (int p = 0; p < pieces.Count; p++) starts[p] = pieces[p].localPosition.y - homes[p].y;
        for (float time = 0f; ; time += Time.deltaTime)
        {
            for (int p = 0; p < pieces.Count; p++)
            {
                float t = Mathf.Clamp01((time - delays[p]) / blockDuration);
                pieces[p].localPosition = homes[p] + Vector3.up * (starts[p] * (1f - riseCurve.Evaluate(t)));
            }
            if (time >= lastDelay + blockDuration) yield break;
            yield return null;
        }
    }

    void LateUpdate()
    {
        if (released) return;
        // 시작 화면에서는 계속 돌고, 대기실에서는 정한 방향 근처에서 조금만 흔들린다.
        float blend = 1f - Mathf.Exp(-viewSharpness * Time.deltaTime);
        if (preview) yaw = Mathf.LerpAngle(yaw, previewYaw + Mathf.Sin(Time.time * 0.15f) * previewSway, blend);
        else yaw += orbitSpeed * Time.deltaTime;
        distance = Mathf.Lerp(distance, targetDistance, blend);
        anchor = Vector2.Lerp(anchor, preview ? previewAnchor : screenAnchor, blend);

        RenderSettings.fogStartDistance = distance - 6f;
        RenderSettings.fogEndDistance = distance + 90f;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        viewCamera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);

        // 렌즈를 옮겨 맵 중심이 화면 가운데가 아닌 anchor에 오게 한다.
        viewCamera.ResetProjectionMatrix();
        Matrix4x4 projection = viewCamera.projectionMatrix;
        projection[0, 2] = 1f - 2f * anchor.x;
        projection[1, 2] = 1f - 2f * anchor.y;
        viewCamera.projectionMatrix = projection;
    }
}
