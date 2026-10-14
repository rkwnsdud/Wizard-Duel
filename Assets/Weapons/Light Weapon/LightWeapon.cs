using UnityEngine;

public class LightWeapon : WeaponBase
{
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightAttack lightAttackPrefab;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lightSound;
    private PlayerAim playerAim;

    private void Awake()
    {
        playerAim = GetComponentInParent<PlayerAim>(true);
    }

    public override void Fire(float chargeRatio)
    {
        // A serialized weapon can be called before its inactive object's Awake.
        // Resolve the actual owner again instead of dropping the attack.
        if (playerAim == null)
            playerAim = GetComponentInParent<PlayerAim>(true);
        if (playerAim == null)
            throw new System.InvalidOperationException(
                $"LightWeapon '{name}' must be parented under a player with PlayerAim.");

        Vector3 direction = playerAim.transform.forward;

        direction.y = 0f;
        direction.Normalize();
      
        if (audioSource != null && lightSound != null)
        {
            audioSource.PlayOneShot(lightSound);
        }

        LightAttack attack = Instantiate(
            lightAttackPrefab,
            firePoint.position,
            Quaternion.identity
        );

        // 발사자 정보 가져오기
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();

        attack.Initialize(direction, owner, audioSource);
    }
}
