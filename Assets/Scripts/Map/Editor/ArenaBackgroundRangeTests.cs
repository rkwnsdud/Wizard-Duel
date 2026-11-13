#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

// 로비 화면 미리보기 배경(Preview Background)과 전투 배경(Battle Background)은 깔 범위만 다르다.
// 로비 화면에서 전투로 들어가면 같은 자리에 전투 맵을 다시 만들어 바꿔 끼우므로, 겹치는 칸은 범위와 관계없이 같은 조각이어야 한다.
// 플레이 모드 없이 MapRendererCombineTests.NewBattleMap으로 같은 프리팹을 쓰는 맵을 범위만 바꿔 만들어 비교한다.
public class ArenaBackgroundRangeTests
{
    // Main 씬 Title Map의 Preview Background 값. 전투 쪽 값(7/10)은 NewBattleMap의 기본값이다.
    const int PreviewRimOffsetCells = 5;
    const int PreviewDecoOffsetCells = 7;
    const float PositionTolerance = 0.001f;
    const float AngleTolerance = 0.01f;

    // 가벼운 테스트 묶음에 들어가므로 만들 수 있는 가장 작은 길쭉한 맵을 쓴다. 대칭은 기본값(Rotate180)이다.
    static readonly MapGenerationSettings Settings = new MapGenerationSettings
    {
        Seed = 7301, Size = new Vector2Int(25, 13), Shape = MapShape.Elongated
    };
    // 대칭이 없으면 미러 칸이 깔 범위 밖에 있는 장식 칸이 생긴다.
    static readonly MapGenerationSettings AsymmetricSettings = new MapGenerationSettings
    {
        Seed = 7301, Size = new Vector2Int(25, 13), Shape = MapShape.Elongated, Symmetry = MapSymmetry.None
    };

    readonly List<ProceduralMap> maps = new List<ProceduralMap>();

    // 배경 루트 바로 아래 조각 하나(Apron_x_y, Deco_x_y).
    struct Piece
    {
        public Mesh mesh;
        public Vector3 position;
        public Quaternion rotation;
    }

