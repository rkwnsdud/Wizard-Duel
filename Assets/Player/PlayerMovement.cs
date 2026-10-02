using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;
    float gravity = -20f;

    CharacterController controller;
    Animator animator;
    Vector3 velocity;
    Transform cameraTransform;
    PlayerAttack playerAttack;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        playerAttack = GetComponent<PlayerAttack>();

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

        animator.SetFloat("MoveSpeed", moveDirection.sqrMagnitude > 0f ? moveSpeed : 0f);
        controller.Move(moveDirection * moveSpeed * Time.deltaTime);

        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = -2f;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
