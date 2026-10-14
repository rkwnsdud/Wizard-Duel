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
public class LobbyScreen : MonoBehaviour
{
    static readonly Color Gold = new Color32(0xFF, 0xD8, 0x66, 0xFF);
    static readonly Color GoldBright = new Color32(0xFF, 0xF0, 0xB4, 0xFF);
    static readonly Color GoldInk = new Color32(0x2A, 0x1F, 0x05, 0xFF);
    static readonly Color Idle = new Color32(0x37, 0x3C, 0x48, 0xFF);
    static readonly Color IdleInk = new Color32(0x95, 0x9C, 0xAB, 0xFF);

    public event Action<DuelSettings> SettingsChanged;
    public event Action StartRequested;
    public event Action LeaveRequested;

    [Header("맵 코드")]
    [SerializeField] TMP_Text codeLabel;
    [SerializeField] GameObject codeHint;

    [Header("명패")]
    [SerializeField] GameObject blueMe;
    [SerializeField] GameObject redMe;
    [SerializeField] GameObject redJoined;
    [SerializeField] GameObject redWaiting;

    [Header("설정")]
    [SerializeField] MapSettingsPanel mapSettings;

    [Header("시작")]
    [SerializeField] TMP_Text status;
    [SerializeField] Button startButton;
    [SerializeField] TMP_Text startLabel;
    [SerializeField] GameObject waitPlaque;
    [SerializeField] Toast toast;

    string code;
    bool isHost;
    bool opponentPresent;
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
        mapSettings.Changed += changed => SettingsChanged?.Invoke(changed);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !typing) Leave();
        typing = mapSettings.IsTyping;

        // 시작할 수 있을 때는 버튼이 천천히 밝아졌다 어두워진다.
        if (isHost && opponentPresent)
            startButton.targetGraphic.color = Color.Lerp(Gold, GoldBright, Mathf.Sin(Time.unscaledTime * 3.5f) * 0.5f + 0.5f);
    }

    public void Open(bool isHost, string code, DuelSettings settings, bool opponentPresent)
    {
        this.isHost = isHost;
        this.code = code;
        this.opponentPresent = opponentPresent;
        codeLabel.text = code.Insert(code.Length / 2, " ");
        startFace.localPosition = startHome;
        mapSettings.Open(isHost, settings);
        Refresh();
        EventSystem.current.SetSelectedGameObject(isHost ? startButton.gameObject : null);
    }

    public void SetOpponentPresent(bool present)
    {
        opponentPresent = present;
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
        if (!isHost) return;
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
        codeHint.SetActive(isHost && !opponentPresent);
        blueMe.SetActive(isHost);
        redMe.SetActive(!isHost);
        redJoined.SetActive(opponentPresent);
        redWaiting.SetActive(!opponentPresent);
        startButton.gameObject.SetActive(isHost);
        waitPlaque.SetActive(!isHost);
        startButton.targetGraphic.color = opponentPresent ? Gold : Idle;
        startLabel.color = opponentPresent ? GoldInk : IdleInk;
        status.text = !isHost ? "설정은 방장이 정해요. 바뀌면 여기에도 바로 보여요."
            : opponentPresent ? "모두 모였어요 · 2 / 2"
            : "상대가 들어오면 시작할 수 있어요 · 1 / 2";
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
