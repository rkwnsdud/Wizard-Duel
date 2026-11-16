#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class DuelCombatStateTests
{
    [Test]
    public void Hit_AppliesOncePerAttackIdAndHitIndex()
    {
        var state = new DuelCombatState(0, 100f);
        Assert.That(state.TryTakeHit(1, 2, 0, 10f, out _), Is.True);
        Assert.That(state.TryTakeHit(1, 2, 0, 10f, out _), Is.False);
        Assert.That(state.Health, Is.EqualTo(90f));
        // 늦게 도착한 이전 공격과 같은 공격의 다음 타격도 들어간다.
        Assert.That(state.TryTakeHit(1, 1, 0, 10f, out _), Is.True);
        Assert.That(state.TryTakeHit(1, 2, 1, 10f, out _), Is.True);
        Assert.That(state.Health, Is.EqualTo(70f));
    }

    [Test]
    public void Hit_IgnoresOwnAttacksAndInvalidDamage()
    {
        var state = new DuelCombatState(0, 100f);
        Assert.That(state.TryTakeHit(0, 1, 0, 10f, out _), Is.False);
        float[] damages = { 0f, -5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        for (int i = 0; i < damages.Length; i++)
            Assert.That(state.TryTakeHit(1, (uint)i + 1, 0, damages[i], out _), Is.False, damages[i].ToString());
        Assert.That(state.Health, Is.EqualTo(100f));
        Assert.That(state.TryTakeHit(1, 1, 0, 10f, out _), Is.True, "Rejected hits do not use the attack id.");
    }

    [Test]
    public void Hit_StopsAtZeroAndReportsTheDefeatOnce()
    {
        var state = new DuelCombatState(0, 100f);
        Assert.That(state.TryTakeHit(1, 1, 0, 60f, out bool first), Is.True);
        Assert.That(first, Is.False);
        Assert.That(state.TryTakeHit(1, 2, 0, 60f, out bool second), Is.True);
        Assert.That(second, Is.True);
        Assert.That(state.Health, Is.EqualTo(0f));
        Assert.That(state.IsDefeated, Is.True);
        Assert.That(state.TryTakeHit(1, 3, 0, 60f, out bool third), Is.False);
        Assert.That(third, Is.False);
        Assert.That(state.Health, Is.EqualTo(0f));
    }

    [Test]
    public void Hit_DistinguishesAttackersWithTheSameAttackId()
    {
        var state = new DuelCombatState(0, 100f);
        Assert.That(state.TryTakeHit(1, 1, 0, 10f, out _), Is.True);
        Assert.That(state.TryTakeHit(2, 1, 0, 10f, out _), Is.True);
        Assert.That(state.Health, Is.EqualTo(80f));
    }

    [Test]
    public void TwoPlayers_RepeatedReportsDamageOnlyOnce()
    {
        var host = new DuelCombatState(0, 100f);
        var guest = new DuelCombatState(1, 100f);
        Assert.That(guest.TryTakeHit(host.Slot, 1, 0, 13f, out _), Is.True);
        Assert.That(guest.TryTakeHit(host.Slot, 1, 0, 13f, out _), Is.False);
        Assert.That(guest.Health, Is.EqualTo(87f));
        Assert.That(host.Health, Is.EqualTo(100f));

        Assert.That(host.TryTakeHit(guest.Slot, 1, 0, 13f, out _), Is.True);
        Assert.That(host.TryTakeHit(guest.Slot, 1, 0, 13f, out _), Is.False);
        Assert.That(host.Health, Is.EqualTo(87f));
    }
}
#endif
