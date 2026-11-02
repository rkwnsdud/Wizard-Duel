using UnityEngine;

/// <summary>Preserves the repaired grip during Basic Wand idle, cast and their blends.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class BasicWandGripSkin : MonoBehaviour
{
    [SerializeField] SkinnedMeshRenderer characterSkin;
    [SerializeField] Mesh originalMesh;
    [SerializeField] Mesh gripMesh;
    [SerializeField] Renderer wandRenderer;
    [SerializeField] Animator targetAnimator;
    [SerializeField] AnimatorOverrideController idleController;
    [SerializeField, Tooltip("Keep the repaired hand visible for edit-mode inspection while the wand is hidden. Runtime weapon/idle checks still apply.")]
    bool previewWhenWandHiddenInEditor;
    static readonly int IdleState = Animator.StringToHash("Idle");
    static readonly int CastState = Animator.StringToHash("Cast");

    public Mesh OriginalMesh => originalMesh;
    public Mesh GripMesh => gripMesh;
    public SkinnedMeshRenderer CharacterSkin => characterSkin;
    public bool PreviewWhenWandHiddenInEditor => previewWhenWandHiddenInEditor;

    public void SetEditorPreviewWhenHidden(bool enabled)
    {
        previewWhenWandHiddenInEditor = enabled;
        RefreshSkin();
    }

    public void Configure(SkinnedMeshRenderer skin, Mesh original, Mesh repaired,
        Renderer wand, Animator animator, AnimatorOverrideController controller)
    {
        Restore();
        characterSkin = skin;
        originalMesh = original;
        gripMesh = repaired;
        wandRenderer = wand;
        targetAnimator = animator;
        idleController = controller;
        RefreshSkin();
    }

    void OnEnable() => RefreshSkin();
    void LateUpdate() => RefreshSkin();
    void OnDisable() => Restore();
    void OnDestroy() => Restore();

    public void RefreshSkin()
    {
        if (!characterSkin || !originalMesh || !gripMesh) return;
        if (!isActiveAndEnabled) { Restore(); return; }
        bool useGrip = wandRenderer && wandRenderer.enabled && wandRenderer.gameObject.activeInHierarchy;
#if UNITY_EDITOR
        useGrip = useGrip || (!Application.isPlaying && previewWhenWandHiddenInEditor);
#endif
        if (Application.isPlaying)
        {
            useGrip = useGrip && targetAnimator && targetAnimator.isInitialized &&
                targetAnimator.runtimeAnimatorController == idleController;
            if (useGrip)
            {
                int current = targetAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
                int next = targetAnimator.IsInTransition(0)
                    ? targetAnimator.GetNextAnimatorStateInfo(0).shortNameHash : current;
                useGrip = (current == IdleState || current == CastState) &&
                    (next == IdleState || next == CastState);
                // Retain the grip while blending out of a walk into the cast.
                useGrip |= next == CastState;
            }
        }
        if (!useGrip) { Restore(); return; }
        // Do not replace a mesh installed by another customization or weapon.
        if (characterSkin.sharedMesh == originalMesh)
            characterSkin.sharedMesh = gripMesh;
    }

    void Restore()
    {
        if (characterSkin && originalMesh && characterSkin.sharedMesh == gripMesh)
            characterSkin.sharedMesh = originalMesh;
    }
}
