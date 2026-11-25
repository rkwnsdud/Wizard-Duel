using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;

    [Tooltip("Main 전투와 DuelOfflineTest가 함께 쓰는 전투 카메라 설정. 지정하면 아래 높이·거리 값 대신 이 에셋 값을 쓴다.")]
    [SerializeField] DuelCameraSettings settings;
    public DuelCameraSettings Settings => settings;

    // 다른 연출(TitleBackdrop이 로비에서 전투로 카메라를 옮기는 동안)이 카메라를 쥐고 있다.
    // 따라갈 플레이어는 받아 두되 카메라는 움직이지 않고, 연출이 끝나면 같은 자리에서 이어받는다.
    public bool Held { get; set; }

    // 카메라가 바라보는 방향(도). 0이면 +Z를 본다. 전투에서는 상대 스폰이 화면 위쪽에 오도록
    // DuelCameraSettings.FacingYaw로 정하고, 한 판 동안 돌지 않는다.
    public float Yaw { get; set; }

    // 카메라 높이 (settings가 비어 있을 때)
    public float height = 10f;

    // 플레이어와 카메라 사이 거리 (settings가 비어 있을 때)
    public float distance = 8f;

    // 카메라가 바라보는 높이 (settings가 비어 있을 때)
    public float lookHeight = 1f;

    // 순간 이동 뒤 카메라가 뒤쫓는 시간 (settings가 비어 있을 때)
    public float teleportChaseTime = 0.35f;

    // 따라가는 지점 - 플레이어 발밑. 순간 이동하면 이전 지점을 가리키게 두었다가 0으로 줄여 뒤쫓는다.
    // 플레이어 자리가 아니라 이 차이를 줄이므로, 도착한 뒤 계속 걸어도 뒤쫓기가 제때 끝난다.
    Vector3 chaseOffset;
    Vector3 chaseVelocity;

    public bool Chasing => chaseOffset != Vector3.zero;

    // 따라가던 플레이어가 from에서 지금 자리로 순간 이동했다. 화면이 from에서 미끄러지듯 따라온다.
    public void ChaseFrom(Vector3 from)
    {
        if (player == null) return;
        chaseOffset += from - player.position;
        chaseOffset.y = 0f;
    }

    // 뒤쫓기를 멈추고 바로 플레이어 위에 선다.
    public void StopChase()
    {
        chaseOffset = chaseVelocity = Vector3.zero;
    }

    void OnDisable() => StopChase();

    void LateUpdate()
    {
        if (Held || player == null)
        {
            StopChase();
            return;
        }

        Vector3 focus = player.position + Chase(ref chaseOffset, ref chaseVelocity,
            settings != null ? settings.TeleportChaseTime : teleportChaseTime, Time.deltaTime);
        // 고정된 방향에서 플레이어를 따라가며 플레이어를 바라본다.
        Pose pose = settings != null
            ? settings.FollowPose(focus, Yaw)
            : DuelCameraSettings.FollowPose(focus, height, distance, lookHeight, Yaw);
        transform.SetPositionAndRotation(pose.position, pose.rotation);
    }

    // offset을 smoothTime 동안 0으로 줄이고 줄인 값을 돌려준다. 1cm 안으로 들어오면 끝낸다.
    public static Vector3 Chase(ref Vector3 offset, ref Vector3 velocity, float smoothTime, float deltaTime)
    {
        if (offset == Vector3.zero) return offset;
        if (smoothTime <= 0f)
        {
            offset = velocity = Vector3.zero;
            return offset;
        }
        if (deltaTime <= 0f) return offset;
        offset = Vector3.SmoothDamp(offset, Vector3.zero, ref velocity, smoothTime, Mathf.Infinity, deltaTime);
        if (offset.sqrMagnitude < 0.0001f) offset = velocity = Vector3.zero;
        return offset;
    }
}
