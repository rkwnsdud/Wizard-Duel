using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// 서버의 대기실 상태를 화면에 반영한다. 전환 중에 받은 최신 상태도 전환이 끝난 뒤 적용한다.
// 서버가 전투 준비를 알리면 대기실을 걷고 카메라를 내 스폰 지점으로 내려보내며, 내 플레이어가 소환되면 그 플레이어에게 넘긴다.
// 승부가 나면 승자를 띄우고, 서버가 다음 판 로비를 알리면 같은 방의 대기실로 돌아온다.
// AI 대전은 서버 없이 같은 로비 화면을 AI용으로 열어 맵과 무기·난이도를 고르고, 전투가 끝나면 그 로비 화면으로 돌아온다.
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

    DuelSettings settings = NetworkDuelConfig.DefaultDuelSettings();
    NetworkLobbySnapshot latest;
    Coroutine transition;
    bool wantsLobby;
    bool lobbyVisible;
    // 대기실 UI를 걷고 카메라를 내 플레이어에게 넘긴 상태.
    bool battleVisible;
    bool creating;
    string exitNotice;
    // 접속을 그만둔 직후처럼 연결을 정리하는 동안 받은 메뉴 요청. 정리가 끝나면 Update에서 실행한다.
    Action queued;
    // AI 로비 화면에 있거나 거기서 시작한 전투 중이다. 이동안 로비 화면의 이벤트는 서버에 보내지 않고 여기서 처리한다.
    bool aiLobby;
    // AI 로비 화면의 "게임 설정" 탭에서 고른 값. 이번 실행 동안 기억한다.
    AiDuelOptions aiOptions = NetworkDuelConfig.DefaultAiOptions();

    void OnEnable()
    {
        startScreen.CreateRequested += Create;
        startScreen.JoinRequested += Join;
        startScreen.JoinCanceled += Cancel;
        startScreen.AiRequested += OpenAiLobby;
        aiDuel.Finished += OnAiDuelFinished;
        aiDuel.Ended += OnAiDuelEnded;
        lobby.SettingsChanged += OnSettingsChanged;
        lobby.GameSettingsChanged += OnGameSettingsChanged;
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
        startScreen.AiRequested -= OpenAiLobby;
        aiDuel.Finished -= OnAiDuelFinished;
        aiDuel.Ended -= OnAiDuelEnded;
        lobby.SettingsChanged -= OnSettingsChanged;
        lobby.GameSettingsChanged -= OnGameSettingsChanged;
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
        if (queued != null && transition == null && !network.IsBusy)
        {
            Action run = queued;
            queued = null;
            run();
        }
        // AI 대전은 혼자 하는 판이라 Esc로 언제든 그만둘 수 있다. 정리가 끝나면 OnAiDuelEnded가 AI 로비 화면을 다시 올린다.
        if (aiDuel.IsRunning && transition == null && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame) aiDuel.Stop();
    }

    void Create()
    {
        if (transition != null || aiLobby || wantsLobby) return;
        if (Queue(Create)) return;
        creating = true;
        settings = new DuelSettings { Map = backdrop.Current }.Clone();
        backdrop.HoldTitle();
        startScreen.BeginCreate();
        network.Create(settings);
    }

    void Join(string code)
    {
        if (transition != null || aiLobby || wantsLobby)
        {
            startScreen.ShowJoinError("연결을 정리하고 있어요. 잠시 후 다시 시도해 주세요.");
            return;
        }
        if (Queue(() => Join(code))) return;
        creating = false;
        network.Join(code);
    }

    // 시작 화면 맵을 그대로 이어받아 AI 로비 화면을 연다. 서버에 접속하지 않는다.
    void OpenAiLobby()
    {
        if (transition != null || aiLobby || wantsLobby) return;
        if (Queue(OpenAiLobby)) return;
        settings = new DuelSettings { Map = backdrop.Current }.Clone();
        aiLobby = true;
        transition = StartCoroutine(EnterAiLobby());
    }

    IEnumerator EnterAiLobby()
    {
        backdrop.HoldTitle();
        stage.gameObject.SetActive(true);
        stage.Show(settings, true);
        yield return startTransition.Hide();
        yield return ShowAiLobby();
        transition = null;
    }

    // 무대가 맵을 이어받은 뒤(stage.Show) 로비 화면 UI를 올리고 모형 마법사 둘을 세운다.
    IEnumerator ShowAiLobby()
    {
        lobby.gameObject.SetActive(true);
        lobby.OpenAi(settings, aiOptions);
        stage.Reveal();
        yield return lobbyTransition.Show();
        // UI가 올라오는 동안 바꾼 맵 설정이 있으면 미리보기에 반영한다.
        stage.Preview(settings);
    }

    // 지금 설정대로 전투 맵을 세우고 고른 무기와 난이도로 봇과 한 판을 치른다.
    IEnumerator EnterAiBattle()
    {
        lobby.SetStarting(true);
        // 맵을 만드는 동안 잠깐 멈추므로, 준비 중인 화면을 한 프레임 먼저 그린다.
        yield return null;
        if (!aiDuel.Begin(settings.ToMapSettings(), aiOptions))
        {
            lobby.SetStarting(false);
            toast.Show("AI 대전을 시작할 수 없어요. 맵 설정을 바꿔 다시 시도해 주세요.");
            transition = null;
            yield break;
        }
        // 같은 설정의 전투 맵이 같은 자리에 섰으므로 미리보기 맵을 감추고, 카메라를 내 스폰 지점으로 내려보낸다.
        // 호스트라서 내 플레이어가 이미 소환되어 따라가는 카메라가 켜졌지만, 같은 프레임 LateUpdate 전에 Dive가 붙잡아 두었다가
        // 다 내려가면 넘겨준다. 모형 마법사는 실제 플레이어가 마법진과 함께 나타난 지금 걷는다.
        stage.StopPreview();
        backdrop.HideMap();
        backdrop.Dive(aiDuel.Arena, 0);
        stage.gameObject.SetActive(false);
        yield return lobbyTransition.Hide();
        transition = null;
    }

    void OnAiDuelFinished(int winnerSlot) => resultBanner.Show(winnerSlot);

    // 승부가 나서 끝났거나 Esc로 그만뒀다. 전투 맵과 플레이어는 AiDuel이 이미 지웠다.
    void OnAiDuelEnded()
    {
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(ReturnToAiLobby());
    }

    // 카메라를 되찾고, 감췄던 미리보기 맵 위로 AI 로비 화면을 다시 올린다. 맵과 게임 설정은 그대로다.
    IEnumerator ReturnToAiLobby()
    {
        resultBanner.Hide();
        backdrop.RetakeCamera();
        stage.gameObject.SetActive(true);
        stage.Show(settings, true);
        yield return ShowAiLobby();
        transition = null;
    }

    IEnumerator LeaveAiLobby()
    {
        stage.Hide();
        backdrop.ShowTitle(settings, true);
        yield return lobbyTransition.Hide();
        stage.gameObject.SetActive(false);
        yield return startTransition.Show();
        aiLobby = false;
        transition = null;
    }

    void OnGameSettingsChanged(AiDuelOptions changed) => aiOptions = changed.Clone();

    // 이전 접속을 정리하는 중이면 요청을 맡아 두고 true를 돌려준다. 접속을 그만두자마자 다른 메뉴를 눌러도 먹히게 한다.
    bool Queue(Action request)
    {
        queued = network.IsBusy ? request : null;
        return queued != null;
    }

    void Cancel()
    {
        queued = null;
        bool restoreTitle = creating && !wantsLobby;
        network.Cancel();
        if (restoreTitle) backdrop.ShowTitle(settings, true);
    }

    void Leave()
    {
        if (!aiLobby)
        {
            network.Cancel();
            return;
        }
        // 화면을 바꾸는 중이거나 전투 중에는 로비 화면이 아직 떠 있어도 나가지 않는다.
        if (transition == null && !aiDuel.IsRunning) transition = StartCoroutine(LeaveAiLobby());
    }

    // 답이 올 때까지 버튼을 잠가 시작 요청이 겹치지 않게 한다.
    void RequestStart()
    {
        if (aiLobby)
        {
            if (transition == null && !aiDuel.IsRunning) transition = StartCoroutine(EnterAiBattle());
            return;
        }
        if (network.RequestStart()) lobby.SetStarting(true);
    }

    // 서버가 전투 준비를 알렸고 아직 다음 판 로비로 돌아오지 않았다. 내 플레이어가 소환되면 battleVisible도 맞춘다.
    bool WantsBattle => wantsLobby && latest.Phase != LobbyPhase.Lobby;

    void OnSnapshot(NetworkLobbySnapshot snapshot)
    {
        latest = snapshot;
        queued = null;
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

    void OnConnectionFailed(LobbyReason reason)
    {
        // 그만둔 접속의 실패가 뒤늦게 왔다. 이미 다른 메뉴로 넘어가는 중이니 알리지 않는다.
        if (queued != null) return;
        string notice = LobbyReasonMessages.ConnectionNotice(reason);
        if (creating)
        {
            backdrop.ShowTitle(settings, true);
            startScreen.ShowCreateError(notice);
        }
        else startScreen.ShowJoinError(notice);
    }

    void OnClosed(LobbyReason reason)
    {
        wantsLobby = false;
        exitNotice = LobbyReasonMessages.ClosedNotice(reason, battleVisible);
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

    // 서버가 전투 준비를 알렸다. 이 클라이언트의 전투 맵은 이미 섰으므로(LobbyNetwork가 스냅샷을 알리기 전에 만든다)
    // 미리보기 맵과 바꿔 끼우고, 대기실 UI를 걷으며 카메라를 내 스폰 지점(방장은 파랑, 참가자는 빨강)으로 내려보낸다.
    // 서버가 두 클라이언트의 맵을 확인하고 플레이어를 소환하면 그 프레임에 모형 마법사를 걷는다. Netcode는 소환 메시지를
    // EarlyUpdate에서 처리하므로 모형과 실제 플레이어가 한 프레임도 겹치지 않는다. 카메라는 다 내려가면 내 플레이어에게 넘어간다.
    IEnumerator EnterBattle()
    {
        ApplySnapshot();
        int slot = network.IsHost ? 0 : 1;
        backdrop.Dive(network.Arena, slot);
        Coroutine hiding = StartCoroutine(lobbyTransition.Hide());
        while (wantsLobby && !network.HasLocalPlayer) yield return null;
        if (!wantsLobby)
        {
            // 접속이 끊겼다. 시작 화면으로 돌아가는 분기가 UI를 마저 걷고 카메라를 빼낸다.
            StopCoroutine(hiding);
            yield break;
        }
        stage.gameObject.SetActive(false);
        battleVisible = true;
        yield return hiding;
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

    // 미리보기는 서버가 확정한 스냅샷으로만 바꾼다. AI 대전은 서버가 없으니 바로 바꾼다.
    void OnSettingsChanged(DuelSettings changed)
    {
        if (aiLobby)
        {
            settings = changed.Clone();
            if (transition == null) stage.Preview(settings);
            return;
        }
        network.UpdateSettings(changed);
    }

    void OnSettingsRejected(LobbyReason reason)
    {
        if (!network.IsInLobby) return;
        if (lobbyVisible && transition == null) ApplySnapshot();
        toast.Show(LobbyReasonMessages.SettingsRejected(reason));
    }

    void OnStartRejected(LobbyReason reason)
    {
        if (!network.IsInLobby) return;
        lobby.SetStarting(network.IsStarting);
        toast.Show(LobbyReasonMessages.StartRejected(reason));
    }

    void OnPreviewFailed() => toast.Show("이 설정으로는 맵을 만들 수 없어요");
}
