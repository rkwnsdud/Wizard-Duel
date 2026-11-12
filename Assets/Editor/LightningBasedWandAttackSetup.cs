using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copies the actual gameplay Lightning cast's motion onto the existing wand grip.
// The source clip and Lightning controller are read only.
public static class LightningBasedWandAttackSetup
{
    const string Source = "Assets/Weapons/Common/BaseCast.anim";
    const string Idle = "Assets/Weapons/Basic Wand/Animations/WizardCombatIdle_BasicWandGrip.anim";
    const string Folder = "Assets/Weapons/Basic Wand/Animations/";
    const string Report = "Docs/BasicWand/Attack/lightning-motion-validation.txt";
    const float Release = .20f;
    const float Duration = .65f;
    static readonly float[] Samples = Enumerable.Range(0, 66).Select(i => i / 100f).ToArray();

    static Transform Find(GameObject actor, string name) =>
        actor.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

    static GameObject[] Actors() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .Where(g => g.GetComponentsInChildren<BasicWandIdlePose>(true).Length > 0)
        .OrderBy(g => g.name == "Player" ? 0 : 1).ToArray();

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the wand clip in Edit Mode.");
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/PlayerTest.unity")
            throw new InvalidOperationException("Open PlayerTest before rebuilding the wand clips.");
        var actors = Actors();
        if (actors.Length != 2 || actors[0].name != "Player" || actors[1].name != "Dummy Player")
            throw new InvalidOperationException("Expected Player and Dummy Player in PlayerTest.");
        string sourceHash = Hash(Source);
        var report = new StringBuilder("Lightning source: " + Source + "\n");
        report.AppendLine("Source SHA256 before: " + sourceHash);
        BuildFor(actors[0], "", report);
        BuildFor(actors[1], "_DummyGrip", report);
        AssetDatabase.SaveAssets();
        if (Hash(Source) != sourceHash)
            throw new Exception("Original Lightning clip changed during the build.");
        report.AppendLine("PASS: source SHA256 unchanged; only FireMagic .200s and EndAttack .650s events copied.");
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, report.ToString());
        Debug.Log("LIGHTNING_BASED_WAND_ATTACK_BUILD_OK");
    }

    static void BuildFor(GameObject sourceActor, string suffix, StringBuilder report)
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Source);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle);
        if (!source || !idle || Mathf.Abs(source.length - Duration) > .001f)
            throw new Exception("Missing or retimed Lightning source / wand idle.");
        var actor = UnityEngine.Object.Instantiate(sourceActor);
        actor.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var wizard = Find(actor, "Wizard");
            wizard.localRotation = Quaternion.identity;
            var armature = Find(actor, "Armature");
            var sourcePaths = new HashSet<string>(AnimationUtility.GetCurveBindings(source).Select(b => b.path));
            var bones = armature.GetComponentsInChildren<Transform>(true)
                .Where(b => b != armature && sourcePaths.Contains(AnimationUtility.CalculateTransformPath(b, wizard)))
                .ToArray();
            if (bones.Length < 20) throw new Exception("Lightning source has too few rigged bones.");
            var arm = Find(actor, "RightArm");
            var hand = Find(actor, "RightHand");
            var forearm = Find(actor, "RightForeArm");
            var emptyForearm = Find(actor, "LeftForeArm");
            var emptyHand = Find(actor, "LeftHand");
            int emptyForearmIndex = Array.IndexOf(bones, emptyForearm);
            int emptyHandIndex = Array.IndexOf(bones, emptyHand);
            if (emptyForearmIndex < 0 || emptyHandIndex < 0)
                throw new Exception("Lightning source is missing the empty left arm.");
            var wand = Find(actor, "BasicWand");
            var leftFoot = Find(actor, "LeftFoot");
            var rightFoot = Find(actor, "RightFoot");
            idle.SampleAnimation(wizard.gameObject, 0);
            var idleRot = bones.Select(b => b.localRotation).ToArray();
            var idlePos = bones.Select(b => b.localPosition).ToArray();
            var grip = hand.GetComponentsInChildren<Transform>(true)
                .Where(b => b.name.StartsWith("Right") && b != hand)
                .ToArray();
            var fingerIdle = grip.Select(b => b.localRotation).ToArray();
            int armIndex = Array.IndexOf(bones, arm);
            int forearmIndex = Array.IndexOf(bones, forearm);
            int handIndex = Array.IndexOf(bones, hand);
            Vector3 socketPosition = wand.localPosition;
            Quaternion socketRotation = wand.localRotation;
            Vector3 rootPosition = wizard.localPosition;
            Quaternion rootRotation = wizard.localRotation;
            idle.SampleAnimation(wizard.gameObject, 0);
            source.SampleAnimation(wizard.gameObject, 0);
            var sourceRot = bones.Select(b => b.localRotation).ToArray();
            var sourcePos = bones.Select(b => b.localPosition).ToArray();
            var sourceLeftFoot = leftFoot.position;
            var sourceRightFoot = rightFoot.position;
            string output = Folder + "BasicWandAttack" + suffix + ".anim";
            var clip = new AnimationClip { name = "BasicWandAttack" + suffix, frameRate = 100 };
            foreach (var binding in AnimationUtility.GetCurveBindings(idle))
            {
                var curve = AnimationUtility.GetEditorCurve(idle, binding);
                AnimationUtility.SetEditorCurve(clip, binding,
                    AnimationCurve.Constant(0, Duration, curve.Evaluate(0)));
            }
            // Solve one release pose, then blend toward it through anticipation.
            // Holding this target briefly avoids an elbow/wrist flip between source keys.
            source.SampleAnimation(wizard.gameObject, .1f);
            Quaternion sourceElbowDelta = forearm.localRotation * Quaternion.Inverse(idleRot[forearmIndex]);
            Vector3 elbowHinge = new Vector3(sourceElbowDelta.x, 0, sourceElbowDelta.z).normalized;
            bool dummyGrip = suffix.Length > 0;
            Vector3 presentationTarget = Quaternion.AngleAxis(dummyGrip ? -36f : -32f, actor.transform.right) * actor.transform.forward;
            idle.SampleAnimation(wizard.gameObject, 0);
            source.SampleAnimation(wizard.gameObject, Release);
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localRotation = idleRot[i] * Quaternion.Inverse(sourceRot[i]) * bones[i].localRotation;
                bones[i].localPosition = idlePos[i] + bones[i].localPosition - sourcePos[i];
            }
            hand.localRotation = idleRot[handIndex];
            AimArm(actor.transform, arm, forearm, hand, wand, idleRot[forearmIndex], elbowHinge, dummyGrip);
            var releaseArm = arm.localRotation;
            var releaseForearm = forearm.localRotation;
            var releaseHand = hand.localRotation;
            var rotations = bones.Select(_ => new List<Quaternion>()).ToArray();
            var positions = bones.Select(_ => new List<Vector3>()).ToArray();
            float releaseError = 0, maxWrist = 0, maxWristTime = 0;
            float maxFootDeviation = 0, maxEmptyForearm = 0;
            for (int sample = 0; sample < Samples.Length; sample++)
            {
                float t = Samples[sample];
                idle.SampleAnimation(wizard.gameObject, 0);
                source.SampleAnimation(wizard.gameObject, t);
                // Preserve the source's foot trajectory for comparison before retargeting.
                Vector3 leftSourceDelta = leftFoot.position - sourceLeftFoot;
                Vector3 rightSourceDelta = rightFoot.position - sourceRightFoot;
                float recovery = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.57f, Duration, t));
                for (int i = 0; i < bones.Length; i++)
                {
                    Quaternion sourceDelta = Quaternion.Inverse(sourceRot[i]) * bones[i].localRotation;
                    bones[i].localRotation = Quaternion.Slerp(idleRot[i] * sourceDelta, idleRot[i], recovery);
                    bones[i].localPosition = Vector3.Lerp(idlePos[i] + bones[i].localPosition - sourcePos[i],
                        idlePos[i], recovery);
                }
                // The Lightning cast folds its free forearm about 79 degrees.
                // That looks like a broken wrist when no staff occupies this hand.
                emptyForearm.localRotation = Quaternion.Slerp(idleRot[emptyForearmIndex],
                    emptyForearm.localRotation, .4f);
                emptyHand.localRotation = idleRot[emptyHandIndex];
                maxEmptyForearm = Mathf.Max(maxEmptyForearm,
                    Quaternion.Angle(idleRot[emptyForearmIndex], emptyForearm.localRotation));
                // Place the elbow outside/down first; blend only a small wrist
                // swing along with the arm. Idle and its attachment stay intact.
                float aimWeight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / Release)) *
                    (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.26f, .55f, t)));
                // Use a smooth Idle-to-aim path for these two joints. Mixing in
                // the staff's arm motion produced counter-rotation during recovery.
                arm.localRotation = Quaternion.Slerp(idleRot[armIndex], releaseArm, aimWeight);
                forearm.localRotation = Quaternion.Slerp(idleRot[forearmIndex], releaseForearm, aimWeight);
                hand.localRotation = Quaternion.Slerp(idleRot[handIndex], releaseHand, aimWeight);
                if (Mathf.Abs(t - Release) < .0001f)
                    releaseError = Vector3.Angle(wand.forward, presentationTarget);
                float wrist = Vector3.Angle(hand.up, hand.position - forearm.position);
                if (wrist > maxWrist) { maxWrist = wrist; maxWristTime = t; }
                // Retargeting may move the pelvis by the source delta, but does not
                // invent a new step or alter the Lightning leg rotations.
                if (t <= .57f)
                    maxFootDeviation = Mathf.Max(maxFootDeviation,
                        Vector3.Distance(leftFoot.position - sourceLeftFoot, leftSourceDelta),
                        Vector3.Distance(rightFoot.position - sourceRightFoot, rightSourceDelta));
                for (int i = 0; i < bones.Length; i++)
                {
                    Quaternion q = bones[i].localRotation;
                    if (rotations[i].Count > 0 && Quaternion.Dot(rotations[i][rotations[i].Count - 1], q) < 0)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    rotations[i].Add(q);
                    positions[i].Add(bones[i].localPosition);
                }
                for (int i = 0; i < grip.Length; i++)
                    if (Quaternion.Angle(grip[i].localRotation, fingerIdle[i]) > .1f)
                        throw new Exception("Wand finger grip changed: " + grip[i].name);
                if (Vector3.Distance(wand.localPosition, socketPosition) > .0001f ||
                    Quaternion.Angle(wand.localRotation, socketRotation) > .01f)
                    throw new Exception("Wand socket shifted.");
                if (Vector3.Distance(wizard.localPosition, rootPosition) > .0001f ||
                    Quaternion.Angle(wizard.localRotation, rootRotation) > .01f)
                    throw new Exception("Character root moved.");
            }
            for (int i = 0; i < bones.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[i], wizard);
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                    .Where(b => b.path == path && b.propertyName.Contains("Rotation")))
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                for (int axis = 0; axis < 4; axis++)
                    WriteCurve(clip, path, "m_LocalRotation." + "xyzw"[axis],
                        rotations[i].Select(q => q[axis]).ToArray());
                // Original hips translation is retained; the character root stays put.
                if (bones[i].name == "Hips")
                    for (int axis = 0; axis < 3; axis++)
                        WriteCurve(clip, path, "m_LocalPosition." + "xyz"[axis],
                            positions[i].Select(p => p[axis]).ToArray());
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0; settings.stopTime = Duration; settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, new[] {
                new AnimationEvent { time = Release, functionName = "FireMagic" },
                new AnimationEvent { time = Duration, functionName = "EndAttack" }
            });
            if (releaseError > 1f || maxWrist > 25f || maxFootDeviation > .025f ||
                maxEmptyForearm > 35f)
            {
                UnityEngine.Object.DestroyImmediate(clip);
                throw new Exception("Wand aim/wrist/foot check failed: " +
                    releaseError + " / " + maxWrist + " at " + maxWristTime + " / " +
                    maxFootDeviation + " / empty forearm " + maxEmptyForearm);
            }
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                EditorUtility.SetDirty(existing);
            }
            else AssetDatabase.CreateAsset(clip, output);
            report.AppendLine("PASS " + sourceActor.name + ": " + bones.Length +
                " Lightning bones, " + Samples.Length + " samples; release presentation error " +
                releaseError.ToString("F3") + " deg; max wrist " + maxWrist.ToString("F2") +
                " deg; empty forearm " + maxEmptyForearm.ToString("F2") +
                " deg; foot trajectory deviation " + maxFootDeviation.ToString("F4") + " m.");
        }
        finally { UnityEngine.Object.DestroyImmediate(actor); }
    }

    static void AimArm(Transform root, Transform arm, Transform elbow, Transform hand, Transform wand,
        Quaternion idleForearm, Vector3 elbowHinge, bool dummyGrip)
    {
        // Use the original positive elbow hinge, not an averaged axial twist.
        Quaternion neutralHand = hand.localRotation;
        Vector3 shaftLocal = Quaternion.Inverse(hand.rotation) * wand.forward;
        Vector3 width = hand.Find("RightLittleProximal").localPosition - hand.Find("RightIndexProximal").localPosition;
        Vector3 handBack = Vector3.Cross(Vector3.up, width).normalized;
        // A raised wand presentation avoids forcing this fixed grip into an
        // anatomical reverse bend. Projectile direction and FirePoint are unchanged.
        Vector3 aim = Quaternion.AngleAxis(dummyGrip ? -36f : -32f, root.right) * root.forward;
        Quaternion handWorld = Quaternion.FromToRotation(shaftLocal, aim);
        Vector3 backPlane = Vector3.ProjectOnPlane(handWorld * handBack, aim).normalized;
        Vector3 backTarget = Vector3.ProjectOnPlane(root.right + root.up, aim).normalized;
        handWorld = Quaternion.AngleAxis(Vector3.SignedAngle(backPlane, backTarget, aim), aim) * handWorld;
        Vector3 handDirection = handWorld * Vector3.up;
        Vector3 wristAxis = Quaternion.AngleAxis(dummyGrip ? 310f : 340f, handDirection) * Vector3.Cross(handDirection, root.forward).normalized;
        Quaternion wristSwing = Quaternion.AngleAxis(24f, wristAxis);
        Quaternion forearmWorld = wristSwing * handWorld * Quaternion.Inverse(neutralHand);
        Quaternion elbowLocal = Quaternion.AngleAxis(18f, elbowHinge) * idleForearm;
        arm.rotation = forearmWorld * Quaternion.Inverse(elbowLocal);
        elbow.localRotation = elbowLocal;
        hand.rotation = handWorld;
    }

    static void WriteCurve(AnimationClip clip, string path, string property, float[] values)
    {
        var keys = Samples.Select((t, i) => new Keyframe(t, values[i])).ToArray();
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }
}
