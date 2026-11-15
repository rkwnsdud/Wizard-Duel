#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class WeaponFiringTests
{
    [Test]
    public void DeathStopsWeaponPosesBeforeTheDeathAnimation()
    {
        var root = new GameObject("Pose death test");
        try
        {
            var health = root.AddComponent<PlayerHealth>();
            typeof(PlayerHealth).GetMethod("Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(health, null);
            var book = new GameObject("Book");
            book.transform.SetParent(root.transform);
            var bookPose = book.AddComponent<BookHoldPose>();
            var orb = new GameObject("Orb");
            orb.transform.SetParent(root.transform);
            var orbPose = orb.AddComponent<LightOrbWeapon>();

            new LocalShotSink().Hit(new AttackInfo(1, 1), 0, health, health.MaxHealth);

            Assert.That(health.IsDead, Is.True);
            Assert.That(bookPose.enabled, Is.False);
            Assert.That(orbPose.enabled, Is.False);
            Assert.That(book.activeSelf && orb.activeSelf, Is.True);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(0f, 5f)]
    [TestCase(1f, 15f)]
    public void LocalSinkAppliesDamageThroughTheSharedWeaponFire(float charge, float damage)
    {
        var root = new GameObject("Firing test owner");
        var target = new GameObject("Firing test target");
        root.SetActive(false);
        var origin = new Vector3(10000f, 2f, 10000f);
        root.transform.position = origin;
        target.transform.position = origin + Vector3.right * 1.25f;
        try
        {
            var initialize = typeof(PlayerHealth).GetMethod("Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            initialize.Invoke(root.AddComponent<PlayerHealth>(), null);
            var health = target.AddComponent<PlayerHealth>();
            initialize.Invoke(health, null);
            target.AddComponent<BoxCollider>();
            var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DuelLightningWeapon>(
                "Assets/Weapons/Lightning Weapon/DuelLightningStaff.prefab"), root.transform);
            var serialized = new SerializedObject(weapon);
            serialized.FindProperty("firePoint").objectReferenceValue = root.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();

            ((IWeapon)weapon).Fire(new AttackInfo(1, weapon.Id), Vector3.right, charge, new LocalShotSink());

            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - damage));
            Assert.That(root.GetComponent<PlayerHealth>().CurrentHealth, Is.EqualTo(100f));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(target);
        }
    }
}
#endif
