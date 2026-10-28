using UnityEngine;

// Temporary IWeapon implementation for the online duel prefabs.
// The original LightningWeapon and LightningBolt remain independent.
public sealed class DuelLightningWeapon : WeaponBase, IWeapon
{
    [SerializeField] private LightningEffect effectPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightningPreview preview;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip thunderSound;
    [SerializeField] private float minRange = 2f;
    [SerializeField] private float maxRange = 8f;
    [SerializeField] private float minDamage = 5f;
    [SerializeField] private float maxDamage = 15f;
    [SerializeField] private float minHitWidth = 0.4f;
    [SerializeField] private float maxHitWidth = 0.8f;
    [SerializeField] private float hitHeight = 2f;
    [SerializeField] private LayerMask blockerMask = (1 << 9) | (1 << 10);

    public override bool UsesCharge => true;

    private Vector3 AimDirection => GetComponentInParent<PlayerAim>().transform.forward;

    public override void ShowChargePreview(float chargeRatio)
    {
        if (preview == null) return;
        Vector3 direction = AimDirection;
        preview.ShowPreview(firePoint.position, GetEndPosition(direction, chargeRatio), direction,
            Mathf.Lerp(minHitWidth, maxHitWidth, chargeRatio));
    }

    public override void HideChargePreview()
    {
        if (preview != null) preview.HidePreview();
    }

    private void OnDisable() => HideChargePreview();

    public void Fire(uint attackId, Vector3 direction, float charge, IShotSink sink)
    {
        HideChargePreview();
        ShotEvent shot = Trace(attackId, direction, charge, out PlayerHealth target);
        // A lethal hit may despawn players, so send the visual first.
        sink.Broadcast(shot);
        if (target != null)
            sink.Hit(attackId, 0, target, Mathf.Lerp(minDamage, maxDamage, charge));
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind != 0) return;
        HideChargePreview();
        if (audioSource != null && thunderSound != null)
            audioSource.PlayOneShot(thunderSound);
        LightningEffect effect = Instantiate(effectPrefab, shot.Start, Quaternion.identity);
        effect.SetEndpoints(shot.Start, shot.Point);
        effect.SetMainBoltWidth(Mathf.Lerp(0.28f, 0.5f, shot.Charge));
        effect.SetChargeRatio(shot.Charge);
    }

    // WeaponBase compatibility for this new weapon only.
    public override void Fire(float chargeRatio)
    {
        chargeRatio = Mathf.Clamp01(chargeRatio);
        ShotEvent shot = Trace(0, AimDirection, chargeRatio, out PlayerHealth target);
        Present(shot);
        if (target != null) target.TakeDamage(Mathf.Lerp(minDamage, maxDamage, chargeRatio));
    }

    private Vector3 GetEndPosition(Vector3 direction, float charge)
    {
        Vector3 start = firePoint.position;
        float range = Mathf.Lerp(minRange, maxRange, charge);
        Vector3 end = start + direction * range;
        foreach (RaycastHit hit in Physics.RaycastAll(start, direction, range, blockerMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<PlayerHealth>() != null || hit.distance >= range) continue;
            range = hit.distance;
            end = hit.point;
        }
        return end;
    }

    private ShotEvent Trace(uint id, Vector3 direction, float charge, out PlayerHealth target)
    {
        Vector3 start = firePoint.position;
        Vector3 end = GetEndPosition(direction, charge);
        Vector3 halfExtents = new Vector3(Mathf.Lerp(minHitWidth, maxHitWidth, charge) / 2f,
            hitHeight / 2f, Vector3.Distance(start, end) / 2f);
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        target = null;
        foreach (Collider hit in Physics.OverlapBox((start + end) / 2f, halfExtents,
            Quaternion.LookRotation(direction, Vector3.up)))
        {
            PlayerHealth candidate = hit.GetComponentInParent<PlayerHealth>();
            if (candidate == null || candidate == owner || candidate.IsDead) continue;
            target = candidate;
            break;
        }
        return new ShotEvent(id, 0, start, end, charge);
    }
}
