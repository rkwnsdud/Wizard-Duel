using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    [SerializeField, Tooltip("Cast clip for this weapon. Keep FireMagic and EndAttack animation events.")]
    private AnimationClip attackAnimation;
    public AnimationClip AttackAnimation => attackAnimation;

    // Local/offline shot. Online combat uses IWeapon through IAttackAuthority;
    // it must never fall back here when an online implementation is missing.
    public abstract void Fire();
}
