using UnityEngine;

public class PlayerSpawn : MonoBehaviour
{
    [Tooltip("GridMap 또는 ProceduralMap 컴포넌트를 지정합니다.")]
    [SerializeField] MonoBehaviour map;

    void Start()
    {
        Transform spawnPoint = map.GetComponent<BlockFactory>().SpawnPoints[0];
        CharacterController controller = GetComponent<CharacterController>();
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        controller.enabled = true;
    }
}
