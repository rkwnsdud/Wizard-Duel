using UnityEngine;

/// <summary>Burn + slow applied once per server-approved censer hit.</summary>
[DisallowMultipleComponent]
public sealed class CenserFlameHitPassive : MonoBehaviour, IWeaponHitPassive
{
    [SerializeField, Min(0f), Tooltip("Damage dealt by each burn tick.")]
    private float tickDamage = 3f;
    [SerializeField, Min(1), Tooltip("Burn ticks per application. 3 ticks x 1s = 3s total.")]
    private int tickCount = 3;
    [SerializeField, Min(0f), Tooltip("Burn window in seconds. Tick interval = duration / tickCount.")]
    private float burnDuration = 3f;
    [SerializeField, Range(0.01f, 1f), Tooltip("Move multiplier while slowed. 0.7 = -30%.")]
    private float slowMultiplier = 0.7f;
    [SerializeField, Min(0f), Tooltip("Slow duration in seconds. 0 disables the slow.")]
    private float slowDuration = 1.5f;

    /// <remarks>Called on the server once per accepted hit. Refreshes instead of stacking.</remarks>
    public void OnServerHit(DuelPlayer attacker, DuelPlayer target, float chargeRatio)
    {
        if (attacker == null || target == null) return;
        if (!target.TryGetComponent(out PlayerStatusEffects status))
        {
            Debug.LogWarning($"[Censer] Burn skipped: slot={target.Slot} has no PlayerStatusEffects. Attach it to the DuelWizard prefab.", target);
            return;
        }
        attacker.ServerInflictStatus(status, tickDamage, tickCount, burnDuration, slowMultiplier, slowDuration);
        Debug.Log($"[Censer] Burn applied to slot={target.Slot} tick={tickDamage}x{tickCount} slow={slowMultiplier}/{slowDuration}s.");
    }
}
