using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10)]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerMovement : MonoBehaviour
{
    public const float BaseSpeed = PlayerMotor.BaseSpeed;
    public const float Gravity = PlayerMotor.Gravity;

    PlayerMotor motor;
    Transform cameraTransform;
    Vector3? externalDirection;

    void Awake() => motor = GetComponent<PlayerMotor>();
    void Start() => cameraTransform = Camera.main.transform;

    // 월드 방향을 유지한다. 키보드로 돌아가려면 UseKeyboard를 부른다.
    public void SetMoveDirection(Vector3 direction) => externalDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
    public void UseKeyboard() => externalDirection = null;

    void Update()
    {
        if (motor.IsAttacking) { motor.StopAnimation(); return; }
        Vector3 direction = externalDirection ?? ReadKeyboard();
        motor.Animate(direction, Time.deltaTime);
        motor.Move(direction, Time.deltaTime);
    }

    Vector3 ReadKeyboard()
    {
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
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        return (forward * vertical + right * horizontal).normalized;
    }
}
