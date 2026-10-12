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
        playerAim = GetComponentInParent<PlayerAim>();
    }

    public override void Fire(float chargeRatio)
    {
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
