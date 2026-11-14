using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One local presentation per connection, called only for a newly confirmed drop.
public sealed class WeaponSpawnNotification : MonoBehaviour
{
    const float FadeIn = .12f;
    const float Hold = 2f;
    const float FadeOut = .45f;
    GameObject canvasObject;
    CanvasGroup group;
    float startedAt;

    void Awake() => enabled = false;

    public void Show(TMP_FontAsset font)
    {
        if (canvasObject == null) CreatePresentation(font);
        canvasObject.SetActive(true);
        startedAt = Time.unscaledTime;
        group.alpha = 0f;
        enabled = true;
        // Restart instead of stacking if two confirmations arrive close together.
        Sfx.Stop(SfxId.WeaponSpawnChime);
        Sfx.Play(SfxId.WeaponSpawnChime);
    }

    public void Hide() => enabled = false;

    void OnDisable()
    {
        Sfx.Stop(SfxId.WeaponSpawnChime);
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    void Update()
    {
        float age = Time.unscaledTime - startedAt;
        if (age >= FadeIn + Hold + FadeOut) { Hide(); return; }
        group.alpha = age < FadeIn ? Mathf.SmoothStep(0, 1, age / FadeIn) :
            1f - Mathf.SmoothStep(0, 1, (age - FadeIn - Hold) / FadeOut);
    }

    void CreatePresentation(TMP_FontAsset font)
    {
        canvasObject = new GameObject("Weapon Spawn Notification", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        group = canvasObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var panel = Image("Gold border", canvasObject.transform, new Vector2(440, 58),
            new Color(.52f, .42f, .25f, .8f));
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, 1);
        panel.pivot = new Vector2(.5f, 1);
        panel.anchoredPosition = new Vector2(0, -38);
        Image("Background", panel, new Vector2(438, 56), new Color(.045f, .055f, .075f, .9f));
        var accent = Image("Rune", panel, new Vector2(10, 10), new Color(.70f, .53f, .29f, .9f));
        accent.anchoredPosition = new Vector2(-191, 0);
        accent.localRotation = Quaternion.Euler(0, 0, 45);

        var labelObject = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(panel, false);
        rect.sizeDelta = new Vector2(375, 48);
        rect.anchoredPosition = new Vector2(12, 0);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = "마법 무기가 등장했습니다!";
        label.fontSize = 24;
        label.enableAutoSizing = true;
        label.fontSizeMin = 18;
        label.fontSizeMax = 24;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.94f, .91f, .84f, 1);
        label.raycastTarget = false;
    }

    static RectTransform Image(string name, Transform parent, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }
}
