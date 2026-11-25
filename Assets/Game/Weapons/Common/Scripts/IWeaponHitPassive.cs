/// <summary>Provides a passive triggered by a hit that the server has accepted.</summary>
public interface IWeaponHitPassive
{
    /// <remarks>Called on the server once per accepted hit. Refresh existing timed effects instead of stacking them.</remarks>
    void OnServerHit(DuelPlayer attacker, DuelPlayer target, float chargeRatio);
}
