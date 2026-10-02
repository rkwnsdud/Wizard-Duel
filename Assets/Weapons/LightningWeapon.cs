using UnityEngine;
using UnityEngine.Serialization;

public sealed class LightningWeapon : WeaponBase
{
    [FormerlySerializedAs("projectilePrefab")]
    [SerializeField] private LightningBolt boltPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip thunderSound;

    public override void Fire(float chargeRatio)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Temporary charge diagnostics for Play mode testing.
        Debug.Log($"[Charge] LightningWeapon.Fire chargeRatio={chargeRatio:F3}, prefab={boltPrefab}, firePoint={firePoint}", this);
#endif
        if (boltPrefab == null || firePoint == null)
        {
            Debug.LogError("LightningWeapon requires a bolt prefab and fire point.", this);
            return;
        }

        if (audioSource != null && thunderSound != null)
            audioSource.PlayOneShot(thunderSound);

        // PlayerAim rotates the player root on the ground plane. The staff and
        // FirePoint rotate with the cast animation, so only use their position.
        PlayerAim playerAim = GetComponentInParent<PlayerAim>();
        Transform aimTransform = playerAim != null ? playerAim.transform : transform.root;
        Vector3 direction = Vector3.ProjectOnPlane(aimTransform.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        // Awake captures the owner while parented. Detach before building the mesh
        // so later bone movement cannot move already-fired world-space geometry.
        LightningBolt bolt = Instantiate(boltPrefab,
            firePoint.position, rotation, firePoint);
        bolt.transform.SetParent(null, true);
        bolt.transform.localScale = boltPrefab.transform.localScale;
        bolt.SetCharge(Mathf.Clamp01(chargeRatio));
    }
}
