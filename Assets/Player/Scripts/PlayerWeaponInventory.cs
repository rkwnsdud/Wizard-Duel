using Unity.VisualScripting;
using UnityEngine;

public class PlayerWeaponInventory : MonoBehaviour
{
    private PlayerAttack playerAttack;

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
}
