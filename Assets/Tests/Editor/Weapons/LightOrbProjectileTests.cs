#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class LightOrbProjectileTests : PlayModeTestFixture
{
    static readonly Vector3 Origin = new Vector3(10000f, 2f, 10000f);
    static readonly MethodInfo Advance = typeof(LightOrbProjectile).GetMethod("Advance",
        BindingFlags.Instance | BindingFlags.NonPublic);

    sealed class Sink : IShotSink
    {
        public int Hits;
        public PlayerHealth Target;
        public float Damage;
        public uint AttackId;
        public void Broadcast(ShotEvent shot) { }
        public void Hit(AttackInfo attack, int index, PlayerHealth target, float damage)
        {
            Hits++;
            AttackId = attack.Id;
            Target = target;
            Damage = damage;
        }
    }

    static PlayerHealth Player(float x, int layer = 0)
    {
        var root = new GameObject("Light orb test player");
        root.layer = layer;
        root.transform.position = Origin + Vector3.right * x;
        root.AddComponent<CharacterController>();
        return root.AddComponent<PlayerHealth>();
    }

    static LightOrbProjectile Beam()
    {
        var beam = Object.Instantiate(AssetDatabase.LoadAssetAtPath<LightOrbProjectile>(
            "Assets/Game/Weapons/LightOrb/Prefabs/LightOrbProjectile.prefab"), Origin, Quaternion.identity);
        // Step the actual physics query explicitly, independent of Game view focus/frame rate.
        beam.enabled = false;
        return beam;
    }

    [TestCase(0)]
    [TestCase(3)]
    public void OnlineBeam_HitsPlayerLayerAndReportsDamageOnce(int layer)
    {
        var owner = Player(1f);
        var target = Player(2f, layer);
        var trigger = new GameObject("Non-blocking trigger");
        trigger.transform.position = Origin + Vector3.right * 0.7f;
        trigger.AddComponent<BoxCollider>().isTrigger = true;
        var sink = new Sink();
        var beam = Beam();
        beam.Initialize(Vector3.right, owner, sink, new AttackInfo(42, 3));
        Physics.SyncTransforms();
        Advance.Invoke(beam, new object[] { 0.1f });
        Assert.That(sink.Hits, Is.EqualTo(1));
        Assert.That(sink.Target, Is.SameAs(target));
        Assert.That(sink.AttackId, Is.EqualTo(42u));
        Assert.That(sink.Damage, Is.EqualTo(10f));
        Assert.That(target.CurrentHealth, Is.EqualTo(100f), "The server applies online damage.");
        Assert.That(owner.CurrentHealth, Is.EqualTo(100f));
    }

    [UnityTest]
    public IEnumerator VisualBeam_StopsOnDefaultPlayerWithoutApplyingDamage()
    {
        var target = Player(2f);
        var beam = Beam();
        beam.InitializeVisualOnly(Vector3.right, null);
        Physics.SyncTransforms();
        Advance.Invoke(beam, new object[] { 0.1f });
        yield return null;
        Assert.That(beam == null, Is.True, "Remote visual must stop at the same target.");
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void ReflectedBeam_HitsDefaultPlayerWithReflectionDamage()
    {
        var owner = Player(0f);
        var target = Player(-2f);
        var wall = new GameObject("Reflection wall");
        wall.layer = LayerMask.NameToLayer("Wall");
        wall.transform.position = Origin + Vector3.right * 2f;
        wall.AddComponent<BoxCollider>().size = new Vector3(1f, 3f, 3f);
        var sink = new Sink();
        var beam = Beam();
        beam.Initialize(Vector3.right, owner, sink, new AttackInfo(43, 3));
        Physics.SyncTransforms();
        Advance.Invoke(beam, new object[] { 0.1f });
        Assert.That(sink.Hits, Is.Zero, "The wall must block the outward shot.");
        Advance.Invoke(beam, new object[] { 0.2f });
        Assert.That(sink.Hits, Is.EqualTo(1));
        Assert.That(sink.Target, Is.SameAs(target));
        Assert.That(sink.Damage, Is.EqualTo(15f));
    }

    [Test]
    public void LocalSink_AppliesTheSameBeamHitToPlayerHealth()
    {
        var target = Player(2f, LayerMask.NameToLayer("Player"));
        var beam = Beam();
        beam.Initialize(Vector3.right, null, new LocalShotSink(), new AttackInfo(44, 3));
        Physics.SyncTransforms();
        Advance.Invoke(beam, new object[] { 0.1f });
        Assert.That(target.CurrentHealth, Is.EqualTo(90f));
    }
}
#endif
