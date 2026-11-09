using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    [SerializeField, Tooltip("Cast clip for this weapon. Keep FireMagic and EndAttack animation events.")]
    private AnimationClip attackAnimation;
    public AnimationClip AttackAnimation => attackAnimation;

    // The offline duel builds its extra test weapons from existing visual assets.
    public void SetAttackAnimation(AnimationClip clip) => attackAnimation = clip;

    [SerializeField, Min(0f), Tooltip("Minimum time between attacks started with this weapon, in seconds.")]
    private float attackCooldown = 0.65f;
    public float AttackCooldown => Mathf.Max(0f, attackCooldown);

    // Local/offline shot. Online combat uses IWeapon through IAttackAuthority;
    // it must never fall back here when an online implementation is missing.
    public abstract void Fire();
}
