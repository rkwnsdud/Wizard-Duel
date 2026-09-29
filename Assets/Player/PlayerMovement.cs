using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;
    float jumpForce = 7f;
    float gravity = -20f;
    float rotationSpeed = 360f;

    CharacterController controller;
    Vector3 velocity;

    bool isGrounded;

    void Start()
    {
        controller = GetComponent<CharacterController>();

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

        // 입력
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

        // 카메라 기준 이동 방향
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;

        // 위아래 방향 제거
        cameraForward.y = 0f;
        cameraRight.y = 0f;

        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDirection =
            cameraForward * vertical +
            cameraRight * horizontal;

        // 대각선 포함 이동
        if (moveDirection != Vector3.zero)
        {
            moveDirection.Normalize();

            // 이동 방향을 바라보도록 회전
            Quaternion targetRotation =
                Quaternion.LookRotation(moveDirection);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

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
            Debug.Log("스페이스 입력 / Grounded: " + isGrounded);

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