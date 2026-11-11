using UnityEngine;

public static class WeaponHitEffect
{
    public static void Spawn(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;

        // Keep the effect independent of the projectile and a defeated target.
        GameObject effect = Object.Instantiate(prefab, position, prefab.transform.rotation);
        float lifetime = 0f;
        foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            lifetime = Mathf.Max(lifetime, main.startDelay.constantMax + main.duration
                + main.startLifetime.constantMax);
            particles.Play(false);
        }
        Object.Destroy(effect, Mathf.Max(.1f, lifetime) + .1f);
    }

    /// <summary>Spawns the effect facing the shot direction on the ground plane.</summary>
    public static void Spawn(GameObject prefab, Vector3 position, Vector3 direction)
    {
        if (prefab == null) return;

        Vector3 flat = direction;
        flat.y = 0f;
        Quaternion rotation = flat.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flat.normalized)
            : prefab.transform.rotation;
        GameObject effect = Object.Instantiate(prefab, position, rotation);
        float lifetime = 0f;
        foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            lifetime = Mathf.Max(lifetime, main.startDelay.constantMax + main.duration
                + main.startLifetime.constantMax);
            particles.Play(false);
        }
        Object.Destroy(effect, Mathf.Max(.1f, lifetime) + .1f);
    }
}
