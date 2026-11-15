using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField, Tooltip("Dying.fbx 안의 mixamo.com 클립(쓰러지는 모션) 길이. 오프라인 더미 리스폰 대기 등에 쓴다. Tools/Player/Connect Dying Motion 메뉴로 자동 연결.")]
    private AnimationClip deathClip;

    private float currentHealth;
    private bool isDead = false;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    // 연결된 사망 모션 길이. 없으면 0. 오프라인 더미 리스폰 대기 등에 쓴다.
    public float DeathDuration => deathClip != null ? deathClip.length : 0f;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // 서버 스냅샷의 표시값이다. 피해 계산·중복 판정·승패 판정은 ServerHitReport/DuelCombatState가 맡는다.
    public void ApplyServerHealth(float value)
    {
        float previous = currentHealth;
        bool wasDead = isDead;
        currentHealth = value;
        isDead = value <= 0f;
        if (currentHealth >= previous || wasDead) return;

        Debug.Log("체력: " + currentHealth);

        if(currentHealth <= 0f)
        {
            StopPlayerAfterDeath();
            return;
        }

        PlayerHitEffect hitEffect = GetComponent<PlayerHitEffect>();

        if (hitEffect != null)
        {
            hitEffect.ChangeToRed();
        }
    }

    private void StopPlayerAfterDeath()
    {
        PlayerAttack playerAttack = GetComponent<PlayerAttack>();
        if (playerAttack != null)
        {
            // 시전 중이던 공격과 미리보기를 먼저 거둔다.
            playerAttack.CancelAttack();
            playerAttack.enabled = false;
        }

        PlayerMovement playerMovement = GetComponent<PlayerMovement>();
        if (playerMovement != null)
            playerMovement.enabled = false;

        PlayerAim playerAim = GetComponent<PlayerAim>();
        if (playerAim != null)
            playerAim.enabled = false;

        // 매 프레임 뼈를 직접 쓰는 자세 스크립트는 쓰러진 자세와 싸우므로 꺼둔다.
        // (컴포넌트만 꺼서 책·구슬 메시는 손에 들린 채로 함께 쓰러진다.)
        foreach (MonoBehaviour pose in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (pose is IWeaponPose)
                pose.enabled = false;
        }

        Transform character = transform.Find("Wizard");
        Animator animator = character != null ? character.GetComponent<Animator>() : null;
        if (animator != null)
        {
            animator.ResetTrigger("Attack");
            animator.SetFloat("MoveSpeed", 0f);
            animator.SetFloat("MoveX", 0f);
            animator.SetFloat("MoveZ", 0f);
            // Death 상태(AnyState -> Death, mixamo.com)가 있으면 리타겟으로 재생한다.
            // 없으면(구 컨트롤러) 예전처럼 애니메이터를 끄고 멈춘다.
            if (HasDeathState(animator))
                animator.SetBool("Death", true);
            else
                animator.enabled = false;
        }
    }

    // 컨트롤러에 패치된 Death 파라미터가 있는지 본다. 없으면 SetBool이 예외를 던지므로 구방식으로 멈춘다.
    private static bool HasDeathState(Animator animator)
    {
        if (animator == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.name == "Death" && p.type == AnimatorControllerParameterType.Bool) return true;
        return false;
    }
}
