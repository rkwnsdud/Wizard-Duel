using System.Collections.Generic;
using UnityEngine;

// 폭발 마법: 왼손에 책을 들고 오른손에서 화염 공을 쏜다.
// 공격자만 판정용 화염 공(ExplosionFireball)을 날리고, 연출은 Present로 모든 클라이언트가 같게 재생한다.
public sealed class ExplosionMagicWeapon : WeaponBase, IWeapon
{
    // ShotEvent.Kind. 발사는 Start→Point(벽 또는 사거리 끝), 폭발은 Point가 폭발 중심이다.
    public const byte LaunchKind = 0;
    public const byte ExplodeKind = 1;

    [Header("Fireball")]
    [SerializeField, Min(0.1f), Tooltip("Fireball speed in meters per second.")]
    private float speed = 12f;
    [SerializeField, Min(0f), Tooltip("Distance the fireball travels before it explodes on its own.")]
    private float range = 9f;
    [SerializeField, Min(0.01f), Tooltip("Collision radius of the flying fireball.")]
    private float hitRadius = 0.25f;
    [SerializeField, Tooltip("Layers that stop the fireball: players (Default/Player), Wall and Obstacle.")]
    private LayerMask hitMask = (1 << 0) | (1 << 3) | (1 << 9) | (1 << 10);

    [Header("Explosion")]
    [SerializeField, Min(0f), Tooltip("Explosion radius in meters.")]
    private float explosionRadius = 1.8f;
    [SerializeField, Min(0f), Tooltip("Damage applied once to each target inside the explosion.")]
    private float explosionDamage = 18f;
    [SerializeField, Tooltip("Layers searched for explosion targets.")]
    private LayerMask targetMask = (1 << 0) | (1 << 3);
    [SerializeField, Tooltip("Layers that block the explosion and the remote fireball path.")]
    private LayerMask blockerMask = (1 << 9) | (1 << 10);

    [Header("Presentation")]
    [SerializeField, Tooltip("Right palm. The fireball starts here.")]
    private Transform firePoint;
    [SerializeField] private ExplosionFireballVisual fireballPrefab;
    [SerializeField] private GameObject explosionEffectPrefab;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip castSound;
    [SerializeField] private AudioClip explosionSound;
    [SerializeField, Range(0f, 1f)] private float explosionVolume = 0.8f;

    // 날아가는 연출용 화염 공. 폭발 신호가 오면 attackId로 찾아 끈다.
    private readonly Dictionary<uint, ExplosionFireballVisual> flying = new Dictionary<uint, ExplosionFireballVisual>();
    private LocalSink localSink;
    // 오프라인 공격 id. 온라인 id(1부터 증가)와 겹치지 않게 위쪽 범위를 쓴다.
    private uint offlineAttackId = 0x80000000u;

    public float Speed => speed;
    public float Range => range;
    public float ExplosionRadius => explosionRadius;
    public float ExplosionDamage => explosionDamage;

    private Vector3 FireOrigin => firePoint != null ? firePoint.position : transform.position;

    private Vector3 AimDirection
    {
        get
        {
            PlayerAim aim = GetComponentInParent<PlayerAim>();
            return aim != null ? aim.transform.forward : transform.forward;
        }
    }

    void IWeapon.Fire(uint attackId, Vector3 direction, float charge, IShotSink sink)
    {
        if (sink == null) return;
        Launch(attackId, direction, sink);
    }

    public override void Fire()
    {
        localSink ??= new LocalSink(this);
        Launch(offlineAttackId++, AimDirection, localSink);
    }

