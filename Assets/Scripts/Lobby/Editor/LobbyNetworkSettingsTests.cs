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
    object client;
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
        Assert.That(received[1].Settings.ToSettings().Seed, Is.EqualTo(222));
        Assert.That(received[1].Settings.ToSettings().Shape, Is.EqualTo(MapShape.Circle));
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
        Assert.That(received[0].Settings.ToSettings().Seed, Is.EqualTo(222));
        Assert.That(received[0].Settings.ToSettings().Shape, Is.EqualTo(MapShape.Circle));
        NetworkLobbySnapshot current = GetField<NetworkLobbySnapshot>("currentSnapshot");
        Assert.That(current.SettingsRevision, Is.EqualTo(2));
        Assert.That(current.Settings.ToSettings().Seed, Is.EqualTo(222));
    }

    [Test]
    public void ErrorForEarlierRequestDoesNotSettleLatestSettingsRequest()
    {
        CreateNetwork(2);
        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(1u);
        writer.WriteValueSafe((byte)LobbyReason.SettingsOutOfDate);
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
        var closed = new List<LobbyReason>();
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
        if (acceptedSnapshot) Assert.That(closed[0], Is.EqualTo(LobbyReason.None));
        Assert.That(failed, Is.Zero);

        network.Cancel();
        SendSnapshot(Snapshot(1, 222, MapShape.Circle));
        Invoke("Update");
        Assert.That(network.IsInLobby, Is.False);
        Assert.That(received.Count, Is.EqualTo(acceptedSnapshot ? 1 : 0));
        Assert.That(closed.Count, Is.EqualTo(acceptedSnapshot ? 1 : 0));
        Assert.That(failed, Is.Zero);
    }

    [Test]
    public void RequestStart_IsRefusedUnlessHostWithOpponentInLobby_AndNotAlreadyPending()
    {
        CreateNetwork(0);
        Assert.That(network.RequestStart(), Is.False);

        SetField("currentSnapshot", GuestSnapshot(LobbyPhase.Lobby));
        Assert.That(network.IsHost, Is.False);
        Assert.That(network.RequestStart(), Is.False);

        SetField("currentSnapshot", Snapshot(0, 100, MapShape.Random, LobbyPhase.Preparing));
        Assert.That(network.IsStarting, Is.True);
        Assert.That(network.RequestStart(), Is.False);

        SetField("currentSnapshot", Snapshot(0, 100, MapShape.Random, LobbyPhase.Lobby));
        SetField("startPending", true);
        Assert.That(network.IsStarting, Is.True);
        Assert.That(network.RequestStart(), Is.False);
        Assert.That(GetField<uint>("requestedStart"), Is.Zero);
    }

    [Test]
    public void StartRejection_IsAcceptedOnlyForTheLatestPendingRequest()
    {
        CreateNetwork(0);
        SetField("currentSnapshot", Snapshot(0, 100, MapShape.Random, LobbyPhase.Lobby));
        SetField("requestedStart", 2u);
        SetField("startPending", true);
        var rejected = new List<LobbyReason>();
        network.StartRejected += rejected.Add;

        SendStartRejection(1, LobbyReason.SettingsOutOfDate);
        SendStartRejection(3, LobbyReason.AlreadyStarted);
        Assert.That(rejected, Is.Empty);
        Assert.That(network.IsStarting, Is.True);

        SendStartRejection(2, LobbyReason.AlreadyStarted);
        SendStartRejection(2, LobbyReason.AlreadyStarted);
        Assert.That(rejected, Is.EqualTo(new[] { LobbyReason.AlreadyStarted }));
        Assert.That(network.IsStarting, Is.False);
        Assert.That(network.IsInLobby, Is.True);
    }

    [Test]
    public void PreparingSnapshotWithoutBattleMap_LeavesRoomInsteadOfPublishing()
    {
        CreateNetwork(0);
        SetField("currentSnapshot", Snapshot(0, 100, MapShape.Random, LobbyPhase.Lobby));
        SetField("requestedStart", 1u);
        SetField("startPending", true);
        var received = new List<NetworkLobbySnapshot>();
        var rejected = new List<LobbyReason>();
        var closed = new List<LobbyReason>();
        network.SnapshotChanged += received.Add;
        network.StartRejected += rejected.Add;
        network.Closed += closed.Add;

        SendSnapshot(Snapshot(0, 100, MapShape.Random, LobbyPhase.Preparing));
        SendStartRejection(1, LobbyReason.AlreadyStarted);
        Assert.That(received, Is.Empty);
        Assert.That(network.IsInLobby, Is.False);
        Assert.That(network.IsStarting, Is.False);

        Invoke("Update");
        Assert.That(closed, Is.EqualTo(new[] { LobbyReason.MapGenerationFailed }));
        Assert.That(rejected, Is.Empty);
    }

    [Test]
    public void BattleSnapshotAfterPreparing_IsPublishedWithoutBuildingTheMapAgain()
    {
        CreateNetwork(0);
        SetField("currentSnapshot", GuestSnapshot(LobbyPhase.Preparing));
        SetField("createdRoom", false);
        var received = new List<NetworkLobbySnapshot>();
        network.SnapshotChanged += received.Add;

        SendSnapshot(GuestSnapshot(LobbyPhase.Battle));
        Assert.That(received.Count, Is.EqualTo(1));
        Assert.That(received[0].Phase, Is.EqualTo(LobbyPhase.Battle));
        Assert.That(network.IsInLobby, Is.True);
        Assert.That(network.IsStarting, Is.True);
    }

    [Test]
    public void SnapshotCannotMoveRoomBackToAnEarlierPhase()
    {
        CreateNetwork(0);
        SetField("currentSnapshot", Snapshot(0, 100, MapShape.Random, LobbyPhase.Battle));
        var received = new List<NetworkLobbySnapshot>();
        network.SnapshotChanged += received.Add;

        SendSnapshot(Snapshot(0, 100, MapShape.Random, LobbyPhase.Lobby));
        Assert.That(received, Is.Empty);
        Assert.That(network.IsInLobby, Is.False);
    }

    [TestCase(LobbyPhase.Preparing)]
    [TestCase(LobbyPhase.Battle)]
    public void JoiningRoomThatAlreadyStarted_IsRejected(LobbyPhase phase)
    {
        CreateNetwork(0);
        SetField("inLobby", false);
        SetField("connecting", true);
        SetField("createdRoom", false);
        SetField("requestedCode", "123456");
        SetField("currentSnapshot", default(NetworkLobbySnapshot));
        var received = new List<NetworkLobbySnapshot>();
        network.SnapshotChanged += received.Add;

        SendSnapshot(GuestSnapshot(phase));
        Assert.That(received, Is.Empty);
        Assert.That(network.IsInLobby, Is.False);
        Assert.That(network.IsBusy, Is.True);
    }

    void CreateNetwork(uint requestedRevision)
    {
        root = new GameObject("LobbyNetworkSettingsTests");
        root.SetActive(false);
        manager = root.AddComponent<NetworkManager>();
        network = root.AddComponent<LobbyNetwork>();
        typeof(LobbyNetwork).GetField("manager", PrivateInstance).SetValue(network, manager);
        client = typeof(LobbyNetwork).GetProperty("Client", PrivateInstance).GetValue(network);
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
        Settings = new NetworkMapSettings(new MapGenerationSettings { Seed = seed, Shape = shape })
    };

    // 방장 쪽에서 본, 상대가 들어온 방.
    NetworkLobbySnapshot Snapshot(uint revision, int seed, MapShape shape, LobbyPhase phase)
    {
        NetworkLobbySnapshot snapshot = Snapshot(revision, seed, shape);
        snapshot.OpponentPresent = true;
        snapshot.GuestClientId = manager.LocalClientId + 1;
        snapshot.Phase = phase;
        return snapshot;
    }

    // 참가자 쪽에서 본 방.
    NetworkLobbySnapshot GuestSnapshot(LobbyPhase phase)
    {
        NetworkLobbySnapshot snapshot = Snapshot(0, 100, MapShape.Random, phase);
        snapshot.HostClientId = manager.LocalClientId + 1;
        snapshot.GuestClientId = manager.LocalClientId;
        return snapshot;
    }

    void SendStartRejection(uint request, LobbyReason reason)
    {
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteValueSafe(request);
        writer.WriteValueSafe((byte)reason);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        Invoke("OnStartRejected", NetworkManager.ServerClientId, reader);
    }

    T GetField<T>(string name) => (T)client.GetType().GetField(name, PrivateInstance).GetValue(client);

    void SendSnapshot(NetworkLobbySnapshot snapshot)
    {
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteNetworkSerializable(snapshot);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        Invoke("OnSnapshot", NetworkManager.ServerClientId, reader);
    }

    void Invoke(string methodName, ulong senderId, FastBufferReader reader) =>
        client.GetType().GetMethod(methodName, PrivateInstance)
            .Invoke(client, new object[] { senderId, reader });

    void Invoke(string methodName) =>
        typeof(LobbyNetwork).GetMethod(methodName, PrivateInstance).Invoke(network, null);

    void SetField(string name, object value) =>
        client.GetType().GetField(name, PrivateInstance).SetValue(client, value);
}
#endif
