using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    [SerializeField, Tooltip("Cast clip for this weapon. Keep FireMagic and EndAttack animation events.")]
    private AnimationClip attackAnimation;
    public AnimationClip AttackAnimation => attackAnimation;

    [SerializeField, Min(0f), Tooltip("Minimum time between attacks started with this weapon, in seconds.")]
    private float attackCooldown = 0.65f;
    public float AttackCooldown => Mathf.Max(0f, attackCooldown);

    public virtual bool UsesCharge => false;

    public abstract void Fire(float chargeRatio);

    public virtual void ShowChargePreview(float chargeRatio) { }
    public virtual void HideChargePreview() { }
}
