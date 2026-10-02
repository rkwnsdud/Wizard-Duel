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
    [Range(0.03f, 0.05f)][SerializeField] private float lightningRefreshInterval = 0.04f;
    [Min(0.01f)][SerializeField] private float mainBoltWidth = 0.24f;
    [Tooltip("Maximum world-space displacement of intermediate main-bolt points.")]
    [Min(0f)][SerializeField] private float mainWander = 0.55f;
    [Tooltip("Maximum world-space change per intermediate point per refresh.")]
    [Min(0f)][SerializeField] private float mainMaxStep = 0.28f;
    [Range(0.02f, 0.05f)][SerializeField] private float mainEndConvergence = 0.03f;
    [Header("Branches")]
    [Range(0, 24)][SerializeField] private int branchCount = 12;
    [Min(0f)][SerializeField] private float branchMaxLength = 1.8f;
    [Range(0.05f, 0.6f)][SerializeField] private float branchWidthRatio = 0.32f;
    [Range(0f, 1f)][SerializeField] private float branchBrightness = 0.65f;
    [Tooltip("Maximum world-space half-width around the StartPoint-EndPoint axis, including glow ribbons.")]
    [Min(0.01f)][SerializeField] private float branchSpread = 1.1f;
    [Range(0f, 1f)][SerializeField] private float secondaryBranchChance = 0.6f;
    [Range(0f, 1f)][SerializeField] private float tailBranchBias = 0.6f;
    [Range(0f, 1f)][SerializeField] private float tailSecondaryBoost = 0.25f;
    [Min(0f)][SerializeField] private float tailBranchMaxLength = 0.65f;
    [Tooltip("Outward direction bias for twigs in the last quarter of the bolt.")]
    [Range(0f, 1f)][SerializeField] private float tailOutwardBias = 0.85f;

    [Header("Major Arcs")]
    [Range(0f, 0.95f)][SerializeField] private float majorArcWidthRatio = 0.65f;
    [Range(0f, 0.95f)][SerializeField] private float majorArcBrightness = 0.55f;
    [Min(0f)][SerializeField] private float majorArcOffset = 0.8f;
    [Min(0f)][SerializeField] private float majorArcBranchLength = 0.55f;
    [Tooltip("Length range used to limit arc count, including when no charge is supplied.")]
    [SerializeField] private Vector2 majorArcLengthRange = new Vector2(4f, 16f);

    [Header("Terminal Forks")]
    [Range(2, 4)][SerializeField] private int terminalForkCount = 3;
    [Range(0f, 0.95f)][SerializeField] private float terminalForkWidthRatio = 0.55f;
    [Range(0f, 0.95f)][SerializeField] private float terminalForkBrightness = 0.5f;
    [Min(0f)][SerializeField] private float terminalForkSpread = 0.65f;
    [Tooltip("Fork tips stop before the hit point, at independently sampled progress values.")]
    [SerializeField] private Vector2 terminalForkEndRange = new Vector2(0.88f, 0.97f);
    [Tooltip("Minimum tip displacement as a fraction of Terminal Fork Spread.")]
    [Range(0.1f, 1f)][SerializeField] private float terminalForkMinOffset = 0.45f;

    private const int MaxTerminalForks = 4;
    private const int TerminalForkSegments = 12;
    private readonly Vector3[][] terminalShapes = new Vector3[MaxTerminalForks][];
    private readonly Vector3[][] terminalPoints = new Vector3[MaxTerminalForks][];
    private readonly Vector3[][] terminalBranches = new Vector3[MaxTerminalForks * 2][];
    private readonly Vector2[] terminalRoots = new Vector2[MaxTerminalForks];
    private readonly Vector2[] terminalTips = new Vector2[MaxTerminalForks];
    private Vector3[] targetShape;
    private Vector2[] mainOffsets;
    private bool mainShapeInitialized;

    private const int MaxMajorArcs = 4;
    private const int MajorArcSegments = 16;
    private const int BranchesPerMajorArc = 2;
    private readonly Vector3[][] majorArcShapes = new Vector3[MaxMajorArcs][];
    private readonly Vector3[][] majorArcPoints = new Vector3[MaxMajorArcs][];
    private readonly Vector3[][] majorArcBranches = new Vector3[MaxMajorArcs * BranchesPerMajorArc][];
    private readonly Vector2[] majorArcRoots = new Vector2[MaxMajorArcs];
    private enum MajorArcKind { Free, Rejoin, Long }
    private readonly MajorArcKind[] majorArcKinds = new MajorArcKind[MaxMajorArcs];
    private readonly Vector2[] majorArcTips = new Vector2[MaxMajorArcs];
    private float visualCharge = -1f;

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
    private int majorArcVertexOffset, majorBranchVertexOffset;
    private int terminalVertexOffset, terminalBranchVertexOffset;

    // Attack endpoints are a world-space snapshot, independent of the animated FirePoint.
    // Standalone effects without SetEndpoints continue to follow their endpoint Transforms.
    private bool hasWorldEndpoints;
    private Vector3 worldStart, worldEnd;
    private float renderedDistance;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool logVisualLength;
