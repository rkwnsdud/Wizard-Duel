using UnityEngine;

public class ArcaneBoltProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 18f;
    [SerializeField] private LayerMask mask;
    [SerializeField] private float hitRadius = 0.12f;

    private PlayerHealth ownerHealth;
    void Update()
    {
        float moveDistance = speed * Time.deltaTime;

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
                target.TakeDamage(10f);
            }

            transform.position += transform.forward * hit.distance;
            Destroy(gameObject);
            return;
        }

        transform.position += transform.forward * moveDistance;
    }

    public void Initialize(PlayerHealth owner)
    {
        ownerHealth = owner;
    }
}