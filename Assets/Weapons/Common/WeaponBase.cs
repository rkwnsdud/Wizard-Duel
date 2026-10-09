using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    public virtual bool UsesCharge => false;

    public abstract void Fire(float chargeRatio);

    public virtual void ShowChargePreview(float chargeRatio) { }
    public virtual void HideChargePreview() { }
}
