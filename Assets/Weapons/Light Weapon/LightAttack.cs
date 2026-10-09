using System.Net;
using UnityEngine;
using UnityEngine.Rendering;

public class LightAttack : MonoBehaviour
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

    private float currentDistance;
    private Vector3 currentPoint;
    private Vector3 direction;
    private int reflectionCount;
    private PlayerHealth ownerHealth;

    public void Initialize(Vector3 fireDirection, PlayerHealth owner)
    {
        direction = fireDirection;
        currentPoint = transform.position;
        ownerHealth = owner;
        
        beamCore.SetPosition(0, transform.position);
        beamCore.SetPosition(1, transform.position);

        beamGlow.SetPosition(0, transform.position);
        beamGlow.SetPosition(1, transform.position);

    }


    private void Update()
    {
        float moveDistance = beamSpeed * Time.deltaTime;
        float spareDistance = maxDistance - currentDistance;

        if (moveDistance > spareDistance)
        {
            moveDistance = spareDistance;
        }

        Vector3 nextPoint = currentPoint + direction * moveDistance; 

        RaycastHit[] hits = Physics.RaycastAll(currentPoint, direction, moveDistance, hitMask);
        
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

            if (target != null && target == ownerHealth)
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
                targetHealth.TakeDamage(finalDamage);
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
            currentPoint = hit.point;
            currentDistance += hit.distance;

            Instantiate(reflectPrefab, hit.point, Quaternion.identity);

            // 현재 선 끝을 벽에 고정
            beamCore.SetPosition(beamCore.positionCount - 1, hit.point);
            beamGlow.SetPosition(beamGlow.positionCount - 1, hit.point);

            // 이미 최대 횟수만큼 반사했다면
            if (reflectionCount >= maxReflections)
            {
                Destroy(gameObject);
                return;
            }
            
            // 아직 반사 가능하면 새 끝점 생성
            beamCore.positionCount++;
            beamGlow.positionCount++;
            beamCore.SetPosition(beamCore.positionCount - 1, hit.point);
            beamGlow.SetPosition(beamGlow.positionCount-1, hit.point);


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


        beamCore.SetPosition(beamCore.positionCount-1, currentPoint);
        beamGlow.SetPosition(beamGlow.positionCount - 1, currentPoint);

        if (currentDistance >= maxDistance)
        {
            Destroy(gameObject);
        }
    }
}
