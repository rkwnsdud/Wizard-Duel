#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class DuelLightningWeaponTests : PlayModeTestFixture
{
    static readonly Vector3 Origin = new Vector3(10000f, 1f, 10000f);
    DuelLightningWeapon weapon;
    PlayerHealth owner;

    static WeaponSpawnCatalog Catalog() => AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>(
        "Assets/Game/Battle/WeaponSpawnCatalog.asset");

    sealed class ShotSink : IShotSink
    {
        public readonly List<ShotEvent> Shots = new List<ShotEvent>();
        public readonly List<string> Order = new List<string>();
        public int Hits;
        public PlayerHealth Target;
        public float Damage;
        public uint AttackId;
        public int HitIndex;
        public void Broadcast(ShotEvent shot) { Shots.Add(shot); Order.Add("present"); }
        public void Hit(AttackInfo attack, int index, PlayerHealth target, float damage)
        {
            Hits++;
            AttackId = attack.Id;
            HitIndex = index;
            Target = target;
            Damage = damage;
            Order.Add("hit");
        }
    }

    sealed class AttackAuthority : IAttackAuthority
    {
        public int Starts, Fires;
        public float Charge;
        public void StartAttack(AttackInfo attack, float chargeRatio) { Starts++; }
        public void FireMagic(AttackInfo attack, float chargeRatio) { Fires++; Charge = chargeRatio; }
    }

    protected override IEnumerator SetUpInPlayMode()
    {
        var root = new GameObject("Duel lightning test owner");
        root.SetActive(false);
        root.transform.position = Origin;
        owner = root.AddComponent<PlayerHealth>();
        root.AddComponent<BoxCollider>();
        var staff = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Weapons/LightningStaff/Prefabs/DuelLightningStaff.prefab"), root.transform);
        weapon = staff.GetComponent<DuelLightningWeapon>();
        typeof(DuelLightningWeapon).GetField("firePoint", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(weapon, root.transform);
        root.SetActive(true);
        yield break;
    }

    static PlayerHealth Target(Vector3 offset)
    {
        var target = new GameObject("Duel lightning test target");
        target.layer = LayerMask.NameToLayer("Hitbox");
        target.transform.position = Origin + offset;
        target.AddComponent<BoxCollider>();
        return target.AddComponent<PlayerHealth>();
    }

    [Test]
    public void Prefabs_UseTheCatalogAndStartWithTheDefaultWeapon()
    {
        WeaponDefinition lightning = Catalog().Find(weapon.Id);
        Assert.That(lightning, Is.Not.Null);
        Assert.That(lightning.prefab.Definition, Is.SameAs(weapon.Definition));
        foreach (string name in new[] { "DuelWizard-1", "DuelWizard-2" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Player/Prefabs/{name}.prefab");
            Assert.That(prefab.GetComponent<PlayerAttack>().Weapon.Id, Is.EqualTo(AiDuelOptions.DefaultWeapon), name);
            Assert.That(prefab.GetComponent<PlayerAttack>().Weapon, Is.InstanceOf<IWeapon>(), name);
        }
    }

    [Test]
    public void Fire_ReportsOneHitWithoutLocalDamageOrVisuals()
    {
        var target = Target(Vector3.right * 4f);
        target.gameObject.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(1, weapon.Id), Vector3.right, 1f, sink);

        Assert.That(sink.Order, Is.EqualTo(new[] { "present", "present", "hit" }));
        Assert.That(sink.Shots[1].Kind, Is.EqualTo(1));
        Assert.That(Vector3.Distance(sink.Shots[1].Point, target.transform.position), Is.LessThan(1f));
        Assert.That(sink.Hits, Is.EqualTo(1));
        Assert.That(sink.Target, Is.SameAs(target));
        Assert.That(sink.AttackId, Is.EqualTo(1u));
        Assert.That(sink.HitIndex, Is.Zero);
        Assert.That(sink.Damage, Is.EqualTo(20f));
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
        Assert.That(owner.CurrentHealth, Is.EqualTo(100f));
        Assert.That(Object.FindObjectsByType<LightningEffect>(FindObjectsSortMode.None), Is.Empty);
    }

    [TestCase(0f, 5f)]
    [TestCase(0.5f, 12.5f)]
    [TestCase(1f, 20f)]
    public void Fire_UsesLocalChargeForDamageAndStopsAtTarget(float charge, float damage)
    {
        Target(Vector3.right * 1.25f);
        Physics.SyncTransforms();
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(9, weapon.Id), Vector3.right, charge, sink);
        Assert.That(sink.Shots.Count, Is.EqualTo(2));
        Assert.That(sink.Shots[0].AttackId, Is.EqualTo(9));
        Assert.That(sink.Shots[0].Start, Is.EqualTo(Origin));
        Assert.That(Vector3.Distance(sink.Shots[0].Point, Origin + Vector3.right * 0.75f), Is.LessThan(0.01f));
        Assert.That(sink.Shots[0].Charge, Is.EqualTo(charge));
        Assert.That(sink.Hits, Is.EqualTo(1));
        Assert.That(sink.Damage, Is.EqualTo(damage));
    }

    [TestCase(0f, 2f)]
    [TestCase(1f, 8f)]
    public void Fire_MissKeepsChargedRange(float charge, float range)
    {
        Physics.SyncTransforms();
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(10, weapon.Id), Vector3.right, charge, sink);
        Assert.That(sink.Shots[0].Point, Is.EqualTo(Origin + Vector3.right * range));
        Assert.That(sink.Hits, Is.Zero);
    }

    [Test]
    public void Fire_StopsAtNearestLivingTargetRegardlessOfCreationOrder()
    {
        Target(Vector3.right * 6f);
        var nearest = Target(Vector3.right * 3f);
        nearest.gameObject.AddComponent<BoxCollider>();
        var dead = Target(Vector3.right * 1.5f);
        new LocalShotSink().Hit(new AttackInfo(1, weapon.Id), 0, dead, dead.MaxHealth);
        Physics.SyncTransforms();
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(11, weapon.Id), Vector3.right, 1f, sink);
        Assert.That(sink.Target, Is.SameAs(nearest));
        Assert.That(sink.Hits, Is.EqualTo(1));
        Assert.That(Vector3.Distance(sink.Shots[0].Point, Origin + Vector3.right * 2.5f), Is.LessThan(0.01f));
    }

    [Test]
    public void Fire_OffCenterTargetWithinBeamAlsoStopsTheBolt()
    {
        var target = Target(Vector3.right * 4f + Vector3.forward * 0.7f);
        Physics.SyncTransforms();
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(12, weapon.Id), Vector3.right, 1f, sink);
        Assert.That(sink.Target, Is.SameAs(target));
        Assert.That(Vector3.Distance(sink.Shots[0].Point, Origin + Vector3.right * 3.5f), Is.LessThan(0.01f));
    }

    [Test]
    public void Fire_WallBlocksTheHitAndStillBroadcastsAMiss()
    {
        Target(Vector3.right * 4f);
        var wall = new GameObject("Duel lightning test wall");
        wall.layer = 9;
        wall.transform.position = Origin + Vector3.right * 2f;
        wall.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        var sink = new ShotSink();
        ((IWeapon)weapon).Fire(new AttackInfo(2, weapon.Id), Vector3.right, 1f, sink);
        Assert.That(sink.Shots.Count, Is.EqualTo(1));
        Assert.That(Vector3.Distance(sink.Shots[0].Point, Origin + Vector3.right * 1.5f), Is.LessThan(0.01f));
        Assert.That(sink.Hits, Is.Zero);
    }

    [UnityTest]
    public IEnumerator Present_UsesAttackerEndpointsWithoutDamageOrWallQueries()
    {
        var target = Target(Vector3.right * 4f);
        var wall = new GameObject("Client-only test wall");
        wall.layer = 9;
        wall.transform.position = Origin + Vector3.right * 2f;
        wall.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        Vector3 end = Origin + Vector3.right * 6f;
        ((IWeapon)weapon).Present(new ShotEvent(new AttackInfo(3, weapon.Id), 0, Origin, end, 1f));
        var effect = Object.FindObjectsByType<LightningEffect>(FindObjectsSortMode.None).Single();
        owner.transform.position += Vector3.forward;
        yield return null;

        var endPoint = (Transform)typeof(LightningEffect).GetField("endPoint",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effect);
        Assert.That(effect.transform.parent, Is.Null);
        Assert.That(effect.transform.position, Is.EqualTo(Origin));
        Assert.That(endPoint.position, Is.EqualTo(end));
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
        Assert.That(owner.CurrentHealth, Is.EqualTo(100f));
    }

    [UnityTest]
    public IEnumerator Attack_PlaysImmediatelyAndFiresTheLocalChargeOnce()
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Player/Prefabs/DuelWizard-1.prefab"),
            Origin + Vector3.back * 20f, Quaternion.identity);
        var attack = player.GetComponent<PlayerAttack>();
        var animator = player.transform.Find("Wizard").GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var authority = new AttackAuthority();
        attack.Authority = authority;
        yield return null;
        // DuelWizard starts with a wand; additional weapons come from the catalog.
        var lightning = Object.Instantiate(Catalog().Find(weapon.Id).prefab, attack.Weapon.transform.parent, false);
        Assert.That(attack.TryEquipWeapon(lightning), Is.True);
        Assert.That(attack.Weapon.Id, Is.EqualTo(weapon.Id));
        typeof(PlayerAttack).GetMethod("CommitAttack", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(attack, new object[] { 0.75f });
        Assert.That(attack.IsAttacking, Is.True);
        Assert.That(authority.Starts, Is.EqualTo(1));
        bool sawCast = false;
        // Sample explicitly so this check does not depend on Game view focus.
        for (int frame = 0; frame < 120 && attack.IsAttacking; frame++)
        {
            animator.Update(1f / 60f);
            if (authority.Fires > 0) attack.FireMagic(); // A repeated animation event must not fire again.
            sawCast |= animator.GetCurrentAnimatorStateInfo(0).IsName("Cast");
            yield return null;
        }
        Assert.That(sawCast, Is.True);
        Assert.That(authority.Fires, Is.EqualTo(1));
        Assert.That(authority.Charge, Is.EqualTo(0.75f));
        Assert.That(attack.IsAttacking, Is.False);
    }
}
#endif
