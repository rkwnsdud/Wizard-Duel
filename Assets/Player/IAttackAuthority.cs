// 공격 시작과 발사를 권한 담당자에게 전달한다.
public interface IAttackAuthority
{
    void StartAttack(float chargeRatio);
    // Execute the weapon captured by PlayerAttack at attack start, owner only.
    void FireMagic(float chargeRatio);
}
