// 온라인 공격의 연출 전달과 로컬 발사를 연결한다. 없으면 오프라인으로 공격한다.
// Presence of an authority selects the online path exclusively, even if the
// equipped weapon does not implement IWeapon yet. Non-charging attacks pass 0.
public interface IAttackAuthority
{
    void StartAttack(float chargeRatio);
    // Execute the weapon captured by PlayerAttack at attack start, owner only.
    void FireMagic(float chargeRatio);
}
