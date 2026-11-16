using UnityEngine;

[ExecuteAlways]
public sealed class LightOrbSpin : MonoBehaviour, IWeaponDisplayAnimation
{
    static readonly System.Collections.Generic.HashSet<LightOrbSpin> active = new System.Collections.Generic.HashSet<LightOrbSpin>();
    public static int ActiveCount => active.Count;
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);
    [SerializeField] private Vector3 degreesPerSecond = new Vector3(0f, 16f, 6f);
    private void Update() { if (Application.isPlaying) transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self); }
}
