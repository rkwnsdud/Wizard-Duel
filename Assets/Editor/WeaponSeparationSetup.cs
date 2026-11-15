using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// EditMode 테스트 WeaponSeparationTests가 Validate()·ValidatePlayerTest()를 호출하므로 유지한다(Apply()는 명시적 1회성 마이그레이션 잔재로 자동 실행되지 않음).
public static class WeaponSeparationSetup
{
    const string PlayerPath = "Assets/Player/Player.prefab";
    const string StaffPath = "Assets/Weapons/Lightning Weapon/LightningStaff.prefab";
    const string CastPath = "Assets/Weapons/Common/BaseCast.anim";

    public static void Apply()
    {
        var oldPath = "Assets/Player/Animations/WizardAnimated/WizardStaffCast.anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(CastPath) == null)
        {
            string error = AssetDatabase.MoveAsset(oldPath, CastPath);
            if (!string.IsNullOrEmpty(error)) throw new Exception(error);
        }
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CastPath);
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var attack = player.GetComponent<PlayerAttack>();
            SetReference(attack, "baseCastClip", clip);
            var staff = attack.CurrentWeapon;
            SetReference(staff, "attackAnimation", clip);
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(staff.gameObject) != StaffPath)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(staff.gameObject, StaffPath,
                    InteractionMode.AutomatedAction);
            }
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }

        string duelPath = "Assets/Weapons/Lightning Weapon/DuelLightningStaff.prefab";
        var duel = PrefabUtility.LoadPrefabContents(duelPath);
        try
        {
            SetReference(duel.GetComponent<WeaponBase>(), "attackAnimation", clip);
            PrefabUtility.SaveAsPrefabAsset(duel, duelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(duel); }
        AssetDatabase.SaveAssets();
        Validate();
    }

    static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Validate()
    {
        foreach (string name in new[] { "Player", "Player-1", "Player-2", "DuelWizard-1", "DuelWizard-2" })
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

    public static void ValidatePlayerTest()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PlayerTest.unity", OpenSceneMode.Additive);
        try
        {
            int players = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var attack in root.GetComponentsInChildren<PlayerAttack>(true))
            {
                Require(attack.CurrentWeapon != null, attack.name + " scene weapon reference");
                Require(attack.CurrentWeapon.transform.IsChildOf(attack.transform), attack.name + " scene weapon owner");
                foreach (var weapon in attack.GetComponentsInChildren<WeaponBase>(true))
                    Require(weapon.AttackAnimation != null, attack.name + "/" + weapon.name + " scene attack clip");
                var wizard = attack.transform.Find("Wizard");
                Require(wizard && wizard.GetComponent<Animator>(), attack.name + " scene Animator");
                foreach (var pose in attack.GetComponentsInChildren<BasicWandIdlePose>(true))
                {
                    var serialized = new SerializedObject(pose);
                    Require(serialized.FindProperty("targetAnimator").objectReferenceValue != null, "wand Animator");
                    Require(serialized.FindProperty("wandRenderer").objectReferenceValue != null, "wand Renderer");
                }
                players++;
            }
            Require(players == 2, "PlayerTest player count");
            Debug.Log("[WeaponSeparation] PASS PlayerTest references");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    static void Require(bool condition, string label)
    {
        if (!condition) throw new Exception("Weapon separation validation failed: " + label);
    }
}
