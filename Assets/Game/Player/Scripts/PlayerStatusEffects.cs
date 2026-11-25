using Unity.Netcode;
using UnityEngine;

// 서버 권위의 지속 피해(Burn)와 둔화(Slow).
// 피해는 DuelPlayer 경유로만 DuelCombatState에 들어가고, 클라는 서버 체력값만 표시한다.
// 같은 종류를 다시 맞으면 쌓지 않고 갱신한다. 사망하면 남은 효과를 지운다.
public sealed class PlayerStatusEffects : NetworkBehaviour
{
    readonly NetworkVariable<bool> burnVisible = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<float> slowMultiplierNet = new NetworkVariable<float>(1f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<bool> stunActiveNet =  new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool BurnVisible => burnVisible.Value;
    public float SlowMultiplierNet => slowMultiplierNet.Value;
    public bool StunActiveNet => stunActiveNet.Value;

    PlayerCombatStats combatStats;
    DuelPlayer duelPlayer;
    [SerializeField] PlayerStunVisual stunVisual;

    // 서버 권위 상태. 클라는 NetworkVariable만 본다.
    // 틱 계산은 아래 순수 상태값에 두고, Test Runner에서 규칙만 검증한다.
    internal readonly StatusEffectState State = new StatusEffectState();

    bool slowActive;
    float slowExpiryTime;
    bool stunActive;
    float stunExpiryTime;

    void Awake()
    {
        combatStats = GetComponent<PlayerCombatStats>();
        duelPlayer = GetComponent<DuelPlayer>();
    }

    public override void OnNetworkSpawn()
    {
        slowMultiplierNet.OnValueChanged += OnSlowMultiplierChanged;
        ApplySlowMultiplierLocal(slowMultiplierNet.Value);
        stunActiveNet.OnValueChanged += OnStunActiveChanged;
        OnStunActiveChanged(false, stunActiveNet.Value);
    }

    public override void OnNetworkDespawn()
    {
        slowMultiplierNet.OnValueChanged -= OnSlowMultiplierChanged;
        ClearSlowMultiplierLocal();
        stunActiveNet.OnValueChanged -= OnStunActiveChanged;
        if (stunVisual != null) stunVisual.SetVisible(false);
    }

    void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (duelPlayer != null && (duelPlayer.IsServerDefeated || duelPlayer.IsServerCombatEnded))
        {
            if (State.BurnActive || slowActive || stunActive) ServerClearAll();
            return;
        }
        float now = Time.time;
        if (slowActive && now >= slowExpiryTime) ServerClearSlow();
        if (stunActive && now >= stunExpiryTime) ServerClearStun();
        if (!State.TryConsumeTick(now, out int attackerSlot, out uint dotId, out int tickIndex, out float damage))
            return;
        if (duelPlayer != null)
            duelPlayer.ServerApplyStatusTick(attackerSlot, dotId, tickIndex, damage);
        if (!State.BurnActive && IsSpawned) burnVisible.Value = false;
    }

    /// <summary>서버에서 호출. 재적용은 틱을 처음부터 갱신하고 쌓지 않는다.</summary>
    public void ServerApplyBurn(int attackerSlot, float tickDamage, int tickCount, float duration)
    {
        if (!IsServer || !IsSpawned) return;
        if (duelPlayer != null && (duelPlayer.IsServerDefeated || duelPlayer.IsServerCombatEnded)) return;
        if (!State.ApplyBurn(attackerSlot, tickDamage, tickCount, duration, Time.time)) return;
        if (IsSpawned) burnVisible.Value = true;
    }

    /// <summary>서버에서 호출. 재적용은 만료를 연장하고 쌓지 않는다.</summary>
    public void ServerApplySlow(float multiplier, float duration)
    {
        if (!IsServer || !IsSpawned) return;
        if (multiplier <= 0f || multiplier >= 1f || duration <= 0f) return;
        if (duelPlayer != null && (duelPlayer.IsServerDefeated || duelPlayer.IsServerCombatEnded)) return;
        slowActive = true;
        slowExpiryTime = Time.time + duration;
        slowMultiplierNet.Value = Mathf.Clamp(multiplier, 0.01f, 1f);
    }

