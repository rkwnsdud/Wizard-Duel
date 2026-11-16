using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 대기실 화면. 맵 코드, 방장·참가자 명패, 게임 시작을 보여 주고, 맵 설정은 MapSettingsPanel에 맡긴다.
// 버튼은 Inspector의 OnClick에서 아래 public 메서드를 부른다.
// 실제 접속과 동기화는 이벤트를 받는 쪽에서 처리하고, 상대 쪽 변화는 SetOpponentPresent와 ApplySettings로 알려 준다.
// AI 대전은 OpenAi로 연다. 상대가 AI라는 점만 다르고, 설정 패널에 "게임 설정" 탭(GameSettingsPanel)이 더해진다.
public class LobbyScreen : MonoBehaviour
{
    const string AiMapNote = "뒤의 맵은 지금 설정으로 만든 미리보기예요. 게임을 시작하면 이 설정 그대로 전투 맵을 만들어요.";

    static readonly Color Gold = new Color32(0xFF, 0xD8, 0x66, 0xFF);
    static readonly Color GoldBright = new Color32(0xFF, 0xF0, 0xB4, 0xFF);
    static readonly Color GoldInk = new Color32(0x2A, 0x1F, 0x05, 0xFF);
    static readonly Color Idle = new Color32(0x37, 0x3C, 0x48, 0xFF);
    static readonly Color IdleInk = new Color32(0x95, 0x9C, 0xAB, 0xFF);

    public event Action<DuelSettings> SettingsChanged;
    // AI 로비 화면의 "게임 설정" 탭에서 무기나 난이도를 바꿨다.
    public event Action<AiDuelOptions> GameSettingsChanged;
    public event Action StartRequested;
    public event Action LeaveRequested;

    [Header("맵 코드")]
    [SerializeField] GameObject codePlate;
    [SerializeField] TMP_Text codeLabel;
    [SerializeField] GameObject codeHint;

    [Header("명패")]
    [SerializeField] GameObject blueMe;
    [SerializeField] GameObject redMe;
    [SerializeField] GameObject redJoined;
    [SerializeField] GameObject redWaiting;
    [Tooltip("빨강 명패의 역할 글자. AI 대전에서는 AI로 바꾼다.")]
    [SerializeField] TMP_Text redRole;

    [Header("설정")]
    [SerializeField] MapSettingsPanel mapSettings;
    [SerializeField] GameSettingsPanel gameSettings;
    [Tooltip("일반 로비 화면에서 보이는 패널 제목. AI 대전에서는 탭이 이 자리를 대신한다.")]
    [SerializeField] GameObject settingsTitle;
    [Tooltip("맵 설정 | 게임 설정. AI 대전에서만 보인다.")]
    [SerializeField] SegmentedControl tabs;
    [SerializeField] GameObject[] mapPage;
    [SerializeField] GameObject[] gamePage;
    [SerializeField] TMP_Text mapNote;

    [Header("시작")]
    [SerializeField] TMP_Text status;
    [SerializeField] Button startButton;
    [SerializeField] TMP_Text startLabel;
    [SerializeField] GameObject waitPlaque;
    [SerializeField] Toast toast;

    string code;
    // 상대가 AI다. 서버가 없으니 맵 코드가 없고, 나는 늘 방장이며 상대는 늘 들어와 있다.
    bool ai;
    string redRoleText;
    string mapNoteText;
    bool isHost;
    bool opponentPresent;
    // 시작을 요청했거나 서버가 전투 준비를 알렸다. 이때는 시작 버튼과 설정을 잠근다.
    bool starting;
    // 지난 프레임에 입력 칸에서 글자를 넣고 있었는지. Esc로 입력을 끝낸 프레임에 대기실까지 나가지 않게 한다.
    bool typing;
    // 버튼 자체는 레이아웃이 자리를 잡으므로, 흔들 때는 그 안의 그림만 움직인다.
    Transform startFace;
    Vector3 startHome;
    Coroutine shake;

    void Awake()
    {
        startFace = startButton.targetGraphic.transform;
        startHome = startFace.localPosition;
        redRoleText = redRole.text;
        mapNoteText = mapNote.text;
        mapSettings.Changed += changed => SettingsChanged?.Invoke(changed);
        gameSettings.Changed += changed => GameSettingsChanged?.Invoke(changed);
        tabs.Changed += ShowTab;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !typing) Leave();
        typing = mapSettings.IsTyping;

