using System;
using Unity.Netcode;

[Serializable]
public struct LobbyJoinRequest : INetworkSerializable
{
    public const int CurrentVersion = 1;

    public int Version;
    public bool CreateRoom;
    public string Code;
    public NetworkLobbySettings Settings;

    public LobbyJoinRequest(bool createRoom, string code, DuelSettings settings)
    {
        Version = CurrentVersion;
        CreateRoom = createRoom;
        Code = code ?? string.Empty;
        Settings = settings == null ? default : new NetworkLobbySettings(settings);
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
