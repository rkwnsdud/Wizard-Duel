using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Runs explicitly on disposable objects; never saves or replaces the user's scene.
public static class WeaponSetupReview
{
    const string Output = "Logs/WeaponSetupReview.txt";

    [MenuItem("Tools/Weapons/Review Setup %#&F9")]
    public static void Review()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the review in Edit Mode.");
        var report = new StringBuilder();
        try
        {
            WeaponSeparationSetup.Validate();
            report.AppendLine("PASS: five player prefab variants retain their weapon, socket and cast events.");
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PlayerTest.unity");
            try
            {
                int weapons = 0;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var weapon in root.GetComponentsInChildren<WeaponBase>(true))
                {
                    CheckWeapon(weapon);
                    weapons++;
                }
                Require(weapons == 6, "PlayerTest must have two Lightning, two Orb and two Basic Wand weapons.");
                report.AppendLine("PASS: six scene weapons have clips, owned FirePoints and projectile references.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            var testScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Player.prefab"));
                SceneManager.MoveGameObjectToScene(player, testScene);
                player.transform.SetPositionAndRotation(new Vector3(10000, 0, 10000), Quaternion.identity);
                var weapon = player.GetComponentInChildren<LightningWeapon>(true);
                CheckWeapon(weapon);
                var preview = Reference<LightningPreview>(weapon, "preview");
                Require(preview, "Default LightningStaff charge preview is assigned.");
                var line = Reference<LineRenderer>(preview, "lineRenderer");
                Require(line && line.transform.IsChildOf(weapon.transform), "Preview has its own LineRenderer.");
                Require(Resources.Load<Shader>("LightningChargePreview"), "Charge preview shader exists.");
                Call(preview, "Awake");
                preview.ShowPreview(Vector3.zero, Vector3.forward * 5, Vector3.forward, .8f);
                Require(line.enabled && line.positionCount == 4 && line.loop, "Charge footprint is shown.");
                preview.HidePreview();
                Require(!line.enabled, "Charge footprint is hidden after cancel.");
                // Dispose the preview's temporary material in Edit Mode.
                var material = Field<Material>(preview, "ownedMaterial");
                SetField(preview, "ownedMaterial", null);
                if (material) Object.DestroyImmediate(material);
                report.AppendLine("PASS: default staff preview initializes, draws four corners and hides without missing references.");

                var attack = player.GetComponent<PlayerAttack>();
                Call(attack, "Awake");
                typeof(PlayerAttack).GetProperty("IsAttacking").GetSetMethod(true).Invoke(attack, new object[] { true });
                SetField(attack, "isCharging", true);
                // Replicates a disabled remote PlayerAttack that never enabled input.
                Require(Field<object>(attack, "inputActions") == null, "Probe has no input object.");
                Call(attack, "OnDisable");
                Require(!attack.IsAttacking && !Field<bool>(attack, "isCharging"), "Disabling always cancels attack and charge.");
                report.AppendLine("PASS: disabled attack cancels even when input was never enabled.");

