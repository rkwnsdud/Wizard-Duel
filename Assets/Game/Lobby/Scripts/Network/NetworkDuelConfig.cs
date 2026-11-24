using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// 온라인(LobbyNetwork)과 AI 대전(AiDuel)이 같은 NetworkManager 위에서 지금 어떤 역할로 돌고 있는지 나타낸다.
public enum NetworkSessionMode
{
    Offline,
    DedicatedServer,
    OnlineClient,
    LocalHost
}

// 온라인 서버·클라이언트와 AI 대전 로컬 호스트가 공유하는 네트워크·대결 기본값과 설정/등록 도우미다.
public static class NetworkDuelConfig
{
    public const string LoopbackAddress = "127.0.0.1";
    public const string ServerListenAllAddress = "0.0.0.0";
    public const ushort DefaultPort = 7777;
    public const uint TickRate = 30;
    public const int ConnectionBufferTimeoutSeconds = 10;
    public const int ClientSnapshotTimeoutMarginSeconds = 5;
    public const int ServerReservationTimeoutMarginSeconds = 1;
    public const int HeadlessFrameRate = 60;
    public const float DefaultResultDuration = 5f;
    public const int DefaultMaxRooms = 50;

    public static bool IsDedicatedServerLaunch =>
        Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0;

    public static DuelSettings DefaultDuelSettings() => new DuelSettings();

    public static AiDuelOptions DefaultAiOptions() => new AiDuelOptions();

    public static void ConfigureNetworkManager(NetworkManager manager,
        Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse> approvalCallback)
    {
        if (manager == null) return;
        manager.NetworkConfig.TickRate = TickRate;
        manager.NetworkConfig.ConnectionApproval = true;
        manager.NetworkConfig.EnableSceneManagement = false;
        manager.NetworkConfig.ClientConnectionBufferTimeout = ConnectionBufferTimeoutSeconds;
        if (manager.ConnectionApprovalCallback != approvalCallback)
        {
            manager.ConnectionApprovalCallback = null;
            manager.ConnectionApprovalCallback = approvalCallback;
        }
    }

    public static void ConfigureServerTransport(UnityTransport transport, ushort port)
    {
        if (transport == null) return;
        transport.SetConnectionData(LoopbackAddress, port, ServerListenAllAddress);
    }

    public static void ConfigureClientTransport(UnityTransport transport, string serverAddress, ushort port)
    {
        if (transport == null) return;
        string address = string.IsNullOrWhiteSpace(serverAddress) ? LoopbackAddress : serverAddress.Trim();
        transport.SetConnectionData(address, port);
    }

    public static void ConfigureLocalHostTransport(UnityTransport transport)
    {
        if (transport == null) return;
        // 루프백의 빈 포트에서만 듣는다. 같은 PC의 전용 서버나 다른 게임 창과 포트가 겹치지 않고, 밖에서는 접속할 수 없다.
        transport.SetConnectionData(LoopbackAddress, 0, LoopbackAddress);
    }

    public static void RejectConnection(NetworkManager.ConnectionApprovalResponse response, LobbyReason reason)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Approved = false;
        response.Reason = LobbyReasonCodec.ToWireReason(reason);
    }

    public static void ApproveLocalHostOnly(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Approved = request.ClientNetworkId == NetworkManager.ServerClientId;
        response.Reason = response.Approved ? string.Empty : LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest);
    }

    public static void RegisterMessages(NetworkManager manager, DuelArena arena, NamedMessageGroup handlers = null)
    {
        if (arena != null && manager != null) arena.RegisterMessages(manager);
        handlers?.Register(manager);
    }

    public static void UnregisterMessages(DuelArena arena, NamedMessageGroup handlers = null)
    {
        if (arena != null) arena.UnregisterMessages();
        handlers?.Unregister();
    }
}
