using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

internal struct WeaponSlotState
{
    public ulong Player;
    public byte Equipped;
    public byte Stored;
}

// 무기 스폰과 인벤토리 교체·줍기 요청의 서버 측 권위를 담당한다.
public sealed class WeaponSpawnServer
{
    internal sealed class ServerRound
    {
        public DuelMatch Match;
        public uint Epoch;
        public uint Revision;
        public readonly WeaponSpawnRound Drops = new WeaponSpawnRound();
        public readonly HashSet<ulong> Recipients = new HashSet<ulong>();
        public readonly Dictionary<ulong, WeaponSlotState> Slots = new Dictionary<ulong, WeaponSlotState>();
        public readonly Dictionary<ulong, uint> ProcessedRequests = new Dictionary<ulong, uint>();
    }

    readonly Func<NetworkManager> getManager;
    readonly Func<WeaponSpawnCatalog> getCatalog;
    readonly ArenaWeaponSpawner authority;
    readonly Dictionary<DuelMatch, ServerRound> serverRounds = new Dictionary<DuelMatch, ServerRound>();
    readonly Dictionary<ulong, ServerRound> serverPlayers = new Dictionary<ulong, ServerRound>();
    uint nextEpoch;

    public WeaponSpawnServer(
        Func<NetworkManager> getManager,
        Func<WeaponSpawnCatalog> getCatalog,
        ArenaWeaponSpawner authority)
    {
        this.getManager = getManager;
        this.getCatalog = getCatalog;
        this.authority = authority;
    }

    public bool HasActiveRounds => serverRounds.Count > 0;

    public void Reset() => nextEpoch = 0;

    public void BeginRound(DuelMatch match)
    {
        NetworkManager manager = getManager();
        WeaponSpawnCatalog catalog = getCatalog();
        if (manager == null || !manager.IsServer || catalog == null || match == null) return;
        if (serverRounds.ContainsKey(match)) EndRound(match);
        var state = new ServerRound { Match = match, Epoch = ++nextEpoch };
        var choices = new List<byte>();
        foreach (var entry in catalog.Entries)
            if (catalog.IsSpawnable(entry)) choices.Add(entry.id);
        foreach (var player in match.Duelists)
        {
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            inventory.NetworkAuthority = authority;
            state.Slots[player.NetworkObjectId] = new WeaponSlotState
            {
                Player = player.NetworkObjectId,
                Equipped = catalog.Identify(player.GetComponent<PlayerAttack>().CurrentWeapon),
                Stored = catalog.Identify(inventory.StoredWeapon)
            };
            state.Recipients.Add(player.OwnerClientId);
            serverPlayers.Add(player.NetworkObjectId, state);
        }
        state.Drops.Begin(match.WeaponSites, choices, manager.ServerTime.Time,
            unchecked(Environment.TickCount ^ (int)state.Epoch));
        serverRounds.Add(match, state);
        state.Revision++;
        Broadcast(state);
        Debug.Log($"[WeaponSpawn] Round {state.Epoch}: {match.WeaponSites.Length} clear sites, {choices.Count} weapons, interval={WeaponSpawnRound.Interval}s.");
        if (match.WeaponSites.Length == 0) Debug.LogWarning("[WeaponSpawn] This layout has no safe, open weapon sites.");
    }

    public bool EndRound(DuelMatch match)
    {
        if (match == null || !serverRounds.TryGetValue(match, out var state)) return false;
        state.Drops.Stop();
        state.Revision++;
        Broadcast(state);
        foreach (ulong player in state.Slots.Keys) serverPlayers.Remove(player);
        serverRounds.Remove(match);
        NetworkManager manager = getManager();
        return manager != null && manager.IsClient && state.Recipients.Contains(manager.LocalClientId);
    }

    public void Clear()
    {
        foreach (var state in new List<ServerRound>(serverRounds.Values)) EndRound(state.Match);
    }

    public void Tick(double serverTime)
    {
        foreach (var state in serverRounds.Values)
            if (state.Drops.Tick(serverTime, position => SiteAvailable(state, position), out var drop))
            {
                state.Revision++;
                Broadcast(state);
                Debug.Log($"[WeaponSpawn] Round {state.Epoch} spawn id={drop.Id} weapon={drop.Weapon} ground={drop.Ground}");
            }
    }

