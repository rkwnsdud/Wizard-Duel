#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

public class StatusEffectStateTests
{
    [Test]
    public void Burn_RejectsInvalidValues()
    {
        var state = new StatusEffectState();
        Assert.That(state.ApplyBurn(1, 0f, 3, 3f, 0f), Is.False);
        Assert.That(state.ApplyBurn(1, 3f, 0, 3f, 0f), Is.False);
        Assert.That(state.ApplyBurn(1, 3f, 3, 0f, 0f), Is.False);
        Assert.That(state.BurnActive, Is.False);
        Assert.That(state.TryConsumeTick(10f, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void Burn_TicksAtFixedIntervalThenEnds()
    {
        var state = new StatusEffectState();
        Assert.That(state.ApplyBurn(1, 3f, 3, 3f, 0f), Is.True);
        Assert.That(state.TryConsumeTick(0.5f, out _, out _, out _, out _), Is.False);
        for (int tick = 0; tick < 3; tick++)
        {
            Assert.That(state.TryConsumeTick(1f + tick, out int attacker, out uint dotId, out int index, out float damage),
                Is.True, $"tick {tick}");
            Assert.That(attacker, Is.EqualTo(1));
            Assert.That(damage, Is.EqualTo(3f));
            Assert.That(index, Is.EqualTo(tick));
            Assert.That(dotId & StatusEffectState.DotAttackIdFlag, Is.Not.EqualTo(0));
        }
        Assert.That(state.BurnActive, Is.False);
        Assert.That(state.TryConsumeTick(10f, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void Burn_ReapplyRefreshesInsteadOfStacking()
    {
        var state = new StatusEffectState();
        Assert.That(state.ApplyBurn(1, 3f, 3, 3f, 0f), Is.True);
        Assert.That(state.TryConsumeTick(1f, out _, out uint firstId, out _, out _), Is.True);
        Assert.That(state.ApplyBurn(1, 3f, 3, 3f, 1f), Is.True);
        Assert.That(state.TryConsumeTick(1.5f, out _, out _, out _, out _), Is.False);
        Assert.That(state.TryConsumeTick(2f, out _, out uint secondId, out int index, out _), Is.True);
        Assert.That(secondId, Is.Not.EqualTo(firstId));
        Assert.That(index, Is.EqualTo(0));
        Assert.That(state.TryConsumeTick(3f, out _, out _, out _, out _), Is.True);
        Assert.That(state.TryConsumeTick(4f, out _, out _, out _, out _), Is.True);
        Assert.That(state.BurnActive, Is.False);
    }

    [Test]
    public void Burn_DotIdsDoNotCollideWithPlainAttackIds()
    {
        var state = new StatusEffectState();
        Assert.That(state.ApplyBurn(1, 1f, 1, 1f, 0f), Is.True);
        Assert.That(state.TryConsumeTick(1f, out _, out uint dotId, out _, out _), Is.True);
        Assert.That(dotId & StatusEffectState.DotAttackIdFlag, Is.Not.EqualTo(0));
    }

    [Test]
    public void Burn_ClearStopsTicks()
    {
        var state = new StatusEffectState();
        Assert.That(state.ApplyBurn(1, 3f, 3, 3f, 0f), Is.True);
        state.ClearBurn();
        Assert.That(state.BurnActive, Is.False);
        Assert.That(state.TryConsumeTick(10f, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void DotTicks_FlowThroughCombatStateWithoutDuplicates()
    {
        var state = new StatusEffectState();
        var combat = new DuelCombatState(0, 100f);
        Assert.That(state.ApplyBurn(1, 3f, 3, 3f, 0f), Is.True);
        for (int tick = 0; tick < 3; tick++)
        {
            Assert.That(state.TryConsumeTick(1f + tick, out int attacker, out uint dotId, out int index, out float damage),
                Is.True);
            Assert.That(combat.TryTakeHit(attacker, dotId, index, damage, out _), Is.True);
            Assert.That(combat.TryTakeHit(attacker, dotId, index, damage, out _), Is.False);
        }
        Assert.That(combat.Health, Is.EqualTo(91f));
    }
}
#endif
