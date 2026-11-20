using UnityEngine;

public sealed class DestructibleWallBlock : MonoBehaviour, IEnvironmentDestructible
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    public int CellX { get; private set; }
    public int CellZ { get; private set; }
    public bool IsDestroyed { get; private set; }

    void Awake() => AddCrackMarks();
    public void SetCell(int x, int z) { CellX = x; CellZ = z; }

    void AddCrackMarks()
    {
        // Deep, branching fissures on every face make these walls readable
        // without requiring a separate imported mesh or changing their collider.
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, new Color(.12f, .10f, .09f));
        AddCrackPattern(material, -.506f, false, -1f);
        AddCrackPattern(material, .506f, false, 1f);
        AddCrackPattern(material, -.506f, true, -1f);
        AddCrackPattern(material, .506f, true, 1f);
        // Keep the generated material alive for the lifetime of this block.
        crackMaterial = material;
    }

    Material crackMaterial;

    void AddCrackPattern(Material material, float face, bool side, float direction)
    {
        float yaw = side ? 90f : 0f;
        float z = side ? 0f : face;
        float x = side ? face : 0f;
        // A wider central split with four jagged branches reads at game distance.
        MakeCrack(material, new Vector3(x, 1.2f, z), new Vector3(.11f, .98f, .03f), direction * 15f, yaw);
        MakeCrack(material, new Vector3(x + (side ? 0f : -.12f), 1.46f, z + (side ? .12f : 0f)),
            new Vector3(.08f, .42f, .032f), -direction * 48f, yaw);
        MakeCrack(material, new Vector3(x + (side ? 0f : .12f), 1.31f, z + (side ? -.12f : 0f)),
            new Vector3(.075f, .38f, .032f), direction * 48f, yaw);
        MakeCrack(material, new Vector3(x + (side ? 0f : -.11f), .99f, z + (side ? .11f : 0f)),
            new Vector3(.075f, .35f, .032f), direction * 45f, yaw);
        MakeCrack(material, new Vector3(x + (side ? 0f : .10f), .83f, z + (side ? -.10f : 0f)),
            new Vector3(.07f, .32f, .032f), -direction * 46f, yaw);
    }

    void MakeCrack(Material material, Vector3 localPosition, Vector3 scale, float angle, float yaw = 0f)
    {
        var crack = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crack.name = "Crack";
        crack.transform.SetParent(transform, false);
        crack.transform.localPosition = localPosition;
        crack.transform.localRotation = Quaternion.Euler(0, yaw, angle);
        crack.transform.localScale = scale;
        Collider collider = crack.GetComponent<Collider>();
        if (Application.isPlaying) Destroy(collider);
        else DestroyImmediate(collider);
        crack.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    public void ApplyDestroyed(bool burning)
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        if (burning) EnvironmentBurnEffect.Spawn(transform.position);
        gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    void OnDestroy()
    {
        if (crackMaterial == null) return;
        if (Application.isPlaying) Destroy(crackMaterial);
        else DestroyImmediate(crackMaterial);
    }
}
