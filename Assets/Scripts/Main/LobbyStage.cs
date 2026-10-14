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

    [Header("상대가 들어올 때의 이펙트")]
    [SerializeField] float effectDuration = 1.35f;
    [SerializeField] float effectRadius = 1.35f;
    [SerializeField] Color blueEffectColor = new Color(0.08f, 1.4f, 2.2f, 1f);
    [SerializeField] Color redEffectColor = new Color(2.2f, 0.22f, 0.035f, 1f);

    // 시드를 고정하지 않은 동안 미리보기에 쓰는 시드. 대기실에 들어올 때마다 새로 뽑는다.
    int previewSeed;
    MapGenerationSettings pending;
    float regenerateAt;

    public void Show(DuelSettings settings, bool opponentPresent)
    {
        previewSeed = UnityEngine.Random.Range(1, int.MaxValue);
        wizards[1].gameObject.SetActive(opponentPresent);
        pending = null;
        if (!backdrop.ShowPreview(settings.ToMapSettings(previewSeed))) PreviewFailed?.Invoke();
    }

    // 바뀐 설정으로 미리보기를 다시 만든다. 슬라이더를 끄는 동안 매번 만들지 않도록 잠깐 모았다가 한 번만 만든다.
    public void Preview(DuelSettings settings)
    {
        pending = settings.ToMapSettings(previewSeed);
        regenerateAt = Time.unscaledTime + regenerateDelay;
    }

    public void SetOpponentPresent(bool present)
    {
        Transform[] spawns = backdrop.SpawnPoints;
        if (present && !wizards[1].gameObject.activeSelf && spawns != null && spawns[1] != null)
            SpawnEffectController.Play(spawns[1].position, 1, effectDuration, effectRadius,
                0.06f, 240f, 2.1f, 36, blueEffectColor, redEffectColor);
        wizards[1].gameObject.SetActive(present);
    }

    void OnDisable()
    {
        pending = null;
        SpawnEffectController.Stop(1);
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
