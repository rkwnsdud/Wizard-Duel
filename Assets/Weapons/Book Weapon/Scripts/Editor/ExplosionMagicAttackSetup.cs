using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 폭발 마법 공격 클립을 만든다. 공통 Cast(BaseCast)의 몸·오른팔 동작을 그대로 쓰고,
// 팔을 뻗는 동안만 오른손을 젖혀 손바닥이 정면을 보게 하고 손가락을 편다.
// 왼팔은 BookHoldPose가 런타임에 책 든 자세로 덮어쓰므로 원본 그대로 둔다.
public static class ExplosionMagicAttackSetup
{
    const string Source = "Assets/Weapons/Common/BaseCast.anim";
    const string Player = "Assets/Player/DuelWizard-1.prefab";
    public const string Output = "Assets/Weapons/Book Weapon/Animations/ExplosionMagicAttack.anim";
    const float Release = .2f;
    const float Duration = .65f;
    const int FrameRate = 100;

    [MenuItem("Tools/Weapons/Build Explosion Magic Attack")]
    public static void BuildMenu() => Build(Output, -1, 15f);

    // palmSign: 손바닥 법선이 오른손 본의 +X(1) 또는 -X(-1)인지. fingerTilt: 손가락이 수직에서 앞으로 기우는 각도.
    public static string Build(string output, int palmSign, float fingerTilt)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the attack clip in Edit Mode.");
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Source);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player);
        if (source == null || prefab == null || Mathf.Abs(source.length - Duration) > .001f)
            throw new Exception("Missing or retimed BaseCast / player prefab.");

        var actor = UnityEngine.Object.Instantiate(prefab);
        actor.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform wizard = actor.transform.Find("Wizard");
            Transform[] all = actor.GetComponentsInChildren<Transform>(true);
            Transform forearm = all.Single(t => t.name == "RightForeArm");
            Transform hand = all.Single(t => t.name == "RightHand");
            Transform[] fingers = hand.GetComponentsInChildren<Transform>(true)
                .Where(t => t != hand && t.name.StartsWith("Right") &&
                            (t.name.Contains("Proximal") || t.name.Contains("Intermediate") || t.name.Contains("Distal")))
                .ToArray();
            Transform[] bones = new[] { hand }.Concat(fingers).ToArray();

            // 뻗은 순간의 손 목표: 손가락은 위(조금 앞으로), 손바닥은 정면.
            source.SampleAnimation(wizard.gameObject, Release);
            Vector3 forward = actor.transform.forward;
            Vector3 fingersUp = Quaternion.AngleAxis(fingerTilt, actor.transform.right) * Vector3.up;
            Vector3 palm = Vector3.ProjectOnPlane(forward, fingersUp).normalized;
            Vector3 xAxis = palm * palmSign;
            Vector3 zAxis = Vector3.Cross(xAxis, fingersUp);
            Quaternion handTargetWorld = Quaternion.LookRotation(zAxis, fingersUp);
            Quaternion handTargetLocal = Quaternion.Inverse(forearm.rotation) * handTargetWorld;

            int samples = Mathf.RoundToInt(Duration * FrameRate) + 1;
            var rotations = bones.Select(_ => new List<Quaternion>()).ToArray();
            for (int s = 0; s < samples; s++)
            {
                float t = s / (float)FrameRate;
                source.SampleAnimation(wizard.gameObject, t);
                float w = Weight(t);
                for (int i = 0; i < bones.Length; i++)
                {
                    Quaternion target = i == 0 ? handTargetLocal : Quaternion.identity;
                    Quaternion q = Quaternion.Slerp(bones[i].localRotation, target, w);
                    if (rotations[i].Count > 0 && Quaternion.Dot(rotations[i][rotations[i].Count - 1], q) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    rotations[i].Add(q);
                }
            }

            var clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(output), frameRate = FrameRate };
            var replaced = new HashSet<string>(bones.Select(b => AnimationUtility.CalculateTransformPath(b, wizard)));
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                if (replaced.Contains(binding.path) && binding.propertyName.StartsWith("m_LocalRotation")) continue;
                AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding));
            }
            for (int i = 0; i < bones.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[i], wizard);
                for (int axis = 0; axis < 4; axis++)
                {
                    var keys = new Keyframe[samples];
                    for (int s = 0; s < samples; s++)
                        keys[s] = new Keyframe(s / (float)FrameRate, rotations[i][s][axis]);
                    var curve = new AnimationCurve(keys);
                    for (int k = 0; k < keys.Length; k++) AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    for (int k = 0; k < keys.Length; k++) AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[axis]), curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = Duration;
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = Release, functionName = "FireMagic" },
                new AnimationEvent { time = Duration, functionName = "EndAttack" },
            });

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(output);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                EditorUtility.SetDirty(existing);
            }
            else AssetDatabase.CreateAsset(clip, output);
            AssetDatabase.SaveAssets();
            return $"Built {output}: {bones.Length} hand bones, release {Release}s.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actor);
        }
    }

    // 손을 펴는 비율. 팔을 뻗기 직전에 펴고, 발사 뒤 잠시 유지했다가 원래 손 모양으로 돌아간다.
    static float Weight(float t)
    {
        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.06f, .17f, t));
        float close = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.36f, .58f, t));
        return open * (1f - close);
    }
}
