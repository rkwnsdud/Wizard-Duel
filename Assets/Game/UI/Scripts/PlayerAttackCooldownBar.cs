using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays the equipped player's remaining weapon attack cooldown as a shrinking UI line.</summary>
public sealed class PlayerAttackCooldownBar : MonoBehaviour
{
    [SerializeField] private PlayerAttack playerAttack;
    [SerializeField] private Image fillImage;

    private void Awake()
    {
        if (playerAttack == null)
            playerAttack = GetComponentInParent<PlayerAttack>();
        if (fillImage == null)
            fillImage = GetComponent<Image>();

        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;
            fillImage.enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (fillImage == null || playerAttack == null)
            return;

        bool coolingDown = playerAttack.IsOnCooldown;
        fillImage.enabled = coolingDown;
        fillImage.fillAmount = coolingDown ? playerAttack.CooldownRemainingFraction : 0f;
    }
}
