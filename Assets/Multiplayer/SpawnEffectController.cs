using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small, client-local spawn flourish. It never changes the networked player transform.</summary>
public sealed class SpawnEffectController : MonoBehaviour
{
    [SerializeField, Min(0.5f)] float duration = 1.2f;
    [SerializeField, Min(0.1f)] float radius = 1.05f;
    [SerializeField, Min(0f)] float floorOffset = 0.035f;
    [SerializeField, Min(0f)] float rotationSpeed = 90f;
    [SerializeField, Min(0f)] float particleRiseSpeed = 1.5f;
    [SerializeField, Min(1)] int particleCount = 22;
    [SerializeField] Color blueColor = new Color(0.05f, 0.8f, 1f, 1f);
    [SerializeField] Color redColor = new Color(1f, 0.22f, 0.035f, 1f);

    static readonly Dictionary<int, SpawnEffectController> Active = new Dictionary<int, SpawnEffectController>();
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    Mesh runeMesh;
    MeshRenderer runeRenderer;
    ParticleSystem particles;
    Material runeMaterial;
    Material particleMaterial;
    int effectSlot;
    Color effectColor;
    float elapsed;

    public static void Play(Vector3 playerPosition, int slot, float requestedDuration, float requestedRadius,
        float requestedFloorOffset, float requestedRotationSpeed, float requestedParticleRiseSpeed,
        int requestedParticleCount, Color blueColor, Color redColor)
    {
        Stop(slot);
        GameObject root = new GameObject($"SpawnEffect_Player{slot + 1}");
        SpawnEffectController controller = root.AddComponent<SpawnEffectController>();
        controller.effectSlot = slot;
        controller.duration = Mathf.Max(0.5f, requestedDuration);
        controller.radius = Mathf.Max(0.1f, requestedRadius);
        controller.floorOffset = Mathf.Max(0f, requestedFloorOffset);
        controller.rotationSpeed = requestedRotationSpeed;
        controller.particleRiseSpeed = requestedParticleRiseSpeed;
        controller.particleCount = Mathf.Max(1, requestedParticleCount);
        controller.blueColor = blueColor;
        controller.redColor = redColor;
        controller.Initialize(playerPosition);
        Active[slot] = controller;
    }

    public static void Stop(int slot)
    {
        if (!Active.TryGetValue(slot, out SpawnEffectController old) || old == null) return;
        Active.Remove(slot);
        Destroy(old.gameObject);
    }

