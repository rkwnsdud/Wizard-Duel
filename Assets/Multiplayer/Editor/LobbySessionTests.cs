#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class LobbySessionTests
{
    static DuelSettings Settings(int seed = 7301) => new DuelSettings
    {
        SeedFixed = true,
        Map = new MapGenerationSettings { Seed = seed }
    };

    static LobbySession CreateSession(int seed = 1001, int code = 123456) =>
        new LobbySession(() => seed, () => code);

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
        Assert.That(createReason, Is.Not.Empty);
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
}
#endif
