using Unity.Netcode;
using UnityEngine;

// 이동·조준·공격·CharacterController는 프리팹에서 꺼 두고, 내 플레이어(소유자)만 켠다.
// 상대 플레이어는 NetworkTransform으로 받은 이동에 맞춰 걷기 애니메이션만 재생한다.
public class NetworkDemoPlayer : NetworkBehaviour
{
    // 0 = Player-1(파랑), 1 = Player-2(빨강). Player-2 변형 프리팹에서 1로 덮어쓴다.
    [SerializeField] int slot;
    [SerializeField, Min(0f)] float spawnEffectDuration = 1.35f;
    [SerializeField, Min(0.1f)] float spawnRuneRadius = 1.35f;
    [SerializeField, Min(0f)] float spawnFloorOffset = 0.06f;
    [SerializeField, Min(0f)] float spawnRuneRotationSpeed = 240f;
    [SerializeField, Min(0f)] float spawnParticleRiseSpeed = 2.1f;
    [SerializeField, Min(1)] int spawnParticleCount = 36;
    [SerializeField] Color blueSpawnColor = new Color(0.08f, 1.4f, 2.2f, 1f);
    [SerializeField] Color redSpawnColor = new Color(2.2f, 0.22f, 0.035f, 1f);

    public int Slot => slot;
    public float SpawnEffectDuration => spawnEffectDuration;
    public bool CombatEnabled => combatEnabled.Value;

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
        if (IsClient)
        {
            SpawnEffectController.Play(transform.position, slot, spawnEffectDuration, spawnRuneRadius,
                spawnFloorOffset, spawnRuneRotationSpeed, spawnParticleRiseSpeed, spawnParticleCount,
                blueSpawnColor, redSpawnColor);
        }

        if (IsOwner)
        {
            if (IsLocalPlayer && TryGetComponent(out SecondaryWeaponHud hud)) hud.ShowForLocalOwner();
            cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cameraFollow != null)
            {
                cameraFollow.player = transform;
                cameraFollow.enabled = true;
            }
            ApplyCombatState(combatEnabled.Value);
        }
        else
        {
            // Player prefab variants inherit enabled input components; remote proxies must never read local input.
            ApplyCombatState(false);
        }
    }

    public void ServerEnableCombat()
    {
        if (IsServer && IsSpawned) combatEnabled.Value = true;
    }

    void OnCombatEnabledChanged(bool previous, bool current)
    {
        if (IsOwner) ApplyCombatState(current);
    }

    void ApplyCombatState(bool enabled)
    {
        CharacterController controller = GetComponent<CharacterController>();
        PlayerMovement movement = GetComponent<PlayerMovement>();
        PlayerAim aim = GetComponent<PlayerAim>();
        PlayerAttack attack = GetComponent<PlayerAttack>();
        // Keep proxy colliders available for local attacks and overlap/raycast hit detection.
        if (controller != null && IsOwner) controller.enabled = enabled;
        if (movement != null) movement.enabled = enabled;
        if (aim != null) aim.enabled = enabled;
        if (attack != null) attack.enabled = enabled;
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log($"[Player] Despawned slot={slot} owner={OwnerClientId}.");
        if (TryGetComponent(out SecondaryWeaponHud hud)) hud.Hide();
        combatEnabled.OnValueChanged -= OnCombatEnabledChanged;
        SpawnEffectController.Stop(slot);
        // 파괴될 플레이어를 계속 따라가지 않도록 카메라 추적을 끈다.
        if (cameraFollow != null) cameraFollow.enabled = false;
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
