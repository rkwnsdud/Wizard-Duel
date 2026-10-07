using UnityEngine;

public class LightWeapon : WeaponBase
{
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightAttack lightAttackPrefab;

    private PlayerAim playerAim;

    private void Awake()
    {
        playerAim = GetComponentInParent<PlayerAim>();
    }

    public override void Fire(float chargeRatio)
    {
        Vector3 direction = playerAim.transform.forward;

        direction.y = 0f;
        direction.Normalize();

        LightAttack attack = Instantiate(
            lightAttackPrefab,
            firePoint.position,
            Quaternion.identity
        );

        attack.Initialize(direction);
    }
}
