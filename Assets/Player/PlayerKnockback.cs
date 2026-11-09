using UnityEngine;

// 피격 넉백. 공격이 들어온 반대쪽으로 짧게 미끄러진다.
// CharacterController.Move로 움직이므로 이동과 똑같이 벽에 막힌다.
// 온라인에서는 위치 권한이 있는 소유자 화면에서만 부른다(DuelPlayer). 다른 화면에는 NetworkTransform으로 보인다.
public sealed class PlayerKnockback : MonoBehaviour
{
    [Tooltip("밀려나는 거리. 플레이어 키(2m)의 약 0.1배.")]
    [SerializeField, Min(0f)] float distance = 0.2f;
    [Tooltip("밀려나는 데 걸리는 시간. 처음에 빠르고 끝에서 멈춘다.")]
    [SerializeField, Min(0.01f)] float duration = 0.1f;

    CharacterController controller;
    Vector3 direction;
    // 0보다 작으면 밀려나는 중이 아니다.
    float elapsed = -1f;

    public float Distance => distance;
    public bool IsPlaying => elapsed >= 0f;

    void Awake() => controller = GetComponent<CharacterController>();

    // away 방향(수평)으로 민다. 밀려나는 중에 또 맞으면 새 방향으로 처음부터 다시 민다.
    public void Push(Vector3 away)
    {
        away.y = 0f;
        if (distance <= 0f || away.sqrMagnitude < 0.0001f) return;
        direction = away.normalized;
        elapsed = 0f;
    }

    void Update()
    {
        if (elapsed < 0f) return;
        float before = Travel(elapsed);
        elapsed = Mathf.Min(elapsed + Time.deltaTime, duration);
        if (controller != null && controller.enabled) controller.Move(direction * (Travel(elapsed) - before));
        if (elapsed >= duration) elapsed = -1f;
    }

    // 시작부터 time까지 밀려난 거리.
    float Travel(float time)
    {
        float remaining = 1f - Mathf.Clamp01(time / duration);
        return distance * (1f - remaining * remaining);
    }

    void OnDisable() => elapsed = -1f;
}
