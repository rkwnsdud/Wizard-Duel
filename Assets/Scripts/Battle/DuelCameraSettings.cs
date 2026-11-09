using UnityEngine;

// Main 전투와 DuelOfflineTest 씬이 함께 쓰는 전투 카메라 값. 이 에셋을 고치면 두 씬의 전투 화면이 같이 바뀐다.
// 따라가는 위치는 CameraFollow가, 시야각과 안개는 카메라를 전투에 넘길 때(TitleBackdrop.ReleaseCamera 등) Apply로 맞춘다.
public sealed class DuelCameraSettings : ScriptableObject
{
    [Tooltip("카메라를 내 플레이어에게 넘긴 뒤의 시야각. 배경용 좁은 시야각으로는 주변이 거의 안 보인다.")]
    [SerializeField, Range(20f, 90f)] float fieldOfView = 60f;
    [Tooltip("전투 중 안개가 시작되고 끝나는 거리. 가까운 곳은 맑고 맵 끝만 살짝 흐려진다.")]
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

    // 전투 시야각과 안개를 맞춘다.
    public void Apply(Camera camera)
    {
        camera.fieldOfView = fieldOfView;
        RenderSettings.fogStartDistance = fog.x;
        RenderSettings.fogEndDistance = fog.y;
    }
}
