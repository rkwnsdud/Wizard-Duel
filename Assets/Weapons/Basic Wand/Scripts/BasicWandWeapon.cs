
using UnityEngine;

public class BasicWandWeapon : WeaponBase
{
    [SerializeField] private ArcaneBoltProjectile projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireSound;

    public override void Fire()
    {
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
        bolt.Initialize(owner);
        if (audioSource != null && fireSound != null)
            audioSource.PlayOneShot(fireSound);
    }
}
