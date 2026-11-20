#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class BushFieldTests
{
    static readonly string[] Layout =
    {
        "#######",
        "#**...#",
        "#**..*#",
        "#....*#",
        "#######"
    };

    readonly List<Object> created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [Test]
    public void BushAt_FindsTheClusterUnderAWorldPosition()
    {
        Transform root = NewObject("Map").transform;
        root.position = new Vector3(10f, 0f, -3f);
        var field = new BushField(Layout, root, 1f);

        Assert.That(field.Count, Is.EqualTo(2));
        // 7x5 레이아웃의 칸 (x, z) 중심은 root 기준 (x - 3, 0, 2 - z)이다.
        Assert.That(field.BushAt(Cell(root, 1, 1)), Is.EqualTo(0));
        Assert.That(field.BushAt(Cell(root, 2, 2)), Is.EqualTo(0));
        Assert.That(field.BushAt(Cell(root, 5, 2)), Is.EqualTo(1));
        Assert.That(field.BushAt(Cell(root, 5, 3)), Is.EqualTo(1));
        Assert.That(field.BushAt(Cell(root, 3, 1)), Is.EqualTo(-1), "Floor cells are not bushes.");
        Assert.That(field.BushAt(Cell(root, 0, 0)), Is.EqualTo(-1), "Walls are not bushes.");
        Assert.That(field.BushAt(Cell(root, 9, 2)), Is.EqualTo(-1), "Outside the map is not a bush.");
        // 칸 안쪽 어디에 서 있어도 같은 칸으로 본다.
        Assert.That(field.BushAt(Cell(root, 1, 1) + new Vector3(0.4f, 1.5f, -0.4f)), Is.EqualTo(0));
    }

    [Test]
    public void BushAt_ReturnsNoBushOnceTheMapIsDestroyed()
    {
        GameObject map = NewObject("Map");
        var field = new BushField(Layout, map.transform, 1f);
        Vector3 bush = Cell(map.transform, 1, 1);
        Object.DestroyImmediate(map);

        Assert.That(field.BushAt(bush), Is.EqualTo(-1));
    }

    [Test]
    public void RemoveCell_RemovesConcealmentAndRelabelsSplitBushGroups()
    {
        Transform root = NewObject("Map").transform;
        var field = new BushField(new[] { "#####", "#***#", "#####" }, root, 1f);
        BushBlock left = NewObject("Bush left").AddComponent<BushBlock>();
        BushBlock center = NewObject("Bush center").AddComponent<BushBlock>();
        BushBlock right = NewObject("Bush right").AddComponent<BushBlock>();
        field.Add(1, 1, left);
        field.Add(2, 1, center);
        field.Add(3, 1, right);

        Assert.That(field.RemoveCell(2, 1), Is.True);

        Assert.That(field.BushAt(Cell(root, 2, 1)), Is.EqualTo(-1));
        Assert.That(field.Count, Is.EqualTo(2));
        int leftId = field.BushAt(Cell(root, 1, 1));
        int rightId = field.BushAt(Cell(root, 3, 1));
        Assert.That(leftId, Is.GreaterThanOrEqualTo(0));
        Assert.That(rightId, Is.GreaterThanOrEqualTo(0).And.Not.EqualTo(leftId));
        Assert.That(field.BlocksOf(leftId), Is.EqualTo(new[] { left }));
        Assert.That(field.BlocksOf(rightId), Is.EqualTo(new[] { right }));
    }

    [Test]
    public void Add_GroupsOnlyBushBlocksByCluster()
    {
        Transform root = NewObject("Map").transform;
        var field = new BushField(Layout, root, 1f);
        BushBlock first = NewObject("Bush A").AddComponent<BushBlock>();
        BushBlock second = NewObject("Bush B").AddComponent<BushBlock>();
        BushBlock other = NewObject("Bush C").AddComponent<BushBlock>();
        FloorBlock floor = NewObject("Floor").AddComponent<FloorBlock>();

        field.Add(1, 1, first);
        field.Add(2, 2, floor);
        field.Add(2, 2, second);
        field.Add(5, 3, other);
        field.Add(3, 1, NewObject("Stray").AddComponent<BushBlock>());

        Assert.That(field.BlocksOf(0), Is.EqualTo(new[] { first, second }));
        Assert.That(field.BlocksOf(1), Is.EqualTo(new[] { other }));
    }

    [Test]
    public void SetFaded_SwapsEveryMaterialSlotAndRestoresIt()
    {
        var opaque = NewMaterial("Opaque");
        var leaves = NewMaterial("Leaves");
        var faded = NewMaterial("Faded");
        BushBlock block = NewObject("Bush").AddComponent<BushBlock>();
        var renderer = new GameObject("Mesh").AddComponent<MeshRenderer>();
        renderer.transform.SetParent(block.transform);
        renderer.sharedMaterials = new[] { opaque, leaves };
        SetFadedMaterial(block, faded);
        var field = new BushField(Layout, NewObject("Map").transform, 1f);
        field.Add(1, 1, block);

        field.SetFaded(0, true);
        Assert.That(block.Faded, Is.True);
        Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { faded, faded }));

        field.SetFaded(0, false);
        Assert.That(block.Faded, Is.False);
        Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { opaque, leaves }));
    }

    [Test]
    public void Deactivate_LeavesAnotherRegisteredMapAlone()
    {
        Transform root = NewObject("Map").transform;
        var battle = new BushField(Layout, root, 1f);
        var stale = new BushField(Layout, root, 1f);
        BushField.Activate(battle);
        try
        {
            BushField.Deactivate(stale);
            BushField.Deactivate(null);
            Assert.That(BushField.Active, Is.SameAs(battle));

            BushField.Deactivate(battle);
            Assert.That(BushField.Active, Is.Null);
        }
        finally
        {
            BushField.Deactivate(battle);
        }
    }

    [Test]
    public void CenterOf_PicksTheBushCellNearestTheClusterMiddle()
    {
        Transform root = NewObject("Map").transform;
        root.position = new Vector3(10f, 0f, -3f);
        var field = new BushField(Layout, root, 1f);

        // 무게중심에서 같은 거리인 칸이 여럿이면 행 우선으로 먼저 만난 칸을 고른다.
        Assert.That(field.CenterOf(0), Is.EqualTo(Cell(root, 1, 1)));
        Assert.That(field.CenterOf(1), Is.EqualTo(Cell(root, 5, 2)));
        Assert.That(field.BushAt(field.CenterOf(0)), Is.EqualTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.CenterOf(5));
    }

    [Test]
    public void BlockFactory_LaysFloorAndBushOnBushCells()
    {
        BlockFactory factory = NewFactory(withBushes: true);
        Transform parent = NewObject("Parent").transform;

        List<IBlock> blocks = factory.CreateCell('*', new Vector3(2f, 0f, 1f), parent).ToList();
        created.AddRange(blocks.Select(block => (Object)block.Transform.gameObject));

        Assert.That(blocks, Has.Count.EqualTo(2));
        Assert.That(blocks[0], Is.InstanceOf<FloorBlock>());
        Assert.That(blocks[1], Is.InstanceOf<BushBlock>());
        Assert.That(blocks[1].Transform.localPosition, Is.EqualTo(new Vector3(2f, 0f, 1f)));
    }

    [Test]
    public void BlockFactory_NeedsBushPrefabsOnlyWhenTheLayoutHasBushes()
    {
        BlockFactory factory = NewFactory(withBushes: false);
        Transform parent = NewObject("Parent").transform;

        List<IBlock> floor = factory.CreateCell('.', Vector3.zero, parent).ToList();
        created.AddRange(floor.Select(block => (Object)block.Transform.gameObject));
        Assert.That(floor, Has.Count.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => factory.CreateCell('*', Vector3.zero, parent).ToList());
    }

    [Test]
    public void ProceduralMap_RegeneratingOrClearingUnregistersTheOldBushes()
    {
        BlockFactory factory = NewFactory(withBushes: true);
        ProceduralMap map = factory.gameObject.AddComponent<ProceduralMap>();
        // 기본 밀도 맵은 부시 덩어리가 생긴다(1단계 분석에서 부시 없는 맵 0%).
        map.Generate(new MapGenerationSettings { Seed = 11 });
        BushField first = map.Bushes;
        Assert.That(first, Is.Not.Null);
        Assert.That(first.Count, Is.GreaterThan(0));
        BushField.Activate(first);

        try
        {
            map.Generate(new MapGenerationSettings { Seed = 12 });
            Assert.That(BushField.Active, Is.Null, "Regenerating unregisters the previous map's bushes.");
            Assert.That(map.Bushes, Is.Not.Null.And.Not.SameAs(first));

            BushField.Activate(map.Bushes);
            map.Clear();
            Assert.That(BushField.Active, Is.Null, "Clearing the map unregisters its bushes.");
            Assert.That(map.Bushes, Is.Null);
            Assert.That(map.Blocks, Is.Empty);
        }
        finally
        {
            BushField.Deactivate(BushField.Active);
        }
    }

    GameObject NewObject(string name)
    {
        var gameObject = new GameObject(name);
        created.Add(gameObject);
        return gameObject;
    }

    Material NewMaterial(string name)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        created.Add(material);
        return material;
    }

    static void SetFadedMaterial(BushBlock block, Material faded)
    {
        var serialized = new SerializedObject(block);
        serialized.FindProperty("fadedMaterial").objectReferenceValue = faded;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    BlockFactory NewFactory(bool withBushes)
    {
        BlockFactory factory = NewObject("Factory").AddComponent<BlockFactory>();
        var serialized = new SerializedObject(factory);
        Fill(serialized.FindProperty("floorBlocks"), NewObject("Floor Template").AddComponent<FloorBlock>());
        Fill(serialized.FindProperty("wallBlocks"), NewObject("Wall Template").AddComponent<WallBlock>());
        var spawns = new Object[2];
        for (int i = 0; i < spawns.Length; i++)
        {
            GameObject spawn = NewObject($"Spawn Template {i}");
            new GameObject("Spawn Position").transform.SetParent(spawn.transform);
            spawns[i] = spawn.AddComponent<FloorBlock>();
        }
        Fill(serialized.FindProperty("spawnBlocks"), spawns);
        Fill(serialized.FindProperty("bushBlocks"),
            withBushes ? new Object[] { NewObject("Bush Template").AddComponent<BushBlock>() } : new Object[0]);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        factory.Initialize(1);
        return factory;
    }

    static void Fill(SerializedProperty array, params Object[] items)
    {
        array.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    static Vector3 Cell(Transform root, int x, int z) =>
        root.TransformPoint(new Vector3(x - (Layout[z].Length - 1) * 0.5f, 0f, (Layout.Length - 1) * 0.5f - z));
}
#endif
