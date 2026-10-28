// 온라인 공격의 연출 전달과 로컬 발사를 연결한다. 없으면 오프라인으로 공격한다.
public interface IAttackAuthority
{
    void StartAttack(float chargeRatio);
    void FireMagic(float chargeRatio);
}
