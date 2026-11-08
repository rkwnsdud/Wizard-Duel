using System;
using Unity.Netcode;

[Serializable]
public struct LobbyJoinRequest : INetworkSerializable
{
    // 3: 스냅샷에 방 단계를 넣고 전투 시작·맵 준비 메시지를 추가했다.
    // 4: 전투 플레이어에 공격 요청·승인 RPC와 서버 체력을 추가했다.
    // 5: 장애물을 구조물 단위로 배치하고 스폰 안전 구역을 넓혀 같은 시드의 맵이 달라졌다.
    public const int CurrentVersion = 5;

    public int Version;
    public bool CreateRoom;
    public string Code;
    public NetworkMapSettings Settings;

    public LobbyJoinRequest(bool createRoom, string code, DuelSettings settings)
    {
        Version = CurrentVersion;
        CreateRoom = createRoom;
        Code = code ?? string.Empty;
        Settings = settings == null ? default : new NetworkMapSettings(settings.Map);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        if (serializer.IsWriter && Code == null) Code = string.Empty;
        serializer.SerializeValue(ref Version);
        serializer.SerializeValue(ref CreateRoom);
        serializer.SerializeValue(ref Code);
        serializer.SerializeValue(ref Settings);
    }
}
