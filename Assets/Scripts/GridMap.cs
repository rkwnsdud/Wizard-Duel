using UnityEngine;

public class GridMap : MonoBehaviour
{
    [SerializeField] GameObject[] floorPrefabs;
    [SerializeField] GameObject[] wallPrefabs;
    [SerializeField] GameObject[] spawnPrefabs = new GameObject[0];
    [SerializeField] int seed = 7301;

    // . = 바닥, # = 바닥과 벽, B/O = 파랑/주황 스폰, 공백 = 빈 공간
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

    public Transform[] SpawnPoints { get; private set; }

    void Awake()
    {
        SpawnPoints = new Transform[spawnPrefabs.Length];
        var random = new System.Random(seed);
        for (int z = 0; z < layout.Length; z++)
        {
            for (int x = 0; x < layout[z].Length; x++)
            {
                char cell = layout[z][x];
                int spawnIndex = cell == 'B' ? 0 : cell == 'O' ? 1 : -1;
                if (cell != '.' && cell != '#' && spawnIndex < 0) continue;

                Vector3 position = new Vector3(
                    (x - (layout[z].Length - 1) * 0.5f) * cellSize, 0f,
                    ((layout.Length - 1) * 0.5f - z) * cellSize);
                GameObject floor = CreateBlock(floorPrefabs, position, random, spawnIndex);
                if (spawnIndex >= 0)
                    SpawnPoints[spawnIndex] = floor.transform.Find("Spawn Position");

                if (cell == '#')
                    CreateBlock(wallPrefabs, position, random);
            }
        }
    }

    GameObject CreateBlock(GameObject[] prefabs, Vector3 position, System.Random random, int spawnIndex = -1)
    {
        GameObject prefab = prefabs[random.Next(prefabs.Length)];
        Quaternion rotation = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
        if (spawnIndex >= 0)
        {
            prefab = spawnPrefabs[spawnIndex];
            rotation = Quaternion.identity;
        }

        GameObject block = Instantiate(prefab, transform, false);
        block.transform.localPosition += position;
        block.transform.localRotation = rotation;
        return block;
    }
}
