using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Main의 로비와 전투 시작을 연결하는 얇은 진입점이다. 서버·클라이언트 역할은 LobbyServer와 LobbyClient가 나눈다.
[DefaultExecutionOrder(-1000)]
public sealed class LobbyNetwork : MonoBehaviour
{
    [SerializeField] NetworkManager manager;
    [SerializeField] UnityTransport transport;
    [SerializeField] DuelArena arena;
    [SerializeField] string serverAddress = NetworkDuelConfig.LoopbackAddress;
    [SerializeField] ushort port = NetworkDuelConfig.DefaultPort;
    [SerializeField] GameObject[] clientPresentation;
    [Tooltip("승부가 난 뒤 두 화면에 승자를 보여 주는 시간. 서버가 이 시간이 지나면 플레이어와 맵을 정리하고 로비로 돌려보낸다. 사망 모션(4.38초)을 끝까지 보려면 5 이상으로 둔다.")]
    [SerializeField, Min(0f)] float resultDuration = NetworkDuelConfig.DefaultResultDuration;
    [Tooltip("서버가 동시에 열어 둘 수 있는 방 수. 가득 차면 새 방 생성을 거절한다.")]
    [SerializeField, Min(1)] int maxRooms = NetworkDuelConfig.DefaultMaxRooms;

    LobbyServer server;
    LobbyClient client;
    NetworkSessionMode mode = NetworkSessionMode.Offline;

    public event Action<NetworkLobbySnapshot> SnapshotChanged;
    public event Action<LobbyReason> Failed;
    public event Action<LobbyReason> Closed;
    public event Action<LobbyReason> SettingsRejected;
    public event Action<LobbyReason> StartRejected;

    public NetworkSessionMode Mode => mode;

    // 이 클라이언트의 전투 맵. 카메라가 내 스폰 지점으로 내려갈 때 쓴다.
    public DuelArena Arena => arena;
    public bool IsBusy => Client.IsBusy;
    public bool IsInLobby => Client.IsInLobby;
    public bool IsHost => Client.IsHost;
    public bool HasPendingSettings => Client.HasPendingSettings;
    // 시작 요청의 답을 기다리거나 서버가 전투 준비를 알린 뒤다.
    public bool IsStarting => Client.IsStarting;
    public bool HasLocalPlayer => Client.HasLocalPlayer;

    LobbyServer Server => server ??= new LobbyServer(
        () => manager, () => transport, () => arena, () => maxRooms, () => resultDuration);

    LobbyClient Client
    {
        get
        {
            if (client != null) return client;
            client = new LobbyClient(() => manager, () => transport, () => arena, () => mode, nextMode => mode = nextMode);
            client.SnapshotChanged += snapshot => SnapshotChanged?.Invoke(snapshot);
            client.Failed += reason => Failed?.Invoke(reason);
            client.Closed += reason => Closed?.Invoke(reason);
            client.SettingsRejected += reason => SettingsRejected?.Invoke(reason);
            client.StartRejected += reason => StartRejected?.Invoke(reason);
            return client;
        }
    }

    void Awake()
    {
        mode = NetworkDuelConfig.IsDedicatedServerLaunch
            ? NetworkSessionMode.DedicatedServer
            : NetworkSessionMode.Offline;
        bool isServer = mode == NetworkSessionMode.DedicatedServer;
        // 서버는 한 플레이어의 이동을 받은 프레임에 상대에게 넘긴다(DuelMoveSync). 그래서 서버의 프레임 간격만큼
        // 이동이 상대 화면에 늦게 보인다. 클라이언트가 보내는 속도(초당 60번)에 맞춰 서버도 초당 60프레임으로 돈다.
        // 화면이 있는 클라이언트의 최대 프레임은 설정 화면(GraphicsOptions)이 정한다. 화면 없이 도는 서버와 batchmode 클라이언트만 여기서 묶는다.
        if (isServer || Application.isBatchMode)
            Application.targetFrameRate = NetworkDuelConfig.HeadlessFrameRate;
        if (isServer && clientPresentation != null)
        {
            foreach (GameObject presentation in clientPresentation)
                if (presentation != null) presentation.SetActive(false);
        }
        if (manager == null || transport == null) return;
        NetworkDuelConfig.ConfigureNetworkManager(manager, ApproveConnection);
        manager.OnClientConnectedCallback += OnConnected;
        manager.OnClientDisconnectCallback += OnDisconnected;
        manager.OnTransportFailure += OnTransportFailure;
    }

