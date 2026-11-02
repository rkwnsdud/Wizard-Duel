using UnityEngine;

/// <summary>Owns the wand's idle/cast overrides while its model is enabled.</summary>
[DisallowMultipleComponent]
public sealed class BasicWandIdlePose : MonoBehaviour
{
    [SerializeField] Animator targetAnimator;
    [SerializeField] RuntimeAnimatorController baseController;
    [SerializeField] AnimatorOverrideController idleController;
    [SerializeField] Renderer wandRenderer;
    bool ownsController;

    // The wand is currently an animation-only model, not a WeaponBase.
    public bool IsActiveFor(Animator animator)
    {
        if (!isActiveAndEnabled || targetAnimator != animator) return false;
        RefreshPose();
        return ownsController && targetAnimator.runtimeAnimatorController == idleController;
    }

    public void Configure(Animator animator, RuntimeAnimatorController baseline,
        AnimatorOverrideController idle, Renderer model)
    {
        targetAnimator = animator;
        baseController = baseline;
        idleController = idle;
        wandRenderer = model;
    }

    void OnEnable() => RefreshPose();
    void Update() => RefreshPose();

    void RefreshPose()
    {
        if (!targetAnimator || !baseController || !idleController) return;
        bool visible = wandRenderer && wandRenderer.enabled && wandRenderer.gameObject.activeInHierarchy;
        var current = targetAnimator.runtimeAnimatorController;
        if (!visible) { Restore(); return; }
        if (current == idleController) { ownsController = true; return; }
        // Respect any controller installed by another weapon/system.
        if (current != baseController) { ownsController = false; return; }
        targetAnimator.runtimeAnimatorController = idleController;
        ownsController = true;
    }

    void OnDisable() => Restore();
    void OnDestroy() => Restore();
    void Restore()
    {
        if (ownsController && targetAnimator && targetAnimator.runtimeAnimatorController == idleController)
            targetAnimator.runtimeAnimatorController = baseController;
        ownsController = false;
    }
}
