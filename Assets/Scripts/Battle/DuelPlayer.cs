using Unity.Netcode;
using UnityEngine;

// Main 전투의 플레이어. 이동·조준·공격·CharacterController는 프리팹에서 꺼 두고,
// 서버가 전투를 켜면 내 플레이어(소유자)만 켠다. 상대는 받은 이동에 맞춰 걷기 애니메이션만 재생한다.
public sealed class DuelPlayer : NetworkBehaviour
{
    // 0 = 방장(파랑), 1 = 참가자(빨강). DuelWizard-2 프리팹에서 1로 덮어쓴다.
    [SerializeField] int slot;
    // 소환 이펙트는 로비 화면의 모형 마법사(LobbyStage)와 같은 값으로 재생한다.
    [SerializeField, Min(0f)] float spawnEffectDuration = 1.35f;
    [SerializeField, Min(0.1f)] float spawnRuneRadius = 1.35f;
    [SerializeField] Color blueSpawnColor = new Color(0.08f, 1.4f, 2.2f, 1f);
    [SerializeField] Color redSpawnColor = new Color(2.2f, 0.22f, 0.035f, 1f);

    public float SpawnEffectDuration => spawnEffectDuration;

    readonly NetworkVariable<bool> combatEnabled = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
        combatEnabled.OnValueChanged += OnCombatEnabledChanged;
        if (IsClient) PlaySpawnEffect();
        if (IsOwner) FollowWithCamera();
        // 상대 플레이어는 내 입력을 읽으면 안 되므로 항상 꺼 둔다.
        ApplyCombatState(IsOwner && combatEnabled.Value);
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log($"[Player] Despawned slot={slot} owner={OwnerClientId}.");
        combatEnabled.OnValueChanged -= OnCombatEnabledChanged;
        SpawnEffectController.Stop(slot);
        // 파괴될 플레이어를 계속 따라가지 않도록 카메라 추적을 끈다.
        if (cameraFollow != null) cameraFollow.enabled = false;
    }

    public void ServerEnableCombat()
    {
        if (IsServer && IsSpawned) combatEnabled.Value = true;
    }

    void PlaySpawnEffect()
    {
        SpawnEffectController.Play(transform.position, slot, spawnEffectDuration, spawnRuneRadius,
            0.06f, 240f, 2.1f, 36, blueSpawnColor, redSpawnColor);
    }

    void FollowWithCamera()
    {
        cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cameraFollow == null) return;
        cameraFollow.player = transform;
        cameraFollow.enabled = true;
    }

    void OnCombatEnabledChanged(bool previous, bool current)
    {
        Debug.Log($"[Player] Combat {(current ? "enabled" : "disabled")} slot={slot} owner={OwnerClientId} local={IsOwner} position={transform.position}.");
        if (IsOwner) ApplyCombatState(current);
    }

    void ApplyCombatState(bool enabled)
    {
        // CharacterController는 소유자만 켜고 끈다. 상대는 프리팹에서 꺼 둔 그대로 NetworkTransform을 따른다.
        if (IsOwner && TryGetComponent(out CharacterController controller)) controller.enabled = enabled;
        if (TryGetComponent(out PlayerMovement movement)) movement.enabled = enabled;
        if (TryGetComponent(out PlayerAim aim)) aim.enabled = enabled;
        if (TryGetComponent(out PlayerAttack attack)) attack.enabled = enabled;
    }

    void LateUpdate()
    {
        if (!IsSpawned || IsOwner) return;
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;
        bool isMoving = displacement.sqrMagnitude > 0.00001f;
        float directionDampTime = animator.GetFloat("MoveSpeed") > 0f ? 0.1f : 0f;
        animator.SetFloat("MoveSpeed", isMoving ? 5f : 0f);
        Vector3 localDirection = isMoving ? transform.InverseTransformDirection(displacement.normalized) : Vector3.zero;
        animator.SetFloat("MoveX", localDirection.x, directionDampTime, Time.deltaTime);
        animator.SetFloat("MoveZ", localDirection.z, directionDampTime, Time.deltaTime);
        previousPosition = transform.position;
    }
}
