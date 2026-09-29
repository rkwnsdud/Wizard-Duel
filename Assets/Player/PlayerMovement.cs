using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;
    float jumpForce = 7f;
    float gravity = -20f;

    CharacterController controller;
    Vector3 velocity;
    float turnVelocity;
    float targetAngle;
    Transform character;
    Animator animator;

    bool isGrounded;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        character = transform.Find("Wizard");
        animator = character.GetComponent<Animator>();
        targetAngle = character.eulerAngles.y;

        Debug.Log("PlayerMovement 실행됨");
    }

    void Update()
    {
        // 바닥 확인
        isGrounded = Physics.Raycast(
            transform.position,
            Vector3.down,
            1.2f
        );

        // WASD 입력
        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.wKey.isPressed)
        {
            vertical += 1f;
        }

        if (Keyboard.current.sKey.isPressed)
        {
            vertical -= 1f;
        }

        if (Keyboard.current.dKey.isPressed)
        {
            horizontal += 1f;
        }

        if (Keyboard.current.aKey.isPressed)
        {
            horizontal -= 1f;
        }

        // 카메라 기준 방향 계산
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;

        cameraForward.y = 0f;
        cameraRight.y = 0f;

        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDirection =
            cameraForward * vertical +
            cameraRight * horizontal;

        // 대각선 이동 시 속도 증가 방지
        if (moveDirection != Vector3.zero)
        {
            moveDirection.Normalize();

            // 이동 방향을 바라보도록 회전
            targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
        }

        float angle = Mathf.SmoothDampAngle(character.eulerAngles.y, targetAngle, ref turnVelocity, 0.12f);
        character.rotation = Quaternion.Euler(0f, angle, 0f);
        animator.SetFloat("MoveSpeed", moveDirection.sqrMagnitude > 0f ? moveSpeed : 0f);

        // 이동
        controller.Move(
            moveDirection * moveSpeed * Time.deltaTime
        );

        // 바닥에 있을 때
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // 점프
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            if (isGrounded)
            {
                velocity.y = jumpForce;
            }
        }

        // 중력
        velocity.y += gravity * Time.deltaTime;

        // 위아래 이동
        controller.Move(velocity * Time.deltaTime);
    }
}
