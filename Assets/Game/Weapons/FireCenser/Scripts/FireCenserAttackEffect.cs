using UnityEngine;

public static class FireCenserAttackEffect
{
    const float BaseParticleLifetime = 0.55f;
    const float PlaybackSpeed = 1.3f;
    const float ParticleDensityPerMeter = 12f;
    const int MinimumParticleCount = 16;
    const int MaximumParticleCount = 128;
    const int RadialLayers = 5;
    const float MinimumSpeedFactor = 0.2f;
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
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.maxParticles = Mathf.Max(main.maxParticles, MaximumParticleCount * RadialLayers);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

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

        ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.velocityScale = 0.02f;
        particleRenderer.lengthScale = 2f;

        particles.Play(false);

        float halfAngle = Mathf.Clamp(attackAngle, 0f, 180f) * 0.5f;
        float arcLength = range * halfAngle * 2f * Mathf.Deg2Rad;
        int particleCount = halfAngle <= 0f
            ? 1
            : Mathf.Clamp(
                Mathf.CeilToInt(arcLength * ParticleDensityPerMeter),
                MinimumParticleCount,
                MaximumParticleCount);
        float horizontalSpeed = range / ParticleLifetime;

        for (int i = 0; i < particleCount; i++)
        {
            float t = particleCount == 1 ? 0.5f : (float)i / (particleCount - 1);
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 particleDirection = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            for (int layer = 0; layer < RadialLayers; layer++)
            {
                float layerT = (float)layer / (RadialLayers - 1);
                float speedFactor = Mathf.Lerp(MinimumSpeedFactor, 1f, layerT);
                var emit = new ParticleSystem.EmitParams
                {
                    position = origin + Vector3.up * SpawnHeight,
                    velocity = particleDirection * (horizontalSpeed * speedFactor) + Vector3.up * 0.35f,
                    startLifetime = ParticleLifetime,
                    applyShapeToPosition = false,
                };
                particles.Emit(emit, 1);
            }
        }

        Object.Destroy(effect, ParticleLifetime + 0.25f);
        return effect;
    }
}
