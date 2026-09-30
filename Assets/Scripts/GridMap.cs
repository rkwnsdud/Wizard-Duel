using UnityEngine;

public class GridMap : MonoBehaviour
{
    [SerializeField] GameObject[] floorPrefabs;
    [SerializeField] GameObject[] wallPrefabs;
    [SerializeField] int seed = 7301;

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
        var random = new System.Random(seed);
        for (int z = 0; z < layout.Length; z++)
        {
            for (int x = 0; x < layout[z].Length; x++)
            {
                char cell = layout[z][x];
                if (cell != '.' && cell != '#') continue;

                Vector3 position = new Vector3(
                    (x - (layout[z].Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - z) * cellSize);
                CreateBlock(floorPrefabs, position, random);

                if (cell == '#')
                    CreateBlock(wallPrefabs, position, random);
            }
        }
    }

    void CreateBlock(GameObject[] prefabs, Vector3 position, System.Random random)
    {
        GameObject block = Instantiate(prefabs[random.Next(prefabs.Length)], transform, false);
        block.transform.localPosition += position;
        block.transform.localRotation = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
    }
}
