using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// 서버의 대기실 상태를 화면에 반영한다. 전환 중에 받은 최신 상태도 전환이 끝난 뒤 적용한다.
// 서버가 전투 준비를 알리면 대기실을 잠그고, 내 플레이어가 소환되면 대기실을 걷고 카메라를 넘긴다.
// 승부가 나면 승자를 띄우고, 서버가 다음 판 로비를 알리면 같은 방의 대기실로 돌아온다.
// AI 대전은 서버와 로비 화면 없이 시작 화면에서 바로 전투로 들어갔다가, 끝나면 시작 화면으로 돌아온다.
public class MainFlow : MonoBehaviour
{
    // 다음 판 로비를 받은 뒤 내 플레이어가 디스폰되기를 기다리는 최대 시간.
    const float DespawnWait = 2f;

    [SerializeField] StartScreen startScreen;
    [SerializeField] ScreenTransition startTransition;
    [SerializeField] LobbyScreen lobby;
    [SerializeField] ScreenTransition lobbyTransition;
    [SerializeField] LobbyStage stage;
    [SerializeField] TitleBackdrop backdrop;
    [SerializeField] Toast toast;
    [SerializeField] ResultBanner resultBanner;
    [SerializeField] LobbyNetwork network;
    [SerializeField] AiDuel aiDuel;

    DuelSettings settings = new DuelSettings();
    NetworkLobbySnapshot latest;
    Coroutine transition;
    bool wantsLobby;
    bool lobbyVisible;
    // 대기실 UI를 걷고 카메라를 내 플레이어에게 넘긴 상태.
    bool battleVisible;
    bool creating;
    string exitNotice;

    void OnEnable()
    {
        startScreen.CreateRequested += Create;
        startScreen.JoinRequested += Join;
        startScreen.JoinCanceled += Cancel;
        startScreen.AiRequested += StartAiDuel;
        aiDuel.Finished += OnAiDuelFinished;
        aiDuel.Ended += OnAiDuelEnded;
        lobby.SettingsChanged += OnSettingsChanged;
        lobby.StartRequested += RequestStart;
        lobby.LeaveRequested += Leave;
        stage.PreviewFailed += OnPreviewFailed;
        network.SnapshotChanged += OnSnapshot;
        network.Failed += OnConnectionFailed;
        network.Closed += OnClosed;
        network.SettingsRejected += OnSettingsRejected;
        network.StartRejected += OnStartRejected;
    }

    void OnDisable()
    {
        startScreen.CreateRequested -= Create;
        startScreen.JoinRequested -= Join;
        startScreen.JoinCanceled -= Cancel;
        startScreen.AiRequested -= StartAiDuel;
        aiDuel.Finished -= OnAiDuelFinished;
        aiDuel.Ended -= OnAiDuelEnded;
        lobby.SettingsChanged -= OnSettingsChanged;
        lobby.StartRequested -= RequestStart;
        lobby.LeaveRequested -= Leave;
        stage.PreviewFailed -= OnPreviewFailed;
        network.SnapshotChanged -= OnSnapshot;
        network.Failed -= OnConnectionFailed;
        network.Closed -= OnClosed;
        network.SettingsRejected -= OnSettingsRejected;
        network.StartRejected -= OnStartRejected;
    }

    void Update()
    {
        // AI 대전은 혼자 하는 판이라 Esc로 언제든 그만둘 수 있다. 정리가 끝나면 OnAiDuelEnded가 시작 화면을 올린다.
        if (aiDuel.IsRunning && transition == null && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame) aiDuel.Stop();
    }

    void Create()
    {
        if (transition != null || network.IsBusy || aiDuel.IsRunning) return;
        creating = true;
        settings = new DuelSettings { Map = backdrop.Current }.Clone();
        backdrop.HoldTitle();
        startScreen.BeginCreate();
        network.Create(settings);
    }

    void Join(string code)
    {
        if (transition != null || network.IsBusy || aiDuel.IsRunning)
        {
            startScreen.ShowJoinError("연결을 정리하고 있어요. 잠시 후 다시 시도해 주세요.");
            return;
        }
        creating = false;
        network.Join(code);
    }

    // 시작 화면 맵의 설정 그대로 전투 맵을 세우고 봇과 한 판을 치른다.
    void StartAiDuel()
    {
        if (transition != null || network.IsBusy || aiDuel.IsRunning) return;
        settings = new DuelSettings { Map = backdrop.Current }.Clone();
        transition = StartCoroutine(EnterAiDuel());
    }

