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
        DuelSettings beforeUnconfirmedUpdate = new DuelSettings { Map = unconfirmedSession.Snapshot.Settings.ToSettings() };
        foreach (ulong sender in new ulong[] { 9, 10 })
        {
            Assert.That(unconfirmedSession.TryUpdateSettings(sender, null, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("Only the connected host can change room settings."));
            AssertSettingsEqual(beforeUnconfirmedUpdate, new DuelSettings { Map = unconfirmedSession.Snapshot.Settings.ToSettings() });
        }

        var session = CreateSession();
        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.Confirm(2), Is.True);
        DuelSettings beforeGuestUpdate = new DuelSettings { Map = session.Snapshot.Settings.ToSettings() };

        Assert.That(session.TryUpdateSettings(2, new DuelSettings { Map = null }, out string guestReason), Is.False);
        Assert.That(guestReason, Is.EqualTo("Only the connected host can change room settings."));
        AssertSettingsEqual(beforeGuestUpdate, new DuelSettings { Map = session.Snapshot.Settings.ToSettings() });

        DuelSettings beforeInvalidUpdates = new DuelSettings { Map = session.Snapshot.Settings.ToSettings() };
        var invalidSettings = new[]
        {
            new DuelSettings { Map = new MapGenerationSettings { ObstacleDensity = 0.26f } },
            new DuelSettings { Map = new MapGenerationSettings { BushDensity = DuelSettings.MaxBushDensity + 0.01f } },
            new DuelSettings { Map = new MapGenerationSettings { NoiseStrength = float.NaN } }
        };
        foreach (DuelSettings invalid in invalidSettings)
        {
            Assert.That(session.TryUpdateSettings(1, invalid, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("Room settings are invalid."));
            AssertSettingsEqual(beforeInvalidUpdates, new DuelSettings { Map = session.Snapshot.Settings.ToSettings() });
        }
    }

    [Test]
    public void GuestAndOutsiderCannotStart_RoomStaysInLobby()
    {
        var session = CreateFullRoom();
        foreach (ulong sender in new ulong[] { 2, 99 })
        {
            Assert.That(session.TryStart(sender, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("Only the connected host can start the duel."));
            Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Lobby));
            Assert.That(session.Snapshot.Phase, Is.EqualTo(LobbyPhase.Lobby));
        }
    }

    [Test]
    public void HostCannotStartUntilBothPlayersAreConfirmed()
    {
        var session = CreateSession();
        Assert.That(session.TryCreate(1, Settings(), out _), Is.True);
        Assert.That(session.TryStart(1, out string unconfirmed), Is.False);
        Assert.That(unconfirmed, Is.EqualTo("Only the connected host can start the duel."));

        Assert.That(session.Confirm(1), Is.True);
        Assert.That(session.TryStart(1, out string alone), Is.False);
        Assert.That(alone, Is.EqualTo("Two players must join before the duel starts."));

        // 참가 예약만 하고 접속을 끝내지 않은 상대도 아직 방에 없는 것으로 본다.
        Assert.That(session.TryJoin(2, session.RoomCode, out _), Is.True);
        Assert.That(session.TryStart(1, out string reserved), Is.False);
        Assert.That(reserved, Is.EqualTo("Two players must join before the duel starts."));
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Lobby));

        Assert.That(session.Confirm(2), Is.True);
        Assert.That(session.TryStart(1, out string reason), Is.True, reason);
        Assert.That(reason, Is.Empty);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Preparing));
        Assert.That(session.Snapshot.Phase, Is.EqualTo(LobbyPhase.Preparing));
    }

    [Test]
    public void RepeatedStartIsRejectedWhilePreparingAndInBattle()
    {
        var session = CreateFullRoom();
        Assert.That(session.BeginBattle(), Is.False);
        Assert.That(session.TryStart(1, out _), Is.True);
        Assert.That(session.TryStart(1, out string preparing), Is.False);
        Assert.That(preparing, Is.EqualTo("The duel has already started."));
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Preparing));

        Assert.That(session.BeginBattle(), Is.True);
        Assert.That(session.TryStart(1, out string battle), Is.False);
        Assert.That(battle, Is.EqualTo("The duel has already started."));
        Assert.That(session.BeginBattle(), Is.False);
        Assert.That(session.CancelStart(), Is.False);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Battle));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SettingsCannotChangeOnceTheDuelStarts(bool inBattle)
    {
        var session = CreateFullRoom();
        Assert.That(session.TryStart(1, out _), Is.True);
        if (inBattle) Assert.That(session.BeginBattle(), Is.True);
        DuelSettings before = new DuelSettings { Map = session.Snapshot.Settings.ToSettings() };

        Assert.That(session.TryUpdateSettings(1, Settings(4242), out string hostReason), Is.False);
        Assert.That(hostReason, Is.EqualTo("Room settings cannot change once the duel starts."));
        Assert.That(session.TryUpdateSettings(2, Settings(4242), out string guestReason), Is.False);
        Assert.That(guestReason, Is.EqualTo("Only the connected host can change room settings."));
        AssertSettingsEqual(before, new DuelSettings { Map = session.Snapshot.Settings.ToSettings() });
    }

    [Test]
    public void FailedPreparationReturnsToLobby_AndClosedRoomStartsOverInLobby()
    {
        var session = CreateFullRoom();
        Assert.That(session.TryStart(1, out _), Is.True);
        Assert.That(session.CancelStart(), Is.True);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Lobby));
        Assert.That(session.CancelStart(), Is.False);
        Assert.That(session.TryUpdateSettings(1, Settings(5), out string reason), Is.True, reason);

        Assert.That(session.TryStart(1, out _), Is.True);
        Assert.That(session.BeginBattle(), Is.True);
        Assert.That(session.Disconnect(2), Is.True);
        Assert.That(session.HasRoom, Is.False);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Lobby));

        CreateAndConfirmHost(session, 3);
        Assert.That(session.Snapshot.Phase, Is.EqualTo(LobbyPhase.Lobby));
        Assert.That(session.TryStart(3, out string alone), Is.False);
        Assert.That(alone, Is.EqualTo("Two players must join before the duel starts."));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public void DefeatInBattleShowsResultOnce_WithTheOtherSlotAsWinner(int defeated, int winner)
    {
        var session = CreateFullRoom();
        Assert.That(session.TryFinishBattle(defeated), Is.False);
        Assert.That(session.TryStart(1, out _), Is.True);
        Assert.That(session.TryFinishBattle(defeated), Is.False);
        Assert.That(session.BeginBattle(), Is.True);
        Assert.That(session.TryFinishBattle(2), Is.False);
        Assert.That(session.TryFinishBattle(-1), Is.False);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Battle));

        Assert.That(session.TryFinishBattle(defeated), Is.True);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Result));
        Assert.That(session.WinnerSlot, Is.EqualTo((byte)winner));
        Assert.That(session.Snapshot.WinnerSlot, Is.EqualTo((byte)winner));
        Assert.That(session.Snapshot.IsValidFor(1), Is.True);
        Assert.That(session.Snapshot.IsValidFor(2), Is.True);

        // 뒤이어 쓰러진 쪽은 결과를 바꾸지 않는다.
        Assert.That(session.TryFinishBattle(1 - defeated), Is.False);
        Assert.That(session.WinnerSlot, Is.EqualTo((byte)winner));
    }

    [Test]
    public void ResultRejectsSettingsAndStart()
    {
        var session = CreateFullRoom();
        PlayUntilResult(session, 0);
        Assert.That(session.TryUpdateSettings(1, Settings(5), out string settingsReason), Is.False);
        Assert.That(settingsReason, Is.EqualTo("Room settings cannot change once the duel starts."));
        Assert.That(session.TryStart(1, out string startReason), Is.False);
        Assert.That(startReason, Is.EqualTo("The duel has already started."));
        Assert.That(session.BeginBattle(), Is.False);
        Assert.That(session.CancelStart(), Is.False);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Result));
    }

    [Test]
    public void ReturnToLobbyKeepsRoomRolesAndSettings_AndAllowsRematch()
    {
        var session = CreateFullRoom();
        Assert.That(session.TryUpdateSettings(1, Settings(4242), out string reason), Is.True, reason);
        NetworkLobbySnapshot before = session.Snapshot;
        Assert.That(session.ReturnToLobby(), Is.False);

        PlayUntilResult(session, 1);
        Assert.That(session.ReturnToLobby(), Is.True);
        Assert.That(session.ReturnToLobby(), Is.False);
        NetworkLobbySnapshot after = session.Snapshot;
        Assert.That(after.Phase, Is.EqualTo(LobbyPhase.Lobby));
        Assert.That(after.Round, Is.EqualTo(before.Round + 1));
        Assert.That(after.WinnerSlot, Is.EqualTo((byte)0));
        Assert.That(after.Code, Is.EqualTo(before.Code));
        Assert.That(after.HostClientId, Is.EqualTo(1ul));
        Assert.That(after.GuestClientId, Is.EqualTo(2ul));
        Assert.That(after.OpponentPresent, Is.True);
        Assert.That(after.IsValidFor(1), Is.True);
        AssertSettingsEqual(Settings(4242), new DuelSettings { Map = after.Settings.ToSettings() });

        // 재대결도 같은 방에서 시작하고 결과를 낸다.
        PlayUntilResult(session, 0);
        Assert.That(session.WinnerSlot, Is.EqualTo((byte)1));
        Assert.That(session.ReturnToLobby(), Is.True);
        Assert.That(session.Round, Is.EqualTo(2u));
        Assert.That(session.Snapshot.Settings.Seed, Is.EqualTo(4242));
    }

    [Test]
    public void LeavingDuringResultClosesRoom_AndNextRoomStartsAtFirstRound()
    {
        var session = CreateFullRoom();
        PlayUntilResult(session, 0);
        Assert.That(session.ReturnToLobby(), Is.True);
        PlayUntilResult(session, 1);
        Assert.That(session.Disconnect(2), Is.True);
        Assert.That(session.HasRoom, Is.False);
        Assert.That(session.Phase, Is.EqualTo(LobbyPhase.Lobby));
        Assert.That(session.Round, Is.EqualTo(0u));
        Assert.That(session.WinnerSlot, Is.EqualTo((byte)0));
        Assert.That(session.ReturnToLobby(), Is.False);

        CreateAndConfirmHost(session, 3);
        Assert.That(session.Snapshot.Round, Is.EqualTo(0u));
        Assert.That(session.Snapshot.Phase, Is.EqualTo(LobbyPhase.Lobby));
    }

    static void PlayUntilResult(LobbySession session, int defeatedSlot)
    {
        Assert.That(session.TryStart(session.HostClientId, out string reason), Is.True, reason);
        Assert.That(session.BeginBattle(), Is.True);
        Assert.That(session.TryFinishBattle(defeatedSlot), Is.True);
    }

    static LobbySession CreateFullRoom()
    {
        var session = CreateSession();
        CreateAndConfirmHost(session);
        Assert.That(session.TryJoin(2, session.RoomCode, out string reason), Is.True, reason);
        Assert.That(session.Confirm(2), Is.True);
        return session;
    }

    static void AssertSettingsEqual(DuelSettings expected, DuelSettings actual)
    {
        Assert.That(actual.Map.Seed, Is.EqualTo(expected.Map.Seed));
        Assert.That(actual.Map.Size, Is.EqualTo(expected.Map.Size));
        Assert.That(actual.Map.Shape, Is.EqualTo(expected.Map.Shape));
        Assert.That(actual.Map.ObstacleDensity, Is.EqualTo(expected.Map.ObstacleDensity));
        Assert.That(actual.Map.BushDensity, Is.EqualTo(expected.Map.BushDensity));
        Assert.That(actual.Map.Symmetry, Is.EqualTo(expected.Map.Symmetry));
        Assert.That(actual.Map.NoiseStrength, Is.EqualTo(expected.Map.NoiseStrength));
        Assert.That(actual.Map.NoiseScale, Is.EqualTo(expected.Map.NoiseScale));
    }
}
#endif
