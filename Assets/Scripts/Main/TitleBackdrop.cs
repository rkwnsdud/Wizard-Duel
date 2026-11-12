using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

// 화면 뒤에 맵을 띄우고 카메라를 맞춘다. 시작 화면에서는 맵을 주기적으로 새로 만들며 천천히 돌고,
// 대기실에서는 받은 설정의 맵을 미리보기로 세워 둔다. 두 화면 사이에서는 맵을 그대로 두고 카메라만 옮길 수 있다.
// 전투가 시작되면 카메라가 대기실 자리에서 내 스폰 지점으로 내려가 내 플레이어를 따라가는 전투 카메라가 되고,
// 결과 뒤 대기실로 돌아오면 거꾸로 빠져나온다. 원근(대기실)과 직교(전투) 사이는 CameraView로 섞는다.
// 태양 광원도 같은 빠르기로 두 스폰을 잇는 선에 직교하는 방향으로 돌았다가 돌아온다.
// 안개와 배경색은 씬의 Lighting 설정에 있고, 여기서는 맵 크기에 맞춰 거리만 바꾼다.
public class TitleBackdrop : MonoBehaviour
{
    enum Blend { None, Enter, Exit }

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
    [Tooltip("대기실 카메라가 내 스폰 지점으로 내려가 전투 카메라가 되기까지, 또는 전투에서 대기실 구도로 빠져나오기까지 걸리는 시간(초). " +
        "AI 대전은 소환 뒤 스폰 이펙트(1.35초)와 0.15초가 지나면 조작이 켜지므로 그보다 짧게 둔다.")]
    [SerializeField, Min(0.1f)] float diveDuration = 1.2f;
    [Tooltip("내려가고 빠져나오는 빠르기. 0은 출발 장면, 1은 도착 장면이다.")]
    [SerializeField] AnimationCurve diveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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
    // 내 플레이어를 따라가는 카메라. 전투로 내려가는 동안에는 Held로 붙잡아 둔다.
    CameraFollow follow;
    // 지금 대기실과 전투 사이를 섞는 중인지와 그 방향.
    Blend blend;
    // 섞기를 시작한 순간의 장면. 도중에 방향이 바뀌어도 그때 보이던 장면에서 다시 출발하므로 카메라가 튀지 않는다.
    CameraView from;
    // 지난 프레임에 실제로 그린 장면.
    CameraView shown;
    // 섞기 진행도(0~1). 시작한 다음 프레임부터 센다.
    float blendTime;
    bool blendCounting;
    // 도착 장면의 방향을 지난 프레임 값 가까이로 풀어 둔 값. 180° 근처에서 반대쪽으로 도는 일이 없다.
    float targetYaw;
    // 전투로 내려가며 겨누는 스폰 지점과, 그것이 사라졌을 때 쓸 마지막 위치.
    Transform diveTarget;
    Vector3 lastTarget;
    // 씬의 태양 광원. 전투 중에는 두 스폰을 잇는 선에 직교하게 돌려 두고(DuelAxis.SideLight), 카메라와 함께 돌아간다.
    // home은 씬에 놓인 방향, battle은 이번 판의 방향, from은 섞기를 시작한 순간의 방향이다.
    Light sun;
    Quaternion sunHome;
    Quaternion sunBattle;
    Quaternion sunFrom;
    // 섞는 동안 끈 SSAO. 원근과 직교를 섞은 투영에서는 깊이를 잘못 풀어서 끈다. 프로젝트 에셋 값이라 꼭 다시 켠다.
    readonly List<ScriptableRendererFeature> pausedOcclusion = new List<ScriptableRendererFeature>();
    UniversalRenderPipelineAsset pipeline;
    float originalShadowDistance;
    // 시작 화면·대기실에서 쓰는 그림자 거리. 멀리서 내려다볼 때는 기본 거리로는 그림자가 안 생겨서 늘린다.
    float backdropShadowDistance;
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
        follow = viewCamera.GetComponent<CameraFollow>();
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null) originalShadowDistance = pipeline.shadowDistance;
        backdropShadowDistance = originalShadowDistance;
        originalFieldOfView = viewCamera.fieldOfView;
        originalOrthographic = viewCamera.orthographic;
        sun = RenderSettings.sun;
        if (sun != null) sunHome = sun.transform.rotation;
        sunBattle = sunHome;
        ShowTitle(titleSettings, false);
    }

    void OnDisable()
    {
        EndBlend();
        SetSun(sunHome);
        // 그림자 거리와 SSAO는 프로젝트 에셋 값이라 꺼질 때 꼭 되돌린다.
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
    // 전투로 내려가던 중이었다면 지금 보이는 자리에서 시작 화면 구도로 빠져나오고, 빠져나오던 중이면 목표만 시작 화면으로 바뀐다.
    public void ShowTitle(DuelSettings settings, bool keepMap)
    {
        titleSettings = settings.Clone();
        preview = false;
        if (map == null) return;
        // 시작 화면에서는 아무도 따라가지 않는다. 디스폰이 늦게 와도 카메라를 두고 다투지 않게 먼저 끈다.
        if (follow != null)
        {
            follow.enabled = false;
            follow.Held = false;
        }
        if (blend == Blend.Enter) StartBlend(Blend.Exit, Shown);
        // 전투에서 돌아오면 감췄던 맵 자리에 새 맵을 솟게 하고, 카메라는 시작 화면 거리로 바로 옮긴다.
        if (released || !map.gameObject.activeSelf) keepMap = false;
        if (released)
        {
            RestoreProjection();
            SetSun(sunHome);
        }
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
    // 부시만 달라졌으면(부시 밀도만 바꿨을 때) 벽과 바닥은 지금 높이 그대로 두고 새로 생긴 부시만 땅에서 솟게 한다.
    public bool ShowPreview(MapGenerationSettings settings)
    {
        preview = true;
        StopAllCoroutines();
        MapLayout before = map.GeneratedLayout;
        var groundDrops = new Dictionary<Vector3, float>();
        var bushDrops = new Dictionary<Vector3, float>();
        RecordDrops(groundDrops, bushDrops);
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

        if (before != null && SameGround(before, map.GeneratedLayout))
        {
            IReadOnlyList<Transform> pieces = map.Pieces;
            for (int p = 0; p < pieces.Count; p++)
            {
                bool bush = IsBush(pieces[p]);
                // 전에 없던 부시는 땅속에서 출발한다. 같은 자리에 있던 조각은 하던 높이에서 이어 간다.
                if (!(bush ? bushDrops : groundDrops).TryGetValue(homes[p], out float drop))
                    drop = bush ? -sinkDepth : 0f;
                pieces[p].localPosition = homes[p] + Vector3.up * drop;
            }
            StartCoroutine(Settle());
        }
        else StartCoroutine(Wave(true));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MapLayout layout = map.GeneratedLayout;
        uint signature = 2166136261;
        foreach (string row in layout.ToLayout())
            foreach (char cell in row) signature = unchecked((signature ^ cell) * 16777619);
        Debug.Log($"[LobbyPreview] seed={settings.Seed} shape={layout.Shape} size={layout.Size} spawnA={layout.SpawnA} spawnB={layout.SpawnB} hash={signature:X8}");
#endif
        return true;
    }

    // 지금 맵의 조각마다 제자리에서 얼마나 내려가 있는지 제자리 좌표로 적는다. 부시는 같은 칸의 바닥과 제자리가 겹칠 수 있어 따로 적는다.
    void RecordDrops(Dictionary<Vector3, float> ground, Dictionary<Vector3, float> bushes)
    {
        IReadOnlyList<Transform> pieces = map.Pieces;
        if (homes == null || homes.Length != pieces.Count) return;
        for (int p = 0; p < pieces.Count; p++)
        {
            if (pieces[p] == null) continue;
            (IsBush(pieces[p]) ? bushes : ground)[homes[p]] = pieces[p].localPosition.y - homes[p].y;
        }
    }

    static bool IsBush(Transform piece) => piece.TryGetComponent(out BushBlock _);

    // 부시를 바닥으로 보면 두 맵이 칸마다 같은지. 부시 밀도만 바뀐 맵은 벽, 바닥, 스폰이 그대로라 true다.
    static bool SameGround(MapLayout a, MapLayout b)
    {
        if (a.Size != b.Size || a.SpawnA != b.SpawnA || a.SpawnB != b.SpawnB) return false;
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                if (Ground(a[x, y]) != Ground(b[x, y])) return false;
        return true;
    }

    static MapCell Ground(MapCell cell) => cell == MapCell.Bush ? MapCell.Floor : cell;

    // 전투 준비가 시작되면 같은 자리에 전투 맵이 서므로 미리보기 맵을 감춘다. 카메라는 Dive를 부를 때까지 그대로 둔다.
    // 물결 도중에 감추더라도 조각을 제자리에 세워 둔다. 화면에는 보이지 않고, 전투 뒤 다시 켤 때 전투 맵과 같은 높이로 선다.
    public void HideMap()
    {
        StopAllCoroutines();
        if (map != null)
        {
            IReadOnlyList<Transform> pieces = map.Pieces;
            if (homes != null && homes.Length == pieces.Count)
                for (int p = 0; p < pieces.Count; p++)
                    if (pieces[p] != null) pieces[p].localPosition = homes[p];
            map.gameObject.SetActive(false);
        }
        if (background != null) background.gameObject.SetActive(false);
    }

    // HideMap으로 전투 맵을 드러낸 뒤 부른다. 카메라가 지금 자리에서 내 스폰 지점으로 내려가 전투 카메라가 된다.
    // 내 플레이어가 그사이 소환되면 그 플레이어를 겨누고, 다 내려가면 따라가는 카메라에 그대로 넘겨준다.
    // 내려가면서 상대 스폰이 화면 위쪽에 오는 전투 카메라 방향(DuelCameraSettings.FacingYaw)으로 돌고,
    // 광원도 두 스폰을 잇는 선에 직교하는 방향(DuelAxis.SideLight)으로 함께 돈다.
    // arena는 이 클라이언트에 전투 맵을 세운 곳이다. 맵이 없으면 같은 설정으로 같은 자리에 섰던 미리보기 맵의 스폰을 쓴다.
    public void Dive(DuelArena arena, int slot)
    {
        if (released) return;
        Transform[] spawns = arena != null && arena.SpawnPoints != null ? arena.SpawnPoints
            : map != null ? map.SpawnPoints : null;
        sunBattle = DuelAxis.TryYaw(spawns, out float axis) ? DuelAxis.SideLight(axis, sunHome) : sunHome;
        diveTarget = spawns != null && slot >= 0 && slot < spawns.Length ? spawns[slot] : null;
        bool following = follow != null && follow.enabled && follow.player != null;
        if (follow == null || battleCamera == null || (diveTarget == null && !following))
        {
            // 겨눌 곳이 없으면 예전처럼 바로 넘긴다.
            ReleaseCamera();
            return;
        }
        // 내 플레이어가 먼저 소환되었으면 DuelPlayer가 같은 스폰으로 이미 정해 두었다. 같은 값이라 덮어써도 된다.
        if (battleCamera.TryFacingYaw(spawns, slot, out float yaw)) follow.Yaw = yaw;
        lastTarget = following ? follow.player.position : diveTarget.position;
        follow.Held = true;
        StartBlend(Blend.Enter, Shown);
    }

    // 결과를 보여 준 뒤 로비 화면으로 돌아올 때 부른다. 내 플레이어를 따라가던 카메라를 되찾고, HideMap으로 꺼 두었던
    // 미리보기 맵을 다시 켠다. 전투 맵이 같은 설정으로 같은 자리에 서 있었으므로 바꿔 끼워도 티가 나지 않는다.
    // 카메라는 따라가던 자리(또는 내려가던 도중의 자리)에서 대기실 구도로 빠져나온다. 도착할 대기실 자리는
    // 이어서 부르는 LobbyStage.Show(HoldPreview)가 정한다.
    public void RetakeCamera()
    {
        // 플레이어 디스폰이 늦게 와도 따라가던 카메라와 겹치지 않게 먼저 끈다.
        if (follow != null) follow.enabled = false;
        if (released)
        {
            // 따라가던 카메라는 FollowPose 자리에 있으므로 그 자리와 방향에서 내 플레이어 위치를 거꾸로 구한다.
            float yaw = viewCamera.transform.eulerAngles.y;
            Vector3 target = battleCamera.TargetFromCamera(viewCamera.transform.position, yaw);
            RestoreProjection();
            released = false;
            StartBlend(Blend.Exit, battleCamera.View(target, yaw, originalShadowDistance));
        }
        else if (blend == Blend.Enter) StartBlend(Blend.Exit, Shown);
        if (follow != null) follow.Held = false;
        snapDistance = true;
        if (map == null) return;
        map.gameObject.SetActive(true);
        if (background != null) background.gameObject.SetActive(true);
    }

    // 다 내려왔거나 겨눌 곳이 없을 때 카메라를 넘긴다. 렌즈 이동과 늘려 둔 그림자 거리를 되돌리고 투영과 안개를 전투에 맞춘다.
    void ReleaseCamera()
    {
        released = true;
        EndBlend();
        StopAllCoroutines();
        SetSun(sunBattle);
        if (pipeline != null) pipeline.shadowDistance = originalShadowDistance;
        battleCamera.Apply(viewCamera);
        viewCamera.ResetProjectionMatrix();
    }

    // 지난 프레임에 그린 장면. 아직 한 번도 그리지 않았으면 지금 배경 장면이다.
    CameraView Shown => shown.Distance > 0f ? shown : BackdropView();

    void StartBlend(Blend kind, CameraView start)
    {
        blend = kind;
        from = start;
        if (sun != null) sunFrom = sun.transform.rotation;
        targetYaw = start.Yaw;
        blendTime = 0f;
        // 전투 맵을 만드느라 멈췄던 프레임의 시간으로 카메라가 한 번에 건너뛰지 않게, 다음 프레임부터 센다.
        blendCounting = false;
        PauseOcclusion();
    }

    void EndBlend()
    {
        blend = Blend.None;
        if (follow != null) follow.Held = false;
        ResumeOcclusion();
    }

    void SetSun(Quaternion rotation)
    {
        if (sun != null) sun.transform.rotation = rotation;
    }

    void PauseOcclusion()
    {
        if (pausedOcclusion.Count > 0 || pipeline == null) return;
        foreach (ScriptableRendererData data in pipeline.rendererDataList)
        {
            if (data == null) continue;
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (!(feature is ScreenSpaceAmbientOcclusion) || !feature.isActive) continue;
                feature.SetActive(false);
                pausedOcclusion.Add(feature);
            }
        }
    }

    void ResumeOcclusion()
    {
        foreach (ScriptableRendererFeature feature in pausedOcclusion)
            if (feature != null) feature.SetActive(true);
        pausedOcclusion.Clear();
    }

    // 시작 화면 또는 대기실의 지금 장면.
    CameraView BackdropView() => CameraView.Perspective(center, anchor, yaw, pitch, distance, originalFieldOfView,
        new Vector2(distance - 6f, distance + 90f), backdropShadowDistance);

    // 전투로 내려가는 도착 장면. 내 플레이어가 소환되어 있으면 그 자리를, 아니면 스폰 지점을 겨눈다.
    CameraView BattleView()
    {
        if (follow.enabled && follow.player != null) lastTarget = follow.player.position;
        else if (diveTarget != null) lastTarget = diveTarget.position;
        return battleCamera.View(lastTarget, follow.Yaw, originalShadowDistance);
    }

    // 다 내려왔다. 따라가는 카메라와 같은 자리에 놓고 넘긴다. 내 플레이어가 아직이면 소환될 때 같은 자리에서 이어받는다.
    void FinishDive()
    {
        Pose pose = battleCamera.FollowPose(lastTarget, follow.Yaw);
        ReleaseCamera();
        viewCamera.transform.SetPositionAndRotation(pose.position, pose.rotation);
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
        // 멀리서 내려다보면 기본 그림자 거리로는 그림자가 안 생겨서 잠시 늘린다. 실제 적용은 LateUpdate가 한다.
        backdropShadowDistance = Mathf.Max(originalShadowDistance, targetDistance + radius * 1.5f);
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
        float smoothing = 1f - Mathf.Exp(-viewSharpness * Time.deltaTime);
        if (preview) yaw = Mathf.LerpAngle(yaw, previewYaw + Mathf.Sin(Time.time * 0.15f) * previewSway, smoothing);
        else yaw += orbitSpeed * Time.deltaTime;
        distance = Mathf.Lerp(distance, targetDistance, smoothing);
        anchor = Vector2.Lerp(anchor, preview ? previewAnchor : screenAnchor, smoothing);

        // 렌즈를 옮겨 맵 중심이 화면 가운데가 아닌 anchor에 오게 한다(CameraView.Projection).
        CameraView view = BackdropView();
        if (blend != Blend.None)
        {
            if (blendCounting) blendTime = Mathf.Min(1f, blendTime + Time.unscaledDeltaTime / diveDuration);
            blendCounting = true;
            // 전투로 내려갈 때는 내 플레이어 쪽으로, 빠져나올 때는 지금 시작 화면·대기실 장면으로 간다.
            CameraView to = blend == Blend.Enter ? BattleView() : view;
            targetYaw += Mathf.DeltaAngle(targetYaw, to.Yaw);
            to.Yaw = targetYaw;
            if (blendTime >= 1f)
            {
                if (blend == Blend.Enter)
                {
                    FinishDive();
                    return;
                }
                EndBlend();
                SetSun(sunHome);
            }
            else
            {
                float t = diveCurve.Evaluate(blendTime);
                view = CameraView.Lerp(from, to, t);
                // 해도 카메라와 같은 빠르기로 하늘을 따라 옆으로 돈다. 고도는 그대로다.
                SetSun(DuelAxis.TurnLight(sunFrom, blend == Blend.Enter ? sunBattle : sunHome, t));
            }
        }

        shown = view;
        view.ApplyTo(viewCamera);
        RenderSettings.fogStartDistance = view.Fog.x;
        RenderSettings.fogEndDistance = view.Fog.y;
        if (pipeline != null) pipeline.shadowDistance = view.ShadowDistance;
    }
}
