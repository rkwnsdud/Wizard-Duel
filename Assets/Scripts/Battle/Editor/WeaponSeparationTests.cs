#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class WeaponSeparationTests
{
    sealed class Authority : IAttackAuthority
    {
        public int Fires;
        public void StartAttack(float chargeRatio) { }
        public void FireMagic(float chargeRatio) { Fires++; }
    }

    [Test]
    public void PrefabVariantsRetainTheirEquippedWeaponAndEvents()
    {
        WeaponSeparationSetup.Validate();
        WeaponSeparationSetup.ValidatePlayerTest();
    }

    [UnityTest, Category(TestSuites.PlayModeCategory)]
    public IEnumerator EquippedWeaponControlsCastAndFiresOnlyOnce()
    {
        yield return new EnterPlayMode();
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Player/DuelWizard-1.prefab"));
        var attack = root.GetComponent<PlayerAttack>();
        var animator = root.transform.Find("Wizard").GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var authority = new Authority();
        attack.Authority = authority;
        yield return null;
        var originalWeapon = attack.CurrentWeapon;
        var originalController = animator.runtimeAnimatorController;
        var replacement = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapons/Lightning Weapon/DuelLightningStaff.prefab"), originalWeapon.transform.parent)
            .GetComponent<WeaponBase>();
        var clip = Object.Instantiate(originalWeapon.AttackAnimation);
        clip.name = "Weapon-specific cast test";
        typeof(WeaponBase).GetField("attackAnimation", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(replacement, clip);

        Assert.That(attack.TryEquipWeapon(replacement), Is.True);
        Assert.That(originalWeapon.gameObject.activeSelf, Is.False);
        attack.PlayAttack(.5f);
        Assert.That(animator.runtimeAnimatorController, Is.TypeOf<AnimatorOverrideController>());
        Assert.That(animator.runtimeAnimatorController.animationClips, Does.Contain(clip));
        Assert.That(attack.TryEquipWeapon(originalWeapon), Is.False);
        attack.FireMagic();
        attack.FireMagic();
        Assert.That(authority.Fires, Is.EqualTo(1));
        // Headless batch tests have no visible camera; evaluate the Animator
        // explicitly, as the existing combat animation test does.
        for (int frame = 0; frame < 120 && attack.IsAttacking; frame++)
        {
            animator.Update(1f / 60f);
            yield return null;
        }
        Assert.That(authority.Fires, Is.EqualTo(1), "Animation event must not duplicate the shot");
        Assert.That(attack.IsAttacking, Is.False, "EndAttack must be delivered by the weapon clip");
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(originalController),
            "Release the cast override so Basic Wand can acquire the idle controller");

        Assert.That(attack.TryEquipWeapon(originalWeapon), Is.True);
        Assert.That(animator.runtimeAnimatorController, Is.SameAs(originalController));
        attack.PlayAttack(0f);
        Assert.That(animator.runtimeAnimatorController.animationClips, Does.Contain(originalWeapon.AttackAnimation));
        attack.EndAttack();
        Assert.That(attack.TryEquipWeapon(null), Is.True);
        attack.PlayAttack(0f);
        Assert.That(attack.IsAttacking, Is.False);

        Object.Destroy(root);
        Object.Destroy(clip);
        yield return new ExitPlayMode();
    }
}
#endif
