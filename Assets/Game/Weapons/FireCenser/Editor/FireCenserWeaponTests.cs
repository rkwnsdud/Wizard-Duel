#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
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
        public void Hit(AttackInfo attack, int index, PlayerHealth target, float damage)
        {
            Hits.Add((attack.Id, index, target, damage));
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
        ((IWeapon)weapon).Fire(new AttackInfo(7, weapon.Id), Vector3.forward, 0f, sink);

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
        ((IWeapon)weapon).Fire(new AttackInfo(9, weapon.Id), Vector3.forward, 0f, sink);

        Assert.That(sink.Hits.Count, Is.EqualTo(2));
        Assert.That(sink.Hits[0].index, Is.EqualTo(0));
        Assert.That(sink.Hits[1].index, Is.EqualTo(1));
        Assert.That(sink.Hits[0].id, Is.EqualTo(9u));
        Assert.That(sink.Hits[1].id, Is.EqualTo(9u));
    }

    [UnityTest]
    public IEnumerator Spawn_AlignsFireFanToGroundDirection_AndRemovesEffects()
    {
        var directions = new[]
        {
            Vector3.forward,
            Vector3.right,
            Vector3.back,
            Vector3.left,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.back + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized,
            (Vector3.forward + Vector3.left).normalized,
        };
        var prefab = new GameObject("AttackFire test");
        var particles = prefab.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = 0.1f;
        main.startSpeed = 1f;
        var effects = new List<GameObject>();
        for (int i = 0; i < directions.Length; i++)
        {
            prefab.name = "AttackFire test " + i;
            GameObject effect = FireCenserAttackEffect.Spawn(prefab, Origin, directions[i], 4f, 100f);
            Assert.That(effect, Is.Not.Null);
            ParticleSystem effectParticles = effect.GetComponent<ParticleSystem>();
            Assert.That(effectParticles.isPlaying, Is.True);
            var emitted = new ParticleSystem.Particle[640];
            int emittedCount = effectParticles.GetParticles(emitted);
            Assert.That(emittedCount, Is.GreaterThan(0));
            float minAngle = float.PositiveInfinity;
            float maxAngle = float.NegativeInfinity;
            float minTravelled = float.PositiveInfinity;
            float maxTravelled = float.NegativeInfinity;
            var radialBands = new HashSet<int>();
            for (int j = 0; j < emittedCount; j++)
            {
                Vector3 velocity = emitted[j].velocity;
                Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
                float yaw = Vector3.SignedAngle(directions[i], flatVelocity, Vector3.up);
                minAngle = Mathf.Min(minAngle, yaw);
                maxAngle = Mathf.Max(maxAngle, yaw);
                Assert.That(Mathf.Abs(yaw), Is.LessThanOrEqualTo(50.01f));
                Assert.That(emitted[j].startLifetime, Is.EqualTo(0.55f / 1.3f).Within(0.001f));
                float travelled = flatVelocity.magnitude * emitted[j].startLifetime;
                minTravelled = Mathf.Min(minTravelled, travelled);
                maxTravelled = Mathf.Max(maxTravelled, travelled);
                radialBands.Add(Mathf.RoundToInt(travelled * 100f));
                Assert.That(
                    (emitted[j].position - (Origin + Vector3.up * 0.25f)).magnitude,
                    Is.LessThan(0.001f));
            }
            Assert.That(minAngle, Is.EqualTo(-50f).Within(0.01f));
            Assert.That(maxAngle, Is.EqualTo(50f).Within(0.01f));
            Assert.That(minTravelled, Is.EqualTo(0.8f).Within(0.01f));
            Assert.That(maxTravelled, Is.EqualTo(4f).Within(0.01f));
            Assert.That(radialBands.Count, Is.EqualTo(5));
            effects.Add(effect);
        }

        prefab.name = "AttackFire zero direction test";
        GameObject zeroDirectionEffect = FireCenserAttackEffect.Spawn(
            prefab, Origin, Vector3.zero, 4f, 100f);
        Assert.That(zeroDirectionEffect, Is.Not.Null);
        effects.Add(zeroDirectionEffect);

        // EnterPlayMode does not make the EditMode runner wait on WaitForSeconds.
        float cleanupAt = Time.time + 1f;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (Time.time < cleanupAt && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(Time.time, Is.GreaterThanOrEqualTo(cleanupAt), "Runtime time must elapse before checking cleanup.");
        foreach (GameObject effect in effects)
            Assert.That(effect == null, Is.True);
        Object.Destroy(prefab);
    }

    [UnityTest]
    public IEnumerator Spawn_UsesAttackRangeAndAngleWithoutChangingPrefabShape()
    {
        var directions = new[]
        {
            Vector3.forward,
            Vector3.right,
            Vector3.back,
            Vector3.left,
            (Vector3.forward + Vector3.right).normalized,
            (Vector3.back + Vector3.right).normalized,
            (Vector3.back + Vector3.left).normalized,
            (Vector3.forward + Vector3.left).normalized,
        };
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Weapons/FireCenser/Art/Effects/AttackFire.prefab");
        Assert.That(prefabAsset, Is.Not.Null);
        float prefabRadius = prefabAsset.GetComponent<ParticleSystem>().shape.radius;

        for (int i = 0; i < directions.Length; i++)
        {
            GameObject effect = FireCenserAttackEffect.Spawn(
                prefabAsset, Origin, directions[i], 4f, 100f);
            Assert.That(effect, Is.Not.Null);
            ParticleSystem particles = effect.GetComponent<ParticleSystem>();
            var emitted = new ParticleSystem.Particle[640];
            int emittedCount = particles.GetParticles(emitted);
            Assert.That(emittedCount, Is.GreaterThan(0));
            float maxTravelled = 0f;
            var radialBands = new HashSet<int>();
            for (int j = 0; j < emittedCount; j++)
            {
                ParticleSystem.Particle particle = emitted[j];
                Vector3 flatVelocity = new Vector3(particle.velocity.x, 0f, particle.velocity.z);
                float yaw = Mathf.Abs(Vector3.SignedAngle(directions[i], flatVelocity, Vector3.up));
                Assert.That(yaw, Is.LessThanOrEqualTo(50.01f));
                Assert.That(particle.startSize, Is.InRange(0.35f, 0.55f));
                float travelled = flatVelocity.magnitude * particle.startLifetime;
                maxTravelled = Mathf.Max(maxTravelled, travelled);
                radialBands.Add(Mathf.RoundToInt(travelled * 100f));
            }
            Assert.That(maxTravelled, Is.EqualTo(4f).Within(0.01f));
            Assert.That(radialBands.Count, Is.EqualTo(5));
            Object.Destroy(effect);
        }
        Assert.That(prefabAsset.GetComponent<ParticleSystem>().shape.radius,
            Is.EqualTo(prefabRadius).Within(0.0001f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator Present_StartsFireFanAheadOfOwnerAndKeepsItsEndpoint()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Weapons/FireCenser/Art/Effects/AttackFire.prefab");
        Assert.That(prefab, Is.Not.Null);
        typeof(FireCenserWeapon).GetField("attackEffectPrefab", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(weapon, prefab);

        Vector3 direction = Vector3.forward;
        Vector3 end = Origin + direction * 4f;
        weapon.Present(new ShotEvent(new AttackInfo(11, weapon.Id), 0, Origin, end, 0f));

        GameObject effect = GameObject.Find("AttackFire(Clone)");
        Assert.That(effect, Is.Not.Null);
        Assert.That(
            (effect.transform.position - (Origin + direction * 0.5f)).magnitude,
            Is.LessThan(0.001f));

        ParticleSystem particles = effect.GetComponent<ParticleSystem>();
        var emitted = new ParticleSystem.Particle[640];
        int emittedCount = particles.GetParticles(emitted);
        Assert.That(emittedCount, Is.GreaterThan(0));
        float maxTravelled = 0f;
        var radialBands = new HashSet<int>();
        for (int i = 0; i < emittedCount; i++)
        {
            float travelled = new Vector3(emitted[i].velocity.x, 0f, emitted[i].velocity.z).magnitude
                * emitted[i].startLifetime;
            maxTravelled = Mathf.Max(maxTravelled, travelled);
            radialBands.Add(Mathf.RoundToInt(travelled * 100f));
            Assert.That(
                (emitted[i].position - (Origin + direction * 0.5f + Vector3.up * 0.25f)).magnitude,
                Is.LessThan(0.001f));
        }
        Assert.That(maxTravelled, Is.EqualTo(3.5f).Within(0.01f));
        Assert.That(radialBands.Count, Is.EqualTo(5));

        Object.Destroy(effect);
        yield return null;
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