    [TearDown]
    public void TearDown()
    {
        // MapRendererCombineTests.Remove와 같은 순서.
        foreach (ProceduralMap map in maps)
        {
            if (map == null) continue;
            map.Clear();
            map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true).Rebuild();
            Object.DestroyImmediate(map.gameObject);
        }
        maps.Clear();
    }

    [Test]
    public void SmallerRange_IsASubsetOfTheLargerRange()
    {
        AssertSubset(Settings);
    }

    // 미러 칸이 범위 안에 있는지에 따라 쌍으로 놓을지 혼자 놓을지 정하면 대칭이 없는 맵에서 범위마다 결과가 달라진다.
    [Test]
    public void SmallerRange_IsASubset_WhenTheMapHasNoSymmetry()
    {
        ProceduralMap preview = AssertSubset(AsymmetricSettings);

        // 이 맵에 미러 칸이 미리보기 장식 범위 밖인 장식 후보 칸이 없으면 이 테스트가 잡으려는 경우가 생기지 않는다.
        MapLayout layout = preview.GeneratedLayout;
        int lonely = 0;
        for (int y = -PreviewDecoOffsetCells; y < layout.Height + PreviewDecoOffsetCells; y++)
            for (int x = -PreviewDecoOffsetCells; x < layout.Width + PreviewDecoOffsetCells; x++)
                if (IsBackgroundCell(layout, x, y, PreviewDecoOffsetCells)
                    && !IsBackgroundCell(layout, layout.Width - 1 - x, layout.Height - 1 - y, PreviewDecoOffsetCells))
                    lonely++;
        Assert.That(lonely, Is.GreaterThan(0), "The map must have decoration cells whose mirror is out of the preview range.");
    }

    // 미러쌍은 장식 유무·종류를 한 번만 굴리고 반대쪽은 반대 방향으로 둔다. 양쪽이 다르면 공정성이 깨진다.
    [Test]
    public void Decorations_MirrorAcrossTheMapCenter()
    {
        ProceduralMap map = BattleMap("Mirrored Background", Settings);
        MapLayout layout = map.GeneratedLayout;
        Dictionary<string, Piece> pieces = Pieces(map);
        Quaternion halfTurn = Quaternion.Euler(0f, 180f, 0f);

        int decorations = 0;
        foreach (KeyValuePair<string, Piece> pair in pieces)
        {
            if (!pair.Key.StartsWith("Deco_")) continue;
            Vector2Int cell = Cell(pair.Key);
            string mirror = "Deco_" + (layout.Width - 1 - cell.x) + "_" + (layout.Height - 1 - cell.y);
            // 가운데 칸은 자기 자신이 미러라 돌리지 않는다.
            if (mirror == pair.Key) continue;
            decorations++;
            Assert.That(pieces.TryGetValue(mirror, out Piece other), Is.True, pair.Key + " has no mirrored decoration.");
            Assert.That(other.mesh, Is.EqualTo(pair.Value.mesh), pair.Key);
            Assert.That(Quaternion.Angle(halfTurn * pair.Value.rotation, other.rotation), Is.LessThan(AngleTolerance), pair.Key);
            Vector3 p = pair.Value.position;
            Assert.That(Vector3.Distance(new Vector3(-p.x, p.y, -p.z), other.position), Is.LessThan(PositionTolerance), pair.Key);
        }
        Assert.That(decorations, Is.GreaterThan(0), "The background must have decorations to compare.");
    }

    // 배경 seed는 레이아웃에서만 나오므로 같은 설정이면 클라이언트마다 같은 배경을 깔아야 한다.
    [Test]
    public void SameSettings_BuildTheSameBackgroundTwice()
    {
        Dictionary<string, Piece> first = Pieces(BattleMap("Background (first)", Settings));
        Dictionary<string, Piece> second = Pieces(BattleMap("Background (second)", Settings));

        Assert.That(first.Count, Is.GreaterThan(0));
        CollectionAssert.AreEquivalent(first.Keys, second.Keys);
        foreach (KeyValuePair<string, Piece> pair in first) AssertSamePiece(pair.Key, pair.Value, second[pair.Key]);
    }

    // 미리보기 범위로 깐 조각이 모두 전투 범위로 깐 배경에 같은 이름·메시·위치·회전으로 있는지 본다. 미리보기 맵을 돌려준다.
    ProceduralMap AssertSubset(MapGenerationSettings settings)
    {
        ProceduralMap previewMap = PreviewMap("Preview Range Background", settings);
        Dictionary<string, Piece> preview = Pieces(previewMap);
        Dictionary<string, Piece> battle = Pieces(BattleMap("Battle Range Background", settings));

        Assert.That(preview.Keys, Has.Some.StartsWith("Apron_"), "The preview background must have floor tiles to compare.");
        Assert.That(preview.Keys, Has.Some.StartsWith("Deco_"), "The preview background must have decorations to compare.");
        Assert.That(battle.Count, Is.GreaterThan(preview.Count), "The battle range lays more cells.");
        foreach (KeyValuePair<string, Piece> pair in preview)
        {
            Assert.That(battle.TryGetValue(pair.Key, out Piece other), Is.True, pair.Key + " is missing from the battle background.");
            AssertSamePiece(pair.Key, pair.Value, other);
        }
        return previewMap;
    }

    static void AssertSamePiece(string name, Piece expected, Piece actual)
    {
        Assert.That(actual.mesh, Is.EqualTo(expected.mesh), name);
        Assert.That(Vector3.Distance(actual.position, expected.position), Is.LessThan(PositionTolerance), name);
        Assert.That(Quaternion.Angle(actual.rotation, expected.rotation), Is.LessThan(AngleTolerance), name);
    }

    ProceduralMap PreviewMap(string name, MapGenerationSettings settings) =>
        Generate(MapRendererCombineTests.NewBattleMap(name, PreviewRimOffsetCells, PreviewDecoOffsetCells), settings);

    ProceduralMap BattleMap(string name, MapGenerationSettings settings) =>
        Generate(MapRendererCombineTests.NewBattleMap(name), settings);

    // 조각을 하나씩 비교해야 하므로 렌더러를 합치지 않고 만든다. 합친 배경이 합치지 않은 배경과 같은 것은 MapRendererCombineTests가 본다.
    ProceduralMap Generate(ProceduralMap map, MapGenerationSettings settings)
    {
        maps.Add(map);
        map.Generate(settings);
        return map;
    }

    // 먼 바닥(FarGround)을 뺀 배경 조각을 이름으로 담는다. 이름이 겹치면 한 칸에 같은 종류를 두 번 깐 것이라 Add에서 실패한다.
    static Dictionary<string, Piece> Pieces(ProceduralMap map)
    {
        Transform background = map.GetComponentInChildren<ArenaBackgroundBuilderNew>(true).transform;
        var pieces = new Dictionary<string, Piece>();
        foreach (Transform child in background)
        {
            if (child.name.StartsWith("FarGround")) continue;
            MeshFilter filter = child.GetComponentInChildren<MeshFilter>(true);
            pieces.Add(child.name, new Piece
            {
                mesh = filter != null ? filter.sharedMesh : null,
                position = child.position,
                rotation = child.rotation
            });
        }
        return pieces;
    }

    // "Deco_x_y"의 칸 좌표. 맵 바깥 칸은 음수다.
    static Vector2Int Cell(string name)
    {
        string[] words = name.Split('_');
        return new Vector2Int(int.Parse(words[1]), int.Parse(words[2]));
    }

    // 배경 빌더가 배경을 까는 칸: 맵 안의 플레이 칸이 아니고, range 칸(체비쇼프 거리) 안에 플레이 칸이 있다.
    static bool IsBackgroundCell(MapLayout layout, int cx, int cy, int range)
    {
        if (cx >= 0 && cy >= 0 && cx < layout.Width && cy < layout.Height && layout[cx, cy] != MapCell.Void) return false;
        for (int y = Mathf.Max(0, cy - range); y <= Mathf.Min(layout.Height - 1, cy + range); y++)
            for (int x = Mathf.Max(0, cx - range); x <= Mathf.Min(layout.Width - 1, cx + range); x++)
                if (layout[x, y] != MapCell.Void) return true;
        return false;
    }
}
#endif
