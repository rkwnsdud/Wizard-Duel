using UnityEngine;
using UnityEngine.Serialization;

public sealed class LightningWeapon : WeaponBase, IChargeableWeapon, IWeapon, IWeaponReach
{
    [FormerlySerializedAs("projectilePrefab")]
    [SerializeField] private LightningBolt boltPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightningPreview preview;
    private PlayerAim playerAim;

    private void Awake()
    {
        playerAim = GetComponentInParent<PlayerAim>();
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
        preview.ShowPreview(start, boltPrefab.GetEndPosition(start, direction, chargeRatio, GetComponentInParent<PlayerHealth>()),
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
    public float ReachAt(float charge) => boltPrefab != null ? boltPrefab.GetRange(charge) : 0f;
    public float ShotSpeed => 0f;

    void IWeapon.Fire(uint attackId, Vector3 direction, float charge, IShotSink sink)
    {
        HideChargePreview();
        if (boltPrefab == null || firePoint == null || sink == null) return;
        boltPrefab.Fire(attackId, firePoint.position, direction, charge, GetComponentInParent<PlayerHealth>(), sink);
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind == 1) { boltPrefab.PresentHit(shot.Point); return; }
        if (shot.Kind != 0) return;
        Sfx.Play(SfxId.Thunder);
        boltPrefab.Present(shot);
    }
}
