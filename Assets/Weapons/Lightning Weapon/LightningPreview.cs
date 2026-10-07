using UnityEngine;

[DisallowMultipleComponent]
public class LightningPreview : MonoBehaviour
{
    [SerializeField] private LineRenderer lineRenderer;
    private Material ownedMaterial;


    private void Awake()
    {
        if (lineRenderer == null)
        {
            Debug.LogError("LightningPreview requires a PreviewVisual LineRenderer reference.", this);
            return;
        }
        lineRenderer.enabled = false;
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.positionCount = 4;
        lineRenderer.widthMultiplier = 1f;
        lineRenderer.startWidth = lineRenderer.endWidth = 0.04f;
        lineRenderer.startColor = lineRenderer.endColor = Color.cyan;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        if (lineRenderer.sharedMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("LightningChargePreview");
            if (shader != null)
            {
                ownedMaterial = new Material(shader);
                lineRenderer.sharedMaterial = ownedMaterial;
            }
            else Debug.LogError("Lightning charge preview shader is missing.", this);
        }
    }
    public void ShowPreview(Vector3 start, Vector3 end, Vector3 direction, float hitWidth)
    {
        if (lineRenderer == null) return;
        // Horizontal footprint of the existing hit box, at the bolt's height.
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * (hitWidth * 0.5f);
        lineRenderer.SetPosition(0, start - side);
        lineRenderer.SetPosition(1, start + side);
        lineRenderer.SetPosition(2, end + side);
        lineRenderer.SetPosition(3, end - side);
        lineRenderer.enabled = true;
    }

    public void HidePreview()
    {
        if (lineRenderer != null) lineRenderer.enabled = false;
    }

    private void OnDisable() { HidePreview(); }

    private void OnDestroy()
    {
        if (ownedMaterial != null) Destroy(ownedMaterial);
    }

}
