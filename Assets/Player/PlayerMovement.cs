using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;

    void Update()
    {
        Vector3 moveDIrection = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
        {
            moveDIrection += transform.forward;
        }
        if (Keyboard.current.sKey.isPressed)
        {
            moveDIrection += -transform.forward;
        }
        if (Keyboard.current.dKey.isPressed)
        {
            moveDIrection += transform.right;
        }
        if (Keyboard.current.aKey.isPressed)
        {
            moveDIrection += -transform.right;
        }

        transform.position += moveDIrection.normalized * moveSpeed * Time.deltaTime;
    }
}