#endif

    public void SetEndpoints(Vector3 start, Vector3 end)
    {
        if (startPoint == null || endPoint == null) return;
        worldStart = start;
        worldEnd = end;
        hasWorldEndpoints = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        logVisualLength = true;
#endif
        startPoint.position = start;
        endPoint.position = end;
        if (beamMesh != null) UpdateLightning();
    }

    public void SetMainBoltWidth(float width)
    {
        mainBoltWidth = width;
    }

    public void SetChargeRatio(float chargeRatio)
    {
        visualCharge = Mathf.Clamp01(chargeRatio);
        if (beamMesh != null) UpdateLightning();
    }

    private IEnumerator Start()
    {
        if (startPoint == null || endPoint == null || lightningBeamMaterial == null)
        {
            Debug.LogError("LightningEffect: StartPoint, EndPoint, Material이 필요합니다.", this);
            Destroy(gameObject);
            yield break;
        }
        ValidateSettings();
        CreateMesh();
        nextRefreshTime = -1f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            UpdateLightning(); // The entire bolt appears on the first frame.
            ApplyFade(elapsed);
            yield return null;
            elapsed += Time.deltaTime;
        }
        beamRenderer.enabled = false;
        Destroy(gameObject);
    }

    private void ApplyFade(float elapsed)
    {
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, elapsed / duration));
        properties.SetFloat("_Opacity", fade);
        beamRenderer.SetPropertyBlock(properties);
    }

    private void ValidateSettings()
    {
        duration = Mathf.Max(0.01f, duration);
        lightningSegmentCount = Mathf.Clamp(lightningSegmentCount, 4, 128);
        branchCount = Mathf.Clamp(branchCount, 0, 24);
        lightningJitter = Mathf.Max(0f, lightningJitter);
        branchMaxLength = Mathf.Max(0f, branchMaxLength);
        mainBoltWidth = Mathf.Max(0.01f, mainBoltWidth);
        lightningRefreshInterval = Mathf.Clamp(lightningRefreshInterval, 0.03f, 0.05f);
        mainWander = Mathf.Max(0f, mainWander);
        mainMaxStep = Mathf.Max(0f, mainMaxStep);
        mainEndConvergence = Mathf.Clamp(mainEndConvergence, 0.02f, 0.05f);
        tailBranchBias = Mathf.Clamp01(tailBranchBias);
        tailSecondaryBoost = Mathf.Clamp01(tailSecondaryBoost);
        tailBranchMaxLength = Mathf.Max(0f, tailBranchMaxLength);
        tailOutwardBias = Mathf.Clamp01(tailOutwardBias);
        terminalForkCount = Mathf.Clamp(terminalForkCount, 2, MaxTerminalForks);
        terminalForkWidthRatio = Mathf.Clamp(terminalForkWidthRatio, 0f, 0.95f);
        terminalForkBrightness = Mathf.Clamp(terminalForkBrightness, 0f, 0.95f);
        terminalForkSpread = Mathf.Max(0f, terminalForkSpread);
        terminalForkEndRange.x = Mathf.Clamp(terminalForkEndRange.x, 0.88f, 0.96f);
        terminalForkEndRange.y = Mathf.Clamp(terminalForkEndRange.y, terminalForkEndRange.x + 0.01f, 0.97f);
        terminalForkMinOffset = Mathf.Clamp(terminalForkMinOffset, 0.1f, 1f);
        branchWidthRatio = Mathf.Clamp(branchWidthRatio, 0.05f, 0.6f);
        branchBrightness = Mathf.Clamp01(branchBrightness);
        branchSpread = Mathf.Max(0.01f, branchSpread);
        secondaryBranchChance = Mathf.Clamp01(secondaryBranchChance);
        majorArcWidthRatio = Mathf.Clamp(majorArcWidthRatio, 0f, 0.95f);
        majorArcBrightness = Mathf.Clamp(majorArcBrightness, 0f, 0.95f);
        majorArcOffset = Mathf.Max(0f, majorArcOffset);
        majorArcBranchLength = Mathf.Max(0f, majorArcBranchLength);
        majorArcLengthRange.x = Mathf.Max(0f, majorArcLengthRange.x);
        majorArcLengthRange.y = Mathf.Max(majorArcLengthRange.x + 0.01f, majorArcLengthRange.y);
    }

    private void CreateMesh()
    {
        // Initialization runs once; refreshes only overwrite the existing mesh and buffers.
        if (beamMesh != null) return;
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
        AllocatePathBuffers();
        InitializeMeshTopology();
    }

    private void AllocatePathBuffers()
    {
        shape = new Vector3[lightningSegmentCount + 1];
        targetShape = new Vector3[shape.Length];
        mainOffsets = new Vector2[shape.Length];
        mainPoints = new Vector3[shape.Length];
        // Reserve one optional secondary ribbon per primary branch.
        branches = new Vector3[branchCount * 2][];
        branchRoots = new float[branchCount];
        branchLengthRatios = new float[branchCount];
        secondaryActive = new bool[branchCount];
        secondaryRoots = new int[branchCount];
        for (int b = 0; b < branches.Length; b++) branches[b] = new Vector3[BranchSegments + 1];
        // Stratified weighted sampling: 60% Free, 25% Rejoin, 15% Long.
        // At most one of the four slots can be Rejoin; kinds stay fixed for this flash.
        float arcTypePhase = Random.value;
        for (int a = 0; a < MaxMajorArcs; a++)
        {
            float pick = Mathf.Repeat(arcTypePhase + a / (float)MaxMajorArcs, 1f);
            majorArcKinds[a] = pick < 0.6f ? MajorArcKind.Free
                : pick < 0.85f ? MajorArcKind.Rejoin : MajorArcKind.Long;
            majorArcShapes[a] = new Vector3[MajorArcSegments + 1];
            majorArcPoints[a] = new Vector3[MajorArcSegments + 1];
        }
        for (int b = 0; b < majorArcBranches.Length; b++)
            majorArcBranches[b] = new Vector3[BranchSegments + 1];
        int arcVertices = MaxMajorArcs * (MajorArcSegments + 1);
        int arcBranchVertices = majorArcBranches.Length * (BranchSegments + 1);
        for (int f = 0; f < MaxTerminalForks; f++)
        {
            terminalShapes[f] = new Vector3[TerminalForkSegments + 1];
            terminalPoints[f] = new Vector3[TerminalForkSegments + 1];
        }
        for (int b = 0; b < terminalBranches.Length; b++)
            terminalBranches[b] = new Vector3[BranchSegments + 1];
        int terminalVertices = MaxTerminalForks * (TerminalForkSegments + 1) + terminalBranches.Length * (BranchSegments + 1);
        vertices = new Vector3[(shape.Length + branches.Length * (BranchSegments + 1) + arcVertices + arcBranchVertices + terminalVertices) * 2];
        uvs = new Vector2[vertices.Length];
        colors = new Color[vertices.Length];
        triangles = new int[(lightningSegmentCount + branches.Length * BranchSegments + MaxMajorArcs * MajorArcSegments + majorArcBranches.Length * BranchSegments + MaxTerminalForks * TerminalForkSegments + terminalBranches.Length * BranchSegments) * 6];
        majorArcVertexOffset = (shape.Length + branches.Length * (BranchSegments + 1)) * 2;
        majorBranchVertexOffset = majorArcVertexOffset + MaxMajorArcs * (MajorArcSegments + 1) * 2;
        terminalVertexOffset = majorBranchVertexOffset + majorArcBranches.Length * (BranchSegments + 1) * 2;
        terminalBranchVertexOffset = terminalVertexOffset + MaxTerminalForks * (TerminalForkSegments + 1) * 2;
    }

    private void InitializeMeshTopology()
    {
        int vertexOffset = 0, triangleOffset = 0;
        int arcStartRibbon = 1 + branches.Length;
        int terminalStartRibbon = arcStartRibbon + MaxMajorArcs + majorArcBranches.Length;
        for (int ribbon = 0; ribbon < terminalStartRibbon + MaxTerminalForks + terminalBranches.Length; ribbon++)
        {
            int segments = ribbon == 0 ? lightningSegmentCount
                : ribbon >= arcStartRibbon && ribbon < arcStartRibbon + MaxMajorArcs ? MajorArcSegments
                : ribbon >= terminalStartRibbon && ribbon < terminalStartRibbon + MaxTerminalForks ? TerminalForkSegments : BranchSegments;
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
        // Preserve random consumption order so identical seeds produce identical geometry.
        RandomizeMainPath();
        RandomizeMainBranches();
        RandomizeMajorArcs();
        RandomizeTerminalForks();
    }

    private void RandomizeMainPath()
    {
        targetShape[0] = Vector3.zero;
        targetShape[lightningSegmentCount] = Vector3.right;
        Subdivide(0, lightningSegmentCount, 1f);
        for (int i = 0; i < shape.Length; i++)
        {
            // Reserve the penultimate control point for the start of endpoint convergence.
            float t = i == lightningSegmentCount ? 1f
                : (i / (float)(lightningSegmentCount - 1)) * (1f - mainEndConvergence);
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.08f))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - mainEndConvergence, 1f, t)));
            Vector2 target = new Vector2(targetShape[i].y, targetShape[i].z) * mainWander
                + Random.insideUnitCircle * (Mathf.Max(lightningJitter * 0.35f, mainWander * 0.45f));
            target = Vector2.ClampMagnitude(target, mainWander) * envelope;
            mainOffsets[i] = mainShapeInitialized
                ? Vector2.MoveTowards(mainOffsets[i], target, mainMaxStep) : target;
            shape[i] = new Vector3(t, mainOffsets[i].x, mainOffsets[i].y);
        }
        shape[0] = Vector3.zero;
        shape[lightningSegmentCount] = Vector3.right;
        mainShapeInitialized = true;
    }

    private void RandomizeMainBranches()
    {
        int tailCount = Mathf.RoundToInt(branchCount * tailBranchBias);
        for (int b = 0; b < branchCount; b++)
        {
            // Stratified weighted sampling guarantees twigs in the final quarter.
            branchRoots[b] = b < tailCount
                ? Mathf.Lerp(0.75f, 0.97f, (b + Random.value) / Mathf.Max(1, tailCount))
                : Mathf.Lerp(0.08f, 0.75f, (b - tailCount + Random.value) / Mathf.Max(1, branchCount - tailCount));
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 direction = new Vector3(Random.Range(0.25f, 0.65f), side, Random.Range(-0.35f, 0.35f)).normalized;
            // Distinct short and long populations rather than near-identical lengths.
            branchLengthRatios[b] = b % 3 == 0 ? Random.Range(0.25f, 0.45f) : Random.Range(0.7f, 1f);
            GenerateBranch(branches[b], direction);
            float secondaryChance = Mathf.Clamp01(secondaryBranchChance + (branchRoots[b] >= 0.75f ? tailSecondaryBoost : 0f));
            secondaryActive[b] = branchLengthRatios[b] >= 0.7f && Random.value < secondaryChance;
            secondaryRoots[b] = Random.Range(2, 4);
            // Diverge from the parent in the horizontal plane so top views show the fork.
            Vector3 secondaryDirection = new Vector3(Random.Range(0.8f, 1.2f), -side * Random.Range(0.3f, 0.65f), Random.Range(-0.12f, 0.12f)).normalized;
            GenerateBranch(branches[branchCount + b], secondaryDirection);
        }
    }

    private void RandomizeMajorArcs()
    {
        for (int a = 0; a < MaxMajorArcs; a++)
        {
            float root = Mathf.Lerp(0.15f, 0.48f, a / (float)(MaxMajorArcs - 1)) + Random.Range(-0.04f, 0.04f);
            MajorArcKind kind = majorArcKinds[a];
            float endProgress = kind == MajorArcKind.Rejoin ? Random.Range(0.55f, 0.85f)
                : kind == MajorArcKind.Long ? Random.Range(0.85f, 0.98f)
                : Random.Range(Mathf.Max(0.58f, root + 0.18f), 0.92f);
            majorArcRoots[a] = new Vector2(root, endProgress);
            float side = a % 2 == 0 ? -1f : 1f;
            float amplitude = Random.Range(0.75f, 1f);
            majorArcTips[a] = new Vector2(side * Random.Range(0.5f, 1f), Random.Range(-0.3f, 0.3f));
            for (int i = 0; i <= MajorArcSegments; i++)
            {
                float t = i / (float)MajorArcSegments;
                float envelope = Mathf.Sin(t * Mathf.PI);
                majorArcShapes[a][i] = new Vector3(t,
                    envelope * (side * amplitude + Random.Range(-0.16f, 0.16f)),
                    envelope * Random.Range(-0.16f, 0.16f));
            }
            // Exact attachment to the main bolt, without a residual offset at either end.
            majorArcShapes[a][0] = Vector3.zero;
            majorArcShapes[a][MajorArcSegments] = Vector3.right;
            for (int b = 0; b < BranchesPerMajorArc; b++)
                GenerateBranch(majorArcBranches[a * BranchesPerMajorArc + b],
                    new Vector3(Random.Range(0.3f, 0.7f), side, Random.Range(-0.2f, 0.2f)).normalized);
        }
    }

    private int GetMajorArcCount(float distance)
    {
        if (distance < 0.5f) return 0;
        float lengthRatio = Mathf.InverseLerp(majorArcLengthRange.x, majorArcLengthRange.y, distance);
        // A wall-shortened bolt cannot carry the density of a full-length charged shot.
        float strength = visualCharge < 0f ? lengthRatio : Mathf.Min(visualCharge, lengthRatio);
        if (strength < 0.1f) return 1;
        if (strength < 0.5f) return 2;
        return strength < 0.8f ? 3 : 4;
    }

    private Vector3 SampleMainBolt(float t)
    {
        int segment = 0;
        while (segment < shape.Length - 2 && shape[segment + 1].x < t) segment++;
        return Vector3.Lerp(mainPoints[segment], mainPoints[segment + 1],
            Mathf.InverseLerp(shape[segment].x, shape[segment + 1].x, t));
    }

    private void UpdateMajorArcs(Vector3 start, Vector3 forward, Vector3 side, Vector3 up,
        float distance, float mainWidth, Camera camera)
    {
        int count = GetMajorArcCount(distance);
        int arcOffset = majorArcVertexOffset;
        int branchOffset = majorBranchVertexOffset;
        for (int a = 0; a < MaxMajorArcs; a++)
        {
            bool active = a < count;
            float width = active ? mainWidth * majorArcWidthRatio : 0f;
            float brightness = active ? majorArcBrightness : 0f;
            float extent = MinWithoutAllocation(majorArcOffset, distance * 0.12f, branchSpread * 0.8f);
            Vector3[] points = majorArcPoints[a];
            bool rejoin = majorArcKinds[a] == MajorArcKind.Rejoin;
            Vector3 arcStart = SampleMainBolt(majorArcRoots[a].x);
            // Open arcs use an independent axis-relative tip, not a sampled main-bolt endpoint.
            Vector3 arcEnd = rejoin ? SampleMainBolt(majorArcRoots[a].y)
                : start + forward * (distance * majorArcRoots[a].y)
                    + (side * majorArcTips[a].x + up * majorArcTips[a].y) * extent;
            BuildBoltPath(majorArcShapes[a], points, arcStart, arcEnd, start, forward, side, up,
                width, extent, rejoin, majorArcRoots[a]);
            points[MajorArcSegments] = rejoin ? arcEnd : ClampToEnvelope(arcEnd, start, forward, width);
            WriteRibbon(points, arcOffset + a * (MajorArcSegments + 1) * 2, width, brightness, !rejoin, camera);
            for (int b = 0; b < BranchesPerMajorArc; b++)
            {
                int index = a * BranchesPerMajorArc + b;
                Vector3 root = points[b == 0 ? 8 : 14];
                float length = active ? Mathf.Min(majorArcBranchLength, distance * 0.1f) : 0f;
                float branchWidth = Mathf.Min(width * branchWidthRatio, length * 0.25f);
                WriteBranch(majorArcBranches[index], root, start, forward, side, up, length,
                    branchWidth, brightness * branchBrightness, branchOffset + index * (BranchSegments + 1) * 2,
                    Vector3.Dot(root - start, forward) >= distance * 0.75f,
                    Mathf.Min(length, tailBranchMaxLength), camera);
            }
        }
    }

    private void RandomizeTerminalForks()
    {
        float firstSide = Random.value < 0.5f ? -1f : 1f;
        for (int f = 0; f < MaxTerminalForks; f++)
        {
            // Stratify tip progress so multiple thick tips do not pile up at one location.
            float end = Mathf.Lerp(terminalForkEndRange.x, terminalForkEndRange.y,
                (f + Random.value) / terminalForkCount);
            end = Mathf.Min(end, terminalForkEndRange.y);
            terminalRoots[f] = new Vector2(Random.Range(0.75f, Mathf.Min(0.9f, end - 0.025f)), end);
            float side = (f % 2 == 0 ? firstSide : -firstSide);
            Vector2 tip = new Vector2(side * Random.Range(0.6f, 1f), Random.Range(-0.35f, 0.35f));
            terminalTips[f] = tip.normalized * Random.Range(terminalForkMinOffset, 1f);
            for (int i = 0; i <= TerminalForkSegments; i++)
            {
                float t = i / (float)TerminalForkSegments;
                Vector2 offset = Random.insideUnitCircle * (Mathf.Sin(t * Mathf.PI) * 0.18f);
                terminalShapes[f][i] = new Vector3(t, offset.x, offset.y);
            }
            terminalShapes[f][0] = Vector3.zero;
            terminalShapes[f][TerminalForkSegments] = Vector3.right;
            for (int b = 0; b < 2; b++)
                GenerateBranch(terminalBranches[f * 2 + b],
                    new Vector3(Random.Range(0.35f, 0.8f), Random.Range(-1f, 1f), Random.Range(-0.25f, 0.25f)).normalized);
        }
    }

    private void UpdateTerminalForks(Vector3 start, Vector3 forward, Vector3 side, Vector3 up,
        float distance, float mainWidth, Camera camera)
    {
        int forkOffset = terminalVertexOffset;
        int twigOffset = terminalBranchVertexOffset;
        for (int f = 0; f < MaxTerminalForks; f++)
        {
            bool active = f < terminalForkCount && distance >= 0.5f;
            float width = active ? mainWidth * terminalForkWidthRatio : 0f;
            float brightness = active ? terminalForkBrightness : 0f;
            float spread = MinWithoutAllocation(terminalForkSpread, distance * 0.12f, branchSpread * 0.8f);
            Vector3[] points = terminalPoints[f];
            Vector3 forkStart = SampleMainBolt(terminalRoots[f].x);
            // Free tip relative to the attack axis, never a connection to Main or EndPoint.
            Vector3 forkEnd = start + forward * (distance * terminalRoots[f].y)
                + (side * terminalTips[f].x + up * terminalTips[f].y) * spread;
            BuildBoltPath(terminalShapes[f], points, forkStart, forkEnd, start, forward, side, up,
                width, spread, false, terminalRoots[f]);
            // Open-ended channels fade and narrow locally instead of forming blunt bright buds.
            WriteRibbon(points, forkOffset + f * (TerminalForkSegments + 1) * 2,
                width, brightness, true, camera);
            for (int b = 0; b < 2; b++)
            {
                int index = f * 2 + b;
                Vector3 root = points[b == 0 ? 6 : 10];
                float length = active ? MinWithoutAllocation(majorArcBranchLength, tailBranchMaxLength, distance * 0.08f) : 0f;
                float twigWidth = Mathf.Min(width * branchWidthRatio, length * 0.25f);
                WriteBranch(terminalBranches[index], root, start, forward, side, up, length,
                    twigWidth, brightness * branchBrightness, twigOffset + index * (BranchSegments + 1) * 2,
                    true, length, camera);
            }
        }
    }

    private void BuildBoltPath(Vector3[] normalizedPath, Vector3[] worldPath,
        Vector3 pathStart, Vector3 pathEnd, Vector3 start, Vector3 forward, Vector3 side, Vector3 up,
        float width, float spread, bool followMain, Vector2 progress)
    {
        for (int i = 0; i < normalizedPath.Length; i++)
        {
            Vector3 p = normalizedPath[i];
            Vector3 root = followMain ? SampleMainBolt(Mathf.Lerp(progress.x, progress.y, p.x))
                : Vector3.Lerp(pathStart, pathEnd, p.x);
            worldPath[i] = ClampToEnvelope(root + (side * p.y + up * p.z) * spread, start, forward, width);
        }
        worldPath[0] = pathStart;
    }

    private void WriteBranch(Vector3[] normalizedPath, Vector3 root, Vector3 start,
        Vector3 forward, Vector3 side, Vector3 up, float length, float width, float brightness,
        int vertexOffset, bool outward, float outwardLength, Camera camera)
    {
        for (int i = 0; i < normalizedPath.Length; i++)
        {
            Vector3 p = normalizedPath[i];
            Vector3 point = outward ? OutwardTwigPoint(root, p, start, forward, side, up, outwardLength)
                : root + (forward * p.x + side * p.y + up * p.z) * length;
            branchWorldPoints[i] = ClampToEnvelope(point, start, forward, width);
        }
        WriteRibbon(branchWorldPoints, vertexOffset, width, brightness, true, camera);
    }

    // Mathf.Min(a, b, c) uses a params float[]; preserve its comparison order without allocating.
    private static float MinWithoutAllocation(float a, float b, float c)
    {
        float result = a;
        if (b < result) result = b;
        if (c < result) result = c;
        return result;
    }

    private Vector3 OutwardTwigPoint(Vector3 root, Vector3 p, Vector3 start, Vector3 forward,
        Vector3 side, Vector3 up, float length)
    {
        Vector3 radial = root - start;
        radial -= forward * Vector3.Dot(radial, forward);
        Vector3 outward = radial.sqrMagnitude > 0.0001f ? radial.normalized : side * (p.y < 0f ? -1f : 1f);
        Vector3 lateral = side * p.y + up * p.z;
        // Reflect inward steps before blending toward the radial outward direction.
        float inward = Vector3.Dot(lateral, outward);
        if (inward < 0f) lateral -= outward * (2f * inward);
        lateral = Vector3.Lerp(lateral, outward * lateral.magnitude, tailOutwardBias);
        return root + (forward * (p.x * 0.3f) + lateral) * length;
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
        Vector3 point = Vector3.Lerp(targetShape[first], targetShape[last], (middle - first) / (float)(last - first));
        Vector2 noise = Random.insideUnitCircle * displacement;
        point.y += noise.x;
        point.z += noise.y;
        targetShape[middle] = point;
        Subdivide(first, middle, displacement * 0.48f);
        Subdivide(middle, last, displacement * 0.48f);
    }

    private void UpdateLightning()
    {
        if (startPoint == null || endPoint == null) return;
        RefreshShapeIfNeeded();
        Vector3 start = hasWorldEndpoints ? worldStart : startPoint.position;
        Vector3 end = hasWorldEndpoints ? worldEnd : endPoint.position;
        float distance = Vector3.Distance(start, end);
        renderedDistance = distance;
        Vector3 forward = distance > 0.0001f ? (end - start) / distance : Vector3.forward;
        Vector3 side = Vector3.Cross(forward, Vector3.up);
        if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(forward, Vector3.right);
        side.Normalize();
        Vector3 up = Vector3.Cross(side, forward).normalized;
        float mainWidth = MinWithoutAllocation(mainBoltWidth, distance, branchSpread * 2f);
        BuildMainPath(start, end, forward, side, up, distance, mainWidth);
        Camera camera = Camera.main;
        WriteRibbon(mainPoints, 0, mainWidth, 1f, false, camera);
        WriteMainBranches(start, forward, side, up, distance, mainWidth, camera);
        UpdateMajorArcs(start, forward, side, up, distance, mainWidth, camera);
        UpdateTerminalForks(start, forward, side, up, distance, mainWidth, camera);
        UploadMesh();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Temporary Play mode diagnostics: measure the rendered main ribbon, not its path bends.
        if (logVisualLength)
        {
            Vector3 visualStart = transform.TransformPoint((vertices[0] + vertices[1]) * 0.5f);
            int last = lightningSegmentCount * 2;
            Vector3 visualEnd = transform.TransformPoint((vertices[last] + vertices[last + 1]) * 0.5f);
            Debug.Log($"[Visual] actualLength={distance:F3}, visualLength={Vector3.Distance(visualStart, visualEnd):F3}, start={visualStart}, end={visualEnd}", this);
            logVisualLength = false;
        }
#endif
    }

    private void RefreshShapeIfNeeded()
    {
        if (Time.time >= nextRefreshTime)
        {
            RandomizeShape();
            // Keep the cadence on a fixed schedule instead of accumulating frame-rounding drift.
            nextRefreshTime = nextRefreshTime < 0f ? Time.time + lightningRefreshInterval
                : nextRefreshTime + (Mathf.Floor((Time.time - nextRefreshTime) / lightningRefreshInterval) + 1f) * lightningRefreshInterval;
        }
    }

    private void BuildMainPath(Vector3 start, Vector3 end, Vector3 forward, Vector3 side,
        Vector3 up, float distance, float mainWidth)
    {
        float shortBoltScale = Mathf.Min(1f, distance / 4f);
        for (int i = 0; i < shape.Length; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, shape[i].x) + (side * shape[i].y + up * shape[i].z) * shortBoltScale;
            mainPoints[i] = ClampToEnvelope(point, start, forward, mainWidth);
        }
        mainPoints[0] = start;
        mainPoints[mainPoints.Length - 1] = end;
    }

    private void WriteMainBranches(Vector3 start, Vector3 forward, Vector3 side, Vector3 up,
        float distance, float mainWidth, Camera camera)
    {
        for (int b = 0; b < branchCount; b++)
        {
            Vector3 root = SampleMainBolt(branchRoots[b]);
            float length = Mathf.Min(branchMaxLength, distance * 0.35f) * branchLengthRatios[b];
            bool tail = branchRoots[b] >= 0.75f;
            if (tail) length = MinWithoutAllocation(branchMaxLength, distance * 0.35f, tailBranchMaxLength) * branchLengthRatios[b];
            float width = Mathf.Min(mainWidth * branchWidthRatio, length * 0.25f);
            int offset = (shape.Length + b * (BranchSegments + 1)) * 2;
            WriteBranch(branches[b], root, start, forward, side, up, length, width,
                branchBrightness, offset, tail, length, camera);
            Vector3 secondaryRoot = branchWorldPoints[secondaryRoots[b]];
            float secondaryLength = secondaryActive[b] ? length * 0.35f : 0f;
            float secondaryWidth = secondaryActive[b] ? width * 0.6f : 0f;
            offset = (shape.Length + (branchCount + b) * (BranchSegments + 1)) * 2;
            WriteBranch(branches[branchCount + b], secondaryRoot, start, forward, side, up,
                secondaryLength, secondaryWidth, secondaryActive[b] ? branchBrightness * 0.65f : 0f,
                offset, tail, secondaryLength, camera);
        }
    }

    private void UploadMesh()
    {
        beamMesh.vertices = vertices;
        beamMesh.colors = colors;
        beamMesh.RecalculateBounds();
    }

    private Vector3 ClampToEnvelope(Vector3 point, Vector3 start, Vector3 forward, float width)
    {
        Vector3 delta = point - start;
        // Side branches must not extend beyond the same start/end interval used for damage.
        float progress = Vector3.Dot(delta, forward);
        Vector3 radial = delta - forward * progress;
        Vector3 along = forward * Mathf.Clamp(progress, 0f, renderedDistance);
        // Both branch generations share one envelope; reserve room for the glow ribbon.
        float radius = Mathf.Max(0f, branchSpread - width * 0.5f);
        return start + along + Vector3.ClampMagnitude(radial, radius);
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
