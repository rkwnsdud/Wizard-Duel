// Optional charge capability. PlayerAttack owns input timing and freezes the
// ratio at release; only weapons implementing this contract receive it.
public interface IChargeableWeapon
{
    void ShowChargePreview(float chargeRatio);
    void HideChargePreview();
}