    IEnumerator EnterAiDuel()
    {
        backdrop.HoldTitle();
        yield return startTransition.Hide();
        if (!aiDuel.Begin(settings.ToMapSettings()))
        {
            backdrop.ShowTitle(settings, true);
            yield return startTransition.Show();
            startScreen.ShowNotice("AI 대전을 시작할 수 없어요. 잠시 후 다시 시도해 주세요.");
            transition = null;
            yield break;
        }
        // 같은 설정의 전투 맵이 같은 자리에 섰으므로 시작 화면 맵을 감춘다. 호스트라서 내 플레이어가 이미 소환되어 있으니,
        // 같은 프레임 LateUpdate 전에 배경 카메라를 놓아 내 플레이어를 따라가는 카메라와 겹치지 않게 한다.
        backdrop.HideMap();
        backdrop.ReleaseCamera();
        transition = null;
    }

    void OnAiDuelFinished(int winnerSlot) => resultBanner.Show(winnerSlot);

    // 승부가 나서 끝났거나 Esc로 그만뒀다. 전투 맵과 플레이어는 AiDuel이 이미 지웠다.
    void OnAiDuelEnded()
    {
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(LeaveAiDuel());
    }

    IEnumerator LeaveAiDuel()
    {
        resultBanner.Hide();
        // 감췄던 시작 화면 맵 자리에 새 맵이 솟는다.
        backdrop.ShowTitle(settings, true);
        yield return startTransition.Show();
        transition = null;
    }

    void Cancel()
    {
        bool restoreTitle = creating && !wantsLobby;
        network.Cancel();
        if (restoreTitle) backdrop.ShowTitle(settings, true);
    }
    void Leave() => network.Cancel();

    // 답이 올 때까지 버튼을 잠가 시작 요청이 겹치지 않게 한다.
    void RequestStart()
    {
        if (network.RequestStart()) lobby.SetStarting(true);
    }

    // 서버가 전투 준비를 알렸고 아직 다음 판 로비로 돌아오지 않았다. 내 플레이어가 소환되면 battleVisible도 맞춘다.
    bool WantsBattle => wantsLobby && latest.Phase != LobbyPhase.Lobby;

    void OnSnapshot(NetworkLobbySnapshot snapshot)
    {
        latest = snapshot;
        settings = new DuelSettings { Map = snapshot.Settings.ToSettings() };
        wantsLobby = true;
        exitNotice = null;
        startScreen.FinishConnection();
        // 결과는 두 화면에 같은 때 떠야 하므로 화면 전환을 기다리지 않는다.
        if (snapshot.Phase == LobbyPhase.Result) resultBanner.Show(snapshot.WinnerSlot);
        if (transition != null) return;
        if (lobbyVisible && !battleVisible) ApplySnapshot();
        Reconcile();
    }

    void ApplySnapshot()
    {
        lobby.SetOpponentPresent(latest.OpponentPresent);
        lobby.SetStarting(network.IsStarting);
        // 이전 요청의 응답이 더 최신인 방장 입력을 되돌리지 않게 한다.
        if (!network.IsHost || !network.HasPendingSettings) lobby.ApplySettings(settings);
        stage.SetOpponentPresent(latest.OpponentPresent);
        if (latest.Phase == LobbyPhase.Lobby)
        {
            stage.Preview(settings);
            return;
        }
        // 같은 설정의 전투 맵이 같은 자리에 섰으므로 미리보기는 감추고 더 바꾸지 않는다.
        stage.StopPreview();
        backdrop.HideMap();
    }

    void Reconcile()
    {
        if (transition == null && (wantsLobby != lobbyVisible || WantsBattle != battleVisible))
            transition = StartCoroutine(ReconcileScreens());
    }

    void OnConnectionFailed(string reason)
    {
        reason = ConnectionNotice(reason);
        if (creating)
        {
            backdrop.ShowTitle(settings, true);
            startScreen.ShowCreateError(reason);
        }
        else startScreen.ShowJoinError(reason);
    }

    void OnClosed(string reason)
    {
        wantsLobby = false;
        exitNotice = string.IsNullOrEmpty(reason) ? reason : ConnectionNotice(reason);
        if (battleVisible && reason == RoomClosed) exitNotice = "상대가 나가서 전투가 끝났어요.";
        resultBanner.Hide();
        startScreen.FinishConnection();
        Reconcile();
    }

