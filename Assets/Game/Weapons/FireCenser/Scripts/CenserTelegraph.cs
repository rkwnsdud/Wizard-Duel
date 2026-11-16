using UnityEngine;

// Shows the censer fan on the ground while an attack is winding up.
// The fan mesh is built from the weapon's range/angle so the preview always matches the hit test.
// Local-only visual: authority, damage and networking stay in FireCenserWeapon / PlayerAttack.
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class CenserTelegraph : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Height of the preview above the ground in meters.")]
    private float height = 0.05f;
    [SerializeField, Tooltip("Red fill used while the attack is winding up.")]
    private Material telegraphMaterial;
    [SerializeField, Range(8, 48), Tooltip("Arc segments used to draw the fan.")]
    private int segments = 24;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private FireCenserWeapon weapon;
    private Transform ownerRoot;
    private Mesh fanMesh;
    private GameObject fanObject;
    private MeshFilter fanFilter;
    private MeshRenderer fanRenderer;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        // The preview must not move the weapon itself, so the fan lives on a
        // separate runtime object. Keep any root mesh disabled.
        if (meshRenderer != null) meshRenderer.enabled = false;
        if (meshFilter != null) meshFilter.sharedMesh = null;
        weapon = GetComponentInParent<FireCenserWeapon>();
        EnsureFanObject();
    }

    void OnDestroy()
    {
        if (fanMesh != null) Destroy(fanMesh);
        if (fanObject != null) Destroy(fanObject);
    }

    void LateUpdate()
    {
        if (fanRenderer == null || !fanRenderer.enabled) return;
        if (fanObject == null || !fanObject.activeSelf) return;
        FlattenToGround();
    }

    /// <summary>Rebuilds the fan from live weapon values and shows it.</summary>
    public void Show()
    {
        if (weapon == null) weapon = GetComponentInParent<FireCenserWeapon>();
        if (weapon == null || weapon.AttackRange <= 0f) return;
        ownerRoot = weapon.transform.root;
        EnsureFanObject();
        Rebuild(weapon.AttackRange, weapon.AttackAngle);
        FlattenToGround();
        if (fanRenderer != null)
        {
            fanRenderer.material = telegraphMaterial;
            fanRenderer.enabled = telegraphMaterial != null;
        }
        if (fanObject != null) fanObject.SetActive(telegraphMaterial != null);
    }

    /// <summary>Hides the preview at swing time. Safe to call when already hidden.</summary>
    public void Hide()
    {
        if (fanRenderer != null) fanRenderer.enabled = false;
        if (fanObject != null) fanObject.SetActive(false);
    }

    /// <summary>Keeps the fan flat on the ground under the owner, following aim yaw only.</summary>
    private void FlattenToGround()
    {
        if (fanObject == null) return;
        if (ownerRoot == null)
            ownerRoot = weapon != null ? weapon.transform.root : transform.root;
        Transform owner = ownerRoot;
        if (owner == null) return;
        // The fan is a separate scene object, so the weapon never moves.
        // Keep it flat on the ground under the owner with yaw only.
        Vector3 aim = owner.forward;
        aim.y = 0f;
        if (aim.sqrMagnitude < 0.0001f) aim = Vector3.forward;
        aim.Normalize();
        fanObject.transform.SetPositionAndRotation(
            owner.position + Vector3.up * height,
            Quaternion.LookRotation(aim));
        fanObject.transform.localScale = Vector3.one;
    }

    private void EnsureFanObject()
    {
        if (fanObject != null && fanFilter != null && fanRenderer != null) return;
        fanObject = new GameObject("CenserTelegraphFan");
        fanObject.hideFlags = HideFlags.DontSave;
        fanObject.SetActive(false);
        fanFilter = fanObject.AddComponent<MeshFilter>();
        fanRenderer = fanObject.AddComponent<MeshRenderer>();
        fanRenderer.enabled = false;
        fanRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fanRenderer.receiveShadows = false;
        if (fanMesh != null && fanFilter != null) fanFilter.sharedMesh = fanMesh;
    }

    private void Rebuild(float range, float angle)
    {
        if (fanMesh == null)
        {
            fanMesh = new Mesh();
            fanMesh.name = "Censer telegraph fan";
            fanMesh.hideFlags = HideFlags.DontSave;
        }
        else fanMesh.Clear();

        int seats = Mathf.Max(2, segments + 1);
        Vector3[] vertices = new Vector3[seats + 1];
        Vector2[] uv = new Vector2[seats + 1];
        int[] triangles = new int[segments * 3];
        vertices[0] = new Vector3(0f, height, 0f);
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float yaw = -angle * 0.5f + angle * t;
            Vector3 edge = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * range;
            vertices[i + 1] = new Vector3(edge.x, height, edge.z);
            uv[i + 1] = new Vector2(0.5f + edge.x / (range * 2f), 0.5f + edge.z / (range * 2f));
        }
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
        fanMesh.vertices = vertices;
        fanMesh.uv = uv;
        fanMesh.triangles = triangles;
        fanMesh.RecalculateNormals();
        fanMesh.RecalculateBounds();
        if (fanFilter != null) fanFilter.sharedMesh = fanMesh;
    }
}
