using System;
using Unity.Netcode;

[Serializable]
public struct NetworkLobbySnapshot : INetworkSerializable
{
    public string Code;
    public ulong HostClientId;
    public bool OpponentPresent;
    public ulong GuestClientId;
    public NetworkLobbySettings Settings;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // 기본 구조체도 안전하게 전송할 수 있도록 null 코드는 빈 문자열로 보낸다.
        if (serializer.IsWriter && Code == null) Code = string.Empty;
        serializer.SerializeValue(ref Code);
        serializer.SerializeValue(ref HostClientId);
        serializer.SerializeValue(ref OpponentPresent);
        serializer.SerializeValue(ref GuestClientId);
        serializer.SerializeValue(ref Settings);
    }
}
