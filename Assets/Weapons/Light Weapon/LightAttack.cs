using UnityEngine;

public class LightAttack : MonoBehaviour
{
    [SerializeField] private LineRenderer beamCore;
    [SerializeField] private float beamSpeed = 20f;
    [SerializeField] private float maxDistance = 10f;

    private float currentDistance;
    private Vector3 direction;

    public void Initialize(Vector3 fireDirection)
    {
        beamCore.SetPosition(0, transform.position);
        beamCore.SetPosition(1, transform.position);

        direction = fireDirection;
    }


    private void Update()
    {
        currentDistance += beamSpeed * Time.deltaTime;

        if (currentDistance > maxDistance)
        {
            currentDistance = maxDistance;
        }

        Vector3 endPoint = transform.position + direction * currentDistance;
    
        beamCore.SetPosition(1, endPoint);
    }
}
