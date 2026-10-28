#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

public class MapFingerprintTests
{
    GameObject root;

    [SetUp]
    public void SetUp() => root = new GameObject("MapFingerprintTests");

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
    }

    [Test]
    public void SameLayoutAndSpawns_GiveSameFingerprint()
    {
        Transform[] spawns = Spawns(new Vector3(-12f, 0.5f, -10f), 45f, new Vector3(12f, 0.5f, 10f), 225f);
        string first = MapFingerprint.Compute(MapLayoutGenerator.Generate(Settings(7301)), spawns);
        string second = MapFingerprint.Compute(MapLayoutGenerator.Generate(Settings(7301)), spawns);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Has.Length.EqualTo(64));
        Assert.That(first, Does.Match("^[0-9A-F]{64}$"));
    }

    [Test]
    public void DifferentLayoutOrSpawnTransform_ChangesFingerprint()
    {
        Transform[] spawns = Spawns(new Vector3(-12f, 0.5f, -10f), 45f, new Vector3(12f, 0.5f, 10f), 225f);
        MapLayout layout = MapLayoutGenerator.Generate(Settings(7301));
        string original = MapFingerprint.Compute(layout, spawns);

        Assert.That(MapFingerprint.Compute(MapLayoutGenerator.Generate(Settings(7302)), spawns), Is.Not.EqualTo(original));

        Transform[] moved = Spawns(new Vector3(-12f, 0.5f, -10f), 45f, new Vector3(12.001f, 0.5f, 10f), 225f);
        Assert.That(MapFingerprint.Compute(layout, moved), Is.Not.EqualTo(original));

        Transform[] turned = Spawns(new Vector3(-12f, 0.5f, -10f), 46f, new Vector3(12f, 0.5f, 10f), 225f);
        Assert.That(MapFingerprint.Compute(layout, turned), Is.Not.EqualTo(original));
    }

    static MapGenerationSettings Settings(int seed) => new MapGenerationSettings
    {
        Seed = seed,
        Shape = MapShape.Circle,
        ObstacleDensity = 0.15f,
        Symmetry = MapSymmetry.Rotate180,
        NoiseStrength = 0.5f,
        NoiseScale = 0.75f
    };

    Transform[] Spawns(Vector3 blue, float blueYaw, Vector3 red, float redYaw)
    {
        var spawns = new Transform[2];
        for (int slot = 0; slot < spawns.Length; slot++)
        {
            spawns[slot] = new GameObject($"Spawn {slot}").transform;
            spawns[slot].SetParent(root.transform);
            spawns[slot].SetPositionAndRotation(slot == 0 ? blue : red, Quaternion.Euler(0f, slot == 0 ? blueYaw : redYaw, 0f));
        }
        return spawns;
    }
}
#endif
