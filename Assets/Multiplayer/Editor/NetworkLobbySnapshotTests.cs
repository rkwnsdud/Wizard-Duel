#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkLobbySnapshotTests
{
    static DuelSettings Settings(int seed = 7301, bool fixedSeed = true, Vector2Int? size = null) => new DuelSettings
    {
        SeedFixed = fixedSeed,
        Map = new MapGenerationSettings
        {
            Seed = seed,
            Size = size,
            Shape = MapShape.Random,
            ObstacleDensity = 0.27f,
            Symmetry = MapSymmetry.Rotate180,
            NoiseStrength = 0.65f,
            NoiseScale = 1.25f
        }
    };

    static LobbySession Session(FuncCounter seeds, int code = 234567) =>
        new LobbySession(seeds.Next, () => code);

    [TestCase(7301, false, false)]
    [TestCase(0, true, true)]
    [TestCase(-11, false, true)]
    public void WireRoundTrip_PreservesLobbySettingsAndRandomLayout(int seed, bool fixedSize, bool fixedSeed)
    {
        var settings = Settings(seed, fixedSeed, fixedSize ? new Vector2Int(35, 29) : null);
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "234567",
            HostClientId = 17,
            OpponentPresent = true,
            GuestClientId = 29,
            SettingsRevision = 37,
            Settings = new NetworkLobbySettings(settings)
        };

        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteNetworkSerializable(snapshot);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out NetworkLobbySnapshot received);

        Assert.That(received.Code, Is.EqualTo(snapshot.Code));
        Assert.That(received.HostClientId, Is.EqualTo(snapshot.HostClientId));
        Assert.That(received.OpponentPresent, Is.EqualTo(snapshot.OpponentPresent));
        Assert.That(received.GuestClientId, Is.EqualTo(snapshot.GuestClientId));
        Assert.That(received.SettingsRevision, Is.EqualTo(37u));
        DuelSettings restored = received.Settings.ToSettings();
        Assert.That(restored.SeedFixed, Is.EqualTo(settings.SeedFixed));
        Assert.That(restored.Map.Seed, Is.EqualTo(seed));
        Assert.That(restored.Map.Size, Is.EqualTo(settings.Map.Size));
        Assert.That(restored.Map.Shape, Is.EqualTo(settings.Map.Shape));
        Assert.That(restored.Map.ObstacleDensity, Is.EqualTo(settings.Map.ObstacleDensity));
        Assert.That(restored.Map.Symmetry, Is.EqualTo(settings.Map.Symmetry));
        Assert.That(restored.Map.NoiseStrength, Is.EqualTo(settings.Map.NoiseStrength));
        Assert.That(restored.Map.NoiseScale, Is.EqualTo(settings.Map.NoiseScale));

        MapLayout sourceLayout = MapLayoutGenerator.Generate(settings.Map);
        MapLayout receivedLayout = MapLayoutGenerator.Generate(restored.ToMapSettings());
        Assert.That(receivedLayout.ToLayout(), Is.EqualTo(sourceLayout.ToLayout()));
        Assert.That(receivedLayout.SpawnA, Is.EqualTo(sourceLayout.SpawnA));
        Assert.That(receivedLayout.SpawnB, Is.EqualTo(sourceLayout.SpawnB));
    }

    [Test]
    public void AutomaticSeed_IsChosenOnceAtCreationAndKeptAcrossUpdatesAndJoin()
    {
        var seeds = new FuncCounter(4100, 4200, 4300);
        var session = Session(seeds);
        Assert.That(session.TryCreate(1, Settings(8, false), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(4100));
        Assert.That(seeds.Calls, Is.EqualTo(1));
        Assert.That(session.Confirm(1), Is.True);

        Assert.That(session.TryUpdateSettings(1, Settings(-99, false), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(4100));
        Assert.That(seeds.Calls, Is.EqualTo(1));
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(4100));
        Assert.That(seeds.Calls, Is.EqualTo(1));
    }

    [Test]
    public void SeedModeTransitions_KeepOrDrawExactlyWhenRequired()
    {
        var seeds = new FuncCounter(5100, 5200, 5300);
        var session = Session(seeds);
        Assert.That(session.TryCreate(1, Settings(77, false), out _), Is.True);
        Assert.That(session.Confirm(1), Is.True);

        Assert.That(session.TryUpdateSettings(1, Settings(-5, true), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(5100));
        Assert.That(seeds.Calls, Is.EqualTo(1));
        Assert.That(session.TryUpdateSettings(1, Settings(880, true), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(880));
        Assert.That(session.TryUpdateSettings(1, Settings(-10, false), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(5200));
        Assert.That(session.TryUpdateSettings(1, Settings(999, false), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(5200));
        Assert.That(seeds.Calls, Is.EqualTo(2));
    }

    [Test]
    public void StaleSeedDraftDoesNotUndoAutomaticSeedAfterRapidModeChanges()
    {
        var seeds = new FuncCounter(5100, 5200);
        var session = Session(seeds);
        Assert.That(session.TryCreate(1, Settings(100), out _), Is.True);
        Assert.That(session.Confirm(1), Is.True);

        Assert.That(session.TryUpdateSettings(1, Settings(100, fixedSeed: false), out _, seedEdited: false), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(5100));

        Assert.That(session.TryUpdateSettings(1, Settings(100, fixedSeed: true), out _, seedEdited: false), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(5100));

        DuelSettings staleDraft = Settings(100);
        staleDraft.Map.Shape = MapShape.Donut;
        staleDraft.Map.ObstacleDensity = 0.31f;
        Assert.That(session.TryUpdateSettings(1, staleDraft, out _, seedEdited: false), Is.True);
        DuelSettings afterStaleDraft = session.Snapshot.Settings.ToSettings();
        Assert.That(afterStaleDraft.Map.Seed, Is.EqualTo(5100));
        Assert.That(afterStaleDraft.Map.Shape, Is.EqualTo(MapShape.Donut));
        Assert.That(afterStaleDraft.Map.ObstacleDensity, Is.EqualTo(0.31f));

        DuelSettings explicitSeedEdit = Settings(880);
        explicitSeedEdit.Map.Shape = MapShape.Donut;
        explicitSeedEdit.Map.ObstacleDensity = 0.31f;
        Assert.That(session.TryUpdateSettings(1, explicitSeedEdit, out _, seedEdited: true), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(880));
        Assert.That(seeds.Calls, Is.EqualTo(1));
    }

    [Test]
    public void FixedSeedRoomUsesSuppliedSeed_AndSettingsCopiesAreIsolated()
    {
        var seeds = new FuncCounter(6100);
        var session = Session(seeds);
        Assert.That(session.TryCreate(1, Settings(-37), out _), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(-37));
        Assert.That(seeds.Calls, Is.Zero);

        DuelSettings returned = session.Snapshot.Settings.ToSettings();
        returned.Map.Seed = 900;
        returned.Map.Shape = MapShape.Donut;
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(-37));
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Shape, Is.EqualTo(MapShape.Random));

        NetworkLobbySnapshot snapshot = session.Snapshot;
        snapshot.Settings.Map.Seed = 902;
        DuelSettings snapshotSettings = snapshot.Settings.ToSettings();
        snapshotSettings.Map.Seed = 901;
        Assert.That(session.Snapshot.Settings.ToSettings().Map.Seed, Is.EqualTo(-37));
    }

    [Test]
    public void LateJoinSnapshotContainsLatestFullSettings()
    {
        var session = Session(new FuncCounter(6200));
        Assert.That(session.TryCreate(1, Settings(1), out _), Is.True);
        Assert.That(session.Confirm(1), Is.True);

        DuelSettings latest = new DuelSettings
        {
            SeedFixed = true,
            Map = new MapGenerationSettings
            {
                Seed = -321,
                Size = new Vector2Int(41, 37),
                Shape = MapShape.Donut,
                ObstacleDensity = 0.33f,
                Symmetry = MapSymmetry.None,
                NoiseStrength = 0.9f,
                NoiseScale = 1.8f
            }
        };
        Assert.That(session.TryUpdateSettings(1, latest, out string reason), Is.True, reason);
        Assert.That(session.TryJoin(2, session.RoomCode, out string joinReason), Is.True, joinReason);
        Assert.That(session.Confirm(2), Is.True);

        NetworkLobbySnapshot lateJoinSnapshot = session.Snapshot;
        Assert.That(lateJoinSnapshot.OpponentPresent, Is.True);
        AssertSettingsEqual(latest, lateJoinSnapshot.Settings.ToSettings());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ServerSnapshotCloneGeneratesSameRandomMapAndSpawns(bool fixedSize)
    {
        var session = Session(new FuncCounter(6300));
        DuelSettings requested = Settings(999, fixedSeed: false,
            size: fixedSize ? new Vector2Int(35, 29) : null);
        Assert.That(session.TryCreate(1, requested, out _), Is.True);

        NetworkLobbySnapshot serverSnapshot = session.Snapshot;
        DuelSettings clonedSettings = serverSnapshot.Settings.ToSettings();
        MapLayout serverLayout = MapLayoutGenerator.Generate(serverSnapshot.Settings.ToSettings().ToMapSettings());
        MapLayout cloneLayout = MapLayoutGenerator.Generate(clonedSettings.ToMapSettings());

        Assert.That(cloneLayout.ToLayout(), Is.EqualTo(serverLayout.ToLayout()));
        Assert.That(cloneLayout.SpawnA, Is.EqualTo(serverLayout.SpawnA));
        Assert.That(cloneLayout.SpawnB, Is.EqualTo(serverLayout.SpawnB));
    }

    [Test]
    public void DuelSettingsMapConversionAndClonePreserveTheStoredSeed()
    {
        foreach (int seed in new[] { 0, -13, 42 })
        {
            DuelSettings source = Settings(seed, fixedSeed: false);
            Assert.That(source.ToMapSettings().Seed, Is.EqualTo(seed));
            Assert.That(source.Clone().Map.Seed, Is.EqualTo(seed));
        }
    }

    [Test]
    public void ValidationRejectsInvalidEnumsNonFiniteNumbersAndOutOfRangeMapValues()
    {
        AssertInvalid(s => s.Map.Shape = (MapShape)999);
        AssertInvalid(s => s.Map.Symmetry = (MapSymmetry)999);
        AssertInvalid(s => s.Map.ObstacleDensity = float.NaN);
        AssertInvalid(s => s.Map.ObstacleDensity = float.PositiveInfinity);
        AssertInvalid(s => s.Map.ObstacleDensity = -0.01f);
        AssertInvalid(s => s.Map.ObstacleDensity = DuelSettings.MaxObstacleDensity + 0.01f);
        AssertInvalid(s => s.Map.NoiseStrength = float.NaN);
        AssertInvalid(s => s.Map.NoiseStrength = float.PositiveInfinity);
        AssertInvalid(s => s.Map.NoiseStrength = float.NegativeInfinity);
        AssertInvalid(s => s.Map.NoiseStrength = -0.01f);
        AssertInvalid(s => s.Map.NoiseStrength = 1.01f);
        AssertInvalid(s => s.Map.NoiseScale = float.PositiveInfinity);
        AssertInvalid(s => s.Map.NoiseScale = float.NaN);
        AssertInvalid(s => s.Map.NoiseScale = 0.24f);
        AssertInvalid(s => s.Map.NoiseScale = 2.01f);
        AssertInvalid(s => s.Map.Size = new Vector2Int(DuelSettings.MinSize - 1, DuelSettings.MinSize));
        AssertInvalid(s => s.Map.Size = new Vector2Int(DuelSettings.MaxSize + 1, DuelSettings.MaxSize));
    }

    static void AssertInvalid(System.Action<DuelSettings> change)
    {
        DuelSettings settings = Settings();
        change(settings);
        Assert.That(LobbySession.ValidateSettings(settings, out string reason), Is.False);
        Assert.That(reason, Is.Not.Empty);
    }

    static void AssertSettingsEqual(DuelSettings expected, DuelSettings actual)
    {
        Assert.That(actual.SeedFixed, Is.EqualTo(expected.SeedFixed));
        Assert.That(actual.Map.Seed, Is.EqualTo(expected.Map.Seed));
        Assert.That(actual.Map.Size, Is.EqualTo(expected.Map.Size));
        Assert.That(actual.Map.Shape, Is.EqualTo(expected.Map.Shape));
        Assert.That(actual.Map.ObstacleDensity, Is.EqualTo(expected.Map.ObstacleDensity));
        Assert.That(actual.Map.Symmetry, Is.EqualTo(expected.Map.Symmetry));
        Assert.That(actual.Map.NoiseStrength, Is.EqualTo(expected.Map.NoiseStrength));
        Assert.That(actual.Map.NoiseScale, Is.EqualTo(expected.Map.NoiseScale));
    }

    sealed class FuncCounter
    {
        readonly int[] values;
        int index;
        public int Calls => index;
        public FuncCounter(params int[] values) => this.values = values;
        public int Next() => values[index++];
    }
}
#endif
