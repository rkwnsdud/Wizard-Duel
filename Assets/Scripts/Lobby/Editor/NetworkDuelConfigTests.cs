#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

public class NetworkDuelConfigTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    GameObject root;
    NetworkManager manager;
    bool initializedManager;

    [TearDown]
    public void TearDown()
    {
        if (initializedManager && manager != null)
            typeof(NetworkManager).GetMethod("OnDestroy", PrivateInstance).Invoke(manager, null);
        initializedManager = false;
        if (root != null) Object.DestroyImmediate(root);
    }

    [Test]
    public void SharedConfig_AppliesIdenticalNetworkSettingsAndComponentDefaults()
    {
        root = new GameObject("NetworkDuelConfigTests");
        root.SetActive(false);
        manager = root.AddComponent<NetworkManager>();
        var transport = root.AddComponent<UnityTransport>();
        var lobby = root.AddComponent<LobbyNetwork>();
        var aiDuel = root.AddComponent<AiDuel>();
        var arena = root.AddComponent<DuelArena>();
        root.SetActive(true);
        if (typeof(NetworkManager).GetField("Log", PrivateInstance).GetValue(manager) == null)
        {
            typeof(NetworkManager).GetMethod("Awake", PrivateInstance).Invoke(manager, null);
            initializedManager = true;
        }

        NetworkDuelConfig.ConfigureNetworkManager(manager, NetworkDuelConfig.ApproveLocalHostOnly);
        // 두 번 호출해도 NGO의 중복 등록 예외 없이 설정을 유지해야 한다.
        NetworkDuelConfig.ConfigureNetworkManager(manager, NetworkDuelConfig.ApproveLocalHostOnly);
        Assert.That(manager.NetworkConfig.TickRate, Is.EqualTo(NetworkDuelConfig.TickRate));
        Assert.That(manager.NetworkConfig.ConnectionApproval, Is.True);
        Assert.That(manager.NetworkConfig.EnableSceneManagement, Is.False);
        Assert.That(manager.NetworkConfig.ClientConnectionBufferTimeout, Is.EqualTo(NetworkDuelConfig.ConnectionBufferTimeoutSeconds));
        Assert.That(manager.ConnectionApprovalCallback, Is.EqualTo(
            (Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse>)NetworkDuelConfig.ApproveLocalHostOnly));

        float lobbyDuration = (float)typeof(LobbyNetwork).GetField("resultDuration", PrivateInstance).GetValue(lobby);
        float aiDuration = (float)typeof(AiDuel).GetField("resultDuration", PrivateInstance).GetValue(aiDuel);
        Assert.That(lobbyDuration, Is.EqualTo(NetworkDuelConfig.DefaultResultDuration));
        Assert.That(aiDuration, Is.EqualTo(NetworkDuelConfig.DefaultResultDuration));

        NetworkDuelConfig.ConfigureServerTransport(transport, 8123);
        Assert.That(transport.ConnectionData.Address, Is.EqualTo(NetworkDuelConfig.LoopbackAddress));
        Assert.That(transport.ConnectionData.Port, Is.EqualTo((ushort)8123));
        Assert.That(transport.ConnectionData.ServerListenAddress, Is.EqualTo(NetworkDuelConfig.ServerListenAllAddress));

        NetworkDuelConfig.ConfigureClientTransport(transport, "  ", NetworkDuelConfig.DefaultPort);
        Assert.That(transport.ConnectionData.Address, Is.EqualTo(NetworkDuelConfig.LoopbackAddress));
        Assert.That(transport.ConnectionData.Port, Is.EqualTo(NetworkDuelConfig.DefaultPort));

        NetworkDuelConfig.ConfigureLocalHostTransport(transport);
        Assert.That(transport.ConnectionData.Address, Is.EqualTo(NetworkDuelConfig.LoopbackAddress));
        Assert.That(transport.ConnectionData.Port, Is.EqualTo((ushort)0));
        Assert.That(transport.ConnectionData.ServerListenAddress, Is.EqualTo(NetworkDuelConfig.LoopbackAddress));

        // AiDuel cannot enter LocalHost while LobbyNetwork is busy in an online or dedicated-server mode.
        typeof(AiDuel).GetField("manager", PrivateInstance).SetValue(aiDuel, manager);
        typeof(AiDuel).GetField("transport", PrivateInstance).SetValue(aiDuel, transport);
        typeof(AiDuel).GetField("arena", PrivateInstance).SetValue(aiDuel, arena);
        typeof(LobbyNetwork).GetField("manager", PrivateInstance).SetValue(lobby, manager);
        typeof(LobbyNetwork).GetField("mode", PrivateInstance).SetValue(lobby, NetworkSessionMode.OnlineClient);

        Assert.That(aiDuel.Begin(NetworkDuelConfig.DefaultDuelSettings().ToMapSettings(), NetworkDuelConfig.DefaultAiOptions()), Is.False);
        Assert.That(lobby.Mode, Is.EqualTo(NetworkSessionMode.OnlineClient));
    }

    [Test]
    public void ApproveLocalHostOnly_AllowsServerClientAndRejectsExternalClient()
    {
        var host = new NetworkManager.ConnectionApprovalResponse();
        NetworkDuelConfig.ApproveLocalHostOnly(
            new NetworkManager.ConnectionApprovalRequest { ClientNetworkId = NetworkManager.ServerClientId },
            host);
        Assert.That(host.Approved, Is.True);
        Assert.That(host.Reason, Is.Empty);

        var remote = new NetworkManager.ConnectionApprovalResponse();
        NetworkDuelConfig.ApproveLocalHostOnly(
            new NetworkManager.ConnectionApprovalRequest { ClientNetworkId = 99 },
            remote);
        Assert.That(remote.Approved, Is.False);
        Assert.That(remote.Reason, Is.EqualTo(LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest)));
    }
}
#endif
