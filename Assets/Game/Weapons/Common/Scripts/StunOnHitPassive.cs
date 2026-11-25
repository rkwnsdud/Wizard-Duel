using UnityEngine;

[DisallowMultipleComponent]
public sealed class StunOnHitPassive : MonoBehaviour, IWeaponHitPassive
{
    [SerializeField] private float stunDuration = 0.5f;

    public void OnServerHit(DuelPlayer attacker, DuelPlayer target, float chargeRatio)
    {
        if (chargeRatio != 1f || target == null || !target.TryGetComponent(out PlayerStatusEffects status)) return;
        status.ServerApplyStun(stunDuration);
    }
}
