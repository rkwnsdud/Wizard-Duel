#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

public class LobbyReasonTests
{
    [Test]
    public void WireReasonCodec_RoundTripsEveryRejectionCode_AndRejectsEmptyOrUnknown()
    {
        Assert.That(LobbyReasonCodec.ToWireReason(LobbyReason.None), Is.Empty);
        Assert.That(LobbyReasonCodec.IsValidRejectionCode(0), Is.False);
        Assert.That(LobbyReasonCodec.IsValidRejectionCode(250), Is.False);

        foreach (LobbyReason reason in Enum.GetValues(typeof(LobbyReason)))
        {
            if (reason == LobbyReason.None) continue;
            Assert.That(LobbyReasonCodec.IsValidRejectionCode((byte)reason), Is.True);
            string wire = LobbyReasonCodec.ToWireReason(reason);
            Assert.That(wire, Is.Not.Empty);
            Assert.That(LobbyReasonCodec.FromWireReason(wire), Is.EqualTo(reason));
        }

        Assert.That(LobbyReasonCodec.FromWireReason(null), Is.EqualTo(LobbyReason.Disconnected));
        Assert.That(LobbyReasonCodec.FromWireReason(string.Empty), Is.EqualTo(LobbyReason.Disconnected));
        Assert.That(LobbyReasonCodec.FromWireReason("0"), Is.EqualTo(LobbyReason.Disconnected));
        Assert.That(LobbyReasonCodec.FromWireReason("250"), Is.EqualTo(LobbyReason.Disconnected));
        Assert.That(LobbyReasonCodec.FromWireReason("not-a-code", LobbyReason.InvalidRequest), Is.EqualTo(LobbyReason.InvalidRequest));
    }

    [Test]
    public void ReasonMessages_PreserveExpectedKoreanCopy()
    {
        Assert.That(LobbyReasonMessages.SettingsRejected(LobbyReason.NotHost), Is.EqualTo("방장만 대기실 설정을 바꿀 수 있어요."));
        Assert.That(LobbyReasonMessages.SettingsRejected(LobbyReason.InvalidSettings), Is.EqualTo("맵 설정이 올바르지 않아 변경하지 않았어요."));
        Assert.That(LobbyReasonMessages.SettingsRejected(LobbyReason.BattleAlreadyStarted), Is.EqualTo("전투 준비가 시작되어 맵 설정을 바꿀 수 없어요."));
        Assert.That(LobbyReasonMessages.SettingsRejected(LobbyReason.SettingsOutOfDate), Is.EqualTo("맵 설정을 적용할 수 없어요. 다시 시도해 주세요."));

        Assert.That(LobbyReasonMessages.StartRejected(LobbyReason.NotHost), Is.EqualTo("방장만 게임을 시작할 수 있어요."));
        Assert.That(LobbyReasonMessages.StartRejected(LobbyReason.OpponentMissing), Is.EqualTo("상대가 들어와야 시작할 수 있어요"));
        Assert.That(LobbyReasonMessages.StartRejected(LobbyReason.AlreadyStarted), Is.EqualTo("이미 전투를 준비하고 있어요."));
        Assert.That(LobbyReasonMessages.StartRejected(LobbyReason.MapGenerationFailed), Is.EqualTo("이 설정으로는 전투 맵을 만들 수 없어요."));
        Assert.That(LobbyReasonMessages.StartRejected(LobbyReason.InvalidRequest), Is.EqualTo("게임을 시작할 수 없어요. 다시 시도해 주세요."));

        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.RoomNotFound), Is.EqualTo("유효하지 않은 맵 코드예요. 코드를 다시 확인해 주세요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.RoomFull), Is.EqualTo("이미 두 명이 참가한 대기실이에요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.ServerFull), Is.EqualTo("서버에 방이 가득 찼어요. 잠시 후 다시 시도해 주세요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.InvalidRoomCode), Is.EqualTo("숫자 6자리 맵 코드를 입력해 주세요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.RoomClosed), Is.EqualTo("상대가 나가서 대기실이 닫혔어요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.MapGenerationFailed), Is.EqualTo("전투 맵을 만들지 못해 대기실에서 나왔어요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.MapMismatch), Is.EqualTo("맵이 서버와 달라서 연결이 끊겼어요."));
        Assert.That(LobbyReasonMessages.ConnectionNotice(LobbyReason.Disconnected), Is.EqualTo("서버 연결이 끊겼거나 접속할 수 없어요. 잠시 후 다시 시도해 주세요."));

        Assert.That(LobbyReasonMessages.ClosedNotice(LobbyReason.None, false), Is.Empty);
        Assert.That(LobbyReasonMessages.ClosedNotice(LobbyReason.None, true), Is.Empty);
        Assert.That(LobbyReasonMessages.ClosedNotice(LobbyReason.RoomClosed, false), Is.EqualTo("상대가 나가서 대기실이 닫혔어요."));
        Assert.That(LobbyReasonMessages.ClosedNotice(LobbyReason.RoomClosed, true), Is.EqualTo("상대가 나가서 전투가 끝났어요."));
    }
}
#endif
