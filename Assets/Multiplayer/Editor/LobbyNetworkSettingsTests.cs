#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class LobbyNetworkSettingsTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    GameObject root;
    LobbyNetwork network;
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
    public void EarlierSnapshotKeepsNewerHostEditPending_UntilLatestRevisionArrives()
    {
        CreateNetwork(2);
        var pendingAtSnapshot = new List<bool>();
        var received = new List<NetworkLobbySnapshot>();
        network.SnapshotChanged += snapshot =>
        {
            pendingAtSnapshot.Add(network.HasPendingSettings);
            received.Add(snapshot);
        };

        SendSnapshot(Snapshot(1, 111, MapShape.Donut));
        Assert.That(network.HasPendingSettings, Is.True);
        Assert.That(pendingAtSnapshot, Is.EqualTo(new[] { true }));

        SendSnapshot(Snapshot(2, 222, MapShape.Circle));
        Assert.That(network.HasPendingSettings, Is.False);
        Assert.That(pendingAtSnapshot, Is.EqualTo(new[] { true, false }));
        Assert.That(received[1].Settings.ToSettings().Map.Seed, Is.EqualTo(222));
        Assert.That(received[1].Settings.ToSettings().Map.Shape, Is.EqualTo(MapShape.Circle));
    }

    [Test]
    public void OlderSnapshotAfterLatestRevisionDoesNotPublishOrReplaceLatestState()
    {
        CreateNetwork(2);
        var received = new List<NetworkLobbySnapshot>();
        network.SnapshotChanged += received.Add;

        SendSnapshot(Snapshot(2, 222, MapShape.Circle));
        Assert.That(network.HasPendingSettings, Is.False);
        SendSnapshot(Snapshot(1, 111, MapShape.Donut));

        Assert.That(received.Count, Is.EqualTo(1));
        Assert.That(received[0].Settings.ToSettings().Map.Seed, Is.EqualTo(222));
        Assert.That(received[0].Settings.ToSettings().Map.Shape, Is.EqualTo(MapShape.Circle));
        NetworkLobbySnapshot current = (NetworkLobbySnapshot)typeof(LobbyNetwork)
            .GetField("currentSnapshot", PrivateInstance).GetValue(network);
        Assert.That(current.SettingsRevision, Is.EqualTo(2));
        Assert.That(current.Settings.ToSettings().Map.Seed, Is.EqualTo(222));
    }

    [Test]
    public void ErrorForEarlierRequestDoesNotSettleLatestSettingsRequest()
    {
        CreateNetwork(2);
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(1u);
        writer.WriteValueSafe("Earlier update was rejected.");
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        Invoke("OnError", NetworkManager.ServerClientId, reader);

        Assert.That(network.HasPendingSettings, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelIgnoresLateSnapshots_AndPublishesClosedOnceOnlyAfterJoining(bool acceptedSnapshot)
    {
        CreateNetwork(1);
        SetField("inLobby", false);
        SetField("connecting", true);
        var received = new List<NetworkLobbySnapshot>();
        var closed = new List<string>();
        int failed = 0;
        network.SnapshotChanged += received.Add;
        network.Closed += closed.Add;
        network.Failed += _ => failed++;
        if (acceptedSnapshot) SendSnapshot(Snapshot(0, 100, MapShape.Random));
        Assert.That(network.IsInLobby, Is.EqualTo(acceptedSnapshot));

        network.Cancel();
        network.Cancel();
        SendSnapshot(Snapshot(1, 222, MapShape.Circle));
        SendSnapshot(Snapshot(1, 222, MapShape.Circle));
        Assert.That(network.IsInLobby, Is.False);
        Assert.That(network.IsBusy, Is.True);
        Assert.That(received.Count, Is.EqualTo(acceptedSnapshot ? 1 : 0));
        Assert.That(closed, Is.Empty);

        // 실제 Update의 종료 완료 경로를 호출한다. 소켓 없는 manager는 이미 중지된 상태다.
        Invoke("Update");
        Assert.That(network.IsBusy, Is.False);
        Assert.That(closed.Count, Is.EqualTo(acceptedSnapshot ? 1 : 0));
        if (acceptedSnapshot) Assert.That(closed[0], Is.Empty);
        Assert.That(failed, Is.Zero);

        network.Cancel();
        SendSnapshot(Snapshot(1, 222, MapShape.Circle));
        Invoke("Update");
        Assert.That(network.IsInLobby, Is.False);
        Assert.That(received.Count, Is.EqualTo(acceptedSnapshot ? 1 : 0));
        Assert.That(closed.Count, Is.EqualTo(acceptedSnapshot ? 1 : 0));
        Assert.That(failed, Is.Zero);
    }

    void CreateNetwork(uint requestedRevision)
    {
        root = new GameObject("LobbyNetworkSettingsTests");
        root.SetActive(false);
        manager = root.AddComponent<NetworkManager>();
        network = root.AddComponent<LobbyNetwork>();
        SetField("manager", manager);
        root.SetActive(true);
        // EditMode에서는 Awake가 자동 실행되지 않는다. NGO의 실제 초기화/종료를 짝지어 호출한다.
        if (typeof(NetworkManager).GetField("Log", PrivateInstance).GetValue(manager) == null)
        {
            typeof(NetworkManager).GetMethod("Awake", PrivateInstance).Invoke(manager, null);
            initializedManager = true;
        }

        SetField("inLobby", true);
        SetField("connecting", false);
        SetField("closing", false);
        SetField("createdRoom", true);
        SetField("requestedSettingsRevision", requestedRevision);
        SetField("settledSettingsRevision", 0u);
        SetField("currentSnapshot", Snapshot(0, 100, MapShape.Random));
    }

    NetworkLobbySnapshot Snapshot(uint revision, int seed, MapShape shape) => new NetworkLobbySnapshot
    {
        Code = "123456",
        HostClientId = manager.LocalClientId,
        OpponentPresent = false,
        GuestClientId = 0,
        SettingsRevision = revision,
        Settings = new NetworkLobbySettings(new DuelSettings
        {
            SeedFixed = true,
            Map = new MapGenerationSettings { Seed = seed, Shape = shape }
        })
    };

    void SendSnapshot(NetworkLobbySnapshot snapshot)
    {
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteNetworkSerializable(snapshot);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        Invoke("OnSnapshot", NetworkManager.ServerClientId, reader);
    }

    void Invoke(string methodName, ulong senderId, FastBufferReader reader) =>
        typeof(LobbyNetwork).GetMethod(methodName, PrivateInstance)
            .Invoke(network, new object[] { senderId, reader });

    void Invoke(string methodName) =>
        typeof(LobbyNetwork).GetMethod(methodName, PrivateInstance).Invoke(network, null);

    void SetField(string name, object value) =>
        typeof(LobbyNetwork).GetField(name, PrivateInstance).SetValue(network, value);
}
#endif
