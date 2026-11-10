using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Wizard Duel/Secondary Weapon HUD Catalog")]
public sealed class SecondaryWeaponHudCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public WeaponBase prefab;
        public Sprite icon;
        public string displayName;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public Entry Find(WeaponBase weapon)
    {
        if (weapon == null) return null;

        Type weaponType = weapon.GetType();
        foreach (Entry entry in entries)
        {
            if (entry != null && entry.prefab != null && entry.prefab.GetType() == weaponType)
                return entry;
        }

        return null;
    }
}
