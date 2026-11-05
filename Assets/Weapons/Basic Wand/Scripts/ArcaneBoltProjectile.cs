using UnityEngine;

public class ArcaneBoltProjectile : MonoBehaviour 
{
    [SerializeField] private float speed = 18f;
    void Update()
    {
        transform.position = transform.position + transform.forward * speed * Time.deltaTime;
        
    }
}
