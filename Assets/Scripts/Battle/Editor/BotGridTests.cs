#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class BotGridTests
{
    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [Test]
    public void CellAtAndCenterOf_UseTheSameMappingAsTheMap()
    {
        Transform root = NewRoot(new Vector3(10f, 0f, -3f));
        BotGrid grid = Grid(root,
            "#######",
            "#.....#",
            "#.....#",
            "#.....#",
            "#######");

        // 7x5 맵의 칸 (x, y) 중심은 root 기준 (x - 3, 0, 2 - y)이다. BushFieldTests와 같은 식이다.
        Assert.That(grid.CenterOf(new Vector2Int(1, 1)), Is.EqualTo(root.TransformPoint(new Vector3(-2f, 0f, 1f))));
        Assert.That(grid.CenterOf(new Vector2Int(5, 3)), Is.EqualTo(root.TransformPoint(new Vector3(2f, 0f, -1f))));
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var cell = new Vector2Int(x, y);
                Assert.That(grid.CellAt(grid.CenterOf(cell)), Is.EqualTo(cell));
                // 칸 안쪽 어디에 서 있어도, 어느 높이에 있어도 같은 칸이다.
                Assert.That(grid.CellAt(grid.CenterOf(cell) + new Vector3(0.4f, 1.5f, -0.4f)), Is.EqualTo(cell));
            }
        Assert.That(grid.IsWalkable(grid.CellAt(root.position)), Is.True);
        Assert.That(grid.IsWalkable(new Vector2Int(0, 0)), Is.False, "Walls are not walkable.");
        Assert.That(grid.IsWalkable(new Vector2Int(-1, 2)), Is.False, "Outside the map is not walkable.");
        Assert.That(grid.IsWalkable(new Vector2Int(7, 2)), Is.False, "Outside the map is not walkable.");
    }

    [Test]
    public void FindPath_WalksAroundAWall()
    {
        BotGrid grid = Grid(null,
            ".....",
            ".###.",
            ".....");
        var from = new Vector2Int(2, 0);
        var to = new Vector2Int(2, 2);
        var path = new List<Vector2Int>();

        Assert.That(grid.FindPath(from, to, path), Is.True);
        AssertWalkablePath(grid, from, to, path);
        // 벽 끝을 돌아가는 가장 짧은 길은 여섯 걸음이다(옆으로 둘, 아래로 둘, 다시 옆으로 둘).
        Assert.That(path.Count, Is.EqualTo(6));
    }

    [Test]
    public void FindPath_DoesNotCutAcrossAWallCorner()
    {
        BotGrid grid = Grid(null,
            ".#",
            "..");
        var path = new List<Vector2Int>();

        Assert.That(grid.FindPath(new Vector2Int(0, 0), new Vector2Int(1, 1), path), Is.True);
        Assert.That(path, Is.EqualTo(new[] { new Vector2Int(0, 1), new Vector2Int(1, 1) }));
    }

    [Test]
    public void FindPath_WalksDiagonallyAcrossOpenFloor()
    {
        BotGrid grid = Grid(null,
            "....",
            "....",
            "....",
            "....");
        var path = new List<Vector2Int>();

        Assert.That(grid.FindPath(new Vector2Int(0, 0), new Vector2Int(3, 3), path), Is.True);
        AssertWalkablePath(grid, new Vector2Int(0, 0), new Vector2Int(3, 3), path);
        Assert.That(path.Count, Is.EqualTo(3));
    }

    [Test]
    public void FindPath_FailsWhenTheGoalCannotBeReached()
    {
        BotGrid grid = Grid(null,
            ".#.",
            ".#.");
        var path = new List<Vector2Int> { new Vector2Int(9, 9) };

        Assert.That(grid.FindPath(new Vector2Int(0, 0), new Vector2Int(2, 0), path), Is.False);
        Assert.That(path, Is.Empty);
        Assert.That(grid.FindPath(new Vector2Int(0, 0), new Vector2Int(1, 0), path), Is.False, "A wall is never a goal.");
        Assert.That(grid.FindPath(new Vector2Int(0, 0), new Vector2Int(0, 0), path), Is.True, "Already there.");
        Assert.That(path, Is.Empty);
        // 실패한 탐색이 남긴 상태가 다음 탐색에 섞이지 않는다.
        Assert.That(grid.FindPath(new Vector2Int(0, 0), new Vector2Int(0, 1), path), Is.True);
        Assert.That(path, Is.EqualTo(new[] { new Vector2Int(0, 1) }));
    }

    [Test]
    public void HasClearLine_IsBlockedByWallsAndByTheMapEdge()
    {
        BotGrid grid = Grid(null,
            "...",
            ".#.",
            "...");
        Vector3 topLeft = grid.CenterOf(new Vector2Int(0, 0));
        Vector3 topRight = grid.CenterOf(new Vector2Int(2, 0));
        Vector3 bottomRight = grid.CenterOf(new Vector2Int(2, 2));

        Assert.That(grid.HasClearLine(topLeft, topRight, 0.3f), Is.True);
        Assert.That(grid.HasClearLine(topLeft + Vector3.up * 2f, topRight, 0.3f), Is.True, "Height does not matter.");
        Assert.That(grid.HasClearLine(topLeft, bottomRight, 0.1f), Is.False, "The wall in the middle blocks the diagonal.");
        Assert.That(grid.HasClearLine(topLeft, topRight, 0.6f), Is.False, "A body wider than the corridor does not fit.");
        Assert.That(grid.HasClearLine(topLeft, topLeft + Vector3.left * 2f, 0.1f), Is.False, "Outside the map is blocked.");
    }

    [Test]
    public void TryNearestWalkable_FindsTheClosestFloorWithinRange()
    {
        BotGrid grid = Grid(null,
            "####.",
            "#####",
            ".####");

        Assert.That(grid.TryNearestWalkable(new Vector2Int(1, 1), 1, out Vector2Int near), Is.True);
        Assert.That(near, Is.EqualTo(new Vector2Int(0, 2)));
        Assert.That(grid.TryNearestWalkable(new Vector2Int(2, 1), 1, out _), Is.False);
        Assert.That(grid.TryNearestWalkable(new Vector2Int(4, 0), 0, out Vector2Int same), Is.True);
        Assert.That(same, Is.EqualTo(new Vector2Int(4, 0)));
    }

    [Test]
    public void GeneratedMaps_ConnectTheSpawnsAndMatchTheBushField([NUnit.Framework.Range(1, 12)] int seed)
    {
        MapLayout layout = MapLayoutGenerator.Generate(new MapGenerationSettings { Seed = seed, BushDensity = 0.1f });
        Assert.That(layout, Is.Not.Null);
        Transform root = NewRoot(new Vector3(-4f, 0f, 7f));
        var grid = new BotGrid(layout, root);
        var field = new BushField(layout.ToLayout(), root, 1f);

        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
            {
                var cell = new Vector2Int(x, y);
                bool floor = layout[x, y] == MapCell.Floor || layout[x, y] == MapCell.Bush;
                Assert.That(grid.IsWalkable(cell), Is.EqualTo(floor), $"cell {cell}");
                Assert.That(grid.BushAt(grid.CenterOf(cell)), Is.EqualTo(layout.BushId(x, y)), $"cell {cell}");
                // 봇이 보는 부시 번호가 플레이어의 부시 판정(BushField)과 같아야 숨고 찾는 규칙이 맞는다.
                Assert.That(field.BushAt(grid.CenterOf(cell)), Is.EqualTo(layout.BushId(x, y)), $"cell {cell}");
            }

        var path = new List<Vector2Int>();
        Assert.That(grid.FindPath(layout.SpawnA, layout.SpawnB, path), Is.True);
        AssertWalkablePath(grid, layout.SpawnA, layout.SpawnB, path);
        Assert.That(grid.FindPath(layout.SpawnB, layout.SpawnA, path), Is.True);
        AssertWalkablePath(grid, layout.SpawnB, layout.SpawnA, path);

        Vector3 start = grid.CenterOf(layout.SpawnA);
        for (int i = 0; i < 20; i++)
        {
            Vector3 point = grid.RandomSearchPoint(start);
            Assert.That(grid.IsWalkable(grid.CellAt(point)), Is.True);
            if (layout.BushCount > 0) Assert.That(grid.BushAt(point), Is.GreaterThanOrEqualTo(0), "Search bushes first.");
            Assert.That(grid.FindPath(layout.SpawnA, grid.CellAt(point), path), Is.True, "Every search point is reachable.");
        }
    }

    // 길의 모든 칸이 걸을 수 있고, 한 칸씩 이어지며, 벽 모서리를 대각선으로 가로지르지 않는지.
    static void AssertWalkablePath(BotGrid grid, Vector2Int from, Vector2Int to, List<Vector2Int> path)
    {
        Assert.That(path, Is.Not.Empty);
        Assert.That(path[path.Count - 1], Is.EqualTo(to));
        Vector2Int previous = from;
        foreach (Vector2Int cell in path)
        {
            Assert.That(grid.IsWalkable(cell), Is.True, $"cell {cell}");
            Vector2Int step = cell - previous;
            Assert.That(Mathf.Max(Mathf.Abs(step.x), Mathf.Abs(step.y)), Is.EqualTo(1), $"step {previous} -> {cell}");
            if (step.x != 0 && step.y != 0)
            {
                Assert.That(grid.IsWalkable(previous.x + step.x, previous.y), Is.True, $"corner {previous} -> {cell}");
                Assert.That(grid.IsWalkable(previous.x, previous.y + step.y), Is.True, $"corner {previous} -> {cell}");
            }
            previous = cell;
        }
    }

    // rows[y][x]가 '#'이 아니면 걸을 수 있는 칸이다.
    static BotGrid Grid(Transform root, params string[] rows)
    {
        var walkable = new bool[rows[0].Length, rows.Length];
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                walkable[x, y] = rows[y][x] != '#';
        return new BotGrid(walkable, root);
    }

    Transform NewRoot(Vector3 position)
    {
        var item = new GameObject("Map");
        created.Add(item);
        item.transform.position = position;
        return item.transform;
    }
}
#endif
