using UnityEngine;

public class ArcaneBoltProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 18f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private float hitRadius = 0.12f;

    private PlayerHealth ownerHealth;
    void Update()
    {
        float moveDistance = speed * Time.deltaTime;

        bool hitWall = Physics.SphereCast(transform.position, hitRadius, transform.forward,
                                          out RaycastHit hit, moveDistance, wallMask, QueryTriggerInteraction.Ignore);

        if(hitWall)
        {
            transform.position += transform.forward * hit.distance;
            Destroy(gameObject);
            return;
        }
        transform.position = transform.position + transform.forward * moveDistance;
    }

    public void Initialize(PlayerHealth owner)
    {
        ownerHealth = owner;
    }
}
