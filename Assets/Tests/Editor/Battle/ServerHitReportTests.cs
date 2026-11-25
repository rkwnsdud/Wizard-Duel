#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class ServerHitReportTests
{
    static ServerHitReport.Request WeaponHit(DuelCombatState target, uint attackId = 1, byte weaponId = 1,
        int hitIndex = 0, float damage = 10f, int attackerSlot = 0, bool combatEnabled = true,
        bool attackerRoundEnded = false, bool targetRoundEnded = false, bool hasTarget = true,
        bool sameMatch = true, bool heldWeapon = true, bool startedAttack = true) => new ServerHitReport.Request
    {
        IsStatusTick = false,
        AttackerCombatEnabled = combatEnabled,
        AttackerRoundEnded = attackerRoundEnded,
        TargetRoundEnded = targetRoundEnded,
        HasTarget = hasTarget,
        SameMatch = sameMatch,
        AttackerHeldWeapon = heldWeapon,
        KnownStartedAttack = startedAttack,
        AttackerSlot = attackerSlot,
        AttackId = attackId,
        WeaponId = weaponId,
        HitIndex = hitIndex,
        Damage = damage,
        TargetCombat = target,
    };

    static ServerHitReport.Request StatusTick(DuelCombatState target, int tickIndex = 0, float damage = 6f,
        int attackerSlot = 0, bool targetRoundEnded = false, bool hasTarget = true,
        bool attackerCombatEnabled = false, bool sameMatch = false) => new ServerHitReport.Request
    {
        IsStatusTick = true,
        AttackerCombatEnabled = attackerCombatEnabled,
        TargetRoundEnded = targetRoundEnded,
        HasTarget = hasTarget,
        SameMatch = sameMatch,
        AttackerSlot = attackerSlot,
        AttackId = StatusEffectState.DotAttackIdFlag | 1u,
        HitIndex = tickIndex,
        Damage = damage,
        TargetCombat = target,
    };

    [Test]
    public void Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject()
    {
        var target = new DuelCombatState(1, 100f);

        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackerRoundEnded: true), out _),
            Is.EqualTo(HitReportDecision.RoundEnded));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, targetRoundEnded: true), out _),
            Is.EqualTo(HitReportDecision.RoundEnded));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, combatEnabled: false), out _),
            Is.EqualTo(HitReportDecision.CombatNotStarted));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, hasTarget: false), out _),
            Is.EqualTo(HitReportDecision.MissingTarget));
        Assert.That(ServerHitReport.TryApply(WeaponHit(null, hasTarget: true), out _),
            Is.EqualTo(HitReportDecision.MissingTarget));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, sameMatch: false), out _),
            Is.EqualTo(HitReportDecision.DifferentMatch));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, attackerSlot: 1), out _),
            Is.EqualTo(HitReportDecision.SelfHit));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, damage: 0f), out _),
            Is.EqualTo(HitReportDecision.InvalidDamage));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, damage: -5f), out _),
            Is.EqualTo(HitReportDecision.InvalidDamage));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, damage: float.NaN), out _),
            Is.EqualTo(HitReportDecision.InvalidDamage));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, damage: float.PositiveInfinity), out _),
            Is.EqualTo(HitReportDecision.InvalidDamage));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, heldWeapon: false), out _),
            Is.EqualTo(HitReportDecision.WeaponNotHeld));
        Assert.That(ServerHitReport.TryApply(WeaponHit(target, startedAttack: false), out _),
            Is.EqualTo(HitReportDecision.UnknownAttack));

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
        Assert.That(target.Health, Is.EqualTo(0f));
    }

    [Test]
    public void WeaponHit_AcceptsDelayedProjectileAfterWeaponSwap_AndMultiVictimOrMultiTickHits()
    {
        var history = new ServerAttackHistory();
        history.RecordWeapon(1);
        var delayedAttack = new AttackInfo(10, 1);
        history.RecordAttack(delayedAttack, 0f);

        // 공격자가 다른 무기로 바꿔 쏜 뒤에도, 먼저 쏜 무기의 늦은 명중(예: 폭발 화염 공)은 받아야 한다.
        history.RecordWeapon(2);
        var areaAttack = new AttackInfo(11, 2);
        history.RecordAttack(areaAttack, 0f);

        var targetA = new DuelCombatState(1, 100f);
        var targetB = new DuelCombatState(2, 100f);

        Assert.That(ServerHitReport.TryApply(WeaponHit(
            targetA, delayedAttack.Id, delayedAttack.WeaponId, hitIndex: 0, damage: 25f,
            heldWeapon: history.HasHeldWeapon(delayedAttack.WeaponId),
            startedAttack: history.HasStartedAttack(delayedAttack)), out _),
            Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(targetA.Health, Is.EqualTo(75f));

        // 범위 공격이 여러 대상에 각각 다른 hitIndex로 닿고, 같은 대상에도 다단 히트로 닿는 경우
        Assert.That(ServerHitReport.TryApply(WeaponHit(
            targetA, areaAttack.Id, areaAttack.WeaponId, hitIndex: 0, damage: 15f,
            heldWeapon: history.HasHeldWeapon(areaAttack.WeaponId),
            startedAttack: history.HasStartedAttack(areaAttack)), out _),
            Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(ServerHitReport.TryApply(WeaponHit(
            targetB, areaAttack.Id, areaAttack.WeaponId, hitIndex: 1, damage: 15f,
            heldWeapon: history.HasHeldWeapon(areaAttack.WeaponId),
            startedAttack: history.HasStartedAttack(areaAttack)), out _),
            Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(ServerHitReport.TryApply(WeaponHit(
            targetA, areaAttack.Id, areaAttack.WeaponId, hitIndex: 2, damage: 15f,
            heldWeapon: history.HasHeldWeapon(areaAttack.WeaponId),
            startedAttack: history.HasStartedAttack(areaAttack)), out _),
            Is.EqualTo(HitReportDecision.Accepted));

        Assert.That(targetA.Health, Is.EqualTo(45f));
        Assert.That(targetB.Health, Is.EqualTo(85f));
    }

    [Test]
    public void StatusTick_IgnoresAttackerCombatAndMatch_RejectsRoundEndedDuplicatesAndDefeated()
    {
        var target = new DuelCombatState(1, 10f);
        var tick = StatusTick(target, tickIndex: 0, damage: 6f, attackerCombatEnabled: false, sameMatch: false);

        Assert.That(ServerHitReport.TryApply(in tick, out bool defeated), Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(defeated, Is.False);
        Assert.That(target.Health, Is.EqualTo(4f));

        Assert.That(ServerHitReport.TryApply(in tick, out _), Is.EqualTo(HitReportDecision.DuplicateHit));

        tick.HitIndex = 1;
        tick.TargetRoundEnded = true;
        Assert.That(ServerHitReport.TryApply(in tick, out _), Is.EqualTo(HitReportDecision.RoundEnded));
        Assert.That(target.Health, Is.EqualTo(4f));

        tick.TargetRoundEnded = false;
        Assert.That(ServerHitReport.TryApply(in tick, out defeated), Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(defeated, Is.True);
        Assert.That(target.Health, Is.EqualTo(0f));

        tick.HitIndex = 2;
        Assert.That(ServerHitReport.TryApply(in tick, out _), Is.EqualTo(HitReportDecision.TargetAlreadyDefeated));
    }

    [Test]
    public void SimultaneousLastHits_FirstProcessedWinsAndLaterReportIsDropped()
    {
        var playerA = new DuelCombatState(0, 30f);
        var playerB = new DuelCombatState(1, 30f);

        // 먼저 서버에 도착한 A의 마지막 일격은 받아들여 B가 쓰러지고 판이 끝난다.
        Assert.That(ServerHitReport.TryApply(WeaponHit(
            playerB, attackId: 1, weaponId: 1, hitIndex: 0, damage: 30f, attackerSlot: 0,
            combatEnabled: true, attackerRoundEnded: false, targetRoundEnded: false), out bool bDefeated),
            Is.EqualTo(HitReportDecision.Accepted));
        Assert.That(bDefeated, Is.True);
        Assert.That(playerB.IsDefeated, Is.True);

        // 직후에 도착한 B의 마지막 일격은 판이 끝난 뒤 보고이므로 버린다.
        Assert.That(ServerHitReport.TryApply(WeaponHit(
            playerA, attackId: 1, weaponId: 1, hitIndex: 0, damage: 30f, attackerSlot: 1,
            combatEnabled: false, attackerRoundEnded: true, targetRoundEnded: true), out bool aDefeated),
            Is.EqualTo(HitReportDecision.RoundEnded));
        Assert.That(aDefeated, Is.False);
        Assert.That(playerA.IsDefeated, Is.False);
        Assert.That(playerA.Health, Is.EqualTo(30f));
    }

    [Test]
    public void ServerAttackHistory_TracksRoundWeaponsAndAttacks_RejectsUnknownOrMismatched_AndClears()
    {
        var history = new ServerAttackHistory();
        history.RecordWeapon(0);
        Assert.That(history.HasHeldWeapon(0), Is.False);

        history.RecordAttack(new AttackInfo(0, 1), 1f);
        history.RecordAttack(new AttackInfo(1, 0), 1f);
        Assert.That(history.HasStartedAttack(new AttackInfo(0, 1)), Is.False);
        Assert.That(history.HasStartedAttack(new AttackInfo(1, 0)), Is.False);

        history.RecordWeapon(2);
        history.RecordWeapon(4);
        Assert.That(history.HasHeldWeapon(2), Is.True);
        Assert.That(history.HasHeldWeapon(4), Is.True);
        Assert.That(history.HasHeldWeapon(3), Is.False);

        var attack = new AttackInfo(7, 2);
        history.RecordAttack(attack, 0.9999f);
        history.RecordAttack(new AttackInfo(8, 4), 1f);
        Assert.That(history.HasStartedAttack(attack), Is.True);
        Assert.That(history.HasStartedAttack(new AttackInfo(7, 4)), Is.False);
        Assert.That(history.HasStartedAttack(new AttackInfo(8, 2)), Is.False);
        Assert.That(history.TryGetChargeRatio(attack, out float partialCharge), Is.True);
        Assert.That(partialCharge, Is.EqualTo(0.9999f));
        Assert.That(history.TryGetChargeRatio(new AttackInfo(8, 4), out float fullCharge), Is.True);
        Assert.That(fullCharge, Is.EqualTo(1f));

        history.Clear();
        Assert.That(history.HasHeldWeapon(2), Is.False);
        Assert.That(history.HasStartedAttack(attack), Is.False);
    }

    [Test]
    public void FormatDropLog_ReturnsSingleLineWithReasonAndAttackIdentity()
    {
        string hitLog = ServerHitReport.FormatDropLog(
            false, HitReportDecision.RoundEnded, attackerSlot: 0, targetLabel: "1",
            attackId: 12, weaponId: 2, hitIndex: 0, damage: 25f);
        Assert.That(hitLog, Is.EqualTo(
            "[Combat] Hit dropped reason=RoundEnded attacker=0 target=1 attack=12 weapon=2 hit=0 damage=25.000"));
        Assert.That(hitLog, Does.Not.Contain("\n").And.Not.Contain("\r"));

        string dotLog = ServerHitReport.FormatDropLog(
            true, HitReportDecision.TargetAlreadyDefeated, attackerSlot: 0, targetLabel: "missing(netId=42)",
            attackId: StatusEffectState.DotAttackIdFlag | 3u, weaponId: 0, hitIndex: 1, damage: 5f);
        Assert.That(dotLog, Is.EqualTo(
            $"[Combat] DoT dropped reason=TargetAlreadyDefeated attacker=0 target=missing(netId=42) " +
            $"attack={StatusEffectState.DotAttackIdFlag | 3u} weapon=0 hit=1 damage=5.000"));
        Assert.That(dotLog, Does.Not.Contain("\n").And.Not.Contain("\r"));
    }
}
#endif
