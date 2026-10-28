using System;
using Unity.Netcode;
using UnityEngine;

// Main 전투의 플레이어. 서버가 전투를 켜면 소유자만 입력을 읽는다.
// 공격과 명중은 소유자가 판정한다. 서버는 보고받은 피해와 패배만 관리한다.
public sealed class DuelPlayer : NetworkBehaviour, IAttackAuthority, IShotSink
{
    // 0 = 방장(파랑), 1 = 참가자(빨강). DuelWizard-2 프리팹에서 1로 덮어쓴다.
    [SerializeField] int slot;
    // 소환 이펙트는 로비 화면의 모형 마법사(LobbyStage)와 같은 값으로 재생한다.
    [SerializeField, Min(0f)] float spawnEffectDuration = 1.35f;
    [SerializeField, Min(0.1f)] float spawnRuneRadius = 1.35f;
    [SerializeField] Color blueSpawnColor = new Color(0.08f, 1.4f, 2.2f, 1f);
    [SerializeField] Color redSpawnColor = new Color(2.2f, 0.22f, 0.035f, 1f);

    public float SpawnEffectDuration => spawnEffectDuration;
    public int Slot => slot;
    // 서버에서 체력이 0이 되는 순간 한 번 불린다.
    public event Action<DuelPlayer> Defeated;

    readonly NetworkVariable<bool> combatEnabled = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 서버가 OnNetworkSpawn에서 최대 체력으로 채운 뒤 스폰 데이터와 함께 보낸다.
    readonly NetworkVariable<float> health = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    Animator animator;
    PlayerAttack attack;
    PlayerHealth playerHealth;
    CameraFollow cameraFollow;
    Vector3 previousPosition;
    // 서버에만 있다.
    DuelCombatState combat;
    uint attackId;

    IWeapon Weapon => attack.Weapon as IWeapon;

    void Awake()
    {
        animator = transform.Find("Wizard").GetComponent<Animator>();
        attack = GetComponent<PlayerAttack>();
        playerHealth = GetComponent<PlayerHealth>();
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Player] Spawned slot={slot} owner={OwnerClientId} local={IsOwner} position={transform.position} rotation={transform.rotation}.");
        name = $"Player-{slot + 1}" + (IsOwner ? " (Me)" : "");
        previousPosition = transform.position;
        attack.Authority = this;
        if (IsServer)
        {
            combat = new DuelCombatState(slot, playerHealth.MaxHealth);
            health.Value = combat.Health;
        }
        else health.OnValueChanged += OnHealthChanged;
        ApplyHealth(health.Value);
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
        health.OnValueChanged -= OnHealthChanged;
        // Authority는 그대로 둔다. 디스폰 뒤 Destroy 전에 남은 애니메이션 이벤트가 오프라인 공격으로 빠지지 않고 아래에서 무시된다.
        SpawnEffectController.Stop(slot);
        // 파괴될 플레이어를 계속 따라가지 않도록 카메라 추적을 끈다.
        if (cameraFollow != null) cameraFollow.enabled = false;
    }

    public void ServerEnableCombat()
    {
        if (!IsServer || !IsSpawned) return;
        combatEnabled.Value = true;
    }

    void IAttackAuthority.StartAttack(float chargeRatio)
    {
        if (IsSpawned && IsOwner) PlayAttackRpc(chargeRatio);
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    void PlayAttackRpc(float chargeRatio)
    {
        if (IsClient) attack.PlayAttack(chargeRatio);
    }

    void IAttackAuthority.FireMagic(float chargeRatio)
    {
        if (!IsSpawned || !IsOwner || !combatEnabled.Value || playerHealth.IsDead) return;
        Physics.SyncTransforms();
        Weapon?.Fire(++attackId, Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, chargeRatio, this);
    }

    void IShotSink.Broadcast(ShotEvent shot)
    {
        if (!IsSpawned || !IsOwner) return;
        Weapon.Present(shot);
        ShotRpc(shot.AttackId, shot.Kind, shot.Start, shot.Point, shot.Charge);
    }

    void IShotSink.Hit(uint attackId, int hitIndex, PlayerHealth target, float damage)
    {
        if (IsSpawned && IsOwner && target.TryGetComponent(out DuelPlayer player))
            ReportHitRpc(player.NetworkObject, attackId, hitIndex, damage);
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    void ShotRpc(uint attackId, byte kind, Vector3 start, Vector3 point, float charge)
    {
        if (IsClient) Weapon?.Present(new ShotEvent(attackId, kind, start, point, charge));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void ReportHitRpc(NetworkObjectReference target, uint attackId, int hitIndex, float damage)
    {
        if (!combatEnabled.Value || !target.TryGet(out NetworkObject obj, NetworkManager) ||
            !obj.TryGetComponent(out DuelPlayer player) ||
            !player.combat.TryTakeHit(slot, attackId, hitIndex, damage, out bool defeatedNow)) return;
        Debug.Log($"[Combat] Hit slot={player.slot} attacker={slot} id={attackId} hit={hitIndex} health={player.combat.Health:F3}");
        player.health.Value = player.combat.Health;
        player.ApplyHealth(player.combat.Health);
        if (defeatedNow) player.Defeated?.Invoke(player);
    }

    void OnHealthChanged(float previous, float current) => ApplyHealth(current);

    // 체력은 줄기만 하므로 서버 체력과의 차이만큼 깎는다. 0이 되면 PlayerHealth가 이동·조준·공격과 애니메이터를 멈춘다.
    void ApplyHealth(float value) => playerHealth.TakeDamage(playerHealth.CurrentHealth - value);

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
        // 상대도 공격자의 물리 판정에 잡히도록 콜라이더를 켜 둔다. 입력은 소유자만 받는다.
        if (TryGetComponent(out CharacterController controller)) controller.enabled = IsClient && (!IsOwner || enabled);
        if (TryGetComponent(out PlayerMovement movement)) movement.enabled = enabled;
        if (TryGetComponent(out PlayerAim aim)) aim.enabled = enabled;
        if (TryGetComponent(out PlayerAttack attack)) attack.enabled = enabled;
    }

    void LateUpdate()
    {
        // 쓰러지면 PlayerHealth가 애니메이터를 끈다.
        if (!IsClient || !IsSpawned || IsOwner || !animator.enabled) return;
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;
        // 시전 중에는 소유자의 PlayerMovement처럼 걷기 값을 0으로 둔다.
        bool isMoving = !attack.IsAttacking && displacement.sqrMagnitude > 0.00001f;
        float directionDampTime = animator.GetFloat("MoveSpeed") > 0f ? 0.1f : 0f;
        animator.SetFloat("MoveSpeed", isMoving ? 5f : 0f);
        Vector3 localDirection = isMoving ? transform.InverseTransformDirection(displacement.normalized) : Vector3.zero;
        animator.SetFloat("MoveX", localDirection.x, directionDampTime, Time.deltaTime);
        animator.SetFloat("MoveZ", localDirection.z, directionDampTime, Time.deltaTime);
        previousPosition = transform.position;
    }
}
