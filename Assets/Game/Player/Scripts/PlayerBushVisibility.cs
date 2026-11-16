using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 전투 플레이어를 부시 규칙(BushVisibilityRules)에 따라 그리거나 숨기고, 내 화면에서 비쳐 보일 부시를 정한다(이슈 #93).
// 각 클라이언트가 지금 싸우는 맵(BushField.Active)과 두 플레이어 위치로 직접 계산하므로 네트워크 변수가 없다.
// 판정은 이 클라이언트의 내 캐릭터(IsLocal) 인스턴스가 매 LateUpdate에 하고, 다른 플레이어의 모습과 비쳐 보이는 부시도 함께 정한다.
// 부시 안에서 보이는 캐릭터는 불투명 URP Lit 머티리얼을 반투명본(WizardDuel/CharacterFaded)으로 바꿔 CharacterAlpha로 그린다.
// 반투명본은 캐릭터와 무기를 한 덩어리로 섞으므로(CharacterFadeFeature) 파츠끼리 비치지 않는다. 들어간 부시는 반투명 갈대가 되어 캐릭터 앞에 겹친다.
// 숨길 때는 렌더러의 forceRenderingOff와 체력바 Canvas만 끈다. 콜라이더와 이동 동기화(DuelMoveSync)는 그대로 둔다.
[DisallowMultipleComponent]
public sealed class PlayerBushVisibility : MonoBehaviour
{
    [Serializable]
    public struct FadedMaterial
    {
        public Material source;
        public Material faded;
    }

    public const string FadedShaderName = "WizardDuel/CharacterFaded";

    [Tooltip("머리 위 체력바·쿨다운 바 Canvas. 숨을 때 함께 끈다. 비우면 자식에서 찾는다.")]
    [SerializeField] Canvas healthBar;
    [Tooltip("부시 안에서 바꿔 끼울 반투명 머티리얼 짝. Tools > Wizard Duel > Rebuild Faded Player Materials가 채운다. " +
             "목록에 없는 불투명 URP Lit 머티리얼은 실행 중에 복제한다.")]
    [SerializeField] FadedMaterial[] fadedMaterials = new FadedMaterial[0];

    static readonly List<PlayerBushVisibility> players = new List<PlayerBushVisibility>();
    static Shader fadedShader;

    // 지금 반투명으로 그리는 플레이어 수. 0이면 CharacterFadeFeature가 깊이 패스를 넣지 않는다.
    public static int FadedCount { get; private set; }

    readonly BushReveal reveal = new BushReveal();
    // 모습을 바꾼 렌더러. 원래대로 돌릴 때까지 들고 있는다.
    readonly List<Renderer> renderers = new List<Renderer>();
    readonly List<Renderer> found = new List<Renderer>();
    readonly List<Material> slots = new List<Material>();
    readonly Dictionary<Material, Material> toFaded = new Dictionary<Material, Material>();
    readonly Dictionary<Material, Material> toSource = new Dictionary<Material, Material>();
    // 짝이 없어 실행 중에 만든 반투명본. 이 플레이어가 사라질 때 지운다.
    readonly List<Material> runtimeFaded = new List<Material>();
    // 내 캐릭터 인스턴스만 쓴다. 내 화면에서 반투명으로 바꿔 둔 부시.
    readonly List<int> fadedBushes = new List<int>();
    readonly List<int> wantedBushes = new List<int>();
    // 내 캐릭터 인스턴스가 모습을 정해 준 다른 플레이어. 내 캐릭터가 사라지면 이들을 원래대로 돌린다.
    readonly List<PlayerBushVisibility> driven = new List<PlayerBushVisibility>();
    BushField fadedField;
    PlayerAttack attack;
    bool initialized;
    bool isLocal;
    BushLook look;

    // 이 클라이언트의 내 캐릭터인지. DuelPlayer가 스폰·디스폰할 때 넣는다.
    // 내 캐릭터가 아니게 되면 이 화면에서 바꿔 둔 다른 플레이어와 부시를 원래대로 돌린다.
    public bool IsLocal
    {
        get => isLocal;
        set
        {
            if (isLocal == value) return;
            isLocal = value;
            if (value) return;
            RestoreScene();
            Restore();
        }
    }

    public BushLook Look
    {
        get => look;
        private set
        {
            if (look == value) return;
            if (look == BushLook.InBush) FadedCount--;
            if (value == BushLook.InBush) FadedCount++;
            look = value;
        }
    }

    public bool IsRevealed => reveal.IsRevealed(Time.time);

    // 상대 클라이언트가 이 플레이어의 공격 시작(DuelPlayer.PlayAttackRpc)을 받으면 부른다. 다시 부르면 처음부터 센다.
    public void Reveal() => reveal.Reveal(Time.time);

    void Awake() => Initialize();

    void OnEnable()
    {
        players.Add(this);
        Initialize();
        if (attack != null) attack.EquippedWeaponChanged += OnEquippedWeaponChanged;
    }

    void OnDisable()
    {
        players.Remove(this);
        if (attack != null) attack.EquippedWeaponChanged -= OnEquippedWeaponChanged;
        if (isLocal) RestoreScene();
        Restore();
        reveal.Clear();
    }

    void OnDestroy()
    {
        foreach (Material material in runtimeFaded)
        {
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        runtimeFaded.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPlayers()
    {
        players.Clear();
        FadedCount = 0;
    }

    void LateUpdate()
    {
        if (isLocal) UpdateVisibility(BushField.Active, players);
    }

    // 내 화면에서 나와 다른 플레이어들의 모습, 비쳐 보이는 부시를 정한다. 내 캐릭터 인스턴스에서 부른다.
    // others에 나 자신이 섞여 있어도 건너뛴다. field가 없으면(전투 맵이 아직 없으면) 모두 원래대로 그린다.
    public void UpdateVisibility(BushField field, IReadOnlyList<PlayerBushVisibility> others)
    {
        Initialize();
        driven.RemoveAll(player => player == null);
        int mine = BushOf(field, this);
        wantedBushes.Clear();
        for (int i = 0; i < others.Count; i++)
        {
            PlayerBushVisibility other = others[i];
            if (other == null || other == this) continue;
            BushView view = BushVisibilityRules.Evaluate(mine, BushOf(field, other), other.IsRevealed);
            other.Apply(view.Opponent);
            if (!driven.Contains(other)) driven.Add(other);
            if (view.OpponentFadedBush >= 0 && !wantedBushes.Contains(view.OpponentFadedBush))
                wantedBushes.Add(view.OpponentFadedBush);
        }
        BushView own = BushVisibilityRules.Evaluate(mine, -1, false);
        Apply(own.Me);
        if (own.MyFadedBush >= 0 && !wantedBushes.Contains(own.MyFadedBush)) wantedBushes.Add(own.MyFadedBush);
        FadeBushes(field);
    }

    static int BushOf(BushField field, PlayerBushVisibility player) =>
        field != null ? field.BushAt(player.transform.position) : -1;

    void Apply(BushLook wanted)
    {
        Initialize();
        if (wanted == BushLook.Normal)
        {
            if (Look != BushLook.Normal) Restore();
            return;
        }
        // 부시 안에 있는 동안 새로 붙은 렌더러(바꾼 무기, 상태이상 이펙트)도 같은 모습이 되도록 매번 다시 모은다.
        GetComponentsInChildren(true, found);
        foreach (Renderer renderer in found)
            if (!renderers.Contains(renderer)) renderers.Add(renderer);
        bool hidden = wanted == BushLook.Hidden;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.forceRenderingOff = hidden;
            // 피격 깜빡임(PlayerHitEffect)이 머티리얼을 바꿔도 다음 LateUpdate에 그 반투명본으로 다시 바뀐다.
            SwapMaterials(renderer, !hidden);
        }
        if (healthBar != null) healthBar.enabled = !hidden;
        Look = wanted;
    }

    // 바꿔 둔 렌더러와 체력바를 원래대로 돌린다.
    void Restore()
    {
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.forceRenderingOff = false;
            SwapMaterials(renderer, false);
        }
        renderers.Clear();
        if (healthBar != null) healthBar.enabled = true;
        Look = BushLook.Normal;
    }

    // 내 캐릭터가 사라지거나 내 캐릭터가 아니게 될 때, 이 화면에서 바꿔 둔 것을 모두 되돌린다.
    void RestoreScene()
    {
        foreach (PlayerBushVisibility player in driven)
            if (player != null) player.Restore();
        driven.Clear();
        wantedBushes.Clear();
        FadeBushes(fadedField);
        fadedField = null;
    }

    void FadeBushes(BushField field)
    {
        // 맵이 바뀌면 이전 맵에서 바꿔 둔 부시를 되돌리고 새 맵 기준으로 다시 센다.
        if (fadedField != field)
        {
            if (fadedField != null)
                foreach (int id in fadedBushes) fadedField.SetFaded(id, false);
            fadedBushes.Clear();
            fadedField = field;
        }
        if (field == null) return;
        for (int i = fadedBushes.Count - 1; i >= 0; i--)
        {
            if (wantedBushes.Contains(fadedBushes[i])) continue;
            field.SetFaded(fadedBushes[i], false);
            fadedBushes.RemoveAt(i);
        }
        foreach (int id in wantedBushes)
        {
            if (fadedBushes.Contains(id)) continue;
            field.SetFaded(id, true);
            fadedBushes.Add(id);
        }
    }

    // 무기를 바꾸면 빠진 무기의 렌더러가 숨기거나 바꾼 채로 남지 않게 모두 돌려 둔 뒤, 새 무기까지 같은 모습으로 바로 다시 적용한다.
    // 다음 LateUpdate를 기다리면 그 사이 숨은 상대가 한 프레임 보일 수 있다.
    void OnEquippedWeaponChanged()
    {
        BushLook current = Look;
        Restore();
        Apply(current);
    }

    void SwapMaterials(Renderer renderer, bool faded)
    {
        renderer.GetSharedMaterials(slots);
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            Material want = faded ? FadedOf(slots[i]) : SourceOf(slots[i]);
            if (want == slots[i]) continue;
            slots[i] = want;
            changed = true;
        }
        if (changed) renderer.SetSharedMaterials(slots);
    }

    Material SourceOf(Material material) =>
        material != null && toSource.TryGetValue(material, out Material source) ? source : material;

    Material FadedOf(Material material)
    {
        if (material == null) return null;
        if (toFaded.TryGetValue(material, out Material faded)) return faded;
        if (toSource.ContainsKey(material) || !CanFade(material)) return material;
        if (fadedShader == null) fadedShader = Shader.Find(FadedShaderName);
        if (fadedShader == null)
        {
            // 셰이더가 빌드에 없으면 바꾸지 않고 불투명하게 그린다. 같은 경고를 되풀이하지 않게 자기 자신과 짝지어 둔다.
            Debug.LogWarning($"PlayerBushVisibility: shader '{FadedShaderName}' is missing; '{material.name}' stays opaque in bushes.", this);
            toFaded[material] = material;
            return material;
        }
        faded = CreateFadedCopy(material, fadedShader);
        runtimeFaded.Add(faded);
        Map(material, faded);
        Debug.LogWarning($"PlayerBushVisibility: '{material.name}' has no faded pair on {name}; using a runtime copy. " +
            "Run Tools > Wizard Duel > Rebuild Faded Player Materials so builds keep the shader variant.", this);
        return faded;
    }

    void Map(Material source, Material faded)
    {
        toFaded[source] = faded;
        toSource[faded] = source;
    }

    void Initialize()
    {
        if (initialized) return;
        initialized = true;
        attack = GetComponent<PlayerAttack>();
        if (healthBar == null) healthBar = GetComponentInChildren<Canvas>(true);
        if (fadedMaterials == null) return;
        foreach (FadedMaterial pair in fadedMaterials)
            if (pair.source != null && pair.faded != null) Map(pair.source, pair.faded);
    }

    // 반투명본으로 바꿀 수 있는 머티리얼인지. 불투명 URP Lit 계열만 바꾸고, 파티클처럼 이미 투명한 것은 그대로 둔다.
    public static bool CanFade(Material material) =>
        material != null && material.HasProperty("_Surface") && material.HasProperty("_BaseColor") &&
        material.GetFloat("_Surface") == 0f;

    // 불투명 URP Lit 머티리얼을 캐릭터 반투명본으로 복제한다. 텍스처, 값, 키워드, 면 그리기는 그대로 두고 알파만 CharacterAlpha로 둔다.
    // 에디터 도구(반투명 머티리얼 에셋 생성)와 실행 중 대체 복제가 같이 쓴다.
    public static Material CreateFadedCopy(Material source, Shader shader)
    {
        var faded = new Material(source) { name = source.name + "_Faded", shader = shader };
        // 셰이더를 바꿔도 같은 이름의 키워드는 남지만, 새 셰이더에 없는 키워드는 지워 에셋에 군더더기가 남지 않게 한다.
        foreach (string keyword in source.shaderKeywords)
            if (!Array.Exists(shader.keywordSpace.keywordNames, name => name == keyword)) faded.DisableKeyword(keyword);
        faded.SetFloat("_Surface", 1f);
        faded.SetOverrideTag("RenderType", "Transparent");
        faded.renderQueue = (int)RenderQueue.Transparent - 1;
        // 반투명본은 라이트맵에 굽지 않으므로 양면 GI도 끈다.
        faded.doubleSidedGI = false;
        Color color = faded.GetColor("_BaseColor");
        color.a = BushVisibilityRules.CharacterAlpha;
        faded.SetColor("_BaseColor", color);
        return faded;
    }
}