        // 시작할 수 있을 때는 버튼이 천천히 밝아졌다 어두워진다.
        if (isHost && opponentPresent && startButton.interactable)
            startButton.targetGraphic.color = Color.Lerp(Gold, GoldBright, Mathf.Sin(Time.unscaledTime * 3.5f) * 0.5f + 0.5f);
    }

    public void Open(bool isHost, string code, DuelSettings settings, bool opponentPresent)
    {
        ai = false;
        SelectTab(0);
        this.isHost = isHost;
        this.code = code;
        this.opponentPresent = opponentPresent;
        starting = false;
        codeLabel.text = code.Insert(code.Length / 2, " ");
        startFace.localPosition = startHome;
        mapSettings.Open(isHost, settings);
        Refresh();
        EventSystem.current.SetSelectedGameObject(isHost ? startButton.gameObject : null);
    }

    // AI 대전의 로비 화면으로 연다. 전투를 마치고 다시 열 때는 보던 탭을 그대로 둔다.
    public void OpenAi(DuelSettings settings, AiDuelOptions options)
    {
        if (!ai) SelectTab(0);
        ai = true;
        isHost = true;
        code = "";
        opponentPresent = true;
        starting = false;
        startFace.localPosition = startHome;
        mapSettings.Open(true, settings);
        gameSettings.Open(options);
        Refresh();
        EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    public void SetOpponentPresent(bool present)
    {
        opponentPresent = present;
        Refresh();
    }

    // 시작 요청을 보냈거나 서버가 전투 준비를 알리면 켜고, 시작이 거절되면 끈다.
    public void SetStarting(bool value)
    {
        if (starting == value) return;
        starting = value;
        mapSettings.SetLocked(value);
        gameSettings.SetLocked(value);
        Refresh();
    }

    // 방장이 바꾼 설정을 참가자 화면에 반영한다.
    public void ApplySettings(DuelSettings changed) => mapSettings.Apply(changed);

    public void Leave() => LeaveRequested?.Invoke();

    public void CopyCode()
    {
        GUIUtility.systemCopyBuffer = code;
        toast.Show("맵 코드를 복사했어요");
    }

    public void RequestStart()
    {
        if (!isHost || starting) return;
        if (!opponentPresent)
        {
            if (shake != null) StopCoroutine(shake);
            shake = StartCoroutine(Shake());
            toast.Show("상대가 들어와야 시작할 수 있어요");
            return;
        }
        StartRequested?.Invoke();
    }

    // 방장인지, 상대가 들어왔는지에 따라 달라지는 부분을 맞춘다.
    void Refresh()
    {
        codePlate.SetActive(!ai);
        codeHint.SetActive(!ai && isHost && !opponentPresent);
        redRole.text = ai ? "AI" : redRoleText;
        settingsTitle.SetActive(!ai);
        tabs.gameObject.SetActive(ai);
        mapNote.text = ai ? AiMapNote : mapNoteText;
        blueMe.SetActive(isHost);
        redMe.SetActive(!isHost);
        redJoined.SetActive(opponentPresent);
        redWaiting.SetActive(!opponentPresent);
        startButton.gameObject.SetActive(isHost);
        waitPlaque.SetActive(!isHost);
        // 방장이 상대와 함께 로비 단계에 있을 때만 누를 수 있다. 서버도 같은 조건을 다시 확인한다.
        startButton.interactable = isHost && opponentPresent && !starting;
        startButton.targetGraphic.color = startButton.interactable ? Gold : Idle;
        startLabel.color = startButton.interactable ? GoldInk : IdleInk;
        status.text = starting ? (ai ? "전투 맵을 만들고 있어요 · 곧 시작해요" : "전투 맵을 맞추고 있어요 · 곧 시작해요")
            : ai ? "AI 상대와 겨뤄요 · 게임을 시작할 수 있어요"
            : !isHost ? "설정은 방장이 정해요. 바뀌면 여기에도 바로 보여요."
            : opponentPresent ? "모두 모였어요 · 2 / 2 · 게임을 시작할 수 있어요"
            : "상대가 들어오면 시작할 수 있어요 · 1 / 2";
    }

    void SelectTab(int index)
    {
        tabs.SetValueWithoutNotify(index);
        ShowTab(index);
    }

    // 0은 맵 설정, 1은 게임 설정.
    void ShowTab(int index)
    {
        foreach (GameObject item in mapPage) item.SetActive(index == 0);
        foreach (GameObject item in gamePage) item.SetActive(index == 1);
    }

    IEnumerator Shake()
    {
        const float time = 0.4f, distance = 6f;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            startFace.localPosition = startHome + Vector3.right * (Mathf.Sin(t / time * Mathf.PI * 4f) * distance);
            yield return null;
        }
        startFace.localPosition = startHome;
    }
}
