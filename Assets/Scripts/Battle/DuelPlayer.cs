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
    public bool CanUseInventory => IsSpawned && combatEnabled.Value && !playerHealth.IsDead;
    // 서버에서 체력이 0이 되는 순간 한 번 불린다.
    public event Action<DuelPlayer> Defeated;
    // 서버에만 있다. 이 플레이어가 속한 방의 판. DuelArena가 소환하기 전에 넣는다.
    public DuelMatch Match { get; set; }
    // AI 대전의 봇인지. 봇은 로컬 호스트가 소유하지만 키보드·마우스를 읽지 않고 DuelBot이 움직인다.
    // 이 화면에서는 상대이므로 카메라를 가져가지 않고, 걷기 애니메이션과 부시 노출도 상대 플레이어처럼 처리한다.
    public bool IsBot => bot != null;

    readonly NetworkVariable<bool> combatEnabled = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 서버가 OnNetworkSpawn에서 최대 체력으로 채운 뒤 스폰 데이터와 함께 보낸다.
    readonly NetworkVariable<float> health = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    Animator animator;
    PlayerAttack attack;
    PlayerHealth playerHealth;
    PlayerBushVisibility bushVisibility;
    CameraFollow cameraFollow;
    Vector3 previousPosition;
    // 서버에만 있다.
    DuelCombatState combat;
    uint attackId;
    DuelBot bot;

    IWeapon Weapon => attack.Weapon as IWeapon;

    void Awake()
    {
        animator = transform.Find("Wizard").GetComponent<Animator>();
        attack = GetComponent<PlayerAttack>();
        playerHealth = GetComponent<PlayerHealth>();
        bushVisibility = GetComponent<PlayerBushVisibility>();
    }

    // DuelArena가 봇을 소환하기 전에 부른다.
    public void AttachBot(DuelBot driver) => bot = driver;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[Player] Spawned slot={slot} owner={OwnerClientId} local={IsOwner} position={transform.position} rotation={transform.rotation}.");
        name = $"Player-{slot + 1}" + (IsBot ? " (Bot)" : IsOwner ? " (Me)" : "");
        previousPosition = transform.position;
        attack.Authority = this;
        var arena = FindFirstObjectByType<DuelArena>();
        if (arena != null && TryGetComponent(out PlayerWeaponInventory inventory))
            inventory.NetworkAuthority = arena.Weapons;
        if (IsServer)
        {
            combat = new DuelCombatState(slot, playerHealth.MaxHealth);
            health.Value = combat.Health;
        }
        health.OnValueChanged += OnHealthChanged;
        ApplyHealth(health.Value);
        combatEnabled.OnValueChanged += OnCombatEnabledChanged;
        if (IsClient) PlaySpawnEffect();
        if (IsOwner && !IsBot) FollowWithCamera();
        if (IsLocalPlayer && TryGetComponent(out SecondaryWeaponHud hud)) hud.ShowForLocalOwner();
        // 부시 가시성은 이 클라이언트의 내 캐릭터가 판정한다. 오프라인 테스트의 더미는 호스트 소유지만 플레이어 오브젝트가 아니다.
        if (bushVisibility != null) bushVisibility.IsLocal = IsLocalPlayer;
        // 상대 플레이어는 내 입력을 읽으면 안 되므로 항상 꺼 둔다.
        ApplyCombatState(IsOwner && combatEnabled.Value);
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log($"[Player] Despawned slot={slot} owner={OwnerClientId}.");
        if (TryGetComponent(out SecondaryWeaponHud hud)) hud.Hide();
        combatEnabled.OnValueChanged -= OnCombatEnabledChanged;
        health.OnValueChanged -= OnHealthChanged;
        // Authority는 그대로 둔다. 디스폰 뒤 Destroy 전에 남은 애니메이션 이벤트가 오프라인 공격으로 빠지지 않고 아래에서 무시된다.
        ApplyCombatState(false);
        attack.CancelAttack();
        SpawnEffectController.Stop(slot);
        if (bushVisibility != null) bushVisibility.IsLocal = false;
        // 파괴될 플레이어를 계속 따라가지 않도록 카메라 추적을 끈다.
        if (cameraFollow != null) cameraFollow.enabled = false;
    }

    public void ServerEnableCombat()
    {
        if (!IsServer || !IsSpawned) return;
        combatEnabled.Value = true;
    }

    // 승부가 나면 두 플레이어 모두 부른다. 입력을 끄고 남은 상태이상을 지워서, 늦게 온 명중 보고와 도트 피해를 더 받지 않는다.
    public void ServerDisableCombat()
    {
        if (!IsServer || !IsSpawned) return;
        combatEnabled.Value = false;
        ClearServerStatusEffects();
    }

    void IAttackAuthority.StartAttack(float chargeRatio)
    {
        if (!IsSpawned || !IsOwner || !combatEnabled.Value || playerHealth.IsDead) return;
        // 봇의 공격 시작을 받을 상대 클라이언트가 없으므로, 받은 쪽이 세던 부시 노출을 여기서 센다.
        if (IsBot && bushVisibility != null) bushVisibility.Reveal();
        PlayAttackRpc(chargeRatio);
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    void PlayAttackRpc(float chargeRatio)
    {
        if (IsClient) ReceiveAttack(chargeRatio);
    }

    // 오프라인 테스트 전용. 더미는 호스트 소유라 PlayAttackRpc를 받는 클라이언트가 없으므로, 받은 것처럼 같은 처리를 한다.
    public void SimulateReceivedAttack(float chargeRatio) => ReceiveAttack(chargeRatio);

    void ReceiveAttack(float chargeRatio)
    {
        // 부시 안에서 공격하면 공격 시작부터 드러난다. 상대가 이미 공격 중이라 모션을 건너뛰어도 노출은 센다.
        if (bushVisibility != null) bushVisibility.Reveal();
        attack.PlayReplicatedAttack(chargeRatio);
    }

    void IAttackAuthority.FireMagic(float chargeRatio)
    {
        if (!IsSpawned || !IsOwner || !combatEnabled.Value || playerHealth.IsDead) return;
        // Use the attack-start snapshot, just like the offline path. Unsupported
        // online weapons must not fall back to local damage/projectile logic.
        IWeapon weapon = attack.AttackingWeapon as IWeapon;
        if (weapon == null)
        {
            Debug.LogWarning("DuelPlayer: attacking weapon does not implement online IWeapon; shot skipped.", this);
            return;
        }
        Physics.SyncTransforms();
        weapon.Fire(++attackId, Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, chargeRatio, this);
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
        // 서버에는 여러 방의 플레이어가 함께 있다. 같은 판의 플레이어만 맞힐 수 있다.
        if (!combatEnabled.Value || !target.TryGet(out NetworkObject obj, NetworkManager) ||
            !obj.TryGetComponent(out DuelPlayer player) || player.Match != Match ||
            !player.combat.TryTakeHit(slot, attackId, hitIndex, damage, out bool defeatedNow)) return;
        Debug.Log($"[Combat] Hit slot={player.slot} attacker={slot} id={attackId} hit={hitIndex} health={player.combat.Health:F3}");
        player.health.Value = player.combat.Health;
        PlayerWeaponPassiveController.NotifyServerHit(attack.Weapon, this, player);
        if (defeatedNow)
        {
            player.ClearServerStatusEffects();
            player.Defeated?.Invoke(player);
        }
    }

    /// <summary>서버에서 상태이상 틱 피해를 넣는다. 공격자 보고가 없어 서버가 직접 굴린다.</summary>
    public void ServerApplyStatusTick(int attackerSlot, uint dotAttackId, int tickIndex, float damage)
    {
        // NOTE: combatEnabled is input gating for the owner. DoT must still tick on
        // dummies / remote players whose combat input was never enabled.
        if (!IsServer || !IsSpawned || combat == null) return;
        if (!combat.TryTakeHit(attackerSlot, dotAttackId, tickIndex, damage, out bool defeatedNow)) return;
        Debug.Log($"[Combat] DoT slot={slot} attacker={attackerSlot} id={dotAttackId} tick={tickIndex} health={combat.Health:F3}");
        health.Value = combat.Health;
        if (defeatedNow)
        {
            ClearServerStatusEffects();
            Defeated?.Invoke(this);
        }
    }

    /// <summary>서버에서 적중 패시브가 상태이상을 건다. 공격자 슬롯을 그대로 쓴다.</summary>
    public void ServerInflictStatus(PlayerStatusEffects target, float tickDamage, int tickCount, float duration,
        float slowMultiplier, float slowDuration)
    {
        if (!IsServer || target == null) return;
        if (slowDuration > 0f) target.ServerApplySlow(slowMultiplier, slowDuration);
        if (tickCount > 0) target.ServerApplyBurn(Slot, tickDamage, tickCount, duration);
    }

    void ClearServerStatusEffects()
    {
        if (TryGetComponent(out PlayerStatusEffects status)) status.ServerClearAll();
    }

    void OnHealthChanged(float previous, float current) => ApplyHealth(current);

    // 체력은 줄기만 하므로 서버 체력과의 차이만큼 깎는다. 0이 되면 PlayerHealth가 이동·조준·공격과 애니메이터를 멈춘다.
    void ApplyHealth(float value)
    {
        playerHealth.TakeDamage(playerHealth.CurrentHealth - value);
        if (playerHealth.IsDead) attack.CancelAttack();
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
        // 상대 스폰이 화면 위쪽에 오는 방향으로 고정한다. 로비에서 내려오는 카메라(TitleBackdrop.Dive)도 같은 방향으로 내려온다.
        // 오프라인 테스트처럼 DuelArena에 맵이 없으면 카메라를 넘겨주는 쪽(DuelOfflineTest.FollowPlayer)이 정한다.
        var arena = FindFirstObjectByType<DuelArena>();
        if (arena != null && cameraFollow.Settings != null &&
            cameraFollow.Settings.TryFacingYaw(arena.SpawnPoints, slot, out float yaw))
            cameraFollow.Yaw = yaw;
        cameraFollow.enabled = true;
    }

    void OnCombatEnabledChanged(bool previous, bool current)
    {
        Debug.Log($"[Player] Combat {(current ? "enabled" : "disabled")} slot={slot} owner={OwnerClientId} local={IsOwner} position={transform.position}.");
        if (!IsOwner) return;
        ApplyCombatState(current);
        if (current) return;
        // 승부가 나서 꺼지면 차징 중이던 공격과 미리보기를 거두고, 걷던 자리에서 제자리 걸음을 하지 않게 멈춰 세운다.
        attack.CancelAttack();
        if (!animator.enabled) return;
        animator.SetFloat("MoveSpeed", 0f);
        animator.SetFloat("MoveX", 0f);
        animator.SetFloat("MoveZ", 0f);
    }

    void ApplyCombatState(bool enabled)
    {
        // 상대도 공격자의 물리 판정에 잡히도록 콜라이더를 켜 둔다. 입력은 소유자만 받는다.
        if (TryGetComponent(out CharacterController controller)) controller.enabled = IsClient && (!IsOwner || enabled);
        // 봇은 입력 컴포넌트를 끈 채로 두고 DuelBot만 켠다.
        bool input = enabled && !IsBot;
        if (TryGetComponent(out PlayerMovement movement)) movement.enabled = input;
        if (TryGetComponent(out PlayerAim aim)) aim.enabled = input;
        if (TryGetComponent(out PlayerAttack attack)) attack.enabled = input;
        if (IsBot) bot.enabled = enabled;
    }

    void LateUpdate()
    {
        // 쓰러지면 PlayerHealth가 애니메이터를 끈다.
        // 내 캐릭터는 PlayerMovement가 걷기 값을 넣는다. 상대와 봇은 움직인 거리로 넣는다.
        if (!IsClient || !IsSpawned || (IsOwner && !IsBot) || !animator.enabled) return;
        Vector3 displacement = transform.position - previousPosition;
        displacement.y = 0f;
        // 시전 중에는 소유자의 PlayerMovement처럼 걷기 값을 0으로 둔다.
        bool isMoving = !attack.IsAttacking && displacement.sqrMagnitude > 0.00001f;
        float directionDampTime = animator.GetFloat("MoveSpeed") > 0f ? 0.1f : 0f;
        float remoteBaseSpeed = 5f;
        if (TryGetComponent(out PlayerCombatStats stats))
            remoteBaseSpeed = stats.GetMoveSpeed(remoteBaseSpeed);
        animator.SetFloat("MoveSpeed", isMoving ? remoteBaseSpeed : 0f);
        Vector3 localDirection = isMoving ? transform.InverseTransformDirection(displacement.normalized) : Vector3.zero;
        animator.SetFloat("MoveX", localDirection.x, directionDampTime, Time.deltaTime);
        animator.SetFloat("MoveZ", localDirection.z, directionDampTime, Time.deltaTime);
        previousPosition = transform.position;
    }
}
