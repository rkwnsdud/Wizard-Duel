using UnityEngine;

// Main 씬에서 어느 화면을 보여 줄지 정한다. 지금은 시작 화면과 대기실만 오간다.
// 네트워크가 붙기 전이라 대기실은 내 화면에서만 열리고, 맵 코드도 여기서 임시로 만든다.
public class MainFlow : MonoBehaviour
{
    [SerializeField] StartScreen startScreen;
    [SerializeField] LobbyScreen lobby;
    [SerializeField] LobbyStage stage;
    [SerializeField] TitleBackdrop backdrop;
    [SerializeField] Toast toast;

    // 대기실을 나갔다가 다시 열어도 마지막으로 정한 설정을 이어서 쓴다.
    DuelSettings settings = new DuelSettings();

    void OnEnable()
    {
        startScreen.CreateRequested += OpenAsHost;
        lobby.SettingsChanged += OnSettingsChanged;
        lobby.LeaveRequested += Leave;
        stage.PreviewFailed += OnPreviewFailed;
    }

    void OnDisable()
    {
        startScreen.CreateRequested -= OpenAsHost;
        lobby.SettingsChanged -= OnSettingsChanged;
        lobby.LeaveRequested -= Leave;
        stage.PreviewFailed -= OnPreviewFailed;
    }

    void OpenAsHost() => OpenLobby(true, Random.Range(0, 1000000).ToString("D" + StartScreen.CodeLength), false);

    void OpenLobby(bool isHost, string code, bool opponentPresent)
    {
        startScreen.gameObject.SetActive(false);
        lobby.gameObject.SetActive(true);
        lobby.Open(isHost, code, settings, opponentPresent);
        stage.gameObject.SetActive(true);
        stage.Show(settings, opponentPresent);
    }

    void ShowTitle()
    {
        lobby.gameObject.SetActive(false);
        stage.gameObject.SetActive(false);
        startScreen.gameObject.SetActive(true);
        backdrop.ShowTitle();
    }

    void Leave()
    {
        ShowTitle();
        toast.Show("대기실에서 나왔어요");
    }

    void OnSettingsChanged(DuelSettings changed)
    {
        settings = changed.Clone();
        stage.Preview(settings);
    }

    void OnPreviewFailed() => toast.Show("이 설정으로는 맵을 만들 수 없어요");

#if UNITY_EDITOR
    // 네트워크가 붙기 전까지 참가자 화면과 상대 쪽 변화를 에디터에서 흉내 낸다.
    // 플레이 중에 이 컴포넌트의 메뉴(⋮)에서 고른다.
    [ContextMenu("참가자로 대기실 열기")]
    void DebugOpenAsGuest()
    {
        if (Application.isPlaying) OpenLobby(false, "123456", true);
    }

    [ContextMenu("상대 들어오기")]
    void DebugOpponentJoin()
    {
        if (!Application.isPlaying || !lobby.gameObject.activeSelf) return;
        lobby.SetOpponentPresent(true);
        stage.SetOpponentPresent(true);
        toast.Show("상대가 맵 코드로 들어왔어요");
    }

    [ContextMenu("방장이 설정 바꾸기")]
    void DebugHostChange()
    {
        if (!Application.isPlaying || !lobby.gameObject.activeSelf) return;
        settings.Map.Shape = settings.Map.Shape == MapShape.Donut ? MapShape.Random : settings.Map.Shape + 1;
        lobby.ApplySettings(settings);
        stage.Preview(settings);
        toast.Show("방장이 맵 형태를 바꿨어요");
    }

    [ContextMenu("상대 이탈")]
    void DebugOpponentLeave()
    {
        if (!Application.isPlaying || !lobby.gameObject.activeSelf) return;
        ShowTitle();
        startScreen.ShowNotice("상대가 나가서 대기실이 닫혔어요.");
    }
#endif
}
