using UnityEngine;

public class LightningProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 10f;

    void Update()
    {
        Vector3 direction = transform.forward;
        transform.position += direction * speed * Time.deltaTime;
    }


}
