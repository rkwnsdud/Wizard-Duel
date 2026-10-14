using System.Collections;
using TMPro;
using UnityEngine;

// 화면 위쪽에 짧은 안내를 잠깐 띄운다. 새 안내가 오면 앞의 것을 바로 바꾼다.
public class Toast : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text label;
    [SerializeField] float duration = 3.2f;
    [SerializeField] float fadeIn = 0.3f;
    [SerializeField] float fadeOut = 0.5f;

    void Awake()
    {
        group.alpha = 0f;
    }

    public void Show(string message)
    {
        label.text = message;
        StopAllCoroutines();
        StartCoroutine(Fade());
    }

    IEnumerator Fade()
    {
        for (float time = 0f; time < duration; time += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Min(Mathf.Clamp01(time / fadeIn), Mathf.Clamp01((duration - time) / fadeOut));
            yield return null;
        }
        group.alpha = 0f;
    }
}
