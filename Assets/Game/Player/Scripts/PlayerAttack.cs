using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerAttack : MonoBehaviour
{
    private PlayerInputActions inputActions;
    private PlayerHealth playerHealth;
    private PlayerWeaponPassiveController passiveController;
    private PlayerCombatStats combatStats;

    [SerializeField] private WeaponBase currentWeapon;
    public WeaponBase CurrentWeapon => currentWeapon;
    [Min(0.01f)] [SerializeField] private float maxChargeTime = 2f;

    private bool isCharging;
    private float chargeStartedAt;
    private float cooldownEndsAt;
    private float activeCooldownDuration;

    private Animator animator;
    [FormerlySerializedAs("attackAnimationSlot")]
    [SerializeField, Tooltip("공통 Animator의 Cast 상태에 연결된 원본 클립입니다. 공격 시 이 클립을 장착 무기의 Attack Animation으로 교체합니다. 무기별 공격 모션은 각 무기에서 지정하세요.")]
    private AnimationClip baseCastClip;
    private AnimatorOverrideController weaponController;
    private RuntimeAnimatorController sourceController;
    private WeaponBase attackingWeapon;
    public WeaponBase AttackingWeapon => attackingWeapon;
    // 진행 중인 공격의 번호와 무기 id. 공격을 시작할 때 정하고 발사·연출·명중 보고에 그대로 싣는다.
    // 공격 중이 아니면 기본값(Id 0)이다.
    private AttackInfo currentAttack;
    public AttackInfo CurrentAttack => currentAttack;
    // 이 플레이어가 마지막으로 시작한 공격 번호. 소유자 화면에서만 는다(상대 화면은 받은 번호를 쓴다).
    private uint lastAttackId;
    private float attackAnimSpeed = 1f;

    // Pose components can apply/release their controller synchronously on equip,
    // including when the new weapon was already active in the hierarchy.
    public event System.Action EquippedWeaponChanged;

    // A temporary Cast override still belongs to the pose controller it wraps.
    // Reading this property never changes the Animator.
    public RuntimeAnimatorController AnimationSourceController
    {
        get
        {
            if (animator == null) return null;
            return weaponController != null && animator.runtimeAnimatorController == weaponController
                ? sourceController : animator.runtimeAnimatorController;
        }
    }

    // Remote players keep their poses even though their input component is disabled.
    public bool IsWeaponPoseActive(WeaponBase weapon) => weapon != null && currentWeapon == weapon;

    // 공격 정보의 무기 id로 이 플레이어의 무기 인스턴스를 찾는다. 공격 이벤트(공격 시작·연출·적중 효과)는
    // 지금 든 무기가 아니라 이 무기로 처리한다. 교체하거나 내려놓은 무기도 인벤토리가 꺼진 채 들고 있다.
    // 찾는 순서: 공격 중인 무기 → 든 무기 → 인벤토리가 든 적 있는 무기 → 이 화면에 아직 없으면 인벤토리가
    // 정의의 프리팹으로 꺼진 채 만든 무기(뒤에 오는 교체 스냅샷도 같은 인스턴스를 쓴다) → 자식 무기(오프라인 테스트).
    // 0(빈 무기)이거나 끝내 찾지 못하면 null이다.
    public WeaponBase FindWeapon(byte id)
    {
        if (id == 0) return null;
        if (attackingWeapon != null && attackingWeapon.Id == id) return attackingWeapon;
        if (currentWeapon != null && currentWeapon.Id == id) return currentWeapon;
        PlayerWeaponInventory inventory = GetComponent<PlayerWeaponInventory>();
        WeaponBase weapon = inventory != null ? inventory.FindWeapon(id) : null;
        if (weapon == null && inventory != null) weapon = inventory.CreateMissingWeapon(id);
        if (weapon != null) return weapon;
        foreach (WeaponBase child in GetComponentsInChildren<WeaponBase>(true))
            if (child.Id == id) return child;
        return null;
    }

    // 연출 한 건을 그 공격의 무기로 재생한다. 공격한 쪽 화면과 상대 화면이 같은 규칙을 쓴다.
    // 무기를 바꾼 뒤 도착한 연출도 쏜 무기(꺼져 있어도)가 재생하고, 지금 든 무기는 끼어들지 않는다.
    // 무기를 찾지 못한 연출은 다른 무기로 대신 띄우지 않고 버린다.
    public bool PresentShot(ShotEvent shot)
    {
        if (FindWeapon(shot.WeaponId) is IWeapon weapon)
        {
            if (currentWeapon == null || currentWeapon.Id != shot.WeaponId)
                Debug.Log($"[Attack] {name} presents {shot.Attack} kind={shot.Kind} with the fired weapon " +
                    $"while holding weapon={(currentWeapon != null ? currentWeapon.Id : 0)}.", this);
            weapon.Present(shot);
            return true;
        }
        Debug.LogWarning($"[Attack] {name} dropped shot {shot.Attack} kind={shot.Kind}: no weapon with that id.", this);
        return false;
    }


    private float pendingChargeRatio;
    private bool hasFired;

    public bool IsAttacking { get; private set; }
    // 화면 위 패널 버튼을 누르는 클릭이 공격으로 새지 않게 새 공격 입력만 막는다(진행 중인 공격은 그대로).
    public bool AttackInputBlocked { get; set; }
    // Hold new attacks while a server-confirmed equipment transaction is pending.
    public bool InventoryChangePending { get; set; }
    public bool CanChangeWeapon => !IsAttacking && !isCharging && (playerHealth == null || !playerHealth.IsDead);
    public bool IsOnCooldown => CooldownRemaining > 0f;
    public float CooldownRemaining => Mathf.Max(0f, cooldownEndsAt - Time.time);
    public float CooldownRemainingFraction => activeCooldownDuration <= 0f || !IsOnCooldown
        ? 0f
        : Mathf.Clamp01(CooldownRemaining / activeCooldownDuration);
    public float CooldownProgress => activeCooldownDuration <= 0f || !IsOnCooldown
        ? 1f
        : Mathf.Clamp01(1f - CooldownRemaining / activeCooldownDuration);
    // DuelPlayer가 넣는다. 발사는 이 권한 경로로만 전달한다.
    public IAttackAuthority Authority { get; set; }
    public WeaponBase Weapon => currentWeapon;
    public float MaxChargeTime => maxChargeTime;

    // Equipment must be parented to this player's socket first. Switching during
    // charge/cast is rejected so animation events cannot fire a different weapon.
    public bool TryEquipWeapon(WeaponBase weapon)
    {
        if (IsAttacking || isCharging || (playerHealth != null && playerHealth.IsDead)) return false;
        if (weapon != null && !weapon.transform.IsChildOf(transform)) return false;
        RestoreWeaponController();
        if (currentWeapon != null && currentWeapon != weapon)
        {
            passiveController?.Unequip(currentWeapon);
            HideChargePreview(currentWeapon);
            currentWeapon.gameObject.SetActive(false);
        }
        currentWeapon = weapon;
        if (weapon != null) weapon.gameObject.SetActive(true);
        passiveController?.Equip(weapon);
        EquippedWeaponChanged?.Invoke();
        return true;
    }

    private void ApplyWeaponAnimation()
    {
        RestoreWeaponController();
        if (attackingWeapon == null ||
            attackingWeapon.AttackAnimation == null || baseCastClip == null) return;
        sourceController = animator.runtimeAnimatorController;
        if (sourceController == null) return;
        // Copy existing Idle/Grip mappings onto the same underlying controller.
        // Do not rely on nesting an override controller to preserve those mappings.
        var poseOverride = sourceController as AnimatorOverrideController;
        weaponController = new AnimatorOverrideController(
            poseOverride != null ? poseOverride.runtimeAnimatorController : sourceController);
        if (poseOverride != null)
        {
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(poseOverride.overridesCount);
            poseOverride.GetOverrides(overrides);
            weaponController.ApplyOverrides(overrides);
        }
        weaponController[baseCastClip] = attackingWeapon.AttackAnimation;
        animator.runtimeAnimatorController = weaponController;
    }

    private void RestoreWeaponController()
    {
        if (weaponController == null) return;
        if (animator != null && animator.runtimeAnimatorController == weaponController)
            animator.runtimeAnimatorController = sourceController;
        Destroy(weaponController);
        weaponController = null;
        sourceController = null;
    }

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        passiveController = GetComponent<PlayerWeaponPassiveController>();
        combatStats = GetComponent<PlayerCombatStats>();

        // Keep player variants working if the weapon reference is not serialized on the variant.
        if (currentWeapon == null)
            currentWeapon = GetComponentInChildren<WeaponBase>(true);

        passiveController?.Equip(currentWeapon);

        Transform character = transform.Find("Wizard");
        animator = character.GetComponent<Animator>();
        combatStats ??= GetComponent<PlayerCombatStats>();
        EquippedWeaponChanged?.Invoke();
    }

    private float ChargeRatio => maxChargeTime > 0f ? Mathf.Clamp01((Time.time - chargeStartedAt) / maxChargeTime) : 1f;

    private static void HideChargePreview(WeaponBase weapon)
    {
        if (weapon != null && weapon is IChargeableWeapon chargeable)
            chargeable.HideChargePreview();
    }

    private void LateUpdate()
    {
        // PlayerAim updates first so the preview follows this frame's aim.
        if (isCharging && currentWeapon != null && currentWeapon is IChargeableWeapon chargeable)
            chargeable.ShowChargePreview(ChargeRatio);
    }
    private void OnEnable()
    {
        inputActions ??= new PlayerInputActions();

        inputActions.Player.Attack.started += AttackStarted;
        inputActions.Player.Attack.canceled += Attack;
        inputActions.Player.Interact.performed += OnInteract;
        inputActions.Player.SwapWeapon.performed += OnSwapWeapon;
        inputActions.Enable();
    }

    private void OnDisable()
    {
        if (inputActions != null)
        {
            inputActions.Player.Attack.started -= AttackStarted;
            inputActions.Player.Attack.canceled -= Attack;
            inputActions.Player.Interact.performed -= OnInteract;
            inputActions.Player.SwapWeapon.performed -= OnSwapWeapon;
            inputActions.Disable();
        }

        CancelAttack();
    }

    // Also called explicitly on network despawn/death: remote PlayerAttack is
    // already disabled for input, so OnDisable cannot be relied on there.
    public void CancelAttack()
    {
        attackingWeapon?.OnAttackFinished();
        if (animator != null) animator.speed = 1f;
        attackAnimSpeed = 1f;
        HideChargePreview(currentWeapon);
        if (attackingWeapon != currentWeapon) HideChargePreview(attackingWeapon);
        isCharging = false;
        chargeStartedAt = 0f;
        IsAttacking = false;
        hasFired = false;
        pendingChargeRatio = 0f;
        attackingWeapon = null;
        currentAttack = default;
        RestoreWeaponController();

        if (animator != null)
        {
            animator.ResetTrigger("Attack");
        }
    }

    // 공격 버튼을 누른 순간 차징 시작
    private void AttackStarted(InputAction.CallbackContext context)
    {
        if (AttackInputBlocked || InventoryChangePending || currentWeapon == null || IsAttacking || isCharging || IsOnCooldown ||
            (playerHealth != null && playerHealth.IsDead))
        {
            return;
        }

        if (!(currentWeapon is IChargeableWeapon))
        {
            CommitAttack(0f);
            return;
        }

        isCharging = true;
        chargeStartedAt = Time.time;
        ((IChargeableWeapon)currentWeapon).ShowChargePreview(ChargeRatio);
    }

    // 공격 버튼을 뗀 순간 공격 애니메이션 시작
    private void Attack(InputAction.CallbackContext context)
    {
        if (IsAttacking || IsOnCooldown || !isCharging || (playerHealth != null && playerHealth.IsDead))
        {
            return;
        }

        isCharging = false;

        float chargeRatio = ChargeRatio;
        HideChargePreview(currentWeapon);

        CommitAttack(chargeRatio);
    }

    // 입력 없이 공격을 시작한다(AI 대전의 봇). 차징 시간은 부르는 쪽이 직접 기다린 뒤 그만큼의 비율을 넘긴다.
    // 차징하지 않는 무기는 비율을 0으로 본다. 쿨다운·공격 중·사망이면 거절한다.
    public bool TryStartAttack(float chargeRatio)
    {
        if (currentWeapon == null || IsAttacking || isCharging || IsOnCooldown ||
            (playerHealth != null && playerHealth.IsDead))
            return false;
        CommitAttack(currentWeapon is IChargeableWeapon ? Mathf.Clamp01(chargeRatio) : 0f);
        return IsAttacking;
    }

    private void CommitAttack(float chargeRatio)
    {
        PlayAttack(chargeRatio);
        if (IsAttacking) Authority?.StartAttack(currentAttack, chargeRatio);
    }

    // 소유자는 즉시 재생하고 상대는 전달받은 충전값으로 재생한다.
    // 소유자의 공격은 여기서 새 공격 번호를 받고, 지금 든 무기가 그 공격의 무기가 된다.
    public void PlayAttack(float chargeRatio)
    {
        if (!CanPlayAttack(respectCooldown: true)) return;
        lastAttackId = AttackInfo.NextId(lastAttackId);
        BeginAttack(new AttackInfo(lastAttackId, currentWeapon.Id), currentWeapon, chargeRatio);
    }

    // Remote attack RPCs must be replayed even if network delay makes local
    // cooldown clocks differ by a few milliseconds.
    // attack은 공격한 쪽이 공격을 시작할 때 정한 번호와 무기 id다.
    // 공격 모션도 받은 무기 id의 무기로 재생한다. 교체 스냅샷보다 그 무기의 공격이 먼저 오면 쏜 무기로 먼저 바꿔 든다.
    // 늦게 온 스냅샷은 같은 무기 인스턴스를 다시 쓰므로 그대로 맞춰진다. 무기를 찾지 못하면 모션을 건너뛴다.
    public void PlayReplicatedAttack(AttackInfo attack, float chargeRatio)
    {
        if (!CanPlayAttack(respectCooldown: false)) return;
        WeaponBase weapon = FindWeapon(attack.WeaponId);
        if (weapon == null)
        {
            Debug.LogWarning($"[Attack] {name} skipped attack start {attack}: no weapon with that id.", this);
            return;
        }
        if (weapon != currentWeapon)
        {
            Debug.Log($"[Attack] {name} equips weapon={weapon.Id} for {attack} before its slot snapshot.", this);
            if (!TryEquipWeapon(weapon)) return;
        }
        BeginAttack(attack, weapon, chargeRatio);
    }

    private bool CanPlayAttack(bool respectCooldown) =>
        currentWeapon != null && !IsAttacking && !(respectCooldown && IsOnCooldown) &&
        (playerHealth == null || !playerHealth.IsDead);

    private void BeginAttack(AttackInfo attack, WeaponBase weapon, float chargeRatio)
    {
        pendingChargeRatio = chargeRatio;
        // Animation events and online authority use this same attack-start weapon.
        attackingWeapon = weapon;
        currentAttack = attack;
        ApplyWeaponAnimation();
        attackAnimSpeed = attackingWeapon != null ? attackingWeapon.AttackAnimSpeed : 1f;
        if (animator != null) animator.speed = attackAnimSpeed;
        attackingWeapon?.OnAttackStarted();
        hasFired = false;
        IsAttacking = true;
        StartAttackCooldown(attackingWeapon);

        // WizardAnimationEvent fires at the cast pose.
        animator.SetFloat("MoveSpeed", 0f);
        animator.SetTrigger("Attack");
    }

    private void StartAttackCooldown(WeaponBase weapon)
    {
        activeCooldownDuration = combatStats != null
            ? combatStats.GetAttackCooldown(weapon)
            : weapon != null ? weapon.AttackCooldown : 0f;
        cooldownEndsAt = Time.time + activeCooldownDuration;
    }

    // 공격 애니메이션 중간 Animation Event에서 호출
    public void FireMagic()
    {
        if (!IsAttacking || hasFired || (playerHealth != null && playerHealth.IsDead))
        {
            return;
        }

        hasFired = true;
        Authority?.FireMagic(currentAttack, pendingChargeRatio);
    }

    // 공격 애니메이션 마지막 Animation Event에서 호출
    public void EndAttack()
    {
        attackingWeapon?.OnAttackFinished();
        if (animator != null) animator.speed = 1f;
        attackAnimSpeed = 1f;
        IsAttacking = false;
        attackingWeapon = null;
        currentAttack = default;
        pendingChargeRatio = 0f;
        // Release the cast override so idle/visual pose owners can take over.
        RestoreWeaponController();
    }

    private void OnDestroy()
    {
        RestoreWeaponController();
        inputActions?.Dispose();
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        // 입력 감지
        PlayerWeaponInventory inventory = GetComponent<PlayerWeaponInventory>();

        if (inventory == null)
            return;

        inventory.TryPickupNearby();
    }

    private void OnSwapWeapon(InputAction.CallbackContext context)
    {
        PlayerWeaponInventory inventory = GetComponent<PlayerWeaponInventory>();

        if (inventory == null)
            return;

        inventory.TrySwapWeapon();
    }
}
