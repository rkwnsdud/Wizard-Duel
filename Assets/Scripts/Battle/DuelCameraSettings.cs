using UnityEngine;

// Main 전투와 DuelOfflineTest 씬이 함께 쓰는 전투 카메라 값. 이 에셋을 고치면 두 씬의 전투 화면이 같이 바뀐다.
// 따라가는 위치는 CameraFollow가, 투영과 안개는 카메라를 전투에 넘길 때(TitleBackdrop.ReleaseCamera 등) Apply로 맞춘다.
// 로비에서 전투로 내려가는 카메라도 View와 FollowPose로 같은 자리를 겨눠, 넘겨받는 프레임에 화면이 튀지 않는다.
public sealed class DuelCameraSettings : ScriptableObject
{
    [Tooltip("켜면 원근 없이 내려다봐서 화면에 보이는 바닥이 직사각형이 된다. 끄면 아래 시야각으로 원근 카메라를 쓴다.")]
    [SerializeField] bool orthographic = true;
    [Tooltip("직교일 때 화면 아래 끝에서 위 끝까지 보이는 바닥 길이(앞뒤). 가로 길이는 화면 비율을 따른다. 값이 작을수록 크게 보인다.")]
    [SerializeField, Min(1f)] float viewDepth = 19f;
    [Tooltip("원근일 때의 시야각. 배경용 좁은 시야각으로는 주변이 거의 안 보인다.")]
    [SerializeField, Range(20f, 90f)] float fieldOfView = 60f;
    [Tooltip("전투 중 안개가 시작되고 끝나는 거리. 가까운 곳은 맑고 맵 끝만 살짝 흐려진다. URP 안개는 원근 기준이라 직교에서는 거의 보이지 않는다.")]
    [SerializeField] Vector2 fog = new Vector2(40f, 160f);
    [Tooltip("플레이어 위로 카메라를 올리는 높이.")]
    [SerializeField] float height = 10f;
    [Tooltip("플레이어 뒤(-Z)로 카메라를 빼는 거리.")]
    [SerializeField] float distance = 8f;
    [Tooltip("카메라가 바라보는 지점의 플레이어 발밑 기준 높이.")]
    [SerializeField] float lookHeight = 1f;

    public float Height => height;
    public float Distance => distance;
    public float LookHeight => lookHeight;

    // 카메라에서 바라보는 지점까지의 거리.
    public float FollowDistance => Mathf.Max(0.01f, new Vector2(height - lookHeight, distance).magnitude);
    // 내려다보는 각도(도).
    public float Pitch => Mathf.Atan2(height - lookHeight, distance) * Mathf.Rad2Deg;
    // 내려다보는 각도만큼 바닥이 화면 세로로 줄어들어 보이므로, 바닥 길이에 그 비율을 곱해 화면 절반 높이로 바꾼다.
    public float OrthographicSize => viewDepth * 0.5f * (height - lookHeight) / FollowDistance;

    // 전투 투영과 안개를 맞춘다.
    public void Apply(Camera camera)
    {
        camera.orthographic = orthographic;
        camera.fieldOfView = fieldOfView;
        camera.orthographicSize = OrthographicSize;
        RenderSettings.fogStartDistance = fog.x;
        RenderSettings.fogEndDistance = fog.y;
    }

    // target(플레이어 발밑)을 따라가는 카메라 자리.
    public Pose FollowPose(Vector3 target) => FollowPose(target, height, distance, lookHeight);

    // 설정 에셋 없이 따라가는 카메라(CameraFollow의 직접 입력값)도 같은 식을 쓴다.
    public static Pose FollowPose(Vector3 target, float height, float distance, float lookHeight)
    {
        Vector3 position = target + new Vector3(0f, height, -distance);
        Vector3 look = target + Vector3.up * lookHeight;
        return new Pose(position, Quaternion.LookRotation(look - position));
    }

    // FollowPose 자리에 있는 카메라가 따라가던 지점(플레이어 발밑).
    public Vector3 TargetFromCamera(Vector3 cameraPosition) => cameraPosition - new Vector3(0f, height, -distance);

    // target을 따라가는 전투 화면을 장면으로 적는다. 카메라 자리는 FollowPose와 같다.
    // 그림자 거리는 전투에서 쓰는 프로젝트 값을 받는다.
    public CameraView View(Vector3 target, float shadowDistance)
    {
        Vector3 focus = target + Vector3.up * lookHeight;
        Vector2 center = new Vector2(0.5f, 0.5f);
        return orthographic
            ? CameraView.Orthographic(focus, center, 0f, Pitch, FollowDistance, OrthographicSize, fog, shadowDistance)
            : CameraView.Perspective(focus, center, 0f, Pitch, FollowDistance, fieldOfView, fog, shadowDistance);
    }
}
