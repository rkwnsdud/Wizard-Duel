#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Dying.fbx 안의 mixamo.com 테이크(쓰러지는 모션)를 Player 프리팹의 PlayerHealth.deathClip에 연결한다.
// 한 번 실행하면 Player.prefab에 저장되고 DuelWizard-1/2 변형에도 그대로 내려간다.
public static class DeathMotionWire
{
    const string DyingPath = "Assets/Player/Animations/Dying.fbx";
    const string PlayerPrefabPath = "Assets/Player/Player.prefab";
    static readonly string[] Controllers =
    {
        "Assets/Player/Animations/WizardLocomotion.controller",
        "Assets/Weapons/Basic Wand/Animations/BasicWandLocomotion.controller",
    };

    // 경로를 고친 사망 모션 저장 위치. Dying.fbx의 mixamo.com 테이크는 본 경로가 Armature/... 라서
    // 캐릭터(WizardModel/Armature/...)와 안 맞으므로 WizardModel/ 을 앞에 붙인 복제본을 만든다.
    const string FixedClipPath = "Assets/Player/Animations/WizardDeath.anim";

    const string DeathParam = "Death";

    [MenuItem("Tools/Player/Connect Dying Motion")]
    public static void Connect()
    {
        AnimationClip death = FindDeathClip();
        if (death == null)
        {
            Debug.LogError("[DeathMotion] Dying.fbx에서 mixamo.com 클립을 찾지 못했다. 임포트 설정을 확인하세요.");
            return;
        }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[DeathMotion] Player.prefab을 찾지 못했다.");
            return;
        }
        PlayerHealth health = prefab.GetComponent<PlayerHealth>();
        if (health == null)
        {
            Debug.LogError("[DeathMotion] Player.prefab에 PlayerHealth가 없다.");
            return;
        }
        var serialized = new SerializedObject(health);
        serialized.FindProperty("deathClip").objectReferenceValue = death;
        serialized.ApplyModifiedProperties();
        PrefabUtility.SavePrefabAsset(prefab);
        Debug.Log($"[DeathMotion] 연결 완료: {PlayerPrefabPath} <- {AssetDatabase.GetAssetPath(death)} ({death.name} 길이 {death.length:0.00}s)");
        AnimationClip fixedClip = FixClipPaths(death);
        PatchControllers(fixedClip);
    }

    // Dying.fbx 안의 모든 테이크를 덤프한다. mixamo.com이 진짜 사망 모션인지 확인할 때 쓴다.
    // 커브 경로 앞부분도 찍는다. 기존 걷기 클립(WizardModel/Armature/...)과 다르면 재생돼도 안 움직인다.
    [MenuItem("Tools/Player/Dump Dying Clips")]
    public static void Dump()
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(DyingPath))
        {
            if (asset is AnimationClip clip)
            {
                string path = AssetDatabase.GetAssetPath(clip);
                Debug.Log($"[DeathMotion] clip name={clip.name} len={clip.length:0.00}s empty={clip.empty} events={clip.events.Length} path={path}", clip);
                if (clip.name == "mixamo.com")
                {
                    int shown = 0;
                    foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                    {
                        Debug.Log($"[DeathMotion] curve path={b.path} prop={b.propertyName}", clip);
                        if (++shown >= 5) break;
                    }
                }
            }
        }
    }

    static AnimationClip FindDeathClip()
    {
        // 미리보기(__preview__)는 건너뛴다. 예전 Contains 방식은 미리보기를 먼저 잡아서
        // 본 경로 없는 클립이 연결되는 원인이었다.
        AnimationClip fallback = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(DyingPath))
        {
            if (!(asset is AnimationClip clip)) continue;
            if (clip.name.StartsWith("__preview__")) continue;
            if (clip.name == "mixamo.com") return clip;
            if (fallback == null && !clip.name.Contains("|") && !clip.name.StartsWith("Wizard_"))
                fallback = clip;
        }
        return fallback;
    }

    // mixamo.com 테이크의 본 경로(Armature/...) 앞에 WizardModel/ 을 붙인 복제본을 만든다.
    // 캐릭터 본 경로(WizardModel/Armature/...)와 맞춰야 컨트롤러에서 재생될 때 쓰러진다.
    // 이미 있으면 그대로 쓴다.
    static AnimationClip FixClipPaths(AnimationClip source)
    {
        if (source == null) return null;
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(FixedClipPath);
        if (existing != null) return existing;
        var copy = new AnimationClip();
        copy.name = "WizardDeath";
        copy.wrapMode = WrapMode.ClampForever;
        // 트랜스폼 커브 경로를 고친다.
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
            string path = binding.path;
            if (path == "Armature" || path.StartsWith("Armature/"))
                path = "WizardModel/" + path;
            var fixedBinding = new EditorCurveBinding
            {
                path = path,
                propertyName = binding.propertyName,
                type = binding.type,
            };
            AnimationUtility.SetEditorCurve(copy, fixedBinding, curve);
        }
        // 오브젝트 참조 커브(거의 없지만 혹시 모르니)도 같은 규칙으로 고친다.
        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
        {
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            string path = binding.path;
            if (path == "Armature" || path.StartsWith("Armature/"))
                path = "WizardModel/" + path;
            var fixedBinding = new EditorCurveBinding
            {
                path = path,
                propertyName = binding.propertyName,
                type = binding.type,
            };
            AnimationUtility.SetObjectReferenceCurve(copy, fixedBinding, keys);
        }
        copy.legacy = source.legacy;
        AssetDatabase.CreateAsset(copy, FixedClipPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DeathMotion] 경로 수정 클립 생성: {FixedClipPath} (길이 {copy.length:0.00}s)");
        return copy;
    }

    // 사망 모션을 Humanoid 리타겟으로 재생하도록 베이스 컨트롤러들에 Death 상태를 넣는다.
    // Playable 직접 재생은 아바타 매핑이 안 맞으면 안 움직일 수 있어서 컨트롤러 경유로 바꾼다.
    // AnyState -> Death (Death=true), Death는 루프 없이 마지막 자세에서 멈춘다.
    [MenuItem("Tools/Player/Patch Death State")]
    public static void PatchControllersMenu()
    {
        AnimationClip death = FindDeathClip();
        if (death == null)
        {
            Debug.LogError("[DeathMotion] Dying.fbx에서 mixamo.com 클립을 찾지 못했다.");
            return;
        }
        PatchControllers(FixClipPaths(death));
    }

    static void PatchControllers(AnimationClip death)
    {
        foreach (string path in Controllers)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogError($"[DeathMotion] 컨트롤러를 찾지 못했다: {path}");
                continue;
            }
            bool dirty = false;
            if (!HasDeathParam(controller))
            {
                controller.AddParameter(DeathParam, AnimatorControllerParameterType.Bool);
                dirty = true;
            }
            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState deathState = FindDeathState(root);
            if (deathState == null)
            {
                deathState = root.AddState("Death");
                dirty = true;
            }
            if (deathState.motion != death)
            {
                deathState.motion = death;
                dirty = true;
            }
            // 마지막 자세에서 멈추게 루프 끄기. 이미 패치된 경우에도 한 번은 적용된다.
            {
                var settings = AnimationUtility.GetAnimationClipSettings(death);
                if (settings.loopTime)
                {
                    settings.loopTime = false;
                    AnimationUtility.SetAnimationClipSettings(death, settings);
                    dirty = true;
                }
            }
            AnimatorStateTransition link = null;
            foreach (AnimatorStateTransition t in root.anyStateTransitions)
            {
                if (t.destinationState == deathState)
                {
                    link = t;
                    break;
                }
            }
            if (link == null)
            {
                link = root.AddAnyStateTransition(deathState);
                dirty = true;
            }
            link.hasExitTime = false;
            link.hasFixedDuration = true;
            link.duration = 0.1f;
            link.canTransitionToSelf = false;
            bool hasCond = false;
            foreach (AnimatorCondition c in link.conditions)
            {
                if (c.parameter == DeathParam && c.mode == AnimatorConditionMode.If)
                {
                    hasCond = true;
                    break;
                }
            }
            if (!hasCond)
            {
                link.AddCondition(AnimatorConditionMode.If, 0f, DeathParam);
                dirty = true;
            }
            if (dirty)
            {
                EditorUtility.SetDirty(controller);
                Debug.Log($"[DeathMotion] Death 상태 패치: {path} <- {death.name} ({death.length:0.00}s)");
            }
            else
            {
                Debug.Log($"[DeathMotion] 이미 패치됨: {path}");
            }
        }
        AssetDatabase.SaveAssets();
    }

    static bool HasDeathParam(AnimatorController controller)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
            if (p.name == DeathParam && p.type == AnimatorControllerParameterType.Bool) return true;
        return false;
    }

    static AnimatorState FindDeathState(AnimatorStateMachine root)
    {
        foreach (ChildAnimatorState s in root.states)
            if (s.state.name == "Death") return s.state;
        return null;
    }
}
#endif
