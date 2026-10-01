using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LightningEffect : MonoBehaviour
{
    [Header("Endpoints")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [Min(0.01f)][SerializeField] private float duration = 0.2f;
    [Header("Main Bolt")]
    [SerializeField] private Material lightningBeamMaterial;
    [Range(4, 128)][SerializeField] private int lightningSegmentCount = 32;
    [Min(0f)][SerializeField] private float lightningJitter = 0.3f;
    [Min(0.01f)][SerializeField] private float lightningRefreshInterval = 0.06f;
    [Min(0.01f)][SerializeField] private float mainBoltWidth = 0.24f;
    [Header("Branches")]
    [Range(0, 24)][SerializeField] private int branchCount = 12;
    [Min(0f)][SerializeField] private float branchMaxLength = 1.8f;
    [Range(0.05f, 0.6f)][SerializeField] private float branchWidthRatio = 0.32f;
    [Range(0f, 1f)][SerializeField] private float branchBrightness = 0.65f;
    [Tooltip("Maximum world-space half-width around the StartPoint-EndPoint axis, including glow ribbons.")]
    [Min(0.01f)][SerializeField] private float branchSpread = 1.1f;
    [Range(0f, 1f)][SerializeField] private float secondaryBranchChance = 0.6f;

    private const int BranchSegments = 5;
    private Mesh beamMesh;
    private MeshRenderer beamRenderer;
    private MaterialPropertyBlock properties;
    private Vector3[] shape, mainPoints, vertices;
    private Vector3[][] branches;
    private float[] branchRoots;
    private float[] branchLengthRatios;
    private bool[] secondaryActive;
    private int[] secondaryRoots;
    private Vector2[] uvs;
    private Color[] colors;
    private int[] triangles;
    private float nextRefreshTime;
    private readonly Vector3[] branchWorldPoints = new Vector3[BranchSegments + 1];

    public void SetEndpoints(Vector3 start, Vector3 end)
    {
        if (startPoint == null || endPoint == null) return;
        startPoint.position = start;
        endPoint.position = end;
        if (beamMesh != null) UpdateLightning();
    }

    public void SetMainBoltWidth(float width)
    {
        mainBoltWidth = width;
    }

    private IEnumerator Start()
    {
        if (startPoint == null || endPoint == null || lightningBeamMaterial == null)
        {
            Debug.LogError("LightningEffect: StartPoint, EndPoint, Material이 필요합니다.", this);
            Destroy(gameObject);
            yield break;
        }
        duration = Mathf.Max(0.01f, duration);
        lightningSegmentCount = Mathf.Clamp(lightningSegmentCount, 4, 128);
        branchCount = Mathf.Clamp(branchCount, 0, 24);
        lightningJitter = Mathf.Max(0f, lightningJitter);
        branchMaxLength = Mathf.Max(0f, branchMaxLength);
        mainBoltWidth = Mathf.Max(0.01f, mainBoltWidth);
        lightningRefreshInterval = Mathf.Max(0.01f, lightningRefreshInterval);
        branchWidthRatio = Mathf.Clamp(branchWidthRatio, 0.05f, 0.6f);
        branchBrightness = Mathf.Clamp01(branchBrightness);
        branchSpread = Mathf.Max(0.01f, branchSpread);
        secondaryBranchChance = Mathf.Clamp01(secondaryBranchChance);
        CreateMesh();
        nextRefreshTime = -1f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            UpdateLightning(); // The entire bolt appears on the first frame.
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, elapsed / duration));
            properties.SetFloat("_Opacity", fade);
            beamRenderer.SetPropertyBlock(properties);
            yield return null;
            elapsed += Time.deltaTime;
        }
        beamRenderer.enabled = false;
        Destroy(gameObject);
    }

    private void CreateMesh()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        beamRenderer = GetComponent<MeshRenderer>();
        if (beamRenderer == null) beamRenderer = gameObject.AddComponent<MeshRenderer>();
        beamMesh = new Mesh { name = "LightningMainAndBranches" };
        beamMesh.MarkDynamic();
        filter.sharedMesh = beamMesh;
        beamRenderer.sharedMaterial = lightningBeamMaterial;
        beamRenderer.sortingOrder = 1;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamRenderer.receiveShadows = false;
        properties = new MaterialPropertyBlock();
        shape = new Vector3[lightningSegmentCount + 1];
        mainPoints = new Vector3[shape.Length];
        // Reserve one optional secondary ribbon per primary branch.
        branches = new Vector3[branchCount * 2][];
        branchRoots = new float[branchCount];
        branchLengthRatios = new float[branchCount];
        secondaryActive = new bool[branchCount];
        secondaryRoots = new int[branchCount];
        for (int b = 0; b < branches.Length; b++) branches[b] = new Vector3[BranchSegments + 1];
        vertices = new Vector3[(shape.Length + branches.Length * (BranchSegments + 1)) * 2];
        uvs = new Vector2[vertices.Length];
        colors = new Color[vertices.Length];
        triangles = new int[(lightningSegmentCount + branches.Length * BranchSegments) * 6];
        int vertexOffset = 0, triangleOffset = 0;
        for (int ribbon = 0; ribbon <= branches.Length; ribbon++)
        {
            int segments = ribbon == 0 ? lightningSegmentCount : BranchSegments;
            for (int i = 0; i <= segments; i++)
            {
                int v = vertexOffset + i * 2;
                float t = (float)i / segments;
                uvs[v] = new Vector2(t, 0f);
                uvs[v + 1] = new Vector2(t, 1f);
                if (i == segments) continue;
                int index = triangleOffset + i * 6;
                triangles[index] = v;
                triangles[index + 1] = v + 2;
                triangles[index + 2] = v + 1;
                triangles[index + 3] = v + 1;
                triangles[index + 4] = v + 2;
                triangles[index + 5] = v + 3;
            }
            vertexOffset += (segments + 1) * 2;
            triangleOffset += segments * 6;
        }
        beamMesh.vertices = vertices;
        beamMesh.uv = uvs;
        beamMesh.triangles = triangles;
    }

    private void RandomizeShape()
    {
        shape[0] = Vector3.zero;
        shape[lightningSegmentCount] = Vector3.right;
        Subdivide(0, lightningSegmentCount, 1f);
        for (int b = 0; b < branchCount; b++)
        {
            branchRoots[b] = Mathf.Lerp(0.1f, 0.9f, (b + Random.value) / branchCount);
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 direction = new Vector3(Random.Range(0.25f, 0.65f), side, Random.Range(-0.35f, 0.35f)).normalized;
            // Distinct short and long populations rather than near-identical lengths.
            branchLengthRatios[b] = b % 3 == 0 ? Random.Range(0.25f, 0.45f) : Random.Range(0.7f, 1f);
            GenerateBranch(branches[b], direction);
            secondaryActive[b] = branchLengthRatios[b] >= 0.7f && Random.value < secondaryBranchChance;
            secondaryRoots[b] = Random.Range(2, 4);
            // Diverge from the parent in the horizontal plane so top views show the fork.
            Vector3 secondaryDirection = new Vector3(Random.Range(0.8f, 1.2f), -side * Random.Range(0.3f, 0.65f), Random.Range(-0.12f, 0.12f)).normalized;
            GenerateBranch(branches[branchCount + b], secondaryDirection);
        }
    }

    private void GenerateBranch(Vector3[] points, Vector3 direction)
    {
        points[0] = Vector3.zero;
        Vector3 offset = Vector3.zero;
        for (int i = 1; i <= BranchSegments; i++)
        {
            Vector3 noise = Random.insideUnitSphere;
            noise.z *= 0.3f; // Favor the ground plane instead of hiding detail in height.
            Vector3 step = (direction + noise * 0.3f).normalized;
            // Total normalized path length <= 1, including all bends.
            offset += step * (Random.Range(0.85f, 1f) / BranchSegments);
            points[i] = offset;
        }
    }

    private void Subdivide(int first, int last, float displacement)
    {
        if (last - first <= 1) return;
        int middle = (first + last) / 2;
        Vector3 point = Vector3.Lerp(shape[first], shape[last], Random.Range(0.35f, 0.65f));
        Vector2 noise = Random.insideUnitCircle * displacement;
        point.y += noise.x;
        point.z += noise.y;
        shape[middle] = point;
        Subdivide(first, middle, displacement * 0.48f);
        Subdivide(middle, last, displacement * 0.48f);
    }

    private void UpdateLightning()
    {
        if (startPoint == null || endPoint == null) return;
        if (Time.time >= nextRefreshTime)
        {
            RandomizeShape();
            nextRefreshTime = Time.time + lightningRefreshInterval;
        }
        Vector3 start = startPoint.position, end = endPoint.position;
        float distance = Vector3.Distance(start, end);
        Vector3 forward = distance > 0.0001f ? (end - start) / distance : Vector3.forward;
        Vector3 side = Vector3.Cross(forward, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(forward, Vector3.right);
        side.Normalize();
        Vector3 up = Vector3.Cross(side, forward).normalized;
        float mainWidth = Mathf.Min(mainBoltWidth, distance, branchSpread * 2f);
        float jitter = Mathf.Min(lightningJitter, distance * 0.07f, branchSpread * 0.3f);
        for (int i = 0; i < shape.Length; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, shape[i].x) + (side * shape[i].y + up * shape[i].z) * jitter;
            mainPoints[i] = ClampToEnvelope(point, start, forward, mainWidth);
        }
        mainPoints[0] = start;
        mainPoints[mainPoints.Length - 1] = end;
        Camera camera = Camera.main;
        WriteRibbon(mainPoints, 0, mainWidth, 1f, false, camera);
        for (int b = 0; b < branchCount; b++)
        {
            int segment = 0;
            while (segment < shape.Length - 2 && shape[segment + 1].x < branchRoots[b]) segment++;
            float t = Mathf.InverseLerp(shape[segment].x, shape[segment + 1].x, branchRoots[b]);
            Vector3 root = Vector3.Lerp(mainPoints[segment], mainPoints[segment + 1], t);
            float length = Mathf.Min(branchMaxLength, distance * 0.35f) * branchLengthRatios[b];
            float width = Mathf.Min(mainWidth * branchWidthRatio, length * 0.25f);
            for (int i = 0; i <= BranchSegments; i++)
            {
                Vector3 p = branches[b][i];
                Vector3 point = root + (forward * p.x + side * p.y + up * p.z) * length;
                branchWorldPoints[i] = ClampToEnvelope(point, start, forward, width);
            }
            int offset = (shape.Length + b * (BranchSegments + 1)) * 2;
            WriteRibbon(branchWorldPoints, offset, width, branchBrightness, true, camera);
            Vector3 secondaryRoot = branchWorldPoints[secondaryRoots[b]];
            float secondaryLength = secondaryActive[b] ? length * 0.35f : 0f;
            float secondaryWidth = secondaryActive[b] ? width * 0.6f : 0f;
            for (int i = 0; i <= BranchSegments; i++)
            {
                Vector3 p = branches[branchCount + b][i];
                Vector3 point = secondaryRoot + (forward * p.x + side * p.y + up * p.z) * secondaryLength;
                branchWorldPoints[i] = ClampToEnvelope(point, start, forward, secondaryWidth);
            }
            offset = (shape.Length + (branchCount + b) * (BranchSegments + 1)) * 2;
            WriteRibbon(branchWorldPoints, offset, secondaryWidth, secondaryActive[b] ? branchBrightness * 0.65f : 0f, true, camera);
        }
        beamMesh.vertices = vertices;
        beamMesh.colors = colors;
        beamMesh.RecalculateBounds();
    }

    private Vector3 ClampToEnvelope(Vector3 point, Vector3 start, Vector3 forward, float width)
    {
        Vector3 delta = point - start;
        Vector3 along = forward * Vector3.Dot(delta, forward);
        // Both branch generations share one envelope; reserve room for the glow ribbon.
        float radius = Mathf.Max(0f, branchSpread - width * 0.5f);
        return start + along + Vector3.ClampMagnitude(delta - along, radius);
    }

    private void WriteRibbon(Vector3[] points, int offset, float width, float brightness, bool taper, Camera camera)
    {
        Vector3 previousWidth = Vector3.zero;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 tangent = points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(0, i - 1)];
            if (tangent.sqrMagnitude < 0.000001f) tangent = Vector3.forward;
            Vector3 view = camera == null ? Vector3.up : camera.orthographic ? -camera.transform.forward : camera.transform.position - points[i];
            Vector3 widthDirection = Vector3.Cross(tangent, view);
            if (widthDirection.sqrMagnitude < 0.000001f) widthDirection = Vector3.Cross(tangent, Vector3.up);
            if (widthDirection.sqrMagnitude < 0.000001f) widthDirection = Vector3.Cross(tangent, Vector3.right);
            widthDirection.Normalize();
            if (Vector3.Dot(previousWidth, widthDirection) < 0f) widthDirection = -widthDirection;
            previousWidth = widthDirection;
            float t = (float)i / (points.Length - 1);
            float profile = taper ? Mathf.Lerp(1f, 0.03f, t * t) : 0.9f + 0.1f * Mathf.Sin(t * 19f);
            Vector3 halfWidth = widthDirection * (width * profile * 0.5f);
            int v = offset + i * 2;
            vertices[v] = transform.InverseTransformPoint(points[i] - halfWidth);
            vertices[v + 1] = transform.InverseTransformPoint(points[i] + halfWidth);
            float emission = brightness * (taper ? 1f - t * t * t : 1f);
            colors[v] = colors[v + 1] = new Color(1f, 1f, 1f, emission);
        }
    }

    private void OnDestroy()
    {
        if (beamMesh != null) Destroy(beamMesh);
    }
}
