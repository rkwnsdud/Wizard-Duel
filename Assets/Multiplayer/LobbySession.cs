using System;
using System.Globalization;

// 네트워크 콜백과 맵 생성에 의존하지 않는 서버의 1대1 대기실 상태.
public sealed class LobbySession
{
    readonly Func<int> codeGenerator;
    ulong? hostOwner;
    ulong? guestOwner;
    bool hostConfirmed;
    bool guestConfirmed;
    int lastInvalidatedCode = -1;
    NetworkLobbySettings settings;

    public LobbySession(Func<int> codeGenerator = null)
    {
        var codeRandom = new Random(Guid.NewGuid().GetHashCode());
        this.codeGenerator = codeGenerator ?? (() => codeRandom.Next(1000000));
    }

    public bool HasRoom => hostOwner.HasValue;
    public bool Ready => hostConfirmed && guestConfirmed;
    public ulong HostClientId => hostOwner.GetValueOrDefault();
    public ulong GuestClientId => guestConfirmed ? guestOwner.GetValueOrDefault() : 0;
    public bool OpponentPresent => guestConfirmed;
    public string RoomCode { get; private set; } = string.Empty;
    public NetworkLobbySnapshot Snapshot => new NetworkLobbySnapshot
    {
        Code = RoomCode,
        HostClientId = HostClientId,
        OpponentPresent = OpponentPresent,
        GuestClientId = GuestClientId,
        Settings = settings
    };

    public bool Contains(ulong clientId) =>
        (hostConfirmed && hostOwner == clientId) || (guestConfirmed && guestOwner == clientId);

    public bool HasReservation(ulong clientId) =>
        (!hostConfirmed && hostOwner == clientId) || (!guestConfirmed && guestOwner == clientId);

    public bool TryCreate(ulong clientId, DuelSettings requested, out string reason)
    {
        reason = "A room already exists.";
        if (HasRoom) return false;
        if (!DuelSettings.ValidateSettings(requested, out reason)) return false;

        settings = new NetworkLobbySettings(requested);
        int code = (int)((uint)codeGenerator() % 1000000);
        // 방 코드와 시드는 별개이며, 방금 닫힌 코드도 바로 다시 쓰지 않는다.
        while (code == lastInvalidatedCode || code == settings.Map.Seed)
            code = (code + 1) % 1000000;
        RoomCode = code.ToString("D6", CultureInfo.InvariantCulture);
        hostOwner = clientId;
        reason = string.Empty;
        return true;
    }

    public bool TryJoin(ulong clientId, string code, out string reason)
    {
        reason = "Enter a six-digit room code.";
        if (!IsValidCode(code)) return false;
        reason = "The room code is invalid.";
        if (!hostConfirmed || code != RoomCode) return false;
        reason = "This client already belongs to the room.";
        if (hostOwner == clientId || guestOwner == clientId) return false;
        reason = "This room already has two players.";
        if (guestOwner.HasValue) return false;

        guestOwner = clientId;
        reason = string.Empty;
        return true;
    }

    public bool Confirm(ulong clientId)
    {
        if (!hostConfirmed && hostOwner == clientId)
        {
            hostConfirmed = true;
            return true;
        }
        if (!guestConfirmed && guestOwner == clientId)
        {
            guestConfirmed = true;
            return true;
        }
        return false;
    }

    public bool CancelReservation(ulong clientId)
    {
        if (!hostConfirmed && hostOwner == clientId)
        {
            ClearRoom();
            return true;
        }
        if (!guestConfirmed && guestOwner == clientId)
        {
            guestOwner = null;
            return true;
        }
        return false;
    }

    public bool Disconnect(ulong clientId)
    {
        if (!Contains(clientId)) return CancelReservation(clientId);
        ClearRoom();
        return true;
    }

    public bool TryUpdateSettings(ulong sender, DuelSettings requested, out string reason)
    {
        reason = "Only the connected host can change room settings.";
        if (!hostConfirmed || hostOwner != sender) return false;
        if (!DuelSettings.ValidateSettings(requested, out reason)) return false;

        settings = new NetworkLobbySettings(requested);
        reason = string.Empty;
        return true;
    }

    public static bool IsValidCode(string code)
    {
        if (code == null || code.Length != 6) return false;
        foreach (char digit in code)
            if (digit < '0' || digit > '9') return false;
        return true;
    }

    void ClearRoom()
    {
        lastInvalidatedCode = int.Parse(RoomCode, CultureInfo.InvariantCulture);
        RoomCode = string.Empty;
        hostOwner = null;
        guestOwner = null;
        hostConfirmed = false;
        guestConfirmed = false;
        settings = default;
    }
}