    void Initialize(Vector3 position)
    {
        effectColor = effectSlot == 0 ? blueColor : redColor;
        transform.position = position + Vector3.up * floorOffset;
        Shader runeShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? runeShader;
        if (runeShader == null || particleShader == null)
        {
            Debug.LogWarning("Spawn effect could not find a compatible URP unlit shader.", this);
            Destroy(gameObject);
            return;
        }

        runeMaterial = CreateTransparentMaterial(runeShader, "SpawnRune_RuntimeMaterial");
        particleMaterial = CreateTransparentMaterial(particleShader, "SpawnParticles_RuntimeMaterial");
        runeMesh = CreateRuneMesh(radius);
        GameObject runeObject = new GameObject("GroundRune");
        runeObject.transform.SetParent(transform, false);
        runeObject.AddComponent<MeshFilter>().sharedMesh = runeMesh;
        runeRenderer = runeObject.AddComponent<MeshRenderer>();
        runeRenderer.sharedMaterial = runeMaterial;
        runeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        runeRenderer.receiveShadows = false;
        SetMaterialColor(runeMaterial, effectColor);

        GameObject particleObject = new GameObject("RisingRunes");
        particleObject.transform.SetParent(transform, false);
        particles = particleObject.AddComponent<ParticleSystem>();
        // A freshly added system starts playing right away, and its duration can only be set while stopped.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.duration = duration;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
        main.startSpeed = particleRiseSpeed;
        main.startSize = new ParticleSystem.MinMaxCurve(0.075f, 0.15f);
        main.startColor = effectColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = Mathf.Max(particleCount * 3, 1);
        var emission = particles.emission;
        emission.enabled = true;
        short burstCount = (short)Mathf.Clamp(particleCount, 1, short.MaxValue);
        short followUpBurstCount = (short)Mathf.Clamp(particleCount / 2, 1, short.MaxValue);
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0.32f, burstCount),
            new ParticleSystem.Burst(0.64f, followUpBurstCount)
        });
        emission.rateOverTime = Mathf.Max(8f, particleCount * 0.45f);
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * 0.82f;
        shape.arc = 360f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient particleGradient = new Gradient();
        particleGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(effectColor, 0.35f),
                new GradientColorKey(Color.white, 0.7f),
                new GradientColorKey(effectColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(particleGradient);
        var particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = particleMaterial;
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particles.Play();
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.22f));
        float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(duration * 0.68f, duration, elapsed));
        float alpha = fadeIn * fadeOut;
        float pulse = 0.9f + 0.65f * Mathf.Sin(progress * Mathf.PI);
        transform.rotation = Quaternion.Euler(0f, elapsed * rotationSpeed, 0f);
        Color animatedColor = new Color(effectColor.r * pulse, effectColor.g * pulse,
            effectColor.b * pulse, alpha);
        SetMaterialColor(runeMaterial, animatedColor);
        transform.localScale = Vector3.one * Mathf.Lerp(0.72f, 1f, fadeIn);
        if (elapsed >= duration) Destroy(gameObject);
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty(BaseColor)) material.SetColor(BaseColor, color);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
    }

    static Material CreateTransparentMaterial(Shader shader, string materialName)
    {
        Material material = new Material(shader) { name = materialName };
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    static Mesh CreateRuneMesh(float outerRadius)
    {
        const int segments = 96;
        List<Vector3> vertices = new List<Vector3>(segments * 12);
        List<int> triangles = new List<int>(segments * 18);
        AddRing(vertices, triangles, outerRadius, outerRadius - 0.095f, segments);
        AddRing(vertices, triangles, outerRadius * 0.91f, outerRadius * 0.91f - 0.035f, segments);
        AddRing(vertices, triangles, outerRadius * 0.68f, outerRadius * 0.68f - 0.045f, segments);
        AddRing(vertices, triangles, outerRadius * 0.27f, outerRadius * 0.27f - 0.035f, segments);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            float halfWidth = 0.032f;
            float inner = outerRadius * 0.72f;
            float outer = outerRadius * 0.9f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 side = new Vector3(-direction.z, 0f, direction.x) * halfWidth;
            AddQuad(vertices, triangles, direction * inner - side, direction * outer - side,
                direction * outer + side, direction * inner + side);

            float starAngle = angle + Mathf.PI / 8f;
            Vector3 starDirection = new Vector3(Mathf.Cos(starAngle), 0f, Mathf.Sin(starAngle));
            Vector3 starSide = new Vector3(-starDirection.z, 0f, starDirection.x) * (halfWidth * 0.7f);
            AddQuad(vertices, triangles, starDirection * (outerRadius * 0.31f) - starSide,
                starDirection * (outerRadius * 0.66f) - starSide,
                starDirection * (outerRadius * 0.66f) + starSide,
                starDirection * (outerRadius * 0.31f) + starSide);
        }

        Mesh mesh = new Mesh { name = "SpawnRune_RuntimeMesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddRing(List<Vector3> vertices, List<int> triangles, float outer, float inner, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments;
            float a1 = (i + 1) * Mathf.PI * 2f / segments;
            AddQuad(vertices, triangles,
                new Vector3(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner),
                new Vector3(Mathf.Cos(a0) * outer, 0f, Mathf.Sin(a0) * outer),
                new Vector3(Mathf.Cos(a1) * outer, 0f, Mathf.Sin(a1) * outer),
                new Vector3(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner));
        }
    }

    static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int index = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 1);
        triangles.Add(index);
        triangles.Add(index + 3);
        triangles.Add(index + 2);
    }

    void OnDestroy()
    {
        if (Active.TryGetValue(effectSlot, out SpawnEffectController active) && active == this)
            Active.Remove(effectSlot);
        if (runeMaterial != null) Destroy(runeMaterial);
        if (particleMaterial != null) Destroy(particleMaterial);
        if (runeMesh != null) Destroy(runeMesh);
    }
}
