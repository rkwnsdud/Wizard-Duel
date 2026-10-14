using System;
using Unity.Netcode;

[Serializable]
public struct NetworkLobbySettings : INetworkSerializable
{
    public NetworkMapSettings Map;

    public NetworkLobbySettings(DuelSettings settings)
    {
        Map = new NetworkMapSettings(settings.Map);
    }

    public DuelSettings ToSettings() => new DuelSettings
    {
        Map = Map.ToSettings()
    };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Map);
    }
}
