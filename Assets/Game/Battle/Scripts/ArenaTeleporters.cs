using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 텔레포트 타일의 서버 승인. 타일 가운데에 닿은 소유자 클라이언트가 요청하고, 서버가 판·전투·생존·쌍 쿨타임을 보고
// 받아 주면 그 방의 두 클라이언트에게 상태와 이동 이벤트를 보낸다. 이동은 소유자가 하고(이동 동기화가 소유자 권한이므로)
// 다른 화면은 DuelMoveSync가 순간 이동으로 받는다. 같은 쌍의 두 타일은 함께 쿨타임에 들어간다.
// 위치·거리 같은 부정 입력 검사는 하지 않는다("악의적인 플레이어는 없다").
public sealed class ArenaTeleporters : MonoBehaviour
{
    internal static readonly NamedMessageChannel RequestChannel =
        new NamedMessageChannel("WizardDuel.TeleportRequest.v1", 32, 32);
    internal static readonly NamedMessageChannel StateChannel =
        new NamedMessageChannel("WizardDuel.TeleportState.v1", 256, 256);
    // 응답이 이만큼 오지 않으면 같은 타일에서 다시 요청할 수 있다.
    const float RequestTimeout = 1f;

    // 이 화면에서 쓰는 텔레포트 통신. Main은 DuelArena가, 오프라인 테스트는 DuelOfflineTest가 붙인다.
    public static ArenaTeleporters Instance { get; private set; }

    sealed class ServerRound
    {
        public object Key;
        public uint Epoch;
        public TeleporterCooldowns Cooldowns;
        public readonly HashSet<ulong> Recipients = new HashSet<ulong>();
        public readonly Dictionary<ulong, DuelPlayer> Players = new Dictionary<ulong, DuelPlayer>();
        public readonly Dictionary<ulong, uint> ProcessedRequests = new Dictionary<ulong, uint>();
    }

    struct TeleportEvent
    {
        public ulong Player;
        public byte Pair;
        public byte Side;
    }

    NetworkManager manager;
    NamedMessageGroup messageHandlers;
    readonly Dictionary<object, ServerRound> serverRounds = new Dictionary<object, ServerRound>();
    readonly Dictionary<ulong, ServerRound> serverPlayers = new Dictionary<ulong, ServerRound>();
    uint nextEpoch;

    uint clientEpoch;
    bool clientActive;
    TeleporterCooldowns clientCooldowns = new TeleporterCooldowns(0);
    uint requestSequence;
    // 응답을 기다리는 요청 번호마다 요청한 플레이어와 보낸 시각.
    readonly Dictionary<uint, (ulong player, float sentAt)> pending = new Dictionary<uint, (ulong, float)>();
    readonly List<uint> expired = new List<uint>();

    NamedMessageGroup MessageHandlers => messageHandlers ??= new NamedMessageGroup(
        RequestChannel.Bind(OnRequest),
        StateChannel.Bind(OnState));

    // 이번 판에서 서버가 텔레포트를 받아 주는지(이 클라이언트가 받은 마지막 상태 기준).
    public bool Running => clientActive;

    public void Initialize(NetworkManager network)
    {
        if (MessageHandlers.IsRegistered) Shutdown();
        manager = network;
        nextEpoch = 0;
        ResetClient();
        MessageHandlers.Register(manager);
        Instance = this;
    }

    public void Shutdown()
    {
        foreach (ServerRound round in new List<ServerRound>(serverRounds.Values)) EndRound(round.Key);
        ResetClient();
        messageHandlers?.Unregister();
        if (Instance == this) Instance = null;
    }

    void OnDestroy() => Shutdown();

    void ResetClient()
    {
        clientEpoch = 0;
        clientActive = false;
        clientCooldowns = new TeleporterCooldowns(0);
        requestSequence = 0;
        pending.Clear();
    }

    // 서버: 판의 전투가 켜질 때 부른다.
    public void BeginRound(DuelMatch match)
    {
        if (match != null) BeginRound(match, match.Duelists, match.Layout != null ? match.Layout.Teleporters.Count : 0);
    }

