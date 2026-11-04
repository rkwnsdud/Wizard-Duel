using UnityEngine;

public class LightOrbWeapon : WeaponBase
{
    [SerializeField, Min(.01f)] private float coreFlashDuration = .18f;
    private Renderer coreRenderer;
    private MaterialPropertyBlock coreFlashProperties;
    private float coreFlashRemaining;
    private static readonly int CoreFlashId=Shader.PropertyToID("_AttackCoreFlash");

    private void SetCoreFlash(float amount)
    {
        if(!coreRenderer) return;
        if(coreFlashProperties==null) coreFlashProperties=new MaterialPropertyBlock();
        coreRenderer.GetPropertyBlock(coreFlashProperties);
        coreFlashProperties.SetFloat(CoreFlashId,amount);
        coreRenderer.SetPropertyBlock(coreFlashProperties);
    }

    private void FlashCore()
    {
        if(!coreRenderer)
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
                if(renderer.name=="Crystal_L4") { coreRenderer=renderer; break; }
        coreFlashRemaining=Mathf.Max(.32f,coreFlashDuration);
        SetCoreFlash(1f);
    }

    private void UpdateCoreFlash()
    {
        if(coreFlashRemaining<=0f) return;
        coreFlashRemaining=Mathf.Max(0f,coreFlashRemaining-Time.deltaTime);
        float duration=Mathf.Max(.32f,coreFlashDuration);
        const float peakHold=.10f;
        float fadeDuration=Mathf.Max(.01f,duration-peakHold);
        float strength=Mathf.Clamp01(coreFlashRemaining/fadeDuration);
        SetCoreFlash(Mathf.SmoothStep(0f,1f,strength));
    }
    [SerializeField] private bool palmUpPose = true;
    [Tooltip("Mesh palm alignment: positive values roll the right forearm toward the torso.")]
    [Range(-45f,45f)] [SerializeField] private float palmInwardRoll = 20f;
    private Transform poseHand, poseForearm, poseIndex, poseThumb;
    private Quaternion savedHand, savedForearm, appliedHand, appliedForearm, savedWeaponRotation;
    private Vector3 savedWeaponPosition;
    private bool poseApplied;
    private PlayerAttack poseOwner;
    private sealed class FingerPose
    {
        public Transform bone;
        public Quaternion saved, applied;
        public Vector3 localAxis;
        public int joint;
        public bool thumb;
    }
    private readonly System.Collections.Generic.List<FingerPose> fingerPoses = new System.Collections.Generic.List<FingerPose>();

    private void CacheFingerRestPose()
    {
        // Use actual joint chains, including meshes with incomplete skin bone
        // lists. A bind pose can itself be a grip and is not an open-hand target.
        foreach(Transform bone in poseHand.GetComponentsInChildren<Transform>(true))
        {
            int joint=bone.name.Contains("Proximal")?0:bone.name.Contains("Intermediate")?1:bone.name.Contains("Distal")?2:-1;
            if(joint<0 || !bone.name.StartsWith("Right")) continue;
            Vector3 localAxis=Vector3.up;
            if(bone.childCount>0 && bone.GetChild(0).localPosition.sqrMagnitude>1e-8f)
                localAxis=bone.GetChild(0).localPosition.normalized;
            fingerPoses.Add(new FingerPose {bone=bone,localAxis=localAxis,joint=joint,thumb=bone.name.Contains("Thumb")});
        }
    }
    private void Update() { RestorePalmPose(); }
    private void LateUpdate() { ApplyPalmUpPose(); UpdateCoreFlash(); }
    private void OnDisable() { RestorePalmPose(); coreFlashRemaining=0f; SetCoreFlash(0f); }

    public void RestorePalmPose()
    {
        if (!poseApplied) return;
        foreach(var finger in fingerPoses)
            if(finger.bone && Quaternion.Angle(finger.bone.localRotation,finger.applied)<.01f)
                finger.bone.localRotation=finger.saved;
        // Do not overwrite a newer pose supplied by the Animator.
        if (poseHand && Quaternion.Angle(poseHand.localRotation,appliedHand)<.01f) poseHand.localRotation=savedHand;
        if (poseForearm && Quaternion.Angle(poseForearm.localRotation,appliedForearm)<.01f) poseForearm.localRotation=savedForearm;
        transform.localPosition=savedWeaponPosition;
        transform.localRotation=savedWeaponRotation;
        poseApplied=false;
    }

