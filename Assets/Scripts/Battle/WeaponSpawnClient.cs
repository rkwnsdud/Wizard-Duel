using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 무기 스폰의 클라이언트 측 역할을 맡는다. 서버 스냅샷 수신, 픽업 연출·알림, 줍기/교체 요청 전송을 처리한다.
public sealed class WeaponSpawnClient
{
    readonly Func<NetworkManager> getManager;
    readonly Func<WeaponSpawnCatalog> getCatalog;
    readonly GameObject hostObject;
    readonly ArenaWeaponSpawner authority;
    readonly Dictionary<uint, SpawnedWeapon> clientDrops = new Dictionary<uint, SpawnedWeapon>();
    readonly Dictionary<ulong, WeaponSlotState> slots = new Dictionary<ulong, WeaponSlotState>();
    readonly HashSet<ulong> needsApply = new HashSet<ulong>();
    readonly Dictionary<uint, FloatingWeaponPickup> visuals = new Dictionary<uint, FloatingWeaponPickup>();
    readonly List<uint> removals = new List<uint>();
    readonly List<ulong> applied = new List<ulong>();

    uint epoch;
    uint revision;
    bool running;
    uint requestSequence;
    uint pendingRequest;
    uint acknowledgedRequest;
    byte pendingAction;
    byte pendingEquippedId;
    byte pendingStoredId;
    PlayerWeaponInventory pendingInventory;
    WeaponSpawnNotification notification;

    public WeaponSpawnClient(
        Func<NetworkManager> getManager,
        Func<WeaponSpawnCatalog> getCatalog,
        GameObject hostObject,
        ArenaWeaponSpawner authority)
    {
        this.getManager = getManager;
        this.getCatalog = getCatalog;
        this.hostObject = hostObject;
        this.authority = authority;
    }

    public bool Running => running;
    public bool RequestPending => pendingRequest != 0;

    public void Reset()
    {
        epoch = 0;
        revision = 0;
        requestSequence = 0;
    }

    public bool RequestPickup(PlayerWeaponInventory inventory)
    {
        uint nearest = 0;
        float distance = ArenaWeaponSpawner.PickupRange * ArenaWeaponSpawner.PickupRange;
        foreach (var pair in clientDrops)
        {
            Vector3 delta = inventory.transform.position - pair.Value.Ground;
            if (Mathf.Abs(delta.y) > 2f) continue;
            delta.y = 0;
            if (delta.sqrMagnitude < distance) { nearest = pair.Key; distance = delta.sqrMagnitude; }
        }
        return nearest != 0 && SendRequest(inventory, 0, nearest);
    }

    public bool RequestSwap(PlayerWeaponInventory inventory) => SendRequest(inventory, 1, 0);

    public void OnSnapshot(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId) return;
        NetworkManager manager = getManager();
        WeaponSpawnCatalog catalog = getCatalog();
        if (manager == null || catalog == null) return;
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
            var incomingSlots = new List<WeaponSlotState>();
            for (int i = 0; i < playerCount; i++)
            {
                reader.ReadValueSafe(out ulong player);
                reader.ReadValueSafe(out byte equipped);
                reader.ReadValueSafe(out byte stored);
                if (catalog.Find(equipped) == null || stored != 0 && catalog.Find(stored) == null) return;
                incomingSlots.Add(new WeaponSlotState { Player = player, Equipped = equipped, Stored = stored });
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

    public void ApplyPendingSlots()
    {
        NetworkManager manager = getManager();
        WeaponSpawnCatalog catalog = getCatalog();
        if (manager == null || manager.SpawnManager == null || catalog == null) return;
        applied.Clear();
        foreach (ulong id in needsApply)
        {
            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(id, out var player)) continue;
            var inventory = player.GetComponent<PlayerWeaponInventory>();
            if (inventory == null) continue;
            inventory.NetworkAuthority = authority;
            if (ArenaWeaponSpawner.ApplySlots(catalog, inventory, slots[id])) applied.Add(id);
        }
        foreach (ulong id in applied) needsApply.Remove(id);
        if (pendingRequest != 0 && acknowledgedRequest == pendingRequest && pendingInventory != null)
        {
            var player = pendingInventory.GetComponent<NetworkObject>();
            if (player != null && !needsApply.Contains(player.NetworkObjectId)) ReleaseRequest();
        }
    }

