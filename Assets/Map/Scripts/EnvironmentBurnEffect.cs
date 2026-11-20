using UnityEngine;

public static class EnvironmentBurnEffect
{
    public static void Spawn(Vector3 position)
    {
        var root = new GameObject("Bush Burn");
        root.transform.position = position;
        var particles = root.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 1.15f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(.32f, .58f);
        main.startColor = Color.white;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -.12f;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 52, 68),
            new ParticleSystem.Burst(.16f, 28, 40)
        });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 24f;
        shape.radius = .4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.alignToDirection = true;
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 1f, .72f), 0f),
                new GradientColorKey(new Color(1f, .72f, .08f), .2f),
                new GradientColorKey(new Color(1f, .19f, .015f), .65f),
                new GradientColorKey(new Color(.2f, .035f, .012f), 1f)
            },
            new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, .5f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .65f), new Keyframe(.2f, 1.2f), new Keyframe(1f, .04f)));
        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null) renderer.material = new Material(shader);
        particles.Play();
        Object.Destroy(root, 2.5f);
    }
}
