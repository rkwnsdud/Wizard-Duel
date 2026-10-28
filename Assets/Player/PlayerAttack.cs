using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    private PlayerInputActions inputActions;
    private PlayerHealth playerHealth;

    [SerializeField] private WeaponBase currentWeapon;
    [Min(0.01f)] [SerializeField] private float maxChargeTime = 2f;

    private bool isCharging;
    private float chargeTime;

    private Animator animator;

    private float pendingChargeRatio;
    private bool hasFired;

    public bool IsAttacking { get; private set; }
    // 온라인 전투에서 DuelPlayer가 넣는다. 없으면 오프라인으로 바로 공격한다.
    public IAttackAuthority Authority { get; set; }
    public WeaponBase Weapon => currentWeapon;

    private void Awake()
    {
        inputActions = new PlayerInputActions();
        playerHealth = GetComponent<PlayerHealth>();

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

        inputActions.Player.Attack.started += AttackStarted;
        inputActions.Player.Attack.canceled += Attack;

        inputActions.Enable();
    }

    private void OnDisable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Attack.started -= AttackStarted;
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
    private void AttackStarted(InputAction.CallbackContext context)
    {
        if (IsAttacking || (playerHealth != null && playerHealth.IsDead))
        {
            return;
        }

        if (currentWeapon == null || !currentWeapon.UsesCharge)
        {
            CommitAttack(0f);
            return;
        }

        isCharging = true;
        chargeTime = 0f;
        if (currentWeapon != null) currentWeapon.ShowChargePreview(ChargeRatio);
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
        if (currentWeapon != null) currentWeapon.HideChargePreview();

        CommitAttack(chargeRatio);
    }

    private void CommitAttack(float chargeRatio)
    {
        PlayAttack(chargeRatio);
        Authority?.StartAttack(chargeRatio);
    }

    // 소유자는 즉시 재생하고 상대는 전달받은 충전값으로 재생한다.
    public void PlayAttack(float chargeRatio)
    {
        if (playerHealth != null && playerHealth.IsDead)
            return;

        pendingChargeRatio = chargeRatio;
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
        else if (currentWeapon != null)
        {
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
        IsAttacking = false;
    }

    private void OnDestroy()
    {
        inputActions?.Dispose();
    }
}
