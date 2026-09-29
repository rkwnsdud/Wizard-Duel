using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;
    float turnVelocity;
    float targetAngle;
    Transform character;

    void Start()
    {
        character = transform.Find("Wizard");
        targetAngle = character.eulerAngles.y;
    }

    void Update()
    {
        Vector3 moveDIrection = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
        {
            moveDIrection += Vector3.forward;
        }
        if (Keyboard.current.sKey.isPressed)
        {
            moveDIrection += -Vector3.forward;
        }
        if (Keyboard.current.dKey.isPressed)
        {
            moveDIrection += Vector3.right;
        }
        if (Keyboard.current.aKey.isPressed)
        {
            moveDIrection += -Vector3.right;
        }

        transform.position += moveDIrection.normalized * moveSpeed * Time.deltaTime;

        if (moveDIrection.sqrMagnitude > 0f)
        {
            targetAngle = Mathf.Atan2(moveDIrection.x, moveDIrection.z) * Mathf.Rad2Deg;
        }

        float angle = Mathf.SmoothDampAngle(character.eulerAngles.y, targetAngle, ref turnVelocity, 0.12f);
        character.rotation = Quaternion.Euler(0f, angle, 0f);
    }
}
