using System;
using System.Globalization;

// 네트워크 콜백과 맵 생성에 의존하지 않는 서버의 1대1 대기실 상태.
public sealed class LobbySession
{
    readonly Func<int> seedGenerator;
    readonly Func<int> codeGenerator;
    ulong? hostOwner;
    ulong? guestOwner;
    bool hostConfirmed;
    bool guestConfirmed;
    int lastInvalidatedCode = -1;
    NetworkLobbySettings settings;

    public LobbySession(Func<int> seedGenerator = null, Func<int> codeGenerator = null)
    {
        var seedRandom = new Random(Guid.NewGuid().GetHashCode());
        var codeRandom = new Random(Guid.NewGuid().GetHashCode());
        this.seedGenerator = seedGenerator ?? (() => seedRandom.Next(int.MinValue, int.MaxValue));
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
        if (!ValidateSettings(requested, out reason)) return false;

        settings = new NetworkLobbySettings(requested);
        if (!settings.SeedFixed) settings.Map.Seed = seedGenerator();
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

    public bool TryUpdateSettings(ulong sender, DuelSettings requested, out string reason, bool seedEdited = true)
    {
        reason = "Only the connected host can change room settings.";
        if (!hostConfirmed || hostOwner != sender) return false;
        if (!ValidateSettings(requested, out reason)) return false;

        var updated = new NetworkLobbySettings(requested);
        // 자동 모드의 일반 수정과 고정 전환은 현재 시드를 유지한다.
        if (!settings.SeedFixed) updated.Map.Seed = settings.Map.Seed;
        else if (!updated.SeedFixed) updated.Map.Seed = seedGenerator();
        // 일반 설정 변경에 남아 있는 오래된 입력 시드는 확정한 서버 시드를 덮어쓰지 않는다.
        else if (!seedEdited) updated.Map.Seed = settings.Map.Seed;
        settings = updated;
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

    public static bool ValidateSettings(DuelSettings requested, out string reason)
    {
        reason = "Room settings are invalid.";
        if (requested == null || requested.Map == null) return false;
        MapGenerationSettings map = requested.Map;
        if (map.Size.HasValue &&
            (map.Size.Value.x < DuelSettings.MinSize || map.Size.Value.x > DuelSettings.MaxSize ||
             map.Size.Value.y < DuelSettings.MinSize || map.Size.Value.y > DuelSettings.MaxSize)) return false;
        if (!Enum.IsDefined(typeof(MapShape), map.Shape) ||
            !Enum.IsDefined(typeof(MapSymmetry), map.Symmetry)) return false;
        // 양방향 범위 비교는 NaN과 무한대도 거절한다.
        if (!(map.ObstacleDensity >= 0 && map.ObstacleDensity <= DuelSettings.MaxObstacleDensity) ||
            !(map.NoiseStrength >= 0 && map.NoiseStrength <= 1) ||
            !(map.NoiseScale >= 0.25f && map.NoiseScale <= 2)) return false;
        reason = string.Empty;
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
