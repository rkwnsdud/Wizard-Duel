#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class FireCenserWeaponTests : PlayModeTestFixture
{
    static readonly Vector3 Origin = new Vector3(20000f, 1f, 20000f);
    GameObject root;
    PlayerHealth owner;
    FireCenserWeapon weapon;
    Transform firePoint;

    sealed class ShotSink : IShotSink
    {
        public readonly List<string> Order = new List<string>();
        public readonly List<(uint id, int index, PlayerHealth target, float damage)> Hits =
            new List<(uint, int, PlayerHealth, float)>();
        public void Broadcast(ShotEvent shot) => Order.Add("present");
        public void Hit(uint id, int index, PlayerHealth target, float damage)
        {
            Hits.Add((id, index, target, damage));
            Order.Add("hit");
        }
    }

    protected override IEnumerator SetUpInPlayMode()
    {
        root = new GameObject("Fire censer test owner");
        root.SetActive(false);
        root.transform.position = Origin;
        root.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        owner = root.AddComponent<PlayerHealth>();
        root.AddComponent<BoxCollider>();
        var holder = new GameObject("FireCenserWeapon");
        holder.transform.SetParent(root.transform, false);
        weapon = holder.AddComponent<FireCenserWeapon>();
        var point = new GameObject("FirePoint");
        point.transform.SetParent(holder.transform, false);
        point.transform.localPosition = new Vector3(0f, 1f, 0.5f);
        firePoint = point.transform;
        typeof(FireCenserWeapon).GetField("firePoint", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(weapon, firePoint);
        typeof(WeaponBase).GetField("attackCooldown", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(weapon, 1.5f);
        typeof(WeaponBase).GetField("attackAnimation", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(weapon, null);
        root.SetActive(true);
        yield break;
    }

    static PlayerHealth Target(Vector3 offset)
    {
        var target = new GameObject("Fire censer test target");
        target.transform.position = Origin + offset;
        target.AddComponent<BoxCollider>();
        return target.AddComponent<PlayerHealth>();
    }

    [UnityTest]
    public IEnumerator OnlineFire_HitsOnlyTargetsInsideRangeAndAngle_OnceEach()
    {
        var front = Target(new Vector3(0f, 0f, 2f));
        var side = Target(new Vector3(3f, 0f, 0.5f));
        var behind = Target(new Vector3(0f, 0f, -2f));
        var far = Target(new Vector3(0f, 0f, 6f));
        yield return null;

        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(7u, Vector3.forward, 0f, sink);

        Assert.That(sink.Hits.Count, Is.EqualTo(1));
        Assert.That(sink.Hits[0].target, Is.SameAs(front));
        Assert.That(sink.Hits[0].damage, Is.EqualTo(20f));
        Assert.That(sink.Hits[0].id, Is.EqualTo(7u));
        Assert.That(sink.Hits[0].index, Is.EqualTo(0));
        Assert.That(sink.Order, Is.EqualTo(new[] { "present", "hit" }));
    }

    [UnityTest]
    public IEnumerator OnlineFire_ReportsEachVictimWithIncreasingHitIndex()
    {
        var left = Target(new Vector3(-1f, 0f, 2f));
        var right = Target(new Vector3(1f, 0f, 2f));
        yield return null;

        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(9u, Vector3.forward, 0f, sink);

        Assert.That(sink.Hits.Count, Is.EqualTo(2));
        Assert.That(sink.Hits[0].index, Is.EqualTo(0));
        Assert.That(sink.Hits[1].index, Is.EqualTo(1));
        Assert.That(sink.Hits[0].id, Is.EqualTo(9u));
        Assert.That(sink.Hits[1].id, Is.EqualTo(9u));
    }

    [UnityTest]
    public IEnumerator EquipPassive_AppliesMoveSpeedOnceAndRemovesOnUnequip()
    {
        var statsObject = new GameObject("Fire censer stats owner");
        var stats = statsObject.AddComponent<PlayerCombatStats>();
        var passive = weapon.gameObject.AddComponent<CenserMoveSpeedPassive>();
        yield return null;

        Assert.That(stats.MoveSpeedMultiplier, Is.EqualTo(1f));
        passive.OnEquipped(stats);
        passive.OnEquipped(stats);
        Assert.That(stats.MoveSpeedMultiplier, Is.EqualTo(1.1f).Within(0.0001f));
        passive.OnUnequipped(stats);
        Assert.That(stats.MoveSpeedMultiplier, Is.EqualTo(1f));
    }
}
#endif