    // 서버: key로 구분하는 판 하나를 시작한다. players는 텔레포트를 쓸 수 있는 그 판의 플레이어다.
    // 같은 key로 다시 부르면 쿨타임을 비우고 새로 시작한다. 오프라인 테스트는 자기 자신을 key로 쓴다.
    public void BeginRound(object key, IReadOnlyList<DuelPlayer> players, int pairs)
    {
        if (manager == null || !manager.IsServer || key == null) return;
        if (serverRounds.ContainsKey(key)) EndRound(key);
        var round = new ServerRound { Key = key, Epoch = ++nextEpoch, Cooldowns = new TeleporterCooldowns(pairs) };
        foreach (DuelPlayer player in players)
        {
            if (player == null || !player.IsSpawned) continue;
            round.Players[player.NetworkObjectId] = player;
            round.Recipients.Add(player.OwnerClientId);
            serverPlayers[player.NetworkObjectId] = round;
        }
        serverRounds.Add(key, round);
        Broadcast(round, true, null);
        Debug.Log($"[Teleport] Round {round.Epoch}: pairs={pairs} cooldown={TeleporterCooldowns.Cooldown}s");
    }

    // 서버: 판이 끝나거나 정리될 때 부른다. 그 방 클라이언트는 더 요청하지 않는다.
    public void EndRound(object key)
    {
        if (key == null || !serverRounds.TryGetValue(key, out ServerRound round)) return;
        Broadcast(round, false, null);
        foreach (ulong player in round.Players.Keys) serverPlayers.Remove(player);
        serverRounds.Remove(key);
    }

    // 소유자: 그 쌍을 지금 쓸 수 있는지(마지막으로 받은 서버 상태 기준).
    public bool IsReady(int pair) =>
        clientActive && manager != null && clientCooldowns.IsReady(pair, manager.ServerTime.Time);

    // 소유자: teleporter가 pair의 side 타일 가운데에 닿았다. 쓸 수 없거나 같은 플레이어의 요청이 아직 답을 기다리면 false다.
    public bool TryRequest(PlayerTeleporter teleporter, int pair, int side)
    {
        if (teleporter == null || manager == null || !IsReady(pair)) return false;
        if (!teleporter.TryGetComponent(out NetworkObject networkObject)) return false;
        ulong player = networkObject.NetworkObjectId;
        float now = Time.unscaledTime;
        foreach (var entry in pending)
            if (entry.Value.player == player && now - entry.Value.sentAt < RequestTimeout) return false;
        uint request = ++requestSequence;
        pending[request] = (player, now);
        uint epoch = clientEpoch;
        RequestChannel.Send(manager, NetworkManager.ServerClientId, writer =>
        {
            writer.WriteValueSafe(epoch);
            writer.WriteValueSafe(request);
            writer.WriteValueSafe(player);
            writer.WriteValueSafe((byte)pair);
            writer.WriteValueSafe((byte)side);
        });
        return true;
    }

    void Update()
    {
        if (pending.Count == 0) return;
        float now = Time.unscaledTime;
        expired.Clear();
        foreach (var entry in pending)
            if (now - entry.Value.sentAt >= RequestTimeout) expired.Add(entry.Key);
        foreach (uint request in expired) pending.Remove(request);
    }

