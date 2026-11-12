using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10)]
public class PlayerMovement : MonoBehaviour
{
    // 무기 패시브·상태이상을 곱하기 전의 걷는 빠르기. AI 대전의 봇(DuelBot)도 같은 값으로 걷는다.
    public const float BaseSpeed = 5f;
    public const float Gravity = -20f;

    float moveSpeed = BaseSpeed;
    float gravity = Gravity;

    CharacterController controller;
    Animator animator;
    Vector3 velocity;
    Transform cameraTransform;
    PlayerAttack playerAttack;
    PlayerCombatStats combatStats;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        playerAttack = GetComponent<PlayerAttack>();
        combatStats = GetComponent<PlayerCombatStats>();

        Transform character = transform.Find("Wizard");
        character.localRotation = Quaternion.identity;
        animator = character.GetComponent<Animator>();

        cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (playerAttack.IsAttacking)
        {
            animator.SetFloat("MoveSpeed", 0f);
            animator.SetFloat("MoveX", 0f);
            animator.SetFloat("MoveZ", 0f);
            return;
        }

        
        bool isGrounded = controller.isGrounded;

        float horizontal = 0f;
        float vertical = 0f;
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed) vertical += 1f;
            if (keyboard.sKey.isPressed) vertical -= 1f;
            if (keyboard.dKey.isPressed) horizontal += 1f;
            if (keyboard.aKey.isPressed) horizontal -= 1f;
        }

        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;
        cameraForward.y = 0f;
        cameraRight.y = 0f;
        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDirection = cameraForward * vertical + cameraRight * horizontal;
        if (moveDirection != Vector3.zero)
        {
            moveDirection.Normalize();
        }

        float effectiveSpeed = combatStats != null ? combatStats.GetMoveSpeed(moveSpeed) : moveSpeed;
        float directionDampTime = animator.GetFloat("MoveSpeed") > 0f ? 0.1f : 0f;
        animator.SetFloat("MoveSpeed", moveDirection.sqrMagnitude > 0f ? effectiveSpeed : 0f);
        Vector3 localDirection = transform.InverseTransformDirection(moveDirection);
        animator.SetFloat("MoveX", localDirection.x, directionDampTime, Time.deltaTime);
        animator.SetFloat("MoveZ", localDirection.z, directionDampTime, Time.deltaTime);
        controller.Move(moveDirection * effectiveSpeed * Time.deltaTime);

        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
