using UnityEngine;

public interface IBlock
{
    Transform Transform { get; }

    void Initialize(Vector3 position, Quaternion rotation);
}
