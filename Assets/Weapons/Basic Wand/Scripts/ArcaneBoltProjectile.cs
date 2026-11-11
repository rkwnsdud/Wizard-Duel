using UnityEngine;

public class ArcaneBoltProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 18f;
    [SerializeField] private float maxRange = 10f;
    [SerializeField] private LayerMask mask;
    [SerializeField] private float hitRadius = 0.12f;
    [SerializeField] private GameObject hitEffectPrefab;

    private PlayerHealth ownerHealth;
    private IShotSink onlineSink;
    private uint onlineAttackId;
    private bool visualOnly;
    private float distanceTraveled = 0f;

    void Update()
    {
        float moveDistance = Mathf.Min(speed * Time.deltaTime, maxRange - distanceTraveled);

        // DuelWizard uses Default; legacy PlayerTest characters use Player.
        int collisionMask = onlineSink != null || visualOnly ? mask.value | (1 << 0) : mask.value;
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, hitRadius, transform.forward,
                            moveDistance, collisionMask, QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            PlayerHealth target = hit.collider.GetComponentInParent<PlayerHealth>();

            if (target != null && target == ownerHealth)
            {
                continue;
            }
            if (target != null)
            {
                WeaponHitEffect.Spawn(hitEffectPrefab, hit.point);
                if (onlineSink != null) onlineSink.Hit(onlineAttackId, 0, target, 10f);
                else if (!visualOnly) target.TakeDamage(10f);
            }

            transform.position += transform.forward * hit.distance;
            Destroy(gameObject);
            return;
        }

        transform.position += transform.forward * moveDistance;
        distanceTraveled += moveDistance;

        if (distanceTraveled >= maxRange)
        {
            Destroy(gameObject);
        }
    }

    public void Initialize(PlayerHealth owner)
    {
        ownerHealth = owner;
    }

    public void InitializeOnline(PlayerHealth owner, IShotSink sink, uint attackId)
    {
        ownerHealth = owner;
        onlineSink = sink;
        onlineAttackId = attackId;
    }

    public void InitializeVisualOnly(PlayerHealth owner)
    {
        ownerHealth = owner;
        visualOnly = true;
    }
}
