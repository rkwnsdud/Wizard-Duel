using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;

    // 카메라 높이
    public float height = 10f;

    // 플레이어와 카메라 사이 거리
    public float distance = 8f;

    // 카메라가 바라보는 높이
    public float lookHeight = 1f;

    void LateUpdate()
    {
        // 카메라가 바라볼 기준점
        Vector3 lookPosition =
            player.position + Vector3.up * lookHeight;

        // 고정된 방향에서 플레이어를 따라감
        Vector3 cameraPosition =
            player.position
            + new Vector3(0f, height, -distance);

        transform.position = cameraPosition;

        // 플레이어를 바라봄
        transform.LookAt(lookPosition);
    }
}