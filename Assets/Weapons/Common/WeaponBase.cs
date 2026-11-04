using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    [SerializeField, Tooltip("Cast clip for this weapon. Keep FireMagic and EndAttack animation events.")]
    private AnimationClip attackAnimation;
    public AnimationClip AttackAnimation => attackAnimation;

    public virtual bool UsesCharge => false;

    public abstract void Fire(float chargeRatio);

    public virtual void ShowChargePreview(float chargeRatio) { }
    public virtual void HideChargePreview() { }
}
