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
    private bool hadOwner;
    // sink가 DuelPlayer처럼 Unity 오브젝트면 파괴 여부를 확인해야 한다.
    private Object sinkObject;

    void Update() => Advance(Time.deltaTime);

    // 편집 모드 테스트가 한 걸음씩 움직일 수 있게 이동 한 번을 나눠 둔다.
    private void Advance(float deltaTime)
    {
        if ((hadOwner && ownerHealth == null) || (!ReferenceEquals(sinkObject, null) && sinkObject == null))
        {
            // 공격자가 디스폰되면 이 공격은 끝났다. 명중을 보고하거나 명중 연출을 띄우지 않고 사라진다.
            Destroy(gameObject);
            return;
        }

        float moveDistance = Mathf.Min(speed * deltaTime, maxRange - distanceTraveled);

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
        hadOwner = owner != null;
        sinkObject = shotSink as Object;
    }

    public void InitializeVisualOnly(PlayerHealth owner) => Initialize(owner, null, default);
}
