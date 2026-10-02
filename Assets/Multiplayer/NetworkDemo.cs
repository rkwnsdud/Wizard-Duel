using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// 1대1 연결 테스트: Host/Join/Server UI와 2인 제한. 비어 있는 슬롯 순서대로 Player-1/Player-2를 스폰한다.
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
    string status = "Choose Host or enter the host IP and Join.";
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
        if (Array.IndexOf(args, "-server") >= 0) StartServer(false);
        else if (join > 0 && join < args.Length) Join(args[join]);
    }

    void Update()
    {
        if (!reopenServer || manager.IsListening) return;
        reopenServer = false;
        StartServer(false);
    }

    void StartServer(bool host)
    {
        Array.Clear(slotOwners, 0, slotOwners.Length);
        transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        bool started = host ? manager.StartHost() : manager.StartServer();
        status = started ? $"Listening on UDP {Port}." : $"Could not start the server. Check port {Port}.";
        Debug.Log($"[Server] {status}");
    }

    void Join(string hostAddress)
    {
        address = hostAddress.Trim();
        transport.SetConnectionData(address, Port);
        status = manager.StartClient() ? "Connecting..." : "Could not start the connection.";
    }

    // 호스트 여부가 아니라 빈 슬롯이 1P/2P를 정하므로 호스트와 전용 서버가 같은 규칙을 쓴다.
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
    // Windows에서 수신 버퍼가 새어 서버가 먹통이 되므로, 전용 서버는 새 소켓으로 다시 연다.
    void OnDisconnected(ulong clientId)
    {
        // 종료 중이거나, 종료를 마친 호스트가 자기 자신의 끊김을 통보받는 경우는 무시한다.
        if (manager.ShutdownInProgress || !manager.IsListening) return;
        if (manager.IsServer)
        {
            if (Array.IndexOf(slotOwners, clientId) < 0) return;
            Debug.Log($"[Server] Client {clientId} left; closing the room.");
            foreach (ulong id in manager.ConnectedClientsIds)
                if (id != clientId && id != NetworkManager.ServerClientId) manager.DisconnectClient(id, RoomClosed);
            reopenServer = !manager.IsHost;
            status = RoomClosed;
            manager.Shutdown();
            return;
        }

        status = string.IsNullOrEmpty(manager.DisconnectReason) ? "Disconnected from the host." : manager.DisconnectReason;
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
            GUILayout.Label($"Host IP (same PC: 127.0.0.1) / Port: {Port}");
            address = GUILayout.TextField(address, 64);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", GUILayout.Height(36))) StartServer(true);
            if (GUILayout.Button("Join", GUILayout.Height(36))) Join(address);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GUILayout.Button("Server", GUILayout.Height(36))) StartServer(false);
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
                status = "Disconnected. Choose Host or Join.";
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
