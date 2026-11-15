using System.Collections.Generic;
using UnityEngine;

// 서버는 클라이언트가 보고한 피해를 중복 없이 반영하고 패배를 한 번만 알린다.
public sealed class DuelCombatState
{
    // 이미 피해를 받은 (공격자 슬롯, 공격 id, 타격 번호).
    readonly HashSet<(int, uint, int)> hits = new HashSet<(int, uint, int)>();

    public DuelCombatState(int slot, float maxHealth)
    {
        Slot = slot;
        Health = maxHealth;
    }

    public int Slot { get; }
    public float Health { get; private set; }
    public bool IsDefeated => Health <= 0f;

    // (공격 id, 타격 번호)마다 한 번만 피해를 받는다. 늦게 도착한 이전 공격도 받는다.
    // 체력은 0 아래로 내려가지 않고, 사망은 0이 되는 순간에만 알린다.
    public bool TryTakeHit(int attackerSlot, uint attackId, int hitIndex, float damage, out bool defeatedNow) =>
        ServerHitReport.TryApplyTargetHit(this, attackerSlot, attackId, hitIndex, damage, out defeatedNow) ==
        HitReportDecision.Accepted;

    internal bool HasHit(int attackerSlot, uint attackId, int hitIndex) =>
        hits.Contains((attackerSlot, attackId, hitIndex));

    internal void ApplyAcceptedHit(int attackerSlot, uint attackId, int hitIndex, float damage, out bool defeatedNow)
    {
        hits.Add((attackerSlot, attackId, hitIndex));
        Health = Mathf.Max(0f, Health - damage);
        defeatedNow = IsDefeated;
    }
}