    public void OnRequest(ulong sender, FastBufferReader reader)
    {
        NetworkManager manager = getManager();
        if (manager == null || !manager.IsServer) return;
        try
        {
            reader.ReadValueSafe(out uint requestedEpoch);
            reader.ReadValueSafe(out uint request);
            reader.ReadValueSafe(out byte action);
            reader.ReadValueSafe(out uint pickup);
            if (reader.Position != reader.Length || action > 1) return;
            if (!manager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;
            var player = client.PlayerObject.GetComponent<DuelPlayer>();
            if (player == null || !serverPlayers.TryGetValue(player.NetworkObjectId, out var room) ||
                player.Match != room.Match || !room.Slots.TryGetValue(player.NetworkObjectId, out var state)) return;
            if (room.ProcessedRequests.TryGetValue(sender, out uint previous) && request <= previous)
            { SendSnapshot(room, sender, request); return; }
            room.ProcessedRequests[sender] = request;
            if (requestedEpoch != room.Epoch || !room.Drops.Running || !player.CanUseInventory)
            { SendSnapshot(room, sender, request); return; }
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            if (!player.GetComponent<PlayerAttack>().CanChangeWeapon)
            { SendSnapshot(room, sender, request); return; }

            WeaponSlotState next = state;
            if (action == 1)
            {
                if (state.Stored == 0) { SendSnapshot(room, sender, request); return; }
                next.Equipped = state.Stored;
                next.Stored = state.Equipped;
            }
            else
            {
                if (!room.Drops.Drops.TryGetValue(pickup, out var drop) ||
                    !InReach(room, player.transform.position, drop.Ground) ||
                    drop.Weapon == state.Equipped || drop.Weapon == state.Stored)
                { SendSnapshot(room, sender, request); return; }
                if (state.Stored == 0) next.Stored = drop.Weapon;
                else next.Equipped = drop.Weapon;
            }
            if (!ArenaWeaponSpawner.ApplySlots(getCatalog(), inventory, next)) { SendSnapshot(room, sender, request); return; }
            room.Slots[state.Player] = next;
            if (action == 0) room.Drops.Remove(pickup); // One server transaction: grant before remove, no double claim.
            room.Revision++;
            Broadcast(room, sender, request);
        }
        catch (Exception exception) { Debug.LogWarning("[WeaponSpawn] Invalid request: " + exception.Message); }
    }

    bool SiteAvailable(ServerRound state, Vector3 position)
    {
        NetworkManager manager = getManager();
        foreach (var pair in state.Slots)
            if (manager.SpawnManager.SpawnedObjects.TryGetValue(pair.Key, out var player) &&
                Vector3.Distance(player.transform.position, position) < ArenaWeaponSpawner.PickupRange) return false;
        // The dedicated server removed the map after recording safe sites. Other
        // rooms can use the same world coordinates, so only this room's players
        // can make one of its sites unavailable.
        return true;
    }

    static bool InReach(ServerRound room, Vector3 player, Vector3 ground)
    {
        Vector3 delta = player - ground;
        if (Mathf.Abs(delta.y) > 2f) return false;
        delta.y = 0;
        return delta.sqrMagnitude <= ArenaWeaponSpawner.PickupRange * ArenaWeaponSpawner.PickupRange &&
            WeaponSpawnPlacement.ClearLine(room.Match.Layout, room.Match.WorldToMap, player, ground);
    }

    void Broadcast(ServerRound state, ulong requester = ulong.MaxValue, uint acknowledgement = 0)
    {
        NetworkManager manager = getManager();
        if (manager == null || !manager.IsListening || !manager.IsServer ||
            manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        foreach (ulong client in state.Recipients)
            if (manager.ConnectedClients.ContainsKey(client))
                SendSnapshot(state, client, client == requester ? acknowledgement : 0);
    }

    void SendSnapshot(ServerRound state, ulong recipient, uint acknowledgement)
    {
        NetworkManager manager = getManager();
        // Disconnect callbacks can run after Netcode has disposed its messaging system.
        if (manager == null || manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(4096, Allocator.Temp);
        writer.WriteValueSafe(state.Epoch);
        writer.WriteValueSafe(state.Revision);
        writer.WriteValueSafe(acknowledgement);
        writer.WriteValueSafe(state.Drops.Running);
        writer.WriteValueSafe(state.Drops.Drops.Count);
        foreach (var pair in state.Drops.Drops)
        {
            var drop = pair.Value;
            writer.WriteValueSafe(drop.Id);
            writer.WriteValueSafe(drop.Weapon);
            writer.WriteValueSafe(drop.Ground);
            writer.WriteValueSafe(drop.Born);
        }
        writer.WriteValueSafe(state.Slots.Count);
        foreach (var pair in state.Slots)
        {
            writer.WriteValueSafe(pair.Value.Player);
            writer.WriteValueSafe(pair.Value.Equipped);
            writer.WriteValueSafe(pair.Value.Stored);
        }
        manager.CustomMessagingManager.SendNamedMessage(ArenaWeaponSpawner.SnapshotMessage, recipient, writer,
            NetworkDelivery.ReliableSequenced);
    }
}
