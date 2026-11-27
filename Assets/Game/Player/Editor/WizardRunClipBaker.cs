using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Kevin Iglesias "Human Melee Animations FREE"의 Humanoid 달리기 클립을 마법사(Generic 뼈대)용 클립으로 굽는다.
// 마법사의 다른 클립(대기·시전·사망·무기 자세)이 모두 Generic이라, 모델을 Humanoid로 바꾸지 않고 걷기 블렌드 트리만 바꾼다.
// 앞·뒤는 달리기(Run01), 옆과 대각선은 옆걸음 달리기(StrafeRun01)를 쓴다. 옆걸음 달리기는 골반이 정면을 유지해
// 마우스를 바라본 채 어느 방향으로 가도 골반이 홱 돌지 않는다.
// 원본은 운동선수처럼 팔을 크게 흔들고 몸을 숙여 잰걸음으로 뛰어서 마법사에게는 가벼워 보인다. 팔·몸통·무릎 움직임과
// 위아래 흔들림을 줄이고 박자를 늦춰 차분하게 굽는다.
public static class WizardRunClipBaker
{
    // 구운 클립을 1배속으로 틀었을 때 맞는 이동 빠르기(m/s). Animator의 Walk 상태는 1/이 값 × LocomotionSpeed로 재생한다.
    public const float NaturalSpeed = 4f;
    // 발이 땅을 미는 빠르기에 맞춘 박자보다 이만큼 느리게 굽는다. 발이 조금 미끄러지는 대신 잰걸음이 사라진다
    // (이동 5 m/s에서 초당 약 3걸음, 원본 박자는 약 4걸음).
    const float Cadence = 0.75f;
    // 팔: 한 주기 평균 자세를 대기 자세에서 얼마나 벗어나게 둘지(1이면 원본)와 앞뒤로 흔드는 폭의 비율.
    const float ArmPose = 0.3f, ArmSwing = 0.3f;
    // 몸통·목·머리를 비틀고 흔드는 폭의 비율.
    const float TorsoSwing = 0.5f;
    // 한 주기 평균으로 기운 상체를 대기 자세 쪽으로 세우는 비율(0이면 원본, 1이면 대기 자세만큼 곧게). 머리는 원래 보던 방향을 유지한다.
    const float Upright = 0.6f;
    // 골반이 오르내리는 폭의 비율. 가장 낮을 때(발을 디딘 때)를 기준으로 줄여 디딘 발이 뜨지 않는다.
    const float Bob = 0.5f;
    // 뒤로 차올릴 때 무릎을 굽히는 정도의 비율.
    const float KneeKick = 0.5f;

    const string BodyPrefab = "Assets/Game/Player/Prefabs/WizardBody.prefab";
    const string SourceDir = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Movement/";
    const string OutputDir = "Assets/Game/Player/Art/Animations/WizardRun/";
    // Humanoid로 옮기지 않는 뼈(손가락, 머리 끝)의 자세와 구울 뼈 목록을 이 클립에서 가져온다.
    const string RestPoseClip = "Assets/Game/Player/Art/Animations/WizardAnimated/WizardCombatIdle.anim";
    // 원본 클립과 같은 빠르기로 굽는다. 더 촘촘히 구워도 원본에 없는 움직임은 생기지 않고 파일만 커진다.
    const float FrameRate = 30f;
    // 마법사와 원본 사람의 다리 비율이 달라 그대로 옮기면 발이 2~5cm 땅에 묻힌다. 골반을 이만큼 올린다(m).
    const float GroundOffset = 0.025f;
    // 모든 클립의 왼발이 디딘 가운데가 이 정규화 시각에 오도록 맞춘다. 박자가 같아야 방향을 섞을 때 다리가 꼬이지 않는다.
    const float LeftStancePhase = 0.22f;

