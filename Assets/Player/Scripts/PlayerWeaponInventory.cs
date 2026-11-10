using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerWeaponInventory : MonoBehaviour
{
    private PlayerAttack playerAttack;
    private WeaponPickup nearbyPickup;

    [SerializeField]
    private WeaponBase storedWeapon;

    public WeaponBase StoredWeapon => storedWeapon;
    public event Action<WeaponBase> StoredWeaponChanged;
    public ArenaWeaponSpawner NetworkAuthority { get; set; }
    readonly Dictionary<Type, WeaponBase> replicatedWeapons = new Dictionary<Type, WeaponBase>();

    private void Awake()
    {
        playerAttack = GetComponent<PlayerAttack>();
    }

    // 보조 슬롯에 무기 저장
    public bool TryStoreWeapon(WeaponBase weapon)
    {
        if (weapon == null) return false;
        if (storedWeapon != null) return false;
        if (weapon == playerAttack.CurrentWeapon) return false;

        storedWeapon = weapon;
        StoredWeaponChanged?.Invoke(storedWeapon);
        return true;
    }

    // 현재 장착 무기와 보조 무기 교환
    public bool TrySwapWeapon()
    {
        if (NetworkAuthority != null) return NetworkAuthority.RequestSwap(this);
        if (storedWeapon == null)
            return false;

        WeaponBase previousWeapon = playerAttack.CurrentWeapon;

        if (previousWeapon == null)
            return false;

        if (!playerAttack.TryEquipWeapon(storedWeapon))
            return false;

        storedWeapon = previousWeapon;
        StoredWeaponChanged?.Invoke(storedWeapon);
        return true;
    }

    // 근처 무기 등록
    public void SetNearbyPickup(WeaponPickup pickup)
    {
        nearbyPickup = pickup;
    }

    // 근처 무기 등록 해제
    public void ClearNearbyPickup(WeaponPickup pickup)
    {
        if (nearbyPickup == pickup)
            nearbyPickup = null;
    }

    // E키를 눌렀을 때 실행
    public void TryPickupNearby()
    {
        if (NetworkAuthority != null)
        {
            NetworkAuthority.RequestPickup(this);
            return;
        }
        if (nearbyPickup == null)
            return;

        WeaponBase weaponPrefab = nearbyPickup.WeaponPrefab;

        if (weaponPrefab == null)
            return;

        if (HasSameWeaponType(weaponPrefab))
        {
            Debug.Log("이미 보유중인 무기입니다.");
            return;
        }

        WeaponBase currentWeapon = playerAttack.CurrentWeapon;

        if (currentWeapon == null)
            return;

        Transform weaponSocket = currentWeapon.transform.parent;

        bool success;

        if (storedWeapon == null)
        {
            success = TryFillEmptySlot(weaponPrefab, weaponSocket);
        }
        else
        {
            success = TryReplaceEquippedWeapon(weaponPrefab, weaponSocket);
        }

        if (success)
        {
            Destroy(nearbyPickup.gameObject);
            nearbyPickup = null;
        }
    }

    // 무기 프리팹으로 실제 무기 생성
    private WeaponBase CreateWeapon(WeaponBase prefab, Transform socket)
    {
        var holder = new GameObject("Inactive weapon holder");
        holder.SetActive(false);
        holder.transform.SetParent(socket, false);
        WeaponBase weapon = Instantiate(prefab, holder.transform, false);
        weapon.gameObject.SetActive(false);
        weapon.transform.SetParent(socket, false);
        // Some existing scene-derived prefabs retain a world-sized root offset.
        // Match the existing DuelOfflineTest socket normalization.
        if (weapon.transform.localPosition.sqrMagnitude > 1f)
            weapon.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Destroy(holder);
        return weapon;
    }

    // Only the authoritative snapshot calls this. Reuse each weapon instance so
    // a swap preserves its pose/animation references and never duplicates models.
    public bool ApplyNetworkSlots(WeaponBase equippedPrefab, WeaponBase storedPrefab)
    {
        if (playerAttack == null) playerAttack = GetComponent<PlayerAttack>();
        if (equippedPrefab == null || playerAttack == null || playerAttack.CurrentWeapon == null) return false;
        WeaponBase current = playerAttack.CurrentWeapon;
        if (current.GetType() != equippedPrefab.GetType() && !playerAttack.CanChangeWeapon) return false;
        replicatedWeapons[current.GetType()] = current;
        if (storedWeapon != null) replicatedWeapons[storedWeapon.GetType()] = storedWeapon;
        Transform socket = current.transform.parent;
        WeaponBase equipped = ResolveNetworkWeapon(equippedPrefab, socket);
        WeaponBase stored = storedPrefab != null ? ResolveNetworkWeapon(storedPrefab, socket) : null;
        if (equipped != current && !playerAttack.TryEquipWeapon(equipped)) return false;
        if (stored != null && stored != equipped) stored.gameObject.SetActive(false);
        if (storedWeapon != stored)
        {
            storedWeapon = stored;
            StoredWeaponChanged?.Invoke(storedWeapon);
        }
        return true;
    }

    WeaponBase ResolveNetworkWeapon(WeaponBase prefab, Transform socket)
    {
        if (!replicatedWeapons.TryGetValue(prefab.GetType(), out var weapon) || weapon == null)
        {
            weapon = CreateWeapon(prefab, socket);
            replicatedWeapons[prefab.GetType()] = weapon;
        }
        return weapon;
    }

    // 보조 슬롯이 비어 있을 때 무기 저장
    private bool TryFillEmptySlot(WeaponBase prefab, Transform socket)
    {
        if (storedWeapon != null)
            return false;

        WeaponBase newWeapon = CreateWeapon(prefab, socket);

        if (!TryStoreWeapon(newWeapon))
        {
            Destroy(newWeapon.gameObject);
            return false;
        }

        return true;
    }

    // 보조 슬롯이 차 있을 때 현재 장착 무기 교체
    private bool TryReplaceEquippedWeapon(WeaponBase prefab, Transform socket)
    {
        WeaponBase oldWeapon = playerAttack.CurrentWeapon;

        if (oldWeapon == null)
            return false;

        WeaponBase newWeapon = CreateWeapon(prefab, socket);

        if (!playerAttack.TryEquipWeapon(newWeapon))
        {
            Destroy(newWeapon.gameObject);
            return false;
        }

        Destroy(oldWeapon.gameObject);

        return true;
    }

    // 중복 무기 확인
    private bool HasSameWeaponType(WeaponBase prefab)
    {
        WeaponBase currentWeapon = playerAttack.CurrentWeapon;

        if (currentWeapon != null && currentWeapon.GetType() == prefab.GetType())
            return true;

        if(storedWeapon != null && storedWeapon.GetType() == prefab.GetType())
            return true;

        return false;
    }
}
