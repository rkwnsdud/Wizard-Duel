#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkLobbySnapshotTests
{
    static DuelSettings Settings(int seed = 7301, Vector2Int? size = null) => new DuelSettings
    {
        Map = new MapGenerationSettings
        {
            Seed = seed,
            Size = size,
            Shape = MapShape.Random,
            ObstacleDensity = 0.22f,
            BushDensity = 0.09f,
            Symmetry = MapSymmetry.Rotate180,
            NoiseStrength = 0.65f,
            NoiseScale = 1.25f
        }
    };

    static LobbySession Session(int code = 234567) => new LobbySession(() => code);

    [TestCase(7301, false)]
    [TestCase(0, true)]
    [TestCase(-11, false)]
    public void WireRoundTrip_PreservesLobbySettingsAndRandomLayout(int seed, bool fixedSize)
    {
        var settings = Settings(seed, fixedSize ? new Vector2Int(35, 29) : null);
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "234567",
            HostClientId = 17,
            OpponentPresent = true,
            GuestClientId = 29,
            SettingsRevision = 37,
            Settings = new NetworkMapSettings(settings.Map)
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
        DuelSettings restored = new DuelSettings { Map = received.Settings.ToSettings() };
        Assert.That(restored.Map.Seed, Is.EqualTo(seed));
        Assert.That(restored.Map.Size, Is.EqualTo(settings.Map.Size));
        Assert.That(restored.Map.Shape, Is.EqualTo(settings.Map.Shape));
        Assert.That(restored.Map.ObstacleDensity, Is.EqualTo(settings.Map.ObstacleDensity));
        Assert.That(restored.Map.BushDensity, Is.EqualTo(settings.Map.BushDensity));
        Assert.That(restored.Map.Symmetry, Is.EqualTo(settings.Map.Symmetry));
        Assert.That(restored.Map.NoiseStrength, Is.EqualTo(settings.Map.NoiseStrength));
        Assert.That(restored.Map.NoiseScale, Is.EqualTo(settings.Map.NoiseScale));

        MapLayout sourceLayout = MapLayoutGenerator.Generate(settings.Map);
        MapLayout receivedLayout = MapLayoutGenerator.Generate(restored.ToMapSettings());
        Assert.That(receivedLayout.ToLayout(), Is.EqualTo(sourceLayout.ToLayout()));
        Assert.That(receivedLayout.SpawnA, Is.EqualTo(sourceLayout.SpawnA));
        Assert.That(receivedLayout.SpawnB, Is.EqualTo(sourceLayout.SpawnB));
    }

    [TestCase(LobbyPhase.Lobby)]
    [TestCase(LobbyPhase.Preparing)]
    [TestCase(LobbyPhase.Battle)]
    [TestCase(LobbyPhase.Result)]
    public void WireRoundTrip_PreservesPhaseRoundAndWinner(LobbyPhase phase)
    {
        byte winner = phase == LobbyPhase.Result ? (byte)1 : (byte)0;
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "234567",
            HostClientId = 17,
            OpponentPresent = true,
            GuestClientId = 29,
            SettingsRevision = 4,
            Round = 3,
            Phase = phase,
            WinnerSlot = winner,
            Settings = new NetworkMapSettings(Settings().Map)
        };

        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteNetworkSerializable(snapshot);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out NetworkLobbySnapshot received);

        Assert.That(reader.Position, Is.EqualTo(reader.Length));
        Assert.That(received.Phase, Is.EqualTo(phase));
        Assert.That(received.Round, Is.EqualTo(3u));
        Assert.That(received.WinnerSlot, Is.EqualTo(winner));
        Assert.That(received.SettingsRevision, Is.EqualTo(4u));
        Assert.That(received.IsValidFor(17), Is.True);
        Assert.That(received.IsValidFor(29), Is.True);
    }

    [Test]
    public void StartedPhaseNeedsOpponent_AndUnknownPhaseIsInvalid()
    {
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "234567",
            HostClientId = 17,
            Phase = LobbyPhase.Lobby,
            Settings = new NetworkMapSettings(Settings().Map)
        };
        Assert.That(snapshot.IsValidFor(17), Is.True);

        foreach (LobbyPhase started in new[] { LobbyPhase.Preparing, LobbyPhase.Battle, LobbyPhase.Result })
        {
            snapshot.Phase = started;
            Assert.That(snapshot.IsValidFor(17), Is.False);
        }

        snapshot.OpponentPresent = true;
        snapshot.GuestClientId = 29;
        snapshot.Phase = (LobbyPhase)4;
        Assert.That(snapshot.IsValidFor(17), Is.False);
        snapshot.Phase = (LobbyPhase)255;
        Assert.That(snapshot.IsValidFor(29), Is.False);
    }

    [Test]
    public void OnlyResultCarriesAWinner_AndItMustBeBlueOrRed()
    {
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "234567",
            HostClientId = 17,
            OpponentPresent = true,
            GuestClientId = 29,
            Phase = LobbyPhase.Result,
            Settings = new NetworkMapSettings(Settings().Map)
        };
        snapshot.WinnerSlot = 0;
        Assert.That(snapshot.IsValidFor(17), Is.True);
        snapshot.WinnerSlot = 1;
        Assert.That(snapshot.IsValidFor(29), Is.True);
        snapshot.WinnerSlot = 2;
        Assert.That(snapshot.IsValidFor(17), Is.False);

        foreach (LobbyPhase phase in new[] { LobbyPhase.Lobby, LobbyPhase.Preparing, LobbyPhase.Battle })
        {
            snapshot.Phase = phase;
            snapshot.WinnerSlot = 1;
            Assert.That(snapshot.IsValidFor(17), Is.False, phase.ToString());
            snapshot.WinnerSlot = 0;
            Assert.That(snapshot.IsValidFor(17), Is.True, phase.ToString());
        }
    }

    [Test]
    public void Follows_MovesForwardWithinARound_AndBackToLobbyOnlyRightAfterResult()
    {
        NetworkLobbySnapshot At(uint round, LobbyPhase phase, uint revision = 4) => new NetworkLobbySnapshot
        {
            Code = "234567",
            HostClientId = 17,
            OpponentPresent = true,
            GuestClientId = 29,
            SettingsRevision = revision,
            Round = round,
            Phase = phase,
            WinnerSlot = phase == LobbyPhase.Result ? (byte)1 : (byte)0,
            Settings = new NetworkMapSettings(Settings().Map)
        };

        NetworkLobbySnapshot lobby = At(0, LobbyPhase.Lobby);
        NetworkLobbySnapshot preparing = At(0, LobbyPhase.Preparing);
        NetworkLobbySnapshot battle = At(0, LobbyPhase.Battle);
        NetworkLobbySnapshot result = At(0, LobbyPhase.Result);
        Assert.That(lobby.Follows(lobby), Is.True);
        Assert.That(At(0, LobbyPhase.Lobby, 5).Follows(lobby), Is.True);
        Assert.That(preparing.Follows(lobby), Is.True);
        Assert.That(battle.Follows(preparing), Is.True);
        Assert.That(result.Follows(battle), Is.True);
        Assert.That(lobby.Follows(battle), Is.False);
        Assert.That(battle.Follows(result), Is.False);

        NetworkLobbySnapshot nextLobby = At(1, LobbyPhase.Lobby);
        Assert.That(nextLobby.Follows(result), Is.True);
        Assert.That(At(1, LobbyPhase.Preparing).Follows(nextLobby), Is.True);
        // 결과 없이 다음 판으로 넘어가거나, 다음 판을 로비가 아닌 단계로 시작하거나, 판을 건너뛰지 않는다.
        Assert.That(nextLobby.Follows(battle), Is.False);
        Assert.That(At(1, LobbyPhase.Preparing).Follows(result), Is.False);
        Assert.That(At(2, LobbyPhase.Lobby).Follows(result), Is.False);
        Assert.That(result.Follows(nextLobby), Is.False);

        // 다른 방, 다른 방장, 줄어든 설정 번호는 이어지지 않는다.
        NetworkLobbySnapshot otherRoom = nextLobby;
        otherRoom.Code = "345678";
        Assert.That(otherRoom.Follows(result), Is.False);
        NetworkLobbySnapshot otherHost = nextLobby;
        otherHost.HostClientId = 99;
        Assert.That(otherHost.Follows(result), Is.False);
        Assert.That(At(1, LobbyPhase.Lobby, 3).Follows(result), Is.False);
    }

    // LobbyNetwork는 모든 메시지를 256바이트 버퍼로 보낸다. 가장 큰 값으로 채워도 그 안에 들어가야 한다.
    [Test]
    public void LargestSnapshotAndStartMessagesFitLobbyBuffer()
    {
        const int bufferSize = 256;
        var snapshot = new NetworkLobbySnapshot
        {
            Code = "999999",
            HostClientId = ulong.MaxValue,
            OpponentPresent = true,
            GuestClientId = ulong.MaxValue - 1,
            SettingsRevision = uint.MaxValue,
            Round = uint.MaxValue,
            Phase = LobbyPhase.Result,
            WinnerSlot = 1,
            Settings = new NetworkMapSettings(WithMapSizeAndRanges(
                new Vector2Int(DuelSettings.MaxSize, DuelSettings.MaxSize), DuelSettings.MaxObstacleDensity, 1, 2,
                DuelSettings.MaxBushDensity).Map)
        };
        using (var writer = new FastBufferWriter(bufferSize, Allocator.Temp))
        {
            writer.WriteNetworkSerializable(snapshot);
            Assert.That(writer.Length, Is.LessThanOrEqualTo(bufferSize));
        }

        LobbyReason[] reasons =
        {
            LobbyReason.InvalidRequest,
            LobbyReason.NotHost,
            LobbyReason.OpponentMissing,
            LobbyReason.AlreadyStarted,
            LobbyReason.MapGenerationFailed
        };
        foreach (LobbyReason reason in reasons)
        {
            using var rejection = new FastBufferWriter(bufferSize, Allocator.Temp);
            rejection.WriteValueSafe(uint.MaxValue);
            rejection.WriteValueSafe((byte)reason);
            Assert.That(rejection.Length, Is.LessThanOrEqualTo(bufferSize));
        }

        string fingerprint = new string('F', 64);
        using var mapReady = new FastBufferWriter(bufferSize, Allocator.Temp);
        mapReady.WriteValueSafe(uint.MaxValue);
        mapReady.WriteValueSafe(fingerprint);
        Assert.That(mapReady.Length, Is.LessThanOrEqualTo(bufferSize));
    }

    [TestCase(0)]
    [TestCase(-37)]
    [TestCase(6100)]
    public void SuppliedSeed_IsStoredAtCreationAcrossUpdatesAndJoin_AndCopiesAreIsolated(int seed)
    {
        var session = Session();
        DuelSettings requested = Settings(seed);
        Assert.That(session.TryCreate(1, requested, out _), Is.True);
        requested.Map.Seed = seed + 1;
        Assert.That(session.Snapshot.Settings.ToSettings().Seed, Is.EqualTo(seed));

        DuelSettings returned = new DuelSettings { Map = session.Snapshot.Settings.ToSettings() };
        returned.Map.Seed = 900;
        returned.Map.Shape = MapShape.Donut;
        Assert.That(session.Snapshot.Settings.ToSettings().Seed, Is.EqualTo(seed));
        Assert.That(session.Snapshot.Settings.ToSettings().Shape, Is.EqualTo(MapShape.Random));

        NetworkLobbySnapshot snapshot = session.Snapshot;
        snapshot.Settings.Seed = 902;
        DuelSettings snapshotSettings = new DuelSettings { Map = snapshot.Settings.ToSettings() };
        snapshotSettings.Map.Seed = 901;
        Assert.That(session.Snapshot.Settings.ToSettings().Seed, Is.EqualTo(seed));
        Assert.That(session.Confirm(1), Is.True);
        foreach (int updatedSeed in new[] { seed + 2, seed })
        {
            DuelSettings changed = Settings(updatedSeed);
            changed.Map.Shape = MapShape.Donut;
            Assert.That(session.TryUpdateSettings(1, changed, out _), Is.True);
            changed.Map.Seed = 903;
            Assert.That(session.Snapshot.Settings.ToSettings().Seed, Is.EqualTo(updatedSeed));
        }
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.Confirm(2), Is.True);
        Assert.That(session.Snapshot.Settings.ToSettings().Seed, Is.EqualTo(seed));
    }

    [Test]
    public void LateJoinSnapshotContainsLatestFullSettings()
    {
        var session = Session();
        Assert.That(session.TryCreate(1, Settings(1), out _), Is.True);
        Assert.That(session.Confirm(1), Is.True);

        DuelSettings latest = new DuelSettings
        {
            Map = new MapGenerationSettings
            {
                Seed = -321,
                Size = new Vector2Int(41, 37),
                Shape = MapShape.Donut,
                ObstacleDensity = 0.24f,
                BushDensity = 0.17f,
                Symmetry = MapSymmetry.None,
                TeleporterPairs = DuelSettings.MaxTeleporterPairs,
                NoiseStrength = 0.9f,
                NoiseScale = 1.8f
            }
        };
        Assert.That(session.TryUpdateSettings(1, latest, out LobbyReason reason), Is.True, $"{reason}");
        Assert.That(session.TryJoin(2, session.RoomCode, out LobbyReason joinReason), Is.True, $"{joinReason}");
        Assert.That(session.Confirm(2), Is.True);

        NetworkLobbySnapshot lateJoinSnapshot = session.Snapshot;
        Assert.That(lateJoinSnapshot.OpponentPresent, Is.True);
        AssertSettingsEqual(latest, new DuelSettings { Map = lateJoinSnapshot.Settings.ToSettings() });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ServerSnapshotCloneGeneratesSameRandomMapAndSpawns(bool fixedSize)
    {
        var session = Session();
        DuelSettings requested = Settings(999,
            size: fixedSize ? new Vector2Int(35, 29) : null);
        Assert.That(session.TryCreate(1, requested, out _), Is.True);

        NetworkLobbySnapshot serverSnapshot = session.Snapshot;
        DuelSettings clonedSettings = new DuelSettings { Map = serverSnapshot.Settings.ToSettings() };
        MapLayout serverLayout = MapLayoutGenerator.Generate(serverSnapshot.Settings.ToSettings());
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
            DuelSettings source = Settings(seed);
            Assert.That(source.ToMapSettings().Seed, Is.EqualTo(seed));
            Assert.That(source.Clone().Map.Seed, Is.EqualTo(seed));
        }
    }

    [Test]
    public void ValidationRejectsInvalidEnumsNonFiniteNumbersAndOutOfRangeMapValues()
    {
        AssertInvalid((DuelSettings)null);
        AssertInvalid(new DuelSettings { Map = null });
        AssertInvalid(s => s.Map.Shape = (MapShape)999);
        AssertInvalid(s => s.Map.Symmetry = (MapSymmetry)999);
        AssertInvalid(s => s.Map.ObstacleDensity = float.NaN);
        AssertInvalid(s => s.Map.ObstacleDensity = float.PositiveInfinity);
        AssertInvalid(s => s.Map.ObstacleDensity = float.NegativeInfinity);
        AssertInvalid(s => s.Map.ObstacleDensity = -0.01f);
        AssertInvalid(s => s.Map.ObstacleDensity = DuelSettings.MaxObstacleDensity + 0.01f);
        AssertInvalid(s => s.Map.BushDensity = float.NaN);
        AssertInvalid(s => s.Map.BushDensity = float.PositiveInfinity);
        AssertInvalid(s => s.Map.BushDensity = float.NegativeInfinity);
        AssertInvalid(s => s.Map.BushDensity = -0.01f);
        AssertInvalid(s => s.Map.BushDensity = DuelSettings.MaxBushDensity + 0.01f);
        AssertInvalid(s => s.Map.NoiseStrength = float.NaN);
        AssertInvalid(s => s.Map.NoiseStrength = float.PositiveInfinity);
        AssertInvalid(s => s.Map.NoiseStrength = float.NegativeInfinity);
        AssertInvalid(s => s.Map.NoiseStrength = -0.01f);
        AssertInvalid(s => s.Map.NoiseStrength = 1.01f);
        AssertInvalid(s => s.Map.NoiseScale = float.PositiveInfinity);
        AssertInvalid(s => s.Map.NoiseScale = float.NegativeInfinity);
        AssertInvalid(s => s.Map.NoiseScale = float.NaN);
        AssertInvalid(s => s.Map.NoiseScale = 0.24f);
        AssertInvalid(s => s.Map.NoiseScale = 2.01f);
        AssertInvalid(s => s.Map.Size = new Vector2Int(DuelSettings.MinSize - 1, DuelSettings.MinSize));
        AssertInvalid(s => s.Map.Size = new Vector2Int(DuelSettings.MinSize, DuelSettings.MinSize - 1));
        AssertInvalid(s => s.Map.Size = new Vector2Int(DuelSettings.MaxSize + 1, DuelSettings.MaxSize));
        AssertInvalid(s => s.Map.Size = new Vector2Int(DuelSettings.MaxSize, DuelSettings.MaxSize + 1));
        AssertInvalid(s => s.Map.TeleporterPairs = -1);
        AssertInvalid(s => s.Map.TeleporterPairs = DuelSettings.MaxTeleporterPairs + 1);

        AssertValid(Settings());
        for (int pairs = 0; pairs <= DuelSettings.MaxTeleporterPairs; pairs++)
        {
            DuelSettings withPairs = Settings();
            withPairs.Map.TeleporterPairs = pairs;
            AssertValid(withPairs);
        }
        AssertValid(WithMapSizeAndRanges(
            new Vector2Int(DuelSettings.MinSize, DuelSettings.MaxSize), 0,
            0, 0.25f, 0));
        AssertValid(WithMapSizeAndRanges(
            new Vector2Int(DuelSettings.MaxSize, DuelSettings.MinSize),
            DuelSettings.MaxObstacleDensity, 1, 2, DuelSettings.MaxBushDensity));
    }

    static void AssertInvalid(System.Action<DuelSettings> change)
    {
        DuelSettings settings = Settings();
        change(settings);
        AssertInvalid(settings);
    }

    static void AssertInvalid(DuelSettings settings)
    {
        Assert.That(DuelSettings.ValidateSettings(settings, out LobbyReason reason), Is.False);
        Assert.That(reason, Is.EqualTo(LobbyReason.InvalidSettings));
    }

    static void AssertValid(DuelSettings settings)
    {
        Assert.That(DuelSettings.ValidateSettings(settings, out LobbyReason reason), Is.True, $"{reason}");
        Assert.That(reason, Is.EqualTo(LobbyReason.None));
    }

    static DuelSettings WithMapSizeAndRanges(Vector2Int? size, float density, float strength, float scale, float bushDensity = 0.05f)
    {
        DuelSettings settings = Settings();
        settings.Map.Size = size;
        settings.Map.ObstacleDensity = density;
        settings.Map.BushDensity = bushDensity;
        settings.Map.NoiseStrength = strength;
        settings.Map.NoiseScale = scale;
        return settings;
    }

    static void AssertSettingsEqual(DuelSettings expected, DuelSettings actual)
    {
        Assert.That(actual.Map.Seed, Is.EqualTo(expected.Map.Seed));
        Assert.That(actual.Map.Size, Is.EqualTo(expected.Map.Size));
        Assert.That(actual.Map.Shape, Is.EqualTo(expected.Map.Shape));
        Assert.That(actual.Map.ObstacleDensity, Is.EqualTo(expected.Map.ObstacleDensity));
        Assert.That(actual.Map.BushDensity, Is.EqualTo(expected.Map.BushDensity));
        Assert.That(actual.Map.Symmetry, Is.EqualTo(expected.Map.Symmetry));
        Assert.That(actual.Map.TeleporterPairs, Is.EqualTo(expected.Map.TeleporterPairs));
        Assert.That(actual.Map.NoiseStrength, Is.EqualTo(expected.Map.NoiseStrength));
        Assert.That(actual.Map.NoiseScale, Is.EqualTo(expected.Map.NoiseScale));
    }

}
#endif
