
using UnityEngine;

public class BasicWandWeapon : WeaponBase
{
    [SerializeField] private ArcaneBoltProjectile projectilePrefab;
    [SerializeField] private Transform firePoint;

    public override void Fire(float chargeRatio)
    {
        Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
    }
}
