using Unity.Netcode;
using UnityEngine;

// Presentation only: the server snapshot owns the lifetime and pickup permission.
public sealed class FloatingWeaponPickup : MonoBehaviour
{
    NetworkManager manager;
    Vector3 ground;
    double born;
    float rotationSpeed;
    float floatHeight = Height;
    const float Height = 1.2f;

    public void Initialize(NetworkManager network, WeaponDefinition entry, Vector3 position,
        double spawnTime, float degreesPerSecond = 30f)
    {
        manager = network;
        ground = position;
        born = spawnTime;
        rotationSpeed = degreesPerSecond;
        // Disable attacks, poses, audio and effects before the display is activated.
        gameObject.SetActive(false);
        var model = Instantiate(entry.prefab.gameObject, transform, false);
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var component in model.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(component is LightOrbSpin)) component.enabled = false;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in model.GetComponentsInChildren<Rigidbody>(true))
        { body.isKinematic = true; body.detectCollisions = false; }
        foreach (var audio in model.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        foreach (var light in model.GetComponentsInChildren<Light>(true)) light.enabled = false;
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var particle in model.GetComponentsInChildren<ParticleSystem>(true)) particle.gameObject.SetActive(false);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) renderer.enabled = false;
        model.SetActive(true);
        // Renderer bounds are valid after activation; gameplay scripts stay disabled.
        gameObject.SetActive(true);
        Bounds bounds = ModelBounds(model);
        if (entry.alignLongestAxis)
        {
            Vector3 axis = bounds.size.x > bounds.size.y && bounds.size.x > bounds.size.z ? Vector3.right :
                bounds.size.z > bounds.size.y ? Vector3.forward : Vector3.up;
            model.transform.rotation = Quaternion.FromToRotation(axis, Vector3.up);
        }
        model.transform.rotation = Quaternion.Euler(entry.displayRotation) * model.transform.rotation;
        bounds = ModelBounds(model);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest > .001f) model.transform.localScale *= entry.displaySize / largest;
        bounds = ModelBounds(model);
        // Keep enlarged, tall pickups clear of the floor even at the bottom of the bob.
        floatHeight = Mathf.Max(Height, bounds.extents.y + .2f);
        model.transform.position -= bounds.center - transform.position;
        Update();
    }

    static Bounds ModelBounds(GameObject model)
    {
        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        bool first = true;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
            if (first) { bounds = renderer.bounds; first = false; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    void Update()
    {
        if (manager == null || !manager.IsListening) return;
        double age = System.Math.Max(0, manager.ServerTime.Time - born);
        transform.position = ground + Vector3.up * (floatHeight + .08f * Mathf.Sin((float)(age % 4) * Mathf.PI * .5f));
        transform.rotation = Quaternion.Euler(0, (float)(age * rotationSpeed % 360), 0);
    }
}
