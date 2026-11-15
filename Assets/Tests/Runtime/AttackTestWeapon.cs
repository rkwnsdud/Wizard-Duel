#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// 공격 이벤트 테스트(AttackEventTests)용 날아가는 공격 대역. 쏠 때 받은 공격 정보와 싱크를 들고 있다가
// Land에서 명중 연출과 명중 보고를 보낸다. 받은 연출은 Presented에 쌓기만 한다.
// 게임 오브젝트에 붙여야 해서 Editor 폴더 밖에 두고, 에디터에서만 컴파일한다(빌드에는 들어가지 않는다).
public sealed class AttackTestWeapon : WeaponBase, IWeapon, IWeaponHitPassive
{
    public const byte LaunchKind = 0;
    public const byte LandKind = 1;

    public readonly List<ShotEvent> Presented = new List<ShotEvent>();
    // 서버가 이 무기의 적중 효과를 건 횟수.
    public int ServerHits { get; private set; }
    public AttackInfo Fired { get; private set; }
    // 공격 모션 동안 켜지는 예고(향로의 부채꼴 같은 것)를 흉내 낸다.
    public bool Winding { get; private set; }
    IShotSink sink;

    public override void OnAttackStarted() => Winding = true;
    public override void OnAttackFinished() => Winding = false;

    public void Fire(AttackInfo attack, Vector3 direction, float charge, IShotSink shotSink)
    {
        Fired = attack;
        sink = shotSink;
        sink.Broadcast(new ShotEvent(attack, LaunchKind, transform.position, transform.position + direction, charge));
    }

    public void Land(PlayerHealth target, float damage = 10f)
    {
        sink.Broadcast(new ShotEvent(Fired, LandKind, transform.position, target.transform.position, 0f));
        sink.Hit(Fired, 0, target, damage);
    }

    public void Present(ShotEvent shot) => Presented.Add(shot);
    public void OnServerHit(DuelPlayer attacker, DuelPlayer target) => ServerHits++;
}
#endif
