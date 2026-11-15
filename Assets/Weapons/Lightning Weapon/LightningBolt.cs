using UnityEngine;

public class LightningBolt : MonoBehaviour
{
    [SerializeField] private float minRange = 4f;
    [SerializeField] private float maxRange = 12f;

    [SerializeField] private float minBoltWidth = 0.18f;
    [SerializeField] private float maxBoltWidth = 0.35f;

    [SerializeField] private float minDamage = 10f;
    [SerializeField] private float maxDamage = 13f;

    [SerializeField] private float minHitWidth = 0.4f;
    [SerializeField] private float maxHitWidth = 0.8f;
    [SerializeField] private float hitHeight = 2f;

    [SerializeField] private LayerMask blockerMask = (1 << 9) | (1 << 10); // Wall and Obstacle; excludes Floor.

    [SerializeField] private LightningEffect mainLightning;
    [SerializeField] private GameObject hitEffectPrefab;

    public float GetRange(float chargeRatio)
    {
        return Mathf.Lerp(minRange, maxRange, Mathf.Clamp01(chargeRatio));
    }

    public float GetHitWidth(float chargeRatio)
    {
        return Mathf.Lerp(minHitWidth, maxHitWidth, Mathf.Clamp01(chargeRatio));
    }

    // Shared by preview and actual bolt, including existing wall filtering.
    public Vector3 GetEndPosition(Vector3 start, Vector3 direction, float chargeRatio, PlayerHealth owner = null)
    {
        return LightningHitTrace.Trace(start, direction, GetRange(chargeRatio), GetHitWidth(chargeRatio),
            hitHeight, blockerMask, owner != null ? owner : GetComponentInParent<PlayerHealth>(), out _);
    }
    public void Fire(uint attackId, Vector3 start, Vector3 direction, float charge, PlayerHealth owner, IShotSink sink)
    {
        charge = Mathf.Clamp01(charge);
        Vector3 end = LightningHitTrace.Trace(start, direction, GetRange(charge), GetHitWidth(charge),
            hitHeight, blockerMask, owner, out PlayerHealth target);
        sink.Broadcast(new ShotEvent(attackId, 0, start, end, charge));
        if (target == null) return;
        sink.Broadcast(new ShotEvent(attackId, 1, start, end, charge));
        sink.Hit(attackId, 0, target, Mathf.Lerp(minDamage, maxDamage, charge));
    }

    public void PresentHit(Vector3 point) => WeaponHitEffect.Spawn(hitEffectPrefab, point);

    public void Present(ShotEvent shot)
    {
        LightningBolt visual = Instantiate(this, shot.Start, Quaternion.identity);
        visual.mainLightning.SetEndpoints(shot.Start, shot.Point);
        visual.mainLightning.SetMainBoltWidth(Mathf.Lerp(minBoltWidth, maxBoltWidth, shot.Charge));
        visual.mainLightning.SetChargeRatio(shot.Charge);
        Destroy(visual.gameObject, 0.25f);
    }
}
