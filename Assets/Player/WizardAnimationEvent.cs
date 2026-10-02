using UnityEngine;

public class WizardAnimationEvent : MonoBehaviour
{
    private PlayerAttack playerAttack;

    private void Awake()
    {
        playerAttack = GetComponentInParent<PlayerAttack>();
    }

    public void EndAttack()
    {
        playerAttack?.EndAttack();
    }

    public void FireMagic()
    {
        playerAttack?.FireMagic();
    }
}
