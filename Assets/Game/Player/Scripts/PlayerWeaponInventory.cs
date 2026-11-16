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

    // 네트워크 슬롯과 오프라인 테스트가 같은 비활성 생성·소켓 보정을 쓴다.
    public static WeaponBase CreateWeapon(WeaponBase prefab, Transform socket)
    {
        var holder = new GameObject("Inactive weapon holder");
        holder.SetActive(false);
        holder.transform.SetParent(socket, false);
        WeaponBase weapon = Instantiate(prefab, holder.transform, false);
        weapon.gameObject.SetActive(false);
        weapon.transform.SetParent(socket, false);
        // Some existing scene-derived prefabs retain a world-sized root offset.
        // Normalize those to the socket in both inventory and offline test setup.
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

    // 이 플레이어가 이번 판에 든 적 있는 무기 인스턴스. 교체·내려놓기 뒤에도 꺼진 채 남아 있어서,
    // 이미 쏜 공격의 늦은 연출과 적중 효과를 쏜 무기로 처리할 수 있다(PlayerAttack.FindWeapon).
    public WeaponBase FindWeapon(byte id)
    {
        if (id == 0) return null;
        if (storedWeapon != null && storedWeapon.Id == id) return storedWeapon;
        return replicatedWeapons.TryGetValue(id, out WeaponBase weapon) && weapon != null ? weapon : null;
    }

    // 이 화면에서 아직 만든 적 없는 무기를 정의의 프리팹으로 꺼진 채 만든다. 교체 스냅샷보다 그 무기의 공격
    // 이벤트가 먼저 도착한 경우에 쓴다. 다음 스냅샷은 같은 인스턴스를 다시 쓴다.
    // 네트워크 인벤토리가 아니거나(오프라인 테스트) 카탈로그에 없는 id면 null이다.
    public WeaponBase CreateMissingWeapon(byte id)
    {
        if (id == 0 || NetworkAuthority == null) return null;
        if (playerAttack == null) playerAttack = GetComponent<PlayerAttack>();
        WeaponBase prefab = NetworkAuthority.FindPrefab(id);
        if (prefab == null || playerAttack == null || playerAttack.CurrentWeapon == null) return null;
        return ResolveNetworkWeapon(prefab, playerAttack.CurrentWeapon.transform.parent);
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
