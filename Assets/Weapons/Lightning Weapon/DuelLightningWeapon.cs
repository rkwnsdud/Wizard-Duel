using UnityEngine;

// Online duel lightning: IWeapon reports hits, while Fire/FireCharged are local.
// The original LightningWeapon and LightningBolt remain independent.
public sealed class DuelLightningWeapon : WeaponBase, IChargeableWeapon, IWeapon, IWeaponReach
{
    [SerializeField] private LightningEffect effectPrefab;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightningPreview preview;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip thunderSound;
    [SerializeField, Range(0f, 1f)] private float thunderVolume = 0.3f;
    [SerializeField] private float minRange = 2f;
    [SerializeField] private float maxRange = 8f;
    [SerializeField] private float minDamage = 5f;
    [SerializeField] private float maxDamage = 15f;
    [SerializeField] private float minHitWidth = 0.4f;
    [SerializeField] private float maxHitWidth = 0.8f;
    [SerializeField] private float hitHeight = 2f;
    [SerializeField] private LayerMask blockerMask = (1 << 9) | (1 << 10);

    public float ReachAt(float charge) => Mathf.Lerp(minRange, maxRange, Mathf.Clamp01(charge));

    private Vector3 AimDirection => GetComponentInParent<PlayerAim>().transform.forward;

    public void ShowChargePreview(float chargeRatio)
    {
        if (preview == null) return;
        Vector3 direction = AimDirection;
        preview.ShowPreview(firePoint.position, GetEndPosition(direction, chargeRatio), direction,
            Mathf.Lerp(minHitWidth, maxHitWidth, chargeRatio));
    }

    public void HideChargePreview()
    {
        if (preview != null) preview.HidePreview();
    }

    private void OnDisable() => HideChargePreview();

    void IWeapon.Fire(uint attackId, Vector3 direction, float charge, IShotSink sink)
    {
        HideChargePreview();
        ShotEvent shot = Trace(attackId, direction, charge, out PlayerHealth target);
        // A lethal hit may despawn players, so send the visual first.
        sink.Broadcast(shot);
        if (target != null)
        {
            sink.Broadcast(new ShotEvent(attackId, 1, shot.Start, GetHitPoint(target, shot), charge));
            sink.Hit(attackId, 0, target, Mathf.Lerp(minDamage, maxDamage, charge));
        }
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind == 1)
        {
            WeaponHitEffect.Spawn(hitEffectPrefab, shot.Point);
            return;
        }
        if (shot.Kind != 0) return;
        HideChargePreview();
        if (audioSource != null && thunderSound != null)
            audioSource.PlayOneShot(thunderSound, thunderVolume);
        LightningEffect effect = Instantiate(effectPrefab, shot.Start, Quaternion.identity);
        effect.SetEndpoints(shot.Start, shot.Point);
        effect.SetMainBoltWidth(Mathf.Lerp(0.28f, 0.5f, shot.Charge));
        effect.SetChargeRatio(shot.Charge);
    }

    public override void Fire() => FireCharged(0f);

    // Offline only. The online IWeapon implementation above never calls this.
    public void FireCharged(float chargeRatio)
    {
        chargeRatio = Mathf.Clamp01(chargeRatio);
        ShotEvent shot = Trace(0, AimDirection, chargeRatio, out PlayerHealth target);
        Present(shot);
        if (target != null)
        {
            WeaponHitEffect.Spawn(hitEffectPrefab, GetHitPoint(target, shot));
            target.TakeDamage(Mathf.Lerp(minDamage, maxDamage, chargeRatio));
        }
    }

    private static Vector3 GetHitPoint(PlayerHealth target, ShotEvent shot)
    {
        Collider collider = target.GetComponentInChildren<Collider>();
        return collider != null ? collider.ClosestPoint((shot.Start + shot.Point) / 2f)
            : target.transform.position;
    }

    private Vector3 GetEndPosition(Vector3 direction, float charge)
    {
        return Trace(0, direction, charge, out _).Point;
    }

    private ShotEvent Trace(uint id, Vector3 direction, float charge, out PlayerHealth target)
    {
        Vector3 start = firePoint.position;
        charge = Mathf.Clamp01(charge);
        Vector3 end = LightningHitTrace.Trace(start, direction, Mathf.Lerp(minRange, maxRange, charge),
            Mathf.Lerp(minHitWidth, maxHitWidth, charge), hitHeight, blockerMask,
            GetComponentInParent<PlayerHealth>(), out target);
        return new ShotEvent(id, 0, start, end, charge);
    }
}
