using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// 로비와 전투 준비의 클라이언트 측 역할을 맡는다. 접속, 설정·시작 요청, 스냅샷 적용, UI 이벤트를 처리한다.
public sealed class LobbyClient
{
    readonly Func<NetworkManager> getManager;
    readonly Func<UnityTransport> getTransport;
    readonly Func<DuelArena> getArena;
    readonly Func<bool> isServerMode;

    bool connecting;
    bool closing;
    bool inLobby;
    bool createdRoom;
    bool notifyClosed;
    bool messagesRegistered;
    string requestedCode;
    string disconnectReason;
    float snapshotDeadline;
    NetworkLobbySnapshot currentSnapshot;
    uint requestedSettingsRevision;
    uint settledSettingsRevision;
    // 방장이 보낸 마지막 시작 요청 번호. 거절 응답은 같은 번호일 때만 받아들인다.
    uint requestedStart;
    bool startPending;

    public LobbyClient(
        Func<NetworkManager> getManager,
        Func<UnityTransport> getTransport,
        Func<DuelArena> getArena,
        Func<bool> isServerMode)
    {
        this.getManager = getManager;
        this.getTransport = getTransport;
        this.getArena = getArena;
        this.isServerMode = isServerMode;
    }

    public event Action<NetworkLobbySnapshot> SnapshotChanged;
    public event Action<string> Failed;
    public event Action<string> Closed;
    public event Action<string> SettingsRejected;
    public event Action<string> StartRejected;

    public bool IsBusy => connecting || closing || inLobby || (getManager() != null && getManager().ShutdownInProgress);
    public bool IsInLobby => inLobby;
    public bool IsHost => inLobby && currentSnapshot.HostClientId == getManager().LocalClientId;
    public bool HasPendingSettings => inLobby && requestedSettingsRevision > settledSettingsRevision;
    // 시작 요청의 답을 기다리거나 서버가 전투 준비를 알린 뒤다.
    public bool IsStarting => inLobby && (startPending || currentSnapshot.Phase != LobbyPhase.Lobby);
    public bool HasLocalPlayer => inLobby && getManager().LocalClient != null && getManager().LocalClient.PlayerObject != null;

    bool MapGenerated => getArena() != null && getArena().HasMap;

    public void Tick(float now)
    {
        NetworkManager manager = getManager();
        if (manager == null) return;
        if (connecting && !closing && now >= snapshotDeadline)
            StopClient("The connection timed out.", false);
        if (!closing || manager.IsListening || manager.ShutdownInProgress) return;

        closing = false;
        // 소환된 플레이어는 연결 종료 때 Netcode가 지운다. 시작 화면으로 돌아가는 프레임에 맵도 지운다.
        DuelArena arena = getArena();
        if (arena != null) arena.Clear();
        string reason = disconnectReason;
        bool wasInLobby = notifyClosed;
        disconnectReason = null;
        notifyClosed = false;
        if (wasInLobby) Closed?.Invoke(reason ?? string.Empty);
        else if (!string.IsNullOrEmpty(reason)) Failed?.Invoke(reason);
    }

