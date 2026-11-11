using UnityEngine;

// 연출용 화염 공. 충돌 판정 없이 시작점에서 끝점(벽 또는 사거리 끝)까지 날아간다.
// 폭발 신호가 오면 Finish로 꺼지고, 신호가 오지 않으면 끝점에서 잠시 기다린 뒤 스스로 꺼진다.
public sealed class ExplosionFireballVisual : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Seconds to wait at the end point for the explosion signal.")]
    private float lingerAtEnd = 0.4f;
    [SerializeField, Min(0f), Tooltip("Seconds trails and embers keep fading after the fireball stops.")]
    private float fadeTime = 0.6f;
    [SerializeField, Tooltip("Renderers hidden immediately when the fireball stops (core, glow).")]
    private Renderer[] coreRenderers;
    [SerializeField] private Light glow;

    private Vector3 end;
    private float speed;
    private float arrivedAt = -1f;
    private bool finished;

    public bool Finished => finished;

    public void Launch(Vector3 start, Vector3 end, float speed)
    {
        transform.position = start;
        this.end = end;
        this.speed = speed;
        Vector3 path = end - start;
        if (path.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(path, Vector3.up);
    }

    private void Update()
    {
        if (finished) return;
        transform.position = Vector3.MoveTowards(transform.position, end, speed * Time.deltaTime);
        if ((transform.position - end).sqrMagnitude > 0.000001f) return;
        if (arrivedAt < 0f) arrivedAt = Time.time;
        else if (Time.time - arrivedAt >= lingerAtEnd) Finish();
    }

    public void Finish()
    {
        if (finished) return;
        finished = true;
        foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>())
            particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (TrailRenderer trail in GetComponentsInChildren<TrailRenderer>()) trail.emitting = false;
        if (coreRenderers != null)
            foreach (Renderer core in coreRenderers)
                if (core != null) core.enabled = false;
        if (glow != null) glow.enabled = false;
        Destroy(gameObject, fadeTime);
    }
}
