using System;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public struct NetworkMapSettings : INetworkSerializable
{
    public int Seed;
    public bool HasSize;
    public Vector2Int Size;
    public MapShape Shape;
    public float ObstacleDensity;
    public MapSymmetry Symmetry;
    public float NoiseStrength;
    public float NoiseScale;

    public NetworkMapSettings(MapGenerationSettings settings)
    {
        Seed = settings.Seed;
        HasSize = settings.Size.HasValue;
        Size = settings.Size.GetValueOrDefault();
        Shape = settings.Shape;
        ObstacleDensity = settings.ObstacleDensity;
        Symmetry = settings.Symmetry;
        NoiseStrength = settings.NoiseStrength;
        NoiseScale = settings.NoiseScale;
    }

    public MapGenerationSettings ToSettings() => new MapGenerationSettings
    {
        Seed = Seed,
        Size = HasSize ? Size : (Vector2Int?)null,
        Shape = Shape,
        ObstacleDensity = ObstacleDensity,
        Symmetry = Symmetry,
        NoiseStrength = NoiseStrength,
        NoiseScale = NoiseScale
    };

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Seed);
        serializer.SerializeValue(ref HasSize);
        serializer.SerializeValue(ref Size);
        serializer.SerializeValue(ref Shape);
        serializer.SerializeValue(ref ObstacleDensity);
        serializer.SerializeValue(ref Symmetry);
        serializer.SerializeValue(ref NoiseStrength);
        serializer.SerializeValue(ref NoiseScale);
    }
}