    IEnumerator ReconcileScreens()
    {
        while (wantsLobby != lobbyVisible || WantsBattle != battleVisible)
        {
            if (wantsLobby && lobbyVisible)
            {
                if (WantsBattle) yield return EnterBattle();
                else yield return ReturnToLobby();
            }
            else if (wantsLobby)
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
                // 전투 중이었다면 대기실 UI는 이미 걷혔고, 감췄던 배경 맵 자리에 새 맵이 솟는다.
                backdrop.ShowTitle(settings, true);
                if (!battleVisible) yield return lobbyTransition.Hide();
                battleVisible = false;
                stage.gameObject.SetActive(false);
                yield return startTransition.Show();
                lobbyVisible = false;
                if (!string.IsNullOrEmpty(exitNotice)) startScreen.ShowNotice(exitNotice);
                else toast.Show("대기실에서 나왔어요");
            }
        }
        transition = null;
    }

    // 서버가 두 클라이언트의 맵을 확인하고 플레이어를 소환할 때까지 대기실에서 준비 중을 보여 준다.
    // Netcode는 소환 메시지를 EarlyUpdate에서 처리하므로, 같은 프레임 LateUpdate 전에 배경 카메라를 놓아
    // 내 플레이어를 따라가는 카메라와 한 프레임도 겹치지 않는다.
    IEnumerator EnterBattle()
    {
        ApplySnapshot();
        while (wantsLobby && !network.HasLocalPlayer) yield return null;
        if (!wantsLobby) yield break;
        backdrop.ReleaseCamera();
        stage.gameObject.SetActive(false);
        battleVisible = true;
        yield return lobbyTransition.Hide();
    }

    // 서버가 결과를 다 보여 주고 다음 판 로비를 알렸다. 방 코드·역할·설정은 그대로이고, 전투 맵은 LobbyNetwork가 이미 지웠다.
    // 내 플레이어가 디스폰되어 카메라 추적이 끝나면 카메라를 되찾고, 같은 자리의 미리보기 맵 위로 대기실을 다시 올린다.
    IEnumerator ReturnToLobby()
    {
        resultBanner.Hide();
        float giveUp = Time.unscaledTime + DespawnWait;
        while (wantsLobby && network.HasLocalPlayer && Time.unscaledTime < giveUp) yield return null;
        if (!wantsLobby) yield break;
        backdrop.RetakeCamera();
        stage.gameObject.SetActive(true);
        stage.Show(settings, latest.OpponentPresent);
        lobby.gameObject.SetActive(true);
        lobby.Open(network.IsHost, latest.Code, settings, latest.OpponentPresent);
        stage.Reveal();
        battleVisible = false;
        yield return lobbyTransition.Show();
        if (wantsLobby) ApplySnapshot();
    }

    // 미리보기는 서버가 확정한 스냅샷으로만 바꾼다.
    void OnSettingsChanged(DuelSettings changed)
    {
        network.UpdateSettings(changed);
    }

    void OnSettingsRejected(string reason)
    {
        if (!network.IsInLobby) return;
        if (lobbyVisible && transition == null) ApplySnapshot();
        toast.Show(reason switch
        {
            "Only the connected host can change room settings." => "방장만 대기실 설정을 바꿀 수 있어요.",
            "Room settings are invalid." => "맵 설정이 올바르지 않아 변경하지 않았어요.",
            "Room settings cannot change once the duel starts." => "전투 준비가 시작되어 맵 설정을 바꿀 수 없어요.",
            _ => "맵 설정을 적용할 수 없어요. 다시 시도해 주세요."
        });
    }

    void OnStartRejected(string reason)
    {
        if (!network.IsInLobby) return;
        lobby.SetStarting(network.IsStarting);
        toast.Show(reason switch
        {
            "Only the connected host can start the duel." => "방장만 게임을 시작할 수 있어요.",
            "Two players must join before the duel starts." => "상대가 들어와야 시작할 수 있어요",
            "The duel has already started." => "이미 전투를 준비하고 있어요.",
            "The battle map could not be generated." => "이 설정으로는 전투 맵을 만들 수 없어요.",
            _ => "게임을 시작할 수 없어요. 다시 시도해 주세요."
        });
    }

    void OnPreviewFailed() => toast.Show("이 설정으로는 맵을 만들 수 없어요");

    const string RoomClosed = "A player left, so the room was closed.";

    static string ConnectionNotice(string reason) => reason switch
    {
        "The room code is invalid." => "유효하지 않은 맵 코드예요. 코드를 다시 확인해 주세요.",
        "This room already has two players." => "이미 두 명이 참가한 대기실이에요.",
        LobbyDirectory.ServerFull => "서버에 방이 가득 찼어요. 잠시 후 다시 시도해 주세요.",
        "Enter a six-digit room code." => "숫자 6자리 맵 코드를 입력해 주세요.",
        RoomClosed => "상대가 나가서 대기실이 닫혔어요.",
        "The battle map could not be generated." => "전투 맵을 만들지 못해 대기실에서 나왔어요.",
        "Generated map does not match the server." => "맵이 서버와 달라서 연결이 끊겼어요.",
        _ => "서버 연결이 끊겼거나 접속할 수 없어요. 잠시 후 다시 시도해 주세요."
    };
}
