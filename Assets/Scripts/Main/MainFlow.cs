using System.Collections;
using UnityEngine;

// Main 씬에서 어느 화면을 보여 줄지 정한다. 지금은 시작 화면과 대기실만 오간다.
// 네트워크가 붙기 전이라 대기실은 내 화면에서만 열리고, 맵 코드도 여기서 임시로 만든다.
// 두 화면 사이에서는 배경 맵을 그대로 두고 카메라만 옆으로 옮기며, 앞 화면이 내려가 사라진 뒤 다음 화면이 올라온다.
public class MainFlow : MonoBehaviour
{
    [SerializeField] StartScreen startScreen;
    [SerializeField] ScreenTransition startTransition;
    [SerializeField] LobbyScreen lobby;
    [SerializeField] ScreenTransition lobbyTransition;
    [SerializeField] LobbyStage stage;
    [SerializeField] TitleBackdrop backdrop;
    [SerializeField] Toast toast;

    // 대기실을 나갔다가 다시 열어도 마지막으로 정한 설정을 이어서 쓴다.
    DuelSettings settings = new DuelSettings();
    // 화면이 넘어가는 동안에는 새 요청을 받지 않는다. 버튼은 CanvasGroup이 막지만 Esc는 화면이 직접 받는다.
    Coroutine transition;

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
        if (transition == null) transition = StartCoroutine(EnterLobby(isHost, code, opponentPresent));
    }

    IEnumerator EnterLobby(bool isHost, string code, bool opponentPresent)
    {
        stage.gameObject.SetActive(true);
        stage.Show(settings, opponentPresent);
        yield return startTransition.Hide();
        lobby.gameObject.SetActive(true);
        lobby.Open(isHost, code, settings, opponentPresent);
        stage.Reveal();
        yield return lobbyTransition.Show();
        transition = null;
    }

    // notice가 있으면 시작 화면 메뉴 위에 돌아온 사유를 띄운다.
    IEnumerator ExitLobby(string notice)
    {
        stage.Hide();
        backdrop.ShowTitle(settings, true);
        yield return lobbyTransition.Hide();
        stage.gameObject.SetActive(false);
        startScreen.gameObject.SetActive(true);
        if (notice != null) startScreen.ShowNotice(notice);
        yield return startTransition.Show();
        transition = null;
    }

    void Leave()
    {
        if (transition != null) return;
        transition = StartCoroutine(ExitLobby(null));
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
        if (Application.isPlaying && !lobby.gameObject.activeSelf) OpenLobby(false, "123456", true);
    }

    [ContextMenu("상대 들어오기")]
    void DebugOpponentJoin()
    {
        if (!Application.isPlaying || transition != null || !lobby.gameObject.activeSelf) return;
        lobby.SetOpponentPresent(true);
        stage.SetOpponentPresent(true);
        toast.Show("상대가 맵 코드로 들어왔어요");
    }

    [ContextMenu("방장이 설정 바꾸기")]
    void DebugHostChange()
    {
        if (!Application.isPlaying || transition != null || !lobby.gameObject.activeSelf) return;
        settings.Map.Shape = settings.Map.Shape == MapShape.Donut ? MapShape.Random : settings.Map.Shape + 1;
        lobby.ApplySettings(settings);
        stage.Preview(settings);
        toast.Show("방장이 맵 형태를 바꿨어요");
    }

    [ContextMenu("상대 이탈")]
    void DebugOpponentLeave()
    {
        if (!Application.isPlaying || transition != null || !lobby.gameObject.activeSelf) return;
        transition = StartCoroutine(ExitLobby("상대가 나가서 대기실이 닫혔어요."));
    }
#endif
}