    public void UpdateSettings(DuelSettings settings)
    {
        if (!IsInLobby || closing || currentSnapshot.Phase != LobbyPhase.Lobby) return;
        if (!DuelSettings.ValidateSettings(settings, out string reason))
        {
            Debug.Log($"[Lobby] Settings rejected: {reason}");
            SettingsRejected?.Invoke(reason);
            return;
        }
        NetworkManager manager = getManager();
        using var writer = new FastBufferWriter(LobbyNetwork.BufferSize, Allocator.Temp);
        requestedSettingsRevision++;
        writer.WriteValueSafe(requestedSettingsRevision);
        writer.WriteNetworkSerializable(new NetworkMapSettings(settings.Map));
        manager.CustomMessagingManager.SendNamedMessage(LobbyNetwork.SettingsMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
    }

    // 방장이 상대와 함께 로비 단계에 있을 때 한 번만 보낸다. 승인은 전투 준비 스냅샷으로, 거절은 같은 번호의 응답으로 온다.
    public bool RequestStart()
    {
        if (!IsHost || closing || startPending || !currentSnapshot.OpponentPresent ||
            currentSnapshot.Phase != LobbyPhase.Lobby) return false;
        NetworkManager manager = getManager();
        using var writer = new FastBufferWriter(LobbyNetwork.BufferSize, Allocator.Temp);
        requestedStart++;
        writer.WriteValueSafe(requestedStart);
        manager.CustomMessagingManager.SendNamedMessage(LobbyNetwork.StartMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
        startPending = true;
        Debug.Log($"[Lobby] Start requested code={currentSnapshot.Code} request={requestedStart} " +
            $"revision={currentSnapshot.SettingsRevision} seed={currentSnapshot.Settings.Seed}");
        return true;
    }

    bool IsServerMode => isServerMode != null && isServerMode();

    public void Cancel()
    {
        if (IsServerMode || closing || (!connecting && !inLobby)) return;
        StopClient(string.Empty, true);
    }

    public void StartRequest(bool createRoom, string code, DuelSettings settings, string serverAddress, ushort port)
    {
        if (IsServerMode || IsBusy) return;
        string reason = "Enter a six-digit room code.";
        bool isValid = createRoom ? DuelSettings.ValidateSettings(settings, out reason) : LobbySession.IsValidCode(code);
        if (!isValid)
        {
            Failed?.Invoke(reason);
            return;
        }
        NetworkManager manager = getManager();
        UnityTransport transport = getTransport();
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
        using var writer = new FastBufferWriter(LobbyNetwork.BufferSize, Allocator.Temp);
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

    public void RegisterMessages()
    {
        NetworkManager manager = getManager();
        DuelArena arena = getArena();
        if (arena != null && manager != null) arena.RegisterWeaponMessages(manager);
        if (messagesRegistered || manager == null || manager.CustomMessagingManager == null) return;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyNetwork.SnapshotMessage, OnSnapshot);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyNetwork.ErrorMessage, OnError);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(LobbyNetwork.StartRejectedMessage, OnStartRejected);
        messagesRegistered = true;
    }

    public void UnregisterMessages()
    {
        NetworkManager manager = getManager();
        DuelArena arena = getArena();
        if (arena != null) arena.UnregisterWeaponMessages();
        if (messagesRegistered && manager != null && manager.CustomMessagingManager != null)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyNetwork.SnapshotMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyNetwork.ErrorMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(LobbyNetwork.StartRejectedMessage);
        }
        messagesRegistered = false;
    }

    public void OnDisconnected()
    {
        if (closing) return;
        NetworkManager manager = getManager();
        if (connecting || inLobby)
            StopClient(string.IsNullOrEmpty(manager.DisconnectReason) ? LobbyNetwork.ConnectionFailed : manager.DisconnectReason, false);
    }

    public void OnTransportFailure()
    {
        if (closing) return;
        if (connecting || inLobby) StopClient(LobbyNetwork.ConnectionFailed, false);
    }