    static readonly (string output, string source)[] Clips =
    {
        ("WizardRunForward", "Run/HumanM@Run01_Forward.fbx"),
        ("WizardRunBackward", "Run/HumanM@Run01_Backward.fbx"),
        ("WizardRunLeft", "Strafe/StrafeRun/HumanM@StrafeRun01_Left.fbx"),
        ("WizardRunRight", "Strafe/StrafeRun/HumanM@StrafeRun01_Right.fbx"),
        ("WizardRunForwardLeft", "Strafe/StrafeRun/HumanM@StrafeRun01_ForwardLeft.fbx"),
        ("WizardRunForwardRight", "Strafe/StrafeRun/HumanM@StrafeRun01_ForwardRight.fbx"),
        ("WizardRunBackwardLeft", "Strafe/StrafeRun/HumanM@StrafeRun01_BackwardLeft.fbx"),
        ("WizardRunBackwardRight", "Strafe/StrafeRun/HumanM@StrafeRun01_BackwardRight.fbx"),
    };

    // 마법사 뼈 이름 → Humanoid 뼈. 등뼈는 아래에서부터 Spine02, Spine01, Spine 순서다.
    static readonly (string bone, string human)[] HumanMap =
    {
        ("Hips", "Hips"), ("Spine02", "Spine"), ("Spine01", "Chest"), ("Spine", "UpperChest"), ("neck", "Neck"), ("Head", "Head"),
        ("LeftUpLeg", "LeftUpperLeg"), ("LeftLeg", "LeftLowerLeg"), ("LeftFoot", "LeftFoot"), ("LeftToeBase", "LeftToes"),
        ("RightUpLeg", "RightUpperLeg"), ("RightLeg", "RightLowerLeg"), ("RightFoot", "RightFoot"), ("RightToeBase", "RightToes"),
        ("LeftShoulder", "LeftShoulder"), ("LeftArm", "LeftUpperArm"), ("LeftForeArm", "LeftLowerArm"), ("LeftHand", "LeftHand"),
        ("RightShoulder", "RightShoulder"), ("RightArm", "RightUpperArm"), ("RightForeArm", "RightLowerArm"), ("RightHand", "RightHand"),
    };

    [MenuItem("Tools/Wizard Duel/Bake Wizard Run Clips")]
    static void BakeFromMenu() => Debug.Log(Bake());

    /// <summary>달리기 클립 8개를 굽고 클립마다 원래 빠르기·박자 보정값을 적은 요약을 돌려준다.</summary>
    public static string Bake()
    {
        var report = new StringBuilder();
        AnimationClip restClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RestPoseClip);
        if (restClip == null) return "Rest pose clip is missing: " + RestPoseClip;
        if (!AssetDatabase.IsValidFolder(OutputDir.TrimEnd('/')))
            AssetDatabase.CreateFolder("Assets/Game/Player/Art/Animations", "WizardRun");

