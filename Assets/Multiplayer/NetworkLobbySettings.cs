using System;
using Unity.Netcode;

[Serializable]
public struct NetworkLobbySettings : INetworkSerializable
{
    public bool SeedFixed;
    public NetworkMapSettings Map;

    public NetworkLobbySettings(DuelSettings settings)
    {
        SeedFixed = settings.SeedFixed;
        Map = new NetworkMapSettings(settings.Map);
    }

    public DuelSettings ToSettings() => new DuelSettings
    {
        SeedFixed = SeedFixed,
        Map = Map.ToSettings()
    };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SeedFixed);
        serializer.SerializeValue(ref Map);
    }
}
