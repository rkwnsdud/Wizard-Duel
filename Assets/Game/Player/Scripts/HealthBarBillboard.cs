using UnityEngine;
using UnityEngine.UI;

public class HealthBarBillboard : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Image damageTrailImage;
    [SerializeField] private float trailHoldDuration = 0.5f;
    [SerializeField] private float trailShrinkSpeed = 0.6f;

    private PlayerHealth playerHealth;
    private float previousFillAmount;
    private float trailHoldTimer;

    private void Awake()
    {
        playerHealth = GetComponentInParent<PlayerHealth>();
    }

    private void Start()
    {
        if (playerHealth == null || damageTrailImage == null)
            return;

        // 게임 시작 시 체력 비율 계산
        previousFillAmount = playerHealth.MaxHealth > 0f
            ? Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth)
            : 0f;

        // DamageTrail 초기화
        damageTrailImage.fillAmount = previousFillAmount;
    }

    private void LateUpdate()
    {
        // 체력바가 항상 카메라 방향을 향하도록 설정
        Camera targetCamera = Camera.main;

        if (targetCamera != null)
            transform.rotation = targetCamera.transform.rotation;

        if (fillImage != null && damageTrailImage != null && playerHealth != null)
        {
            // 현재 체력 비율 계산
            float currentFillAmount = playerHealth.MaxHealth > 0f
                ? Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth)
                : 0f;

            // 이전 체력과 비교하여 감소 여부 확인
            if (currentFillAmount < previousFillAmount)
            {
                trailHoldTimer = trailHoldDuration;
            }

            // 타이머 감소
            if (trailHoldTimer > 0f)
            {
                trailHoldTimer -= Time.deltaTime;
            }

            // 타이머 초과시 체력 줄어둠
            if (trailHoldTimer <= 0f)
            {
                damageTrailImage.fillAmount = Mathf.MoveTowards(
                    damageTrailImage.fillAmount,
                    currentFillAmount,
                    trailShrinkSpeed * Time.deltaTime
                );
            }

            // 현재 체력바 갱신
            fillImage.fillAmount = currentFillAmount;

            // 다음 프레임에서 비교할 이전 체력 저장
            previousFillAmount = currentFillAmount;
        }
    }
}
