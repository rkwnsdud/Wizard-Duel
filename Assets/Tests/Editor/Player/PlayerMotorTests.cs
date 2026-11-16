#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class PlayerMotorTests
{
    static void Awake(Component component) => component.GetType().GetMethod("Awake",
        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);

    [TestCase(false)]
    [TestCase(true)]
    public void HumanAndBotMovementUseEquipmentAndSlowMultipliersAndExistingGravity(bool bot)
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
            motor.Move(Vector3.right, 0.1f, combinedGravity: bot);
            Assert.That(root.transform.position.x - before.x, Is.EqualTo(0.275f).Within(0.003f));
            Assert.That(root.transform.position.y - before.y, Is.EqualTo(-0.2f).Within(0.003f));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void RemotePlaybackUsesTheSamePoseAndWalkParametersAsLocalMovement()
    {
        var root = new GameObject("Remote motor test");
        try
        {
            var character = new GameObject("Wizard");
            character.transform.SetParent(root.transform);
            var animator = character.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Game/Player/Art/Animations/WizardLocomotion.controller");
            var stats = root.AddComponent<PlayerCombatStats>();
            var motor = root.AddComponent<PlayerMotor>();
            Awake(motor);
            stats.SetMoveSpeedModifier(root, 0.5f);
            var playback = new MovePlayback(0.12f, 0.06f, 0.03f, 0f, 0f);
            playback.Reset(Vector3.zero, 0f);
            playback.Push(1.0, 2.0, Vector3.right * 2f, Vector3.right * 2.5f, 90f);
            Assert.That(playback.Advance(2.0, 0.1f, out Vector3 position, out float yaw), Is.True);
            motor.ApplyPose(position, yaw);
            motor.Animate(playback.Velocity.normalized, 0.1f);
            Assert.That(root.transform.position, Is.EqualTo(Vector3.right * 2f));
            Assert.That(Vector3.Angle(root.transform.forward, Vector3.right), Is.LessThan(0.01f));
            Assert.That(animator.GetFloat("MoveSpeed"), Is.EqualTo(2.5f));
            Assert.That(animator.GetFloat("MoveX"), Is.EqualTo(0f).Within(0.001f));
            Assert.That(animator.GetFloat("MoveZ"), Is.EqualTo(1f).Within(0.001f));
            motor.StopAnimation();
            Assert.That(animator.GetFloat("MoveSpeed"), Is.Zero);
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
    public void CombatPrefabsHaveOneMotorWithoutChangingNetworkBehaviourOrder()
    {
        Assert.That(typeof(Unity.Netcode.NetworkBehaviour).IsAssignableFrom(typeof(PlayerMotor)), Is.False);
        foreach (string name in new[] { "DuelWizard-1", "DuelWizard-2" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Player/Prefabs/" + name + ".prefab");
            Assert.That(prefab.GetComponents<PlayerMotor>().Length, Is.EqualTo(1), name);
            CollectionAssert.AreEqual(new[] { typeof(DuelPlayer), typeof(PlayerStatusEffects), typeof(DuelMoveSync) },
                System.Array.ConvertAll(prefab.GetComponents<Unity.Netcode.NetworkBehaviour>(), x => x.GetType()));
        }
    }
}
#endif
