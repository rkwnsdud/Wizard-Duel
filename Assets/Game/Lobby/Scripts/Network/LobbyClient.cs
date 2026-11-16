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
    readonly Func<NetworkSessionMode> getMode;
    readonly Action<NetworkSessionMode> setMode;

    bool connecting;
    bool closing;
    bool inLobby;
    bool createdRoom;
    bool notifyClosed;
    NamedMessageGroup messageHandlers;
    string requestedCode;
    LobbyReason disconnectReason;
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
        Func<NetworkSessionMode> getMode,
        Action<NetworkSessionMode> setMode = null)
    {
        this.getManager = getManager;
        this.getTransport = getTransport;
        this.getArena = getArena;
        this.getMode = getMode;
        this.setMode = setMode;
    }

    public event Action<NetworkLobbySnapshot> SnapshotChanged;
    public event Action<LobbyReason> Failed;
    public event Action<LobbyReason> Closed;
    public event Action<LobbyReason> SettingsRejected;
    public event Action<LobbyReason> StartRejected;

    public bool IsBusy => connecting || closing || inLobby || (getManager() != null && getManager().ShutdownInProgress);
    public bool IsInLobby => inLobby;
    public bool IsHost => inLobby && currentSnapshot.HostClientId == getManager().LocalClientId;
    public bool HasPendingSettings => inLobby && requestedSettingsRevision > settledSettingsRevision;
    // 시작 요청의 답을 기다리거나 서버가 전투 준비를 알린 뒤다.
    public bool IsStarting => inLobby && (startPending || currentSnapshot.Phase != LobbyPhase.Lobby);
    public bool HasLocalPlayer => inLobby && getManager().LocalClient != null && getManager().LocalClient.PlayerObject != null;

    bool MapGenerated => getArena() != null && getArena().HasMap;
    NamedMessageGroup MessageHandlers => messageHandlers ??= new NamedMessageGroup(
        LobbyMessages.Snapshot.Bind(OnSnapshot),
        LobbyMessages.Error.Bind(OnError),
        LobbyMessages.StartRejected.Bind(OnStartRejected));

    public void Tick(float now)
    {
        NetworkManager manager = getManager();
        if (manager == null) return;
        if (connecting && !closing && now >= snapshotDeadline)
            StopClient(LobbyReason.ConnectionTimedOut, false);
        if (!closing || manager.IsListening || manager.ShutdownInProgress) return;

        closing = false;
        if (getMode != null && getMode() == NetworkSessionMode.OnlineClient)
            setMode?.Invoke(NetworkSessionMode.Offline);
        // 소환된 플레이어는 연결 종료 때 Netcode가 지운다. 시작 화면으로 돌아가는 프레임에 맵도 지운다.
        DuelArena arena = getArena();
        if (arena != null) arena.Clear();
        LobbyReason reason = disconnectReason;
        bool wasInLobby = notifyClosed;
        disconnectReason = LobbyReason.None;
        notifyClosed = false;
        if (wasInLobby) Closed?.Invoke(reason);
        else if (reason != LobbyReason.None) Failed?.Invoke(reason);
    }

    public void UpdateSettings(DuelSettings settings)
    {
        if (!IsInLobby || closing || currentSnapshot.Phase != LobbyPhase.Lobby) return;
        if (!DuelSettings.ValidateSettings(settings, out LobbyReason reason))
        {
            Debug.Log($"[Lobby] Settings rejected: {reason}");
            SettingsRejected?.Invoke(reason);
            return;
        }
        requestedSettingsRevision++;
        uint revision = requestedSettingsRevision;
        LobbyMessages.Settings.Send(getManager(), NetworkManager.ServerClientId, writer =>
        {
            writer.WriteValueSafe(revision);
            writer.WriteNetworkSerializable(new NetworkMapSettings(settings.Map));
        });
    }

    // 방장이 상대와 함께 로비 단계에 있을 때 한 번만 보낸다. 승인은 전투 준비 스냅샷으로, 거절은 같은 번호의 응답으로 온다.
    public bool RequestStart()
    {
        if (!IsHost || closing || startPending || !currentSnapshot.OpponentPresent ||
            currentSnapshot.Phase != LobbyPhase.Lobby) return false;
        requestedStart++;
        uint request = requestedStart;
        LobbyMessages.Start.Send(getManager(), NetworkManager.ServerClientId, writer => writer.WriteValueSafe(request));
        startPending = true;
        Debug.Log($"[Lobby] Start requested code={currentSnapshot.Code} request={requestedStart} " +
            $"revision={currentSnapshot.SettingsRevision} seed={currentSnapshot.Settings.Seed}");
        return true;
    }

    bool AllowsClientSession => getMode == null ||
        (getMode() != NetworkSessionMode.DedicatedServer && getMode() != NetworkSessionMode.LocalHost);

    public void Cancel()
    {
        if (!AllowsClientSession || closing || (!connecting && !inLobby)) return;
        StopClient(LobbyReason.None, true);
    }

    public void StartRequest(bool createRoom, string code, DuelSettings settings, string serverAddress, ushort port)
    {
        if (!AllowsClientSession || (getMode != null && getMode() != NetworkSessionMode.Offline) || IsBusy) return;
        LobbyReason reason = LobbyReason.InvalidRoomCode;
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
            Failed?.Invoke(LobbyReason.NetworkNotConfigured);
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
        snapshotDeadline = Time.realtimeSinceStartup + manager.NetworkConfig.ClientConnectionBufferTimeout +
            NetworkDuelConfig.ClientSnapshotTimeoutMarginSeconds;
        using var writer = new FastBufferWriter(LobbyMessages.BufferSize, Allocator.Temp);
        writer.WriteNetworkSerializable(new LobbyJoinRequest(createRoom, code, settings));
        manager.NetworkConfig.ConnectionData = writer.ToArray();
        NetworkDuelConfig.ConfigureClientTransport(transport, serverAddress, port);
        setMode?.Invoke(NetworkSessionMode.OnlineClient);
        Debug.Log($"[Lobby] Connecting address={serverAddress}:{port} create={createdRoom} code={requestedCode}");
        if (!manager.StartClient())
        {
            StopClient(LobbyReason.ConnectionStartFailed, false);
            return;
        }
        RegisterMessages();
    }

    public void RegisterMessages() => NetworkDuelConfig.RegisterMessages(getManager(), getArena(), MessageHandlers);

    public void UnregisterMessages() => NetworkDuelConfig.UnregisterMessages(getArena(), MessageHandlers);

    public void OnDisconnected()
    {
        if (closing) return;
        NetworkManager manager = getManager();
        if (connecting || inLobby)
            StopClient(LobbyReasonCodec.FromWireReason(manager.DisconnectReason, LobbyReason.Disconnected), false);
    }

    public void OnTransportFailure()
    {
        if (closing) return;
        if (connecting || inLobby) StopClient(LobbyReason.Disconnected, false);
    }

    void OnSnapshot(ulong senderId, FastBufferReader reader)
    {
        if (!AllowsClientSession || closing || (!connecting && !inLobby) || senderId != NetworkManager.ServerClientId) return;
        NetworkManager manager = getManager();
        NetworkLobbySnapshot snapshot = default;
        LobbyPhase previousPhase = currentSnapshot.Phase;
        uint previousRound = currentSnapshot.Round;
        if (!LobbyMessages.Snapshot.TryRead(reader, r =>
        {
            r.ReadNetworkSerializable(out snapshot);
            bool isHost = snapshot.HostClientId == manager.LocalClientId;
            // 시작된 방에는 새로 들어갈 수 없다. 한 판 안에서 단계는 되돌아가지 않고, 결과 뒤에만 다음 판 로비로 돌아간다.
            bool matchesRequest = createdRoom == isHost && (createdRoom || snapshot.Code == requestedCode) &&
                snapshot.Phase == LobbyPhase.Lobby;
            bool matchesCurrentRoom = snapshot.Follows(currentSnapshot);
            if (!snapshot.IsValidFor(manager.LocalClientId) ||
                !(inLobby ? matchesCurrentRoom : matchesRequest) || (isHost && snapshot.SettingsRevision > requestedSettingsRevision))
                throw new InvalidOperationException(nameof(LobbyReason.InvalidRequest));
            return true;
        }, "[Lobby] Invalid snapshot"))
        {
            StopClient(LobbyReason.InvalidRequest, false);
            return;
        }
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
        if (arena == null || !arena.Build(snapshot.Settings.ToSettings()))
        {
            StopClient(LobbyReason.MapGenerationFailed, false);
            return false;
        }
        LobbyMessages.MapReady.Send(getManager(), NetworkManager.ServerClientId, writer =>
        {
            writer.WriteValueSafe(snapshot.SettingsRevision);
            writer.WriteValueSafe(arena.Fingerprint);
        });
        Debug.Log($"[Lobby] Battle map ready code={snapshot.Code} revision={snapshot.SettingsRevision} " +
            $"seed={snapshot.Settings.Seed} fingerprint={arena.Fingerprint}");
        return true;
    }

    void OnError(ulong senderId, FastBufferReader reader)
    {
        if (!AllowsClientSession || closing || !inLobby || senderId != NetworkManager.ServerClientId) return;
        if (!TryReadRejection(LobbyMessages.Error, reader, requestedSettingsRevision, "[Lobby] Invalid error message",
            out uint revision, out LobbyReason reason)) return;
        settledSettingsRevision = Math.Max(settledSettingsRevision, revision);
        Debug.Log($"[Lobby] Settings rejected revision={revision}: {reason}");
        SettingsRejected?.Invoke(reason);
    }

    void OnStartRejected(ulong senderId, FastBufferReader reader)
    {
        if (!AllowsClientSession || closing || !inLobby || senderId != NetworkManager.ServerClientId) return;
        if (!TryReadRejection(LobbyMessages.StartRejected, reader, requestedStart, "[Lobby] Invalid start rejection",
            out uint request, out LobbyReason reason)) return;
        // 이미 승인되었거나 더 새 요청을 기다리는 중이면 늦게 온 거절을 무시한다.
        if (!startPending || request != requestedStart) return;
        startPending = false;
        Debug.Log($"[Lobby] Start rejected request={request}: {reason}");
        StartRejected?.Invoke(reason);
    }

    static bool TryReadRejection(NamedMessageChannel channel, FastBufferReader reader, uint maxSequence, string context,
        out uint sequence, out LobbyReason reason)
    {
        uint parsedSequence = 0;
        byte rawReason = 0;
        if (!channel.TryRead(reader, r =>
        {
            r.ReadValueSafe(out parsedSequence);
            r.ReadValueSafe(out rawReason);
            if (parsedSequence > maxSequence || !LobbyReasonCodec.IsValidRejectionCode(rawReason))
                throw new InvalidOperationException(nameof(LobbyReason.InvalidRequest));
            return true;
        }, context))
        {
            sequence = 0;
            reason = LobbyReason.None;
            return false;
        }
        sequence = parsedSequence;
        reason = (LobbyReason)rawReason;
        return true;
    }

    void StopClient(LobbyReason reason, bool explicitlyCancelled)
    {
        if (closing) return;
        NetworkManager manager = getManager();
        notifyClosed = inLobby;
        disconnectReason = explicitlyCancelled ? LobbyReason.None : reason;
        connecting = false;
        inLobby = false;
        startPending = false;
        closing = true;
        Debug.Log($"[Lobby] Connection ended joined={notifyClosed} cancelled={explicitlyCancelled} reason={reason}");
        UnregisterMessages();
        if (!manager.ShutdownInProgress) manager.Shutdown();
    }
}
