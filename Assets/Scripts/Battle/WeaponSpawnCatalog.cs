using System;
using UnityEngine;
using TMPro;

[CreateAssetMenu(menuName = "Wizard Duel/Weapon Spawn Catalog")]
public sealed class WeaponSpawnCatalog : ScriptableObject
{
    [SerializeField] WeaponDefinition[] definitions = Array.Empty<WeaponDefinition>();
    public WeaponDefinition[] Entries => definitions;

    [Header("Spawn notification")]
    public TMP_FontAsset notificationFont;

    public WeaponDefinition Find(byte id) => Array.Find(definitions, definition => definition.id == id);
    public byte Identify(WeaponBase weapon)
    {
        if (weapon == null) return 0;
        WeaponDefinition definition = Array.Find(definitions, item => item.prefab != null && item.prefab.GetType() == weapon.GetType());
        return definition != null ? definition.id : (byte)0;
    }

    public bool IsSpawnable(WeaponDefinition definition) => definition != null && definition.canSpawn &&
        definition.prefab != null && !(definition.prefab is BasicWandWeapon) && definition.prefab is IWeapon;
}
