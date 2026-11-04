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

    /// <summary>Dispatches hit passives for a server-approved hit.</summary>
    public static void NotifyServerHit(WeaponBase weapon, DuelPlayer attacker, DuelPlayer target)
    {
        if (weapon == null) return;
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
        hitPassive?.OnServerHit(attacker, target);
    }
}
