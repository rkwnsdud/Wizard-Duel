using System;
using UnityEngine;
using TMPro;

[CreateAssetMenu(menuName = "Wizard Duel/Weapon Spawn Catalog")]
public sealed class WeaponSpawnCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public byte id;
        public WeaponBase prefab;
        public bool canSpawn = true;
        public bool alignLongestAxis = true;
        public Vector3 displayRotation;
        [Min(.1f)] public float displaySize = 1f;
        public float rotationSpeed = 30f;
    }

    [SerializeField] Entry[] entries = Array.Empty<Entry>();
    public Entry[] Entries => entries;

    [Header("Spawn notification")]
    public TMP_FontAsset notificationFont;
    public AudioClip notificationSound;
    [Range(0f, 1f)] public float notificationVolume = .22f;

    public Entry Find(byte id) => Array.Find(entries, entry => entry.id == id);
    public byte Identify(WeaponBase weapon)
    {
        if (weapon == null) return 0;
        Entry entry = Array.Find(entries, item => item.prefab != null && item.prefab.GetType() == weapon.GetType());
        return entry != null ? entry.id : (byte)0;
    }

    public bool IsSpawnable(Entry entry) => entry != null && entry.canSpawn &&
        entry.prefab != null && !(entry.prefab is BasicWandWeapon) && entry.prefab is IWeapon;
}
