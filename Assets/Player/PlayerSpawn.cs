using UnityEngine;

public class PlayerSpawn : MonoBehaviour
{
    [SerializeField] GridMap map;

    void Start()
    {
        Transform spawnPoint = map.SpawnPoints[0];
        CharacterController controller = GetComponent<CharacterController>();
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        controller.enabled = true;
    }
}