                MeasurePose(player, weapon, report);
                var activeScene = SceneManager.GetActiveScene();
                var physicsScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(activeScene);
                try
                {
                    SceneManager.SetActiveScene(physicsScene);
                    CheckWallTrace(weapon, physicsScene);
                    CheckOrbReflection(physicsScene);
                }
                finally
                {
                    SceneManager.SetActiveScene(activeScene);
                    EditorSceneManager.CloseScene(physicsScene, true);
                }
                report.AppendLine("PASS: Lightning trace stops at a wall and ignores a floor-layer collider.");
                report.AppendLine("PASS: Orb ignores its owner and reflects twice; previous beam segments stay attached to the projectile.");
            }
            finally { EditorSceneManager.ClosePreviewScene(testScene); }
            report.AppendLine("Scope: Edit Mode asset/pose/physics checks; no user scene save or Play Mode.");
            Debug.Log("WEAPON_SETUP_REVIEW_PASS\n" + report);
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + error);
            Debug.LogException(error);
            throw;
        }
        finally
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Output, report.ToString());
        }
    }

    static void CheckWeapon(WeaponBase weapon)
    {
        Require(weapon.transform.parent && weapon.transform.parent.name == "WeaponSocket", weapon.name + " socket");
        Require(weapon.AttackAnimation, weapon.name + " attack clip");
        var events = AnimationUtility.GetAnimationEvents(weapon.AttackAnimation);
        Require(events.Count(e => e.functionName == "FireMagic") == 1 &&
            events.Count(e => e.functionName == "EndAttack") == 1, weapon.name + " cast events");
        var point = Reference<Transform>(weapon, "firePoint");
        Require(point && point.IsChildOf(weapon.transform), weapon.name + " owned FirePoint");
        if (weapon is LightningWeapon)
            Require(Reference<LightningBolt>(weapon, "boltPrefab"), weapon.name + " bolt prefab");
        if (weapon is BasicWandWeapon)
        {
            var projectile = Reference<ArcaneBoltProjectile>(weapon, "projectilePrefab");
            Require(projectile, weapon.name + " projectile");
            int mask = new SerializedObject(projectile).FindProperty("mask").intValue;
            int required = LayerMask.GetMask("Player", "Wall", "Obstacle");
            Require((mask & required) == required, "Arcane Bolt detects players, walls and obstacles");
        }
        if (weapon is LightOrbWeapon)
        {
            var projectile = Reference<LightOrbProjectile>(weapon, "lightAttackPrefab");
            Require(projectile, weapon.name + " projectile");
            Require(Reference<LineRenderer>(projectile, "beamCore") && Reference<LineRenderer>(projectile, "beamGlow"), "Orb beam renderers");
            var serialized = new SerializedObject(projectile);
            int reflections = serialized.FindProperty("reflectionMask").intValue;
            int hits = serialized.FindProperty("hitMask").intValue;
            Require(reflections != 0 && (hits & reflections) == reflections, "Orb detects its reflection layers");
            Require(serialized.FindProperty("maxReflections").intValue > 0, "Orb reflection count");
        }
    }

    static void MeasurePose(GameObject player, WeaponBase weapon, StringBuilder report)
    {
        var wizard = player.transform.Find("Wizard");
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim");
        var firePoint = Reference<Transform>(weapon, "firePoint");
        var arm = wizard.GetComponentsInChildren<Transform>(true).Single(t => t.name == "RightArm");
        var elbow = wizard.GetComponentsInChildren<Transform>(true).Single(t => t.name == "RightForeArm");
        var hand = wizard.GetComponentsInChildren<Transform>(true).Single(t => t.name == "RightHand");
        idle.SampleAnimation(wizard.gameObject, 0);
        report.AppendLine($"POSE Idle: FirePoint height={firePoint.position.y - player.transform.position.y:F3}m; elbow angle={Vector3.Angle(arm.position - elbow.position, hand.position - elbow.position):F1}deg.");
        float fireTime = AnimationUtility.GetAnimationEvents(weapon.AttackAnimation).Single(e => e.functionName == "FireMagic").time;
        weapon.AttackAnimation.SampleAnimation(wizard.gameObject, fireTime);
        report.AppendLine($"POSE Cast at {fireTime:F3}s: FirePoint height={firePoint.position.y - player.transform.position.y:F3}m.");
    }

    static void CheckWallTrace(LightningWeapon weapon, Scene scene)
    {
        var bolt = Reference<LightningBolt>(weapon, "boltPrefab");
        var wall = new GameObject("Weapon review wall");
        SceneManager.MoveGameObjectToScene(wall, scene);
        wall.layer = LayerMask.NameToLayer("Wall");
        Require(wall.layer >= 0, "Wall layer");
        var box = wall.AddComponent<BoxCollider>();
        box.size = new Vector3(2, 2, 1);
        var start = new Vector3(10000, 1, 10000);
        wall.transform.position = start + Vector3.forward * 2;
        Physics.SyncTransforms();
        var end = bolt.GetEndPosition(start, Vector3.forward, 1);
        Require(Mathf.Abs(end.z - (start.z + 1.5f)) < .01f, "Wall blocks Lightning");
        int floor = LayerMask.NameToLayer("Floor");
        Require(floor >= 0, "Floor layer");
        wall.layer = floor;
        Physics.SyncTransforms();
        end = bolt.GetEndPosition(start, Vector3.forward, 1);
        Require(Mathf.Abs(Vector3.Distance(start, end) - bolt.GetRange(1)) < .01f, "Floor does not block Lightning");
    }

    static void CheckOrbReflection(Scene scene)
    {
        var origin = new Vector3(10020, 1, 10000);
        var ownerObject = new GameObject("Orb review owner");
        SceneManager.MoveGameObjectToScene(ownerObject, scene);
        ownerObject.layer = LayerMask.NameToLayer("Player");
        ownerObject.transform.position = origin + Vector3.forward * .5f;
        ownerObject.AddComponent<BoxCollider>().size = Vector3.one * .2f;
        var owner = ownerObject.AddComponent<PlayerHealth>();
        foreach (float offset in new[] { 2f, -2f })
        {
            var wall = new GameObject("Orb review wall");
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.layer = LayerMask.NameToLayer("Wall");
            wall.transform.position = origin + Vector3.forward * offset;
            wall.AddComponent<BoxCollider>().size = new Vector3(2, 2, 1);
        }
        var projectile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Effect/Light/LightOrbProjectile.prefab").GetComponent<LightOrbProjectile>(), origin, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(projectile.gameObject, scene);
        projectile.Initialize(Vector3.forward, owner, null);
        Physics.SyncTransforms();
        var advance = typeof(LightOrbProjectile).GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic);
        advance.Invoke(projectile, new object[] { .1f });
        Require(Field<int>(projectile, "reflectionCount") == 1, "Orb ignores owner and reaches first wall");
        Require(Vector3.Dot(Field<Vector3>(projectile, "direction"), Vector3.back) > .99f, "First reflection direction");
        advance.Invoke(projectile, new object[] { .1f });
        Require(Field<int>(projectile, "reflectionCount") == 2, "Orb reaches second wall");
        Require(Vector3.Dot(Field<Vector3>(projectile, "direction"), Vector3.forward) > .99f, "Second reflection direction");
        Require(projectile.GetComponentsInChildren<LineRenderer>().Length == 6, "Three independent core/glow segments");
    }

    static T Reference<T>(Object obj, string field) where T : Object =>
        new SerializedObject(obj).FindProperty(field).objectReferenceValue as T;
    static T Field<T>(object obj, string field) => (T)obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    static void SetField(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
    static void Call(object obj, string method) => obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Weapon setup: " + label);
    }
}
