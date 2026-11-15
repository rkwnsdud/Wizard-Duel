using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Main already uses named, reliable messages with scene management disabled.
// Replicate drop records and authoritative slot pairs through the same transport;
// pickup visuals have no NetworkObject, combat components, or client-side authority.
public sealed class ArenaWeaponSpawner : MonoBehaviour
{
    internal static readonly NamedMessageChannel SnapshotChannel =
        new NamedMessageChannel("WizardDuel.WeaponSnapshot.v1", 4096);
    internal static readonly NamedMessageChannel RequestChannel =
        new NamedMessageChannel("WizardDuel.WeaponRequest.v1", 32);
    internal const float PickupRange = 2f;
    const int Blockers = (1 << 9) | (1 << 10);

    NetworkManager manager;
    WeaponSpawnCatalog catalog;
    WeaponSpawnServer server;
    WeaponSpawnClient client;
    NamedMessageGroup messageHandlers;

    public bool Running => manager != null && (manager.IsServer ? server != null && server.HasActiveRounds : client != null && client.Running);
    // 무기 정의 id의 프리팹. 인벤토리가 이 화면에 없던 무기를 만들 때 쓴다.
    public WeaponBase FindPrefab(byte id) => catalog != null ? catalog.Find(id)?.prefab : null;
    public bool RequestPending => client != null && client.RequestPending;

    WeaponSpawnServer Server => server ??= new WeaponSpawnServer(() => manager, () => catalog, this);
    WeaponSpawnClient Client => client ??= new WeaponSpawnClient(() => manager, () => catalog, gameObject, this);
    NamedMessageGroup MessageHandlers => messageHandlers ??= new NamedMessageGroup(
        SnapshotChannel.Bind(Client.OnSnapshot),
        RequestChannel.Bind(Server.OnRequest));

    public void Initialize(NetworkManager network, WeaponSpawnCatalog definitions)
    {
        if (MessageHandlers.IsRegistered) Shutdown();
        manager = network;
        catalog = definitions;
        Server.Reset();
        Client.Reset();
        MessageHandlers.Register(manager);
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

    public void BeginRound(DuelMatch match) => Server.BeginRound(match);

    public void EndRound(DuelMatch match)
    {
        if (server != null && server.EndRound(match)) client?.ClearRound();
    }

    public void Clear(DuelMatch match) => EndRound(match);

    public bool RequestPickup(PlayerWeaponInventory inventory) => Client.RequestPickup(inventory);

    public bool RequestSwap(PlayerWeaponInventory inventory) => Client.RequestSwap(inventory);

    public void Shutdown()
    {
        server?.Clear();
        client?.Clear();
        messageHandlers?.Unregister();
    }

    void OnDestroy() => Shutdown();

    void Update()
    {
        if (manager == null || !manager.IsListening || manager.ShutdownInProgress) return;
        if (manager.IsServer) server?.Tick(manager.ServerTime.Time);
        client?.ApplyPendingSlots();
    }

    internal static bool ApplySlots(WeaponSpawnCatalog catalog, PlayerWeaponInventory inventory, WeaponSlotState state) =>
        inventory.ApplyNetworkSlots(catalog.Find(state.Equipped)?.prefab, state.Stored == 0 ? null : catalog.Find(state.Stored)?.prefab);
}
