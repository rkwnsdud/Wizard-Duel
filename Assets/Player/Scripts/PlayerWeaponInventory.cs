using Unity.VisualScripting;
using UnityEngine;

public class PlayerWeaponInventory : MonoBehaviour
{
    private PlayerAttack playerAttack;
    private WeaponPickup nearbyPickup;

    [SerializeField]
    private WeaponBase storedWeapon;

    private void Awake()
    {
        playerAttack = GetComponent<PlayerAttack>();
    }

    public bool TryStoreWeapon(WeaponBase weapon)
    {
        if (weapon == null) return false;
        if (storedWeapon != null) return false;
        if (weapon == playerAttack.CurrentWeapon) return false;

        storedWeapon = weapon;
        return true;
    }

    public bool TrySwapWeapon()
    {
        if (storedWeapon == null) return false;

        WeaponBase previousWeapon = playerAttack.CurrentWeapon;

        if (previousWeapon == null)
            return false;

        if (!playerAttack.TryEquipWeapon(storedWeapon))
            return false;

        storedWeapon = previousWeapon;
        return true;
    }
    public void SetNearbyPickup(WeaponPickup pickup)
    {
        nearbyPickup = pickup;
    }

    public void ClearNearbyPickup(WeaponPickup pickup)
    {
        if (nearbyPickup == pickup)
            nearbyPickup = null;
    }
}
