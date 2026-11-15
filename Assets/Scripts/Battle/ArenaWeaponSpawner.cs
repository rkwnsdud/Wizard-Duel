using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Main already uses named, reliable messages with scene management disabled.
// Replicate drop records and authoritative slot pairs through the same transport;
// pickup visuals have no NetworkObject, combat components, or client-side authority.
public sealed class ArenaWeaponSpawner : MonoBehaviour
{
    const string SnapshotMessage = "WizardDuel.WeaponSnapshot.v1";
    const string RequestMessage = "WizardDuel.WeaponRequest.v1";
    const float PickupRange = 2f;
    const int Blockers = (1 << 9) | (1 << 10);
    struct Slots
    {
        public ulong Player;
        public byte Equipped, Stored;
    }

    sealed class ServerRound
    {
        public DuelMatch Match;
        public readonly WeaponSpawnRound Drops = new WeaponSpawnRound();
        public readonly Dictionary<ulong, Slots> Slots = new Dictionary<ulong, Slots>();
        public readonly Dictionary<ulong, uint> ProcessedRequests = new Dictionary<ulong, uint>();
        public readonly HashSet<ulong> Recipients = new HashSet<ulong>();
        public uint Epoch, Revision;
    }

    NetworkManager manager;
    WeaponSpawnCatalog catalog;
    readonly Dictionary<DuelMatch, ServerRound> serverRounds = new Dictionary<DuelMatch, ServerRound>();
    readonly Dictionary<ulong, ServerRound> serverPlayers = new Dictionary<ulong, ServerRound>();
    readonly Dictionary<ulong, Slots> slots = new Dictionary<ulong, Slots>();
    readonly Dictionary<uint, SpawnedWeapon> clientDrops = new Dictionary<uint, SpawnedWeapon>();
    readonly Dictionary<uint, FloatingWeaponPickup> visuals = new Dictionary<uint, FloatingWeaponPickup>();
    readonly List<uint> removals = new List<uint>();
    readonly HashSet<ulong> needsApply = new HashSet<ulong>();
    readonly List<ulong> applied = new List<ulong>();
    uint epoch, revision, nextEpoch, requestSequence, pendingRequest, acknowledgedRequest;
    byte pendingAction;
    Type pendingEquippedType, pendingStoredType;
    bool running, registered;
    PlayerWeaponInventory pendingInventory;
    WeaponSpawnNotification notification;
    public bool Running => manager != null && (manager.IsServer ? serverRounds.Count > 0 : running);
    public bool RequestPending => pendingRequest != 0;

