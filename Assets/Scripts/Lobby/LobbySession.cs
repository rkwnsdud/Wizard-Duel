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
    NetworkMapSettings settings;

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
    public LobbyPhase Phase { get; private set; }
    public NetworkLobbySnapshot Snapshot => new NetworkLobbySnapshot
    {
        Code = RoomCode,
        HostClientId = HostClientId,
        OpponentPresent = OpponentPresent,
        GuestClientId = GuestClientId,
        Phase = Phase,
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

        settings = new NetworkMapSettings(requested.Map);
        int code = (int)((uint)codeGenerator() % 1000000);
        // 방 코드와 시드는 별개이며, 방금 닫힌 코드도 바로 다시 쓰지 않는다.
        while (code == lastInvalidatedCode || code == settings.Seed)
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
        reason = "Room settings cannot change once the duel starts.";
        if (Phase != LobbyPhase.Lobby) return false;
        if (!DuelSettings.ValidateSettings(requested, out reason)) return false;

        settings = new NetworkMapSettings(requested.Map);
        reason = string.Empty;
        return true;
    }

    // 확정된 방장이 상대와 함께 있을 때만 전투 준비로 넘긴다. 저장한 시드와 설정은 그대로 쓴다.
    public bool TryStart(ulong sender, out string reason)
    {
        reason = "Only the connected host can start the duel.";
        if (!hostConfirmed || hostOwner != sender) return false;
        reason = "Two players must join before the duel starts.";
        if (!guestConfirmed) return false;
        reason = "The duel has already started.";
        if (Phase != LobbyPhase.Lobby) return false;

        Phase = LobbyPhase.Preparing;
        reason = string.Empty;
        return true;
    }

    // 서버가 전투 맵을 만들지 못했을 때 로비 단계로 되돌린다.
    public bool CancelStart()
    {
        if (Phase != LobbyPhase.Preparing) return false;
        Phase = LobbyPhase.Lobby;
        return true;
    }

    // 두 플레이어가 소환되고 전투 입력을 켤 때 부른다.
    public bool BeginBattle()
    {
        if (Phase != LobbyPhase.Preparing || !Ready) return false;
        Phase = LobbyPhase.Battle;
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
        Phase = LobbyPhase.Lobby;
        settings = default;
    }
}
