// Optional charge capability. PlayerAttack owns input timing and freezes the
// ratio at release; only weapons implementing this contract receive it.
public interface IChargeableWeapon
{
    // Local/offline charged shot. Online shots receive the same frozen ratio
    // through IWeapon.Fire instead, so damage is reported via IShotSink.
    void FireCharged(float chargeRatio);
    void ShowChargePreview(float chargeRatio);
    void HideChargePreview();
}
