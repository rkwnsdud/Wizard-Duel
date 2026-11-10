#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkMapSettingsTests
{
    [TestCase(7301, false, MapShape.Random, MapSymmetry.Rotate180)]
    [TestCase(0, false, MapShape.Donut, MapSymmetry.None)]
    [TestCase(-11, false, MapShape.Elongated, MapSymmetry.Rotate180)]
    [TestCase(7301, true, MapShape.Circle, MapSymmetry.None)]
    [TestCase(0, true, MapShape.Rectangle, MapSymmetry.Rotate180)]
    [TestCase(-11, true, MapShape.Donut, MapSymmetry.None)]
    public void WireRoundTrip_PreservesAllSettingsAndGeneratedLayout(
        int seed, bool fixedSize, MapShape shape, MapSymmetry symmetry)
    {
        var original = new MapGenerationSettings
        {
            Seed = seed,
            Size = fixedSize ? new Vector2Int(35, 29) : (Vector2Int?)null,
            Shape = shape,
            ObstacleDensity = 0.27f,
            BushDensity = 0.13f,
            Symmetry = symmetry,
            NoiseStrength = 0.65f,
            NoiseScale = 1.25f
        };
        using var writer = new FastBufferWriter(128, Allocator.Temp);
        writer.WriteNetworkSerializable(new NetworkMapSettings(original));
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out NetworkMapSettings received);
        MapGenerationSettings client = received.ToSettings();

        Assert.That(client.Seed, Is.EqualTo(original.Seed));
        Assert.That(client.Size, Is.EqualTo(original.Size));
        Assert.That(client.Shape, Is.EqualTo(original.Shape));
        Assert.That(client.ObstacleDensity, Is.EqualTo(original.ObstacleDensity));
        Assert.That(client.BushDensity, Is.EqualTo(original.BushDensity));
        Assert.That(client.Symmetry, Is.EqualTo(original.Symmetry));
        Assert.That(client.NoiseStrength, Is.EqualTo(original.NoiseStrength));
        Assert.That(client.NoiseScale, Is.EqualTo(original.NoiseScale));

        MapLayout serverMap = MapLayoutGenerator.Generate(original);
        MapLayout clientMap = MapLayoutGenerator.Generate(client);
        Assert.That(clientMap.Shape, Is.EqualTo(serverMap.Shape));
        Assert.That(clientMap.ToLayout(), Is.EqualTo(serverMap.ToLayout()));
        Assert.That(clientMap.SpawnA, Is.EqualTo(serverMap.SpawnA));
        Assert.That(clientMap.SpawnB, Is.EqualTo(serverMap.SpawnB));
    }
}
#endif
