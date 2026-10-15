#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class LobbySessionTests
{
    static DuelSettings Settings(int seed = 7301) => new DuelSettings
    {
        Map = new MapGenerationSettings { Seed = seed }
    };

    static LobbySession CreateSession(int code = 123456) => new LobbySession(() => code);

    static void CreateAndConfirmHost(LobbySession session, ulong host = 1)
    {
        Assert.That(session.TryCreate(host, Settings(), out string reason), Is.True, reason);
        Assert.That(session.Confirm(host), Is.True);
    }

    [Test]
    public void Room_AllowsOnlyHostAndOneGuestReservation()
    {
        var session = CreateSession();
        CreateAndConfirmHost(session);

        Assert.That(session.TryJoin(2, session.RoomCode, out string joinReason), Is.True, joinReason);
        Assert.That(session.HasReservation(2), Is.True);
        Assert.That(session.TryJoin(3, session.RoomCode, out string thirdReason), Is.False);
        Assert.That(thirdReason, Is.Not.Empty);
        Assert.That(session.Confirm(2), Is.True);
        Assert.That(session.Ready, Is.True);
        Assert.That(session.Contains(1), Is.True);
        Assert.That(session.Contains(2), Is.True);
        Assert.That(session.TryJoin(3, session.RoomCode, out _), Is.False);

        Assert.That(session.TryCreate(4, Settings(), out string createReason), Is.False);
        Assert.That(createReason, Is.EqualTo("A room already exists."));
    }

    [Test]
    public void Join_RejectsMalformedAndUnknownCodes()
    {
        var session = CreateSession();
        Assert.That(session.TryJoin(2, "12x456", out string malformed), Is.False);
        Assert.That(malformed, Is.Not.Empty);
        Assert.That(session.TryJoin(2, "999999", out string unknown), Is.False);
        Assert.That(unknown, Is.Not.Empty);
        Assert.That(LobbySession.IsValidCode("123456"), Is.True);
        Assert.That(LobbySession.IsValidCode("12345"), Is.False);
        Assert.That(LobbySession.IsValidCode("12345x"), Is.False);
    }

    [Test]
    public void CancellingHostReservationReleasesRoom_ButGuestCancelKeepsHost()
    {
        var session = CreateSession();
        Assert.That(session.TryCreate(1, Settings(), out _), Is.True);
        Assert.That(session.CancelReservation(1), Is.True);
        Assert.That(session.HasRoom, Is.False);

        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.CancelReservation(2), Is.True);
        Assert.That(session.HasRoom, Is.True);
        Assert.That(session.Contains(1), Is.True);
        Assert.That(session.OpponentPresent, Is.False);
        Assert.That(session.Ready, Is.False);
    }

    [Test]
    public void CancelledHostReservationCannotBeConfirmedOrJoinedByItsOldCode()
    {
        var session = CreateSession();
        Assert.That(session.TryCreate(1, Settings(), out _), Is.True);
        string abandonedCode = session.RoomCode;

        Assert.That(session.CancelReservation(1), Is.True);
        Assert.That(session.Confirm(1), Is.False);
        Assert.That(session.TryJoin(2, abandonedCode, out string reason), Is.False);
        Assert.That(reason, Is.Not.Empty);
        Assert.That(session.HasRoom, Is.False);
        Assert.That(session.HasReservation(1), Is.False);
        Assert.That(session.HasReservation(2), Is.False);
    }

    [Test]
    public void OutsiderOperationsDoNotCloseRoom_ConfirmedDepartureDoes()
    {
        var session = CreateSession();
        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);

        Assert.That(session.CancelReservation(99), Is.False);
        Assert.That(session.Disconnect(99), Is.False);
        Assert.That(session.HasRoom, Is.True);
        Assert.That(session.Contains(1), Is.True);

        Assert.That(session.Confirm(2), Is.True);
        Assert.That(session.Disconnect(2), Is.True);
        Assert.That(session.HasRoom, Is.False);

        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.Confirm(2), Is.True);
        Assert.That(session.Disconnect(1), Is.True);
        Assert.That(session.HasRoom, Is.False);
    }

    [Test]
    public void DepartureInvalidatesOldRoomAndStaleDisconnectCannotCloseNewRoom()
    {
        var session = CreateSession();
        CreateAndConfirmHost(session, 1);
        string previousCode = session.RoomCode;
        Assert.That(session.TryJoin(2, previousCode, out _), Is.True);
        Assert.That(session.Confirm(2), Is.True);

        Assert.That(session.Disconnect(2), Is.True);
        Assert.That(session.HasRoom, Is.False);
        Assert.That(session.Contains(1), Is.False);
        Assert.That(session.Contains(2), Is.False);
        Assert.That(session.HasReservation(1), Is.False);
        Assert.That(session.HasReservation(2), Is.False);
        Assert.That(session.TryJoin(3, previousCode, out _), Is.False);

        Assert.That(session.TryCreate(3, Settings(), out _), Is.True);
        Assert.That(session.RoomCode, Is.Not.EqualTo(previousCode));
        Assert.That(session.Confirm(3), Is.True);
        Assert.That(session.TryJoin(5, previousCode, out _), Is.False);
        Assert.That(session.TryJoin(4, session.RoomCode, out _), Is.True);
        Assert.That(session.Confirm(4), Is.True);

        // This session API identifies members by client ID; later room members have fresh IDs.
        Assert.That(session.Disconnect(1), Is.False);
        Assert.That(session.Disconnect(2), Is.False);
        Assert.That(session.HasRoom, Is.True);
        Assert.That(session.Ready, Is.True);
        Assert.That(session.Contains(3), Is.True);
        Assert.That(session.Contains(4), Is.True);
    }

    [Test]
    public void FullRoomRejectsAnotherReservation()
    {
        var session = CreateSession();
        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.TryJoin(3, session.RoomCode, out string reason), Is.False);
        Assert.That(reason, Is.Not.Empty);
        Assert.That(session.HasReservation(2), Is.True);
        Assert.That(session.HasReservation(3), Is.False);
    }

    [Test]
    public void GuestCannotUpdateSettings_AndInvalidHostUpdatesLeaveSnapshotUnchanged()
    {
        var unconfirmedSession = CreateSession();
        Assert.That(unconfirmedSession.TryCreate(9, Settings(), out _), Is.True);
        DuelSettings beforeUnconfirmedUpdate = unconfirmedSession.Snapshot.Settings.ToSettings();
        foreach (ulong sender in new ulong[] { 9, 10 })
        {
            Assert.That(unconfirmedSession.TryUpdateSettings(sender, null, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("Only the connected host can change room settings."));
            AssertSettingsEqual(beforeUnconfirmedUpdate, unconfirmedSession.Snapshot.Settings.ToSettings());
        }

        var session = CreateSession();
        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.Confirm(2), Is.True);
        DuelSettings beforeGuestUpdate = session.Snapshot.Settings.ToSettings();

        Assert.That(session.TryUpdateSettings(2, new DuelSettings { Map = null }, out string guestReason), Is.False);
        Assert.That(guestReason, Is.EqualTo("Only the connected host can change room settings."));
        AssertSettingsEqual(beforeGuestUpdate, session.Snapshot.Settings.ToSettings());

        DuelSettings beforeInvalidUpdates = session.Snapshot.Settings.ToSettings();
        var invalidSettings = new[]
        {
            new DuelSettings { Map = new MapGenerationSettings { ObstacleDensity = 0.36f } },
            new DuelSettings { Map = new MapGenerationSettings { NoiseStrength = float.NaN } }
        };
        foreach (DuelSettings invalid in invalidSettings)
        {
            Assert.That(session.TryUpdateSettings(1, invalid, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("Room settings are invalid."));
            AssertSettingsEqual(beforeInvalidUpdates, session.Snapshot.Settings.ToSettings());
        }
    }

    static void AssertSettingsEqual(DuelSettings expected, DuelSettings actual)
    {
        Assert.That(actual.Map.Seed, Is.EqualTo(expected.Map.Seed));
        Assert.That(actual.Map.Size, Is.EqualTo(expected.Map.Size));
        Assert.That(actual.Map.Shape, Is.EqualTo(expected.Map.Shape));
        Assert.That(actual.Map.ObstacleDensity, Is.EqualTo(expected.Map.ObstacleDensity));
        Assert.That(actual.Map.Symmetry, Is.EqualTo(expected.Map.Symmetry));
        Assert.That(actual.Map.NoiseStrength, Is.EqualTo(expected.Map.NoiseStrength));
        Assert.That(actual.Map.NoiseScale, Is.EqualTo(expected.Map.NoiseScale));
    }
}
#endif
