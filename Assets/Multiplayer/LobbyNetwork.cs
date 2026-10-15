using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Main의 대기실만 연결한다. 서버에서는 맵이나 플레이어를 생성하지 않는다.
[DefaultExecutionOrder(-1000)]
public sealed class LobbyNetwork : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] UnityTransport transport;
    [SerializeField] string serverAddress = "127.0.0.1";
    [SerializeField] ushort port = 7777;
    [SerializeField] GameObject[] clientPresentation;

    const string SnapshotMessage = "WizardDuel.LobbySnapshot";
    const string SettingsMessage = "WizardDuel.LobbySettings";
    const string ErrorMessage = "WizardDuel.LobbyError";
    const string RoomClosed = "A player left, so the room was closed.";
    const string InvalidRequest = "The lobby request is invalid.";
    const string ConnectionFailed = "Disconnected from the server.";
    const int BufferSize = 256;
    readonly LobbySession session = new LobbySession();
    readonly Dictionary<ulong, float> reservations = new Dictionary<ulong, float>();
    readonly List<ulong> expiredReservations = new List<ulong>();
    bool serverMode;
    bool connecting;
    bool closing;
    bool restartServer;
    bool inLobby;
    bool createdRoom;
    bool notifyClosed;
    string requestedCode;
    string closeReason;
    string failureReason;
    float snapshotDeadline;
    NetworkLobbySnapshot currentSnapshot;
    uint requestedSettingsRevision;
    uint settledSettingsRevision;
    uint serverSettingsRevision;

    public event Action<NetworkLobbySnapshot> SnapshotChanged;
    public event Action<string> Failed;
    public event Action<string> Closed;
    public event Action<string> SettingsRejected;
    public bool IsBusy => connecting || closing || inLobby || (manager != null && manager.ShutdownInProgress);
    public bool IsInLobby => inLobby;
    public bool IsHost => inLobby && currentSnapshot.HostClientId == manager.LocalClientId;
    public bool HasPendingSettings => inLobby && requestedSettingsRevision > settledSettingsRevision;

    void Awake()
    {
        serverMode = Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0;
        Application.targetFrameRate = serverMode ? 30 : 60;
        if (serverMode && clientPresentation != null)
            foreach (GameObject presentation in clientPresentation)
                if (presentation != null) presentation.SetActive(false);
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
        int join = Array.IndexOf(args, "-join") + 1;
        if (join > 0 && join < args.Length) serverAddress = args[join].Trim();
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
        string failed = failureReason;
        string closed = closeReason;
        bool wasInLobby = notifyClosed;
        failureReason = null;
        closeReason = null;
        notifyClosed = false;
        if (wasInLobby) Closed?.Invoke(closed ?? string.Empty);
        else if (!string.IsNullOrEmpty(failed)) Failed?.Invoke(failed);
    }

    public void Create(DuelSettings settings)
    {
        if (serverMode || IsBusy) return;
        if (!DuelSettings.ValidateSettings(settings, out string reason))
        {
            Failed?.Invoke(reason);
            return;
        }
        StartRequest(new LobbyJoinRequest(true, string.Empty, settings));
    }

    public void Join(string code)
    {
        if (serverMode || IsBusy) return;
        if (!LobbySession.IsValidCode(code))
        {
            Failed?.Invoke("Enter a six-digit room code.");
            return;
        }
        StartRequest(new LobbyJoinRequest(false, code, null));
    }

    public void UpdateSettings(DuelSettings settings)
    {
        if (!IsInLobby || closing) return;
        if (!DuelSettings.ValidateSettings(settings, out string reason))
        {
            Debug.Log($"[Lobby] Settings rejected: {reason}");
            SettingsRejected?.Invoke(reason);
            return;
        }
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteValueSafe(++requestedSettingsRevision);
        writer.WriteNetworkSerializable(new NetworkLobbySettings(settings));
        manager.CustomMessagingManager.SendNamedMessage(SettingsMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
    }

    public void Cancel()
    {
        if (serverMode || closing || (!connecting && !inLobby)) return;
        StopClient(string.Empty, true);
    }

    public void Leave() => Cancel();

    void StartRequest(LobbyJoinRequest request)
    {
        if (manager == null || transport == null)
        {
            Failed?.Invoke("The lobby network is not configured.");
            return;
        }
        if (manager.IsListening || manager.ShutdownInProgress) return;
        createdRoom = request.CreateRoom;
        requestedCode = request.Code;
        requestedSettingsRevision = 0;
        settledSettingsRevision = 0;
        currentSnapshot = default;
        connecting = true;
        snapshotDeadline = Time.realtimeSinceStartup + manager.NetworkConfig.ClientConnectionBufferTimeout + 5;
        using var writer = new FastBufferWriter(BufferSize, Allocator.Temp);
        writer.WriteNetworkSerializable(request);
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
        Debug.Log($"[Lobby] Listening UDP={port} room=none playerobjects=0 mapgenerated=false");
    }

    void RegisterMessages()
    {
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SnapshotMessage, OnSnapshot);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SettingsMessage, OnSettings);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(ErrorMessage, OnError);
    }

    void UnregisterMessages()
    {
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(SnapshotMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(SettingsMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(ErrorMessage);
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
            bool approved = payload.CreateRoom
                ? session.TryCreate(request.ClientNetworkId, payload.Settings.ToSettings(), out string reason)
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
            if (session.Contains(clientId))
                manager.CustomMessagingManager.SendNamedMessage(SnapshotMessage, clientId, writer,
                    NetworkDelivery.ReliableSequenced);
        Debug.Log($"[Lobby] Snapshot code={snapshot.Code} host={snapshot.HostClientId} members={(snapshot.OpponentPresent ? 2 : 1)} revision={snapshot.SettingsRevision} seed={snapshot.Settings.Map.Seed} settings={JsonUtility.ToJson(snapshot.Settings)} playerobjects={manager.SpawnManager.PlayerObjects.Count} mapgenerated=false");
    }

    void OnSnapshot(ulong senderId, FastBufferReader reader)
    {
        if (serverMode || closing || (!connecting && !inLobby) || senderId != NetworkManager.ServerClientId) return;
        NetworkLobbySnapshot snapshot;
        try
        {
            if (reader.Length - reader.Position > BufferSize) throw new InvalidOperationException(InvalidRequest);
            reader.ReadNetworkSerializable(out snapshot);
            ulong localId = manager.LocalClientId;
            bool isHost = snapshot.HostClientId == localId;
            bool isGuest = snapshot.OpponentPresent && snapshot.GuestClientId == localId;
            if (reader.Position != reader.Length || !LobbySession.IsValidCode(snapshot.Code) ||
                !DuelSettings.ValidateSettings(snapshot.Settings.ToSettings(), out _) ||
                (!isHost && !isGuest) || (snapshot.OpponentPresent && snapshot.GuestClientId == snapshot.HostClientId) ||
                (!inLobby && (createdRoom != isHost || (!createdRoom && snapshot.Code != requestedCode))) ||
                (isHost && snapshot.SettingsRevision > requestedSettingsRevision) ||
                (inLobby && (snapshot.Code != currentSnapshot.Code || snapshot.HostClientId != currentSnapshot.HostClientId ||
                    snapshot.SettingsRevision < currentSnapshot.SettingsRevision)))
                throw new InvalidOperationException(InvalidRequest);
            connecting = false;
            inLobby = true;
            currentSnapshot = snapshot;
            settledSettingsRevision = Math.Max(settledSettingsRevision, snapshot.SettingsRevision);
            Debug.Log($"[Lobby] Joined code={snapshot.Code} host={isHost} members={(snapshot.OpponentPresent ? 2 : 1)} revision={snapshot.SettingsRevision} seed={snapshot.Settings.Map.Seed} playerobjects=0 mapgenerated=false");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid snapshot: {exception.Message}");
            StopClient(InvalidRequest, false);
            return;
        }
        SnapshotChanged?.Invoke(snapshot);
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
            reader.ReadNetworkSerializable(out NetworkLobbySettings settings);
            if (reader.Position != reader.Length) throw new InvalidOperationException(InvalidRequest);
            if (revision == 0 || revision <= serverSettingsRevision)
                reason = "The settings update is out of date.";
            else if (session.TryUpdateSettings(senderId, settings.ToSettings(), out reason))
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
        NetworkLobbySnapshot snapshot = session.Snapshot;
        session.Disconnect(departingClient);
        serverSettingsRevision = 0;
        reservations.Clear();
        closing = true;
        restartServer = true;
        // 종료 중 콜백의 IsServer 값이 바뀌어도 클라이언트 이벤트로 오인하지 않는다.
        foreach (ulong clientId in new List<ulong>(manager.ConnectedClientsIds))
            if (clientId != departingClient) manager.DisconnectClient(clientId, RoomClosed);
        Debug.Log($"[Lobby] Closed code={snapshot.Code} departing={departingClient} reason={RoomClosed} playerobjects=0");
        UnregisterMessages();
        manager.Shutdown();
    }

    void ExpireReservations()
    {
        expiredReservations.Clear();
        foreach (var reservation in reservations)
            if (Time.realtimeSinceStartup >= reservation.Value) expiredReservations.Add(reservation.Key);
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
        closeReason = explicitlyCancelled ? string.Empty : reason;
        failureReason = explicitlyCancelled || inLobby ? null : reason;
        connecting = false;
        inLobby = false;
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
            serverSettingsRevision = 0;
            reservations.Clear();
            UnregisterMessages();
            closing = true;
            restartServer = manager.IsListening;
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
