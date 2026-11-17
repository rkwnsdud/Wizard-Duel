using UnityEngine;

public class LightOrbProjectile : MonoBehaviour
{
    [SerializeField] private LineRenderer beamCore;
    [SerializeField] private float beamSpeed = 30f;
    [SerializeField] private float maxDistance = 20f;
    [SerializeField] private LayerMask reflectionMask;
    [SerializeField] private LayerMask hitMask;
    [SerializeField] private int maxReflections = 3;
    [SerializeField] private GameObject reflectPrefab;
    [SerializeField] private LineRenderer beamGlow;
    [SerializeField] private float damage = 10f;
    [SerializeField, Min(0f)] private float damageIncreaseRate = 0.5f; // 데미지 증가율
    [SerializeField] private GameObject hitEffectPrefab;

    public float BeamSpeed => beamSpeed;
    public float MaxDistance => maxDistance;

    private float currentDistance;
    private Vector3 currentPoint;
    private Vector3 direction;
    private int reflectionCount;
    private PlayerHealth ownerHealth;
    private LineRenderer currentBeamCore;
    private LineRenderer currentBeamGlow;
    private IShotSink sink;
    private AttackInfo attack;
    private bool hadOwner;
    // sink가 DuelPlayer처럼 Unity 오브젝트면 파괴 여부를 확인해야 한다.
    private Object sinkObject;

    // 명중은 쏠 때 받은 공격 정보로 보고한다. 반사하며 날아가는 동안 무기를 바꿔도 이 공격의 명중이다.
    public void Initialize(Vector3 fireDirection, PlayerHealth owner, IShotSink shotSink, AttackInfo attackInfo)
    {
        direction = fireDirection;
        currentPoint = transform.position;
        ownerHealth = owner;
        sink = shotSink;
        attack = attackInfo;
        hadOwner = owner != null;
        sinkObject = shotSink as Object;

        currentBeamCore = beamCore;
        currentBeamGlow = beamGlow;
        beamCore.positionCount = 2;
        beamGlow.positionCount = 2;

        beamCore.SetPosition(0, transform.position);
        beamCore.SetPosition(1, transform.position);

        beamGlow.SetPosition(0, transform.position);
        beamGlow.SetPosition(1, transform.position);

    }

    public void InitializeVisualOnly(Vector3 fireDirection, PlayerHealth owner) =>
        Initialize(fireDirection, owner, null, default);

    private LineRenderer CreateBeamSegment(LineRenderer template, Vector3 startPoint)
    {
        // 기존 설정과 활성 상태를 복제하고, LightOrbProjectile의 자식으로 함께 제거되게 한다.
        LineRenderer segment = Instantiate(template, template.transform.parent);
        segment.positionCount = 2;
        segment.SetPosition(0, startPoint);
        segment.SetPosition(1, startPoint);
        return segment;
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    // Keep one movement step deterministic for editor combat verification.
    private void Advance(float deltaTime)
    {
        if ((hadOwner && ownerHealth == null) || (!ReferenceEquals(sinkObject, null) && sinkObject == null))
        {
            // 공격자가 디스폰되면 이 공격은 끝났다. 반사·명중을 보고하거나 띄우지 않고 빛줄기째 사라진다.
            Destroy(gameObject);
            return;
        }

        float moveDistance = beamSpeed * deltaTime;
        float spareDistance = maxDistance - currentDistance;

        if (moveDistance > spareDistance)
        {
            moveDistance = spareDistance;
        }

        Vector3 nextPoint = currentPoint + direction * moveDistance; 

        RaycastHit[] hits = Physics.RaycastAll(currentPoint, direction, moveDistance, hitMask,
            QueryTriggerInteraction.Collide);
        
        // 가까운 순서대로 정렬
        System.Array.Sort(hits, (a, b) => 
            a.distance.CompareTo(b.distance));

        // 자기 자신을 제외한 가장 가까운 충돌 찾기
        RaycastHit hit = default;
        PlayerHealth targetHealth = null;
        bool hasHit = false;    
        
        foreach (RaycastHit candidate in hits)
        {
            PlayerHealth target =
                candidate.collider.GetComponentInParent<PlayerHealth>();

            if (target != null &&
               (target == ownerHealth || candidate.collider.gameObject.layer != LayerMask.NameToLayer("Hitbox")))
            {
                continue;
            }

            if (target == null && candidate.collider.isTrigger)
                continue;

            hit = candidate;
            targetHealth = target;
            hasHit = true;
            break;
        }
        if (hasHit)
        {
            // 캐릭터 피격 판정
            if (targetHealth != null)
            {
                float finalDamage = damage * (1f + damageIncreaseRate * reflectionCount);
                WeaponHitEffect.Spawn(hitEffectPrefab, hit.point);
                sink?.Hit(attack, 0, targetHealth, finalDamage);
                Destroy(gameObject);
                return;
            }

            // 반사 가능한 레이어인지 확인
            if ((reflectionMask.value & (1 << hit.collider.gameObject.layer)) == 0)
            {
                Destroy(gameObject);
                return;
            }

            // 벽 반사 처리
            Debug.Log($"Light Reflect: {hit.collider.name}, Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}", hit.collider);
            currentPoint = hit.point;
            currentDistance += hit.distance;

            Instantiate(reflectPrefab, hit.point, Quaternion.identity);

            Sfx.Play(SfxId.LightOrbReflect);

            // 현재 선 끝을 벽에 고정
            currentBeamCore.SetPosition(1, hit.point);
            currentBeamGlow.SetPosition(1, hit.point);

            // 이미 최대 횟수만큼 반사했다면
            if (reflectionCount >= maxReflections)
            {
                Destroy(gameObject);
                return;
            }
            
            // 반사 지점에서 시작하는 독립적인 2점 구간 생성
            currentBeamCore = CreateBeamSegment(beamCore, hit.point);
            currentBeamGlow = CreateBeamSegment(beamGlow, hit.point);


            direction = Vector3.Reflect(direction, hit.normal);
            // 벽 표면에서 살짝 떨어뜨리기
            currentPoint = hit.point + direction * 0.01f;
            reflectionCount++;
        }
        else
        {
            currentPoint = nextPoint;
            currentDistance += moveDistance;
        }


        currentBeamCore.SetPosition(1, currentPoint);
        currentBeamGlow.SetPosition(1, currentPoint);

        if (currentDistance >= maxDistance)
        {
            Destroy(gameObject);
        }
    }
}
