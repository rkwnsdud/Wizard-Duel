using System.Net;
using UnityEngine;
using UnityEngine.Rendering;

public class LightAttack : MonoBehaviour
{
    [SerializeField] private LineRenderer beamCore;
    [SerializeField] private float beamSpeed = 30f;
    [SerializeField] private float maxDistance = 20f;
    [SerializeField] private LayerMask reflectionMask;
    [SerializeField] private int maxReflections = 3;
    [SerializeField] private GameObject reflectPrefab;
    [SerializeField] private LineRenderer beamGlow;

    private float currentDistance;
    private Vector3 currentPoint;
    private Vector3 direction;
    private int reflectionCount;

    public void Initialize(Vector3 fireDirection)
    {
        beamCore.SetPosition(0, transform.position);
        beamCore.SetPosition(1, transform.position);

        beamGlow.SetPosition(0, transform.position);
        beamGlow.SetPosition(1, transform.position);

        direction = fireDirection;
        currentPoint = transform.position;
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

        if (Physics.Raycast(
            currentPoint,
            direction,
            out RaycastHit hit,
            moveDistance,
            reflectionMask))
        {
            currentPoint= hit.point;
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
