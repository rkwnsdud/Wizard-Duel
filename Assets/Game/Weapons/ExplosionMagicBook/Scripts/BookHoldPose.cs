using UnityEngine;

// 폭발 마법을 장착하면 왼손에 펼친 책을 든다.
// Animator가 자세를 쓴 뒤(LateUpdate) 책을 가슴 앞에 두고, 왼팔을 2본 IK로 책 아래 손잡이 지점에 맞춘다.
// Animator가 매 프레임 팔을 다시 쓰므로 해제하면 자세는 저절로 원래대로 돌아간다.
[DisallowMultipleComponent]
public sealed class BookHoldPose : MonoBehaviour, IWeaponPose
{
    [SerializeField, Tooltip("Book visual moved in front of the chest every frame.")]
    private Transform book;
    [SerializeField, Tooltip("Child of the book where the left wrist goes. Its rotation is the left hand rotation.")]
    private Transform handGrip;
    [SerializeField, Tooltip("Book position from the chest bone, in body space (x right, y up, z forward).")]
    private Vector3 bookOffset = new Vector3(-0.14f, -0.22f, 0.33f);
    [SerializeField, Tooltip("Book rotation in body space.")]
    private Vector3 bookEuler = new Vector3(-40f, 15f, 0f);
    [SerializeField, Tooltip("Where the left elbow points, from the left shoulder in body space.")]
    private Vector3 elbowHint = new Vector3(-0.5f, -0.4f, -0.1f);
    [SerializeField, Tooltip("Left hand bone rotation relative to the hand grip.")]
    private Vector3 handEuler = new Vector3(0f, 90f, 90f);

    private PlayerAttack owner;
    private WeaponBase weapon;
    private Transform chest, upperArm, foreArm, hand;

    private void LateUpdate()
    {
        if (owner == null) owner = GetComponentInParent<PlayerAttack>();
        if (weapon == null) weapon = GetComponentInParent<WeaponBase>();
        // 원격 플레이어도 입력과 무관하게 장착 무기를 기준으로 자세를 맞춘다.
        if (owner == null || !owner.IsWeaponPoseActive(weapon)) return;
        Apply();
    }

    // 에디터 미리보기에서도 부를 수 있다.
    public void Apply()
    {
        if (!Bind() || book == null) return;
        Transform body = owner != null ? owner.transform : transform.root;
        Vector3 forward = body.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        Quaternion bodyRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        book.SetPositionAndRotation(chest.position + bodyRotation * bookOffset,
            bodyRotation * Quaternion.Euler(bookEuler));
        if (handGrip == null) return;
        TwoBoneIK.Solve(upperArm, foreArm, hand, handGrip.position,
            upperArm.position + bodyRotation * elbowHint);
        hand.rotation = handGrip.rotation * Quaternion.Euler(handEuler);
    }

    private bool Bind()
    {
        if (chest != null && upperArm != null && foreArm != null && hand != null) return true;
        if (owner == null) owner = GetComponentInParent<PlayerAttack>();
        Transform root = owner != null ? owner.transform : transform.root;
        foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
        {
            switch (bone.name)
            {
                case "Spine": chest = bone; break;
                case "LeftArm": upperArm = bone; break;
                case "LeftForeArm": foreArm = bone; break;
                case "LeftHand": hand = bone; break;
            }
        }
        return chest != null && upperArm != null && foreArm != null && hand != null;
    }
}
