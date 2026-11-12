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

    public float AttackRange => attackRange;
    public float AttackAngle => attackAngle;

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
        Vector3 start = OwnerGroundPosition();
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
        Vector3 direction = shot.Point - shot.Start;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = AimDirection;
        WeaponHitEffect.Spawn(attackEffectPrefab, shot.Start, direction.normalized);
        HideTelegraph();
        Debug.Log($"[Censer] Present attackId={shot.AttackId} origin={shot.Start} range={attackRange} angle={attackAngle}.");
    }

    public override void Fire()
    {
        Vector3 direction = AimDirection;
        List<PlayerHealth> victims = Trace(direction);
        Vector3 ground = OwnerGroundPosition();
        Present(new ShotEvent(0, 0, ground,
            ground + direction * attackRange, 0f));
        foreach (PlayerHealth victim in victims)
            victim.TakeDamage(attackDamage);
        Debug.Log($"[Censer] Offline fire hit {victims.Count} target(s) for {attackDamage}.");
    }

    List<PlayerHealth> Trace(Vector3 direction)
    {
        List<PlayerHealth> victims = new List<PlayerHealth>();
        if (attackRange <= 0f) return victims;
        // The preview fan starts at the owner's feet, so the hit test uses the
        // same ground origin. The hand-held firePoint only feeds visuals.
        Vector3 ownerPos = OwnerGroundPosition();
        Vector3 sphereCenter = ownerPos + Vector3.up * 1f;
        float sphereRadius = attackRange + 2f;
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        float halfAngle = attackAngle * 0.5f;
        foreach (Collider hit in Physics.OverlapSphere(sphereCenter, sphereRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            PlayerHealth candidate = hit.GetComponentInParent<PlayerHealth>();
            if (candidate == null || candidate == owner || candidate.IsDead) continue;
            if (victims.Contains(candidate)) continue;
            // Aim is measured from the victim's root so child collider offsets
            // (crouching dummies, low hitboxes) cannot tilt the fan out of range.
            Transform targetRoot = candidate.transform;
            DuelPlayer victimPlayer = candidate.GetComponentInParent<DuelPlayer>();
            if (victimPlayer != null) targetRoot = victimPlayer.transform;
            Vector3 toTarget = targetRoot.position - ownerPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > attackRange * attackRange) continue;
            if (toTarget.sqrMagnitude < 0.0001f) { victims.Add(candidate); continue; }
            if (Vector3.Angle(direction, toTarget.normalized) > halfAngle) continue;
            victims.Add(candidate);
        }
        return victims;
    }

    Vector3 OwnerGroundPosition()
    {
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        if (owner != null) return owner.transform.position;
        DuelPlayer player = GetComponentInParent<DuelPlayer>();
        if (player != null) return player.transform.position;
        return transform.position;
    }

    /// <summary>Shows the ground fan while the swing is winding up.</summary>
    public void ShowTelegraph()
    {
        CenserTelegraph telegraph = GetComponentInChildren<CenserTelegraph>(true);
        if (telegraph != null) telegraph.Show();
    }

    /// <summary>Hides the ground fan at swing time or on cancel.</summary>
    public void HideTelegraph()
    {
        CenserTelegraph telegraph = GetComponentInChildren<CenserTelegraph>(true);
        if (telegraph != null) telegraph.Hide();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = OwnerGroundPosition();
        Gizmos.color = new Color(1f, 0.45f, 0f, 0.35f);
        Vector3 forward = Application.isPlaying ? AimDirection : transform.forward;
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
