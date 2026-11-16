using System.Collections;
using TMPro;
using UnityEngine;

// 승부가 나면 화면 가운데에 이긴 쪽을 크게 띄운다. 서버가 결과 단계를 끝내고 로비로 돌려보낼 때까지 떠 있다.
// 두 클라이언트가 같은 결과 스냅샷을 받아 띄우므로 두 화면에 같은 승자가 같은 시간 동안 보인다.
public class ResultBanner : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text label;
    [Tooltip("0은 방장(파랑), 1은 참가자(빨강)이 이겼을 때의 글자 색. 로비 화면 명패의 BLUE·RED와 같은 색이다.")]
    [SerializeField] Color[] teamColors =
    {
        new Color32(0x4A, 0xA3, 0xFF, 0xFF),
        new Color32(0xFF, 0x56, 0x56, 0xFF)
    };
    [Tooltip("글자가 이 배율에서 제 크기로 줄어들며 나타난다.")]
    [SerializeField, Min(1f)] float popScale = 1.6f;
    [SerializeField, Min(0.01f)] float popDuration = 0.35f;
    [SerializeField, Min(0.01f)] float fadeOut = 0.3f;

    static readonly string[] Titles = { "BLUE WIN!", "RED WIN!" };

    int shownSlot = -1;

    void Awake()
    {
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;
    }

    public void Show(int winnerSlot)
    {
        if (winnerSlot < 0 || winnerSlot >= Titles.Length || shownSlot == winnerSlot) return;
        shownSlot = winnerSlot;
        label.text = Titles[winnerSlot];
        label.color = teamColors[winnerSlot];
        gameObject.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(Pop());
    }

    public void Hide()
    {
        if (shownSlot < 0) return;
        shownSlot = -1;
        StopAllCoroutines();
        if (isActiveAndEnabled) StartCoroutine(Fade());
        else group.alpha = 0f;
    }

    // 크게 떠서 제 크기로 줄어들며 나타난다.
    IEnumerator Pop()
    {
        for (float time = 0f; time < popDuration; time += Time.unscaledDeltaTime)
        {
            float t = time / popDuration;
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            group.alpha = t;
            label.rectTransform.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, eased);
            yield return null;
        }
        group.alpha = 1f;
        label.rectTransform.localScale = Vector3.one;
    }

    IEnumerator Fade()
    {
        float start = group.alpha;
        for (float time = 0f; time < fadeOut; time += Time.unscaledDeltaTime)
        {
            group.alpha = start * (1f - time / fadeOut);
            yield return null;
        }
        group.alpha = 0f;
        label.rectTransform.localScale = Vector3.one;
    }
}
