// 공격 시작과 발사를 권한 담당자에게 전달한다.
public interface IAttackAuthority
{
    // attack은 PlayerAttack이 공격을 시작할 때 정한 번호와 무기 id다.
    void StartAttack(AttackInfo attack, float chargeRatio);
    // Execute the weapon captured by PlayerAttack at attack start, owner only.
    // attack은 StartAttack에 넘긴 것과 같은 공격이다.
    void FireMagic(AttackInfo attack, float chargeRatio);
}
