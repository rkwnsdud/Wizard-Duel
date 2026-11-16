// 공격이 닿는 거리와 날아가는 빠르기를 알려 주는 무기가 구현한다. AI 대전의 봇(DuelBot)이 다가갈 거리와 쏠 때, 겨눌 곳을 정할 때 본다.
// 구현하지 않은 무기는 봇이 기본 사거리(DuelBotTuning.fallbackReach)로, 즉시 닿는 공격처럼 쏜다.
public interface IWeaponReach
{
    // 발사 지점에서 공격이 닿는 가장 먼 거리. charge는 0~1이고 차징하지 않는 무기는 0만 받는다.
    float ReachAt(float charge);

    // 발사체가 날아가는 빠르기(m/s). 쏘는 순간 닿는 공격은 0이다.
    float ShotSpeed { get; }
}
