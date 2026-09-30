using UnityEngine;

public class GridMap : MonoBehaviour
{
    [SerializeField] GameObject floorPrefab;
    [SerializeField] GameObject wallPrefab;

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
        for (int z = 0; z < layout.Length; z++)
        {
            for (int x = 0; x < layout[z].Length; x++)
            {
                char cell = layout[z][x];
                if (cell != '.' && cell != '#') continue;

                Vector3 position = new Vector3(
                    (x - (layout[z].Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - z) * cellSize);
                CreateBlock(floorPrefab, position);

                if (cell == '#')
                    CreateBlock(wallPrefab, position);
            }
        }
    }

    void CreateBlock(GameObject prefab, Vector3 position)
    {
        GameObject block = Instantiate(prefab, transform, false);
        block.transform.localPosition += position;
    }
}
