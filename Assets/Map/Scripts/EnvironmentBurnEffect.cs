using UnityEngine;

public static class EnvironmentBurnEffect
{
    public static void Spawn(Vector3 position)
    {
        var root = new GameObject("Bush Burn");
        root.transform.position = position;
        var particles = root.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = .9f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(.22f, .42f);
        main.startColor = Color.white;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -.08f;
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30, 42) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = .28f;
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, .95f, .25f), 0f),
                new GradientColorKey(new Color(1f, .28f, .025f), .48f),
                new GradientColorKey(new Color(.22f, .055f, .025f), 1f)
            },
            new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(.9f, .5f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, .7f), new Keyframe(.22f, 1.15f), new Keyframe(1f, .05f)));
        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader != null) renderer.material = new Material(shader);
        particles.Play();
        Object.Destroy(root, 2f);
    }
}
