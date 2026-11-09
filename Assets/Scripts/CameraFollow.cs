using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;

    [Tooltip("Main 전투와 DuelOfflineTest가 함께 쓰는 전투 카메라 설정. 지정하면 아래 높이·거리 값 대신 이 에셋 값을 쓴다.")]
    [SerializeField] DuelCameraSettings settings;
    public DuelCameraSettings Settings => settings;

    // 카메라 높이 (settings가 비어 있을 때)
    public float height = 10f;

    // 플레이어와 카메라 사이 거리 (settings가 비어 있을 때)
    public float distance = 8f;

    // 카메라가 바라보는 높이 (settings가 비어 있을 때)
    public float lookHeight = 1f;

    void LateUpdate()
    {
        float followHeight = settings != null ? settings.Height : height;
        float followDistance = settings != null ? settings.Distance : distance;
        float followLookHeight = settings != null ? settings.LookHeight : lookHeight;

        // 카메라가 바라볼 기준점
        Vector3 lookPosition =
            player.position + Vector3.up * followLookHeight;

        // 고정된 방향에서 플레이어를 따라감
        Vector3 cameraPosition =
            player.position
            + new Vector3(0f, followHeight, -followDistance);

        transform.position = cameraPosition;

        // 플레이어를 바라봄
        transform.LookAt(lookPosition);
    }
}
