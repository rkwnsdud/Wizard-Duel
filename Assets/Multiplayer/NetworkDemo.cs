using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// 1대1 연결 테스트: Join/Server UI와 2인 제한. 비어 있는 슬롯 순서대로 Player-1/Player-2를 스폰한다.
public class NetworkDemo : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] UnityTransport transport;
    [SerializeField] ProceduralMap map;
    [SerializeField] NetworkObject player1Prefab;
    [SerializeField] NetworkObject player2Prefab;

    const ushort Port = 7777;
    const string RoomClosed = "A player left, so the room was closed.";
    const string MapSettingsMessage = "WizardDuel.MapSettings";
    const string MapReadyMessage = "WizardDuel.MapReady";
    readonly ulong?[] slotOwners = new ulong?[2];
    readonly HashSet<ulong> readyClients = new HashSet<ulong>();
    ISpawnPointProvider spawnProvider;
    NetworkMapSettings mapSettings;
    string mapFingerprint;
    string address = "127.0.0.1";
    string status = "Enter the server IP and Join.";
    bool reopenServer;

    void Awake()
    {
        // 한 PC에서 여러 인스턴스를 띄워도 CPU를 다 쓰지 않도록 제한한다.
        Application.targetFrameRate = Application.isBatchMode ? 30 : 60;
        spawnProvider = map;
        manager.ConnectionApprovalCallback = ApproveConnection;
        manager.OnClientConnectedCallback += OnConnected;
        manager.OnClientDisconnectCallback += OnDisconnected;
    }

    void Start()
    {
        // -server: 전용 서버로 시작, -join <IP>: 실행하자마자 접속
        string[] args = Environment.GetCommandLineArgs();
        int join = Array.IndexOf(args, "-join") + 1;
        if (Array.IndexOf(args, "-server") >= 0) StartServer();
        else if (join > 0 && join < args.Length) Join(args[join]);
    }

    void Update()
    {
        if (!reopenServer || manager.IsListening) return;
        reopenServer = false;
        StartServer();
    }

    void StartServer()
    {
        Array.Clear(slotOwners, 0, slotOwners.Length);
        readyClients.Clear();
        MapGenerationSettings settings = map.CreateSettings();
        string[] args = Environment.GetCommandLineArgs();
        int seedArg = Array.IndexOf(args, "-mapSeed") + 1;
        if (seedArg > 0 && seedArg < args.Length && int.TryParse(args[seedArg], out int seed))
            settings.Seed = seed;
        mapSettings = new NetworkMapSettings(settings);
        if (!GenerateMap()) return;
        transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        bool started = manager.StartServer();
        if (started) RegisterMapMessages();
        status = started ? $"Listening on UDP {Port}." : $"Could not start the server. Check port {Port}.";
        Debug.Log($"[Server] {status}");
    }

    void Join(string serverAddress)
    {
        address = serverAddress.Trim();
        mapFingerprint = null;
        transport.SetConnectionData(address, Port);
        bool started = manager.StartClient();
        if (started) RegisterMapMessages();
        status = started ? "Connecting..." : "Could not start the connection.";
    }

    // 빈 슬롯 순서대로 1P/2P를 정한다.
    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        int slot = Array.IndexOf(slotOwners, null);
        response.Approved = slot >= 0;
        response.CreatePlayerObject = false;
        if (slot < 0)
        {
            response.Reason = "This match already has two players.";
            return;
        }

        slotOwners[slot] = request.ClientNetworkId;
        Debug.Log($"[Server] Client {request.ClientNetworkId} approved as Player-{slot + 1}.");
    }

    void RegisterMapMessages()
    {
        manager.CustomMessagingManager.RegisterNamedMessageHandler(MapSettingsMessage, OnMapSettings);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(MapReadyMessage, OnMapReady);
    }

    void OnConnected(ulong clientId)
    {
        if (!manager.IsServer) return;
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteNetworkSerializable(mapSettings);
        manager.CustomMessagingManager.SendNamedMessage(MapSettingsMessage, clientId, writer);
    }

    void OnMapSettings(ulong senderId, FastBufferReader reader)
    {
        if (!manager.IsClient || manager.IsServer || senderId != NetworkManager.ServerClientId) return;
        reader.ReadNetworkSerializable(out mapSettings);
        if (!GenerateMap())
        {
            manager.Shutdown();
            return;
        }
        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteValueSafe(mapFingerprint);
        manager.CustomMessagingManager.SendNamedMessage(MapReadyMessage, NetworkManager.ServerClientId, writer);
        status = "Map ready. Waiting for both players...";
    }

    void OnMapReady(ulong clientId, FastBufferReader reader)
    {
        if (!manager.IsServer || Array.IndexOf(slotOwners, clientId) < 0) return;
        reader.ReadValueSafe(out string fingerprint);
        if (fingerprint != mapFingerprint)
        {
            Debug.LogError($"[Server] Client {clientId} map does not match the server.");
            manager.DisconnectClient(clientId, "Generated map does not match the server.");
            return;
        }
        if (!readyClients.Add(clientId)) return;
        Debug.Log($"[Server] Client {clientId} map ready ({readyClients.Count}/2).");
        foreach (ulong? owner in slotOwners)
            if (!owner.HasValue || !readyClients.Contains(owner.Value)) return;

        NetworkDemoPlayer[] spawnedPlayers = new NetworkDemoPlayer[slotOwners.Length];
        for (int slot = 0; slot < slotOwners.Length; slot++)
        {
            Transform spawn = spawnProvider.SpawnPoints[slot];
            NetworkObject player = Instantiate(slot == 0 ? player1Prefab : player2Prefab, spawn.position, spawn.rotation);
            player.SpawnAsPlayerObject(slotOwners[slot].Value);
            spawnedPlayers[slot] = player.GetComponent<NetworkDemoPlayer>();
            Debug.Log($"[Server] Player-{slot + 1} placed at {spawn.position} rotation={spawn.rotation}.");
        }
        StartCoroutine(EnableCombatAfterSpawn(spawnedPlayers));
    }

    IEnumerator EnableCombatAfterSpawn(NetworkDemoPlayer[] players)
    {
        float duration = 1.2f;
        foreach (NetworkDemoPlayer player in players)
            if (player != null) duration = Mathf.Max(duration, player.SpawnEffectDuration);
        // Allow the reliable spawn messages to reach clients before opening the combat gate.
        yield return new WaitForSeconds(duration + 0.15f);
        foreach (NetworkDemoPlayer player in players)
            if (player != null) player.ServerEnableCombat();
    }

    bool GenerateMap()
    {
        try
        {
            map.Generate(mapSettings.ToSettings());
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            foreach (string row in map.GeneratedLayout.ToLayout()) writer.Write(row);
            foreach (Transform spawn in spawnProvider.SpawnPoints)
            {
                Vector3 position = spawn.position;
                Quaternion rotation = spawn.rotation;
                writer.Write(position.x);
                writer.Write(position.y);
                writer.Write(position.z);
                writer.Write(rotation.x);
                writer.Write(rotation.y);
                writer.Write(rotation.z);
                writer.Write(rotation.w);
            }
            writer.Flush();
            using var hash = SHA256.Create();
            mapFingerprint = BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
            Debug.Log($"[Map] Ready settings={JsonUtility.ToJson(mapSettings)} shape={map.GeneratedLayout.Shape} size={map.GeneratedLayout.Size} fingerprint={mapFingerprint}");
            for (int slot = 0; slot < spawnProvider.SpawnPoints.Length; slot++)
            {
                Transform spawn = spawnProvider.SpawnPoints[slot];
                Debug.Log($"[Map] Spawn {slot}: position={spawn.position} rotation={spawn.rotation}");
            }
            return true;
        }
        catch (Exception exception)
        {
            status = "Map generation failed.";
            Debug.LogException(exception);
            return false;
        }
    }

    void OnDestroy()
    {
        if (manager == null) return;
        manager.OnClientConnectedCallback -= OnConnected;
        manager.OnClientDisconnectCallback -= OnDisconnected;
        manager.ConnectionApprovalCallback -= ApproveConnection;
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(MapSettingsMessage);
        manager.CustomMessagingManager?.UnregisterNamedMessageHandler(MapReadyMessage);
    }

    // 한 명이라도 나가면(강제 종료 포함) 방을 닫는다. 강제 종료된 상대에게 보내던 소켓은
    // Windows에서 수신 버퍼가 새어 서버가 먹통이 되므로, 새 소켓으로 다시 연다.
    void OnDisconnected(ulong clientId)
    {
        if (manager.ShutdownInProgress) return;
        if (manager.IsServer)
        {
            if (Array.IndexOf(slotOwners, clientId) < 0) return;
            Debug.Log($"[Server] Client {clientId} left; closing the room.");
            foreach (ulong id in manager.ConnectedClientsIds)
                if (id != clientId) manager.DisconnectClient(id, RoomClosed);
            reopenServer = true;
            status = RoomClosed;
            manager.Shutdown();
            return;
        }

        status = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected from the server." : manager.DisconnectReason;
        Debug.Log($"[Client] Disconnected: {status}");
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16, 16, 330, 230), GUI.skin.box);
        GUILayout.Label("Wizard Duel - 1v1 Connection Test");
        GUI.enabled = !manager.ShutdownInProgress;
        if (!manager.IsListening)
        {
            GUILayout.Label(status);
            GUILayout.Label($"Server IP (same PC: 127.0.0.1) / Port: {Port}");
            address = GUILayout.TextField(address, 64);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Join", GUILayout.Height(36))) Join(address);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GUILayout.Button("Server", GUILayout.Height(36))) StartServer();
#endif
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label(manager.IsClient ? $"You: {LocalPlayerLabel()}" : $"Dedicated server (UDP {Port}), no player of its own");
            GUILayout.Label($"Players: {manager.SpawnManager.PlayerObjects.Count}/{slotOwners.Length}");
            if (mapFingerprint != null) GUILayout.Label($"Map seed: {mapSettings.Seed}");
            if (manager.IsClient && manager.LocalClient.PlayerObject == null) GUILayout.Label(status);
            if (GUILayout.Button(manager.IsClient ? "Disconnect" : "Stop Server", GUILayout.Height(36)))
            {
                manager.Shutdown();
                status = "Disconnected.";
            }
        }
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    string LocalPlayerLabel()
    {
        NetworkObject player = manager.LocalClient.PlayerObject;
        if (player == null) return "waiting to spawn...";
        return player.GetComponent<NetworkDemoPlayer>().Slot == 0 ? "1P (blue wizard)" : "2P (red wizard)";
    }
}
