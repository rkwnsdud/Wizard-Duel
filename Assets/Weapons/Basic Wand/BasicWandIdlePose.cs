using UnityEngine;

/// <summary>Owns the equipped wand's pose controller; preserves explicit editor previews.</summary>
[DisallowMultipleComponent]
public sealed class BasicWandIdlePose : MonoBehaviour
{
    [SerializeField] Animator targetAnimator;
    [SerializeField] RuntimeAnimatorController baseController;
    [SerializeField] AnimatorOverrideController idleController;
    [SerializeField] Renderer wandRenderer;
    bool ownsController;
    WeaponBase weapon;
    PlayerAttack attackOwner;

    public void Configure(Animator animator, RuntimeAnimatorController baseline,
        AnimatorOverrideController idle, Renderer model)
    {
        Restore();
        UnbindOwner();
        targetAnimator = animator;
        baseController = baseline;
        idleController = idle;
        wandRenderer = model;
        if (isActiveAndEnabled)
        {
            BindOwner();
            RefreshPose();
        }
    }

    void OnEnable()
    {
        BindOwner();
        RefreshPose();
    }
    void Update() => RefreshPose();

    void BindOwner()
    {
        UnbindOwner();
        weapon = GetComponent<WeaponBase>();
        attackOwner = GetComponentInParent<PlayerAttack>(true);
        if (attackOwner != null) attackOwner.EquippedWeaponChanged += RefreshPose;
    }

    void UnbindOwner()
    {
        if (attackOwner != null) attackOwner.EquippedWeaponChanged -= RefreshPose;
        attackOwner = null;
    }

    void RefreshPose()
    {
        if (!targetAnimator || !baseController || !idleController) return;
        // Gameplay follows equipment, not renderer visibility. Edit-mode capture
        // tools can still explicitly preview a visible model without equipping it.
        bool selected = Application.isPlaying
            ? attackOwner != null && attackOwner.IsWeaponPoseActive(weapon)
            : wandRenderer && wandRenderer.enabled && wandRenderer.gameObject.activeInHierarchy;
        if (!selected) { Restore(); return; }
        var current = targetAnimator.runtimeAnimatorController;
        if (current == idleController) { ownsController = true; return; }
        if (attackOwner != null && attackOwner.AnimationSourceController == idleController)
        {
            // PlayerAttack owns the temporary Cast override. Keep pose ownership
            // without replacing it; EndAttack/CancelAttack restores idleController.
            ownsController = true;
            return;
        }
        // Respect any controller installed by another weapon/system.
        if (current != baseController) { ownsController = false; return; }
        targetAnimator.runtimeAnimatorController = idleController;
        ownsController = true;
    }

    void OnDisable()
    {
        UnbindOwner();
        Restore();
    }
    void OnDestroy()
    {
        UnbindOwner();
        Restore();
    }
    void Restore()
    {
        if (ownsController && targetAnimator && targetAnimator.runtimeAnimatorController == idleController)
            targetAnimator.runtimeAnimatorController = baseController;
        ownsController = false;
    }
}
