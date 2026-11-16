using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// 로비와 전투의 서버 측 역할을 맡는다. 접속 승인, 방 관리, 시작 검증, 맵 준비, 스냅샷 전송을 처리한다.
public sealed class LobbyServer
{
    readonly Func<NetworkManager> getManager;
    readonly Func<UnityTransport> getTransport;
    readonly Func<DuelArena> getArena;
    readonly Func<int> getMaxRooms;
    readonly Func<float> getResultDuration;
    readonly Dictionary<ulong, float> reservations = new Dictionary<ulong, float>();
    readonly List<ulong> expiredReservations = new List<ulong>();

    LobbyDirectory directory;
    bool closing;
    bool restartRequested;
    NamedMessageGroup messageHandlers;

    public LobbyServer(
        Func<NetworkManager> getManager,
        Func<UnityTransport> getTransport,
        Func<DuelArena> getArena,
        Func<int> getMaxRooms,
        Func<float> getResultDuration)
    {
        this.getManager = getManager;
        this.getTransport = getTransport;
        this.getArena = getArena;
        this.getMaxRooms = getMaxRooms;
        this.getResultDuration = getResultDuration;
    }

    public bool IsClosing => closing;

    LobbyDirectory Directory => directory ??= new LobbyDirectory(Mathf.Max(1, getMaxRooms()));

    NamedMessageGroup MessageHandlers => messageHandlers ??= new NamedMessageGroup(
        LobbyMessages.Settings.Bind(OnSettings),
        LobbyMessages.Start.Bind(OnStart),
        LobbyMessages.MapReady.Bind(OnMapReady));

    public void Start(ushort port)
    {
        NetworkManager manager = getManager();
        UnityTransport transport = getTransport();
        if (manager == null || transport == null || manager.IsListening || manager.ShutdownInProgress) return;
        NetworkDuelConfig.ConfigureServerTransport(transport, port);
        if (!manager.StartServer())
        {
            Debug.LogError($"[Lobby] Could not listen on UDP {port}.");
            return;
        }
        RegisterMessages();
        Debug.Log($"[Lobby] Listening UDP={port} rooms={Directory.Count} maxrooms={getMaxRooms()} " +
            $"playerobjects={manager.SpawnManager.PlayerObjects.Count}");
    }

    public void Tick(float now, ushort port)
    {
        NetworkManager manager = getManager();
        if (manager == null) return;
        if (manager.IsListening && !closing)
        {
            ExpireReservations(now);
            IReadOnlyList<LobbyRoom> rooms = Directory.Rooms;
            for (int i = rooms.Count - 1; i >= 0; i--)
            {
                LobbyRoom room = rooms[i];
                if (room.Session.Phase == LobbyPhase.Result && now >= room.ResultDeadline)
                    ReturnToLobby(room);
            }
        }
        if (!closing || manager.IsListening || manager.ShutdownInProgress) return;
        closing = false;
        if (restartRequested)
        {
            restartRequested = false;
            Start(port);
        }
    }

    public void RegisterMessages() => NetworkDuelConfig.RegisterMessages(getManager(), getArena(), MessageHandlers);

    public void UnregisterMessages() => NetworkDuelConfig.UnregisterMessages(getArena(), MessageHandlers);

