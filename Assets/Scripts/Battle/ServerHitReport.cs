// 서버가 명중 보고와 상태이상 틱을 받을지 버릴지 판정하는 한 곳.
public enum HitReportDecision
{
    Accepted = 0,
    CombatInactive,
    MissingTarget,
    DifferentMatch,
    SelfHit,
    TargetAlreadyDefeated,
    InvalidDamage,
    DuplicateHit,
}

public static class ServerHitReport
{
    public struct Request
    {
        public bool IsStatusTick;
        public bool AttackerCombatEnabled;
        public bool HasTarget;
        public bool SameMatch;
        public int AttackerSlot;
        public uint AttackId;
        public byte WeaponId;
        public int HitIndex;
        public float Damage;
        public DuelCombatState TargetCombat;
    }

    public static HitReportDecision Evaluate(in Request request)
    {
        if (!request.IsStatusTick && !request.AttackerCombatEnabled)
            return HitReportDecision.CombatInactive;
        if (!request.HasTarget || request.TargetCombat == null)
            return HitReportDecision.MissingTarget;
        if (!request.IsStatusTick && !request.SameMatch)
            return HitReportDecision.DifferentMatch;
        if (request.AttackerSlot == request.TargetCombat.Slot)
            return HitReportDecision.SelfHit;
        if (request.TargetCombat.IsDefeated)
            return HitReportDecision.TargetAlreadyDefeated;
        if (!IsFinite(request.Damage) || request.Damage <= 0f)
            return HitReportDecision.InvalidDamage;
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
