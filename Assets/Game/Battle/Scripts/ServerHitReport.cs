using System.Collections.Generic;

// 서버가 명중 보고와 상태이상 틱을 받을지 버릴지 판정하는 한 곳.
// 받는 보고와 버리는 보고의 전체 규칙·로그·예외는 Docs/ServerHitReportRules.md를 따른다.
public enum HitReportDecision
{
    Accepted = 0,
    RoundEnded,
    CombatNotStarted,
    MissingTarget,
    DifferentMatch,
    SelfHit,
    TargetAlreadyDefeated,
    InvalidDamage,
    WeaponNotHeld,
    UnknownAttack,
    DuplicateHit,
}

// 서버가 플레이어마다 이번 판에 든 적 있는 무기와 시작한 공격을 기록해 둔다.
public sealed class ServerAttackHistory
{
    readonly HashSet<byte> heldWeapons = new HashSet<byte>();
    readonly Dictionary<uint, (byte WeaponId, float ChargeRatio)> startedAttacks =
        new Dictionary<uint, (byte, float)>();

    public void RecordWeapon(byte weaponId)
    {
        if (weaponId != 0) heldWeapons.Add(weaponId);
    }

    public void RecordAttack(AttackInfo attack, float chargeRatio)
    {
        if (attack.IsValid) startedAttacks[attack.Id] = (attack.WeaponId, chargeRatio);
    }

    public bool HasHeldWeapon(byte weaponId) => weaponId != 0 && heldWeapons.Contains(weaponId);

    public bool HasStartedAttack(AttackInfo attack) => TryGetChargeRatio(attack, out _);

    public bool TryGetChargeRatio(AttackInfo attack, out float chargeRatio)
    {
        chargeRatio = 0f;
        if (!attack.IsValid || !startedAttacks.TryGetValue(attack.Id, out var started) ||
            started.WeaponId != attack.WeaponId) return false;
        chargeRatio = started.ChargeRatio;
        return true;
    }

    public void Clear()
    {
        heldWeapons.Clear();
        startedAttacks.Clear();
    }
}

public static class ServerHitReport
{
    public struct Request
    {
        public bool IsStatusTick;
        public bool AttackerCombatEnabled;
        public bool AttackerRoundEnded;
        public bool TargetRoundEnded;
        public bool HasTarget;
        public bool SameMatch;
        public bool AttackerHeldWeapon;
        public bool KnownStartedAttack;
        public int AttackerSlot;
        public uint AttackId;
        public byte WeaponId;
        public int HitIndex;
        public float Damage;
        public DuelCombatState TargetCombat;
    }

    public static HitReportDecision Evaluate(in Request request)
    {
        if (!request.IsStatusTick)
        {
            if (request.AttackerRoundEnded) return HitReportDecision.RoundEnded;
            if (!request.AttackerCombatEnabled) return HitReportDecision.CombatNotStarted;
        }
        else
        {
            if (request.TargetRoundEnded) return HitReportDecision.RoundEnded;
        }

        if (!request.HasTarget || request.TargetCombat == null)
            return HitReportDecision.MissingTarget;

        if (!request.IsStatusTick)
        {
            if (!request.SameMatch) return HitReportDecision.DifferentMatch;
            if (request.TargetRoundEnded) return HitReportDecision.RoundEnded;
        }

        if (request.AttackerSlot == request.TargetCombat.Slot)
            return HitReportDecision.SelfHit;

        if (request.TargetCombat.IsDefeated)
            return HitReportDecision.TargetAlreadyDefeated;

        if (!IsFinite(request.Damage) || request.Damage <= 0f)
            return HitReportDecision.InvalidDamage;

        if (!request.IsStatusTick)
        {
            if (!request.AttackerHeldWeapon) return HitReportDecision.WeaponNotHeld;
            if (!request.KnownStartedAttack) return HitReportDecision.UnknownAttack;
        }

        if (request.TargetCombat.HasHit(request.AttackerSlot, request.AttackId, request.HitIndex))
            return HitReportDecision.DuplicateHit;

        return HitReportDecision.Accepted;
    }

    public static HitReportDecision TryApply(in Request request, out bool defeatedNow)
    {
        defeatedNow = false;
        HitReportDecision decision = Evaluate(in request);
        if (decision != HitReportDecision.Accepted) return decision;
        request.TargetCombat.ApplyAcceptedHit(
            request.AttackerSlot, request.AttackId, request.HitIndex, request.Damage, out defeatedNow);
        return HitReportDecision.Accepted;
    }

    public static string FormatDropLog(bool isStatusTick, HitReportDecision decision, int attackerSlot,
        string targetLabel, uint attackId, byte weaponId, int hitIndex, float damage)
    {
        string kind = isStatusTick ? "DoT" : "Hit";
        return $"[Combat] {kind} dropped reason={decision} attacker={attackerSlot} target={targetLabel} " +
            $"attack={attackId} weapon={weaponId} hit={hitIndex} damage={damage:F3}";
    }

    internal static HitReportDecision TryApplyTargetHit(
        DuelCombatState target, int attackerSlot, uint attackId, int hitIndex, float damage, out bool defeatedNow)
    {
        var request = new Request
        {
            IsStatusTick = true,
            HasTarget = target != null,
            AttackerSlot = attackerSlot,
            AttackId = attackId,
            HitIndex = hitIndex,
            Damage = damage,
            TargetCombat = target,
        };
        return TryApply(in request, out defeatedNow);
    }

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
