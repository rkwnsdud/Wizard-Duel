#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class BasicWandWeaponTests
{
    sealed class Sink : IShotSink
    {
        public BasicWandWeapon Weapon;
        public int Hits;
        public PlayerHealth Target;
        public void Broadcast(ShotEvent shot) => Weapon.Present(shot);
        public void Hit(uint id, int index, PlayerHealth target, float damage)
        {
            Hits++;
            Target = target;
        }
    }

    [UnitySetUp]
    public IEnumerator SetUp() { yield return new EnterPlayMode(); }

    [UnityTearDown]
    public IEnumerator TearDown() { yield return new ExitPlayMode(); }

    static BasicWandWeapon CreateWand(out PlayerHealth owner)
    {
        var root = new GameObject("Wand test owner");
        root.transform.position = new Vector3(10000f, 2f, 10000f);
        root.transform.rotation = Quaternion.LookRotation(Vector3.right);
        owner = root.AddComponent<PlayerHealth>();
        return Object.Instantiate(AssetDatabase.LoadAssetAtPath<BasicWandWeapon>(
            "Assets/Weapons/Basic Wand/Prefabs/BasicWand.prefab"), root.transform, false);
    }

    [Test]
    public void Fire_LocalShotFollowsPlayerAimDespiteTiltedFirePoint()
    {
        var wand = CreateWand(out _);
        wand.transform.localRotation = Quaternion.Euler(70f, 25f, 40f);
        wand.Fire();
        var bolts = Object.FindObjectsByType<ArcaneBoltProjectile>(FindObjectsSortMode.None);
        Assert.That(bolts.Length, Is.EqualTo(1));
        Assert.That(Vector3.Dot(bolts[0].transform.forward, Vector3.right), Is.GreaterThan(0.999f));
    }

    [UnityTest]
    public IEnumerator Fire_OnlineCreatesOneBoltAndHitsDefaultLayerWithoutLocalDamage()
    {
        var wand = CreateWand(out _);
        Transform muzzle = (Transform)typeof(BasicWandWeapon).GetField("firePoint",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wand);
        var target = new GameObject("Default layer duel target");
        target.transform.position = muzzle.position + Vector3.right * 2f;
        target.AddComponent<BoxCollider>();
        var health = target.AddComponent<PlayerHealth>();
        Physics.SyncTransforms();
        var sink = new Sink { Weapon = wand };
        ((IWeapon)wand).Fire(1, Vector3.right, 0f, sink);
        Assert.That(Object.FindObjectsByType<ArcaneBoltProjectile>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        for (int frame = 0; frame < 120 && sink.Hits == 0; frame++) yield return null;
        Assert.That(sink.Hits, Is.EqualTo(1));
        Assert.That(sink.Target, Is.SameAs(health));
        Assert.That(health.CurrentHealth, Is.EqualTo(100f));
    }
}
#endif