    void OnSnapshot(ulong senderId, FastBufferReader reader)
    {
        if (IsServerMode || closing || (!connecting && !inLobby) || senderId != NetworkManager.ServerClientId) return;
        NetworkManager manager = getManager();
        NetworkLobbySnapshot snapshot;
        LobbyPhase previousPhase = currentSnapshot.Phase;
        uint previousRound = currentSnapshot.Round;
        try
        {
            if (reader.Length - reader.Position > LobbyNetwork.BufferSize)
                throw new InvalidOperationException(LobbyNetwork.InvalidRequest);
            reader.ReadNetworkSerializable(out snapshot);
            bool isHost = snapshot.HostClientId == manager.LocalClientId;
            // 시작된 방에는 새로 들어갈 수 없다. 한 판 안에서 단계는 되돌아가지 않고, 결과 뒤에만 다음 판 로비로 돌아간다.
            bool matchesRequest = createdRoom == isHost && (createdRoom || snapshot.Code == requestedCode) &&
                snapshot.Phase == LobbyPhase.Lobby;
            bool matchesCurrentRoom = snapshot.Follows(currentSnapshot);
            if (reader.Position != reader.Length || !snapshot.IsValidFor(manager.LocalClientId) ||
                !(inLobby ? matchesCurrentRoom : matchesRequest) || (isHost && snapshot.SettingsRevision > requestedSettingsRevision))
                throw new InvalidOperationException(LobbyNetwork.InvalidRequest);
            connecting = false;
            inLobby = true;
            currentSnapshot = snapshot;
            settledSettingsRevision = Math.Max(settledSettingsRevision, snapshot.SettingsRevision);
            if (snapshot.Phase != LobbyPhase.Lobby) startPending = false;
            Debug.Log($"[Lobby] Joined code={snapshot.Code} host={IsHost} " +
                $"members={(snapshot.OpponentPresent ? 2 : 1)} round={snapshot.Round} phase={snapshot.Phase} " +
                $"winner={(snapshot.Phase == LobbyPhase.Result ? snapshot.WinnerSlot.ToString() : "none")} " +
                $"revision={snapshot.SettingsRevision} seed={snapshot.Settings.Seed} " +
                $"playerobjects={manager.SpawnManager?.PlayerObjects.Count ?? 0} mapgenerated={MapGenerated}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid snapshot: {exception.Message}");
            StopClient(LobbyNetwork.InvalidRequest, false);
            return;
        }
        DuelArena arena = getArena();
        // 다음 판 로비로 돌아왔다. 플레이어는 서버가 디스폰하므로 여기서는 이번 판의 전투 맵만 지운다.
        if (snapshot.Round != previousRound && arena != null) arena.Clear();
        if (previousPhase == LobbyPhase.Lobby && snapshot.Phase != LobbyPhase.Lobby && !SendMapReady(snapshot)) return;
        SnapshotChanged?.Invoke(snapshot);
    }

    // 전투 준비를 처음 알게 되면 서버가 보낸 설정 그대로 맵을 만들고, 지문을 서버에 보내 비교받는다.
    bool SendMapReady(NetworkLobbySnapshot snapshot)
    {
        DuelArena arena = getArena();
        NetworkManager manager = getManager();
        if (arena == null || !arena.Build(snapshot.Settings.ToSettings()))
        {
            StopClient(LobbyNetwork.MapFailed, false);
            return false;
        }
        using var writer = new FastBufferWriter(LobbyNetwork.BufferSize, Allocator.Temp);
        writer.WriteValueSafe(snapshot.SettingsRevision);
        writer.WriteValueSafe(arena.Fingerprint);
        manager.CustomMessagingManager.SendNamedMessage(LobbyNetwork.MapReadyMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
        Debug.Log($"[Lobby] Battle map ready code={snapshot.Code} revision={snapshot.SettingsRevision} " +
            $"seed={snapshot.Settings.Seed} fingerprint={arena.Fingerprint}");
        return true;
    }

    void OnError(ulong senderId, FastBufferReader reader)
    {
        if (IsServerMode || closing || !inLobby || senderId != NetworkManager.ServerClientId) return;
        uint revision;
        string reason;
        try
        {
            if (reader.Length - reader.Position > LobbyNetwork.BufferSize)
                throw new InvalidOperationException(LobbyNetwork.InvalidRequest);
            reader.ReadValueSafe(out revision);
            reader.ReadValueSafe(out reason);
            if (reader.Position != reader.Length || revision > requestedSettingsRevision)
                throw new InvalidOperationException(LobbyNetwork.InvalidRequest);
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

    void OnStartRejected(ulong senderId, FastBufferReader reader)
    {
        if (IsServerMode || closing || !inLobby || senderId != NetworkManager.ServerClientId) return;
        uint request;
        string reason;
        try
        {
            if (reader.Length - reader.Position > LobbyNetwork.BufferSize)
                throw new InvalidOperationException(LobbyNetwork.InvalidRequest);
            reader.ReadValueSafe(out request);
            reader.ReadValueSafe(out reason);
            if (reader.Position != reader.Length || request > requestedStart)
                throw new InvalidOperationException(LobbyNetwork.InvalidRequest);
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

    void StopClient(string reason, bool explicitlyCancelled)
    {
        if (closing) return;
        NetworkManager manager = getManager();
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
}
