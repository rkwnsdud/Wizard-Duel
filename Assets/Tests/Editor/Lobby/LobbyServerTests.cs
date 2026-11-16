#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class LobbyServerTests
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
    public void ApproveConnection_CreatesRoomForValidPayload_AndEnforcesMaxRooms()
    {
        LobbyServer server = CreateServer(maxRooms: 1);

        NetworkManager.ConnectionApprovalResponse first = Approve(server, 1,
            new LobbyJoinRequest(true, string.Empty, new DuelSettings { Map = new MapGenerationSettings { Seed = 100 } }));
        Assert.That(first.Approved, Is.True);
        Assert.That(first.Reason, Is.Empty);

        NetworkManager.ConnectionApprovalResponse second = Approve(server, 2,
            new LobbyJoinRequest(true, string.Empty, new DuelSettings { Map = new MapGenerationSettings { Seed = 200 } }));
        Assert.That(second.Approved, Is.False);
        Assert.That(second.Reason, Is.EqualTo(LobbyReasonCodec.ToWireReason(LobbyReason.ServerFull)));
    }

    [Test]
    public void ApproveConnection_RejectsCorruptPayloadAndUnknownRoomCode()
    {
        LobbyServer server = CreateServer(maxRooms: 4);

        var badResponse = new NetworkManager.ConnectionApprovalResponse();
        server.ApproveConnection(new NetworkManager.ConnectionApprovalRequest
        {
            ClientNetworkId = 1,
            Payload = new byte[] { 1, 2, 3 }
        }, badResponse);
        Assert.That(badResponse.Approved, Is.False);
        Assert.That(badResponse.Reason, Is.EqualTo(LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest)));

        NetworkManager.ConnectionApprovalResponse missingRoom = Approve(server, 2,
            new LobbyJoinRequest(false, "999999", null));
        Assert.That(missingRoom.Approved, Is.False);
        Assert.That(missingRoom.Reason, Is.EqualTo(LobbyReasonCodec.ToWireReason(LobbyReason.RoomNotFound)));
    }

    [Test]
    public void SessionModeApproval_AllowsLocalHostOnlyInLocalHostMode_AndRejectsExternalAndOffline()
    {
        CreateServer(maxRooms: 4);
        var network = root.AddComponent<LobbyNetwork>();
        typeof(LobbyNetwork).GetField("manager", PrivateInstance).SetValue(network, manager);
        MethodInfo approve = typeof(LobbyNetwork).GetMethod("ApproveConnection", PrivateInstance);
        MethodInfo beginLocalHost = typeof(LobbyNetwork).GetMethod("BeginLocalHost", PrivateInstance);
        MethodInfo endLocalHost = typeof(LobbyNetwork).GetMethod("EndLocalHost", PrivateInstance);
        string invalidReason = LobbyReasonCodec.ToWireReason(LobbyReason.InvalidRequest);

        Assert.That(network.Mode, Is.EqualTo(NetworkSessionMode.Offline));
        NetworkManager.ConnectionApprovalResponse offlineHost = InvokeApprove(approve, network, NetworkManager.ServerClientId);
        Assert.That(offlineHost.Approved, Is.False);
        Assert.That(offlineHost.Reason, Is.EqualTo(invalidReason));

        Assert.That((bool)beginLocalHost.Invoke(network, null), Is.True);
        Assert.That(network.Mode, Is.EqualTo(NetworkSessionMode.LocalHost));

        NetworkManager.ConnectionApprovalResponse hostResponse = InvokeApprove(approve, network, NetworkManager.ServerClientId);
        Assert.That(hostResponse.Approved, Is.True);
        Assert.That(hostResponse.Reason, Is.Empty);

        NetworkManager.ConnectionApprovalResponse remoteResponse = InvokeApprove(approve, network, 42);
        Assert.That(remoteResponse.Approved, Is.False);
        Assert.That(remoteResponse.Reason, Is.EqualTo(invalidReason));

        endLocalHost.Invoke(network, null);
        Assert.That(network.Mode, Is.EqualTo(NetworkSessionMode.Offline));

        typeof(LobbyNetwork).GetField("mode", PrivateInstance).SetValue(network, NetworkSessionMode.OnlineClient);
        Assert.That((bool)beginLocalHost.Invoke(network, null), Is.False);
        NetworkManager.ConnectionApprovalResponse onlineClientHost = InvokeApprove(approve, network, NetworkManager.ServerClientId);
        Assert.That(onlineClientHost.Approved, Is.False);
        Assert.That(onlineClientHost.Reason, Is.EqualTo(invalidReason));
    }

    static NetworkManager.ConnectionApprovalResponse InvokeApprove(MethodInfo approve, LobbyNetwork network, ulong clientId)
    {
        var response = new NetworkManager.ConnectionApprovalResponse();
        approve.Invoke(network, new object[]
        {
            new NetworkManager.ConnectionApprovalRequest { ClientNetworkId = clientId },
            response
        });
        return response;
    }

    LobbyServer CreateServer(int maxRooms)
    {
        root = new GameObject("LobbyServerTests");
        root.SetActive(false);
        manager = root.AddComponent<NetworkManager>();
        // PlayMode 전환 뒤 NGO의 OnNetworkManagerReset 정적 훅이 비면 AddComponent<NetworkManager>()만으로는 NetworkConfig가 null이다.
        if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
        root.SetActive(true);
        if (typeof(NetworkManager).GetField("Log", PrivateInstance).GetValue(manager) == null)
        {
            typeof(NetworkManager).GetMethod("Awake", PrivateInstance).Invoke(manager, null);
            initializedManager = true;
        }
        return new LobbyServer(() => manager, () => null, () => null, () => maxRooms, () => 5f);
    }

    static NetworkManager.ConnectionApprovalResponse Approve(LobbyServer server, ulong clientId, LobbyJoinRequest payload)
    {
        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteNetworkSerializable(payload);
        var response = new NetworkManager.ConnectionApprovalResponse();
        server.ApproveConnection(new NetworkManager.ConnectionApprovalRequest
        {
            ClientNetworkId = clientId,
            Payload = writer.ToArray()
        }, response);
        return response;
    }
}
#endif
