using UnityEngine;
using UnityEngine.Serialization;

public sealed class LightningWeapon : WeaponBase, IChargeableWeapon
{
    [FormerlySerializedAs("projectilePrefab")]
    [SerializeField] private LightningBolt boltPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip thunderSound;
    // The clip is normalized on import, so scale it here instead of the
    // player's AudioSource, which other weapons share.
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 0.3f;

    [SerializeField] private LightningPreview preview;
    private PlayerAim playerAim;

    private void Awake()
    {
        playerAim = GetComponentInParent<PlayerAim>();
        if (audioSource == null) audioSource = GetComponentInParent<AudioSource>();
        if (preview == null) preview = GetComponentInChildren<LightningPreview>(true);
    }

    private Vector3 GetAimDirection()
    {
        Transform aimTransform = playerAim != null ? playerAim.transform : transform.root;
        Vector3 direction = Vector3.ProjectOnPlane(aimTransform.forward, Vector3.up);
        return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
    }

    public void ShowChargePreview(float chargeRatio)
    {
        if (preview == null || boltPrefab == null || firePoint == null) return;
        Vector3 start = firePoint.position;
        Vector3 direction = GetAimDirection();
        preview.ShowPreview(start, boltPrefab.GetEndPosition(start, direction, chargeRatio),
            direction, boltPrefab.GetHitWidth(chargeRatio));
    }

    public void HideChargePreview()
    {
        if (preview != null) preview.HidePreview();
    }

    private void OnDisable()
    {
        HideChargePreview();
    }
    public override void Fire() => FireCharged(0f);

    public void FireCharged(float chargeRatio)
    {
        HideChargePreview();
        if (boltPrefab == null || firePoint == null)
        {
            Debug.LogError("LightningWeapon requires a bolt prefab and fire point.", this);
            return;
        }

        if (audioSource != null && thunderSound != null)
            audioSource.PlayOneShot(thunderSound, thunderVolume);

        // PlayerAim rotates the player root on the ground plane. The staff and
        // FirePoint rotate with the cast animation, so only use their position.
        Quaternion rotation = Quaternion.LookRotation(GetAimDirection(), Vector3.up);

        // Awake captures the owner while parented. Detach before building the mesh
        // so later bone movement cannot move already-fired world-space geometry.
        LightningBolt bolt = Instantiate(boltPrefab,
            firePoint.position, rotation, firePoint);
        bolt.transform.SetParent(null, true);
        bolt.transform.localScale = boltPrefab.transform.localScale;
        bolt.SetCharge(Mathf.Clamp01(chargeRatio));
    }
}