    private void Launch(uint attackId, Vector3 direction, IShotSink sink)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = Flat(transform.forward);
        direction.Normalize();

        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        Vector3 start = SafeStart(owner, FireOrigin);
        Vector3 end = PlannedEnd(start, direction);
        // 다른 클라이언트는 이 끝점까지만 날린다. 벽은 움직이지 않으므로 공격자의 판정과 같은 자리에서 멈춘다.
        sink.Broadcast(new ShotEvent(attackId, LaunchKind, start, end, 0f));
        ExplosionFireball.Spawn(new ExplosionFireball.Settings
        {
            AttackId = attackId,
            Start = start,
            Direction = direction,
            Speed = speed,
            Range = range,
            HitRadius = hitRadius,
            HitMask = hitMask,
            ExplosionRadius = explosionRadius,
            Damage = explosionDamage,
            TargetMask = targetMask,
            BlockerMask = blockerMask,
            Owner = owner,
            Sink = sink,
        });
        Debug.Log($"[ExplosionMagic] Launch id={attackId} start={start} end={end}.");
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind == LaunchKind)
        {
            PurgeFinished();
            if (audioSource != null && castSound != null) audioSource.PlayOneShot(castSound);
            if (fireballPrefab == null) return;
            ExplosionFireballVisual visual = Instantiate(fireballPrefab, shot.Start, Quaternion.identity);
            visual.Launch(shot.Start, shot.Point, speed);
            if (flying.TryGetValue(shot.AttackId, out ExplosionFireballVisual previous) && previous != null)
                previous.Finish();
            flying[shot.AttackId] = visual;
        }
        else if (shot.Kind == ExplodeKind)
        {
            if (flying.TryGetValue(shot.AttackId, out ExplosionFireballVisual visual))
            {
                if (visual != null) visual.Finish();
                flying.Remove(shot.AttackId);
            }
            WeaponHitEffect.Spawn(explosionEffectPrefab, shot.Point);
            if (explosionSound != null) AudioSource.PlayClipAtPoint(explosionSound, shot.Point, explosionVolume);
        }
    }

    // 손이 벽 안으로 들어간 채 쏘면 화염 공이 벽을 뚫고 나가므로, 몸 중심에서 손까지 막혀 있으면 벽 앞에서 시작한다.
    private Vector3 SafeStart(PlayerHealth owner, Vector3 start)
    {
        if (owner == null) return start;
        Vector3 body = owner.transform.position;
        body.y = start.y;
        if (!Physics.Linecast(body, start, out RaycastHit block, blockerMask, QueryTriggerInteraction.Ignore))
            return start;
        return block.point + block.normal * (hitRadius * 1.05f);
    }

    private Vector3 PlannedEnd(Vector3 start, Vector3 direction)
    {
        float distance = range;
        foreach (RaycastHit hit in Physics.SphereCastAll(start, hitRadius, direction, range, blockerMask,
                     QueryTriggerInteraction.Ignore))
            distance = Mathf.Min(distance, hit.distance);
        return start + direction * distance;
    }

    private void PurgeFinished()
    {
        if (flying.Count == 0) return;
        List<uint> finished = null;
        foreach (KeyValuePair<uint, ExplosionFireballVisual> pair in flying)
            if (pair.Value == null) (finished ??= new List<uint>()).Add(pair.Key);
        if (finished == null) return;
        foreach (uint id in finished) flying.Remove(id);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // 오프라인 발사는 온라인과 같은 경로로 연출을 보내고, 피해는 바로 넣는다.
    private sealed class LocalSink : IShotSink
    {
        private readonly ExplosionMagicWeapon weapon;
        public LocalSink(ExplosionMagicWeapon weapon) => this.weapon = weapon;
        public void Broadcast(ShotEvent shot)
        {
            if (weapon != null) weapon.Present(shot);
        }
        public void Hit(uint attackId, int hitIndex, PlayerHealth target, float damage)
        {
            if (target != null) target.TakeDamage(damage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 start = FireOrigin;
        Vector3 direction = Flat(Application.isPlaying ? AimDirection : transform.root.forward);
        Vector3 end = start + direction * range;
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f);
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(start, hitRadius);
        Gizmos.color = new Color(1f, 0.25f, 0f, 0.5f);
        Gizmos.DrawWireSphere(end, explosionRadius);
    }
}
