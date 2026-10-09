using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    public abstract void Fire(float chargeRatio);

    public virtual void ShowChargePreview(float chargeRatio) { }
    public virtual void HideChargePreview() { }
}
