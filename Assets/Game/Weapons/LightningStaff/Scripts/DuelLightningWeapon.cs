using UnityEngine;

// Duel lightning reports hits through the supplied sink.
public sealed class DuelLightningWeapon : WeaponBase, IChargeableWeapon, IWeapon, IWeaponReach
{
    [SerializeField] private LightningEffect effectPrefab;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightningPreview preview;
    [SerializeField] private float minRange = 2f;
    [SerializeField] private float maxRange = 8f;
    [SerializeField] private float minDamage = 5f;
    [SerializeField] private float maxDamage = 15f;
    [SerializeField] private float minHitWidth = 0.4f;
    [SerializeField] private float maxHitWidth = 0.8f;
    [SerializeField] private float hitHeight = 2f;
    [SerializeField] private LayerMask blockerMask = (1 << 9) | (1 << 10);

    public float ReachAt(float charge) => Mathf.Lerp(minRange, maxRange, Mathf.Clamp01(charge));
    // 번개는 쏘는 순간 끝까지 닿는다.
    public float ShotSpeed => 0f;

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

    void IWeapon.Fire(AttackInfo attack, Vector3 direction, float charge, IShotSink sink)
    {
        HideChargePreview();
        ShotEvent shot = Trace(attack, direction, charge, out PlayerHealth target, out IEnvironmentDestructible environment);
        // A lethal hit may despawn players, so send the visual first.
        sink.Broadcast(shot);
        if (target != null)
        {
            sink.Broadcast(new ShotEvent(attack, 1, shot.Start, GetHitPoint(target, shot), charge));
            sink.Hit(attack, 0, target, Mathf.Lerp(minDamage, maxDamage, charge));
        }
        else if (environment is DestructibleWallBlock)
            (sink as IEnvironmentShotSink)?.EnvironmentHit(attack, environment, false);
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
        Sfx.Play(SfxId.Thunder);
        LightningEffect effect = Instantiate(effectPrefab, shot.Start, Quaternion.identity);
        effect.SetEndpoints(shot.Start, shot.Point);
        effect.SetMainBoltWidth(Mathf.Lerp(0.28f, 0.5f, shot.Charge));
        effect.SetChargeRatio(shot.Charge);
    }

    private static Vector3 GetHitPoint(PlayerHealth target, ShotEvent shot)
    {
        Collider collider = target.GetComponentInChildren<Collider>();
        return collider != null ? collider.ClosestPoint((shot.Start + shot.Point) / 2f)
            : target.transform.position;
    }

    private Vector3 GetEndPosition(Vector3 direction, float charge)
    {
        return Trace(default, direction, charge, out _).Point;
    }

    private ShotEvent Trace(AttackInfo attack, Vector3 direction, float charge, out PlayerHealth target)
        => Trace(attack, direction, charge, out target, out _);

    private ShotEvent Trace(AttackInfo attack, Vector3 direction, float charge, out PlayerHealth target,
        out IEnvironmentDestructible environment)
    {
        Vector3 start = firePoint.position;
        charge = Mathf.Clamp01(charge);
        Vector3 end = LightningHitTrace.Trace(start, direction, Mathf.Lerp(minRange, maxRange, charge),
            Mathf.Lerp(minHitWidth, maxHitWidth, charge), hitHeight, blockerMask,
            GetComponentInParent<PlayerHealth>(), out target, out environment);
        return new ShotEvent(attack, 0, start, end, charge);
    }
}
