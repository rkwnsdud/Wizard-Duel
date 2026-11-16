using System;
using System.Globalization;

// 서버와 클라이언트 사이에 주고받는 로비 거절·오류 사유와 로컬 검증 실패 코드다.
// 전선과 Netcode 연결 끊김 사유로는 이 enum 코드만 전송하며, 화면 표시는 클라이언트가 한국어로 매핑한다.
public enum LobbyReason : byte
{
    None = 0,
    InvalidRequest = 1,
    InvalidRoomCode = 2,
    RoomNotFound = 3,
    ServerFull = 4,
    AlreadyInRoom = 5,
    RoomAlreadyExists = 6,
    RoomFull = 7,
    InvalidSettings = 8,
    NotHost = 9,
    BattleAlreadyStarted = 10,
    SettingsOutOfDate = 11,
    OpponentMissing = 12,
    AlreadyStarted = 13,
    MapGenerationFailed = 14,
    MapMismatch = 15,
    RoomClosed = 16,
    Disconnected = 17,
    ConnectionTimedOut = 18,
    NetworkNotConfigured = 19,
    ConnectionStartFailed = 20,
}

public static class LobbyReasonCodec
{
    public static bool IsValidRejectionCode(byte raw) =>
        raw != 0 && Enum.IsDefined(typeof(LobbyReason), raw);

    // Netcode ConnectionApprovalResponse.Reason / DisconnectClient 문자열 API를 통과할 때 숫자 코드 문자열만 싣는다.
    public static string ToWireReason(LobbyReason reason) =>
        reason == LobbyReason.None ? string.Empty : ((byte)reason).ToString(CultureInfo.InvariantCulture);

    public static LobbyReason FromWireReason(string token, LobbyReason fallback = LobbyReason.Disconnected)
    {
        if (string.IsNullOrEmpty(token)) return fallback;
        if (byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte raw) &&
            IsValidRejectionCode(raw))
            return (LobbyReason)raw;
        return fallback;
    }
}

public static class LobbyReasonMessages
{
    public static string SettingsRejected(LobbyReason reason) => reason switch
    {
        LobbyReason.NotHost => "방장만 대기실 설정을 바꿀 수 있어요.",
        LobbyReason.InvalidSettings => "맵 설정이 올바르지 않아 변경하지 않았어요.",
        LobbyReason.BattleAlreadyStarted => "전투 준비가 시작되어 맵 설정을 바꿀 수 없어요.",
        _ => "맵 설정을 적용할 수 없어요. 다시 시도해 주세요."
    };

    public static string StartRejected(LobbyReason reason) => reason switch
    {
        LobbyReason.NotHost => "방장만 게임을 시작할 수 있어요.",
        LobbyReason.OpponentMissing => "상대가 들어와야 시작할 수 있어요",
        LobbyReason.AlreadyStarted => "이미 전투를 준비하고 있어요.",
        LobbyReason.MapGenerationFailed => "이 설정으로는 전투 맵을 만들 수 없어요.",
        _ => "게임을 시작할 수 없어요. 다시 시도해 주세요."
    };

    public static string ConnectionNotice(LobbyReason reason) => reason switch
    {
        LobbyReason.RoomNotFound => "유효하지 않은 맵 코드예요. 코드를 다시 확인해 주세요.",
        LobbyReason.RoomFull => "이미 두 명이 참가한 대기실이에요.",
        LobbyReason.ServerFull => "서버에 방이 가득 찼어요. 잠시 후 다시 시도해 주세요.",
        LobbyReason.InvalidRoomCode => "숫자 6자리 맵 코드를 입력해 주세요.",
        LobbyReason.RoomClosed => "상대가 나가서 대기실이 닫혔어요.",
        LobbyReason.MapGenerationFailed => "전투 맵을 만들지 못해 대기실에서 나왔어요.",
        LobbyReason.MapMismatch => "맵이 서버와 달라서 연결이 끊겼어요.",
        _ => "서버 연결이 끊겼거나 접속할 수 없어요. 잠시 후 다시 시도해 주세요."
    };

    public static string ClosedNotice(LobbyReason reason, bool battleVisible)
    {
        if (reason == LobbyReason.None) return string.Empty;
        if (battleVisible && reason == LobbyReason.RoomClosed) return "상대가 나가서 전투가 끝났어요.";
        return ConnectionNotice(reason);
    }
}
