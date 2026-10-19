using System;
using UnityEngine;

// 대기실의 3D 쪽을 맡는다. 지금 설정의 미리보기 맵을 띄우고, 스폰 지점에 표시용 마법사를 세우고, 그 위에 명패를 붙인다.
// 마법사는 네트워크 플레이어가 아닌 모형이라, 전투 전에는 플레이어를 만들지 않는다는 조건과 부딪히지 않는다.
[DefaultExecutionOrder(100)] // 명패는 TitleBackdrop이 카메라를 옮긴 뒤에 놓아야 밀리지 않는다.
public class LobbyStage : MonoBehaviour
{
    public event Action PreviewFailed;

    [SerializeField] TitleBackdrop backdrop;
    [SerializeField] Camera viewCamera;
    [Tooltip("0은 파랑, 1은 빨강.")]
    [SerializeField] Transform[] wizards;
    [SerializeField] RectTransform[] plates;
    [Tooltip("명패가 뜨는 높이. 스폰 지점에서 위로 잰다.")]
    [SerializeField] float plateHeight = 3.3f;
    [Tooltip("설정을 연달아 바꿀 때 마지막 변경 뒤 이만큼 기다렸다가 맵을 다시 만든다.")]
    [SerializeField] float regenerateDelay = 0.25f;

    [Header("마법사가 나타날 때의 이펙트")]
    [SerializeField] float effectDuration = 1.35f;
    [SerializeField] float effectRadius = 1.35f;
    [SerializeField] Color blueEffectColor = new Color(0.08f, 1.4f, 2.2f, 1f);
    [SerializeField] Color redEffectColor = new Color(2.2f, 0.22f, 0.035f, 1f);

    MapGenerationSettings pending;
    float regenerateAt;
    // 들어올 때 배경 맵이 설정과 달라서 Reveal에서 새로 만들 맵. 그대로 쓰면 null이다.
    MapGenerationSettings revealMap;
    bool revealOpponent;

    // 배경 맵을 이어받아 카메라를 대기실 자리로 옮기기 시작한다. 마법사는 Reveal에서 세운다.
    public void Show(DuelSettings settings, bool opponentPresent)
    {
        MapGenerationSettings current = backdrop.Current;
        pending = null;
        revealOpponent = opponentPresent;
        foreach (Transform wizard in wizards) wizard.gameObject.SetActive(false);
        backdrop.HoldPreview();
        MapGenerationSettings wanted = settings.ToMapSettings();
        revealMap = SameMap(current, wanted) ? null : wanted;
    }

    // 대기실 UI가 올라오기 시작할 때 부른다. 이어받은 맵이면 마법사를 이펙트와 함께 세우고,
    // 새로 만들어야 하면 맵을 다시 솟게 해서 마법사도 물결을 타고 올라오게 한다.
    public void Reveal()
    {
        if (revealMap != null)
        {
            wizards[0].gameObject.SetActive(true);
            wizards[1].gameObject.SetActive(revealOpponent);
            if (!backdrop.ShowPreview(revealMap)) PreviewFailed?.Invoke();
            revealMap = null;
            return;
        }
        Appear(0);
        if (revealOpponent) Appear(1);
    }

    // 대기실을 나갈 때 부른다. 기다리던 재생성을 버리고, 들어오자마자 나가서 남아 있는 등장 이펙트도 끈다.
    // 마법사는 이펙트 없이 대기실 UI가 다 사라질 때 무대와 함께 꺼진다.
    public void Hide()
    {
        pending = null;
        revealMap = null;
        SpawnEffectController.Stop(0);
        SpawnEffectController.Stop(1);
    }

    // 전투 준비가 시작되면 부른다. 같은 자리에 전투 맵이 서므로 기다리던 미리보기 재생성을 버린다.
    // 모형 마법사는 실제 플레이어가 소환될 때 무대와 함께 꺼진다.
    public void StopPreview()
    {
        pending = null;
        revealMap = null;
    }

    // 바뀐 설정으로 미리보기를 다시 만든다. 슬라이더를 끄는 동안 매번 만들지 않도록 잠깐 모았다가 한 번만 만든다.
    public void Preview(DuelSettings settings)
    {
        pending = settings.ToMapSettings();
        if (SameMap(backdrop.Current, pending))
        {
            pending = null;
            return;
        }
        regenerateAt = Time.unscaledTime + regenerateDelay;
    }

    public void SetOpponentPresent(bool present)
    {
        if (present && !wizards[1].gameObject.activeSelf) Appear(1);
        else wizards[1].gameObject.SetActive(present);
    }

    static bool SameMap(MapGenerationSettings a, MapGenerationSettings b) =>
        a.Seed == b.Seed && a.Size == b.Size && a.Shape == b.Shape && a.ObstacleDensity == b.ObstacleDensity &&
        a.Symmetry == b.Symmetry && a.NoiseStrength == b.NoiseStrength && a.NoiseScale == b.NoiseScale;

    // 스폰 지점에 마법진 이펙트를 띄우며 마법사를 세운다.
    void Appear(int slot)
    {
        Transform[] spawns = backdrop.SpawnPoints;
        if (spawns != null && spawns[slot] != null)
            SpawnEffectController.Play(spawns[slot].position, slot, effectDuration, effectRadius,
                0.06f, 240f, 2.1f, 36, blueEffectColor, redEffectColor);
        wizards[slot].gameObject.SetActive(true);
    }

    void OnDisable()
    {
        pending = null;
    }

    void Update()
    {
        if (pending == null || Time.unscaledTime < regenerateAt) return;
        MapGenerationSettings settings = pending;
        pending = null;
        if (!backdrop.ShowPreview(settings)) PreviewFailed?.Invoke();
    }

    void LateUpdate()
    {
        Transform[] spawns = backdrop.SpawnPoints;
        if (spawns == null) return;
        for (int slot = 0; slot < wizards.Length; slot++)
        {
            // 맵을 다시 만드는 프레임에는 이전 스폰 지점이 이미 사라졌을 수 있다.
            if (spawns[slot] == null) continue;
            // 스폰 블록이 물결을 타고 올라오는 동안 마법사와 명패도 함께 올라온다.
            Vector3 position = spawns[slot].position;
            wizards[slot].SetPositionAndRotation(position, spawns[slot].rotation);
            // Overlay 캔버스에서는 화면 픽셀 좌표가 곧 위치다.
            Vector3 screen = viewCamera.WorldToScreenPoint(position + Vector3.up * plateHeight);
            plates[slot].position = new Vector3(screen.x, screen.y, 0f);
        }
    }
}
