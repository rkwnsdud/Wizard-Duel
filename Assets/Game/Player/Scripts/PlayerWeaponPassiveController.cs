using System.Collections.Generic;
using UnityEngine;

/// <summary>Applies and removes the equipped weapon's passive on this player.</summary>
public sealed class PlayerWeaponPassiveController : MonoBehaviour
{
    private PlayerCombatStats combatStats;
    private WeaponBase equippedWeapon;
    private readonly List<IWeaponEquipPassive> activePassives = new List<IWeaponEquipPassive>();

    private void Awake()
    {
        EnsureCombatStats();
    }

    private void EnsureCombatStats()
    {
        if (combatStats != null) return;
        combatStats = GetComponent<PlayerCombatStats>();
        if (combatStats == null)
            combatStats = gameObject.AddComponent<PlayerCombatStats>();
    }

    public void Equip(WeaponBase weapon)
    {
        if (equippedWeapon == weapon) return;
        Unequip();
        if (weapon == null) return;

        EnsureCombatStats();
        equippedWeapon = weapon;
        foreach (MonoBehaviour component in weapon.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component is IWeaponEquipPassive passive)
            {
                if (activePassives.Count > 0)
                {
                    Debug.LogError($"Weapon '{weapon.name}' has more than one equip passive. Only one is allowed.", weapon);
                    continue;
                }
                activePassives.Add(passive);
                passive.OnEquipped(combatStats);
            }
        }
    }

    public void Unequip(WeaponBase weapon)
    {
        if (weapon == null || equippedWeapon == weapon)
            Unequip();
    }

    public void Unequip()
    {
        for (int i = activePassives.Count - 1; i >= 0; i--)
            activePassives[i].OnUnequipped(combatStats);
        activePassives.Clear();
        equippedWeapon = null;
    }

    /// <summary>서버가 받아들인 명중 한 번의 적중 효과(화상·둔화 등)를 건다.</summary>
    /// <remarks>효과는 그 공격을 시작한 무기(attack.WeaponId)로 고른다. 지금 든 무기는 보지 않으므로
    /// 쏜 뒤 무기를 바꿔도 쏜 무기의 효과만 걸린다. 공격자에게서 그 무기를 찾지 못하면 효과 없이 넘어간다
    /// (피해는 쏜 쪽 판정대로 이미 반영됐다).</remarks>
    public static bool NotifyServerHit(PlayerAttack weapons, AttackInfo attack, DuelPlayer attacker, DuelPlayer target,
        float chargeRatio)
    {
        WeaponBase weapon = weapons != null ? weapons.FindWeapon(attack.WeaponId) : null;
        if (weapon == null)
        {
            Debug.LogWarning($"[Combat] Hit passive skipped for {attack}: the attacker has no weapon with that id.", weapons);
            return false;
        }
        Dispatch(weapon, attacker, target, chargeRatio);
        return true;
    }

    static void Dispatch(WeaponBase weapon, DuelPlayer attacker, DuelPlayer target, float chargeRatio)
    {
        IWeaponHitPassive hitPassive = null;
        foreach (MonoBehaviour component in weapon.GetComponentsInChildren<MonoBehaviour>(true))
            if (component is IWeaponHitPassive passive)
            {
                if (hitPassive != null)
                {
                    Debug.LogError($"Weapon '{weapon.name}' has more than one hit passive. Only one is allowed.", weapon);
                    return;
                }
                hitPassive = passive;
            }
        hitPassive?.OnServerHit(attacker, target, chargeRatio);
    }
}
