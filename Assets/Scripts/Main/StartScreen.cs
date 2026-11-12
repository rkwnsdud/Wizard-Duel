using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 시작 화면의 메뉴와 맵 코드 입력을 맡는다. 버튼은 Inspector의 OnClick에서 아래 public 메서드를 부른다.
// 실제 접속은 이벤트를 받는 쪽에서 처리하고, 실패하면 ShowJoinError로 알려 준다.
public class StartScreen : MonoBehaviour
{
    public const int CodeLength = 6;
    const string JoinHint = "숫자 6자리를 입력하고 Enter를 눌러요";

    public event Action CreateRequested;
    public event Action AiRequested;
    public event Action<string> JoinRequested;
    public event Action JoinCanceled;

    [Tooltip("아무것도 고르지 않은 채 방향키를 누르면 여기부터 고른다.")]
    [SerializeField] Button firstMenuButton;
    [SerializeField] Button joinMenuButton;
    [Tooltip("맵 코드를 입력하는 모달. 화면 전체를 덮어 뒤의 메뉴를 가린다.")]
    [SerializeField] GameObject joinBox;
    [Tooltip("모달이 열려 있는 동안 방향키로 뒤의 메뉴가 골라지지 않게 잠근다.")]
    [SerializeField] CanvasGroup menu;
    [SerializeField] TMP_InputField codeField;
    [SerializeField] TMP_Text joinButtonLabel;
    [SerializeField] TMP_Text joinMessage;
    [SerializeField] GameObject notice;
    [SerializeField] TMP_Text noticeText;

    bool connecting;
    bool creating;

    // 대기실에서 돌아오면 메뉴를 처음 상태로 되돌린다.
    // 버튼을 미리 골라 두지 않는다. 마우스를 올리거나 방향키를 누를 때 비로소 고른다.
    void OnEnable()
    {
        ShowJoinBox(false);
        SetConnecting(false);
        codeField.SetTextWithoutNotify("");
        EventSystem events = EventSystem.current;
        if (events != null) events.SetSelectedGameObject(null);
    }

    // 게임을 처음 켰을 때도 EventSystem이 첫 버튼을 골라 두지 않게 한다. 입력 모듈은 첫 Update에서 고르므로 그 전에 비운다.
    void Start()
    {
        EventSystem events = EventSystem.current;
        if (events == null) return;
        events.firstSelectedGameObject = null;
        events.SetSelectedGameObject(null);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Back();
    }

    // 고른 버튼이 없을 때 방향키를 누르면 첫 메뉴부터 고른다. 이번 프레임의 이동 입력이 한 칸 더 옮기지 않도록
    // EventSystem이 입력을 다 처리한 뒤(LateUpdate)에 고른다.
    void LateUpdate()
    {
        EventSystem events = EventSystem.current;
        if (events == null || joinBox.activeSelf || firstMenuButton == null) return;
        GameObject selected = events.currentSelectedGameObject;
        if (selected != null && selected.activeInHierarchy) return;
        if (NavigationPressed()) firstMenuButton.Select();
    }

    static bool NavigationPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
            keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
            keyboard.wKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame))
            return true;
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && (gamepad.dpad.up.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame ||
            gamepad.leftStick.up.wasPressedThisFrame || gamepad.leftStick.down.wasPressedThisFrame);
    }

    public void CreateMap()
    {
        if (!connecting) CreateRequested?.Invoke();
    }

    // "AI 대전" 버튼이 부른다. 서버에 접속하지 않고 AI 로비 화면을 연다.
    // 맵 생성의 답을 기다리는 중이면 그 접속을 그만두고 넘어간다. 서버가 없으면 답이 한참 뒤에야 오기 때문이다.
    public void StartAiDuel()
    {
        AbandonConnection();
        AiRequested?.Invoke();
    }

    // "코드로 참가" 버튼이 부른다. 메뉴 위에 코드 입력 모달을 띄운다.
    public void OpenJoin()
    {
        if (creating) AbandonConnection();
        if (!connecting) joinMessage.text = JoinHint;
        ShowJoinBox(true);
        codeField.ActivateInputField();
    }

    // 모달의 닫기 버튼과 바깥 어두운 부분이 부른다. 접속 중이었다면 그 접속도 그만둔다.
    public void CloseJoin()
    {
        AbandonConnection();
        ShowJoinBox(false);
        EventSystem events = EventSystem.current;
        if (events != null) events.SetSelectedGameObject(null);
    }

    // 입력 칸에서 Enter를 누르면 호출된다.
    public void Join()
    {
        if (connecting) return;
        string code = codeField.text;
        if (code.Length != CodeLength)
        {
            ShowJoinError($"코드 {CodeLength}자리를 모두 입력해 주세요");
            return;
        }
        SetConnecting(true);
        joinMessage.text = $"{code} 맵에 접속하는 중...";
        JoinRequested?.Invoke(code);
    }

    // 입력 칸 옆 버튼이 호출한다. 접속 중에는 취소 버튼이 된다.
    public void JoinOrCancel()
    {
        if (connecting) CancelJoin();
        else Join();
    }

    public void ShowJoinError(string reason)
    {
        creating = false;
        SetConnecting(false);
        ShowJoinBox(true);
        joinMessage.text = $"<color=#FF9D9D>{reason}</color>";
        codeField.ActivateInputField();
    }

    public void CancelJoin()
    {
        if (!connecting) return;
        bool wasCreating = creating;
        AbandonConnection();
        if (!wasCreating) codeField.ActivateInputField();
    }

    // 맵 생성이나 참가의 답을 기다리던 접속을 그만두고 메뉴를 다시 쓸 수 있게 한다.
    void AbandonConnection()
    {
        if (!connecting) return;
        bool wasCreating = creating;
        creating = false;
        SetConnecting(false);
        if (wasCreating) notice.SetActive(false);
        else joinMessage.text = JoinHint;
        JoinCanceled?.Invoke();
    }

    // 연결 끊김이나 방 닫힘처럼 시작 화면으로 돌아온 사유를 메뉴 위에 띄운다.
    public void ShowNotice(string reason)
    {
        noticeText.text = reason;
        notice.SetActive(true);
    }

    public void BeginCreate()
    {
        creating = true;
        SetConnecting(true);
        ShowNotice("대기실을 여는 중이에요. Esc로 취소할 수 있어요.");
    }

    public void FinishConnection()
    {
        creating = false;
        SetConnecting(false);
        notice.SetActive(false);
    }

    public void ShowCreateError(string reason)
    {
        FinishConnection();
        ShowNotice(reason);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Esc는 접속 취소, 모달 닫기, 알림 닫기 순서로 한 단계씩 물러난다.
    void Back()
    {
        if (connecting) CancelJoin();
        else if (joinBox.activeSelf)
        {
            ShowJoinBox(false);
            joinMenuButton.Select();
        }
        else notice.SetActive(false);
    }

    void ShowJoinBox(bool open)
    {
        joinBox.SetActive(open);
        if (menu != null) menu.interactable = !open;
    }

    void SetConnecting(bool value)
    {
        connecting = value;
        // 비활성화하면 선택이 풀려 키보드 조작이 끊기므로 읽기 전용으로만 막는다.
        codeField.readOnly = value;
        joinButtonLabel.text = value ? "취소" : "참가";
    }
}
