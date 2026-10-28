using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

// 대기실의 맵 설정 패널. 방장은 컨트롤로 설정을 바꾸고, 참가자는 방장이 바꾼 값을 보기만 한다.
// 시드 줄의 "새로" 버튼은 Inspector의 OnClick에서 RollSeed를 부르고, 값을 바꾸는 컨트롤은 Awake에서 구독한다.
public class MapSettingsPanel : MonoBehaviour
{
    // 슬라이더는 정수 눈금으로 움직이고 실제 값은 이만큼 나눈다.
    const float DensitySteps = 100f;
    const float NoiseSteps = 20f;

    public event Action<DuelSettings> Changed;

    [SerializeField] GameObject lockNote;
    [Tooltip("방장만 만질 수 있는 컨트롤 묶음. 참가자에게는 흐리게 보인다.")]
    [SerializeField] CanvasGroup[] hostControls;
    [Tooltip("줄 순서는 시드, 크기, 형태, 장애물 밀도, 대칭, 노이즈 강도.")]
    [SerializeField] Graphic[] rowHighlights;
    [SerializeField] TMP_InputField seedField;
    [SerializeField] SegmentedControl sizeMode;
    [SerializeField] GameObject sizeBox;
    [SerializeField] TMP_InputField widthField;
    [SerializeField] TMP_InputField heightField;
    [SerializeField] SegmentedControl shape;
    [SerializeField] Slider density;
    [SerializeField] TMP_Text densityValue;
    [SerializeField] SegmentedControl symmetry;
    [SerializeField] Slider noiseStrength;
    [SerializeField] TMP_Text noiseStrengthValue;

    DuelSettings settings = new DuelSettings();
    Coroutine[] highlights;
    bool isHost;
    // 전투 준비가 시작되면 방장도 설정을 바꿀 수 없다.
    bool locked;
    bool seedInputChanged;

    // 입력 칸에서 글자를 넣고 있는지.
    public bool IsTyping => seedField.isFocused || widthField.isFocused || heightField.isFocused;
    bool Editable => isHost && !locked;

    void Awake()
    {
        highlights = new Coroutine[rowHighlights.Length];
        seedField.textComponent.fontSizeMax = seedField.textComponent.fontSize;
        seedField.textComponent.fontSizeMin = 9f;
        seedField.textComponent.enableAutoSizing = true;
        seedField.onSelect.AddListener(_ => seedInputChanged = false);
        seedField.onValueChanged.AddListener(_ => seedInputChanged = true);
        seedField.onEndEdit.AddListener(_ => EditSeed());
        sizeMode.Changed += _ => Edit();
        widthField.onEndEdit.AddListener(_ => Edit());
        heightField.onEndEdit.AddListener(_ => Edit());
        shape.Changed += _ => Edit();
        density.onValueChanged.AddListener(_ => Edit());
        symmetry.Changed += _ => Edit();
        noiseStrength.onValueChanged.AddListener(_ => Edit());
    }

    // 대기실을 열 때 설정을 받아 보여 준다. 참가자에게는 컨트롤을 잠근다.
    public void Open(bool isHost, DuelSettings settings)
    {
        this.isHost = isHost;
        this.settings = settings.Clone();
        seedInputChanged = false;
        foreach (Graphic highlight in rowHighlights) SetAlpha(highlight, 0f);
        lockNote.SetActive(!isHost);
        SetLocked(false);
        ShowSettings();
    }

    // 전투 준비가 시작되면 방장 컨트롤도 참가자처럼 흐리게 잠근다.
    public void SetLocked(bool locked)
    {
        this.locked = locked;
        foreach (CanvasGroup control in hostControls)
        {
            control.interactable = control.blocksRaycasts = Editable;
            control.alpha = Editable ? 1f : 0.5f;
        }
    }

