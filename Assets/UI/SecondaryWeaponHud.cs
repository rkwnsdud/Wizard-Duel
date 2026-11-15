using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A screen canvas is created only for the local player's inventory. The world-space
// health and cooldown bars on the combat prefab remain separate.
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerWeaponInventory))]
public sealed class SecondaryWeaponHud : MonoBehaviour
{
    [SerializeField] private WeaponSpawnCatalog catalog;
    [SerializeField] private TMP_FontAsset fontAsset;

    private PlayerWeaponInventory inventory;
    private GameObject canvasObject;
    private Image iconImage;
    private GameObject iconWell;
    private GameObject keyBadge;
    private TextMeshProUGUI slotLabel;
    private TextMeshProUGUI nameText;

    private static readonly Color OuterGold = new Color(0.52f, 0.42f, 0.25f, 0.9f);
    private static readonly Color InnerGold = new Color(0.31f, 0.27f, 0.20f, 0.8f);
    private static readonly Color Background = new Color(0.045f, 0.055f, 0.075f, 0.88f);
    private static readonly Color Ivory = new Color(0.94f, 0.91f, 0.84f, 1f);

    // Called by the owning NetworkBehaviour after its NetworkObject has spawned.
    public void ShowForLocalOwner()
    {
        if (canvasObject != null) return;
        inventory = GetComponent<PlayerWeaponInventory>();
        CreateCanvas();
        inventory.StoredWeaponChanged += Refresh;
        Refresh(inventory.StoredWeapon);
    }

    public void Hide()
    {
        if (inventory != null) inventory.StoredWeaponChanged -= Refresh;
        if (canvasObject != null) Destroy(canvasObject);
        canvasObject = null;
    }

    private void OnDisable() => Hide();

    private void CreateCanvas()
    {
        canvasObject = new GameObject("Secondary Weapon HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = canvasObject.GetComponent<RectTransform>();
        // Enlarge the entire slot, including its icon, text and Q badge.
        const float hudScale = 1.5f;
        RectTransform panel = Block("Border", root, OuterGold, new Vector2(292, 76), new Vector2(0, 16 + 38 * hudScale));
        panel.localScale = Vector3.one * hudScale;
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        RectTransform inset = Block("Panel", panel, Background, new Vector2(290, 74), Vector2.zero);
        Block("Inner border", inset, InnerGold, new Vector2(282, 66), Vector2.zero);
        RectTransform surface = Block("Surface", inset, Background, new Vector2(280, 64), Vector2.zero);
        Block("Top accent", surface, new Color(0.70f, 0.53f, 0.29f, 0.8f), new Vector2(48, 2), new Vector2(0, 31));

        iconWell = Block("Icon well", surface, new Color(0.18f, 0.21f, 0.25f, 0.9f), new Vector2(56, 56), new Vector2(-105, 0)).gameObject;
        Block("Icon edge", iconWell.transform, InnerGold, new Vector2(54, 54), Vector2.zero);
        RectTransform iconRect = Block("Weapon icon", iconWell.transform, Color.white, new Vector2(50, 50), Vector2.zero);
        iconImage = iconRect.GetComponent<Image>();
        iconImage.preserveAspect = true;

        slotLabel = Text("Slot label", surface, "무기 교체", 12, new Vector2(146, 18), new Vector2(3, 13),
            new Color(0.68f, 0.61f, 0.47f, 1f));
        nameText = Text("Weapon name", surface, "", 18, new Vector2(146, 30), new Vector2(3, -10), Ivory);

        keyBadge = Block("Q edge", surface, OuterGold, new Vector2(29, 29), new Vector2(118, 0)).gameObject;
        RectTransform keyFace = Block("Q face", keyBadge.transform, new Color(0.13f, 0.15f, 0.18f, 1f), new Vector2(27, 27), Vector2.zero);
        TextMeshProUGUI key = Text("Q", keyFace, "Q", 17, new Vector2(25, 25), Vector2.zero, Ivory);
        key.alignment = TextAlignmentOptions.Center;
    }

    private void Refresh(WeaponBase storedWeapon)
    {
        if (canvasObject == null) return;

        bool filled = storedWeapon != null;
        WeaponDefinition entry = filled && catalog != null ? catalog.Find(storedWeapon.Id) : null;
        iconWell.SetActive(filled && entry != null && entry.icon != null);
        keyBadge.SetActive(filled);
        slotLabel.gameObject.SetActive(filled);
        nameText.rectTransform.sizeDelta = filled ? new Vector2(146, 30) : new Vector2(240, 30);
        nameText.rectTransform.anchoredPosition = filled ? new Vector2(3, -10) : Vector2.zero;
        nameText.alignment = filled ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        iconImage.sprite = entry != null ? entry.icon : null;
        nameText.text = !filled ? "보조 무기 없음" : entry != null && !string.IsNullOrWhiteSpace(entry.hudDisplayName)
            ? entry.hudDisplayName : storedWeapon.name.Replace("(Clone)", "").Trim();
    }

    private static RectTransform Block(string name, Transform parent, Color color, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private TextMeshProUGUI Text(string name, Transform parent, string value, float size, Vector2 dimensions,
        Vector2 position, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) text.font = fontAsset;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12;
        text.fontSizeMax = size;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }
}
