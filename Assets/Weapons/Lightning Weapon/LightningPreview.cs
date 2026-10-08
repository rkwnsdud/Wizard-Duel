using UnityEngine;

public class LightningPreview : MonoBehaviour
{
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField]
    private Color previewColor = new Color(0.65f, 0.91f, 0.96f, 1f);
    private Material ownedMaterial;


    private void Awake()
    {
        bool createdRenderer = lineRenderer == null;
        if (lineRenderer == null)
        {
            GameObject visual = new GameObject("Lightning Charge Preview");
            visual.transform.SetParent(transform, false);
            lineRenderer = visual.AddComponent<LineRenderer>();
        }
        lineRenderer.enabled = false;
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;
        lineRenderer.positionCount = 4;
        lineRenderer.widthMultiplier = 1f;
        lineRenderer.startWidth = lineRenderer.endWidth = 0.04f;
        lineRenderer.startColor = lineRenderer.endColor = previewColor;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        if (createdRenderer || lineRenderer.sharedMaterial == null)
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

        const float previewY = 0.03f;
        start.y = previewY;
        end.y = previewY;

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
