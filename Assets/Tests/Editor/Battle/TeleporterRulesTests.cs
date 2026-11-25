#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TeleporterRulesTests
{
    [Test]
    public void CooldownCoversTheWholePairAndEndsAfterFiveSeconds()
    {
        var cooldowns = new TeleporterCooldowns(2);
        Assert.That(TeleporterCooldowns.Cooldown, Is.EqualTo(5f));
        Assert.That(cooldowns.IsReady(0, 0.0), Is.True);
        Assert.That(cooldowns.TryUse(0, 10.0), Is.True);
        // 쿨타임은 쌍 번호에 걸리므로 같은 쌍의 어느 쪽 타일도 5초 동안 못 쓴다. 다른 쌍은 그대로 쓸 수 있다.
        Assert.That(cooldowns.TryUse(0, 12.0), Is.False);
        Assert.That(cooldowns.IsReady(0, 14.999), Is.False);
        Assert.That(cooldowns.IsReady(1, 12.0), Is.True);
        Assert.That(cooldowns.ReadyAt(0), Is.EqualTo(15.0));
        Assert.That(cooldowns.IsReady(0, 15.0), Is.True);
        Assert.That(cooldowns.TryUse(0, 15.0), Is.True);
        Assert.That(cooldowns.IsReady(2, 100.0), Is.False, "Unknown pairs are never ready.");
        Assert.That(cooldowns.IsReady(-1, 100.0), Is.False);
    }

    [Test]
    public void WeaponSitesStayOffTeleporterTilesAndTheirRing()
    {
        for (int seed = 0; seed < 6; seed++)
        {
            MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings { Seed = seed, TeleporterPairs = 3 });
            Assert.That(map.Teleporters, Is.Not.Empty, $"seed {seed}");
            foreach (Vector2Int site in WeaponSpawnPlacement.Select(map))
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        Assert.That(map.IsTeleporter(site.x + dx, site.y + dy), Is.False, $"seed {seed} site {site}");
        }
    }

    [Test]
    public void BotWalksAroundTeleporterTilesButStillShootsOverThem()
    {
        // 타일 양옆과 위 줄이 걸을 수 있어, 위로 한 칸 돌아가면 타일을 밟지 않는 자리를 찾는다.
        MapLayout map = null;
        Vector2Int tile = default;
        for (int seed = 1; seed <= 20 && map == null; seed++)
        {
            MapLayout candidate = MapLayoutGenerator.Generate(new MapGenerationSettings { Seed = seed, TeleporterPairs = 3 });
            foreach (TeleporterPair pair in candidate.Teleporters)
                for (int side = 0; side < 2 && map == null; side++)
                {
                    Vector2Int corner = pair[side];
                    bool open = Walkable(candidate, corner.x - 1, corner.y) && Walkable(candidate, corner.x + TeleporterRegions.Size, corner.y);
                    for (int dx = -1; dx <= TeleporterRegions.Size; dx++)
                        open &= Walkable(candidate, corner.x + dx, corner.y - 1);
                    if (!open) continue;
                    map = candidate;
                    tile = corner;
                }
        }
        Assert.That(map, Is.Not.Null, "No generated tile has an open row on both sides.");
        var grid = new BotGrid(map, null);
        Assert.That(grid.IsTeleporter(tile.x, tile.y), Is.True);
        Assert.That(grid.IsTeleporter(tile.x - 1, tile.y), Is.False);

        // 타일 왼쪽 둘레 칸에서 오른쪽 둘레 칸까지 타일 한가운데 줄을 지난다.
        var from = new Vector2Int(tile.x - 1, tile.y);
        var to = new Vector2Int(tile.x + TeleporterRegions.Size, tile.y);
        Vector3 start = grid.CenterOf(from), end = grid.CenterOf(to);
        Assert.That(grid.HasClearLine(start, end, 0.3f), Is.True, "Shots and sight pass over the tile.");
        Assert.That(grid.HasClearPath(start, end, 0.3f), Is.False, "The bot does not walk straight across the tile.");

        var path = new List<Vector2Int>();
        Assert.That(grid.FindPath(from, to, path), Is.True);
        foreach (Vector2Int cell in path)
            Assert.That(grid.IsTeleporter(cell.x, cell.y), Is.False, $"Path steps on {cell}");
    }

    static bool Walkable(MapLayout map, int x, int y) =>
        x >= 0 && y >= 0 && x < map.Width && y < map.Height && (map[x, y] == MapCell.Floor || map[x, y] == MapCell.Bush);

    [Test]
    public void CameraChaseShrinksTheJumpToZeroWithinAFewChaseTimes()
    {
        var offset = new Vector3(-6f, 0f, 3f);
        var velocity = Vector3.zero;
        float previous = offset.magnitude;
        const float chaseTime = 0.35f, deltaTime = 1f / 60f;
        float elapsed = 0f;
        while (offset != Vector3.zero && elapsed < 3f)
        {
            CameraFollow.Chase(ref offset, ref velocity, chaseTime, deltaTime);
            Assert.That(offset.magnitude, Is.LessThanOrEqualTo(previous + 1e-5f));
            previous = offset.magnitude;
            elapsed += deltaTime;
        }
        Assert.That(offset, Is.EqualTo(Vector3.zero));
        Assert.That(elapsed, Is.InRange(chaseTime, chaseTime * 6f));

        offset = new Vector3(1f, 0f, 0f);
        CameraFollow.Chase(ref offset, ref velocity, 0f, deltaTime);
        Assert.That(offset, Is.EqualTo(Vector3.zero), "A chase time of zero jumps straight to the player.");
    }
}
#endif
