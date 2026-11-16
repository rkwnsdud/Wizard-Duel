#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;

// 네트워크 없이 같은 발사·투사체 경로를 검증할 때만 피해 반영을 로컬로 바꾼다.
public sealed class LocalShotSink : IShotSink
{
    readonly IWeapon weapon;
    readonly Dictionary<PlayerHealth, DuelCombatState> targets = new Dictionary<PlayerHealth, DuelCombatState>();
    public LocalShotSink(IWeapon weapon = null) => this.weapon = weapon;
    public void Broadcast(ShotEvent shot) => weapon?.Present(shot);
    public void Hit(AttackInfo attack, int hitIndex, PlayerHealth target, float damage)
    {
        if (!targets.TryGetValue(target, out DuelCombatState combat))
        {
            combat = new DuelCombatState(1, target.CurrentHealth);
            targets.Add(target, combat);
        }
        // 테스트가 만드는 공격과 대상은 같은 판의 등록된 공격으로 취급한다.
        var request = new ServerHitReport.Request
        {
            AttackerCombatEnabled = true,
            HasTarget = true,
            SameMatch = true,
            AttackerHeldWeapon = true,
            KnownStartedAttack = true,
            AttackerSlot = 0,
            AttackId = attack.Id,
            WeaponId = attack.WeaponId,
            HitIndex = hitIndex,
            Damage = damage,
            TargetCombat = combat,
        };
        if (ServerHitReport.TryApply(in request, out _) == HitReportDecision.Accepted)
            target.ApplyServerHealth(combat.Health);
    }
}
#endif
