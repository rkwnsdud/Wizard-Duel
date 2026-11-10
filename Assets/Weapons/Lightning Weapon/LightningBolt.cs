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

    private float damage;
    [SerializeField] private LightningEffect mainLightning;
    [SerializeField] private GameObject hitEffectPrefab;
    private PlayerHealth ownerHealth;

    private void Awake()
    {
        ownerHealth = GetComponentInParent<PlayerHealth>();
    }
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
    public void SetCharge(float chargeRatio)
    {
        chargeRatio = Mathf.Clamp01(chargeRatio);
        float boltWidth = Mathf.Lerp(minBoltWidth, maxBoltWidth, chargeRatio);
        float hitWidth = GetHitWidth(chargeRatio);
        damage = Mathf.Lerp(minDamage, maxDamage, chargeRatio);

        Vector3 startPosition = transform.position;
        Vector3 endPosition = LightningHitTrace.Trace(startPosition, transform.forward, GetRange(chargeRatio),
            hitWidth, hitHeight, blockerMask, ownerHealth, out PlayerHealth target);
        if (target != null)
        {
            WeaponHitEffect.Spawn(hitEffectPrefab, endPosition);
            target.TakeDamage(damage);
        }

        mainLightning.SetEndpoints(startPosition, endPosition);
        mainLightning.SetMainBoltWidth(boltWidth);

        mainLightning.SetChargeRatio(chargeRatio);

        Destroy(gameObject, 0.25f);
    }
}