    public void ClearRound()
    {
        running = false;
        ReleaseRequest();
        clientDrops.Clear();
        ClearVisuals();
        if (notification != null) notification.Hide();
    }

    public void Clear()
    {
        ClearRound();
        slots.Clear();
        needsApply.Clear();
    }

    bool SendRequest(PlayerWeaponInventory inventory, byte action, uint pickup)
    {
        NetworkManager manager = getManager();
        var player = inventory.GetComponent<DuelPlayer>();
        var attack = inventory.GetComponent<PlayerAttack>();
        bool isRunning = authority != null ? authority.Running : running;
        if (!isRunning || RequestPending || player == null || !player.IsSpawned || !player.IsLocalPlayer ||
            !player.CanUseInventory || !attack.CanChangeWeapon || (action == 1 && inventory.StoredWeapon == null)) return false;
        pendingInventory = inventory;
        pendingAction = action;
        pendingEquippedId = attack.CurrentWeapon != null ? attack.CurrentWeapon.Id : (byte)0;
        pendingStoredId = inventory.StoredWeapon != null ? inventory.StoredWeapon.Id : (byte)0;
        pendingRequest = ++requestSequence;
        acknowledgedRequest = 0;
        attack.InventoryChangePending = true;
        using var writer = new FastBufferWriter(32, Allocator.Temp);
        writer.WriteValueSafe(epoch);
        writer.WriteValueSafe(pendingRequest);
        writer.WriteValueSafe(action);
        writer.WriteValueSafe(pickup);
        manager.CustomMessagingManager.SendNamedMessage(ArenaWeaponSpawner.RequestMessage, NetworkManager.ServerClientId,
            writer, NetworkDelivery.ReliableSequenced);
        return true;
    }

    void ReleaseRequest()
    {
        if (pendingInventory != null)
        {
            var attack = pendingInventory.GetComponent<PlayerAttack>();
            attack.InventoryChangePending = false;
            if (Running && pendingRequest != 0 && acknowledgedRequest == pendingRequest)
            {
                byte equipped = attack.CurrentWeapon != null ? attack.CurrentWeapon.Id : (byte)0;
                byte stored = pendingInventory.StoredWeapon != null ? pendingInventory.StoredWeapon.Id : (byte)0;
                if (pendingAction == 0 && (equipped != pendingEquippedId || stored != pendingStoredId))
                    pendingInventory.PlayPickupSound();
                else if (pendingAction == 1 && equipped == pendingStoredId && stored == pendingEquippedId)
                    pendingInventory.PlaySwapSound();
            }
        }
        pendingInventory = null;
        pendingRequest = acknowledgedRequest = 0;
        pendingAction = 0;
        pendingEquippedId = pendingStoredId = 0;
    }

    void ReconcileVisuals(IReadOnlyDictionary<uint, SpawnedWeapon> drops)
    {
        NetworkManager manager = getManager();
        WeaponSpawnCatalog catalog = getCatalog();
        if (manager == null || !manager.IsClient || catalog == null) return;
        removals.Clear();
        foreach (var pair in visuals) if (!drops.ContainsKey(pair.Key)) removals.Add(pair.Key);
        foreach (uint id in removals) { UnityEngine.Object.Destroy(visuals[id].gameObject); visuals.Remove(id); }
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
            if (notification == null && hostObject != null) notification = hostObject.AddComponent<WeaponSpawnNotification>();
            if (notification != null) notification.Show(catalog.notificationFont);
        }
    }

    void ClearVisuals()
    {
        foreach (var visual in visuals.Values) if (visual != null) UnityEngine.Object.Destroy(visual.gameObject);
        visuals.Clear();
    }
}
