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

    NetworkManager manager;
    WeaponSpawnCatalog catalog;
    readonly WeaponSpawnRound round = new WeaponSpawnRound();
    readonly Dictionary<ulong, Slots> slots = new Dictionary<ulong, Slots>();
    readonly Dictionary<uint, SpawnedWeapon> clientDrops = new Dictionary<uint, SpawnedWeapon>();
    readonly Dictionary<uint, FloatingWeaponPickup> visuals = new Dictionary<uint, FloatingWeaponPickup>();
    readonly Dictionary<ulong, uint> processedRequests = new Dictionary<ulong, uint>();
    readonly List<uint> removals = new List<uint>();
    readonly HashSet<ulong> needsApply = new HashSet<ulong>();
    readonly List<ulong> applied = new List<ulong>();
    uint epoch, revision, requestSequence, pendingRequest, acknowledgedRequest;
    byte pendingAction;
    Type pendingEquippedType, pendingStoredType;
    bool running, registered;
    PlayerWeaponInventory pendingInventory;
    WeaponSpawnNotification notification;
    public bool Running => manager != null && (manager.IsServer ? round.Running : running);
    public bool RequestPending => pendingRequest != 0;

    public void Initialize(NetworkManager network, WeaponSpawnCatalog definitions)
    {
        if (registered) Shutdown();
        manager = network;
        catalog = definitions;
        epoch = revision = requestSequence = 0;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(SnapshotMessage, OnSnapshot);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, OnRequest);
        registered = true;
    }

    public void BeginRound(ProceduralMap map, IReadOnlyList<DuelPlayer> players)
    {
        if (manager == null || !manager.IsServer || catalog == null) return;
        Clear();
        epoch++;
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
        var choices = new List<byte>();
        foreach (var entry in catalog.Entries)
            if (catalog.IsSpawnable(entry)) choices.Add(entry.id);
        foreach (var player in players)
        {
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            inventory.NetworkAuthority = this;
            slots[player.NetworkObjectId] = new Slots
            {
                Player = player.NetworkObjectId,
                Equipped = catalog.Identify(player.GetComponent<PlayerAttack>().CurrentWeapon),
                Stored = catalog.Identify(inventory.StoredWeapon)
            };
        }
        round.Begin(positions, choices, manager.ServerTime.Time, unchecked(Environment.TickCount ^ (int)epoch));
        running = true;
        revision++;
        Broadcast();
        Debug.Log($"[WeaponSpawn] Round {epoch}: {positions.Count} clear sites, {choices.Count} weapons, interval=30s.");
        if (positions.Count == 0) Debug.LogWarning("[WeaponSpawn] This layout has no safe, open weapon sites.");
    }

    public void EndRound()
    {
        if (notification != null) notification.Hide();
        round.Stop();
        running = false;
        ReleaseRequest();
        ClearVisuals();
        clientDrops.Clear();
        if (manager != null && manager.IsServer && registered)
        {
            revision++;
            Broadcast();
        }
    }

    public void Clear()
    {
        EndRound();
        slots.Clear();
        needsApply.Clear();
        processedRequests.Clear();
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
        if (manager.IsServer && round.Tick(manager.ServerTime.Time, SiteAvailable, out var drop))
        {
            revision++;
            Broadcast();
            Debug.Log($"[WeaponSpawn] Spawn id={drop.Id} weapon={drop.Weapon} ground={drop.Ground}");
        }
        ApplyPendingSlots();
    }

    bool SiteAvailable(Vector3 position)
    {
        foreach (var pair in slots)
            if (manager.SpawnManager.SpawnedObjects.TryGetValue(pair.Key, out var player) &&
                Vector3.Distance(player.transform.position, position) < PickupRange) return false;
        return !Physics.CheckSphere(position + Vector3.up * .75f, .45f, Blockers, QueryTriggerInteraction.Ignore);
    }

    public bool RequestPickup(PlayerWeaponInventory inventory)
    {
        uint nearest = 0;
        float distance = PickupRange * PickupRange;
        var drops = manager != null && manager.IsServer ? round.Drops : clientDrops;
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
            if (player == null || !slots.TryGetValue(player.NetworkObjectId, out var state)) return;
            if (processedRequests.TryGetValue(sender, out uint previous) && request <= previous)
            { SendSnapshot(sender, request); return; }
            processedRequests[sender] = request;
            if (requestedEpoch != epoch || !round.Running || !player.CanUseInventory)
            { SendSnapshot(sender, request); return; }
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            if (!player.GetComponent<PlayerAttack>().CanChangeWeapon)
            { SendSnapshot(sender, request); return; }

            Slots next = state;
            if (action == 1)
            {
                if (state.Stored == 0) { SendSnapshot(sender, request); return; }
                next.Equipped = state.Stored;
                next.Stored = state.Equipped;
            }
            else
            {
                if (!round.Drops.TryGetValue(pickup, out var drop) || !InReach(player.transform.position, drop.Ground) ||
                    drop.Weapon == state.Equipped || drop.Weapon == state.Stored)
                { SendSnapshot(sender, request); return; }
                if (state.Stored == 0) next.Stored = drop.Weapon;
                else next.Equipped = drop.Weapon;
            }
            if (!ApplySlots(inventory, next)) { SendSnapshot(sender, request); return; }
            slots[state.Player] = next;
            if (action == 0) round.Remove(pickup); // One server transaction: grant before remove, no double claim.
            revision++;
            Broadcast(sender, request);
            Debug.Log($"[WeaponSpawn] Granted client={sender} action={action} pickup={pickup} equipped={next.Equipped} stored={next.Stored}");
        }
        catch (Exception exception) { Debug.LogWarning("[WeaponSpawn] Invalid request: " + exception.Message); }
    }

    static bool InReach(Vector3 player, Vector3 ground)
    {
        Vector3 delta = player - ground;
        if (Mathf.Abs(delta.y) > 2f) return false;
        delta.y = 0;
        return delta.sqrMagnitude <= PickupRange * PickupRange &&
            !Physics.Linecast(player + Vector3.up * .7f, ground + Vector3.up * .7f, Blockers, QueryTriggerInteraction.Ignore);
    }

    void Broadcast(ulong requester = ulong.MaxValue, uint acknowledgement = 0)
    {
        if (manager == null || !manager.IsListening || !manager.IsServer ||
            manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        foreach (ulong client in manager.ConnectedClientsIds)
            SendSnapshot(client, client == requester ? acknowledgement : 0);
        if (manager.IsClient) ReconcileVisuals(round.Drops);
    }

    void SendSnapshot(ulong recipient, uint acknowledgement)
    {
        // Disconnect callbacks can run after Netcode has disposed its messaging system.
        if (manager == null || manager.ShutdownInProgress || manager.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(4096, Allocator.Temp);
        writer.WriteValueSafe(epoch);
        writer.WriteValueSafe(revision);
        writer.WriteValueSafe(acknowledgement);
        writer.WriteValueSafe(round.Running);
        writer.WriteValueSafe(round.Drops.Count);
        foreach (var pair in round.Drops)
        {
            var drop = pair.Value;
            writer.WriteValueSafe(drop.Id);
            writer.WriteValueSafe(drop.Weapon);
            writer.WriteValueSafe(drop.Ground);
            writer.WriteValueSafe(drop.Born);
        }
        writer.WriteValueSafe(slots.Count);
        foreach (var pair in slots)
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
                if (notification != null) notification.Hide();
            }
            ApplyPendingSlots();
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
            notification.Show(catalog.notificationFont, catalog.notificationSound, catalog.notificationVolume);
        }
    }

    void ClearVisuals()
    {
        foreach (var visual in visuals.Values) if (visual != null) Destroy(visual.gameObject);
        visuals.Clear();
    }
}