    // 스턴
    public void ServerApplyStun(float duration)
    {
        if (!IsServer || !IsSpawned) return;

        if (duration <= 0f) return;

        if (duelPlayer != null &&
            (duelPlayer.IsServerDefeated || duelPlayer.IsServerCombatEnded))
            return;

        stunActive = true;
        stunActiveNet.Value = true;
        stunExpiryTime = Mathf.Max(Time.time + duration, stunExpiryTime);
    }
    /// <summary>서버에서 호출. 사망·전투 종료 시 남은 효과를 지운다.</summary>
    public void ServerClearAll()
    {
        if (!IsServer) return;
        State.ClearBurn();
        if (IsSpawned) burnVisible.Value = false;
        ServerClearSlow();
        ServerClearStun();
    }

    void ServerClearSlow()
    {
        slowActive = false;
        if (IsSpawned) slowMultiplierNet.Value = 1f;
    }

    void ServerClearStun()
    {
        stunActive = false;
        stunExpiryTime = 0f;
        stunActiveNet.Value = false;
    }

    void OnSlowMultiplierChanged(float previous, float current) => ApplySlowMultiplierLocal(current);

    void OnStunActiveChanged(bool previous, bool current)
    {
        if (IsClient && stunVisual != null) stunVisual.SetVisible(current);
    }

    void ApplySlowMultiplierLocal(float multiplier)
    {
        if (combatStats == null) combatStats = GetComponent<PlayerCombatStats>();
        if (combatStats == null) return;
        if (multiplier >= 0.999f) combatStats.RemoveMoveSpeedModifier(this);
        else combatStats.SetMoveSpeedModifier(this, multiplier);
    }

    void ClearSlowMultiplierLocal()
    {
        if (combatStats != null) combatStats.RemoveMoveSpeedModifier(this);
    }
}

// 서버 틱 규칙만 담은 순수 상태. MonoBehaviour 없이 Test Runner에서 검증한다.
// 재적용은 쌓지 않고 갱신한다.
public sealed class StatusEffectState
{
    // 일반 공격 attackId와 겹치지 않게 도트용 id에 세운다.
    public const uint DotAttackIdFlag = 0x80000000;
    uint nextDotSequence = 1;

    public bool BurnActive { get; private set; }
    int burnAttackerSlot;
    uint burnDotId;
    float burnTickDamage;
    int burnTicksRemaining;
    float burnTickInterval;
    float burnNextTickTime;
    int burnTickIndex;

    public bool ApplyBurn(int attackerSlot, float tickDamage, int tickCount, float duration, float now)
    {
        if (tickDamage <= 0f || tickCount <= 0 || duration <= 0f) return false;
        BurnActive = true;
        burnAttackerSlot = attackerSlot;
        burnDotId = NextDotId();
        burnTickDamage = tickDamage;
        burnTicksRemaining = tickCount;
        burnTickInterval = duration / tickCount;
        burnNextTickTime = now + burnTickInterval;
        burnTickIndex = 0;
        return true;
    }

    public bool TryConsumeTick(float now, out int attackerSlot, out uint dotId, out int tickIndex, out float damage)
    {
        attackerSlot = 0;
        dotId = 0;
        tickIndex = 0;
        damage = 0f;
        if (!BurnActive || now < burnNextTickTime) return false;
        attackerSlot = burnAttackerSlot;
        dotId = burnDotId;
        tickIndex = burnTickIndex;
        damage = burnTickDamage;
        burnTickIndex++;
        burnTicksRemaining--;
        if (burnTicksRemaining <= 0)
        {
            BurnActive = false;
            return true;
        }
        burnNextTickTime += burnTickInterval;
        return true;
    }

    public void ClearBurn() => BurnActive = false;

    uint NextDotId()
    {
        uint sequence = nextDotSequence++;
        if (nextDotSequence == 0 || (nextDotSequence & DotAttackIdFlag) != 0) nextDotSequence = 1;
        return DotAttackIdFlag | sequence;
    }
}
