using UnityEngine;
using UnityEngine.Serialization;

public sealed class LightningWeapon : WeaponBase
{
    [FormerlySerializedAs("projectilePrefab")]
    [SerializeField] private LightningBolt boltPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip thunderSound;

    [SerializeField] private LightningPreview preview;
    private PlayerAim playerAim;

    private void Awake()
    {
        playerAim = GetComponentInParent<PlayerAim>();
        preview = GetComponentInChildren<LightningPreview>();
        if (preview == null) preview = gameObject.AddComponent<LightningPreview>();
    }

    private Vector3 GetAimDirection()
    {
        Transform aimTransform = playerAim != null ? playerAim.transform : transform.root;
        Vector3 direction = Vector3.ProjectOnPlane(aimTransform.forward, Vector3.up);
        return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
    }

    public override void ShowChargePreview(float chargeRatio)
    {
        if (preview == null || boltPrefab == null || firePoint == null) return;
        Vector3 start = firePoint.position;
        Vector3 direction = GetAimDirection();
        preview.ShowPreview(start, boltPrefab.GetEndPosition(start, direction, chargeRatio),
            direction, boltPrefab.GetHitWidth(chargeRatio));
    }

    public override void HideChargePreview()
    {
        if (preview != null) preview.HidePreview();
    }

    private void OnDisable()
    {
        HideChargePreview();
    }
    public override void Fire(float chargeRatio)
    {
        HideChargePreview();
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