    public void Initialize(NetworkManager network, WeaponSpawnCatalog definitions)
    {
        if (registered) Shutdown();
        manager = network;
        catalog = definitions;
        epoch = revision = nextEpoch = requestSequence = 0;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SnapshotMessage, OnSnapshot);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, OnRequest);
        registered = true;
    }

    // Capture the locations while the temporary server map still has colliders.
    public static Vector3[] FindSites(ProceduralMap map)
    {
        var positions = new List<Vector3>();
        Physics.SyncTransforms();
        foreach (var cell in WeaponSpawnPlacement.Select(map.GeneratedLayout))
        {
            Vector3 point = map.transform.TransformPoint(WeaponSpawnPlacement.LocalPosition(map.GeneratedLayout, cell));
            if (!Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var hit, 8, 1 << 8,
                QueryTriggerInteraction.Ignore) || hit.normal.y < .9f) continue;
            if (Physics.CheckSphere(hit.point + Vector3.up * .75f, .45f, Blockers, QueryTriggerInteraction.Ignore)) continue;
            positions.Add(hit.point);
        }
        return positions.ToArray();
    }

    public void BeginRound(DuelMatch match)
    {
        if (manager == null || !manager.IsServer || catalog == null || match == null) return;
        if (serverRounds.ContainsKey(match)) Clear(match);
        var state = new ServerRound { Match = match, Epoch = ++nextEpoch };
        var choices = new List<byte>();
        foreach (var entry in catalog.Entries)
            if (catalog.IsSpawnable(entry)) choices.Add(entry.id);
        foreach (var player in match.Duelists)
        {
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            inventory.NetworkAuthority = this;
            state.Slots[player.NetworkObjectId] = new Slots
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

    public void EndRound(DuelMatch match)
    {
        if (match == null || !serverRounds.TryGetValue(match, out var state)) return;
        state.Drops.Stop();
        state.Revision++;
        Broadcast(state);
        foreach (ulong player in state.Slots.Keys) serverPlayers.Remove(player);
        serverRounds.Remove(match);
        if (manager != null && manager.IsClient && state.Recipients.Contains(manager.LocalClientId))
        {
            running = false;
            ReleaseRequest();
            clientDrops.Clear();
            ClearVisuals();
            if (notification != null) notification.Hide();
        }
    }

    public void Clear(DuelMatch match) => EndRound(match);

    void Clear()
    {
        foreach (var state in new List<ServerRound>(serverRounds.Values)) EndRound(state.Match);
        if (notification != null) notification.Hide();
        running = false;
        ReleaseRequest();
        ClearVisuals();
        clientDrops.Clear();
        slots.Clear();
        needsApply.Clear();
    }

    public void Shutdown()
    {
        Clear();
        if (registered && manager != null && manager.CustomMessagingManager != null)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMessage);
        }
        registered = false;
    }

    void OnDestroy() => Shutdown();

    void Update()
    {
        if (manager == null || !manager.IsListening || manager.ShutdownInProgress) return;
        if (manager.IsServer)
        {
            foreach (var state in serverRounds.Values)
                if (state.Drops.Tick(manager.ServerTime.Time, position => SiteAvailable(state, position), out var drop))
                {
                    state.Revision++;
                    Broadcast(state);
                    Debug.Log($"[WeaponSpawn] Round {state.Epoch} spawn id={drop.Id} weapon={drop.Weapon} ground={drop.Ground}");
                }
        }
        ApplyPendingSlots();
    }

    bool SiteAvailable(ServerRound state, Vector3 position)
    {
        foreach (var pair in state.Slots)
            if (manager.SpawnManager.SpawnedObjects.TryGetValue(pair.Key, out var player) &&
                Vector3.Distance(player.transform.position, position) < PickupRange) return false;
        // The dedicated server removed the map after recording safe sites. Other
        // rooms can use the same world coordinates, so only this room's players
        // can make one of its sites unavailable.
        return true;
    }

    public bool RequestPickup(PlayerWeaponInventory inventory)
    {
        uint nearest = 0;
        float distance = PickupRange * PickupRange;
        IReadOnlyDictionary<uint, SpawnedWeapon> drops = clientDrops;
        if (manager != null && manager.IsServer && inventory.TryGetComponent(out NetworkObject player) &&
            serverPlayers.TryGetValue(player.NetworkObjectId, out var state)) drops = state.Drops.Drops;
        foreach (var pair in drops)
        {
            Vector3 delta = inventory.transform.position - pair.Value.Ground;
            if (Mathf.Abs(delta.y) > 2f) continue;
            delta.y = 0;
            if (delta.sqrMagnitude < distance) { nearest = pair.Key; distance = delta.sqrMagnitude; }
        }
        return nearest != 0 && SendRequest(inventory, 0, nearest);
    }

    public bool RequestSwap(PlayerWeaponInventory inventory) => SendRequest(inventory, 1, 0);

    bool SendRequest(PlayerWeaponInventory inventory, byte action, uint pickup)
    {
        var player = inventory.GetComponent<DuelPlayer>();
        var attack = inventory.GetComponent<PlayerAttack>();
        if (!Running || RequestPending || player == null || !player.IsSpawned || !player.IsLocalPlayer ||
            !player.CanUseInventory || !attack.CanChangeWeapon || (action == 1 && inventory.StoredWeapon == null)) return false;
        pendingInventory = inventory;
        pendingAction = action;
        pendingEquippedType = attack.CurrentWeapon != null ? attack.CurrentWeapon.GetType() : null;
        pendingStoredType = inventory.StoredWeapon != null ? inventory.StoredWeapon.GetType() : null;
        pendingRequest = ++requestSequence;
        acknowledgedRequest = 0;
        attack.InventoryChangePending = true;
        using var writer = new FastBufferWriter(32, Allocator.Temp);
        writer.WriteValueSafe(epoch);
        writer.WriteValueSafe(pendingRequest);
        writer.WriteValueSafe(action);
        writer.WriteValueSafe(pickup);
        manager.CustomMessagingManager.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
        return true;
    }

    void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (!manager.IsServer) return;
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

            Slots next = state;
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
            if (!ApplySlots(inventory, next)) { SendSnapshot(room, sender, request); return; }
            room.Slots[state.Player] = next;
            if (action == 0) room.Drops.Remove(pickup); // One server transaction: grant before remove, no double claim.
            room.Revision++;
            Broadcast(room, sender, request);
            Debug.Log($"[WeaponSpawn] Granted client={sender} action={action} pickup={pickup} equipped={next.Equipped} stored={next.Stored}");
        }
        catch (Exception exception) { Debug.LogWarning("[WeaponSpawn] Invalid request: " + exception.Message); }
    }

    static bool InReach(ServerRound room, Vector3 player, Vector3 ground)
    {
        Vector3 delta = player - ground;
        if (Mathf.Abs(delta.y) > 2f) return false;
        delta.y = 0;
        return delta.sqrMagnitude <= PickupRange * PickupRange &&
            WeaponSpawnPlacement.ClearLine(room.Match.Layout, room.Match.WorldToMap, player, ground);
    }

    void Broadcast(ServerRound state, ulong requester = ulong.MaxValue, uint acknowledgement = 0)
    {
        if (manager == null || !manager.IsListening || !manager.IsServer ||
            manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        foreach (ulong client in state.Recipients)
            if (manager.ConnectedClients.ContainsKey(client))
                SendSnapshot(state, client, client == requester ? acknowledgement : 0);
        if (manager.IsClient && state.Recipients.Contains(manager.LocalClientId))
        {
            epoch = state.Epoch;
            revision = state.Revision;
            running = state.Drops.Running;
            clientDrops.Clear();
            foreach (var drop in state.Drops.Drops) clientDrops.Add(drop.Key, drop.Value);
            ReconcileVisuals(state.Drops.Drops);
            if (requester == manager.LocalClientId && acknowledgement == pendingRequest && pendingRequest != 0)
            {
                acknowledgedRequest = acknowledgement;
                ReleaseRequest();
            }
        }
    }

    void SendSnapshot(ServerRound state, ulong recipient, uint acknowledgement)
    {
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
        manager.CustomMessagingManager.SendNamedMessage(SnapshotMessage, recipient, writer, NetworkDelivery.ReliableSequenced);
    }

    void OnSnapshot(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        try
        {
            reader.ReadValueSafe(out uint incomingEpoch);
            reader.ReadValueSafe(out uint incomingRevision);
            reader.ReadValueSafe(out uint acknowledgement);
            reader.ReadValueSafe(out bool active);
            if (incomingEpoch < epoch || incomingEpoch == epoch && incomingRevision < revision) return;
            reader.ReadValueSafe(out int count);
            if (count < 0 || count > 12) return;
            var drops = new Dictionary<uint, SpawnedWeapon>();
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out uint id);
                reader.ReadValueSafe(out byte weapon);
                reader.ReadValueSafe(out Vector3 ground);
                reader.ReadValueSafe(out double born);
                if (catalog.Find(weapon) == null) return;
                drops.Add(id, new SpawnedWeapon(id, -1, weapon, ground, born));
            }
            reader.ReadValueSafe(out int playerCount);
            if (playerCount < 0 || playerCount > 2) return;
            var incomingSlots = new List<Slots>();
            for (int i = 0; i < playerCount; i++)
            {
                reader.ReadValueSafe(out ulong player);
                reader.ReadValueSafe(out byte equipped);
                reader.ReadValueSafe(out byte stored);
                if (catalog.Find(equipped) == null || stored != 0 && catalog.Find(stored) == null) return;
                incomingSlots.Add(new Slots { Player = player, Equipped = equipped, Stored = stored });
            }
            if (reader.Position != reader.Length) return;
            if (incomingEpoch != epoch)
            {
                ReleaseRequest();
                slots.Clear();
                needsApply.Clear();
            }
            epoch = incomingEpoch;
            revision = incomingRevision;
            running = active;
            clientDrops.Clear();
            foreach (var drop in drops) clientDrops.Add(drop.Key, drop.Value);
            foreach (var state in incomingSlots)
            {
                bool changed = !slots.TryGetValue(state.Player, out var previous) ||
                    previous.Equipped != state.Equipped || previous.Stored != state.Stored;
                slots[state.Player] = state;
                if (changed || !manager.IsServer) needsApply.Add(state.Player);
            }
            ReconcileVisuals(drops);
            if (acknowledgement == pendingRequest && pendingRequest != 0) acknowledgedRequest = acknowledgement;
            if (!active)
            {
                ReleaseRequest();
                slots.Clear();
                needsApply.Clear();
                if (notification != null) notification.Hide();
            }
            else ApplyPendingSlots();
        }
        catch (Exception exception) { Debug.LogWarning("[WeaponSpawn] Invalid snapshot: " + exception.Message); }
    }

    void ApplyPendingSlots()
    {
        applied.Clear();
        foreach (ulong id in needsApply)
        {
            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(id, out var player)) continue;
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            if (inventory == null) continue;
            inventory.NetworkAuthority = this;
            if (ApplySlots(inventory, slots[id])) applied.Add(id);
        }
        foreach (ulong id in applied) needsApply.Remove(id);
        if (pendingRequest != 0 && acknowledgedRequest == pendingRequest && pendingInventory != null)
        {
            var player = pendingInventory.GetComponent<NetworkObject>();
            if (player != null && !needsApply.Contains(player.NetworkObjectId)) ReleaseRequest();
        }
    }

    bool ApplySlots(PlayerWeaponInventory inventory, Slots state) => inventory.ApplyNetworkSlots(
        catalog.Find(state.Equipped)?.prefab, state.Stored == 0 ? null : catalog.Find(state.Stored)?.prefab);

    void ReleaseRequest()
    {
        if (pendingInventory != null)
        {
            var attack = pendingInventory.GetComponent<PlayerAttack>();
            attack.InventoryChangePending = false;
            if (Running && pendingRequest != 0 && acknowledgedRequest == pendingRequest)
            {
                Type equipped = attack.CurrentWeapon != null ? attack.CurrentWeapon.GetType() : null;
                Type stored = pendingInventory.StoredWeapon != null ? pendingInventory.StoredWeapon.GetType() : null;
                if (pendingAction == 0 && (equipped != pendingEquippedType || stored != pendingStoredType))
                    pendingInventory.PlayPickupSound();
                else if (pendingAction == 1 && equipped == pendingStoredType && stored == pendingEquippedType)
                    pendingInventory.PlaySwapSound();
            }
        }
        pendingInventory = null;
        pendingRequest = acknowledgedRequest = 0;
        pendingAction = 0;
        pendingEquippedType = pendingStoredType = null;
    }

    void ReconcileVisuals(IReadOnlyDictionary<uint, SpawnedWeapon> drops)
    {
        if (!manager.IsClient) return;
        removals.Clear();
        foreach (var pair in visuals) if (!drops.ContainsKey(pair.Key)) removals.Add(pair.Key);
        foreach (uint id in removals) { Destroy(visuals[id].gameObject); visuals.Remove(id); }
        foreach (var pair in drops)
        {
            if (visuals.ContainsKey(pair.Key)) continue;
            var visual = new GameObject("Weapon Pickup " + pair.Key).AddComponent<FloatingWeaponPickup>();
            var entry = catalog.Find(pair.Value.Weapon);
            visual.Initialize(manager, entry, pair.Value.Ground, pair.Value.Born, entry.rotationSpeed);
            visuals.Add(pair.Key, visual);
            // This method runs only on clients, after server confirmation. Existing
            // IDs survive inventory snapshots, so pickup/swap acknowledgements and
            // repeated snapshots cannot replay the notice. Hosts also use this path.
            if (notification == null) notification = gameObject.AddComponent<WeaponSpawnNotification>();
            notification.Show(catalog.notificationFont);
        }
    }

    void ClearVisuals()
    {
        foreach (var visual in visuals.Values) if (visual != null) Destroy(visual.gameObject);
        visuals.Clear();
    }
}
