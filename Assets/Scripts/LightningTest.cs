using UnityEngine;
using UnityEngine.InputSystem;

public class LightningTest : MonoBehaviour
{
    public GameObject lightningPrefab;
    public float distance = 10f;

    void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.qKey.wasPressedThisFrame)
        {
            Vector3 start =
                transform.position + transform.forward * 5f
                - transform.right * 5f;

            // 화면 왼쪽 → 오른쪽으로 길게 뻗음
            Vector3 end = start + transform.right * distance;

            GameObject obj = Instantiate(
                lightningPrefab,
                Vector3.zero,
                Quaternion.identity
            );

            obj.GetComponent<LightningEffect>()
               .SetEndpoints(start, end);
        }
    }
}