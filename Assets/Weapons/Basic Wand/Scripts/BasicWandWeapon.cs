
using UnityEngine;

public class BasicWandWeapon : WeaponBase
{
    [SerializeField] private ArcaneBoltProjectile projectilePrefab;
    [SerializeField] private Transform firePoint;

    public override void Fire()
    {
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
        bolt.Initialize(owner);
    }
}
