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

    // 카메라 높이 (settings가 비어 있을 때)
    public float height = 10f;

    // 플레이어와 카메라 사이 거리 (settings가 비어 있을 때)
    public float distance = 8f;

    // 카메라가 바라보는 높이 (settings가 비어 있을 때)
    public float lookHeight = 1f;

    void LateUpdate()
    {
        if (Held || player == null) return;

        // 고정된 방향에서 플레이어를 따라가며 플레이어를 바라본다.
        Pose pose = settings != null
            ? settings.FollowPose(player.position)
            : DuelCameraSettings.FollowPose(player.position, height, distance, lookHeight);
        transform.SetPositionAndRotation(pose.position, pose.rotation);
    }
}
