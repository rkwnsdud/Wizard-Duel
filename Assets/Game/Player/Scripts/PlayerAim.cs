using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMotor))]
public class PlayerAim : MonoBehaviour
{
    private PlayerInputActions inputActions;
    private Vector2 mousePosition;
    private PlayerMotor motor;
    private Vector3? lookTarget;

    // 외부 입력이 있는 동안 마우스가 조준을 덮지 않는다. 컴포넌트를 끌 필요가 없다.
    public void SetLookTarget(Vector3 point) => lookTarget = point;
    public void UseMouse() => lookTarget = null;

    private void Update()
    {
        if (lookTarget.HasValue)
        {
            motor.Look(lookTarget.Value - transform.position, Time.deltaTime);
            return;
        }
        if (motor.IsAttacking) return;
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);

        Plane groundPlane = new Plane(Vector3.up, transform.position);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 mouseWorldPosition = ray.GetPoint(enter);
            motor.Look(mouseWorldPosition - transform.position, Time.deltaTime);
        }
    }
    private void Awake()
    {
        inputActions = new PlayerInputActions();
        motor = GetComponent<PlayerMotor>();
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
