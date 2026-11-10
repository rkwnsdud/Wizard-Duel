#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 갈대 부시(ReedPatch)가 이웃 칸에 맞춰 이어지는 모양을 만들고, 프리팹과 반투명 머티리얼이 제대로 걸려 있는지 검사한다.
public class ReedPatchTests
{
    // 덩어리 0은 왼쪽 위 2×2, 덩어리 1은 오른쪽 세로 2칸이다.
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
    public void NeighborsAt_ListsTheBushCellsAround()
    {
        var field = new BushField(Layout, NewObject("Map").transform, 1f);

        Assert.That(field.NeighborsAt(1, 1),
            Is.EqualTo(BushNeighbors.East | BushNeighbors.South | BushNeighbors.SouthEast));
        Assert.That(field.NeighborsAt(2, 2),
            Is.EqualTo(BushNeighbors.North | BushNeighbors.West | BushNeighbors.NorthWest));
        Assert.That(field.NeighborsAt(5, 2), Is.EqualTo(BushNeighbors.South));
        Assert.That(field.NeighborsAt(3, 1), Is.EqualTo(BushNeighbors.West | BushNeighbors.SouthWest),
            "A floor cell also reports the bush cells around it.");
    }

    [Test]
    public void AddingABushBlockShapesItsReedsToTheNeighbors()
    {
        var field = new BushField(Layout, NewObject("Map").transform, 1f);
        ReedPatch corner = NewPatch();
        ReedPatch lone = NewPatch();

        field.Add(1, 1, corner.GetComponent<BushBlock>());
        field.Add(5, 2, lone.GetComponent<BushBlock>());

        Assert.That(corner.Neighbors, Is.EqualTo(field.NeighborsAt(1, 1)));
        Assert.That(lone.Neighbors, Is.EqualTo(BushNeighbors.South));
        Assert.That(Mesh(corner).vertexCount, Is.GreaterThan(0));
        Assert.That(field.BlocksOf(field.IdAt(5, 2)), Has.Member(lone.GetComponent<BushBlock>()));
        Assert.That(field.SeedAt(1, 1), Is.Not.EqualTo(field.SeedAt(5, 2)));
        Assert.That(new BushField(Layout, NewObject("Other").transform, 1f).SeedAt(1, 1), Is.EqualTo(field.SeedAt(1, 1)),
            "Every client that lays the same layout sees the same reeds.");
    }

    [Test]
    public void SameSeedAndNeighborsBuildTheSameReeds()
    {
        ReedPatch a = NewPatch();
        ReedPatch b = NewPatch();

        a.Build(42, BushNeighbors.North | BushNeighbors.East);
        b.Build(42, BushNeighbors.North | BushNeighbors.East);
        Assert.That(Mesh(a).vertices, Is.EqualTo(Mesh(b).vertices));

        b.Build(43, BushNeighbors.North | BushNeighbors.East);
        Assert.That(Mesh(b).vertices, Is.Not.EqualTo(Mesh(a).vertices));
    }

    [Test]
    public void RemovingTheBlockReleasesItsBuiltMesh()
    {
        ReedPatch patch = NewPatch();
        patch.Build(3, BushNeighbors.All);
        Mesh built = Mesh(patch);
        patch.Build(4, BushNeighbors.None);
        Assert.That(Mesh(patch), Is.SameAs(built), "Rebuilding reuses the same mesh.");

        Object.DestroyImmediate(patch.gameObject);
        Assert.That(built == null, Is.True, "Maps cleared in edit mode must not leave reed meshes behind.");
    }

    [Test]
    public void ReedsAreShorterTowardOpenEdges()
    {
        ReedPatch inside = NewPatch();
        ReedPatch alone = NewPatch();
        inside.Build(7, BushNeighbors.All);
        alone.Build(7, BushNeighbors.None);

        float insideHeight = Mesh(inside).vertices.Average(v => v.y);
        float aloneHeight = Mesh(alone).vertices.Average(v => v.y);
        Assert.That(aloneHeight, Is.LessThan(insideHeight * 0.9f));
        Assert.That(Mesh(inside).bounds.max.y, Is.InRange(1.1f, 1.8f), "Reeds stand a little below the wizard's 2.2 height.");
    }

    [Test]
    public void ReedsFollowTheMapNotTheRandomBlockRotation()
    {
        ReedPatch straight = NewPatch();
        ReedPatch turned = NewPatch();
        turned.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        BushNeighbors neighbors = BushNeighbors.North | BushNeighbors.West | BushNeighbors.NorthWest;

        straight.Build(5, neighbors);
        turned.Build(5, neighbors);

        Vector3[] a = Mesh(straight).vertices.Select(straight.transform.TransformPoint).ToArray();
        Vector3[] b = Mesh(turned).vertices.Select(turned.transform.TransformPoint).ToArray();
        for (int i = 0; i < a.Length; i++)
            Assert.That(Vector3.Distance(a[i], b[i]), Is.LessThan(1e-4f), $"vertex {i}");
    }

    [Test]
    public void EdgeDistance_MeasuresToTheOpenSideOrCorner()
    {
        Assert.That(ReedPatch.EdgeDistance(Vector2.zero, BushNeighbors.All, out _), Is.GreaterThan(1f));

        float north = ReedPatch.EdgeDistance(new Vector2(0f, 0.3f), BushNeighbors.All & ~BushNeighbors.North, out Vector2 up);
        Assert.That(north, Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(up, Is.EqualTo(Vector2.up));

        // 북쪽과 동쪽은 부시인데 북동쪽 칸만 비면, 그 꼭짓점까지 잰다.
        float corner = ReedPatch.EdgeDistance(new Vector2(0.2f, 0.2f), BushNeighbors.All & ~BushNeighbors.NorthEast, out Vector2 toward);
        Assert.That(corner, Is.EqualTo(new Vector2(0.3f, 0.3f).magnitude).Within(1e-5f));
        Assert.That(toward.x, Is.GreaterThan(0f));
        Assert.That(toward.y, Is.GreaterThan(0f));
    }

    [Test]
    public void ReedPrefabFadesToTheBushAlphaWithoutColliders()
    {
        var faded = AssetDatabase.LoadAssetAtPath<Material>(ReedAssetsBuilder.FadedMaterialPath);
        Assert.That(faded.shader.name, Is.EqualTo(ReedAssetsBuilder.FadedShaderName));
        Assert.That(faded.GetColor("_BaseColor").a, Is.EqualTo(BushVisibilityRules.BushAlpha).Within(1e-4f));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReedAssetsBuilder.PrefabPath);
        var block = prefab.GetComponent<BushBlock>();
        Assert.That(new SerializedObject(block).FindProperty("fadedMaterial").objectReferenceValue, Is.SameAs(faded));
        Assert.That(prefab.GetComponent<ReedPatch>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null, "The prefab keeps a preview mesh.");
        Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, "Reeds must not block movement or projectiles.");
    }

    ReedPatch NewPatch()
    {
        GameObject gameObject = NewObject("Reeds");
        gameObject.AddComponent<MeshRenderer>();
        gameObject.AddComponent<BushBlock>();
        return gameObject.AddComponent<ReedPatch>();
    }

    GameObject NewObject(string name)
    {
        var gameObject = new GameObject(name);
        created.Add(gameObject);
        return gameObject;
    }

    static Mesh Mesh(ReedPatch patch) => patch.GetComponent<MeshFilter>().sharedMesh;
}
#endif
