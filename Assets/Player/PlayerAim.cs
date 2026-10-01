using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAim : MonoBehaviour
{
    private PlayerInputActions inputActions;
    private Vector2 mousePosition;

    private void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);

        Plane groundPlane = new Plane(Vector3.up, transform.position);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 mouseWorldPosition = ray.GetPoint(enter);
            Vector3 direction = mouseWorldPosition - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }
    private void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Player.Aim.performed += OnAim;
        inputActions.Enable();
    }
    
    private void OnDisable()
    {
        inputActions.Player.Aim.performed -= OnAim;
        inputActions.Disable();
    }

    private void OnAim(InputAction.CallbackContext context)
    {
        mousePosition = context.ReadValue<Vector2>();
    }
}
