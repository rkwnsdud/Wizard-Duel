using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerMovement : MonoBehaviour
{
    float moveSpeed = 5f;
    float rotationSpeed = 360f;
    void Update()
    {
        Vector3 moveDIrection = Vector3.zero;
        
        if (Keyboard.current.wKey.isPressed)
        {
            moveDIrection += new Vector3(0, 0, 1);
        }
        if (Keyboard.current.sKey.isPressed)
        {
            moveDIrection += new Vector3(0, 0, -1);
        }
        if (Keyboard.current.dKey.isPressed)
        {
            moveDIrection += new Vector3(1, 0, 0);
        }
        if (Keyboard.current.aKey.isPressed)
        {
            moveDIrection += new Vector3(-1, 0, 0);
        }

        transform.position += moveDIrection.normalized * moveSpeed * Time.deltaTime;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            Vector3 lookDirection = hit.point - transform.position;
            lookDirection.y = 0;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = targetRotation;
        }
    }
}
