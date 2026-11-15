using UnityEngine;

public class ArcaneBoltProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 18f;
    [SerializeField] private float maxRange = 10f;
    [SerializeField] private LayerMask mask;
    [SerializeField] private float hitRadius = 0.12f;
    [SerializeField] private GameObject hitEffectPrefab;

    public float Speed => speed;
    public float MaxRange => maxRange;

    private PlayerHealth ownerHealth;
    private IShotSink sink;
    private AttackInfo attack;
    private float distanceTraveled = 0f;

    void Update()
    {
        float moveDistance = Mathf.Min(speed * Time.deltaTime, maxRange - distanceTraveled);

        RaycastHit[] hits = Physics.SphereCastAll(transform.position, hitRadius, transform.forward,
                            moveDistance, mask, QueryTriggerInteraction.Ignore);

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
                sink?.Hit(attack, 0, target, 10f);
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

    // 명중은 쏠 때 받은 공격 정보로 보고한다. 날아가는 동안 무기를 바꿔도 이 공격의 명중이다.
    public void Initialize(PlayerHealth owner, IShotSink shotSink, AttackInfo attackInfo)
    {
        ownerHealth = owner;
        sink = shotSink;
        attack = attackInfo;
    }

    public void InitializeVisualOnly(PlayerHealth owner) => Initialize(owner, null, default);
}
