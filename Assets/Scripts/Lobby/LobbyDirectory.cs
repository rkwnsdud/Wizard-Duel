using System;
using System.Collections.Generic;

// 서버에 열린 방 하나. 방의 상태 기계(LobbySession)와, 그 방에만 속하는 서버 상태를 묶는다.
public sealed class LobbyRoom
{
    public LobbyRoom(LobbySession session)
    {
        Session = session;
        Code = session.RoomCode;
    }

    public LobbySession Session { get; }
    // 방이 닫힌 뒤에도 남는 코드. 로그와 목록 정리에 쓴다.
    public string Code { get; }
    // 방장이 보낸 설정 변경 중 서버가 마지막으로 받아들인 번호.
    public uint SettingsRevision;
    // 이번 판의 맵 지문이 서버와 같다고 확인된 클라이언트.
    public readonly HashSet<ulong> ReadyClients = new HashSet<ulong>();
    // 결과 단계를 끝내고 다음 판 로비로 돌아갈 시각.
    public float ResultDeadline;
    // 전투 준비부터 결과까지 이 방이 치르는 한 판. 로비 단계에서는 null이다.
    public DuelMatch Match;
}

// 서버가 동시에 여는 방 목록. 방 코드와 클라이언트로 방을 찾고, 방마다 LobbySession을 하나씩 둔다.
// 네트워크 콜백과 맵 생성에 의존하지 않는다.
public sealed class LobbyDirectory
{
    public const string ServerFull = "The server is full.";
    const string AlreadyInRoom = "This client already belongs to a room.";

    readonly Dictionary<string, LobbyRoom> byCode = new Dictionary<string, LobbyRoom>();
    // 예약만 한 클라이언트도 들어 있다. 확정 여부는 방의 LobbySession이 안다.
    readonly Dictionary<ulong, LobbyRoom> byClient = new Dictionary<ulong, LobbyRoom>();
    readonly List<LobbyRoom> rooms = new List<LobbyRoom>();
    readonly List<ulong> departing = new List<ulong>();
    readonly Func<int> codeGenerator;
    readonly int maxRooms;
    string lastClosedCode;

    public LobbyDirectory(int maxRooms, Func<int> codeGenerator = null)
    {
        this.maxRooms = Math.Max(1, maxRooms);
        var codeRandom = new Random(Guid.NewGuid().GetHashCode());
        this.codeGenerator = codeGenerator ?? (() => codeRandom.Next(1000000));
    }

    public IReadOnlyList<LobbyRoom> Rooms => rooms;
    public int Count => rooms.Count;

    public bool TryCreate(ulong clientId, DuelSettings settings, out LobbyRoom room, out string reason)
    {
        room = null;
        reason = AlreadyInRoom;
        if (byClient.ContainsKey(clientId)) return false;
        reason = ServerFull;
        if (rooms.Count >= maxRooms) return false;

        // 열린 방의 코드와 방금 닫힌 방의 코드는 피한다. 늦게 온 참가 요청이 다른 방에 들어가지 않는다.
        var session = new LobbySession(codeGenerator, code => byCode.ContainsKey(code) || code == lastClosedCode);
        if (!session.TryCreate(clientId, settings, out reason)) return false;
        room = new LobbyRoom(session);
        rooms.Add(room);
        byCode[room.Code] = room;
        byClient[clientId] = room;
        return true;
    }

    public bool TryJoin(ulong clientId, string code, out LobbyRoom room, out string reason)
    {
        room = null;
        reason = "Enter a six-digit room code.";
        if (!LobbySession.IsValidCode(code)) return false;
        reason = AlreadyInRoom;
        if (byClient.ContainsKey(clientId)) return false;
        reason = "The room code is invalid.";
        if (!byCode.TryGetValue(code, out LobbyRoom found)) return false;
        if (!found.Session.TryJoin(clientId, code, out reason)) return false;
        byClient[clientId] = found;
        room = found;
        return true;
    }

    // 승인된 클라이언트의 접속이 끝났을 때 부른다. 예약이 없거나 방이 그 사이 닫혔으면 false다.
    public bool Confirm(ulong clientId, out LobbyRoom room) =>
        byClient.TryGetValue(clientId, out room) && room.Session.Confirm(clientId);

    // 접속을 끝낸 구성원의 방. 예약만 했거나 방이 없으면 null이다.
    public LobbyRoom RoomOf(ulong clientId) =>
        byClient.TryGetValue(clientId, out LobbyRoom room) && room.Session.Contains(clientId) ? room : null;

    // 접속을 끝내지 못한 예약을 거둔다. 방장의 예약이었다면 그 방도 사라진다.
    public bool CancelReservation(ulong clientId)
    {
        if (!byClient.TryGetValue(clientId, out LobbyRoom room) || !room.Session.CancelReservation(clientId)) return false;
        byClient.Remove(clientId);
        if (!room.Session.HasRoom) Remove(room);
        return true;
    }

    // 방을 닫는다. 그 방의 구성원과 예약도 함께 지운다.
    public bool Remove(LobbyRoom room)
    {
        if (room == null || !rooms.Remove(room)) return false;
        byCode.Remove(room.Code);
        lastClosedCode = room.Code;
        departing.Clear();
        foreach (KeyValuePair<ulong, LobbyRoom> member in byClient)
            if (member.Value == room) departing.Add(member.Key);
        foreach (ulong clientId in departing) byClient.Remove(clientId);
        return true;
    }

    public void Clear()
    {
        rooms.Clear();
        byCode.Clear();
        byClient.Clear();
        lastClosedCode = null;
    }
}
