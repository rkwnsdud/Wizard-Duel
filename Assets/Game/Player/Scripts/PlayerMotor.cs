using UnityEngine;

// 입력원이 정한 이동·조준과 재생 위치를 적용한다. 키보드·봇 판단·네트워크 보간은 모른다.
[DisallowMultipleComponent]
public sealed class PlayerMotor : MonoBehaviour
{
    public const float BaseSpeed = 5f;
    public const float Gravity = -20f;

    PlayerStatusEffects statusEffects;
    CharacterController controller;
    Animator animator;
    PlayerAttack attack;
    PlayerCombatStats stats;
    float fall;

    public bool IsAttacking => attack != null && attack.IsAttacking;
    public bool CanMove => controller != null && controller.enabled;
    public float MoveSpeed => stats != null ? stats.GetMoveSpeed(BaseSpeed) : BaseSpeed;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        attack = GetComponent<PlayerAttack>();
        stats = GetComponent<PlayerCombatStats>();
        Transform character = transform.Find("Wizard");
        animator = character != null ? character.GetComponent<Animator>() : null;
        statusEffects = GetComponent<PlayerStatusEffects>();
    }

    public void ResetVisualRotation()
    {
        Transform character = transform.Find("Wizard");
        if (character != null) character.localRotation = Quaternion.identity;
    }

    public void ResetFall() => fall = 0f;

    // 사람은 수평·중력 순서의 두 sweep, 봇은 합친 한 sweep을 써 온 충돌 동작을 유지한다.
    public void Move(Vector3 direction, float deltaTime, bool combinedGravity = false)
    {
        if (statusEffects != null && statusEffects.StunActiveNet) direction = Vector3.zero;
        bool grounded = controller.isGrounded;
        Vector3 horizontal = direction * MoveSpeed * deltaTime;
        if (!combinedGravity) controller.Move(horizontal);
        if (grounded && fall < 0f) fall = -2f;
        fall += Gravity * deltaTime;
        controller.Move((combinedGravity ? horizontal : Vector3.zero) + Vector3.up * (fall * deltaTime));
    }

    // 사람의 조준은 즉시, 봇의 조준은 지정한 각속도로 적용한다.
    public void Look(Vector3 direction, float deltaTime, float turnSpeed = float.PositiveInfinity)
    {
        direction.y = 0f;
        if (IsAttacking || direction.sqrMagnitude <= 0.001f) return;
        if (statusEffects != null && statusEffects.StunActiveNet) return;
        Quaternion rotation = Quaternion.LookRotation(direction);
        transform.rotation = float.IsPositiveInfinity(turnSpeed) ? rotation :
            Quaternion.RotateTowards(transform.rotation, rotation, turnSpeed * deltaTime);
    }

    public void ApplyPose(Vector3 position, float yaw) =>
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

    public void Animate(Vector3 direction, float deltaTime)
    {
        if (statusEffects != null && statusEffects.StunActiveNet)
        {
            StopAnimation();
            return;
        }
        if (animator == null || !animator.enabled) return;
        float dampTime = animator.GetFloat("MoveSpeed") > 0f ? 0.1f : 0f;
        bool moving = direction.sqrMagnitude > 0f;
        animator.SetFloat("MoveSpeed", moving ? MoveSpeed : 0f);
        // 달리기 클립의 재생 빠르기(이동 속도 m/s). 멈출 때는 마지막 값을 남겨 대기로 넘어가는 동안 다리가 굳지 않게 한다.
        if (moving) animator.SetFloat("LocomotionSpeed", MoveSpeed);
        Vector3 local = transform.InverseTransformDirection(direction);
        animator.SetFloat("MoveX", local.x, dampTime, deltaTime);
        animator.SetFloat("MoveZ", local.z, dampTime, deltaTime);
    }

    public void StopAnimation()
    {
        if (animator == null || !animator.enabled) return;
        animator.SetFloat("MoveSpeed", 0f);
        animator.SetFloat("MoveX", 0f);
        animator.SetFloat("MoveZ", 0f);
    }
}

