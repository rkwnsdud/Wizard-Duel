using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Main의 로비와 전투 시작을 연결한다. 맵과 플레이어는 방장의 시작 요청을 서버가 승인한 뒤에만 만든다.
[DefaultExecutionOrder(-1000)]
public sealed class LobbyNetwork : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] UnityTransport transport;
    [SerializeField] DuelArena arena;
    [SerializeField] string serverAddress = "127.0.0.1";
    [SerializeField] ushort port = 7777;
    [SerializeField] GameObject[] clientPresentation;

    const string SnapshotMessage = "WizardDuel.LobbySnapshot";
    const string SettingsMessage = "WizardDuel.LobbySettings";
    const string ErrorMessage = "WizardDuel.LobbyError";
    const string StartMessage = "WizardDuel.LobbyStart";
    const string StartRejectedMessage = "WizardDuel.LobbyStartRejected";
    const string MapReadyMessage = "WizardDuel.LobbyMapReady";
    const string RoomClosed = "A player left, so the room was closed.";
    const string InvalidRequest = "The lobby request is invalid.";
    const string ConnectionFailed = "Disconnected from the server.";
    const string MapFailed = "The battle map could not be generated.";
    const string MapMismatch = "Generated map does not match the server.";
    const int BufferSize = 256;
    readonly LobbySession session = new LobbySession();
    readonly Dictionary<ulong, float> reservations = new Dictionary<ulong, float>();
    readonly List<ulong> expiredReservations = new List<ulong>();
    readonly HashSet<ulong> readyClients = new HashSet<ulong>();
    bool serverMode;
    bool connecting;
    bool closing;
    bool restartServer;
    bool inLobby;
    bool createdRoom;
    bool notifyClosed;
    string requestedCode;
    string disconnectReason;
    float snapshotDeadline;
    NetworkLobbySnapshot currentSnapshot;
    uint requestedSettingsRevision;
    uint settledSettingsRevision;
    uint serverSettingsRevision;
    // 방장이 보낸 마지막 시작 요청 번호. 거절 응답은 같은 번호일 때만 받아들인다.
    uint requestedStart;
    bool startPending;

    public event Action<NetworkLobbySnapshot> SnapshotChanged;
    public event Action<string> Failed;
    public event Action<string> Closed;
    public event Action<string> SettingsRejected;
    public event Action<string> StartRejected;
    public bool IsBusy => connecting || closing || inLobby || (manager != null && manager.ShutdownInProgress);
    public bool IsInLobby => inLobby;
    public bool IsHost => inLobby && currentSnapshot.HostClientId == manager.LocalClientId;
    public bool HasPendingSettings => inLobby && requestedSettingsRevision > settledSettingsRevision;
    // 시작 요청의 답을 기다리거나 서버가 전투 준비를 알린 뒤다.
    public bool IsStarting => inLobby && (startPending || currentSnapshot.Phase != LobbyPhase.Lobby);
    public bool HasLocalPlayer => inLobby && manager.LocalClient != null && manager.LocalClient.PlayerObject != null;

    void Awake()
    {
        serverMode = Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0;
        Application.targetFrameRate = serverMode ? 30 : 60;
        if (serverMode && clientPresentation != null)
        {
            foreach (GameObject presentation in clientPresentation)
                if (presentation != null) presentation.SetActive(false);
        }
        if (manager == null || transport == null) return;
        manager.NetworkConfig.ConnectionApproval = true;
        manager.NetworkConfig.EnableSceneManagement = false;
        manager.NetworkConfig.ClientConnectionBufferTimeout = 10;
        manager.ConnectionApprovalCallback = ApproveConnection;
        manager.OnClientConnectedCallback += OnConnected;
        manager.OnClientDisconnectCallback += OnDisconnected;
        manager.OnTransportFailure += OnTransportFailure;
    }

    void Start()
    {
        // -join은 주소만 정한다. 코드 없는 자동 접속은 하지 않는다.
        string[] args = Environment.GetCommandLineArgs();
        int joinIndex = Array.IndexOf(args, "-join");
        if (joinIndex >= 0 && joinIndex + 1 < args.Length) serverAddress = args[joinIndex + 1].Trim();
        if (serverMode) StartServer();
    }

    void Update()
    {
        if (manager == null) return;
        if (serverMode && manager.IsListening && !closing) ExpireReservations();
        if (connecting && !closing && Time.realtimeSinceStartup >= snapshotDeadline)
            StopClient("The connection timed out.", false);
        if (!closing || manager.IsListening || manager.ShutdownInProgress) return;

        closing = false;
        if (serverMode)
        {
            if (restartServer)
            {
                restartServer = false;
                StartServer();
            }
            return;
        }
        // 소환된 플레이어는 연결 종료 때 Netcode가 지운다. 시작 화면으로 돌아가는 프레임에 맵도 지운다.
        if (arena != null) arena.Clear();
        string reason = disconnectReason;
        bool wasInLobby = notifyClosed;
        disconnectReason = null;
        notifyClosed = false;
        if (wasInLobby) Closed?.Invoke(reason ?? string.Empty);
        else if (!string.IsNullOrEmpty(reason)) Failed?.Invoke(reason);
    }

    public void Create(DuelSettings settings) => StartRequest(true, string.Empty, settings);
    public void Join(string code) => StartRequest(false, code, null);

    public void UpdateSettings(DuelSettings settings)
    {
        if (!IsInLobby || closing || currentSnapshot.Phase != LobbyPhase.Lobby) return;
        if (!DuelSettings.ValidateSettings(settings, out string reason))
        {
            Debug.Log($"[Lobby] Settings rejected: {reason}");
            SettingsRejected?.Invoke(reason);
            return;
        }
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        requestedSettingsRevision++;
        writer.WriteValueSafe(requestedSettingsRevision);
        writer.WriteNetworkSerializable(new NetworkMapSettings(settings.Map));
        manager.CustomMessagingManager.SendNamedMessage(SettingsMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
    }

    // 방장이 상대와 함께 로비 단계에 있을 때 한 번만 보낸다. 승인은 전투 준비 스냅샷으로, 거절은 같은 번호의 응답으로 온다.
    public bool RequestStart()
    {
        if (!IsHost || closing || startPending || !currentSnapshot.OpponentPresent ||
            currentSnapshot.Phase != LobbyPhase.Lobby) return false;
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        requestedStart++;
        writer.WriteValueSafe(requestedStart);
        manager.CustomMessagingManager.SendNamedMessage(StartMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
        startPending = true;
        Debug.Log($"[Lobby] Start requested code={currentSnapshot.Code} request={requestedStart} " +
            $"revision={currentSnapshot.SettingsRevision} seed={currentSnapshot.Settings.Seed}");
        return true;
    }

    public void Cancel()
    {
        if (serverMode || closing || (!connecting && !inLobby)) return;
        StopClient(string.Empty, true);
    }

    void StartRequest(bool createRoom, string code, DuelSettings settings)
    {
        if (serverMode || IsBusy) return;
        string reason = "Enter a six-digit room code.";
        bool isValid = createRoom ? DuelSettings.ValidateSettings(settings, out reason) : LobbySession.IsValidCode(code);
        if (!isValid)
        {
            Failed?.Invoke(reason);
            return;
        }
        if (manager == null || transport == null)
        {
            Failed?.Invoke("The lobby network is not configured.");
            return;
        }
        if (manager.IsListening || manager.ShutdownInProgress) return;
        createdRoom = createRoom;
        requestedCode = code;
        requestedSettingsRevision = 0;
        settledSettingsRevision = 0;
        requestedStart = 0;
        startPending = false;
        currentSnapshot = default;
        connecting = true;
        snapshotDeadline = Time.realtimeSinceStartup + manager.NetworkConfig.ClientConnectionBufferTimeout + 5;
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteNetworkSerializable(new LobbyJoinRequest(createRoom, code, settings));
        manager.NetworkConfig.ConnectionData = writer.ToArray();
        transport.SetConnectionData(serverAddress, port);
        Debug.Log($"[Lobby] Connecting address={serverAddress}:{port} create={createdRoom} code={requestedCode}");
        if (!manager.StartClient())
        {
            StopClient("Could not start the connection.", false);
            return;
        }
        RegisterMessages();
    }

    void StartServer()
    {
        if (manager == null || transport == null || manager.IsListening || manager.ShutdownInProgress) return;
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
        if (!manager.StartServer())
        {
            Debug.LogError($"[Lobby] Could not listen on UDP {port}.");
            return;
        }
        RegisterMessages();
        Debug.Log($"[Lobby] Listening UDP={port} room=none playerobjects={manager.SpawnManager.PlayerObjects.Count} " +
            $"mapgenerated={MapGenerated}");
    }

    bool MapGenerated => arena != null && arena.HasMap;

    void RegisterMessages()
    {
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SnapshotMessage, OnSnapshot);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SettingsMessage, OnSettings);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(ErrorMessage, OnError);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StartMessage, OnStart);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StartRejectedMessage, OnStartRejected);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(MapReadyMessage, OnMapReady);
    }

    void UnregisterMessages()
    {
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(SnapshotMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(SettingsMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(ErrorMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(StartMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(StartRejectedMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(MapReadyMessage);
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Approved = false;
        response.Reason = InvalidRequest;
        if (!serverMode || closing || request.Payload == null || request.Payload.Length > BufferSize) return;
        ExpireReservations();
        try
        {
            using var reader = new FastBufferReader(request.Payload, Allocator.Temp);
            reader.ReadNetworkSerializable(out LobbyJoinRequest payload);
            if (reader.Position != reader.Length || payload.Version != LobbyJoinRequest.CurrentVersion) return;
            string reason;
            bool approved = payload.CreateRoom
                ? session.TryCreate(request.ClientNetworkId, new DuelSettings { Map = payload.Settings.ToSettings() }, out reason)
                : session.TryJoin(request.ClientNetworkId, payload.Code, out reason);
            response.Approved = approved;
            response.Reason = approved ? string.Empty : reason;
            if (approved)
                reservations[request.ClientNetworkId] = Time.realtimeSinceStartup + manager.NetworkConfig.ClientConnectionBufferTimeout + 1;
            Debug.Log($"[Lobby] Approval client={request.ClientNetworkId} approved={approved} reason={response.Reason}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid approval client={request.ClientNetworkId}: {exception.Message}");
        }
    }

    void OnConnected(ulong clientId)
    {
        if (!serverMode || closing) return;
        reservations.Remove(clientId);
        if (!session.Confirm(clientId))
        {
            manager.DisconnectClient(clientId, InvalidRequest);
            return;
        }
        BroadcastSnapshot();
    }

    void BroadcastSnapshot()
    {
        NetworkLobbySnapshot snapshot = session.Snapshot;
        snapshot.SettingsRevision = serverSettingsRevision;
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteNetworkSerializable(snapshot);
        foreach (ulong clientId in manager.ConnectedClientsIds)
        {
            if (!session.Contains(clientId)) continue;
            manager.CustomMessagingManager.SendNamedMessage(SnapshotMessage, clientId, writer,
                NetworkDelivery.ReliableSequenced);
        }
        Debug.Log($"[Lobby] Snapshot code={snapshot.Code} host={snapshot.HostClientId} " +
            $"members={(snapshot.OpponentPresent ? 2 : 1)} phase={snapshot.Phase} revision={snapshot.SettingsRevision} " +
            $"seed={snapshot.Settings.Seed} settings={JsonUtility.ToJson(snapshot.Settings)} " +
            $"playerobjects={manager.SpawnManager.PlayerObjects.Count} mapgenerated={MapGenerated}");
    }

    void OnSnapshot(ulong senderId, FastBufferReader reader)
    {
        if (serverMode || closing || (!connecting && !inLobby) || senderId != NetworkManager.ServerClientId) return;
        NetworkLobbySnapshot snapshot;
        LobbyPhase previousPhase = currentSnapshot.Phase;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadNetworkSerializable(out snapshot);
            bool isHost = snapshot.HostClientId == manager.LocalClientId;
            // 시작된 방에는 새로 들어갈 수 없고, 한 방 안에서 단계는 되돌아가지 않는다.
            bool matchesRequest = createdRoom == isHost && (createdRoom || snapshot.Code == requestedCode) &&
                snapshot.Phase == LobbyPhase.Lobby;
            bool matchesCurrentRoom = snapshot.Code == currentSnapshot.Code &&
                snapshot.HostClientId == currentSnapshot.HostClientId && snapshot.SettingsRevision >= currentSnapshot.SettingsRevision &&
                snapshot.Phase >= currentSnapshot.Phase;
            if (reader.Position != reader.Length || !snapshot.IsValidFor(manager.LocalClientId) ||
                !(inLobby ? matchesCurrentRoom : matchesRequest) || (isHost && snapshot.SettingsRevision > requestedSettingsRevision))
                throw new InvalidOperationException(InvalidRequest);
            connecting = false;
            inLobby = true;
            currentSnapshot = snapshot;
            settledSettingsRevision = Math.Max(settledSettingsRevision, snapshot.SettingsRevision);
            if (snapshot.Phase != LobbyPhase.Lobby) startPending = false;
            Debug.Log($"[Lobby] Joined code={snapshot.Code} host={IsHost} " +
                $"members={(snapshot.OpponentPresent ? 2 : 1)} phase={snapshot.Phase} revision={snapshot.SettingsRevision} " +
                $"seed={snapshot.Settings.Seed} playerobjects={manager.SpawnManager?.PlayerObjects.Count ?? 0} " +
                $"mapgenerated={MapGenerated}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid snapshot: {exception.Message}");
            StopClient(InvalidRequest, false);
            return;
        }
        if (previousPhase == LobbyPhase.Lobby && snapshot.Phase != LobbyPhase.Lobby && !SendMapReady(snapshot)) return;
        SnapshotChanged?.Invoke(snapshot);
    }

    // 전투 준비를 처음 알게 되면 서버가 보낸 설정 그대로 맵을 만들고, 지문을 서버에 보내 비교받는다.
    bool SendMapReady(NetworkLobbySnapshot snapshot)
    {
        if (arena == null || !arena.Build(snapshot.Settings.ToSettings()))
        {
            StopClient(MapFailed, false);
            return false;
        }
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteValueSafe(snapshot.SettingsRevision);
        writer.WriteValueSafe(arena.Fingerprint);
        manager.CustomMessagingManager.SendNamedMessage(MapReadyMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
        Debug.Log($"[Lobby] Battle map ready code={snapshot.Code} revision={snapshot.SettingsRevision} " +
            $"seed={snapshot.Settings.Seed} fingerprint={arena.Fingerprint}");
        return true;
    }

    void OnSettings(ulong senderId, FastBufferReader reader)
    {
        if (!serverMode || closing || !session.Contains(senderId)) return;
        uint revision = 0;
        string reason = InvalidRequest;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadValueSafe(out revision);
            reader.ReadNetworkSerializable(out NetworkMapSettings mapSettings);
            if (reader.Position != reader.Length) throw new InvalidOperationException(InvalidRequest);
            if (revision == 0 || revision <= serverSettingsRevision)
                reason = "The settings update is out of date.";
            else if (session.TryUpdateSettings(senderId, new DuelSettings { Map = mapSettings.ToSettings() }, out reason))
            {
                serverSettingsRevision = revision;
                BroadcastSnapshot();
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid settings client={senderId}: {exception.Message}");
        }
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteValueSafe(revision);
        writer.WriteValueSafe(reason);
        manager.CustomMessagingManager.SendNamedMessage(ErrorMessage, senderId, writer, NetworkDelivery.ReliableSequenced);
    }

    void OnError(ulong senderId, FastBufferReader reader)
    {
        if (serverMode || closing || !inLobby || senderId != NetworkManager.ServerClientId) return;
        uint revision;
        string reason;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadValueSafe(out revision);
            reader.ReadValueSafe(out reason);
            if (reader.Position != reader.Length || revision > requestedSettingsRevision)
                throw new InvalidOperationException(InvalidRequest);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid error message: {exception.Message}");
            return;
        }
        settledSettingsRevision = Math.Max(settledSettingsRevision, revision);
        Debug.Log($"[Lobby] Settings rejected revision={revision}: {reason}");
        SettingsRejected?.Invoke(reason);
    }

    // 서버: 방장만, 두 명이 모두 확정 참가한 로비 단계에서만 시작을 받는다. 맵은 저장된 설정 그대로 만든다.
    void OnStart(ulong senderId, FastBufferReader reader)
    {
        if (!serverMode || closing || !session.Contains(senderId)) return;
        uint request = 0;
        string reason = InvalidRequest;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadValueSafe(out request);
            if (reader.Position != reader.Length) throw new InvalidOperationException(InvalidRequest);
            if (session.TryStart(senderId, out reason))
            {
                if (BuildServerMap())
                {
                    BroadcastSnapshot();
                    return;
                }
                session.CancelStart();
                reason = MapFailed;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid start client={senderId}: {exception.Message}");
        }
        Debug.Log($"[Lobby] Start rejected client={senderId} request={request} phase={session.Phase}: {reason}");
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteValueSafe(request);
        writer.WriteValueSafe(reason);
        manager.CustomMessagingManager.SendNamedMessage(StartRejectedMessage, senderId, writer, NetworkDelivery.ReliableSequenced);
    }

    bool BuildServerMap()
    {
        readyClients.Clear();
        NetworkLobbySnapshot snapshot = session.Snapshot;
        bool built = arena != null && arena.Build(snapshot.Settings.ToSettings());
        Debug.Log($"[Lobby] Battle preparing code={snapshot.Code} revision={serverSettingsRevision} " +
            $"seed={snapshot.Settings.Seed} mapgenerated={built} fingerprint={(built ? arena.Fingerprint : "none")}");
        return built;
    }

    void OnStartRejected(ulong senderId, FastBufferReader reader)
    {
        if (serverMode || closing || !inLobby || senderId != NetworkManager.ServerClientId) return;
        uint request;
        string reason;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadValueSafe(out request);
            reader.ReadValueSafe(out reason);
            if (reader.Position != reader.Length || request > requestedStart)
                throw new InvalidOperationException(InvalidRequest);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid start rejection: {exception.Message}");
            return;
        }
        // 이미 승인되었거나 더 새 요청을 기다리는 중이면 늦게 온 거절을 무시한다.
        if (!startPending || request != requestedStart) return;
        startPending = false;
        Debug.Log($"[Lobby] Start rejected request={request}: {reason}");
        StartRejected?.Invoke(reason);
    }

    // 서버: 두 클라이언트의 맵 지문이 서버와 같아야 플레이어를 소환한다. 다르면 그 접속을 끊어 방을 닫는다.
    void OnMapReady(ulong senderId, FastBufferReader reader)
    {
        if (!serverMode || closing || !session.Contains(senderId) || session.Phase != LobbyPhase.Preparing) return;
        uint revision;
        string fingerprint;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadValueSafe(out revision);
            reader.ReadValueSafe(out fingerprint);
            if (reader.Position != reader.Length) throw new InvalidOperationException(InvalidRequest);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid map ready client={senderId}: {exception.Message}");
            manager.DisconnectClient(senderId, InvalidRequest);
            return;
        }
        if (revision != serverSettingsRevision || fingerprint != arena.Fingerprint)
        {
            Debug.LogError($"[Lobby] Client {senderId} map does not match the server revision={revision}/{serverSettingsRevision} " +
                $"fingerprint={fingerprint}");
            manager.DisconnectClient(senderId, MapMismatch);
            return;
        }
        if (!readyClients.Add(senderId)) return;
        Debug.Log($"[Lobby] Client {senderId} map ready ({readyClients.Count}/2) fingerprint={fingerprint}");
        if (!readyClients.Contains(session.HostClientId) || !readyClients.Contains(session.GuestClientId)) return;
        arena.SpawnPlayers(session.HostClientId, session.GuestClientId, OnCombatEnabled, OnPlayerDefeated);
        Debug.Log($"[Lobby] Players spawned code={session.RoomCode} playerobjects={manager.SpawnManager.PlayerObjects.Count}");
    }

    void OnCombatEnabled()
    {
        if (closing || !session.BeginBattle()) return;
        BroadcastSnapshot();
    }

    // 먼저 쓰러진 플레이어 한 명만 온다. 승자 표시와 로비 화면 복귀는 #19 후속 작업에서 이 결과를 쓴다.
    void OnPlayerDefeated(int slot)
    {
        if (closing) return;
        ulong loser = slot == 0 ? session.HostClientId : session.GuestClientId;
        ulong winner = slot == 0 ? session.GuestClientId : session.HostClientId;
        Debug.Log($"[Lobby] Player defeated code={session.RoomCode} slot={slot} loser={loser} winner={winner} phase={session.Phase}");
    }

    void OnDisconnected(ulong clientId)
    {
        if (closing) return;
        if (serverMode)
        {
            reservations.Remove(clientId);
            if (!session.Contains(clientId))
            {
                session.CancelReservation(clientId);
                return;
            }
            CloseServerRoom(clientId);
            return;
        }
        if (connecting || inLobby)
            StopClient(string.IsNullOrEmpty(manager.DisconnectReason) ? ConnectionFailed : manager.DisconnectReason, false);
    }

    void CloseServerRoom(ulong departingClient)
    {
        string code = session.RoomCode;
        session.Disconnect(departingClient);
        PrepareServerShutdown(true);
        // 종료 중 콜백의 IsServer 값이 바뀌어도 클라이언트 이벤트로 오인하지 않는다.
        foreach (ulong clientId in new List<ulong>(manager.ConnectedClientsIds))
            if (clientId != departingClient) manager.DisconnectClient(clientId, RoomClosed);
        Debug.Log($"[Lobby] Closed code={code} departing={departingClient} reason={RoomClosed} playerobjects=0");
        manager.Shutdown();
    }

    void PrepareServerShutdown(bool shouldRestart)
    {
        // 연결을 끊기 전에 플레이어를 디스폰하고 맵을 지워 새 방이 빈 상태에서 시작하게 한다.
        if (arena != null) arena.Clear();
        readyClients.Clear();
        serverSettingsRevision = 0;
        reservations.Clear();
        closing = true;
        restartServer = shouldRestart;
        UnregisterMessages();
    }

    void ExpireReservations()
    {
        float now = Time.realtimeSinceStartup;
        expiredReservations.Clear();
        foreach (var reservation in reservations)
        {
            if (now >= reservation.Value) expiredReservations.Add(reservation.Key);
        }
        foreach (ulong clientId in expiredReservations)
        {
            reservations.Remove(clientId);
            session.CancelReservation(clientId);
            Debug.Log($"[Lobby] Reservation expired client={clientId} room={session.RoomCode}");
            if (manager.ConnectedClients.ContainsKey(clientId)) manager.DisconnectClient(clientId, "The connection timed out.");
        }
    }

    void StopClient(string reason, bool explicitlyCancelled)
    {
        if (closing) return;
        notifyClosed = inLobby;
        disconnectReason = explicitlyCancelled ? string.Empty : reason;
        connecting = false;
        inLobby = false;
        startPending = false;
        closing = true;
        Debug.Log($"[Lobby] Connection ended joined={notifyClosed} cancelled={explicitlyCancelled} reason={reason}");
        UnregisterMessages();
        if (!manager.ShutdownInProgress) manager.Shutdown();
    }

    void OnTransportFailure()
    {
        if (closing) return;
        if (serverMode)
        {
            if (session.HasRoom) session.Disconnect(session.HostClientId);
            PrepareServerShutdown(manager.IsListening);
            Debug.LogError($"[Lobby] Server transport failed; restart={restartServer}.");
        }
        else if (connecting || inLobby) StopClient(ConnectionFailed, false);
    }

    void OnDestroy()
    {
        if (manager == null) return;
        manager.OnClientConnectedCallback -= OnConnected;
        manager.OnClientDisconnectCallback -= OnDisconnected;
        manager.OnTransportFailure -= OnTransportFailure;
        manager.ConnectionApprovalCallback -= ApproveConnection;
        UnregisterMessages();
        if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
    }
}
