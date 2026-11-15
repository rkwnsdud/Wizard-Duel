#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class ServerHitReportTests
{
    static ServerHitReport.Request WeaponHit(DuelCombatState target, uint attackId = 1, byte weaponId = 1,
        int hitIndex = 0, float damage = 10f, int attackerSlot = 0, bool combatEnabled = true,
        bool hasTarget = true, bool sameMatch = true) => new ServerHitReport.Request
    {
        IsStatusTick = false,
        AttackerCombatEnabled = combatEnabled,
        HasTarget = hasTarget,
        SameMatch = sameMatch,
        AttackerSlot = attackerSlot,
        AttackId = attackId,
        WeaponId = weaponId,
        HitIndex = hitIndex,
        Damage = damage,
        TargetCombat = target,
    };

    [Test]
    public void Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject()
    {
        var target = new DuelCombatState(1, 100f);

        Assert.That(ServerHitReport.TryApply(WeaponHit(target, combatEnabled: false), out _),
            Is.EqualTo(HitReportDecision.CombatInactive));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, hasTarget: false), out _),
            Is.EqualTo(HitReportDecision.MissingTarget));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, sameMatch: false), out _),
            Is.EqualTo(HitReportDecision.DifferentMatch));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackerSlot: 1), out _),
            Is.EqualTo(HitReportDecision.SelfHit));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, damage: 0f), out _),
            Is.EqualTo(HitReportDecision.InvalidDamage));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, damage: float.NaN), out _),
            Is.EqualTo(HitReportDecision.InvalidDamage));

        Assert.That(target.Health, Is.EqualTo(100f));

        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackId: 1, damage: 40f), out bool defeated),
            Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(defeated, Is.False);
        Assert.That(target.Health, Is.EqualTo(60f));

        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackId: 1, damage: 40f), out _),
            Is.EqualTo(HitReportDecision.DuplicateHit));
        Assert.That(target.Health, Is.EqualTo(60f));

        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackId: 2, damage: 70f), out defeated),
            Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(defeated, Is.True);
        Assert.That(target.Health, Is.EqualTo(0f));

        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackId: 3, damage: 10f), out _),
            Is.EqualTo(HitReportDecision.TargetAlreadyDefeated));
    }

    [Test]
    public void StatusTick_IgnoresAttackerCombatFlag_AndRejectsDuplicatesAndDefeatedTargets()
    {
        var target = new DuelCombatState(1, 10f);
        var tick = new ServerHitReport.Request
        {
            IsStatusTick = true,
            AttackerCombatEnabled = false,
            HasTarget = true,
            SameMatch = false,
            AttackerSlot = 0,
            AttackId = StatusEffectState.DotAttackIdFlag | 1u,
            HitIndex = 0,
            Damage = 6f,
            TargetCombat = target,
        };

        Assert.That(ServerHitReport.TryApply(in tick, out bool defeated), Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(defeated, Is.False);
        Assert.That(target.Health, Is.EqualTo(4f));

        Assert.That(ServerHitReport.TryApply(in tick, out _), Is.EqualTo(HitReportDecision.DuplicateHit));

        tick.HitIndex = 1;
        Assert.That(ServerHitReport.TryApply(in tick, out defeated), Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(defeated, Is.True);
        Assert.That(target.Health, Is.EqualTo(0f));

        tick.HitIndex = 2;
        Assert.That(ServerHitReport.TryApply(in tick, out _), Is.EqualTo(HitReportDecision.TargetAlreadyDefeated));
    }
}
#endif
