using UnityEngine;

public class LightningProjectile : MonoBehaviour
{
    [SerializeField] private float minRange = 4f;
    [SerializeField] private float maxRange = 12f;
    [SerializeField] private float minBoltWidth = 0.18f;
    [SerializeField] private float maxBoltWidth = 0.35f;
    [SerializeField] private float minDamage = 10f;
    [SerializeField] private float maxDamage = 13f;

    private float damage;
    private LightningEffect lightningEffect;

    private void Awake()
    {
         lightningEffect = GetComponentInChildren<LightningEffect>();
    }
    public void SetCharge(float chargeRatio)
    {
        float range = Mathf.Lerp(minRange, maxRange, chargeRatio);
        float boltWidth = Mathf.Lerp(minBoltWidth, maxBoltWidth, chargeRatio);
        damage = Mathf.Lerp(minDamage, maxDamage, chargeRatio);

        Vector3 startPosition = transform.position;
        Vector3 endPosition = transform.position + transform.forward * range;

        lightningEffect.SetEndpoints(startPosition, endPosition);
        lightningEffect.SetMainBoltWidth(boltWidth);
     }
}
