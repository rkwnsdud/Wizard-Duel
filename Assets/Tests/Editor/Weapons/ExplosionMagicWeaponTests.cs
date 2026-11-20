#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class ExplosionMagicWeaponTests : PlayModeTestFixture
{
    static readonly Vector3 Origin = new Vector3(21000f, 1f, 21000f);
    const int WallLayer = 9;
    static readonly LayerMask TargetMask = (1 << 0) | (1 << 3);
    static readonly LayerMask BlockerMask = (1 << 9) | (1 << 10);

    GameObject root;
    PlayerHealth owner;
    ExplosionMagicWeapon weapon;
    Transform firePoint;

    sealed class ShotSink : IShotSink, IEnvironmentShotSink
    {
        public readonly List<string> Order = new List<string>();
        public readonly List<ShotEvent> Shots = new List<ShotEvent>();
        public readonly List<(uint id, int index, PlayerHealth target, float damage)> Hits =
            new List<(uint, int, PlayerHealth, float)>();
        public readonly List<IEnvironmentDestructible> EnvironmentHits = new List<IEnvironmentDestructible>();
        public void Broadcast(ShotEvent shot)
        {
            Shots.Add(shot);
            Order.Add(shot.Kind == ExplosionMagicWeapon.LaunchKind ? "launch" : "explode");
        }
        public void Hit(AttackInfo attack, int index, PlayerHealth target, float damage)
        {
            Hits.Add((attack.Id, index, target, damage));
            Order.Add("hit");
        }
        public void EnvironmentHit(AttackInfo attack, IEnvironmentDestructible target, bool burning)
        {
            EnvironmentHits.Add(target);
        }
    }

    protected override IEnumerator SetUpInPlayMode()
    {
        root = new GameObject("Explosion magic test owner");
        root.SetActive(false);
        root.transform.SetPositionAndRotation(Origin, Quaternion.LookRotation(Vector3.forward));
        owner = root.AddComponent<PlayerHealth>();
        root.AddComponent<BoxCollider>();
        var holder = new GameObject("ExplosionMagicWeapon");
        holder.transform.SetParent(root.transform, false);
        weapon = holder.AddComponent<ExplosionMagicWeapon>();
        var point = new GameObject("FirePoint");
        point.transform.SetParent(holder.transform, false);
        point.transform.localPosition = new Vector3(0f, 0f, 0.5f);
        firePoint = point.transform;
        typeof(ExplosionMagicWeapon).GetField("firePoint", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(weapon, firePoint);
        root.SetActive(true);
        yield break;
    }

    static PlayerHealth Target(Vector3 offset)
    {
        var target = new GameObject("Explosion magic test target");
        target.transform.position = Origin + offset;
        target.AddComponent<BoxCollider>();
        return target.AddComponent<PlayerHealth>();
    }

    static void Wall(Vector3 offset, Vector3 size)
    {
        var wall = new GameObject("Explosion magic test wall") { layer = WallLayer };
        wall.transform.position = Origin + offset;
        wall.AddComponent<BoxCollider>().size = size;
    }

    // Update보다 먼저 한 번에 사거리 끝까지 진행시켜 프레임 시간과 무관하게 판정한다.
    static ExplosionFireball FlyToEnd()
    {
        ExplosionFireball fireball = Object.FindFirstObjectByType<ExplosionFireball>();
        Assert.That(fireball, Is.Not.Null, "Fire should spawn the logic fireball.");
        fireball.Advance(10f);
        Assert.That(fireball.Exploded, Is.True);
        return fireball;
    }

    [UnityTest]
    public IEnumerator Fire_BroadcastsLaunchOnce_EndingInFrontOfWall()
    {
        Wall(new Vector3(0f, 0f, 5f), new Vector3(4f, 3f, 0.2f));
        yield return null;
        Physics.SyncTransforms();

        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(3, weapon.Id), Vector3.forward, 0f, sink);

        Assert.That(sink.Order, Is.EqualTo(new[] { "launch" }));
        ShotEvent launch = sink.Shots[0];
        Assert.That(launch.AttackId, Is.EqualTo(3u));
        Assert.That(Vector3.Distance(launch.Start, firePoint.position), Is.LessThan(0.001f));
        // 벽 앞면(z=4.9)에서 충돌 반지름(0.25)만큼 앞.
        Assert.That(launch.Point.z - Origin.z, Is.EqualTo(4.65f).Within(0.02f));
        Assert.That(launch.Point.x, Is.EqualTo(Origin.x).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator Fire_WithoutWall_PlansEndAtMaxRange()
    {
        yield return null;
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(4, weapon.Id), Vector3.forward, 0f, sink);

        Assert.That(sink.Shots.Count, Is.EqualTo(1));
        Vector3 expected = firePoint.position + Vector3.forward * weapon.Range;
        Assert.That(Vector3.Distance(sink.Shots[0].Point, expected), Is.LessThan(0.001f));
    }

    [UnityTest]
    public IEnumerator Fireball_HitsTarget_ExplodesThenDamagesEveryoneInRadiusOnce()
    {
        var front = Target(new Vector3(0f, 0f, 4f));
        var near = Target(new Vector3(1.2f, 0f, 4.5f));
        var far = Target(new Vector3(3f, 0f, 4f));
        yield return null;
        Physics.SyncTransforms();

        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(7, weapon.Id), Vector3.forward, 0f, sink);
        FlyToEnd();

        Assert.That(sink.Order, Is.EqualTo(new[] { "launch", "explode", "hit", "hit" }));
        ShotEvent explode = sink.Shots[1];
        Assert.That(explode.AttackId, Is.EqualTo(7u));
        Assert.That(explode.Kind, Is.EqualTo(ExplosionMagicWeapon.ExplodeKind));
        // 앞 대상 앞면(z=3.5)에서 반지름만큼 앞에서 터진다.
        Assert.That(explode.Point.z - Origin.z, Is.EqualTo(3.25f).Within(0.02f));
        Assert.That(sink.Hits[0].index, Is.EqualTo(0));
        Assert.That(sink.Hits[1].index, Is.EqualTo(1));
        var victims = new[] { sink.Hits[0].target, sink.Hits[1].target };
        Assert.That(victims, Is.EquivalentTo(new[] { front, near }));
        Assert.That(victims, Has.No.Member(far));
        Assert.That(sink.Hits[0].damage, Is.EqualTo(18f));
        Assert.That(sink.Hits[1].id, Is.EqualTo(7u));
    }

    [UnityTest]
    public IEnumerator Fireball_HitsNothing_ExplodesAtMaxRange()
    {
        yield return null;
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(8, weapon.Id), Vector3.forward, 0f, sink);
        FlyToEnd();

        Assert.That(sink.Order, Is.EqualTo(new[] { "launch", "explode" }));
        Vector3 expected = firePoint.position + Vector3.forward * weapon.Range;
        Assert.That(Vector3.Distance(sink.Shots[1].Point, expected), Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(sink.Shots[1].Point, sink.Shots[0].Point), Is.LessThan(0.01f));
    }

    [UnityTest]
    public IEnumerator LocalSink_DamagesTargetOnceThroughTheSharedFirePath()
    {
        var front = Target(new Vector3(0f, 0f, 3f));
        yield return null;
        Physics.SyncTransforms();

        ((IWeapon)weapon).Fire(new AttackInfo(9, weapon.Id), Vector3.forward, 0f, new LocalShotSink(weapon));
        FlyToEnd();

        Assert.That(front.CurrentHealth, Is.EqualTo(front.MaxHealth - weapon.ExplosionDamage));
        Assert.That(owner.CurrentHealth, Is.EqualTo(owner.MaxHealth));
    }

    [UnityTest]
    public IEnumerator FindVictims_SkipsOwnerDeadOutOfRadiusAndTargetsBehindWall()
    {
        Vector3 center = Origin + new Vector3(0f, 0f, 1f);
        var inside = Target(new Vector3(0f, 0f, 2.2f));
        var dead = Target(new Vector3(-1.2f, 0f, 1f));
        var outside = Target(new Vector3(0f, 0f, 4f));
        var behindWall = Target(new Vector3(1.8f, 0f, 1f));
        Wall(new Vector3(1f, 0f, 1f), new Vector3(0.2f, 3f, 3f));
        yield return null;
        Physics.SyncTransforms();
        new LocalShotSink().Hit(new AttackInfo(1, weapon.Id), 0, dead, dead.MaxHealth);

        List<PlayerHealth> victims = ExplosionFireball.FindVictims(center, 1.8f, owner, TargetMask, BlockerMask);

        Assert.That(victims, Is.EquivalentTo(new[] { inside }));
        Assert.That(victims, Has.No.Member(owner));
        Assert.That(victims, Has.No.Member(dead));
        Assert.That(victims, Has.No.Member(outside));
        Assert.That(victims, Has.No.Member(behindWall));
    }

    [UnityTest]
    public IEnumerator Explosion_DoesNotTreatTheCandidateWallAsAnOccluder()
    {
        var wallObject = new GameObject("Blast target wall") { layer = WallLayer };
        wallObject.transform.position = Origin + new Vector3(1.1f, 0f, 0f);
        wallObject.AddComponent<BoxCollider>().size = new Vector3(.4f, 2f, 1f);
        DestructibleWallBlock wall = wallObject.AddComponent<DestructibleWallBlock>();
        yield return null;
        Physics.SyncTransforms();

        var sink = new ShotSink();
        ExplosionFireball fireball = ExplosionFireball.Spawn(new ExplosionFireball.Settings
        {
            Attack = new AttackInfo(18, weapon.Id),
            Start = Origin,
            Direction = Vector3.forward,
            Range = 0f,
            ExplosionRadius = 2f,
            BlockerMask = BlockerMask,
            Sink = sink
        });
        fireball.Advance(0f);

        Assert.That(sink.EnvironmentHits, Is.EqualTo(new IEnvironmentDestructible[] { wall }));
    }

}