    // 방장이 바꾼 설정을 참가자 화면에 반영하고, 바뀐 줄을 잠깐 밝힌다.
    public void Apply(DuelSettings changed)
    {
        MapGenerationSettings before = settings.Map, after = changed.Map;
        bool[] rows =
        {
            before.Seed != after.Seed,
            before.Size != after.Size,
            before.Shape != after.Shape,
            !Mathf.Approximately(before.ObstacleDensity, after.ObstacleDensity),
            before.Symmetry != after.Symmetry,
            !Mathf.Approximately(before.NoiseStrength, after.NoiseStrength)
        };
        settings = changed.Clone();
        ShowSettings();
        for (int row = 0; row < rows.Length; row++)
        {
            if (!rows[row]) continue;
            if (highlights[row] != null) StopCoroutine(highlights[row]);
            highlights[row] = StartCoroutine(Highlight(rowHighlights[row]));
        }
    }

    // 시드 줄의 "새로" 버튼이 호출한다.
    public void RollSeed()
    {
        if (!Editable) return;
        settings.Map.Seed = Random.Range(1000, 100000);
        seedInputChanged = false;
        seedField.SetTextWithoutNotify(settings.Map.Seed.ToString());
        ShowSettings();
        Changed?.Invoke(settings);
    }

    void EditSeed()
    {
        bool changed = seedInputChanged;
        seedInputChanged = false;
        if (Editable && changed && !seedField.wasCanceled &&
            int.TryParse(seedField.text, out int seed) && seed != settings.Map.Seed)
        {
            settings.Map.Seed = seed;
            seedField.SetTextWithoutNotify(seed.ToString());
            Changed?.Invoke(settings);
        }
        else seedField.SetTextWithoutNotify(settings.Map.Seed.ToString());
    }

    // 컨트롤의 값을 읽어 설정에 담고 알린다. 범위를 벗어난 입력은 고쳐서 다시 보여 준다.
    void Edit()
    {
        if (!Editable) return;
        MapGenerationSettings map = settings.Map;
        Vector2Int size = map.Size ?? new Vector2Int(31, 31);
        if (int.TryParse(widthField.text, out int width)) size.x = Mathf.Clamp(width, DuelSettings.MinSize, DuelSettings.MaxSize);
        if (int.TryParse(heightField.text, out int height)) size.y = Mathf.Clamp(height, DuelSettings.MinSize, DuelSettings.MaxSize);
        map.Size = sizeMode.Value == 1 ? size : (Vector2Int?)null;
        map.Shape = (MapShape)shape.Value;
        map.ObstacleDensity = density.value / DensitySteps;
        map.Symmetry = (MapSymmetry)symmetry.Value;
        map.NoiseStrength = noiseStrength.value / NoiseSteps;
        ShowSettings();
        Changed?.Invoke(settings);
    }

    void ShowSettings()
    {
        MapGenerationSettings map = settings.Map;
        if (!seedField.isFocused) seedField.SetTextWithoutNotify(map.Seed.ToString());
        sizeMode.SetValueWithoutNotify(map.Size.HasValue ? 1 : 0);
        sizeBox.SetActive(map.Size.HasValue);
        // 자동일 때도 마지막으로 넣은 크기를 칸에 남겨 둔다.
        if (map.Size.HasValue)
        {
            if (!widthField.isFocused) widthField.SetTextWithoutNotify(map.Size.Value.x.ToString());
            if (!heightField.isFocused) heightField.SetTextWithoutNotify(map.Size.Value.y.ToString());
        }
        shape.SetValueWithoutNotify((int)map.Shape);
        density.SetValueWithoutNotify(Mathf.Round(map.ObstacleDensity * DensitySteps));
        densityValue.text = map.ObstacleDensity.ToString("0.00");
        symmetry.SetValueWithoutNotify((int)map.Symmetry);
        noiseStrength.SetValueWithoutNotify(Mathf.Round(map.NoiseStrength * NoiseSteps));
        noiseStrengthValue.text = map.NoiseStrength.ToString("0.00");
    }

    // 줄 배경을 잠깐 밝혔다가 서서히 끈다.
    IEnumerator Highlight(Graphic row)
    {
        const float hold = 0.55f, fade = 1.05f, peak = 0.08f;
        for (float t = 0f; t < hold + fade; t += Time.unscaledDeltaTime)
        {
            SetAlpha(row, peak * Mathf.Clamp01((hold + fade - t) / fade));
            yield return null;
        }
        SetAlpha(row, 0f);
    }

    static void SetAlpha(Graphic graphic, float alpha)
    {
        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }
}
