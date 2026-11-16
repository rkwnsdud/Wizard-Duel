#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 무기 스폰 카탈로그·배치·라운드 규칙을 확인하는 EditMode 테스트.
public class WeaponSpawnTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    const string CatalogPath = "Assets/Game/Battle/WeaponSpawnCatalog.asset";

    readonly List<GameObject> created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null) Object.DestroyImmediate(created[i]);
        }
        created.Clear();
    }

    [Test]
    public void Catalog_HasFourSpawnableWeapons_AndExcludesBasicWand()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>(CatalogPath);
        Assert.That(catalog, Is.Not.Null, CatalogPath);
        Assert.That(catalog.notificationFont, Is.Not.Null);

        SfxLibrary.Entry chime = Sfx.Library != null ? Sfx.Library.Find(SfxId.WeaponSpawnChime) : default;
        Assert.That(chime.clip, Is.Not.Null);
        Assert.That(chime.clip.length, Is.InRange(1.7f, 1.9f));
        Assert.That(chime.volume, Is.GreaterThan(0f));

        var ids = new HashSet<byte>();
        int choices = 0;
        foreach (WeaponDefinition entry in catalog.Entries)
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.id, Is.Not.Zero);
            Assert.That(ids.Add(entry.id), Is.True, $"Duplicate weapon ID {entry.id}");
            Assert.That(entry.prefab, Is.Not.Null, $"Missing prefab for {entry.id}");
            if (catalog.IsSpawnable(entry)) choices++;
        }

        Assert.That(choices, Is.EqualTo(4));
        Assert.That(catalog.IsSpawnable(catalog.Find(1)), Is.False,
            "Basic Wand must remain excluded from arena weapon drops.");
    }

    [Test]
    public void Placement_AllShapesAndDensities_SelectValidClearSites()
    {
        var shapes = new[] { MapShape.Circle, MapShape.Rectangle, MapShape.Elongated, MapShape.Donut };
        var seeds = new[] { 7301, 42, 2026, 1234, 9001 };
        var densities = new[] { 0f, 0.15f, DuelSettings.MaxObstacleDensity };

        foreach (MapShape shape in shapes)
        foreach (int seed in seeds)
        foreach (float density in densities)
        {
            MapLayout layout = MapLayoutGenerator.Generate(new MapGenerationSettings
            {
                Seed = seed,
                Shape = shape,
                ObstacleDensity = density,
                BushDensity = 0.05f
            });
            List<Vector2Int> selected = WeaponSpawnPlacement.Select(layout);
            Assert.That(selected.Count, Is.InRange(1, 12), $"{shape}/{seed}/{density}");

            foreach (Vector2Int cell in selected)
            {
                Assert.That(layout[cell.x, cell.y], Is.EqualTo(MapCell.Floor));
                Assert.That(MapLayout.InSpawnZone(cell, layout.SpawnA, 3), Is.False);
                Assert.That(MapLayout.InSpawnZone(cell, layout.SpawnB, 3), Is.False);
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    if (Mathf.Abs(x) + Mathf.Abs(y) > 1) continue;
                    MapCell neighbor = layout[cell.x + x, cell.y + y];
                    Assert.That(neighbor != MapCell.Wall && neighbor != MapCell.Void, Is.True,
                        $"Site {cell} touches wall or void at {shape}/{seed}/{density}");
                }
                foreach (Vector2Int other in selected)
                {
                    if (cell != other)
                        Assert.That(Vector2Int.Distance(cell, other), Is.GreaterThanOrEqualTo(4f));
                }
            }
        }
    }

    [Test]
    public void Round_EnforcesTimerOccupancyDuplicateRemovalCapAndReset()
    {
        var round = new WeaponSpawnRound();
        var positions = new[] { Vector3.zero, new Vector3(5f, 0f, 0f) };
        var weapons = new byte[] { 2, 3, 4, 5 };

        round.Begin(positions, weapons, 100, 7);
        Assert.That(round.Tick(109.999, null, out _), Is.False);
        Assert.That(round.Tick(110, null, out SpawnedWeapon first), Is.True);
        Assert.That(round.Tick(110, null, out _), Is.False);
        Assert.That(round.Tick(120, null, out SpawnedWeapon second), Is.True);
        Assert.That(second.Site, Is.Not.EqualTo(first.Site));
        Assert.That(round.Tick(130, null, out _), Is.False);
        Assert.That(round.Remove(first.Id), Is.True);
        Assert.That(round.Remove(first.Id), Is.False);
        Assert.That(round.Tick(140, null, out SpawnedWeapon reused), Is.True);
        Assert.That(reused.Site, Is.EqualTo(first.Site));

        round.Stop();
        Assert.That(round.Drops.Count, Is.EqualTo(0));
        Assert.That(round.Tick(1000, null, out _), Is.False);

        round.Begin(positions, weapons, 1000, 8);
        Assert.That(round.Tick(1009, null, out _), Is.False);
        Assert.That(round.Tick(1010, _ => false, out _), Is.False);
        Assert.That(round.Tick(1300, null, out SpawnedWeapon late), Is.True);
        Assert.That(late.Id, Is.Not.EqualTo(first.Id));
        Assert.That(round.Tick(1300, null, out _), Is.False);

        round.Begin(new[] { Vector3.zero, Vector3.right * 5f, Vector3.right * 10f }, weapons, 2000, 9);
        Assert.That(round.Tick(2010, null, out SpawnedWeapon cappedFirst), Is.True);
        Assert.That(round.Tick(2020, null, out _), Is.True);
        Assert.That(round.Tick(2030, null, out _), Is.False);
        Assert.That(round.Drops.Count, Is.EqualTo(2));
        Assert.That(round.Remove(cappedFirst.Id), Is.True);
        Assert.That(round.Tick(2031, null, out _), Is.False);
        Assert.That(round.Tick(2040, null, out _), Is.True);
        Assert.That(round.Drops.Count, Is.EqualTo(2));
    }

    [Test]
    public void DuelArena_PreparePreservesWeaponSitesAcrossRooms_WithoutKeepingMap()
    {
        ProceduralMap map = MapRendererCombineTests.NewBattleMap("WeaponSpawnTests Map");
        created.Add(map.gameObject);
        var arena = map.gameObject.AddComponent<DuelArena>();
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>(CatalogPath);
        typeof(DuelArena).GetField("map", PrivateInstance).SetValue(arena, map);
        typeof(DuelArena).GetField("weaponCatalog", PrivateInstance).SetValue(arena, catalog);

        DuelMatch firstMatch = arena.Prepare(new MapGenerationSettings
        {
            Seed = 7301,
            Shape = MapShape.Rectangle,
            ObstacleDensity = 0.15f,
            BushDensity = 0.05f
        });
        Assert.That(firstMatch, Is.Not.Null);
        Assert.That(firstMatch.WeaponSites.Length, Is.GreaterThan(0));
        Assert.That(arena.HasMap, Is.False);
        Vector3 firstSite = firstMatch.WeaponSites[0];

        DuelMatch secondMatch = arena.Prepare(new MapGenerationSettings
        {
            Seed = 42,
            Shape = MapShape.Donut,
            ObstacleDensity = 0.15f,
            BushDensity = 0.05f
        });
        Assert.That(secondMatch, Is.Not.Null);
        Assert.That(secondMatch.WeaponSites.Length, Is.GreaterThan(0));
        Assert.That(arena.HasMap, Is.False);
        Assert.That(firstMatch.WeaponSites[0], Is.EqualTo(firstSite));
        Assert.That(firstMatch.WeaponSites, Is.Not.SameAs(secondMatch.WeaponSites));
    }
}
#endif
