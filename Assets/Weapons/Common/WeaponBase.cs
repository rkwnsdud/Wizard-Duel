using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    [SerializeField] private WeaponDefinition definition;
    public WeaponDefinition Definition => definition;
    public byte Id => definition != null ? definition.id : (byte)0;

    [SerializeField, Tooltip("Cast clip for this weapon. Keep FireMagic and EndAttack animation events.")]
    private AnimationClip attackAnimation;
    public AnimationClip AttackAnimation => attackAnimation;

    [SerializeField, Min(0.01f), Tooltip("Playback speed for the attack animation. 2 = twice as fast.")]
    private float attackAnimSpeed = 1f;
    public float AttackAnimSpeed => Mathf.Max(0.01f, attackAnimSpeed);

    [SerializeField, Min(0f), Tooltip("Minimum time between attacks started with this weapon, in seconds.")]
    private float attackCooldown = 0.65f;
    public float AttackCooldown => Mathf.Max(0f, attackCooldown);

}
