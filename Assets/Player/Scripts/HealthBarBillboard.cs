using UnityEngine;
using UnityEngine.UI;

public class HealthBarBillboard : MonoBehaviour
{
    [SerializeField] private Image fillImage;

    private PlayerHealth playerHealth;

    private void Awake()
    {
        playerHealth = GetComponentInParent<PlayerHealth>();
    }

    private void LateUpdate()
    {
        Camera targetCamera = Camera.main;
        if (targetCamera != null)
            transform.rotation = targetCamera.transform.rotation;

        if (fillImage != null && playerHealth != null)
        {
            fillImage.fillAmount = playerHealth.MaxHealth > 0f
                ? Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth)
                : 0f;
        }
    }
}
