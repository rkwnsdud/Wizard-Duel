using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    private PlayerInputActions inputActions;
    private PlayerHealth playerHealth;

    [SerializeField] private WeaponBase currentWeapon;
    public WeaponBase CurrentWeapon => currentWeapon;
    [Min(0.01f)] [SerializeField] private float maxChargeTime = 2f;

    private bool isCharging;
    private float chargeStartedAt;

    private Animator animator;
    [SerializeField, Tooltip("The Cast motion in the shared locomotion controller, replaced by the equipped weapon's clip.")]
    private AnimationClip attackAnimationSlot;
    private AnimatorOverrideController weaponController;
    private RuntimeAnimatorController sourceController;
    private WeaponBase attackingWeapon;
    public WeaponBase AttackingWeapon => attackingWeapon;

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

    private float pendingChargeRatio;
    private bool hasFired;

    public bool IsAttacking { get; private set; }
    // 온라인 전투에서 DuelPlayer가 넣는다. 없으면 오프라인으로 바로 공격한다.
    public IAttackAuthority Authority { get; set; }
    public WeaponBase Weapon => currentWeapon;

    // Equipment must be parented to this player's socket first. Switching during
    // charge/cast is rejected so animation events cannot fire a different weapon.
    public bool TryEquipWeapon(WeaponBase weapon)
    {
        if (IsAttacking || isCharging || (playerHealth != null && playerHealth.IsDead)) return false;
        if (weapon != null && !weapon.transform.IsChildOf(transform)) return false;
        RestoreWeaponController();
        if (currentWeapon != null && currentWeapon != weapon)
        {
            HideChargePreview(currentWeapon);
            currentWeapon.gameObject.SetActive(false);
        }
        currentWeapon = weapon;
        if (weapon != null) weapon.gameObject.SetActive(true);
        EquippedWeaponChanged?.Invoke();
        return true;
    }

    private void ApplyWeaponAnimation()
    {
        RestoreWeaponController();
        if (attackingWeapon == null ||
            attackingWeapon.AttackAnimation == null || attackAnimationSlot == null) return;
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
        weaponController[attackAnimationSlot] = attackingWeapon.AttackAnimation;
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

        // Keep player variants working if the weapon reference is not serialized on the variant.
        if (currentWeapon == null)
            currentWeapon = GetComponentInChildren<WeaponBase>(true);

        Transform character = transform.Find("Wizard");
        animator = character.GetComponent<Animator>();
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

        inputActions.Enable();
    }

    private void OnDisable()
    {
        if (inputActions != null)
        {
            inputActions.Player.Attack.started -= AttackStarted;
            inputActions.Player.Attack.canceled -= Attack;
            inputActions.Disable();
        }

        CancelAttack();
    }

    // Also called explicitly on network despawn/death: remote PlayerAttack is
    // already disabled for input, so OnDisable cannot be relied on there.
    public void CancelAttack()
    {
        HideChargePreview(currentWeapon);
        if (attackingWeapon != currentWeapon) HideChargePreview(attackingWeapon);
        isCharging = false;
        chargeStartedAt = 0f;
        IsAttacking = false;
        hasFired = false;
        pendingChargeRatio = 0f;
        attackingWeapon = null;
        RestoreWeaponController();

        if (animator != null)
        {
            animator.ResetTrigger("Attack");
        }
    }

    // 공격 버튼을 누른 순간 차징 시작
    private void AttackStarted(InputAction.CallbackContext context)
    {
        if (currentWeapon == null || IsAttacking || isCharging || (playerHealth != null && playerHealth.IsDead))
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
        if (IsAttacking || !isCharging || (playerHealth != null && playerHealth.IsDead))
        {
            return;
        }

        isCharging = false;

        float chargeRatio = ChargeRatio;
        HideChargePreview(currentWeapon);

        CommitAttack(chargeRatio);
    }

    private void CommitAttack(float chargeRatio)
    {
        PlayAttack(chargeRatio);
        if (IsAttacking) Authority?.StartAttack(chargeRatio);
    }

    // 소유자는 즉시 재생하고 상대는 전달받은 충전값으로 재생한다.
    public void PlayAttack(float chargeRatio)
    {
        if (currentWeapon == null || IsAttacking || (playerHealth != null && playerHealth.IsDead))
            return;

        pendingChargeRatio = chargeRatio;
        // Animation events and online authority use this same attack-start weapon.
        attackingWeapon = currentWeapon;
        ApplyWeaponAnimation();
        hasFired = false;
        IsAttacking = true;

        // WizardAnimationEvent fires at the cast pose.
        animator.SetFloat("MoveSpeed", 0f);
        animator.SetTrigger("Attack");
    }

    // 공격 애니메이션 중간 Animation Event에서 호출
    public void FireMagic()
    {
        if (!IsAttacking || hasFired || (playerHealth != null && playerHealth.IsDead))
        {
            return;
        }

        hasFired = true;

        // 온라인 명중은 공격한 클라이언트가 판정하고 체력은 서버가 반영한다.
        if (Authority != null)
        {
            Authority.FireMagic(pendingChargeRatio);
        }
        else if (attackingWeapon != null)
        {
            if (attackingWeapon is IChargeableWeapon chargeable)
                chargeable.FireCharged(pendingChargeRatio);
            else
                attackingWeapon.Fire();
        }
        else
        {
            Debug.LogError("PlayerAttack: currentWeapon is not assigned.", this);
        }
    }

    // 공격 애니메이션 마지막 Animation Event에서 호출
    public void EndAttack()
    {
        IsAttacking = false;
        attackingWeapon = null;
        pendingChargeRatio = 0f;
        // Release the cast override so idle/visual pose owners can take over.
        RestoreWeaponController();
    }

    private void OnDestroy()
    {
        RestoreWeaponController();
        inputActions?.Dispose();
    }
}
