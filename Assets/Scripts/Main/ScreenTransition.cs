using System.Collections;
using UnityEngine;

// 화면 캔버스를 아래로 내리며 감추고, 아래에서 올리며 보여 준다. 넘어가는 동안에는 입력을 막는다.
// 화면 전체를 덮는 그림이나 3D를 따라다니는 명패처럼 제자리에 있어야 하는 것은 sliding에 넣지 않고 페이드만 한다.
public class ScreenTransition : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [Tooltip("내려가며 사라지고 올라오며 나타나는 요소.")]
    [SerializeField] RectTransform[] sliding;
    [Tooltip("요소가 내려가거나 올라오는 거리.")]
    [SerializeField] float distance = 40f;
    [SerializeField, Min(0.01f)] float hideDuration = 0.3f;
    [SerializeField, Min(0.01f)] float showDuration = 0.4f;

    Vector2[] homes;

    void Awake()
    {
        homes = new Vector2[sliding.Length];
        for (int i = 0; i < sliding.Length; i++) homes[i] = sliding[i].anchoredPosition;
    }

    // 점점 빨라지며 내려가 사라진 뒤 화면을 끈다. 다음에 다시 켜도 제자리에 있도록 위치를 되돌려 둔다.
    // 걷던 도중에 멈췄다가 다시 부르면 처음부터 다시 나타나지 않고 그 자리에서 이어서 걷는다.
    public IEnumerator Hide()
    {
        SetInteractable(false);
        float start = Mathf.Sqrt(Mathf.Clamp01(1f - group.alpha)) * hideDuration;
        for (float time = start; time < hideDuration; time += Time.unscaledDeltaTime)
        {
            float t = time / hideDuration;
            Apply(1f - t * t);
            yield return null;
        }
        gameObject.SetActive(false);
        Apply(1f);
        SetInteractable(true);
    }

    // 화면을 켜고 아래에서 올라오며 나타난다. 첫 프레임부터 감춘 상태로 시작해서 제자리에 잠깐 보이는 일이 없다.
    public IEnumerator Show()
    {
        gameObject.SetActive(true);
        SetInteractable(false);
        for (float time = 0f; time < showDuration; time += Time.unscaledDeltaTime)
        {
            float t = 1f - time / showDuration;
            Apply(1f - t * t * t);
            yield return null;
        }
        Apply(1f);
        SetInteractable(true);
    }

    // shown이 1이면 제자리에 다 보이고, 0이면 distance만큼 내려가 사라진 상태다.
    void Apply(float shown)
    {
        group.alpha = shown;
        Vector2 offset = Vector2.down * (distance * (1f - shown));
        for (int i = 0; i < sliding.Length; i++) sliding[i].anchoredPosition = homes[i] + offset;
    }

    void SetInteractable(bool value) => group.interactable = group.blocksRaycasts = value;
}
