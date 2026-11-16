
using UnityEngine;

public class BasicWandWeapon : WeaponBase, IWeapon, IWeaponReach
{
    [SerializeField] private ArcaneBoltProjectile projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private RuntimeAnimatorController basePoseController;
    [SerializeField] private AnimatorOverrideController wandPoseController;
    [SerializeField] private Mesh originalCharacterMesh;
    [SerializeField] private Mesh wandGripMesh;

    private bool presentingOwnShot;
    private PlayerAttack poseOwner;
    private Quaternion idleGripRotation;

    private void Awake() => idleGripRotation = transform.localRotation;

    private void LateUpdate()
    {
        // Idle has no wand transform track. Restore the attachment after a
        // cancelled cast too, even when the animation never reaches its last key.
        if (poseOwner && (!poseOwner.IsAttacking || poseOwner.AttackingWeapon != this))
            transform.localRotation = idleGripRotation;
    }

    private void OnDisable() => transform.localRotation = idleGripRotation;

    // Duel prefabs instantiate their own wand, so connect the grip pose
    // components when the wand starts.
    private void OnEnable() => EnsureGripPose();
    private void Start() => EnsureGripPose();

    private void EnsureGripPose()
    {
        if (!Application.isPlaying || !basePoseController || !wandPoseController ||
            !originalCharacterMesh || !wandGripMesh) return;
        PlayerAttack owner = GetComponentInParent<PlayerAttack>(true);
        if (!owner) return;
        poseOwner = owner;
        // The cast's attachment track resolves this child by its prefab name.
        if (name == "BasicWand(Clone)") name = "BasicWand";
        Transform wizard = owner.transform.Find("Wizard");
        if (!wizard || !wizard.TryGetComponent(out Animator animator)) return;
        MeshRenderer model = GetComponentInChildren<MeshRenderer>(true);
        if (!model) return;

        BasicWandIdlePose pose = GetComponent<BasicWandIdlePose>();
        if (!pose)
        {
            pose = gameObject.AddComponent<BasicWandIdlePose>();
            pose.Configure(animator, basePoseController, wandPoseController, model);
        }

        // A hand mesh can already be configured in an authored preview scene.
        if (wizard.GetComponent<BasicWandGripSkin>()) return;
        foreach (SkinnedMeshRenderer skin in wizard.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.sharedMesh != originalCharacterMesh) continue;
            BasicWandGripSkin grip = wizard.gameObject.AddComponent<BasicWandGripSkin>();
            grip.Configure(skin, originalCharacterMesh, wandGripMesh, model,
                animator, wandPoseController);
            break;
        }
    }

    public float ReachAt(float charge) => projectilePrefab != null ? projectilePrefab.MaxRange : 0f;
    public float ShotSpeed => projectilePrefab != null ? projectilePrefab.Speed : 0f;

    void IWeapon.Fire(AttackInfo attack, Vector3 direction, float charge, IShotSink sink)
    {
        if (sink == null || projectilePrefab == null || firePoint == null) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.root.forward;
        direction.Normalize();
        Vector3 start = firePoint.position;
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, start, Quaternion.LookRotation(direction));
        bolt.Initialize(GetComponentInParent<PlayerHealth>(), sink, attack);
        presentingOwnShot = true;
        try { sink.Broadcast(new ShotEvent(attack, 0, start, start + direction, charge)); }
        finally { presentingOwnShot = false; }
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind != 0) return;
        Sfx.Play(SfxId.WandFire);
        if (presentingOwnShot || projectilePrefab == null) return;
        Vector3 direction = shot.Point - shot.Start;
        if (direction.sqrMagnitude < 0.0001f) return;
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, shot.Start, Quaternion.LookRotation(direction));
        // 교체한 뒤에는 꺼진 무기가 연출하므로 꺼진 오브젝트에서도 주인을 찾는다.
        bolt.InitializeVisualOnly(GetComponentInParent<PlayerHealth>(true));
    }

}
