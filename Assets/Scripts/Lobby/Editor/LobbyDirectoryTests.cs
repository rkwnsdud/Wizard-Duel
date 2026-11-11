#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class LobbyDirectoryTests
{
    static DuelSettings Settings(int seed = 7301) => new DuelSettings
    {
        Map = new MapGenerationSettings { Seed = seed }
    };

    // 코드 생성기가 늘 같은 값을 내도 방마다 다른 코드를 받아야 한다.
    static LobbyDirectory CreateDirectory(int maxRooms = 8, int code = 123456) => new LobbyDirectory(maxRooms, () => code);

    static LobbyRoom CreateFullRoom(LobbyDirectory directory, ulong host, ulong guest)
    {
        Assert.That(directory.TryCreate(host, Settings(), out LobbyRoom room, out string createReason), Is.True, createReason);
        Assert.That(directory.Confirm(host, out LobbyRoom confirmedHostRoom), Is.True);
        Assert.That(confirmedHostRoom, Is.SameAs(room));
        Assert.That(directory.TryJoin(guest, room.Code, out LobbyRoom joined, out string joinReason), Is.True, joinReason);
        Assert.That(joined, Is.SameAs(room));
        Assert.That(directory.Confirm(guest, out _), Is.True);
        return room;
    }

    [Test]
    public void RoomsGetDistinctCodes_EvenWhenTheGeneratorRepeats()
    {
        var directory = CreateDirectory();
        Assert.That(directory.TryCreate(1, Settings(), out LobbyRoom first, out _), Is.True);
        Assert.That(directory.TryCreate(2, Settings(), out LobbyRoom second, out _), Is.True);
        Assert.That(directory.TryCreate(3, Settings(), out LobbyRoom third, out _), Is.True);

        Assert.That(first.Code, Is.EqualTo("123456"));
        Assert.That(second.Code, Is.EqualTo("123457"));
        Assert.That(third.Code, Is.EqualTo("123458"));
        Assert.That(first.Session.RoomCode, Is.EqualTo(first.Code));
        Assert.That(directory.Count, Is.EqualTo(3));
        Assert.That(directory.Rooms, Is.EquivalentTo(new[] { first, second, third }));
    }

    [Test]
    public void JoinFindsTheRoomByCode_AndEachRoomKeepsItsOwnPhaseAndSettings()
    {
        var directory = CreateDirectory();
        LobbyRoom first = CreateFullRoom(directory, 1, 2);
        LobbyRoom second = CreateFullRoom(directory, 3, 4);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(directory.RoomOf(1), Is.SameAs(first));
        Assert.That(directory.RoomOf(2), Is.SameAs(first));
        Assert.That(directory.RoomOf(3), Is.SameAs(second));
        Assert.That(directory.RoomOf(4), Is.SameAs(second));
        Assert.That(directory.RoomOf(99), Is.Null);

        // 다른 방의 방장은 이 방의 설정과 시작을 건드리지 못한다.
        Assert.That(first.Session.TryUpdateSettings(3, Settings(4242), out _), Is.False);
        Assert.That(first.Session.TryStart(3, out _), Is.False);
        Assert.That(first.Session.TryUpdateSettings(1, Settings(4242), out string reason), Is.True, reason);
        Assert.That(first.Session.TryStart(1, out reason), Is.True, reason);

        Assert.That(first.Session.Phase, Is.EqualTo(LobbyPhase.Preparing));
        Assert.That(first.Session.Snapshot.Settings.Seed, Is.EqualTo(4242));
        Assert.That(second.Session.Phase, Is.EqualTo(LobbyPhase.Lobby));
        Assert.That(second.Session.Snapshot.Settings.Seed, Is.EqualTo(7301));
        Assert.That(second.Session.Snapshot.IsValidFor(3), Is.True);
        Assert.That(second.Session.Snapshot.IsValidFor(1), Is.False);
    }

    [Test]
    public void Join_RejectsMalformedUnknownAndFullRooms()
    {
        var directory = CreateDirectory();
        LobbyRoom room = CreateFullRoom(directory, 1, 2);

        Assert.That(directory.TryJoin(5, "12x456", out LobbyRoom malformedRoom, out string malformed), Is.False);
        Assert.That(malformed, Is.EqualTo("Enter a six-digit room code."));
        Assert.That(malformedRoom, Is.Null);
        Assert.That(directory.TryJoin(5, "999999", out _, out string unknown), Is.False);
        Assert.That(unknown, Is.EqualTo("The room code is invalid."));
        Assert.That(directory.TryJoin(5, room.Code, out _, out string full), Is.False);
        Assert.That(full, Is.EqualTo("This room already has two players."));
        Assert.That(directory.RoomOf(5), Is.Null);

        // 거절된 클라이언트는 방에 묶이지 않아서 다른 방을 만들 수 있다.
        Assert.That(directory.TryCreate(5, Settings(), out _, out string reason), Is.True, reason);
    }

    [Test]
    public void ClientCannotHoldTwoRooms()
    {
        var directory = CreateDirectory();
        LobbyRoom first = CreateFullRoom(directory, 1, 2);
        Assert.That(directory.TryCreate(3, Settings(), out LobbyRoom second, out _), Is.True);
        Assert.That(directory.Confirm(3, out _), Is.True);

        Assert.That(directory.TryCreate(1, Settings(), out _, out string createReason), Is.False);
        Assert.That(createReason, Is.Not.Empty);
        Assert.That(directory.TryJoin(2, second.Code, out _, out string joinReason), Is.False);
        Assert.That(joinReason, Is.Not.Empty);
        Assert.That(directory.Count, Is.EqualTo(2));
        Assert.That(directory.RoomOf(2), Is.SameAs(first));
        Assert.That(second.Session.OpponentPresent, Is.False);
    }

    [Test]
    public void RemovingOneRoomLeavesTheOthersRunning()
    {
        var directory = CreateDirectory();
        LobbyRoom first = CreateFullRoom(directory, 1, 2);
        LobbyRoom second = CreateFullRoom(directory, 3, 4);
        Assert.That(second.Session.TryStart(3, out _), Is.True);
        Assert.That(second.Session.BeginBattle(), Is.True);

        Assert.That(directory.Remove(first), Is.True);
        Assert.That(directory.Remove(first), Is.False);
        Assert.That(directory.RoomOf(1), Is.Null);
        Assert.That(directory.RoomOf(2), Is.Null);
        Assert.That(directory.TryJoin(5, first.Code, out _, out string reason), Is.False);
        Assert.That(reason, Is.EqualTo("The room code is invalid."));

        Assert.That(directory.Count, Is.EqualTo(1));
        Assert.That(directory.RoomOf(3), Is.SameAs(second));
        Assert.That(directory.RoomOf(4), Is.SameAs(second));
        Assert.That(second.Session.Phase, Is.EqualTo(LobbyPhase.Battle));
        Assert.That(second.Session.Ready, Is.True);

        // 나간 사람은 새 방을 만들 수 있다.
        Assert.That(directory.TryCreate(1, Settings(), out _, out reason), Is.True, reason);
    }

    [Test]
    public void ClosedCodeIsNotReusedByTheNextRoom()
    {
        var directory = CreateDirectory();
        LobbyRoom first = CreateFullRoom(directory, 1, 2);
        Assert.That(directory.Remove(first), Is.True);

        Assert.That(directory.TryCreate(3, Settings(), out LobbyRoom next, out _), Is.True);
        Assert.That(next.Code, Is.Not.EqualTo(first.Code));
        // 닫힌 방의 코드로 늦게 온 참가 요청은 새 방에 들어가지 못한다.
        Assert.That(directory.Confirm(3, out _), Is.True);
        Assert.That(directory.TryJoin(4, first.Code, out _, out _), Is.False);
        Assert.That(next.Session.OpponentPresent, Is.False);
    }

    [Test]
    public void FullServerRejectsNewRooms_UntilOneCloses()
    {
        var directory = CreateDirectory(maxRooms: 2);
        LobbyRoom first = CreateFullRoom(directory, 1, 2);
        CreateFullRoom(directory, 3, 4);

        Assert.That(directory.TryCreate(5, Settings(), out LobbyRoom rejected, out string reason), Is.False);
        Assert.That(reason, Is.EqualTo(LobbyDirectory.ServerFull));
        Assert.That(rejected, Is.Null);
        Assert.That(directory.Count, Is.EqualTo(2));
        // 가득 찬 서버에서도 열린 방에는 코드로 들어갈 수 있어야 하므로 참가는 방 수를 세지 않는다.
        Assert.That(directory.TryJoin(5, "999999", out _, out reason), Is.False);
        Assert.That(reason, Is.EqualTo("The room code is invalid."));

        Assert.That(directory.Remove(first), Is.True);
        Assert.That(directory.TryCreate(5, Settings(), out _, out reason), Is.True, reason);
        Assert.That(directory.Count, Is.EqualTo(2));
    }

    [Test]
    public void InvalidSettingsDoNotOpenARoom()
    {
        var directory = CreateDirectory();
        var invalid = new DuelSettings { Map = new MapGenerationSettings { ObstacleDensity = 0.26f } };

        Assert.That(directory.TryCreate(1, invalid, out LobbyRoom room, out string reason), Is.False);
        Assert.That(reason, Is.EqualTo("Room settings are invalid."));
        Assert.That(room, Is.Null);
        Assert.That(directory.Count, Is.Zero);
        Assert.That(directory.TryCreate(1, Settings(), out _, out reason), Is.True, reason);
    }

    [Test]
    public void ReservedClientsAreNotMembersUntilConfirmed()
    {
        var directory = CreateDirectory();
        Assert.That(directory.TryCreate(1, Settings(), out LobbyRoom room, out _), Is.True);
        Assert.That(directory.RoomOf(1), Is.Null);
        // 방장이 접속을 끝내기 전에는 코드를 알아도 들어갈 수 없다.
        Assert.That(directory.TryJoin(2, room.Code, out _, out _), Is.False);

        Assert.That(directory.Confirm(1, out LobbyRoom confirmed), Is.True);
        Assert.That(confirmed, Is.SameAs(room));
        Assert.That(directory.Confirm(1, out _), Is.False);
        Assert.That(directory.Confirm(99, out _), Is.False);
        Assert.That(directory.RoomOf(1), Is.SameAs(room));

        Assert.That(directory.TryJoin(2, room.Code, out _, out string reason), Is.True, reason);
        Assert.That(directory.RoomOf(2), Is.Null);
        Assert.That(room.Session.OpponentPresent, Is.False);
    }

    [Test]
    public void CancellingAReservationAffectsOnlyThatRoom()
    {
        var directory = CreateDirectory();
        LobbyRoom running = CreateFullRoom(directory, 1, 2);

        // 방장 예약을 거두면 그 방만 사라진다.
        Assert.That(directory.TryCreate(3, Settings(), out LobbyRoom abandoned, out _), Is.True);
        Assert.That(directory.CancelReservation(3), Is.True);
        Assert.That(directory.CancelReservation(3), Is.False);
        Assert.That(directory.Count, Is.EqualTo(1));
        Assert.That(directory.Confirm(3, out _), Is.False);
        Assert.That(directory.TryJoin(4, abandoned.Code, out _, out _), Is.False);

        // 참가자 예약을 거두면 방과 방장은 남고 자리가 다시 빈다.
        Assert.That(directory.TryCreate(5, Settings(), out LobbyRoom waiting, out _), Is.True);
        Assert.That(directory.Confirm(5, out _), Is.True);
        Assert.That(directory.TryJoin(6, waiting.Code, out _, out _), Is.True);
        Assert.That(directory.CancelReservation(6), Is.True);
        Assert.That(directory.RoomOf(5), Is.SameAs(waiting));
        Assert.That(directory.TryJoin(7, waiting.Code, out _, out string reason), Is.True, reason);

        // 접속을 끝낸 구성원과 모르는 클라이언트는 예약 취소로 방을 닫지 못한다.
        Assert.That(directory.CancelReservation(1), Is.False);
        Assert.That(directory.CancelReservation(99), Is.False);
        Assert.That(directory.RoomOf(1), Is.SameAs(running));
        Assert.That(directory.RoomOf(2), Is.SameAs(running));
        Assert.That(directory.Count, Is.EqualTo(2));
    }

    [Test]
    public void ReservationInAClosedRoomCannotBeConfirmed()
    {
        var directory = CreateDirectory();
        Assert.That(directory.TryCreate(1, Settings(), out LobbyRoom room, out _), Is.True);
        Assert.That(directory.Confirm(1, out _), Is.True);
        Assert.That(directory.TryJoin(2, room.Code, out _, out _), Is.True);

        // 참가자가 접속을 끝내기 전에 방장이 나가 방이 닫혔다.
        Assert.That(directory.Remove(room), Is.True);
        Assert.That(directory.Confirm(2, out _), Is.False);
        Assert.That(directory.RoomOf(2), Is.Null);
        Assert.That(directory.CancelReservation(2), Is.False);
        Assert.That(directory.TryCreate(2, Settings(), out _, out string reason), Is.True, reason);
    }

    [Test]
    public void ClearClosesEveryRoom()
    {
        var directory = CreateDirectory();
        LobbyRoom first = CreateFullRoom(directory, 1, 2);
        CreateFullRoom(directory, 3, 4);

        directory.Clear();
        Assert.That(directory.Count, Is.Zero);
        Assert.That(directory.Rooms, Is.Empty);
        Assert.That(directory.RoomOf(1), Is.Null);
        Assert.That(directory.RoomOf(4), Is.Null);
        Assert.That(directory.TryJoin(5, first.Code, out _, out _), Is.False);
        Assert.That(directory.TryCreate(1, Settings(), out _, out string reason), Is.True, reason);
    }
}
#endif
