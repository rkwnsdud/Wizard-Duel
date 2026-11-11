using System;
using System.Globalization;

// 네트워크 콜백과 맵 생성에 의존하지 않는 서버의 1대1 대기실 상태.
public sealed class LobbySession
{
    readonly Func<int> codeGenerator;
    // 서버에 방이 여러 개일 때 다른 방이 쓰는 코드를 피한다. 방 하나만 쓰면 null이다.
    readonly Func<string, bool> codeInUse;
    ulong? hostOwner;
    ulong? guestOwner;
    bool hostConfirmed;
    bool guestConfirmed;
    int lastInvalidatedCode = -1;
    NetworkMapSettings settings;

    public LobbySession(Func<int> codeGenerator = null, Func<string, bool> codeInUse = null)
    {
        var codeRandom = new Random(Guid.NewGuid().GetHashCode());
        this.codeGenerator = codeGenerator ?? (() => codeRandom.Next(1000000));
        this.codeInUse = codeInUse;
    }

    public bool HasRoom => hostOwner.HasValue;
    public bool Ready => hostConfirmed && guestConfirmed;
    public ulong HostClientId => hostOwner.GetValueOrDefault();
    public ulong GuestClientId => guestConfirmed ? guestOwner.GetValueOrDefault() : 0;
    public bool OpponentPresent => guestConfirmed;
    public string RoomCode { get; private set; } = string.Empty;
    public LobbyPhase Phase { get; private set; }
    // 이 방에서 몇 번째 판인지. 결과를 보여 주고 로비로 돌아갈 때 하나 오른다.
    public uint Round { get; private set; }
    // 결과 단계에서만 의미가 있다. 0은 방장(파랑), 1은 참가자(빨강).
    public byte WinnerSlot { get; private set; }
    public NetworkLobbySnapshot Snapshot => new NetworkLobbySnapshot
    {
        Code = RoomCode,
        HostClientId = HostClientId,
        OpponentPresent = OpponentPresent,
        GuestClientId = GuestClientId,
        Round = Round,
        Phase = Phase,
        WinnerSlot = Phase == LobbyPhase.Result ? WinnerSlot : (byte)0,
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
        // 방 코드와 시드는 별개이며, 방금 닫힌 코드와 다른 방이 쓰는 코드도 다시 쓰지 않는다.
        while (code == lastInvalidatedCode || code == settings.Seed || (codeInUse != null && codeInUse(FormatCode(code))))
            code = (code + 1) % 1000000;
        RoomCode = FormatCode(code);
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

    // 전투 중 먼저 쓰러진 플레이어의 슬롯을 받아 결과 단계로 넘긴다. 승자는 반대 슬롯이고, 이미 끝난 판이면 무시한다.
    public bool TryFinishBattle(int defeatedSlot)
    {
        if (Phase != LobbyPhase.Battle || !Ready || (defeatedSlot != 0 && defeatedSlot != 1)) return false;
        WinnerSlot = (byte)(1 - defeatedSlot);
        Phase = LobbyPhase.Result;
        return true;
    }

    // 결과를 보여 준 뒤 다음 판의 로비로 돌아간다. 방 코드, 방장·참가자, 설정과 시드는 그대로 둔다.
    public bool ReturnToLobby()
    {
        if (Phase != LobbyPhase.Result) return false;
        Phase = LobbyPhase.Lobby;
        WinnerSlot = 0;
        Round++;
        return true;
    }

    public static bool IsValidCode(string code)
    {
        if (code == null || code.Length != 6) return false;
        foreach (char digit in code)
            if (digit < '0' || digit > '9') return false;
        return true;
    }

    static string FormatCode(int code) => code.ToString("D6", CultureInfo.InvariantCulture);

    void ClearRoom()
    {
        lastInvalidatedCode = int.Parse(RoomCode, CultureInfo.InvariantCulture);
        RoomCode = string.Empty;
        hostOwner = null;
        guestOwner = null;
        hostConfirmed = false;
        guestConfirmed = false;
        Phase = LobbyPhase.Lobby;
        Round = 0;
        WinnerSlot = 0;
        settings = default;
    }
}
