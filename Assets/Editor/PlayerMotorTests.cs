#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PlayerMotorTests
{
    static void Awake(Component component) => component.GetType().GetMethod("Awake",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

    [Test]
    public void MovementUsesEquipmentAndSlowMultipliersAndExistingGravity()
    {
        var root = new GameObject("Motor test");
        root.transform.position = new Vector3(20000f, 100f, 20000f);
        try
        {
            root.AddComponent<CharacterController>();
            var stats = root.AddComponent<PlayerCombatStats>();
            var motor = root.AddComponent<PlayerMotor>();
            Awake(motor);
            stats.SetMoveSpeedModifier(root, 1.1f);
            stats.SetMoveSpeedModifier(motor, 0.5f);
            Vector3 before = root.transform.position;
            motor.Move(Vector3.right, 0.1f);
            Assert.That(root.transform.position.x - before.x, Is.EqualTo(0.275f).Within(0.003f));
            Assert.That(root.transform.position.y - before.y, Is.EqualTo(-0.2f).Within(0.003f));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void ExternalAimSurvivesMouseInputWithoutDisablingTheComponent()
    {
        var root = new GameObject("Aim test");
        root.SetActive(false);
        try
        {
            var aim = root.AddComponent<PlayerAim>();
            var motor = root.GetComponent<PlayerMotor>();
            Awake(motor);
            Awake(aim);
            aim.SetLookTarget(root.transform.position + Vector3.right * 10f);
            var update = typeof(PlayerAim).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            update.Invoke(aim, null);
            update.Invoke(aim, null);
            Assert.That(Vector3.Angle(root.transform.forward, Vector3.right), Is.LessThan(0.01f));
            Assert.That(aim.enabled, Is.True);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void PlayerVariantsInheritTheMotorWithoutChangingNetworkBehaviourOrder()
    {
        foreach (string name in new[] { "Player", "Player-1", "Player-2", "DuelWizard-1", "DuelWizard-2" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/" + name + ".prefab");
            Assert.That(prefab.GetComponent<PlayerMotor>(), Is.Not.Null, name);
        }
        Assert.That(typeof(Unity.Netcode.NetworkBehaviour).IsAssignableFrom(typeof(PlayerMotor)), Is.False);
        foreach (string name in new[] { "DuelWizard-1", "DuelWizard-2" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/" + name + ".prefab");
            CollectionAssert.AreEqual(new[] { typeof(DuelPlayer), typeof(PlayerStatusEffects), typeof(DuelMoveSync) },
                System.Array.ConvertAll(prefab.GetComponents<Unity.Netcode.NetworkBehaviour>(), x => x.GetType()));
        }
    }
}
#endif
