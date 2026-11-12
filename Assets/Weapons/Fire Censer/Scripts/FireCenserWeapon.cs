using System.Collections.Generic;
using UnityEngine;

// Online duel fire censer: IWeapon reports hits, while Fire stays for offline use.
// Melee fan attack. Reports each victim once per swing with an increasing hitIndex.
public sealed class FireCenserWeapon : WeaponBase, IWeapon, IWeaponReach
{
    [SerializeField, Tooltip("Direct damage applied to each victim in the fan.")]
    private float attackDamage = 20f;
    [SerializeField, Min(0f), Tooltip("Fan radius in meters from the fire point.")]
    private float attackRange = 4f;
    [SerializeField, Range(0f, 180f), Tooltip("Full fan angle in degrees centered on the aim direction.")]
    private float attackAngle = 100f;
    [SerializeField, Tooltip("Where the fan starts and the attack visual spawns.")]
    private Transform firePoint;
    [SerializeField, Tooltip("One-shot attack visual spawned through WeaponHitEffect.")]
    private GameObject attackEffectPrefab;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireSound;

    // The fan lands the moment it is swung, out to attackRange.
    public float ReachAt(float charge) => attackRange;
    public float ShotSpeed => 0f;

    // Owner resolution mirrors DuelLightningWeapon: aim comes from the parent player.
    Vector3 AimDirection
    {
        get
        {
            PlayerAim aim = GetComponentInParent<PlayerAim>();
            if (aim == null) return transform.forward;
            Vector3 forward = aim.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
        }
    }

    void IWeapon.Fire(uint attackId, Vector3 direction, float charge, IShotSink sink)
    {
        if (sink == null) return;
        if (direction.sqrMagnitude < 0.0001f) direction = AimDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
        direction.Normalize();

        List<PlayerHealth> victims = Trace(direction);
        Vector3 start = firePoint != null ? firePoint.position : transform.position;
        // A lethal hit may despawn players, so send the visual first.
        sink.Broadcast(new ShotEvent(attackId, 0, start, start + direction * attackRange, charge));
        for (int i = 0; i < victims.Count; i++)
            sink.Hit(attackId, i, victims[i], attackDamage);
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind != 0) return;
        if (audioSource != null && fireSound != null)
            audioSource.PlayOneShot(fireSound);
        WeaponHitEffect.Spawn(attackEffectPrefab, shot.Start);
        Debug.Log($"[Censer] Present attackId={shot.AttackId} origin={shot.Start} range={attackRange} angle={attackAngle}.");
    }

    public override void Fire()
    {
        Vector3 direction = AimDirection;
        List<PlayerHealth> victims = Trace(direction);
        Present(new ShotEvent(0, 0, firePoint != null ? firePoint.position : transform.position,
            (firePoint != null ? firePoint.position : transform.position) + direction * attackRange, 0f));
        foreach (PlayerHealth victim in victims)
            victim.TakeDamage(attackDamage);
        Debug.Log($"[Censer] Offline fire hit {victims.Count} target(s) for {attackDamage}.");
    }

    List<PlayerHealth> Trace(Vector3 direction)
    {
        List<PlayerHealth> victims = new List<PlayerHealth>();
        if (firePoint == null || attackRange <= 0f) return victims;
        Vector3 origin = firePoint.position;
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        float halfAngle = attackAngle * 0.5f;
        foreach (Collider hit in Physics.OverlapSphere(origin, attackRange, ~0, QueryTriggerInteraction.Ignore))
        {
            PlayerHealth candidate = hit.GetComponentInParent<PlayerHealth>();
            if (candidate == null || candidate == owner || candidate.IsDead) continue;
            if (victims.Contains(candidate)) continue;
            Vector3 toTarget = candidate.transform.position - origin;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > attackRange * attackRange) continue;
            if (toTarget.sqrMagnitude < 0.0001f) { victims.Add(candidate); continue; }
            if (Vector3.Angle(direction, toTarget.normalized) > halfAngle) continue;
            victims.Add(candidate);
        }
        return victims;
    }

    void OnDrawGizmosSelected()
    {
        if (firePoint == null) return;
        Vector3 origin = Application.isPlaying ? firePoint.position : firePoint.position;
        Gizmos.color = new Color(1f, 0.45f, 0f, 0.35f);
        Vector3 forward = Application.isPlaying ? AimDirection : firePoint.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        int segments = 24;
        Vector3 previous = origin + Quaternion.Euler(0f, -attackAngle * 0.5f, 0f) * forward * attackRange;
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 next = origin + Quaternion.Euler(0f, -attackAngle * 0.5f + attackAngle * t, 0f) * forward * attackRange;
            Gizmos.DrawLine(origin, next);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
