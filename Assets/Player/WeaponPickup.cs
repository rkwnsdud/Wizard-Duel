
using UnityEngine;

public class WeaponPickup : MonoBehaviour
{
    [SerializeField]
    private WeaponBase weaponPrefab;

    public WeaponBase WeaponPrefab => weaponPrefab;

    private void OnTriggerEnter(Collider other)
    {
        PlayerWeaponInventory inventory =
            other.GetComponentInParent<PlayerWeaponInventory>();

        if (inventory == null)
            return;

        inventory.SetNearbyPickup(this);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerWeaponInventory inventory =
            other.GetComponentInParent<PlayerWeaponInventory>();

        if (inventory == null)
            return;

        inventory.ClearNearbyPickup(this);
    }
}

