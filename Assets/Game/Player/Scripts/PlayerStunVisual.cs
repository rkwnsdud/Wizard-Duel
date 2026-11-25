using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerStunVisual : MonoBehaviour
{
    [SerializeField] LineRenderer arc;
    [SerializeField] LineRenderer upperSpark;
    [SerializeField] LineRenderer lowerSpark;

    const int ArcSegments = 32;
    const int SparkSegments = 6;
    const float RefreshInterval = 0.055f;
    const float OpeningFlashDuration = 0.11f;
    static readonly int Frame = Shader.PropertyToID("_Frame");
    static readonly int Brightness = Shader.PropertyToID("_Brightness");
    static readonly int Breakup = Shader.PropertyToID("_Breakup");
    static readonly int GapShift = Shader.PropertyToID("_GapShift");
    static readonly int[] Frames = { 0, 8, 4, 12 };

    readonly Vector3[] arcPoints = new Vector3[ArcSegments + 1];
    readonly Vector3[] sparkPoints = new Vector3[SparkSegments + 1];
    MaterialPropertyBlock properties;
    float nextRefresh;
    float openingFlashEnd;
    int flash;
    bool visible;

    void Awake() => SetRenderersEnabled(false);

    public void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        SetRenderersEnabled(value);
        if (!value) return;

        float now = Time.time;
        flash = 0;
        openingFlashEnd = now + OpeningFlashDuration;
        DrawFlash(1f);
        nextRefresh = now + RefreshInterval;
    }

    void LateUpdate()
    {
        float now = Time.time;
        if (!visible || now < nextRefresh) return;
        float opening = Mathf.Clamp01((openingFlashEnd - now) / (OpeningFlashDuration - RefreshInterval));
        nextRefresh = opening > 0f ? Mathf.Min(now + RefreshInterval, openingFlashEnd) : now + RefreshInterval;
        flash = (flash + 1) % 12;
        DrawFlash(opening);
    }

#if UNITY_EDITOR
    [ContextMenu("Preview Stun Arc")]
    public void DrawPreview()
    {
        flash = 0;
        DrawFlash(0f);
    }
#endif

    void SetRenderersEnabled(bool value)
    {
        if (arc != null) arc.enabled = value;
        if (upperSpark != null) upperSpark.enabled = value;
        if (lowerSpark != null) lowerSpark.enabled = value;
    }

    void DrawFlash(float opening)
    {
        if (arc == null) return;
        float phase = flash * 1.67f;
        float startAngle = 0.34f + Mathf.Sin(phase) * 0.23f;
        float sweep = 5.35f + Mathf.Cos(phase * 0.7f) * 0.25f;
        for (int index = 0; index <= ArcSegments; index++)
        {
            float progress = index / (float)ArcSegments;
            float angle = startAngle + progress * sweep;
            float radius = 0.53f + opening * 0.058f + Mathf.Sin(index * 0.77f + phase) * 0.027f
                + Mathf.Sin(index * 2.39f + phase) * 0.016f;
            float height = 1.18f + Mathf.Cos(angle + 0.42f) * 0.15f
                + Mathf.Sin(index * 0.94f - phase) * 0.050f
                + Mathf.Sin(index * 2.55f + phase) * 0.023f;
            arcPoints[index] = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        }

        int frame = Frames[flash % Frames.Length];
        float intensity = 0.96f + 0.29f * Mathf.Cos(flash * 2.4f) + opening * 1.15f;
        DrawLine(arc, arcPoints, frame, intensity, 1f, Mathf.Sin(phase) * 0.055f);
        float upperBrightness = flash % 3 == 1 ? 0f : 1.03f;
        float lowerBrightness = flash % 3 == 2 ? 0f : 1.05f;
        DrawSpark(upperSpark, 21 + flash % 3, 1, 0.26f + opening * 0.20f, frame,
            Mathf.Lerp(upperBrightness, 2.15f, opening));
        DrawSpark(lowerSpark, 5 + flash % 4, -1, 0.30f + opening * 0.19f, frame,
            Mathf.Lerp(lowerBrightness, 2.20f, opening));
    }

    void DrawSpark(LineRenderer spark, int originIndex, int direction, float length, int frame, float brightness)
    {
        if (spark == null) return;
        spark.enabled = brightness > 0;
        if (!spark.enabled) return;

        Vector3 origin = arcPoints[originIndex];
        Vector3 outward = new Vector3(origin.x, 0, origin.z).normalized;
        Vector3 tangent = new Vector3(-outward.z, 0, outward.x) * direction;
        Vector3 endpoint = tangent * (length * 0.53f) + outward * (length * 0.81f)
            + Vector3.up * (direction * length * 0.62f);
        for (int index = 0; index <= SparkSegments; index++)
        {
            float progress = index / (float)SparkSegments;
            float bend = Mathf.Sin(progress * Mathf.PI);
            sparkPoints[index] = origin + endpoint * progress + outward * (bend * 0.04f)
                + Vector3.up * (bend * Mathf.Sin(index * 2.5f + flash) * 0.033f);
        }

        DrawLine(spark, sparkPoints, frame, brightness, 0f, 0f);
    }

    void DrawLine(LineRenderer line, Vector3[] points, int frame, float brightness, float breakup, float gapShift)
    {
        line.positionCount = points.Length;
        line.SetPositions(points);
        properties ??= new MaterialPropertyBlock();
        properties.SetFloat(Frame, frame);
        properties.SetFloat(Brightness, brightness);
        properties.SetFloat(Breakup, breakup);
        properties.SetFloat(GapShift, gapShift);
        line.SetPropertyBlock(properties);
    }
}
