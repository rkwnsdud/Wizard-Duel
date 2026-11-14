using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WeaponSpawnValidation
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Weapon spawn validation: " + message);
    }

    public static void Validate()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<WeaponSpawnCatalog>("Assets/Scripts/Battle/WeaponSpawnCatalog.asset");
        Check(catalog != null, "catalog missing");
        Check(catalog.notificationFont != null, "notification font missing");
        Check(catalog.notificationSound != null && catalog.notificationSound.length > 1.7f &&
            catalog.notificationSound.length < 1.9f, "notification chime missing/incorrect duration");
        Check(catalog.notificationVolume > 0 && catalog.notificationVolume <= .25f, "notification volume too loud");
        var ids = new HashSet<byte>();
        int choices = 0;
        foreach (var entry in catalog.Entries)
        {
            Check(entry.id != 0 && ids.Add(entry.id), "duplicate/zero weapon ID");
            Check(entry.prefab != null, "missing weapon prefab");
            if (catalog.IsSpawnable(entry)) choices++;
        }
        Check(choices == 4, "expected four spawnable weapons");
        Check(!catalog.IsSpawnable(new WeaponSpawnCatalog.Entry { prefab = catalog.Find(1).prefab, canSpawn = true }),
            "Basic Wand must remain excluded even if its inspector flag is checked");

        int maps = 0, sites = 0;
        foreach (MapShape shape in new[] { MapShape.Circle, MapShape.Rectangle, MapShape.Elongated, MapShape.Donut })
        foreach (int seed in new[] { 7301, 42, 2026, 1234, 9001 })
        foreach (float density in new[] { 0f, .15f, DuelSettings.MaxObstacleDensity })
        {
            var layout = MapLayoutGenerator.Generate(new MapGenerationSettings
            { Seed = seed, Shape = shape, ObstacleDensity = density, BushDensity = .05f });
            var selected = WeaponSpawnPlacement.Select(layout);
            Check(selected.Count > 0 && selected.Count <= 12, $"no usable sites: {shape}/{seed}/{density}");
            foreach (var cell in selected)
            {
                Check(layout[cell.x, cell.y] == MapCell.Floor, "site is not open floor");
                Check(!MapLayout.InSpawnZone(cell, layout.SpawnA, 3) && !MapLayout.InSpawnZone(cell, layout.SpawnB, 3),
                    "site inside a player spawn zone");
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (Mathf.Abs(x) + Mathf.Abs(y) <= 1)
                    Check(layout[cell.x + x, cell.y + y] != MapCell.Wall && layout[cell.x + x, cell.y + y] != MapCell.Void,
                        "site touches a wall or map hole");
                foreach (var other in selected)
                    if (cell != other) Check(Vector2Int.Distance(cell, other) >= 4f, "sites overlap");
            }
            maps++;
            sites += selected.Count;
        }

        var round = new WeaponSpawnRound();
        var positions = new[] { Vector3.zero, new Vector3(5, 0, 0) };
        var weapons = new byte[] { 2, 3, 4, 5 };
        round.Begin(positions, weapons, 100, 7);
        Check(!round.Tick(109.999, null, out _), "spawned before 10 seconds");
        Check(round.Tick(110, null, out var first), "first spawn missing");
        Check(!round.Tick(110, null, out _), "multiple spawns in one interval");
        Check(round.Tick(120, null, out var second) && second.Site != first.Site, "occupied site reused");
        Check(!round.Tick(130, null, out _), "full map should skip a spawn");
        Check(round.Remove(first.Id) && !round.Remove(first.Id), "pickup can be claimed twice");
        Check(round.Tick(140, null, out var reused) && reused.Site == first.Site, "freed site not reusable");
        round.Stop();
        Check(round.Drops.Count == 0 && !round.Tick(1000, null, out _), "round end leaves pickups/timer alive");
        round.Begin(positions, weapons, 1000, 8);
        Check(!round.Tick(1009, null, out _), "new round retained the previous timer");
        Check(!round.Tick(1010, _ => false, out _), "spawned on an obstructed/player-occupied site");
        Check(round.Tick(1300, null, out var late) && late.Id != first.Id && !round.Tick(1300, null, out _),
            "stall caused a catch-up burst or reused a stale pickup ID");

        round.Begin(new[] { Vector3.zero, Vector3.right * 5, Vector3.right * 10 }, weapons, 2000, 9);
        Check(round.Tick(2010, null, out var cappedFirst) && round.Tick(2020, null, out _),
            "two available drops should be allowed");
        Check(!round.Tick(2030, null, out _) && round.Drops.Count == 2,
            "spawned a third drop despite the two-drop limit");
        Check(round.Remove(cappedFirst.Id) && !round.Tick(2031, null, out _) &&
            round.Tick(2040, null, out _) && round.Drops.Count == 2,
            "pickup should allow a replacement only at the next interval");

        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        var arena = UnityEngine.Object.FindFirstObjectByType<DuelArena>();
        Check(arena != null && new SerializedObject(arena).FindProperty("weaponCatalog").objectReferenceValue == catalog,
            "Main arena catalog not wired");
        var firstMatch = arena.Prepare(new MapGenerationSettings
            { Seed = 7301, Shape = MapShape.Rectangle, ObstacleDensity = .15f, BushDensity = .05f });
        Check(firstMatch != null && firstMatch.WeaponSites.Length > 0 && !arena.HasMap,
            "server must retain weapon sites after clearing its temporary map");
        Vector3 firstSite = firstMatch.WeaponSites[0];
        var secondMatch = arena.Prepare(new MapGenerationSettings
            { Seed = 42, Shape = MapShape.Donut, ObstacleDensity = .15f, BushDensity = .05f });
        Check(secondMatch != null && secondMatch.WeaponSites.Length > 0 && !arena.HasMap &&
            firstMatch.WeaponSites[0] == firstSite && !ReferenceEquals(firstMatch.WeaponSites, secondMatch.WeaponSites),
            "preparing another room replaced the first room's weapon sites");
        Debug.Log($"WEAPON_SPAWN_VALIDATION_PASS maps={maps} sites={sites} choices={choices}; timer, occupancy, duplicate claim, room site persistence, Main wiring");
    }

    // Optional command-line build for the existing Main scene, without adding a scene asset.
    public static void Build()
    {
        Validate();
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-weaponSpawnBuildPath");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing -weaponSpawnBuildPath");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" }, locationPathName = args[index + 1],
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Smoke build failed");
        Debug.Log("WEAPON_SPAWN_BUILD_PASS");
    }
}