        GameObject body = PrefabUtility.LoadPrefabContents(BodyPrefab);
        Avatar avatar = null;
        try
        {
            Transform root = body.transform;
            avatar = BuildAvatar(root);
            if (!avatar.isValid || !avatar.isHuman) return "Could not build a humanoid avatar for " + BodyPrefab;
            var handler = new HumanPoseHandler(avatar, root);
            Transform spineBase = Find(root, "Spine02");
            Transform neck = Find(root, "neck");
            Transform head = Find(root, "Head");
            restClip.SampleAnimation(body, 0f);
            var idle = new HumanPose();
            handler.GetHumanPose(ref idle);
            Vector3 idleTorso = root.InverseTransformDirection(neck.position - spineBase.position).normalized;
            var paths = TransformPaths(restClip);
            var bones = new Transform[paths.Count];
            for (int i = 0; i < paths.Count; i++) bones[i] = root.Find(paths[i]);
            Transform hips = Find(root, "Hips");
            Transform leftFoot = Find(root, "LeftFoot");
            Transform rightFoot = Find(root, "RightFoot");

            foreach (var (output, source) in Clips)
            {
                AnimationClip clip = LoadHumanoidClip(SourceDir + source);
                if (clip == null) { report.AppendLine("Missing source clip: " + source); continue; }
                var curves = MuscleCurves(clip);
                float[] mean = MeanMuscles(curves, clip.length);
                float hipsLow = float.MaxValue;
                for (int i = 0; i < 60; i++)
                {
                    restClip.SampleAnimation(body, 0f);
                    HumanPose raw = ReadPose(curves, clip.length * i / 60);
                    handler.SetHumanPose(ref raw);
                    hipsLow = Mathf.Min(hipsLow, root.InverseTransformPoint(hips.position).y);
                }
                Quaternion upright = Quaternion.identity;
                void Pose(float sourceTime)
                {
                    restClip.SampleAnimation(body, 0f);
                    HumanPose pose = ReadPose(curves, sourceTime);
                    handler.SetHumanPose(ref pose);
                    // 팔·몸통 근육을 바꾸면 HumanPose의 몸 중심이 옮겨 가 골반과 다리까지 돈다. 원본 골반 자리를 기억해 두었다가 되돌린다.
                    Vector3 hipsLocal = root.InverseTransformPoint(hips.position);
                    Quaternion hipsRotation = hips.rotation;
                    Calm(pose.muscles, idle.muscles, mean);
                    handler.SetHumanPose(ref pose);
                    hipsLocal.y = hipsLow + (hipsLocal.y - hipsLow) * Bob + GroundOffset;
                    hips.SetPositionAndRotation(root.TransformPoint(hipsLocal), hipsRotation);
                    Quaternion headRotation = head.rotation;
                    spineBase.rotation = root.rotation * upright * Quaternion.Inverse(root.rotation) * spineBase.rotation;
                    head.rotation = headRotation;
                }
                // 근육값을 대기 자세 쪽으로 당기면 골반 기울기를 받치던 등 굽힘까지 풀려 뒤로 뛸 때 더 젖혀진다.
                // 그래서 상체 전체의 평균 기울기를 재서 허리 뼈 하나를 돌려 세운다.
                Vector3 torso = Vector3.zero;
                for (int i = 0; i < 60; i++)
                {
                    Pose(clip.length * i / 60);
                    torso += root.InverseTransformDirection(neck.position - spineBase.position).normalized;
                }
                torso.Normalize();
                upright = Quaternion.FromToRotation(torso, Vector3.Slerp(torso, idleTorso, Upright));

                Measure(clip.length, Pose, root, leftFoot, rightFoot, out float speed, out float phase);
                float length = clip.length * speed / (NaturalSpeed * Cadence);
                float shift = Mathf.Repeat(phase - LeftStancePhase, 1f) * clip.length;
                int frames = Mathf.Max(2, Mathf.RoundToInt(length * FrameRate));
                var keys = new Keyframe[paths.Count, 7][];
                for (int b = 0; b < paths.Count; b++)
                    for (int c = 0; c < 7; c++) keys[b, c] = new Keyframe[frames + 1];
                var previous = new Quaternion[paths.Count];
                for (int f = 0; f <= frames; f++)
                {
                    float time = length * f / frames;
                    Pose(Mathf.Repeat(shift + clip.length * f / frames, clip.length));
                    for (int b = 0; b < paths.Count; b++)
                    {
                        if (bones[b] == null) continue;
                        Quaternion q = bones[b].localRotation;
                        if (f > 0 && Quaternion.Dot(q, previous[b]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        previous[b] = q;
                        Vector3 p = bones[b].localPosition;
                        keys[b, 0][f] = new Keyframe(time, q.x);
                        keys[b, 1][f] = new Keyframe(time, q.y);
                        keys[b, 2][f] = new Keyframe(time, q.z);
                        keys[b, 3][f] = new Keyframe(time, q.w);
                        keys[b, 4][f] = new Keyframe(time, p.x);
                        keys[b, 5][f] = new Keyframe(time, p.y);
                        keys[b, 6][f] = new Keyframe(time, p.z);
                    }
                }
                Save(OutputDir + output + ".anim", output, paths, bones, hips, keys);
                report.AppendLine($"{output}: source {source} speed {speed:F2} m/s -> length {length:F3}s, phase shift {shift / clip.length:F2}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(body);
            if (avatar != null) Object.DestroyImmediate(avatar);
        }
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    // 근육마다 한 주기 평균을 기준으로 흔들림(평균과의 차이)을 줄인다. 팔은 평균 자세도 대기 자세 쪽으로 당긴다.
    static void Calm(float[] muscles, float[] idle, float[] mean)
    {
        for (int i = 0; i < muscles.Length; i++)
        {
            string name = HumanTrait.MuscleName[i];
            float value = muscles[i];
            if (IsArm(name)) value = idle[i] + (mean[i] - idle[i]) * ArmPose + (value - mean[i]) * ArmSwing;
            else if (IsTorso(name)) value = mean[i] + (value - mean[i]) * TorsoSwing;
            // Lower Leg Stretch는 값이 작을수록 무릎을 더 굽힌다. 평균보다 굽힌 쪽만 줄여 디딘 다리는 그대로 둔다.
            else if (name.EndsWith("Lower Leg Stretch") && value < mean[i]) value = mean[i] + (value - mean[i]) * KneeKick;
            muscles[i] = value;
        }
    }

    static bool IsArm(string muscle) =>
        muscle.Contains("Shoulder") || muscle.Contains(" Arm ") || muscle.Contains("Forearm") || muscle.Contains(" Hand ");

    static bool IsTorso(string muscle) =>
        muscle.StartsWith("Spine") || muscle.StartsWith("Chest") || muscle.StartsWith("UpperChest") ||
        muscle.StartsWith("Neck") || muscle.StartsWith("Head");

    static float[] MeanMuscles(Dictionary<string, AnimationCurve> curves, float length)
    {
        const int samples = 60;
        var mean = new float[HumanTrait.MuscleCount];
        for (int s = 0; s < samples; s++)
        {
            HumanPose pose = ReadPose(curves, length * s / samples);
            for (int i = 0; i < mean.Length; i++) mean[i] += pose.muscles[i] / samples;
        }
        return mean;
    }

    // 원래 자세는 팔을 내린 자세라 Humanoid 기준(T자세)과 다르다. 팔은 옆으로, 다리는 아래로 펴서 아바타를 만든다.
    static Avatar BuildAvatar(Transform root)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        var saved = new (Vector3, Quaternion)[all.Length];
        for (int i = 0; i < all.Length; i++) saved[i] = (all[i].localPosition, all[i].localRotation);
        Straighten(root, "LeftArm", "LeftForeArm", Vector3.left);
        Straighten(root, "LeftForeArm", "LeftHand", Vector3.left);
        Straighten(root, "RightArm", "RightForeArm", Vector3.right);
        Straighten(root, "RightForeArm", "RightHand", Vector3.right);
        Straighten(root, "LeftUpLeg", "LeftLeg", Vector3.down);
        Straighten(root, "LeftLeg", "LeftFoot", Vector3.down);
        Straighten(root, "RightUpLeg", "RightLeg", Vector3.down);
        Straighten(root, "RightLeg", "RightFoot", Vector3.down);

        var skeleton = new SkeletonBone[all.Length];
        for (int i = 0; i < all.Length; i++)
            skeleton[i] = new SkeletonBone
            {
                name = all[i].name, position = all[i].localPosition, rotation = all[i].localRotation, scale = all[i].localScale
            };
        var human = new HumanBone[HumanMap.Length];
        for (int i = 0; i < HumanMap.Length; i++)
        {
            human[i] = new HumanBone { boneName = HumanMap[i].bone, humanName = HumanMap[i].human };
            human[i].limit.useDefaultValues = true;
        }
        var description = new HumanDescription
        {
            human = human, skeleton = skeleton,
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false
        };
        Avatar avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, description);
        for (int i = 0; i < all.Length; i++) all[i].SetLocalPositionAndRotation(saved[i].Item1, saved[i].Item2);
        return avatar;
    }

    static void Straighten(Transform root, string bone, string child, Vector3 direction)
    {
        Transform from = Find(root, bone), to = Find(root, child);
        Vector3 current = (to.position - from.position).normalized;
        from.rotation = Quaternion.FromToRotation(current, root.TransformDirection(direction)) * from.rotation;
    }

    // 발이 땅에 닿은 동안 미끄러지는 빠르기와, 두 발이 디딘 가운데 시각으로 잰 박자(왼발 기준, 0~1).
    static void Measure(float length, System.Action<float> pose, Transform root, Transform leftFoot, Transform rightFoot,
        out float speed, out float phase)
    {
        const int samples = 120;
        var left = new Vector3[samples + 1];
        var right = new Vector3[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            pose(length * i / samples);
            left[i] = root.InverseTransformPoint(leftFoot.position);
            right[i] = root.InverseTransformPoint(rightFoot.position);
        }
        Stance(left, length, out float leftSpeed, out Vector2 leftPhase);
        Stance(right, length, out float rightSpeed, out Vector2 rightPhase);
        speed = (leftSpeed + rightSpeed) * 0.5f;
        // 오른발은 반 주기 뒤에 디딘다. 반 바퀴 돌려 왼발 박자와 합친다.
        Vector2 sum = leftPhase - rightPhase;
        phase = Mathf.Repeat(Mathf.Atan2(sum.y, sum.x) / (2f * Mathf.PI), 1f);
    }

    static void Stance(Vector3[] foot, float length, out float speed, out Vector2 phase)
    {
        int samples = foot.Length - 1;
        float low = float.MaxValue, high = float.MinValue;
        foreach (Vector3 p in foot) { low = Mathf.Min(low, p.y); high = Mathf.Max(high, p.y); }
        float contact = low + Mathf.Max(0.015f, (high - low) * 0.12f);
        float total = 0f;
        int steps = 0;
        phase = Vector2.zero;
        for (int i = 0; i < samples; i++)
        {
            if (foot[i].y > contact) continue;
            float angle = 2f * Mathf.PI * i / samples;
            phase += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            if (foot[i + 1].y > contact) continue;
            Vector3 step = foot[i + 1] - foot[i];
            step.y = 0f;
            total += step.magnitude / (length / samples);
            steps++;
        }
        speed = steps > 0 ? total / steps : 0f;
    }

    static List<string> TransformPaths(AnimationClip clip)
    {
        var paths = new List<string>();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Transform) && !paths.Contains(binding.path)) paths.Add(binding.path);
        return paths;
    }

