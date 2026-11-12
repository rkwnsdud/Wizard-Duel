
using UnityEngine;

public class BasicWandWeapon : WeaponBase, IWeapon, IWeaponReach
{
    [SerializeField] private ArcaneBoltProjectile projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireSound;

    private bool presentingOwnShot;

    public float ReachAt(float charge) => projectilePrefab != null ? projectilePrefab.MaxRange : 0f;
    public float ShotSpeed => projectilePrefab != null ? projectilePrefab.Speed : 0f;

    void IWeapon.Fire(uint attackId, Vector3 direction, float charge, IShotSink sink)
    {
        if (sink == null || projectilePrefab == null || firePoint == null) return;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.root.forward;
        direction.Normalize();
        Vector3 start = firePoint.position;
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, start, Quaternion.LookRotation(direction));
        bolt.InitializeOnline(GetComponentInParent<PlayerHealth>(), sink, attackId);
        presentingOwnShot = true;
        try { sink.Broadcast(new ShotEvent(attackId, 0, start, start + direction, charge)); }
        finally { presentingOwnShot = false; }
    }

    public void Present(ShotEvent shot)
    {
        if (shot.Kind != 0) return;
        if (audioSource != null && fireSound != null) audioSource.PlayOneShot(fireSound);
        if (presentingOwnShot || projectilePrefab == null) return;
        Vector3 direction = shot.Point - shot.Start;
        if (direction.sqrMagnitude < 0.0001f) return;
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, shot.Start, Quaternion.LookRotation(direction));
        bolt.InitializeVisualOnly(GetComponentInParent<PlayerHealth>());
    }

    public override void Fire()
    {
        PlayerHealth owner = GetComponentInParent<PlayerHealth>();
        PlayerAim aim = GetComponentInParent<PlayerAim>();
        Vector3 direction = Vector3.ProjectOnPlane(aim != null ? aim.transform.forward : transform.root.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        ArcaneBoltProjectile bolt = Instantiate(projectilePrefab, firePoint.position, Quaternion.LookRotation(direction));
        bolt.Initialize(owner);
        if (audioSource != null && fireSound != null)
            audioSource.PlayOneShot(fireSound);
    }
}