    public void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        NetworkManager manager = getManager();
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Approved = false;
        response.Reason = LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest);
        if (closing || request.Payload == null || request.Payload.Length > LobbyMessages.BufferSize) return;
        ExpireReservations(Time.realtimeSinceStartup);
        try
        {
            using var reader = new FastBufferReader(request.Payload, Allocator.Temp);
            reader.ReadNetworkSerializable(out LobbyJoinRequest payload);
            if (reader.Position != reader.Length || payload.Version != LobbyJoinRequest.CurrentVersion) return;
            LobbyReason reason;
            LobbyRoom room;
            bool approved = payload.CreateRoom
                ? Directory.TryCreate(request.ClientNetworkId, new DuelSettings { Map = payload.Settings.ToSettings() }, out room, out reason)
                : Directory.TryJoin(request.ClientNetworkId, payload.Code, out room, out reason);
            response.Approved = approved;
            response.Reason = approved ? string.Empty : LobbyReasonCodec.ToWireReason(reason);
            if (approved)
                reservations[request.ClientNetworkId] = Time.realtimeSinceStartup +
                    manager.NetworkConfig.ClientConnectionBufferTimeout + NetworkDuelConfig.ServerReservationTimeoutMarginSeconds;
            Debug.Log($"[Lobby] Approval client={request.ClientNetworkId} create={payload.CreateRoom} approved={approved} " +
                $"code={(approved ? room.Code : "none")} rooms={Directory.Count} reason={(approved ? LobbyReason.None : reason)}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Lobby] Invalid approval client={request.ClientNetworkId}: {exception.Message}");
        }
    }

    public void OnConnected(ulong clientId)
    {
        if (closing) return;
        NetworkManager manager = getManager();
        reservations.Remove(clientId);
        // 접속을 끝내기 전에 방장이 나가 방이 닫혔다면 예약도 함께 사라졌다.
        if (!Directory.Confirm(clientId, out LobbyRoom room))
        {
            manager.DisconnectClient(clientId, LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest));
            return;
        }
        BroadcastSnapshot(room);
    }

    public void OnDisconnected(ulong clientId)
    {
        if (closing) return;
        reservations.Remove(clientId);
        LobbyRoom room = Directory.RoomOf(clientId);
        if (room == null)
        {
            Directory.CancelReservation(clientId);
            return;
        }
        CloseRoom(room, clientId);
    }

    public void OnTransportFailure()
    {
        if (closing) return;
        NetworkManager manager = getManager();
        PrepareShutdown(manager != null && manager.IsListening);
        Debug.LogError($"[Lobby] Server transport failed; restart={restartRequested}.");
    }

    // 서버: 그 방의 방장과 참가자에게만 보낸다.
    void BroadcastSnapshot(LobbyRoom room)
    {
        NetworkManager manager = getManager();
        LobbySession session = room.Session;
        NetworkLobbySnapshot snapshot = session.Snapshot;
        snapshot.SettingsRevision = room.SettingsRevision;
        Span<ulong> recipients = stackalloc ulong[session.OpponentPresent ? 2 : 1];
        recipients[0] = session.HostClientId;
        if (session.OpponentPresent) recipients[1] = session.GuestClientId;
        LobbyMessages.Snapshot.SendToRecipients(manager, recipients,
            clientId => session.Contains(clientId) && manager.ConnectedClients.ContainsKey(clientId),
            writer => writer.WriteNetworkSerializable(snapshot));
        Debug.Log($"[Lobby] Snapshot code={snapshot.Code} host={snapshot.HostClientId} " +
            $"members={(snapshot.OpponentPresent ? 2 : 1)} round={snapshot.Round} phase={snapshot.Phase} " +
            $"winner={(snapshot.Phase == LobbyPhase.Result ? snapshot.WinnerSlot.ToString() : "none")} revision={snapshot.SettingsRevision} " +
            $"seed={snapshot.Settings.Seed} settings={JsonUtility.ToJson(snapshot.Settings)} " +
            $"players={room.Match?.PlayerCount ?? 0} rooms={Directory.Count} playerobjects={manager.SpawnManager.PlayerObjects.Count}");
    }

    void OnSettings(ulong senderId, FastBufferReader reader)
    {
        LobbyRoom room = RoomOf(senderId);
        if (room == null) return;
        uint revision = 0;
        NetworkMapSettings mapSettings = default;
        LobbyReason reason = LobbyReason.InvalidRequest;
        if (LobbyMessages.Settings.TryRead(reader, r =>
        {
            r.ReadValueSafe(out revision);
            r.ReadNetworkSerializable(out mapSettings);
            return true;
        }, $"[Lobby] Invalid settings client={senderId}"))
        {
            if (revision == 0 || revision <= room.SettingsRevision)
                reason = LobbyReason.SettingsOutOfDate;
            else if (room.Session.TryUpdateSettings(senderId, new DuelSettings { Map = mapSettings.ToSettings() }, out reason))
            {
                room.SettingsRevision = revision;
                BroadcastSnapshot(room);
                return;
            }
        }
        LobbyMessages.Error.Send(getManager(), senderId, writer =>
        {
            writer.WriteValueSafe(revision);
            writer.WriteValueSafe((byte)reason);
        });
    }

    // 서버: 방장만, 두 명이 모두 확정 참가한 로비 단계에서만 시작을 받는다. 맵은 저장된 설정 그대로 만든다.
    void OnStart(ulong senderId, FastBufferReader reader)
    {
        LobbyRoom room = RoomOf(senderId);
        if (room == null) return;
        uint request = 0;
        LobbyReason reason = LobbyReason.InvalidRequest;
        if (LobbyMessages.Start.TryRead(reader, r =>
        {
            r.ReadValueSafe(out request);
            return true;
        }, $"[Lobby] Invalid start client={senderId}"))
        {
            if (room.Session.TryStart(senderId, out reason))
            {
                if (PrepareMatch(room))
                {
                    BroadcastSnapshot(room);
                    return;
                }
                room.Session.CancelStart();
                reason = LobbyReason.MapGenerationFailed;
            }
        }
        Debug.Log($"[Lobby] Start rejected code={room.Code} client={senderId} request={request} phase={room.Session.Phase}: {reason}");
        LobbyMessages.StartRejected.Send(getManager(), senderId, writer =>
        {
            writer.WriteValueSafe(request);
            writer.WriteValueSafe((byte)reason);
        });
    }

    // 서버: 보낸 클라이언트가 접속을 끝낸 구성원인 방. 닫는 중이거나 방이 없으면 null이다.
    LobbyRoom RoomOf(ulong clientId) => !closing ? Directory.RoomOf(clientId) : null;

    // 서버: 그 방의 설정으로 맵을 만들어 지문과 스폰 자리를 받아 둔다. 서버에는 방별 맵이 남지 않는다.
    // 맵을 만들다 예외가 나도 실패로 돌려준다. 예외가 OnStart까지 올라가면 시작이 취소되지 않아 방이 준비 단계에 갇힌다.
    bool PrepareMatch(LobbyRoom room)
    {
        DuelArena arena = getArena();
        room.ReadyClients.Clear();
        NetworkLobbySnapshot snapshot = room.Session.Snapshot;
        try
        {
            room.Match = arena != null ? arena.Prepare(snapshot.Settings.ToSettings()) : null;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[Lobby] Battle prepare failed code={snapshot.Code} seed={snapshot.Settings.Seed}: {exception}");
            room.Match = null;
        }
        Debug.Log($"[Lobby] Battle preparing code={snapshot.Code} round={snapshot.Round} revision={room.SettingsRevision} " +
            $"seed={snapshot.Settings.Seed} mapgenerated={room.Match != null} fingerprint={room.Match?.Fingerprint ?? "none"}");
        return room.Match != null;
    }

    // 서버: 두 클라이언트의 맵 지문이 서버와 같아야 플레이어를 소환한다. 다르면 그 접속을 끊어 방을 닫는다.
    void OnMapReady(ulong senderId, FastBufferReader reader)
    {
        LobbyRoom room = RoomOf(senderId);
        if (room == null || room.Match == null || room.Session.Phase != LobbyPhase.Preparing) return;
        NetworkManager manager = getManager();
        DuelArena arena = getArena();
        LobbySession session = room.Session;
        uint revision = 0;
        string fingerprint = null;
        if (!LobbyMessages.MapReady.TryRead(reader, r =>
        {
            r.ReadValueSafe(out revision);
            r.ReadValueSafe(out fingerprint);
            return true;
        }, $"[Lobby] Invalid map ready client={senderId}"))
        {
            manager.DisconnectClient(senderId, LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest));
            return;
        }
        if (revision != room.SettingsRevision || fingerprint != room.Match.Fingerprint)
        {
            Debug.LogError($"[Lobby] Client {senderId} map does not match the server code={room.Code} " +
                $"revision={revision}/{room.SettingsRevision} fingerprint={fingerprint}");
            manager.DisconnectClient(senderId, LobbyReasonCodec.ToWireReason(LobbyReason.MapMismatch));
            return;
        }
        if (!room.ReadyClients.Add(senderId)) return;
        Debug.Log($"[Lobby] Client {senderId} map ready code={room.Code} ({room.ReadyClients.Count}/2) fingerprint={fingerprint}");
        if (!room.ReadyClients.Contains(session.HostClientId) || !room.ReadyClients.Contains(session.GuestClientId)) return;
        arena.SpawnPlayers(room.Match, session.HostClientId, session.GuestClientId,
            () => OnCombatEnabled(room), slot => OnPlayerDefeated(room, slot));
        Debug.Log($"[Lobby] Players spawned code={room.Code} round={session.Round} " +
            $"playerobjects={manager.SpawnManager.PlayerObjects.Count}");
    }

    void OnCombatEnabled(LobbyRoom room)
    {
        if (closing || !room.Session.BeginBattle()) return;
        BroadcastSnapshot(room);
    }

    // 먼저 쓰러진 플레이어 한 명만 온다. 그 방 두 플레이어의 입력을 끄고 결과 단계를 알린다.
    // 플레이어는 승자를 보여 주는 동안 그대로 두고, resultDuration 뒤 Update에서 정리한다.
    void OnPlayerDefeated(LobbyRoom room, int slot)
    {
        if (closing) return;
        DuelArena arena = getArena();
        LobbySession session = room.Session;
        ulong loser = slot == 0 ? session.HostClientId : session.GuestClientId;
        ulong winner = slot == 0 ? session.GuestClientId : session.HostClientId;
        Debug.Log($"[Lobby] Player defeated code={room.Code} round={session.Round} slot={slot} loser={loser} " +
            $"winner={winner} phase={session.Phase}");
        if (!session.TryFinishBattle(slot)) return;
        arena.EndCombat(room.Match);
        room.ResultDeadline = Time.realtimeSinceStartup + getResultDuration();
        BroadcastSnapshot(room);
    }

    // 서버: 결과를 다 보여 주면 이번 판의 플레이어를 지우고, 방 코드·역할·설정을 그대로 둔 채 다음 판 로비로 돌아간다.
    void ReturnToLobby(LobbyRoom room)
    {
        getArena().Clear(room.Match);
        room.Match = null;
        room.ReadyClients.Clear();
        room.Session.ReturnToLobby();
        BroadcastSnapshot(room);
    }

    // 서버: 한 명이 나간 방만 닫는다. 그 방의 플레이어를 지우고 남은 한 명을 내보낸다. 다른 방과 서버는 그대로 돈다.
    void CloseRoom(LobbyRoom room, ulong departingClient)
    {
        NetworkManager manager = getManager();
        DuelArena arena = getArena();
        LobbySession session = room.Session;
        ulong host = session.HostClientId;
        ulong? remaining = departingClient != host ? host : session.OpponentPresent ? session.GuestClientId : (ulong?)null;
        if (arena != null) arena.Clear(room.Match);
        room.Match = null;
        // 방을 먼저 뺀다. 남은 한 명의 끊김 콜백이 이 방을 다시 찾지 못해 한 번만 닫힌다.
        Directory.Remove(room);
        if (remaining.HasValue && manager.ConnectedClients.ContainsKey(remaining.Value))
            manager.DisconnectClient(remaining.Value, LobbyReasonCodec.ToWireReason(LobbyReason.RoomClosed));
        Debug.Log($"[Lobby] Closed code={room.Code} departing={departingClient} reason={LobbyReason.RoomClosed} " +
            $"rooms={Directory.Count} playerobjects={manager.SpawnManager.PlayerObjects.Count}");
    }

    void PrepareShutdown(bool shouldRestart)
    {
        DuelArena arena = getArena();
        // 연결을 끊기 전에 모든 방의 플레이어를 디스폰해 다시 열린 서버가 빈 상태에서 시작하게 한다.
        foreach (LobbyRoom room in Directory.Rooms)
        {
            if (arena != null) arena.Clear(room.Match);
            room.Match = null;
        }
        Directory.Clear();
        reservations.Clear();
        closing = true;
        restartRequested = shouldRestart;
        UnregisterMessages();
    }

    void ExpireReservations(float now)
    {
        NetworkManager manager = getManager();
        expiredReservations.Clear();
        foreach (var reservation in reservations)
        {
            if (now >= reservation.Value) expiredReservations.Add(reservation.Key);
        }
        foreach (ulong clientId in expiredReservations)
        {
            reservations.Remove(clientId);
            Directory.CancelReservation(clientId);
            Debug.Log($"[Lobby] Reservation expired client={clientId} rooms={Directory.Count}");
            if (manager.ConnectedClients.ContainsKey(clientId))
                manager.DisconnectClient(clientId, LobbyReasonCodec.ToWireReason(LobbyReason.ConnectionTimedOut));
        }
    }
}
