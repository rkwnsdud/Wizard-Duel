using UnityEngine;

public class LightningProjectile : MonoBehaviour
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

    [SerializeField] private LayerMask wallLayer = ~0;

    private float damage;
    [SerializeField] private LightningEffect mainLightning;
    private PlayerHealth ownerHealth;

    private void Awake()
    {
        ownerHealth = GetComponentInParent<PlayerHealth>();
    }
    public void SetCharge(float chargeRatio)
    {
        float range = Mathf.Lerp(minRange, maxRange, chargeRatio);
        float boltWidth = Mathf.Lerp(minBoltWidth, maxBoltWidth, chargeRatio);
        float hitWidth = Mathf.Lerp(minHitWidth, maxHitWidth, chargeRatio);
        damage = Mathf.Lerp(minDamage, maxDamage, chargeRatio);

        Vector3 startPosition = transform.position;
        Vector3 endPosition = transform.position + transform.forward * range;

        // 벽에 맞으면 맞히게 설정
        float wallDistance = range;
        foreach (RaycastHit hit in Physics.RaycastAll(startPosition, transform.forward, range, wallLayer, QueryTriggerInteraction.Ignore))
        {
            // Player colliders are handled by the damage box, not as walls.
            if (hit.collider.GetComponentInParent<PlayerHealth>() != null || hit.distance >= wallDistance)
            {
                continue;
            }

            wallDistance = hit.distance;
            endPosition = hit.point;
        }

        // 타격 박스 형성
        Vector3 center = (startPosition + endPosition) / 2f;
        float length = Vector3.Distance(startPosition, endPosition);
        Vector3 halfExtents = new Vector3(hitWidth / 2f, hitHeight / 2f, length / 2f);
        Collider[] hits = Physics.OverlapBox(center, halfExtents, transform.rotation);
        foreach (Collider hitCollider in hits)
        {
            PlayerHealth playerHealth = hitCollider.GetComponentInParent<PlayerHealth>();
            
            if (playerHealth == null)
            {
                continue;
            }

            if (playerHealth == ownerHealth)
            {
                continue;
            }

            Debug.Log("플레이어 감지: " + playerHealth.gameObject.name);
            playerHealth.TakeDamage(damage);
            break;
        }

        mainLightning.SetEndpoints(startPosition, endPosition);
        mainLightning.SetMainBoltWidth(boltWidth);

        mainLightning.SetChargeRatio(chargeRatio);

        Destroy(gameObject, 0.25f);
    }
}
