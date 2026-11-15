#if UNITY_INCLUDE_TESTS
// 네트워크 없이 같은 발사·투사체 경로를 검증할 때만 피해 반영을 로컬로 바꾼다.
public sealed class LocalShotSink : IShotSink
{
    readonly IWeapon weapon;
    public LocalShotSink(IWeapon weapon = null) => this.weapon = weapon;
    public void Broadcast(ShotEvent shot) => weapon?.Present(shot);
    public void Hit(uint attackId, int hitIndex, PlayerHealth target, float damage) => target.TakeDamage(damage);
}
#endif