    public void ApplyPalmUpPose()
    {
        // Editor captures may explicitly preview a pose; during gameplay the
        // attack system's pose owner is the authority, including the visible
        // animation-only wand that can coexist with an old WeaponBase reference.
        if (Application.isPlaying)
        {
            if (!poseOwner) poseOwner=GetComponentInParent<PlayerAttack>();
            if (!poseOwner || !poseOwner.isActiveAndEnabled || !poseOwner.IsWeaponPoseActive(this))
            {
                RestorePalmPose();
                return;
            }
        }
        if (!palmUpPose || !isActiveAndEnabled || poseApplied) return;
        if (!poseHand)
        {
            for (Transform t=transform.parent;t!=null;t=t.parent)
                if (t.name=="RightHand") { poseHand=t; break; }
            if (!poseHand) return;
            poseForearm=poseHand.parent;
            foreach (Transform t in poseHand.GetComponentsInChildren<Transform>(true))
            {
                if(t.name=="RightIndexProximal") poseIndex=t;
                if(t.name=="RightThumbProximal") poseThumb=t;
            }
            CacheFingerRestPose();
        }
        if (!poseForearm || !poseIndex || !poseThumb) return;
        Vector3 axis=(poseHand.position-poseForearm.position).normalized;
        if(!poseOwner) poseOwner=GetComponentInParent<PlayerAttack>();
        Vector3 bodyRight=poseOwner ? poseOwner.transform.right : Vector3.right;
        Vector3 inward=Vector3.ProjectOnPlane(-bodyRight,axis).normalized;
        Vector3 upAcrossArm=Vector3.ProjectOnPlane(Vector3.up,axis).normalized;
        // The thumb/index joint plane is not the visible palm plane on this
        // model. Calibrate the forearm roll toward the torso without changing
        // the elbow-to-wrist direction or adding lateral wrist deviation.
        float inwardSign=Mathf.Sign(Vector3.Dot(Vector3.Cross(axis,upAcrossArm),inward));
        // Additional mesh calibration requested after inspecting the 20-degree pose.
        const float palmMeshCalibration = 7f;
        Vector3 palmTarget=Quaternion.AngleAxis((palmInwardRoll+palmMeshCalibration)*inwardSign,axis)*upAcrossArm;
        Vector3 palm=Vector3.Cross(poseIndex.position-poseHand.position,poseThumb.position-poseHand.position);
        Vector3 from=Vector3.ProjectOnPlane(palm,axis);
        Vector3 to=palmTarget;
        if(from.sqrMagnitude<1e-10f || to.sqrMagnitude<1e-6f) return;
        float roll=Vector3.SignedAngle(from,to,axis);
        savedHand=poseHand.localRotation; savedForearm=poseForearm.localRotation;
        savedWeaponPosition=transform.localPosition; savedWeaponRotation=transform.localRotation;
        Vector3 weaponPosition=transform.position; Quaternion weaponRotation=transform.rotation;
        // Supination belongs to the forearm, not axial twisting of the wrist.
        poseForearm.rotation=Quaternion.AngleAxis(roll,axis)*poseForearm.rotation;
        Vector3 palmNormal=Vector3.Cross(poseIndex.position-poseHand.position,
            poseThumb.position-poseHand.position).normalized;
        Vector3 handForward=(poseIndex.position-poseHand.position).normalized;
        Vector3 neutralUp=palmTarget;
        // Match the entire palm frame to the forearm: eliminate both lateral
        // deviation and the inherited cocked wrist, without a body-inward bias.
        if(neutralUp.sqrMagnitude>1e-6f && handForward.sqrMagnitude>1e-6f)
        {
            Quaternion sourceFrame=Quaternion.LookRotation(handForward,palmNormal);
            Quaternion targetFrame=Quaternion.LookRotation(axis,neutralUp);
            poseHand.rotation=targetFrame*Quaternion.Inverse(sourceFrame)*poseHand.rotation;
            palmNormal=neutralUp;
        }
        foreach(var finger in fingerPoses)
        {
            if(!finger.bone) continue;
            finger.saved=finger.bone.localRotation;
            Vector3 forward=Vector3.ProjectOnPlane(axis,palmNormal).normalized;
            Vector3 thumbSide=Vector3.Cross(palmNormal,forward).normalized;
            Vector3 target=finger.thumb
                ? (forward*.75f+thumbSide*.5f+palmNormal*(.08f+finger.joint*.07f)).normalized
                : (forward+palmNormal*(.06f+finger.joint*.10f)).normalized;
            Vector3 actual=finger.bone.TransformDirection(finger.localAxis);
            finger.bone.rotation=Quaternion.FromToRotation(actual,target)*finger.bone.rotation;
            finger.applied=finger.bone.localRotation;
        }
        transform.SetPositionAndRotation(weaponPosition,weaponRotation);
        appliedHand=poseHand.localRotation; appliedForearm=poseForearm.localRotation;
        poseApplied=true;
    }
    [SerializeField] private Transform firePoint;
    [SerializeField] private LightOrbProjectile lightAttackPrefab;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lightSound;
    private PlayerAim playerAim;

    private void Awake()
    {
        playerAim = GetComponentInParent<PlayerAim>(true);
    }

    public override void Fire(float chargeRatio)
    {
        // A serialized weapon can be called before its inactive object's Awake.
        // Resolve the actual owner again instead of dropping the attack.
        if (playerAim == null)
            playerAim = GetComponentInParent<PlayerAim>(true);
        if (playerAim == null)
            throw new System.InvalidOperationException(
                $"LightOrbWeapon '{name}' must be parented under a player with PlayerAim.");

        Vector3 direction = playerAim.transform.forward;

        direction.y = 0f;
        direction.Normalize();
      
        if (audioSource != null && lightSound != null)
        {
            audioSource.PlayOneShot(lightSound);
        }

        LightOrbProjectile attack = Instantiate(
            lightAttackPrefab,
            firePoint.position,
            Quaternion.identity
        );

        // 발사자 정보 가져오기
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();

        attack.Initialize(direction, owner, audioSource);
        FlashCore();
    }
}


