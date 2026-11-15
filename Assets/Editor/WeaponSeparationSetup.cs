using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 전투 프리팹의 무기 소유권·애니메이션 이벤트·표시 참조를 EditMode에서 검증한다.
public static class WeaponSeparationSetup
{
    public static void Validate()
    {
        foreach (string name in new[] { "DuelWizard-1", "DuelWizard-2" })
        {
            var root = PrefabUtility.LoadPrefabContents($"Assets/Player/{name}.prefab");
            try
            {
                var attack = root.GetComponent<PlayerAttack>();
                Require(attack.CurrentWeapon != null, name + " currentWeapon");
                Require(attack.CurrentWeapon.transform.IsChildOf(root.transform), name + " weapon ownership");
                Require(attack.CurrentWeapon.transform.parent.name == "WeaponSocket", name + " socket");
                Require(attack.CurrentWeapon.AttackAnimation != null, name + " weapon attack clip");
                // Inactive spare weapons may wait in the socket for weapon switching; only the equipped one is active.
                var weapons = root.GetComponentsInChildren<WeaponBase>(true);
                Require(attack.CurrentWeapon.gameObject.activeSelf &&
                        weapons.Count(w => w.gameObject.activeSelf) == 1, name + " duplicate weapon");
                Require(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0, name + " character mesh");
                var wizard = root.transform.Find("Wizard");
                Require(wizard && wizard.GetComponent<Animator>(), name + " Animator");
                foreach (var pose in root.GetComponentsInChildren<BasicWandIdlePose>(true))
                {
                    var serialized = new SerializedObject(pose);
                    Require(serialized.FindProperty("targetAnimator").objectReferenceValue != null, name + " wand Animator");
                    Require(serialized.FindProperty("wandRenderer").objectReferenceValue != null, name + " wand Renderer");
                }
                foreach (var weapon in weapons)
                {
                    string label = weapon == attack.CurrentWeapon ? name : name + "/" + weapon.name;
                    Require(weapon.transform.parent.name == "WeaponSocket", label + " socket");
                    Require(weapon.AttackAnimation != null, label + " weapon attack clip");
                    var events = AnimationUtility.GetAnimationEvents(weapon.AttackAnimation);
                    Require(events.Count(e => e.functionName == "FireMagic") == 1, label + " FireMagic event");
                    Require(events.Count(e => e.functionName == "EndAttack") == 1, label + " EndAttack event");
                }
                Debug.Log("[WeaponSeparation] PASS " + name);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception("Weapon separation validation failed: " + label);
    }
}
