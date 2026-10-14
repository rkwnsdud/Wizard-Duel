using System.Collections;
using UnityEngine;

// 서버의 대기실 상태를 화면에 반영한다. 전환 중에 받은 최신 상태도 전환이 끝난 뒤 적용한다.
public class MainFlow : MonoBehaviour
{
    [SerializeField] StartScreen startScreen;
    [SerializeField] ScreenTransition startTransition;
    [SerializeField] LobbyScreen lobby;
    [SerializeField] ScreenTransition lobbyTransition;
    [SerializeField] LobbyStage stage;
    [SerializeField] TitleBackdrop backdrop;
    [SerializeField] Toast toast;
    [SerializeField] LobbyNetwork network;

    DuelSettings settings = new DuelSettings();
    NetworkLobbySnapshot latest;
    Coroutine transition;
    bool wantsLobby;
    bool lobbyVisible;
    bool creating;
    string exitNotice;

    void OnEnable()
    {
        startScreen.CreateRequested += Create;
        startScreen.JoinRequested += Join;
        startScreen.JoinCanceled += Cancel;
        lobby.SettingsChanged += OnSettingsChanged;
        lobby.LeaveRequested += Leave;
        stage.PreviewFailed += OnPreviewFailed;
        network.SnapshotChanged += OnSnapshot;
        network.Failed += OnConnectionFailed;
        network.Closed += OnClosed;
    }

    void OnDisable()
    {
        startScreen.CreateRequested -= Create;
        startScreen.JoinRequested -= Join;
        startScreen.JoinCanceled -= Cancel;
        lobby.SettingsChanged -= OnSettingsChanged;
        lobby.LeaveRequested -= Leave;
        stage.PreviewFailed -= OnPreviewFailed;
        network.SnapshotChanged -= OnSnapshot;
        network.Failed -= OnConnectionFailed;
        network.Closed -= OnClosed;
    }

    void Create()
    {
        if (transition != null || network.IsBusy) return;
        creating = true;
        startScreen.BeginCreate();
        network.Create(settings);
    }

    void Join(string code)
    {
        if (transition != null || network.IsBusy)
        {
            startScreen.ShowJoinError("연결을 정리하고 있어요. 잠시 후 다시 시도해 주세요.");
            return;
        }
        creating = false;
        network.Join(code);
    }

    void Cancel() => network.Cancel();
    void Leave() => network.Leave();

    void OnSnapshot(NetworkLobbySnapshot snapshot)
    {
        latest = snapshot;
        settings = snapshot.Settings.ToSettings();
        wantsLobby = true;
        exitNotice = null;
        startScreen.FinishConnection();
        if (transition == null)
        {
            if (lobbyVisible) ApplySnapshot();
            else transition = StartCoroutine(ReconcileScreens());
        }
    }

    void ApplySnapshot()
    {
        lobby.SetOpponentPresent(latest.OpponentPresent);
        lobby.ApplySettings(settings);
        stage.SetOpponentPresent(latest.OpponentPresent);
        stage.Preview(settings);
    }

    void OnConnectionFailed(string reason)
    {
        reason = ConnectionNotice(reason);
        if (creating) startScreen.ShowCreateError(reason);
        else startScreen.ShowJoinError(reason);
    }

    void OnClosed(string reason)
    {
        wantsLobby = false;
        exitNotice = string.IsNullOrEmpty(reason) ? reason : ConnectionNotice(reason);
        startScreen.FinishConnection();
        if (transition == null && lobbyVisible) transition = StartCoroutine(ReconcileScreens());
    }

    IEnumerator ReconcileScreens()
    {
        while (wantsLobby != lobbyVisible)
        {
            if (wantsLobby)
            {
                stage.gameObject.SetActive(true);
                stage.Show(settings, latest.OpponentPresent);
                yield return startTransition.Hide();
                // 승인 직후 취소·이탈했다면 감춘 시작 화면으로 바로 돌아간다.
                if (wantsLobby)
                {
                    lobby.gameObject.SetActive(true);
                    lobby.Open(network.IsHost, latest.Code, settings, latest.OpponentPresent);
                    stage.Reveal();
                    yield return lobbyTransition.Show();
                    lobbyVisible = true;
                    if (wantsLobby) ApplySnapshot();
                }
                else
                {
                    stage.Hide();
                    stage.gameObject.SetActive(false);
                    backdrop.ShowTitle(settings, true);
                    yield return startTransition.Show();
                    if (!string.IsNullOrEmpty(exitNotice)) startScreen.ShowNotice(exitNotice);
                }
            }
            else
            {
                stage.Hide();
                backdrop.ShowTitle(settings, true);
                yield return lobbyTransition.Hide();
                stage.gameObject.SetActive(false);
                yield return startTransition.Show();
                lobbyVisible = false;
                if (!string.IsNullOrEmpty(exitNotice)) startScreen.ShowNotice(exitNotice);
                else toast.Show("대기실에서 나왔어요");
            }
        }
        transition = null;
    }

    // 설정 동기화는 다음 단계에서 서버 요청에 연결한다.
    void OnSettingsChanged(DuelSettings changed)
    {
        settings = changed.Clone();
        stage.Preview(settings);
    }

    void OnPreviewFailed() => toast.Show("이 설정으로는 맵을 만들 수 없어요");

    static string ConnectionNotice(string reason) => reason switch
    {
        "The room code is invalid." => "유효하지 않은 맵 코드예요. 코드를 다시 확인해 주세요.",
        "This room already has two players." => "이미 두 명이 참가한 대기실이에요.",
        "A room already exists." => "서버에 이미 대기실이 있어요. 맵 코드로 참가해 주세요.",
        "Enter a six-digit room code." => "숫자 6자리 맵 코드를 입력해 주세요.",
        "A player left, so the room was closed." => "상대가 나가서 대기실이 닫혔어요.",
        _ => "서버 연결이 끊겼거나 접속할 수 없어요. 잠시 후 다시 시도해 주세요."
    };
}
