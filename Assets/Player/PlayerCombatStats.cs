using System.Collections.Generic;
using UnityEngine;

/// <summary>Collects equipment modifiers used by the player's combat systems.</summary>
public sealed class PlayerCombatStats : MonoBehaviour
{
    private readonly Dictionary<Object, float> attackSpeedModifiers = new Dictionary<Object, float>();

    public float AttackSpeedMultiplier
    {
        get
        {
            float multiplier = 1f;
            foreach (float modifier in attackSpeedModifiers.Values)
                multiplier *= modifier;
            return Mathf.Max(0.01f, multiplier);
        }
    }

    public float GetAttackCooldown(WeaponBase weapon)
    {
        return weapon == null ? 0f : weapon.AttackCooldown / AttackSpeedMultiplier;
    }

    /// <summary>Sets or replaces the attack-speed modifier owned by source.</summary>
    public void SetAttackSpeedModifier(Object source, float multiplier)
    {
        if (source == null) return;
        attackSpeedModifiers[source] = Mathf.Max(0.01f, multiplier);
    }

    /// <summary>Removes the attack-speed modifier owned by source.</summary>
    public void RemoveAttackSpeedModifier(Object source)
    {
        if (source != null) attackSpeedModifiers.Remove(source);
    }
}