// 트랜스폼 계산뿐이라 플레이 모드 없이 가벼운 테스트로 돈다.
public class TwoBoneIKTests
{
    [Test]
    public void Solve_ReachesTargetAndKeepsBoneLengths()
    {
        var shoulder = new GameObject("IK shoulder") { hideFlags = HideFlags.HideAndDontSave }.transform;
        shoulder.position = new Vector3(5f, 0f, 0f);
        var elbow = new GameObject("IK elbow").transform;
        elbow.SetParent(shoulder, false);
        elbow.localPosition = new Vector3(0f, 1f, 0f);
        var hand = new GameObject("IK hand").transform;
        hand.SetParent(elbow, false);
        hand.localPosition = new Vector3(0f, 1f, 0f);

        Vector3 target = shoulder.position + new Vector3(1f, 1f, 0.5f);
        Vector3 hint = shoulder.position + new Vector3(0f, 0f, 1f);
        TwoBoneIK.Solve(shoulder, elbow, hand, target, hint);

        Assert.That(Vector3.Distance(hand.position, target), Is.LessThan(0.001f));
        Assert.That(Vector3.Distance(shoulder.position, elbow.position), Is.EqualTo(1f).Within(0.001f));
        Assert.That(Vector3.Distance(elbow.position, hand.position), Is.EqualTo(1f).Within(0.001f));
        // 팔꿈치는 어깨-손 축을 기준으로 hint 쪽에 있다.
        Vector3 axis = (target - shoulder.position).normalized;
        Vector3 elbowSide = Vector3.ProjectOnPlane(elbow.position - shoulder.position, axis);
        Vector3 hintSide = Vector3.ProjectOnPlane(hint - shoulder.position, axis);
        Assert.That(Vector3.Dot(elbowSide, hintSide), Is.GreaterThan(0f));
        Object.DestroyImmediate(shoulder.gameObject);
    }
}
#endif