    void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (manager == null || !manager.IsServer) return;
        uint epoch = 0, request = 0;
        ulong playerId = 0;
        byte pair = 0, side = 0;
        if (!RequestChannel.TryRead(reader, r =>
        {
            r.ReadValueSafe(out epoch);
            r.ReadValueSafe(out request);
            r.ReadValueSafe(out playerId);
            r.ReadValueSafe(out pair);
            r.ReadValueSafe(out side);
            return side <= 1;
        }, "[Teleport] Invalid request")) return;
        // 봇과 오프라인 더미는 호스트가 소유하므로 보낸 클라이언트가 그 플레이어의 소유자인지만 본다.
        if (!serverPlayers.TryGetValue(playerId, out ServerRound round) ||
            !round.Players.TryGetValue(playerId, out DuelPlayer player) || player == null ||
            !player.IsSpawned || player.OwnerClientId != sender) return;
        if (round.ProcessedRequests.TryGetValue(playerId, out uint previous) && request <= previous)
        {
            Reply(round, sender, request);
            return;
        }
        round.ProcessedRequests[playerId] = request;
        string reason = epoch != round.Epoch ? "round"
            : pair >= round.Cooldowns.Count ? "pair"
            : !player.CanUseInventory ? "combat"
            : !round.Cooldowns.TryUse(pair, manager.ServerTime.Time) ? "cooldown"
            : null;
        if (reason != null)
        {
            Debug.Log($"[Teleport] Round {round.Epoch} Player-{player.Slot + 1} pair={pair} dropped reason={reason}");
            Reply(round, sender, request);
            return;
        }
        Debug.Log($"[Teleport] Round {round.Epoch} Player-{player.Slot + 1} pair={pair} side={side}->{1 - side} " +
            $"ready={round.Cooldowns.ReadyAt(pair):0.00}");
        Broadcast(round, true, new TeleportEvent { Player = playerId, Pair = pair, Side = side }, sender, request);
    }

    void Reply(ServerRound round, ulong recipient, uint acknowledgement) =>
        SendState(round, recipient, serverRounds.ContainsKey(round.Key), null, acknowledgement);

    void Broadcast(ServerRound round, bool active, TeleportEvent? teleport, ulong requester = ulong.MaxValue, uint acknowledgement = 0)
    {
        if (manager == null || !manager.IsListening || !manager.IsServer || manager.ShutdownInProgress) return;
        foreach (ulong client in round.Recipients)
            if (manager.ConnectedClients.ContainsKey(client))
                SendState(round, client, active, teleport, client == requester ? acknowledgement : 0);
    }

    void SendState(ServerRound round, ulong recipient, bool active, TeleportEvent? teleport, uint acknowledgement)
    {
        StateChannel.Send(manager, recipient, writer =>
        {
            writer.WriteValueSafe(round.Epoch);
            writer.WriteValueSafe(active);
            writer.WriteValueSafe(acknowledgement);
            writer.WriteValueSafe((byte)round.Cooldowns.Count);
            for (int pair = 0; pair < round.Cooldowns.Count; pair++) writer.WriteValueSafe(round.Cooldowns.ReadyAt(pair));
            writer.WriteValueSafe(teleport.HasValue);
            if (!teleport.HasValue) return;
            writer.WriteValueSafe(teleport.Value.Player);
            writer.WriteValueSafe(teleport.Value.Pair);
            writer.WriteValueSafe(teleport.Value.Side);
        });
    }

    void OnState(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId || manager == null) return;
        uint epoch = 0, acknowledgement = 0;
        bool active = false, hasEvent = false;
        double[] readyAt = null;
        var teleport = new TeleportEvent();
        if (!StateChannel.TryRead(reader, r =>
        {
            r.ReadValueSafe(out epoch);
            r.ReadValueSafe(out active);
            r.ReadValueSafe(out acknowledgement);
            r.ReadValueSafe(out byte count);
            if (count > TeleporterRegions.MaxPairs) return false;
            readyAt = new double[count];
            for (int pair = 0; pair < count; pair++) r.ReadValueSafe(out readyAt[pair]);
            r.ReadValueSafe(out hasEvent);
            if (!hasEvent) return true;
            r.ReadValueSafe(out teleport.Player);
            r.ReadValueSafe(out teleport.Pair);
            r.ReadValueSafe(out teleport.Side);
            return teleport.Pair < count && teleport.Side <= 1;
        }, "[Teleport] Invalid state")) return;
        if (acknowledgement != 0) pending.Remove(acknowledgement);
        if (epoch < clientEpoch) return;
        if (epoch > clientEpoch) pending.Clear();
        clientEpoch = epoch;
        clientActive = active;
        if (clientCooldowns.Count != readyAt.Length) clientCooldowns = new TeleporterCooldowns(readyAt.Length);
        for (int pair = 0; pair < readyAt.Length; pair++) clientCooldowns.Set(pair, readyAt[pair]);
        ShowState(readyAt, hasEvent ? teleport : (TeleportEvent?)null);
        if (hasEvent) MoveOwnedPlayer(teleport);
    }

    // 이 화면의 타일 빛을 서버 상태에 맞춘다. 서버 시각으로 온 쿨타임 끝을 이 화면의 Time.time으로 옮긴다.
    void ShowState(double[] readyAt, TeleportEvent? teleport)
    {
        TeleporterField field = TeleporterField.Active;
        if (field == null || field.Count != readyAt.Length) return;
        float now = Time.time;
        double serverNow = manager.ServerTime.Time;
        for (int pair = 0; pair < readyAt.Length; pair++)
        {
            field.GetState(pair, out float flashStart, out _);
            if (teleport.HasValue && teleport.Value.Pair == pair) flashStart = now;
            float cooldownEnd = double.IsNegativeInfinity(readyAt[pair])
                ? float.NegativeInfinity
                : now + (float)(readyAt[pair] - serverNow);
            field.SetState(pair, flashStart, cooldownEnd);
        }
    }

    // 승인된 이동이 이 화면이 소유한 플레이어(내 캐릭터, 또는 호스트의 봇)면 짝 타일 가운데로 옮긴다.
    void MoveOwnedPlayer(TeleportEvent teleport)
    {
        TeleporterField field = TeleporterField.Active;
        if (field == null || teleport.Pair >= field.Count || manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(teleport.Player, out NetworkObject player) ||
            !player.IsOwner || !player.TryGetComponent(out PlayerTeleporter teleporter)) return;
        teleporter.Arrive(field.CenterOf(teleport.Pair, 1 - teleport.Side));
    }
}
