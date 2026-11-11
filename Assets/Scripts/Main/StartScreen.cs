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

    [SerializeField] Button joinMenuButton;
    [SerializeField] GameObject joinBox;
    [Tooltip("코드 입력 상자가 열려 있는 동안 숨길 메뉴 버튼. 함께 보이면 메뉴가 화면 아래로 넘친다.")]
    [SerializeField] GameObject aiMenuButton;
    [SerializeField] TMP_InputField codeField;
    [SerializeField] TMP_Text joinButtonLabel;
    [SerializeField] TMP_Text joinMessage;
    [SerializeField] GameObject notice;
    [SerializeField] TMP_Text noticeText;

    bool connecting;
    bool creating;

    // 대기실에서 돌아오면 메뉴를 처음 상태로 되돌린다.
    void OnEnable()
    {
        ShowJoinBox(false);
        SetConnecting(false);
        codeField.SetTextWithoutNotify("");
        // 씬을 처음 열 때는 EventSystem이 아직 없을 수 있고, 그때는 EventSystem이 알아서 첫 버튼을 고른다.
        EventSystem events = EventSystem.current;
        if (events != null) events.SetSelectedGameObject(events.firstSelectedGameObject);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Back();
    }

    public void CreateMap()
    {
        if (!connecting) CreateRequested?.Invoke();
    }

    // "AI 대전" 버튼이 부른다. 서버에 접속하지 않고 봇과 바로 한 판을 치른다.
    public void StartAiDuel()
    {
        if (!connecting) AiRequested?.Invoke();
    }

    public void OpenJoin()
    {
        if (creating) return;
        if (!connecting) joinMessage.text = JoinHint;
        ShowJoinBox(true);
        codeField.ActivateInputField();
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
        creating = false;
        SetConnecting(false);
        if (wasCreating)
        {
            notice.SetActive(false);
            JoinCanceled?.Invoke();
            return;
        }
        joinMessage.text = JoinHint;
        codeField.ActivateInputField();
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

    // Esc는 접속 취소, 입력 상자 닫기, 알림 닫기 순서로 한 단계씩 물러난다.
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

    // 코드 입력 상자를 열면 그 높이만큼 아래 메뉴가 밀린다. 화면을 넘지 않게 그동안 AI 대전 버튼을 숨긴다.
    void ShowJoinBox(bool open)
    {
        joinBox.SetActive(open);
        if (aiMenuButton != null) aiMenuButton.SetActive(!open);
    }

    void SetConnecting(bool value)
    {
        connecting = value;
        // 비활성화하면 선택이 풀려 키보드 조작이 끊기므로 읽기 전용으로만 막는다.
        codeField.readOnly = value;
        joinButtonLabel.text = value ? "취소" : "참가";
    }
}
