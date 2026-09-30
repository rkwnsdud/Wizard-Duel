using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    PlayerInputActions inputActions;
    [SerializeField] private GameObject lightningPrefab;
    [SerializeField] private Transform firePoint;
    private bool isCharging;
    private float chargeTime;

    private void Update()
    {
        if (isCharging)
        {
            chargeTime += Time.deltaTime;
        }
    }
    private void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Player.Attack.started += ChargeStart;
        inputActions.Enable();
    }

    private void OnDisable()
    {
        inputActions.Player.Attack.started -= ChargeStart;
        inputActions.Disable();
    }

    private void Attack(InputAction.CallbackContext context)
    {
       Instantiate(lightningPrefab, firePoint.position, firePoint.rotation);
    }

    private void ChargeStart(InputAction.CallbackContext context)
    {
        isCharging = true;
        chargeTime = 0f;
    }

}
