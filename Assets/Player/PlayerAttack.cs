using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    private PlayerInputActions inputActions;

    [SerializeField] private WeaponBase currentWeapon;
    [Min(0.01f)] [SerializeField] private float maxChargeTime = 2f;

    private bool isCharging;
    private float chargeTime;

    private Animator animator;

    private float pendingChargeRatio;
    private bool hasFired;

    public bool IsAttacking { get; private set; }

    private void Awake()
    {
        inputActions = new PlayerInputActions();

        // Keep player variants working if the weapon reference is not serialized on the variant.
        if (currentWeapon == null)
            currentWeapon = GetComponentInChildren<WeaponBase>(true);

        Transform character = transform.Find("Wizard");
        animator = character.GetComponent<Animator>();
    }

    private void Update()
    {
        if (isCharging)
        {
            chargeTime = Mathf.Min(chargeTime + Time.deltaTime, Mathf.Max(0f, maxChargeTime));
        }
    }

    private float ChargeRatio => maxChargeTime > 0f ? Mathf.Clamp01(chargeTime / maxChargeTime) : 1f;

    private void LateUpdate()
    {
        // PlayerAim updates first so the preview follows this frame's aim.
        if (isCharging && currentWeapon != null)
            currentWeapon.ShowChargePreview(ChargeRatio);
    }
    private void OnEnable()
    {
        if (inputActions == null)
        {
            inputActions = new PlayerInputActions();
        }

        inputActions.Player.Attack.started += ChargeStart;
        inputActions.Player.Attack.canceled += Attack;

        inputActions.Enable();
    }

    private void OnDisable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Attack.started -= ChargeStart;
        inputActions.Player.Attack.canceled -= Attack;

        inputActions.Disable();

        if (currentWeapon != null) currentWeapon.HideChargePreview();
        isCharging = false;
        chargeTime = 0f;
        IsAttacking = false;
        hasFired = false;

        if (animator != null)
        {
            animator.ResetTrigger("Attack");
        }
    }

    // 공격 버튼을 누른 순간 차징 시작
    private void ChargeStart(InputAction.CallbackContext context)
    {
        if (IsAttacking)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Temporary charge diagnostics for Play mode testing.
            Debug.Log("[Charge] Start ignored: attack animation still active.", this);
#endif
            return;
        }

        isCharging = true;
        chargeTime = 0f;
        if (currentWeapon != null) currentWeapon.ShowChargePreview(ChargeRatio);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Temporary charge diagnostics for Play mode testing.
        Debug.Log($"[Charge] Start maxChargeTime={maxChargeTime:F3}, weapon={currentWeapon}", this);
#endif
    }

    // 공격 버튼을 뗀 순간 공격 애니메이션 시작
    private void Attack(InputAction.CallbackContext context)
    {
        if (IsAttacking || !isCharging)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Temporary charge diagnostics for Play mode testing.
            Debug.Log($"[Charge] Release ignored: IsAttacking={IsAttacking}, isCharging={isCharging}", this);
#endif
            return;
        }

        isCharging = false;

        pendingChargeRatio = ChargeRatio;
        if (currentWeapon != null) currentWeapon.HideChargePreview();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Temporary charge diagnostics: log once on release, not every frame.
        Debug.Log($"[Charge] Release chargeTime={chargeTime:F3}, maxChargeTime={maxChargeTime:F3}, pendingChargeRatio={pendingChargeRatio:F3}", this);
#endif

        hasFired = false;
        IsAttacking = true;

        // Release commits the charge; WizardAnimationEvent fires at the cast pose.
        animator.SetFloat("MoveSpeed", 0f);
        animator.SetTrigger("Attack");
    }

    // 공격 애니메이션 중간 Animation Event에서 호출
    public void FireMagic()
    {
        if (!IsAttacking || hasFired)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Temporary charge diagnostics for duplicate or out-of-order events.
            Debug.Log($"[Charge] FireMagic ignored: IsAttacking={IsAttacking}, hasFired={hasFired}", this);
#endif
            return;
        }

        hasFired = true;

        if (currentWeapon != null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Temporary charge diagnostics: confirms the animation event arrived.
            Debug.Log($"[Charge] FireMagic pendingChargeRatio={pendingChargeRatio:F3}, weapon={currentWeapon.name}", this);
#endif
            currentWeapon.Fire(pendingChargeRatio);
        }
        else
        {
            Debug.LogError("PlayerAttack: currentWeapon is not assigned.", this);
        }
    }

    // 공격 애니메이션 마지막 Animation Event에서 호출
    public void EndAttack()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Temporary charge diagnostics: verifies the final animation event.
        Debug.Log($"[Charge] EndAttack hasFired={hasFired}", this);
#endif
        IsAttacking = false;
    }

    private void OnDestroy()
    {
        inputActions?.Dispose();
    }
}
