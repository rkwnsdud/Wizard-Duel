using UnityEngine;

public class GridMap : MonoBehaviour
{
    // . = 바닥, # = 바닥과 벽, 공백 = 빈 공간
    [SerializeField] string[] layout =
    {
        "###########",
        "#.........#",
        "#..##.....#",
        "#.........#",
        "#.........#",
        "#.........#",
        "#.....##..#",
        "#.........#",
        "###########"
    };

    const float cellSize = 2f;

    void Awake()
    {
        var colors = new MaterialPropertyBlock();
        for (int z = 0; z < layout.Length; z++)
        {
            for (int x = 0; x < layout[z].Length; x++)
            {
                char cell = layout[z][x];
                if (cell != '.' && cell != '#') continue;

                Vector3 position = new Vector3(
                    (x - (layout[z].Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - z) * cellSize);
                Color floorColor = (x + z) % 2 == 0
                    ? new Color(0.45f, 0.5f, 0.45f) : new Color(0.55f, 0.6f, 0.55f);
                CreateBlock("Floor", position + Vector3.down * 0.1f,
                    new Vector3(cellSize, 0.2f, cellSize), floorColor, colors);

                if (cell == '#')
                    CreateBlock("Wall", position + Vector3.up * cellSize * 0.5f,
                        Vector3.one * cellSize, new Color(0.3f, 0.35f, 0.4f), colors);
            }
        }
    }

    void CreateBlock(string blockName, Vector3 position, Vector3 scale,
        Color color, MaterialPropertyBlock colors)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = blockName;
        block.transform.SetParent(transform, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;
        colors.SetColor("_BaseColor", color);
        block.GetComponent<Renderer>().SetPropertyBlock(colors);
    }
}
