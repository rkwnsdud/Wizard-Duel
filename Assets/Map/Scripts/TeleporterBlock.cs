using System.Collections.Generic;
using UnityEngine;

// 텔레포트 타일 한 장(2×2칸). 모양은 일반 머티리얼과 하이라이트 머티리얼로 이뤄지고, 하이라이트 머티리얼을 쓰는 슬롯만
// 쌍 색과 상태(쓸 수 있음·쿨타임·발동)에 맞춰 빛을 바꾼다. 모델을 바꿔도 빛낼 부분에 같은 하이라이트 머티리얼만 쓰면 된다.
// 타일 칸에는 바닥 블록이 따로 깔리므로 이 블록에는 콜라이더가 없다. 상태는 TeleporterField가 들고 있다.
[DisallowMultipleComponent]
public sealed class TeleporterBlock : Block
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Tooltip("하이라이트 머티리얼. 자식 렌더러에서 이 머티리얼을 쓰는 슬롯만 빛을 바꾼다.")]
    [SerializeField] Material highlightMaterial;
    [Tooltip("쓸 수 있을 때 도는 안쪽 무늬. 비우면 돌리지 않는다.")]
    [SerializeField] Transform spinner;
    [Tooltip("쌍 번호 순서의 하이라이트 색(sRGB). 쌍이 더 많으면 처음부터 다시 쓴다.")]
    [SerializeField] Color[] pairColors =
    {
        new Color32(0x9d, 0x6b, 0xff, 0xff),
        new Color32(0x3e, 0xe0, 0x8f, 0xff),
        new Color32(0xff, 0x5c, 0xc6, 0xff)
    };
    [SerializeField] TeleporterGlowSettings glow = new TeleporterGlowSettings();

    readonly List<(Renderer renderer, int index)> slots = new List<(Renderer, int)>();
    MaterialPropertyBlock properties;
    TeleporterField field;
    bool slotsFound;

    public int Pair { get; private set; }
    public int Side { get; private set; }
    // 이 타일의 하이라이트 색(sRGB).
    public Color PairColor { get; private set; } = new Color32(0x9d, 0x6b, 0xff, 0xff);
    public TeleporterGlowSettings Glow => glow;
    // 마지막으로 칠한 빛 세기와 쓸 수 있는 정도. 테스트와 디버그용이다.
    public float Level { get; private set; }
    public float Ready { get; private set; }
    public int HighlightSlotCount { get { FindSlots(); return slots.Count; } }

    // 블록을 깐 뒤 BlockFactory가 부른다. 쌍 번호로 색을 고르고 지금 상태의 빛을 바로 칠한다.
    public void Bind(TeleporterField owner, int pair, int side)
    {
        field = owner;
        Pair = pair;
        Side = side;
        if (pairColors != null && pairColors.Length > 0) PairColor = pairColors[Mathf.Abs(pair) % pairColors.Length];
        Refresh(Time.time);
    }

    void Update()
    {
        Refresh(Time.time);
        if (spinner != null && Ready > 0f)
            spinner.Rotate(0f, -glow.spinSpeed * Ready * Time.deltaTime, 0f, Space.Self);
    }

    // now 시각의 상태로 하이라이트 슬롯을 칠한다.
    public void Refresh(float now)
    {
        float flashStart = float.NegativeInfinity, cooldownEnd = float.NegativeInfinity;
        field?.GetState(Pair, out flashStart, out cooldownEnd);
        TeleporterGlow.Evaluate(glow, now, flashStart, cooldownEnd, out float level, out float ready);
        Level = level;
        Ready = ready;
        Paint(level, ready);
    }

    void Paint(float level, float ready)
    {
        FindSlots();
        if (slots.Count == 0) return;
        properties ??= new MaterialPropertyBlock();
        // SetColor는 선형 색 공간에서 sRGB 값을 바꿔 넣으므로, 세기를 곱한 HDR 값은 미리 선형으로 바꿔 SetVector로 넣는다.
        Color linear = PairColor.linear;
        float tint = TeleporterGlow.Tint(glow, ready);
        var baseColor = new Vector4(linear.r * tint, linear.g * tint, linear.b * tint, 1f);
        var emission = new Vector4(linear.r * level, linear.g * level, linear.b * level, 1f);
        foreach ((Renderer renderer, int index) in slots)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(properties, index);
            properties.SetVector(BaseColorId, baseColor);
            properties.SetVector(EmissionColorId, emission);
            renderer.SetPropertyBlock(properties, index);
        }
    }

    void FindSlots()
    {
        if (slotsFound) return;
        slotsFound = true;
        slots.Clear();
        if (highlightMaterial == null)
        {
            Debug.LogWarning($"[Teleporter] {name} has no highlight material; the tile will not light up.", this);
            return;
        }
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == highlightMaterial) slots.Add((renderer, i));
        }
    }
}