    static AnimationClip LoadHumanoidClip(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && clip.humanMotion && !clip.name.StartsWith("__preview")) return clip;
        return null;
    }

    static Dictionary<string, AnimationCurve> MuscleCurves(AnimationClip clip)
    {
        var curves = new Dictionary<string, AnimationCurve>();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            curves[binding.propertyName] = AnimationUtility.GetEditorCurve(clip, binding);
        return curves;
    }

    static HumanPose ReadPose(Dictionary<string, AnimationCurve> curves, float time)
    {
        float Value(string name) => curves.TryGetValue(name, out AnimationCurve curve) ? curve.Evaluate(time) : 0f;
        var rotation = new Quaternion(Value("RootQ.x"), Value("RootQ.y"), Value("RootQ.z"), Value("RootQ.w"));
        var pose = new HumanPose
        {
            bodyPosition = new Vector3(Value("RootT.x"), Value("RootT.y"), Value("RootT.z")),
            bodyRotation = rotation.w == 0f && rotation.x == 0f && rotation.y == 0f && rotation.z == 0f
                ? Quaternion.identity : Quaternion.Normalize(rotation),
            muscles = new float[HumanTrait.MuscleCount]
        };
        for (int i = 0; i < HumanTrait.MuscleCount; i++) pose.muscles[i] = Value(HumanTrait.MuscleName[i]);
        return pose;
    }

    // 회전은 모든 뼈, 위치는 골반만 쓴다. 이미 있으면 같은 에셋을 고쳐 GUID(컨트롤러 연결)를 유지한다.
    static void Save(string path, string name, List<string> paths, Transform[] bones, Transform hips, Keyframe[,][] keys)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool created = clip == null;
        if (created) clip = new AnimationClip();
        clip.ClearCurves();
        clip.name = name;
        clip.frameRate = FrameRate;
        string[] rotation = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        string[] position = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
        for (int b = 0; b < paths.Count; b++)
        {
            if (bones[b] == null) continue;
            for (int c = 0; c < 4; c++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[b], typeof(Transform), rotation[c]), Linear(keys[b, c]));
            if (bones[b] != hips) continue;
            for (int c = 0; c < 3; c++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[b], typeof(Transform), position[c]), Linear(keys[b, 4 + c]));
        }
        clip.EnsureQuaternionContinuity();
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (created) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
    }

    // 값이 변하지 않는 곡선(손가락, 머리 끝 등)은 처음과 끝 키만 남긴다.
    static AnimationCurve Linear(Keyframe[] keys)
    {
        bool constant = true;
        for (int i = 1; i < keys.Length && constant; i++) constant = Mathf.Abs(keys[i].value - keys[0].value) < 1e-6f;
        if (constant) keys = new[] { keys[0], new Keyframe(keys[keys.Length - 1].time, keys[0].value) };
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    static Transform Find(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = Find(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
