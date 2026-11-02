using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit edit-mode authoring. Never opens/creates scenes or enters Play Mode.
public static class BasicWandAttackSetup
{
    const string Folder = "Assets/Weapons/Basic Wand/Animations/";
    const string Idle = Folder + "WizardCombatIdle_BasicWandGrip.anim";
    const string Cast = Folder + "BasicWandAttack.anim";
    const string Controller = Folder + "BasicWandLocomotion.controller";
    const string Baseline = "Assets/Player/Animations/WizardLocomotion.controller";
    const string OriginalCast = "Assets/Player/Animations/WizardAnimated/WizardStaffCast.anim";
    const string Output = "Docs/BasicWand/Attack/";
    const float Duration = .4f;
    static readonly string[] ArmBones = { "RightArm", "RightForeArm", "RightHand" };
    sealed class AttackProbe : IAttackAuthority
    {
        public int starts, fires;
        public void StartAttack(float chargeRatio) { starts++; }
        public void FireMagic(float chargeRatio) { fires++; }
    }
    static Transform Find(GameObject actor, string name) =>
        actor.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
    static GameObject[] Actors() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .Where(g => g.GetComponentsInChildren<BasicWandIdlePose>(true).Length > 0)
        .OrderBy(g => g.name == "Player" ? 0 : 1).ToArray();
    static bool Rotation(EditorCurveBinding b) =>
        b.propertyName.IndexOf("rotation", StringComparison.OrdinalIgnoreCase) >= 0 ||
        b.propertyName.IndexOf("euler", StringComparison.OrdinalIgnoreCase) >= 0;
    static void Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Invoke(target, args);
    static void EditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Basic Wand authoring requires edit mode.");
    }

    [MenuItem("Tools/Basic Wand/Build Attack %&#k")]
    public static void Build()
    {
        EditMode();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/PlayerTest.unity")
            throw new InvalidOperationException("Open the existing PlayerTest scene first.");
        var actors = Actors();
        if (actors.Length != 2 || actors[0].name != "Player" || actors[1].name != "Dummy Player")
            throw new InvalidOperationException("Expected Player and Dummy Player with existing wand attachments.");
        for (int i = 0; i < actors.Length; i++) BuildFor(actors[i], i == 0 ? "" : "_DummyGrip");
        Validate();
        Undo.FlushUndoRecordObjects();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("BASIC_WAND_ATTACK_BUILD_OK");
    }

    static void BuildFor(GameObject sourceActor, string suffix)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle);
        var original = AssetDatabase.LoadAssetAtPath<AnimationClip>(OriginalCast);
        var actor = UnityEngine.Object.Instantiate(sourceActor);
        actor.hideFlags = HideFlags.HideAndDontSave;
        var report = new StringBuilder();
        try
        {
            var wizard = Find(actor, "Wizard");
            // PlayerMovement.Start resets this transform before runtime playback.
            wizard.localRotation = Quaternion.identity;
            var bones = ArmBones.Select(n => Find(actor, n)).ToArray();
            var wand = Find(actor, "BasicWand");
            report.AppendLine("Authoring actor=" + actor.name + "; wand/hand=" +
                (Quaternion.Inverse(bones[2].rotation) * wand.rotation).ToString("F6"));
            // Inspect the staff cast for scale/timing; the wand gets independently
            // authored lift, thrust, snap and recovery rather than a sped-up staff cast.
            original.SampleAnimation(wizard.gameObject, .2f);
            report.AppendLine("Original staff cast: length=" + original.length.ToString("F3") +
                "; hand at release=" + actor.transform.InverseTransformPoint(bones[2].position).ToString("F4"));
            idle.SampleAnimation(wizard.gameObject, 0);
            var neutral = bones.Select(b => b.localRotation).ToArray();
            string castPath = Folder + "BasicWandAttack" + suffix + ".anim";
            var clip = new AnimationClip { name = "BasicWandAttack" + suffix, frameRate = 100 };
            // Hold the actual relaxed idle frame, including all fifteen finger joints.
            // No locomotion/root motion or staff's off-hand flourish is carried over.
            foreach (var b in AnimationUtility.GetCurveBindings(idle))
            {
                var curve = AnimationUtility.GetEditorCurve(idle, b);
                AnimationUtility.SetEditorCurve(clip, b, AnimationCurve.Constant(0, Duration, curve.Evaluate(0)));
            }
            var times = new[] { 0f, .04f, .06f, .08f, .11f, .16f, .24f, .32f, Duration };
            var poses = new Quaternion[times.Length][];
            poses[0] = neutral;
            poses[times.Length - 1] = neutral;
            // Hand targets in shoulder-relative character space, in arm-length units.
            var targets = new[] {
                new Vector3(.18f, -.05f, .42f), new Vector3(.13f, -.08f, .62f),
                new Vector3(.09f, -.08f, .72f), new Vector3(.08f, -.10f, .76f),
                new Vector3(.12f, -.15f, .70f), new Vector3(.20f, -.44f, .57f),
                new Vector3(.16f, -.79f, .24f) };
            var pitch = new[] { -12f, -9f, 0f, 7f, 12f, 35f, 70f };
            for (int k = 1; k < times.Length - 1; k++)
            {
                idle.SampleAnimation(wizard.gameObject, 0);
                Pose(actor.transform, bones, wand, targets[k - 1], pitch[k - 1]);
                poses[k] = bones.Select(b => b.localRotation).ToArray();
                report.AppendLine("Authored " + times[k].ToString("F3") + " aim=" + Vector3.Angle(wand.forward, actor.transform.forward).ToString("F4") +
                    "; wrist bend=" + Vector3.Angle(bones[2].up, bones[2].position - bones[1].position).ToString("F2") +
                    "; rotations=" + string.Join(" / ", poses[k].Select(q => q.ToString("F6"))));
            }
            for (int bone = 0; bone < bones.Length; bone++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[bone], wizard);
                foreach (var b in AnimationUtility.GetCurveBindings(clip).Where(b => b.path == path && Rotation(b)))
                    AnimationUtility.SetEditorCurve(clip, b, null);
                var samples = new List<Quaternion>();
                for (int i = 0; i <= 40; i++)
                {
                    float t = i / 100f;
                    int segment = Mathf.Min(times.Length - 2, Array.FindLastIndex(times, x => x <= t));
                    float u = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(times[segment], times[segment + 1], t));
                    var q = Quaternion.Slerp(poses[segment][bone], poses[segment + 1][bone], u);
                    if (samples.Count > 0 && Quaternion.Dot(samples[samples.Count - 1], q) < 0)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    samples.Add(q);
                }
                for (int axis = 0; axis < 4; axis++)
                {
                    var keys = samples.Select((q, i) => new Keyframe(i / 100f, q[axis])).ToArray();
                    var curve = new AnimationCurve(keys);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    var first = curve.keys[0]; first.inTangent = first.outTangent = 0; curve.MoveKey(0, first);
                    var last = curve.keys[curve.length - 1]; last.inTangent = last.outTangent = 0; curve.MoveKey(curve.length - 1, last);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        "m_LocalRotation." + "xyzw"[axis]), curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0; settings.stopTime = Duration; settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, new[] {
                new AnimationEvent { time = .08f, functionName = "FireMagic" },
                new AnimationEvent { time = Duration, functionName = "EndAttack" }
            });
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(castPath);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip); clip = existing;
                EditorUtility.SetDirty(clip);
            }
            else AssetDatabase.CreateAsset(clip, castPath);

            // A private copy isolates transition timing from Lightning/Light weapons.
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller))
                if (!AssetDatabase.CopyAsset(Baseline, Controller)) throw new Exception("Controller copy failed");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            var states = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
            foreach (var state in states)
            {
                foreach (var transition in state.transitions)
                {
                    if (transition.destinationState.name == "Cast") transition.duration = .015f;
                    if (state.name == "Cast")
                    {
                        transition.exitTime = 1f; transition.duration = 0;
                        transition.hasExitTime = true; transition.hasFixedDuration = true;
                    }
                    EditorUtility.SetDirty(transition);
                }
            }
            EditorUtility.SetDirty(controller);
            string overridePath = Folder + "BasicWandGrip" + suffix + ".overrideController";
            var overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
            if (!overrides)
            {
                overrides = new AnimatorOverrideController { name = "BasicWandGrip" + suffix };
                AssetDatabase.CreateAsset(overrides, overridePath);
            }
            overrides.runtimeAnimatorController = controller;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(pairs);
            overrides.ApplyOverrides(pairs.Select(p => new KeyValuePair<AnimationClip, AnimationClip>(p.Key,
                p.Key == original ? clip : p.Key.name == "WizardCombatIdle" ? idle : null)).ToList());
            EditorUtility.SetDirty(overrides);
            var sourceWand = Find(sourceActor, "BasicWand");
            var owner = sourceWand.GetComponent<BasicWandIdlePose>();
            var animator = Find(sourceActor, "Wizard").GetComponent<Animator>();
            var renderer = sourceWand.GetComponentInChildren<MeshRenderer>(true);
            Undo.RecordObject(owner, "Connect wand attack");
            owner.Configure(animator, AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Baseline), overrides, renderer);
            EditorUtility.SetDirty(owner);
            var gripSkin = sourceActor.GetComponentInChildren<BasicWandGripSkin>(true);
            Undo.RecordObject(gripSkin, "Connect wand attack grip");
            gripSkin.Configure(gripSkin.CharacterSkin, gripSkin.OriginalMesh, gripSkin.GripMesh, renderer, animator, overrides);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gripSkin);
            EditorUtility.SetDirty(gripSkin);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Output);
            report.AppendLine("Authored: 0 idle, .04 lift, .06 aim/preload, .08 thrust/release, .11 snap follow-through, .16 settle, .24/.32 recovery, .40 idle.");
            report.AppendLine("Only RightArm/RightForeArm/RightHand rotate. Fingers/body hold the actual wand idle frame; no root movement.");
            File.WriteAllText(Output + "build" + suffix + ".txt", report.ToString());
        }
        finally { UnityEngine.Object.DestroyImmediate(actor); }
    }

    static void Pose(Transform root, Transform[] bones, Transform wand, Vector3 target, float pitch)
    {
        var arm = bones[0]; var elbow = bones[1]; var hand = bones[2];
        float upper = Vector3.Distance(arm.position, elbow.position);
        float lower = Vector3.Distance(elbow.position, hand.position);
        Vector3 offset = (root.right * target.x + root.up * target.y + root.forward * target.z) * (upper + lower);
        float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(upper - lower) + .001f, (upper + lower) * .985f);
        Vector3 direction = offset.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(-root.up + root.right * .3f, direction).normalized;
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        Vector3 elbowTarget = arm.position + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        Vector3 handTarget = arm.position + direction * distance;
        // Swing from the relaxed frame, then distribute aim into forearm roll
        // so the wrist does not have to bend sideways to compensate for the grip.
        arm.rotation = Quaternion.FromToRotation(elbow.position - arm.position, elbowTarget - arm.position) * arm.rotation;
        elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, handTarget - elbow.position) * elbow.rotation;
        Vector3 aim = Quaternion.AngleAxis(pitch, root.right) * root.forward;
        Vector3 forearmAxis = (hand.position - elbow.position).normalized;
        Vector3 shaftPlane = Vector3.ProjectOnPlane(wand.forward, forearmAxis);
        Vector3 aimPlane = Vector3.ProjectOnPlane(aim, forearmAxis);
        if (shaftPlane.sqrMagnitude > .0001f && aimPlane.sqrMagnitude > .0001f)
            elbow.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(shaftPlane, aimPlane, forearmAxis), forearmAxis) * elbow.rotation;
        hand.rotation = Quaternion.FromToRotation(wand.forward, aim) * hand.rotation;
    }

    [MenuItem("Tools/Basic Wand/Validate Attack")]
    public static void Validate()
    {
        EditMode();
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Cast);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle);
        var original = AssetDatabase.LoadAssetAtPath<AnimationClip>(OriginalCast);
        var overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(Folder + "BasicWandGrip.overrideController");
        if (Mathf.Abs(clip.length - Duration) > .00001f) throw new Exception("Wrong attack length");
        var events = AnimationUtility.GetAnimationEvents(clip);
        if (events.Length != 2 || events[0].functionName != "FireMagic" || Mathf.Abs(events[0].time - .08f) > .00001f ||
            events[1].functionName != "EndAttack" || Mathf.Abs(events[1].time - Duration) > .00001f)
            throw new Exception("Wrong events");
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); overrides.GetOverrides(pairs);
        if (pairs.Count(p => p.Value && p.Value != p.Key) != 2 || overrides[original] != clip ||
            overrides[AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Player/Animations/WizardAnimated/WizardCombatIdle.anim")] != idle)
            throw new Exception("Only wand idle/cast may be overridden");
        var report = new StringBuilder("PASS: duration .400s; FireMagic .080s; EndAttack .400s; non-looping.\n");
        foreach (var source in Actors())
        {
            string suffix = source.name == "Player" ? "" : "_DummyGrip";
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "BasicWandAttack" + suffix + ".anim");
            overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(Folder + "BasicWandGrip" + suffix + ".overrideController");
            var variantEvents = AnimationUtility.GetAnimationEvents(clip);
            if (Mathf.Abs(clip.length - Duration) > .00001f || variantEvents.Length != 2 ||
                variantEvents[0].functionName != "FireMagic" || Mathf.Abs(variantEvents[0].time - .08f) > .00001f ||
                variantEvents[1].functionName != "EndAttack" || Mathf.Abs(variantEvents[1].time - Duration) > .00001f ||
                AnimationUtility.GetAnimationClipSettings(clip).loopTime || overrides[original] != clip)
                throw new Exception("Invalid variant timing/connection: " + source.name);
            var actor = UnityEngine.Object.Instantiate(source); actor.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var wizard = Find(actor, "Wizard"); var hand = Find(actor, "RightHand");
                wizard.localRotation = Quaternion.identity;
                var wand = Find(actor, "BasicWand"); var animator = wizard.GetComponent<Animator>();
                var fingers = hand.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Right") && t != hand).ToArray();
                idle.SampleAnimation(wizard.gameObject, 0);
                var fingerRotations = fingers.Select(t => t.localRotation).ToArray();
                var boneRotations = ArmBones.Select(n => Find(actor, n).localRotation).ToArray();
                Vector3 wandPosition = wand.localPosition; Quaternion wandRotation = wand.localRotation;
                for (int i = 0; i <= 80; i++)
                {
                    float time = Duration * i / 80;
                    clip.SampleAnimation(wizard.gameObject, time);
                    for (int f = 0; f < fingers.Length; f++)
                        if (Quaternion.Angle(fingerRotations[f], fingers[f].localRotation) > .05f)
                            throw new Exception("Finger grip changed at " + time);
                    if (wand.localPosition != wandPosition || Quaternion.Angle(wand.localRotation, wandRotation) > .01f)
                        throw new Exception("Wand socket changed");
                    if (i == 0 || i == 80)
                        for (int b = 0; b < ArmBones.Length; b++)
                            if (Quaternion.Angle(boneRotations[b], Find(actor, ArmBones[b]).localRotation) > .05f)
                                throw new Exception("Idle endpoint mismatch");
                }
                clip.SampleAnimation(wizard.gameObject, .08f);
                float aimError = Vector3.Angle(wand.forward, actor.transform.forward);
                float wristBend = Vector3.Angle(hand.up, hand.position - Find(actor, "RightForeArm").position);
                if (wristBend > 40f) throw new Exception("Excessive release wrist bend: " + wristBend);
                if (aimError > .1f) throw new Exception(source.name + " Wand release aim error=" + aimError +
                    "; rotations=" + string.Join(" / ", ArmBones.Select(n => Find(actor, n).localRotation.ToString("F6"))));
                var owner = wand.GetComponent<BasicWandIdlePose>();
                var renderer = wand.GetComponentInChildren<MeshRenderer>(true);
                var baseline = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Baseline);
                wand.gameObject.SetActive(true); renderer.gameObject.SetActive(true); renderer.enabled = true;
                animator.runtimeAnimatorController = baseline;
                // Inactive dummy remains inactive; validate its data above, ownership on active actors.
                if (owner.isActiveAndEnabled)
                {
                    if (!owner.IsActiveFor(animator) || animator.runtimeAnimatorController != overrides)
                        throw new Exception("Visible wand failed to select controller");
                    renderer.enabled = false; Call(owner, "RefreshPose");
                    if (animator.runtimeAnimatorController != baseline) throw new Exception("Hidden wand failed to restore baseline");
                    renderer.enabled = true;
                    var attack = actor.GetComponent<PlayerAttack>();
                    Call(attack, "Awake");
                    var probe = new AttackProbe(); attack.Authority = probe;
                    Call(attack, "AttackStarted", default(UnityEngine.InputSystem.InputAction.CallbackContext));
                    if (!attack.IsAttacking || probe.starts != 1) throw new Exception("Wand input did not immediately lock/start");
                    attack.FireMagic(); attack.FireMagic();
                    if (probe.fires != 0) throw new Exception("Animation-only wand fired the old weapon");
                    attack.EndAttack();
                    if (attack.IsAttacking) throw new Exception("EndAttack did not unlock movement");
                    renderer.enabled = false; Call(owner, "RefreshPose");
                    attack.PlayAttack(0); attack.FireMagic(); attack.FireMagic(); attack.EndAttack();
                    if (probe.fires != 1) throw new Exception("Original weapon event routing/deduplication changed");
                    attack.Authority = null;
                    report.AppendLine("PASS " + source.name + ": immediate attack/input lock; FireMagic suppresses old weapon only for wand; EndAttack unlocks; hidden wand restores original single-fire event routing (authority spy, no projectile).");
                }
                report.AppendLine("PASS " + source.name + ": 81 samples preserve fingers/socket; idle endpoints match; release aim error=" + aimError.ToString("F4") + " deg; wrist bend=" + wristBend.ToString("F2") + " deg.");
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }
        report.AppendLine("PASS: only Idle/Cast overrides; private controller, .015s entry, no post-cast blend delay. Existing IsAttacking movement lock retained.");
        report.AppendLine("No Play Mode, no new scene. Only existing wand controller references saved in PlayerTest; attachment transforms unchanged. Final motion review is manual in Unity.");
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "validation.txt", report.ToString());
        Debug.Log("BASIC_WAND_ATTACK_VALIDATE_OK");
    }
}
