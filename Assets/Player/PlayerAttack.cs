using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    PlayerInputActions inputActions;
    [SerializeField] private GameObject lightningPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float maxChargeTime = 2f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip thunderSound;

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
        if (inputActions == null)
        {
            inputActions = new PlayerInputActions();
        }

        inputActions.Player.Attack.started += ChargeStart;
        inputActions.Player.Attack.canceled += Attack;
        inputActions.Enable();
    }

    private void OnDisable()
    {
        if (inputActions == null)
        {
            return;
        }

        inputActions.Player.Attack.started -= ChargeStart;
        inputActions.Player.Attack.canceled -= Attack;
        inputActions.Disable();
    }

    private void Attack(InputAction.CallbackContext context)
    {
        isCharging = false;
        float chargeRatio = Mathf.Clamp01(chargeTime / maxChargeTime);
        audioSource.PlayOneShot(thunderSound);
        GameObject projectile = Instantiate(lightningPrefab, firePoint.position, firePoint.rotation, firePoint);
        LightningProjectile projectileScript = projectile.GetComponent<LightningProjectile>();   
        projectileScript.SetCharge(chargeRatio);
        audioSource.PlayOneShot(thunderSound);
    }

    private void ChargeStart(InputAction.CallbackContext context)
    {
        isCharging = true;
        chargeTime = 0f;
    }

}
