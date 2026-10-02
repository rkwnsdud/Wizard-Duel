using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// 1대1 연결 테스트: Join/Server UI와 2인 제한. 비어 있는 슬롯 순서대로 Player-1/Player-2를 스폰한다.
public class NetworkDemo : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] UnityTransport transport;
    [SerializeField] GridMap map;
    [SerializeField] NetworkObject player1Prefab;
    [SerializeField] NetworkObject player2Prefab;

    const ushort Port = 7777;
    const string RoomClosed = "A player left, so the room was closed.";
    readonly ulong?[] slotOwners = new ulong?[2];
    string address = "127.0.0.1";
    string status = "Enter the server IP and Join.";
    bool reopenServer;

    void Awake()
    {
        // 한 PC에서 여러 인스턴스를 띄워도 CPU를 다 쓰지 않도록 제한한다.
        Application.targetFrameRate = Application.isBatchMode ? 30 : 60;
        manager.ConnectionApprovalCallback = ApproveConnection;
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
        transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        bool started = manager.StartServer();
        status = started ? $"Listening on UDP {Port}." : $"Could not start the server. Check port {Port}.";
        Debug.Log($"[Server] {status}");
    }

    void Join(string serverAddress)
    {
        address = serverAddress.Trim();
        transport.SetConnectionData(address, Port);
        status = manager.StartClient() ? "Connecting..." : "Could not start the connection.";
    }

    // 빈 슬롯 순서대로 1P/2P를 정한다.
    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        int slot = Array.IndexOf(slotOwners, null);
        response.Approved = slot >= 0;
        response.CreatePlayerObject = slot >= 0;
        if (slot < 0)
        {
            response.Reason = "This match already has two players.";
            return;
        }

        slotOwners[slot] = request.ClientNetworkId;
        response.PlayerPrefabHash = (slot == 0 ? player1Prefab : player2Prefab).PrefabIdHash;
        response.Position = map.SpawnPoints[slot].position;
        response.Rotation = map.SpawnPoints[slot].rotation;
        Debug.Log($"[Server] Client {request.ClientNetworkId} approved as Player-{slot + 1}.");
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
        GUILayout.BeginArea(new Rect(16, 16, 330, 200), GUI.skin.box);
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
