using UnityEngine;

public static class FireCenserAttackEffect
{
    const float BaseParticleLifetime = 0.54f;
    const float PlaybackSpeed = 1.2f;
    const float ParticleDensityPerMeter = 22f;
    const int MinimumParticleCount = 32;
    const int MaximumParticleCount = 192;
    const int RadialLayers = 8;
    const float MinimumRadiusFactor = 0.14f;
    const float SpawnHeight = 0.25f;

    static float ParticleLifetime => BaseParticleLifetime / PlaybackSpeed;

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 origin,
        Vector3 direction,
        float range,
        float attackAngle)
    {
        if (prefab == null || range <= 0f) return null;

        if (prefab.GetComponent<ParticleSystem>() == null)
        {
            Debug.LogError("[FireCenserAttackEffect] The fire effect prefab needs a root ParticleSystem.", prefab);
            return null;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;
        else
            direction.Normalize();

        GameObject effect = Object.Instantiate(prefab, origin, Quaternion.identity);
        ParticleSystem particles = effect.GetComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = ParticleLifetime;
        main.startDelay = 0f;
        main.startLifetime = ParticleLifetime;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.maxParticles = Mathf.Max(main.maxParticles, MaximumParticleCount * RadialLayers);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var flameGradient = new Gradient();
        flameGradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 1f, .78f), 0f),
                new GradientColorKey(new Color(1f, .72f, .12f), .28f),
                new GradientColorKey(new Color(1f, .2f, .025f), .72f),
                new GradientColorKey(new Color(.24f, .025f, .008f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(.95f, .62f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = flameGradient;

        var textureSheet = particles.textureSheetAnimation;
        textureSheet.enabled = true;
        textureSheet.mode = ParticleSystemAnimationMode.Grid;
        textureSheet.numTilesX = 3;
        textureSheet.numTilesY = 3;
        textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
        textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
        textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        textureSheet.cycleCount = 1;

        var sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        var shape = particles.shape;
        shape.enabled = false;
        var emission = particles.emission;
        emission.enabled = false;
        var limitVelocity = particles.limitVelocityOverLifetime;
        limitVelocity.enabled = false;
        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(.18f, .42f);
        noise.frequency = .55f;
        noise.scrollSpeed = .8f;
        noise.damping = true;
        var rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-220f, 220f);

        ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.velocityScale = 0.05f;
        particleRenderer.lengthScale = 3.5f;

        particles.Play(false);

        float halfAngle = Mathf.Clamp(attackAngle, 0f, 180f) * 0.5f;
        float arcLength = range * halfAngle * 2f * Mathf.Deg2Rad;
        int particleCount = halfAngle <= 0f
            ? 1
            : Mathf.Clamp(
                Mathf.CeilToInt(arcLength * ParticleDensityPerMeter),
                MinimumParticleCount,
                MaximumParticleCount);
        for (int i = 0; i < particleCount; i++)
        {
            float t = particleCount == 1 ? 0.5f : (float)i / (particleCount - 1);
            float baseYaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            for (int layer = 0; layer < RadialLayers; layer++)
            {
                float layerT = (float)layer / (RadialLayers - 1);
                float sway = Mathf.Sin(t * Mathf.PI * 4f + layerT * Mathf.PI) * 5f * Mathf.Sin(t * Mathf.PI);
                float yaw = Mathf.Clamp(baseYaw + sway, -halfAngle, halfAngle);
                Vector3 particleDirection = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
                Vector3 tangent = Vector3.Cross(Vector3.up, particleDirection).normalized;
                float swirl = Mathf.Sin(t * Mathf.PI * 6f + layerT * Mathf.PI) * Mathf.Lerp(.25f, .8f, layerT);
                float radius = range * Mathf.Lerp(MinimumRadiusFactor, 1f, layerT);
                var emit = new ParticleSystem.EmitParams
                {
                    // Fill the whole fan on the impact frame; the particles flare and curl in place.
                    position = origin + Vector3.up * SpawnHeight + particleDirection * radius,
                    velocity = Vector3.up * Mathf.Lerp(.8f, 2f, layerT) + tangent * swirl,
                    startLifetime = ParticleLifetime,
                    startSize = Mathf.Lerp(.42f, .9f, layerT),
                    applyShapeToPosition = false,
                };
                particles.Emit(emit, 1);
            }
        }

        Object.Destroy(effect, ParticleLifetime + 0.25f);
        return effect;
    }
}
