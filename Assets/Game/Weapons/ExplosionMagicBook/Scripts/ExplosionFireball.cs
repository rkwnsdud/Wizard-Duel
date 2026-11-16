using System.Collections.Generic;
using UnityEngine;

// 공격자에게만 있는 판정용 화염 공. 보이지 않으며, 연출은 ExplosionMagicWeapon.Present가 맡는다.
// 상대·벽·장애물에 닿거나 사거리 끝에 도달하면 폭발을 알리고, 반경 안의 상대마다 hitIndex를 늘려 피해를 보고한다.
public sealed class ExplosionFireball : MonoBehaviour
{
    public struct Settings
    {
        public AttackInfo Attack;
        public Vector3 Start;
        public Vector3 Direction;
        public float Speed;
        public float Range;
        public float HitRadius;
        public LayerMask HitMask;
        public float ExplosionRadius;
        public float Damage;
        public LayerMask TargetMask;
        public LayerMask BlockerMask;
        public PlayerHealth Owner;
        public IShotSink Sink;
    }

    private Settings settings;
    private float traveled;
    private bool exploded;
    private bool hadOwner;
    // sink가 DuelPlayer처럼 Unity 오브젝트면 파괴 여부를 확인해야 한다.
    private Object sinkObject;

    public bool Exploded => exploded;

    public static ExplosionFireball Spawn(Settings settings)
    {
        var go = new GameObject($"ExplosionFireball #{settings.Attack.Id}");
        go.transform.SetPositionAndRotation(settings.Start,
            Quaternion.LookRotation(settings.Direction, Vector3.up));
        ExplosionFireball fireball = go.AddComponent<ExplosionFireball>();
        fireball.settings = settings;
        fireball.hadOwner = settings.Owner != null;
        fireball.sinkObject = settings.Sink as Object;
        return fireball;
    }

    private void Update() => Advance(Time.deltaTime);

    public void Advance(float deltaTime)
    {
        if (exploded) return;
        if ((hadOwner && settings.Owner == null) || (!ReferenceEquals(sinkObject, null) && sinkObject == null))
        {
            // 공격자가 사라졌으면 보고할 곳이 없다.
            exploded = true;
            Destroy(gameObject);
            return;
        }

        Vector3 position = transform.position;
        Vector3 direction = settings.Direction;
        float step = Mathf.Min(settings.Speed * deltaTime, settings.Range - traveled);
        if (step > 0f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(position, settings.HitRadius, direction, step,
                settings.HitMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (IsOwner(hit.collider)) continue;
                PlayerHealth target = hit.collider.GetComponentInParent<PlayerHealth>();
                if (target != null && target.IsDead) continue;
                // 구체 중심에서 터지므로 벽에 닿으면 표면보다 반지름만큼 앞에서 터진다.
                Explode(position + direction * hit.distance);
                return;
            }
            position += direction * step;
            traveled += step;
            transform.position = position;
        }
        if (traveled >= settings.Range - 0.0001f) Explode(position);
    }

    private bool IsOwner(Collider collider)
    {
        if (settings.Owner == null) return false;
        return collider.transform.IsChildOf(settings.Owner.transform);
    }

    private void Explode(Vector3 center)
    {
        exploded = true;
        transform.position = center;
        // 명중으로 승부가 나면 플레이어가 정리될 수 있으므로 대상은 미리 고르고 연출을 먼저 보낸다.
        List<PlayerHealth> victims = FindVictims(center, settings.ExplosionRadius, settings.Owner,
            settings.TargetMask, settings.BlockerMask);
        settings.Sink.Broadcast(new ShotEvent(settings.Attack, ExplosionMagicWeapon.ExplodeKind,
            settings.Start, center, 0f));
        // 폭발 한 번은 대상마다 다른 타격 번호로 한 번씩 보고한다.
        for (int i = 0; i < victims.Count; i++)
            settings.Sink.Hit(settings.Attack, i, victims[i], settings.Damage);
        Debug.Log($"[ExplosionMagic] Explode {settings.Attack} center={center} victims={victims.Count}.");
        Destroy(gameObject);
    }

    // 폭발 반경 안의 상대. 시전자·사망자는 빼고, 중심과 대상 사이가 벽으로 막혀 있으면 뺀다.
    public static List<PlayerHealth> FindVictims(Vector3 center, float radius, PlayerHealth owner,
        LayerMask targetMask, LayerMask blockerMask)
    {
        var victims = new List<PlayerHealth>();
        if (radius <= 0f) return victims;
        foreach (Collider collider in Physics.OverlapSphere(center, radius, targetMask,
                     QueryTriggerInteraction.Ignore))
        {
            PlayerHealth candidate = collider.GetComponentInParent<PlayerHealth>();
            if (candidate == null || candidate == owner || candidate.IsDead || victims.Contains(candidate))
                continue;
            if (Physics.Linecast(center, collider.bounds.center, blockerMask, QueryTriggerInteraction.Ignore))
                continue;
            victims.Add(candidate);
        }
        return victims;
    }
}
