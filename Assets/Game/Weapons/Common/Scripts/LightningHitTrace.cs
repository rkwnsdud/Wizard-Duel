using UnityEngine;

// Keep the charge preview, local bolt and duel bolt on the same first-hit rule.
public static class LightningHitTrace
{
    public static Vector3 Trace(Vector3 start, Vector3 direction, float range, float width,
        float height, LayerMask blockers, PlayerHealth owner, out PlayerHealth target)
    {
        direction.Normalize();
        foreach (RaycastHit hit in Physics.RaycastAll(start, direction, range, blockers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<PlayerHealth>() == null)
                range = Mathf.Min(range, hit.distance);
        }

        target = null;
        float nearest = range;
        Vector3 end = start + direction * range;
        Vector3 halfExtents = new Vector3(width / 2f, height / 2f, range / 2f);
        foreach (Collider collider in Physics.OverlapBox((start + end) / 2f, halfExtents,
            Quaternion.LookRotation(direction, Vector3.up), LayerMask.GetMask("Hitbox"), QueryTriggerInteraction.Collide))
        {
            PlayerHealth candidate = collider.GetComponentInParent<PlayerHealth>();
            if (candidate == null || candidate == owner || candidate.IsDead) continue;
            float distance = Mathf.Clamp(Vector3.Dot(collider.ClosestPoint(start) - start, direction), 0f, range);
            if (target != null && distance >= nearest) continue;
            nearest = distance;
            target = candidate;
        }
        return start + direction * nearest;
    }
}
