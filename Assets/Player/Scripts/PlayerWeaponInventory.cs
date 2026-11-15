using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerWeaponInventory : MonoBehaviour
{
    private PlayerAttack playerAttack;

    [SerializeField]
    private WeaponBase storedWeapon;

    public WeaponBase StoredWeapon => storedWeapon;
    public event Action<WeaponBase> StoredWeaponChanged;
    public ArenaWeaponSpawner NetworkAuthority { get; set; }
    readonly Dictionary<byte, WeaponBase> replicatedWeapons = new Dictionary<byte, WeaponBase>();

    private void Awake()
    {
        playerAttack = GetComponent<PlayerAttack>();
    }

    // 줍기·교체는 서버가 확인한 슬롯 스냅샷으로만 적용한다.
    public bool TrySwapWeapon() => NetworkAuthority != null && NetworkAuthority.RequestSwap(this);
    public void TryPickupNearby() => NetworkAuthority?.RequestPickup(this);

    // Networked inventory changes call these after the local request is confirmed.
    public void PlayPickupSound() => PlayFeedback(SfxId.WeaponPickup);
    public void PlaySwapSound() => PlayFeedback(SfxId.WeaponSwap);

    // 줍기·교체 소리는 그 플레이어 본인에게만 들린다.
    private void PlayFeedback(SfxId id)
    {
        var player = GetComponent<DuelPlayer>();
        if (player != null && player.IsSpawned && !player.IsOwner) return;
        Sfx.Play(id);
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
        if (current.Id != equippedPrefab.Id && !playerAttack.CanChangeWeapon) return false;
        replicatedWeapons[current.Id] = current;
        if (storedWeapon != null) replicatedWeapons[storedWeapon.Id] = storedWeapon;
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
        if (!replicatedWeapons.TryGetValue(prefab.Id, out var weapon) || weapon == null)
        {
            weapon = CreateWeapon(prefab, socket);
            replicatedWeapons[prefab.Id] = weapon;
        }
        return weapon;
    }

}
