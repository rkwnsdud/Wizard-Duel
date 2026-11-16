using UnityEngine;

public interface ISpawnPointProvider
{
    Transform[] SpawnPoints { get; }
}
