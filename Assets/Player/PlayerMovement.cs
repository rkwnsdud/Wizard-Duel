using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;
    float jumpForce = 7f;
    float gravity = -20f;

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

        // 이동 방향
        Vector3 moveDirection = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
        {
            moveDirection += transform.forward;
        }

        if (Keyboard.current.sKey.isPressed)
        {
            moveDirection -= transform.forward;
        }

        if (Keyboard.current.dKey.isPressed)
        {
            moveDirection += transform.right;
        }

        if (Keyboard.current.aKey.isPressed)
        {
            moveDirection -= transform.right;
        }

        // 이동
        controller.Move(
            moveDirection.normalized * moveSpeed * Time.deltaTime
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