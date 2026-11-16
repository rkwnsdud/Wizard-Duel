using UnityEngine;

[DisallowMultipleComponent]
public abstract class Block : MonoBehaviour, IBlock
{
    public Transform Transform => transform;

    public IBlock Create(Transform parent, Vector3 position, Quaternion rotation)
    {
        Block block = Instantiate(this, parent, false);
        block.Initialize(position, rotation);
        return block;
    }

    public virtual void Initialize(Vector3 position, Quaternion rotation)
    {
        transform.localPosition += position;
        transform.localRotation = rotation;
    }
}
