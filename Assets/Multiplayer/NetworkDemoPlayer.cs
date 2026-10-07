using Unity.Netcode;
using UnityEngine;

// 이동·조준·공격·CharacterController는 프리팹에서 꺼 두고, 내 플레이어(소유자)만 켠다.
// 상대 플레이어는 NetworkTransform으로 받은 이동에 맞춰 걷기 애니메이션만 재생한다.
public class NetworkDemoPlayer : NetworkBehaviour
{
    // 0 = Player-1(파랑), 1 = Player-2(빨강). Player-2 변형 프리팹에서 1로 덮어쓴다.
    [SerializeField] int slot;

    public int Slot => slot;

    Animator animator;
    CameraFollow cameraFollow;
    Vector3 previousPosition;

    void Awake()
    {
        animator = transform.Find("Wizard").GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Player] Spawned slot={slot} owner={OwnerClientId} local={IsOwner} position={transform.position} rotation={transform.rotation}.");
        name = $"Player-{slot + 1}" + (IsOwner ? " (Me)" : "");
        previousPosition = transform.position;
        if (!IsOwner) return;

        GetComponent<CharacterController>().enabled = true;
        GetComponent<PlayerMovement>().enabled = true;
        GetComponent<PlayerAim>().enabled = true;
        GetComponent<PlayerAttack>().enabled = true;
        cameraFollow = Camera.main.GetComponent<CameraFollow>();
        cameraFollow.player = transform;
        cameraFollow.enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log($"[Player] Despawned slot={slot} owner={OwnerClientId}.");
        // 파괴될 플레이어를 계속 따라가지 않도록 카메라 추적을 끈다.
        if (cameraFollow != null) cameraFollow.enabled = false;
    }

    void LateUpdate()
    {
        if (!IsSpawned || IsOwner) return;
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;
        bool isMoving = displacement.sqrMagnitude > 0.00001f;
        animator.SetFloat("MoveSpeed", isMoving ? 5f : 0f);
        Vector3 localDirection = isMoving ? transform.InverseTransformDirection(displacement.normalized) : Vector3.zero;
        animator.SetFloat("MoveX", localDirection.x, 0.1f, Time.deltaTime);
        animator.SetFloat("MoveZ", localDirection.z, 0.1f, Time.deltaTime);
        previousPosition = transform.position;
    }
}