    void Start()
    {
        // -join은 주소만 정한다. 코드 없는 자동 접속은 하지 않는다.
        string[] args = Environment.GetCommandLineArgs();
        int joinIndex = Array.IndexOf(args, "-join");
        if (joinIndex >= 0 && joinIndex + 1 < args.Length) serverAddress = args[joinIndex + 1].Trim();
        if (mode == NetworkSessionMode.DedicatedServer) Server.Start(port);
    }

    void Update()
    {
        if (manager == null) return;
        float now = Time.realtimeSinceStartup;
        if (mode == NetworkSessionMode.DedicatedServer) Server.Tick(now, port);
        else if (mode != NetworkSessionMode.LocalHost) Client.Tick(now);
    }

    public void Create(DuelSettings settings)
    {
        if (mode != NetworkSessionMode.Offline) return;
        Client.StartRequest(true, string.Empty, settings, serverAddress, port);
    }

    public void Join(string code)
    {
        if (mode != NetworkSessionMode.Offline) return;
        Client.StartRequest(false, code, null, serverAddress, port);
    }

    public void UpdateSettings(DuelSettings settings) => Client.UpdateSettings(settings);

    // 방장이 상대와 함께 로비 단계에 있을 때 한 번만 보낸다. 승인은 전투 준비 스냅샷으로, 거절은 같은 번호의 응답으로 온다.
    public bool RequestStart() => Client.RequestStart();

    public void Cancel() => Client.Cancel();

    internal bool BeginLocalHost()
    {
        if (mode != NetworkSessionMode.Offline || IsBusy) return false;
        if (manager != null)
            NetworkDuelConfig.ConfigureNetworkManager(manager, ApproveConnection);
        mode = NetworkSessionMode.LocalHost;
        return true;
    }

    internal void EndLocalHost()
    {
        if (mode == NetworkSessionMode.LocalHost) mode = NetworkSessionMode.Offline;
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        switch (mode)
        {
            case NetworkSessionMode.DedicatedServer:
                Server.ApproveConnection(request, response);
                return;
            case NetworkSessionMode.LocalHost:
                NetworkDuelConfig.ApproveLocalHostOnly(request, response);
                return;
            default:
                NetworkDuelConfig.RejectConnection(response, LobbyReason.InvalidRequest);
                return;
        }
    }

    void OnConnected(ulong clientId)
    {
        if (mode == NetworkSessionMode.DedicatedServer) Server.OnConnected(clientId);
    }

    void OnDisconnected(ulong clientId)
    {
        if (mode == NetworkSessionMode.DedicatedServer) Server.OnDisconnected(clientId);
        else if (mode == NetworkSessionMode.OnlineClient) Client.OnDisconnected();
    }

    void OnTransportFailure()
    {
        if (mode == NetworkSessionMode.DedicatedServer) Server.OnTransportFailure();
        else if (mode == NetworkSessionMode.OnlineClient) Client.OnTransportFailure();
    }

    void OnDestroy()
    {
        if (manager == null) return;
        manager.OnClientConnectedCallback -= OnConnected;
        manager.OnClientDisconnectCallback -= OnDisconnected;
        manager.OnTransportFailure -= OnTransportFailure;
        manager.ConnectionApprovalCallback -= ApproveConnection;
        server?.UnregisterMessages();
        client?.UnregisterMessages();
        if (manager.IsListening && !manager.ShutdownInProgress) manager.Shutdown();
    }
}
