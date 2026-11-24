using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// 봇의 실력. AiDuel의 Inspector에서 고친다.
[Serializable]
public sealed class DuelBotTuning
{
    [Tooltip("상대가 보이기 시작한 뒤 겨누고 차징을 시작하기까지 걸리는 시간.")]
    [Min(0f)] public float reactionTime = 0.35f;
    [Tooltip("조준이 돌아가는 빠르기(도/초).")]
    [Min(1f)] public float turnSpeed = 420f;
    [Tooltip("공격마다 새로 뽑는 조준 오차의 최대 각도.")]
    [Min(0f)] public float aimError = 6f;
    [Tooltip("상대가 움직이는 쪽으로 미리 겨누는 시간. 0이면 지금 서 있는 곳을 겨눈다.")]
    [Min(0f)] public float aimLead = 0.15f;
    [Tooltip("날아가는 공격(화염 공, 완드 탄 등)이 닿을 때까지 걸리는 시간 중 미리 겨눠 주는 비율. 0이면 헤아리지 않고 1이면 전부 헤아린다.")]
    [Range(0f, 1f)] public float projectileLead = 0.5f;
    [Tooltip("공격마다 모을 차징 비율의 범위. 차징하지 않는 무기에는 쓰지 않는다.")]
    public Vector2 chargeRange = new Vector2(0.3f, 0.85f);
    [Tooltip("공격을 마친 뒤 다음 차징을 시작하기까지 쉬는 시간의 범위.")]
    public Vector2 attackPause = new Vector2(0.2f, 0.8f);
    [Tooltip("상대가 나를 겨누고 공격을 시작하면 옆으로 피할 확률.")]
    [Range(0f, 1f)] public float dodgeChance = 0.5f;
    [Tooltip("사거리를 알려 주지 않는 무기(IWeaponReach 없음)를 들었을 때 쏘는 거리.")]
    [Min(1f)] public float fallbackReach = 6f;
    [Tooltip("공격을 마치고 다음 공격을 준비하는 동안 벽 뒤로 숨을 확률. 준비하다가 맞았을 때도 이 확률로 숨는다.")]
    [Range(0f, 1f)] public float coverUse = 0.6f;
    [Tooltip("다음 공격을 준비하는 동안 물러나 거리를 벌리는 정도. 0이면 가까이에서 맴돌고 1이면 사거리 끝까지 물러난다.")]
    [Range(0f, 1f)] public float keepDistance = 0.6f;
    [Tooltip("체력이 이 비율 아래로 내려가면 몸을 사린다. 더 멀리서 싸우고 더 자주 숨는다.")]
    [Range(0f, 1f)] public float cautiousHealth = 0.4f;

    // 난이도별 기본값. AiDuel이 BotDifficulty 순서(쉬움·보통·어려움)로 들고 있고 Inspector에서 고칠 수 있다.
    public static DuelBotTuning Easy() => new DuelBotTuning
    {
        reactionTime = 0.6f, turnSpeed = 300f, aimError = 14f, aimLead = 0f, projectileLead = 0f,
        chargeRange = new Vector2(0.2f, 0.5f), attackPause = new Vector2(0.8f, 1.6f), dodgeChance = 0.15f,
        coverUse = 0.2f, keepDistance = 0.25f, cautiousHealth = 0.2f
    };

    public static DuelBotTuning Normal() => new DuelBotTuning();

    public static DuelBotTuning Hard() => new DuelBotTuning
    {
        reactionTime = 0.2f, turnSpeed = 600f, aimError = 2.5f, aimLead = 0.2f, projectileLead = 1f,
        chargeRange = new Vector2(0.4f, 1f), attackPause = new Vector2(0.1f, 0.4f), dodgeChance = 0.85f,
        coverUse = 0.9f, keepDistance = 1f, cautiousHealth = 0.5f
    };
}

// AI 대전에서 봇 캐릭터를 움직이는 두뇌. 사람의 PlayerMovement·PlayerAim·PlayerAttack 입력을 대신한다.
// 사람과 같은 규칙을 따른다: 같은 빠르기로 걷고, 시전 중에는 움직이거나 돌지 못하고, 차징한 시간만큼만 세게 쏘고,
// 부시에 숨은 상대는 같은 부시에 들어가거나 상대가 공격할 때만 본다(BushVisibilityRules).
// 무작정 달려들지 않는다: 쏘고 나서 다음 공격을 준비하는 동안에는 물러나 거리를 벌리거나 벽 뒤로 숨고,
// 쏠 준비가 되면 다시 나와 겨눈다. 체력이 적거나 무기 사거리가 다르면 그에 맞춰 거리를 잡는다.
// 벽은 물리 판정 대신 맵 격자(BotGrid)로 보고, 실제 명중은 무기의 온라인 판정(IWeapon)이 정한다.
// 전투가 켜져 있는 동안만 DuelPlayer가 이 컴포넌트를 켠다.
[DisallowMultipleComponent]
public sealed class DuelBot : MonoBehaviour
{
    // 길을 따라갈 때 벽에서 띄우는 몸 반지름과, 쏜 공격이 벽에 막히는지 볼 때 쓰는 반지름.
    const float BodyRadius = 0.35f;
    const float ShotRadius = 0.2f;
    const float RepathInterval = 0.3f;
    const float ArriveDistance = 0.7f;
    // 길에서 이 칸 수 앞까지만 곧게 갈 수 있는지 본다.
    const int LookAhead = 8;
    // 조준이 이 각도 안으로 들어와야 쏜다.
    const float AimTolerance = 4f;
    // 쏠 수 있는 거리의 이 비율 안으로 다가가고, 이 비율보다 가까우면 물러난다.
    const float ApproachRatio = 0.85f;
    const float RetreatRatio = 0.5f;
    const float FireRatio = 0.9f;
    // 상대가 이 거리(최대 사거리 + 여유) 안에 들어오면 차징을 시작한다.
    const float EngageMargin = 2f;
    const float DodgeDuration = 0.45f;
    const float DodgeAngle = 25f;
    // 차징 중에 상대를 이 시간 넘게 못 보면 차징을 푼다.
    const float LoseSightAfter = 1.2f;
    const float SearchGoalLifetime = 8f;
    // 아주 멀리 닿는 무기(빛 반사 구슬)를 들어도 이 거리보다 멀리서 싸우려 하지 않는다. 넓지 않은 맵에서 계속 물러나기만 하지 않게 한다.
    const float MaxFightingReach = 10f;
    // 날아가는 공격을 미리 겨눌 때 헤아리는 가장 긴 비행 시간.
    const float MaxFlightTime = 0.8f;
    // 다음 공격을 준비하는 동안 물러나는 가장 먼 거리와, 쏠 준비가 된 채로 물러나는 가장 먼 거리(사거리 대비).
    const float ReloadRetreatRatio = 1f;
    const float ReadyRetreatRatio = 0.75f;
    // 준비하는 동안 물러난 거리에서 이만큼 더 벌어져야 다시 따라붙는다.
    const float ReapproachMargin = 1.5f;
    // 두 무기의 사거리가 이만큼 넘게 차이 나면 거리를 다르게 잡는다.
    const float OutrangeMargin = 1.5f;
    // 숨을 곳은 걸어서 이 칸 수 안에서 찾고, 상대에게 이보다 가까운 곳에는 숨지 않는다.
    const int CoverSearchSteps = 6;
    const float CoverMinDistance = 2.5f;
    const float CoverArrive = 0.2f;
    const float CoverRecheck = 0.3f;
    // 다음 공격까지 이만큼도 남지 않았으면 숨으러 가지 않고, 숨어도 이 시간을 넘기지 않는다.
    const float MinHideTime = 0.7f;
    const float MaxHideTime = 4f;
    // 차징이 다 차기 이만큼 전에 숨은 곳에서 나선다.
    const float PeekLead = 0.3f;

    BotGrid grid;
    DuelBotTuning tuning = new DuelBotTuning();
    Transform target;
    PlayerAttack targetAttack;
    PlayerMotor motor;
    PlayerAttack attack;
    PlayerHealth health;

    // 상대가 공격해서 드러난 시간. 내 화면의 PlayerBushVisibility와 같은 규칙을 봇 쪽에서 따로 센다.
    readonly BushReveal targetReveal = new BushReveal();
    readonly List<Vector2Int> path = new List<Vector2Int>();
    Vector2Int pathGoal;
    float repathAt;

    Vector3 targetPrevious;
    Vector3 targetVelocity;
    bool targetWasAttacking;
    bool visible;
    float visibleSince;
    Vector3 lastSeen;
    float lastSeenAt;
    bool hasLastSeen;
    Vector3 searchGoal;
    float searchGoalExpires;

    float strafeSign = 1f;
    float strafeFlipAt;
    Vector3 dodgeDirection;
    float dodgeFrom;
    float dodgeUntil;

    // 벽 뒤에 숨으러 가는 중이거나 숨어 있다.
    bool hiding;
    Vector3 coverPoint;
    float coverCheckAt;
    float hideUntil;
    float lastHealth;

    bool casting;
    bool charging;
    float chargeStartedAt;
    float nextChargeAt;
    float plannedCharge;
    float aimOffset;

    float stuckTime;
    float stuckProgress;

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        attack = GetComponent<PlayerAttack>();
        health = GetComponent<PlayerHealth>();
        // 봇은 텔레포트 타일을 피해 다니지만 밟혀서 옮겨지면 새 자리에서 길을 다시 찾는다.
        if (TryGetComponent(out PlayerTeleporter teleporter)) teleporter.Teleported += OnTeleported;
        // 전투가 켜질 때 DuelPlayer가 켠다.
        enabled = false;
    }

    void OnDestroy()
    {
        if (TryGetComponent(out PlayerTeleporter teleporter)) teleporter.Teleported -= OnTeleported;
    }

    void OnTeleported(PlayerTeleporter teleporter, Vector3 from)
    {
        path.Clear();
        repathAt = 0f;
        stuckTime = stuckProgress = 0f;
    }

    // DuelArena가 봇을 소환하기 전에 부른다. opponent는 봇이 싸울 상대(사람)다.
    public void Initialize(BotGrid grid, DuelPlayer opponent, DuelBotTuning tuning)
    {
        this.grid = grid;
        if (tuning != null) this.tuning = tuning;
        target = opponent.transform;
        targetAttack = opponent.GetComponent<PlayerAttack>();
    }

    void OnEnable()
    {
        float now = Time.time;
        path.Clear();
        repathAt = 0f;
        visible = false;
        hasLastSeen = false;
        searchGoalExpires = 0f;
        targetVelocity = Vector3.zero;
        targetWasAttacking = false;
        targetReveal.Clear();
        if (target != null) targetPrevious = target.position;
        strafeSign = Random.value < 0.5f ? -1f : 1f;
        strafeFlipAt = now + Random.Range(0.7f, 2f);
        dodgeUntil = 0f;
        hiding = false;
        lastHealth = health != null ? health.CurrentHealth : 0f;
        casting = false;
        charging = false;
        nextChargeAt = now;
        motor.ResetFall();
        stuckTime = stuckProgress = 0f;
        PlanAttack();
    }

    void Update()
    {
        if (grid == null || target == null || health.IsDead || !motor.CanMove) return;
        float now = Time.time;
        Perceive(now);

        Vector3 move = Vector3.zero;
        bool hurt = health.CurrentHealth < lastHealth;
        lastHealth = health.CurrentHealth;
        if (attack.IsAttacking)
        {
            // 시전 중에는 사람이 조작할 때처럼 이동과 조준이 멈춘다.
            casting = true;
            hiding = false;
        }
        else
        {
            if (casting)
            {
                casting = false;
                nextChargeAt = now + Random.Range(tuning.attackPause.x, tuning.attackPause.y);
                PlanAttack();
                PlanCover(now);
            }
            // 쏠 수 없는 동안 맞았으면 맞고만 있지 않고 숨을 곳을 찾는다.
            else if (hurt && !hiding && Reloading(now)) PlanCover(now);
            move = ChooseMove(now);
            Aim(move, now);
            if (TryAttack(now)) move = Vector3.zero;
        }
        Step(move);
    }

    // 상대가 보이는지와 어디로 움직이는지를 갱신한다.
    void Perceive(float now)
    {
        Vector3 position = target.position;
        if (Time.deltaTime > 0f)
        {
            Vector3 velocity = Flat(position - targetPrevious) / Time.deltaTime;
            targetVelocity = Vector3.Lerp(targetVelocity, velocity, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }
        targetPrevious = position;

        bool attacking = targetAttack != null && targetAttack.IsAttacking;
        if (attacking && !targetWasAttacking)
        {
            targetReveal.Reveal(now);
            PlanDodge(now);
        }
        targetWasAttacking = attacking;

        BushView view = BushVisibilityRules.Evaluate(grid.BushAt(transform.position), grid.BushAt(position), targetReveal.IsRevealed(now));
        bool sees = view.Opponent != BushLook.Hidden;
        if (sees)
        {
            if (!visible) visibleSince = now;
            lastSeen = position;
            lastSeenAt = now;
            hasLastSeen = true;
        }
        visible = sees;
    }

    // 상대를 보고 반응 시간이 지났는지. 그 전에는 겨누거나 차징하지 않는다.
    bool Aware(float now) => visible && now - visibleSince >= tuning.reactionTime;

    bool Chargeable => attack.CurrentWeapon is IChargeableWeapon;

    float ChargeRatio(float now) =>
        charging && Chargeable && attack.MaxChargeTime > 0f ? Mathf.Clamp01((now - chargeStartedAt) / attack.MaxChargeTime) : 0f;

    // 지금 든 무기로 싸울 거리. 무기는 판마다 다르므로 그때그때 묻는다.
    float Reach(float charge) => Mathf.Min(MaxFightingReach,
        attack.CurrentWeapon is IWeaponReach reach ? reach.ReachAt(charge) : tuning.fallbackReach);

    // 다음 공격에서 모을 차징과 조준 오차를 새로 뽑는다.
    void PlanAttack()
    {
        charging = false;
        plannedCharge = attack != null && Chargeable ? Random.Range(tuning.chargeRange.x, tuning.chargeRange.y) : 0f;
        aimOffset = Random.Range(-tuning.aimError, tuning.aimError);
    }

    // 이번 프레임에 걸을 방향(길이 1 또는 0).
    Vector3 ChooseMove(float now)
    {
        Vector3 position = transform.position;
        if (now >= dodgeFrom && now < dodgeUntil)
        {
            if (CanStep(dodgeDirection)) return dodgeDirection;
            dodgeUntil = 0f;
        }
        if (!visible)
        {
            hiding = false;
            return Search(now);
        }
        if (hiding)
        {
            Vector3 toCover = Hide(now);
            if (hiding) return toCover;
        }

        Vector3 toTarget = Flat(target.position - position);
        float distance = toTarget.magnitude;
        float reach = Reach(Mathf.Max(plannedCharge, ChargeRatio(now)));
        bool reloading = Reloading(now);
        float keepAway = KeepAway(reach, reloading);
        bool lineOfFire = grid.HasClearLine(position, target.position, ShotRadius);
        if (reloading)
        {
            // 쏠 수 없는 동안에는 굳이 다가가지 않는다. 너무 멀어졌을 때만 따라붙고, 벽에 가려 있으면 그 자리에서 기다린다.
            if (distance > Mathf.Max(reach, keepAway + ReapproachMargin)) return Follow(target.position, now);
            if (!lineOfFire) return Vector3.zero;
        }
        else if (distance > reach * ApproachRatio || !lineOfFire) return Follow(target.position, now);

        // 쏠 수 있는 거리에서는 상대 둘레를 옆으로 돌고, 지키려는 거리보다 가까우면 물러난다. 막히면 반대쪽으로 돈다.
        Vector3 forward = distance > 0.01f ? toTarget / distance : transform.forward;
        if (now >= strafeFlipAt) FlipStrafe(now);
        if (distance < keepAway)
        {
            Vector3 back = BackOff(forward);
            if (back != Vector3.zero) return back;
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Vector3 side = Vector3.Cross(Vector3.up, forward) * strafeSign;
            if (CanStep(side)) return side;
            FlipStrafe(now);
        }
        return Vector3.zero;
    }

    // 공격을 마친 뒤 쉬는 중이거나 무기 쿨다운이 돌고 있어 아직 쏠 수 없다.
    bool Reloading(float now) => now < nextChargeAt || attack.IsOnCooldown;

    bool Cautious => health.MaxHealth > 0f && health.CurrentHealth / health.MaxHealth < tuning.cautiousHealth;

    // 상대 무기가 닿는 가장 먼 거리.
    float TheirReach() => targetAttack != null && targetAttack.CurrentWeapon is IWeaponReach reach ? reach.ReachAt(1f) : tuning.fallbackReach;

    // 상대가 이 거리 안으로 들어오면 물러난다.
    float KeepAway(float reach, bool reloading)
    {
        float mine = Reach(1f), theirs = TheirReach();
        float ratio = RetreatRatio;
        if (reloading)
        {
            // 상대가 훨씬 멀리 닿으면 물러나 봐야 맞기만 한다. 그때는 붙어서 맴돈다.
            if (theirs <= mine + OutrangeMargin) ratio = Mathf.Lerp(RetreatRatio, ReloadRetreatRatio, tuning.keepDistance);
        }
        else
        {
            if (Cautious) ratio = Mathf.Lerp(RetreatRatio, ReadyRetreatRatio, tuning.keepDistance);
            // 내가 훨씬 멀리 닿으면 상대 사거리 밖에서 쏜다.
            if (mine > theirs + OutrangeMargin && reach > 0f)
                ratio = Mathf.Max(ratio, Mathf.Lerp(RetreatRatio, Mathf.Min(ReadyRetreatRatio, (theirs + 0.75f) / reach), tuning.keepDistance));
        }
        return reach * ratio;
    }

    // 상대에게서 멀어지는 걸음. 돌던 쪽으로 비스듬히 물러나고, 막히면 반대쪽 비스듬히, 그다음 곧장 뒤로 물러난다.
    Vector3 BackOff(Vector3 forward)
    {
        Vector3 side = Vector3.Cross(Vector3.up, forward) * strafeSign;
        Vector3 wanted = (side - forward).normalized;
        if (CanStep(wanted)) return wanted;
        wanted = (-side - forward).normalized;
        if (CanStep(wanted))
        {
            strafeSign = -strafeSign;
            return wanted;
        }
        return CanStep(-forward) ? -forward : Vector3.zero;
    }

    // 지금부터 쏠 수 있게 되기까지 걸리는 시간.
    float WaitBeforeFiring(float now)
    {
        float wait = Mathf.Max(0f, nextChargeAt - now, attack.CooldownRemaining);
        if (Chargeable) wait += Mathf.Max(0f, plannedCharge - ChargeRatio(now)) * attack.MaxChargeTime;
        return wait;
    }

    // 다음 공격을 준비하는 동안 숨을지 정한다. 상대가 보이고, 숨을 만큼 시간이 남았고, 가까이에 가려진 곳이 있을 때만 숨는다.
    void PlanCover(float now)
    {
        hiding = false;
        if (!visible || WaitBeforeFiring(now) < MinHideTime) return;
        // 체력이 적거나 상대가 훨씬 멀리 닿으면 더 자주 숨는다.
        bool wary = Cautious || TheirReach() > Reach(1f) + OutrangeMargin;
        if (Random.value >= (wary ? Mathf.Lerp(tuning.coverUse, 1f, 0.5f) : tuning.coverUse)) return;
        if (!grid.TryFindCover(transform.position, target.position, CoverSearchSteps, BodyRadius, CoverMinDistance, out coverPoint)) return;
        hiding = true;
        coverCheckAt = now + CoverRecheck;
        hideUntil = now + MaxHideTime;
    }

    // 숨을 곳으로 가서 머문다. 쏠 때가 됐거나 더는 가려지지 않으면 hiding을 끄고, 그러면 호출한 쪽이 평소처럼 움직인다.
    Vector3 Hide(float now)
    {
        if (now >= hideUntil || ReadyToPeek(now))
        {
            hiding = false;
            return Vector3.zero;
        }
        if (now >= coverCheckAt)
        {
            coverCheckAt = now + CoverRecheck;
            // 상대가 돌아 들어와 가려지지 않게 됐으면 다른 곳을 찾는다.
            if (!grid.IsShielded(target.position, coverPoint, BodyRadius) &&
                !grid.TryFindCover(transform.position, target.position, CoverSearchSteps, BodyRadius, CoverMinDistance, out coverPoint))
            {
                hiding = false;
                return Vector3.zero;
            }
        }
        if (Flat(coverPoint - transform.position).sqrMagnitude <= CoverArrive * CoverArrive) return Vector3.zero;
        return Follow(coverPoint, now);
    }

    // 숨은 곳에서 나설 때가 됐는지. 차징하는 무기는 벽 뒤에서 거의 다 모은 뒤에 나선다.
    bool ReadyToPeek(float now)
    {
        if (Reloading(now)) return false;
        if (!Chargeable) return true;
        // 여기서는 멀어서 차징을 시작하지 못한다면 나가서 다가간다. 시작할 수 있으면 TryAttack이 이번 프레임에 시작한다.
        if (!charging) return Aware(now) && Flat(target.position - transform.position).magnitude > Reach(1f) + EngageMargin;
        return (plannedCharge - ChargeRatio(now)) * attack.MaxChargeTime <= PeekLead;
    }

    void FlipStrafe(float now)
    {
        strafeSign = -strafeSign;
        strafeFlipAt = now + Random.Range(0.7f, 2f);
    }

    // 상대가 숨었다. 마지막으로 본 곳에 가 보고, 거기에도 없으면 부시를 하나씩 찾아다닌다.
    // 숨을 수 있는 곳은 부시뿐이고, 같은 부시에 들어서면 상대가 보인다.
    Vector3 Search(float now)
    {
        Vector3 position = transform.position;
        if (hasLastSeen)
        {
            if (Flat(lastSeen - position).sqrMagnitude > ArriveDistance * ArriveDistance) return Follow(lastSeen, now);
            hasLastSeen = false;
            searchGoalExpires = 0f;
        }
        if (now >= searchGoalExpires || Flat(searchGoal - position).sqrMagnitude <= ArriveDistance * ArriveDistance)
        {
            searchGoal = grid.RandomSearchPoint(position);
            searchGoalExpires = now + SearchGoalLifetime;
        }
        return Follow(searchGoal, now);
    }

    // destination으로 가는 방향. 곧게 갈 수 있으면 바로 가고, 벽이 막으면 격자에서 찾은 길을 따라간다.
    Vector3 Follow(Vector3 destination, float now)
    {
        Vector3 position = transform.position;
        if (grid.HasClearPath(position, destination, BodyRadius))
        {
            path.Clear();
            return Direction(destination - position);
        }

        Vector2Int goal = grid.CellAt(destination);
        if (now >= repathAt && (path.Count == 0 || goal != pathGoal))
        {
            repathAt = now + RepathInterval;
            pathGoal = goal;
            Vector2Int from = grid.CellAt(position);
            // 칸 경계에 걸쳐 서 있거나 목적지가 벽 쪽이면 가장 가까운 바닥 칸으로 맞춘다.
            if (!grid.IsWalkable(from)) grid.TryNearestWalkable(from, 2, out from);
            if (!grid.IsWalkable(goal)) grid.TryNearestWalkable(goal, 2, out goal);
            grid.FindPath(from, goal, path);
        }
        if (path.Count == 0) return Vector3.zero;

        // 곧게 갈 수 있는 가장 먼 칸으로 향하고, 그 앞의 칸은 지나간 것으로 본다.
        for (int i = Mathf.Min(path.Count, LookAhead) - 1; i > 0; i--)
        {
            if (!grid.HasClearPath(position, grid.CenterOf(path[i]), BodyRadius)) continue;
            path.RemoveRange(0, i);
            break;
        }
        Vector3 next = grid.CenterOf(path[0]);
        if (path.Count > 1 && Flat(next - position).sqrMagnitude < 0.04f)
        {
            path.RemoveAt(0);
            next = grid.CenterOf(path[0]);
        }
        return Direction(next - position);
    }

    // 그 방향으로 한 걸음 내디딜 자리가 비어 있는지.
    bool CanStep(Vector3 direction) =>
        grid.HasClearPath(transform.position, transform.position + direction * 0.9f, BodyRadius);

    // 상대가 공격을 시작했다. 나를 겨누고 있으면 반응 시간 뒤에 조준선 밖으로 비켜선다.
    void PlanDodge(float now)
    {
        if (attack.IsAttacking || Random.value > tuning.dodgeChance) return;
        Vector3 fromTarget = Flat(transform.position - target.position);
        Vector3 aim = Flat(target.forward);
        if (fromTarget.sqrMagnitude < 0.01f || aim.sqrMagnitude < 0.01f || Vector3.Angle(aim, fromTarget) > DodgeAngle) return;
        // 조준선에서 이미 벗어나 있는 쪽으로 더 벗어난다.
        Vector3 side = Vector3.Cross(Vector3.up, aim).normalized;
        if (Vector3.Dot(side, fromTarget) < 0f) side = -side;
        if (!CanStep(side)) side = -side;
        dodgeDirection = side;
        dodgeFrom = now + tuning.reactionTime * 0.5f;
        dodgeUntil = dodgeFrom + DodgeDuration;
    }

    // 상대를 알아챘으면 상대(가 움직일 곳)를 겨누고, 아니면 걷는 쪽을 본다.
    void Aim(Vector3 move, float now)
    {
        Vector3 direction = Aware(now) ? AimDirection() : move;
        if (direction.sqrMagnitude < 0.0001f) return;
        motor.Look(direction.normalized, Time.deltaTime, tuning.turnSpeed);
    }

    // 이번 공격의 조준 오차를 더한, 쏘려는 방향. 날아가는 공격은 닿을 때까지 상대가 움직일 만큼 더 앞을 겨눈다.
    Vector3 AimDirection()
    {
        Vector3 toTarget = Flat(target.position - transform.position);
        float time = tuning.aimLead;
        if (attack.CurrentWeapon is IWeaponReach reach && reach.ShotSpeed > 0f)
            time += tuning.projectileLead * Mathf.Min(MaxFlightTime, toTarget.magnitude / reach.ShotSpeed);
        Vector3 lead = Vector3.ClampMagnitude(targetVelocity, PlayerMotor.BaseSpeed * 1.5f) * time;
        return Quaternion.Euler(0f, aimOffset, 0f) * (toTarget + lead);
    }

    // 차징을 시작하거나, 모은 차징으로 쏜다. 이번 프레임에 공격을 시작했으면 true다.
    bool TryAttack(float now)
    {
        bool aware = Aware(now);
        float distance = Flat(target.position - transform.position).magnitude;
        if (!charging)
        {
            if (!aware || now < nextChargeAt || attack.IsOnCooldown || distance > Reach(1f) + EngageMargin) return false;
            charging = true;
            chargeStartedAt = now;
        }
        if (!visible && now - lastSeenAt > LoseSightAfter)
        {
            charging = false;
            return false;
        }
        if (!aware) return false;

        // 계획한 만큼 모은 뒤, 닿는 거리에서 벽에 가리지 않고 조준이 맞았을 때 쏜다. 기다리는 동안 차징은 계속 쌓인다.
        float ratio = ChargeRatio(now);
        if (Chargeable && ratio < plannedCharge) return false;
        if (distance > Reach(ratio) * FireRatio || !grid.HasClearLine(transform.position, target.position, ShotRadius)) return false;
        if (Vector3.Angle(transform.forward, AimDirection()) > AimTolerance) return false;
        if (!attack.TryStartAttack(ratio)) return false;
        charging = false;
        return true;
    }

    // 이동은 공통 모터에 넘기고, 여기서는 길에 걸렸는지만 판단한다.
    void Step(Vector3 move)
    {
        float speed = motor.MoveSpeed;
        Vector3 before = transform.position;
        motor.Move(move, Time.deltaTime, combinedGravity: true);

        // 가려는데 반 초 동안 거의 못 갔으면 모서리에 걸린 것이다. 길을 다시 찾고 도는 방향을 바꾼다.
        if (move == Vector3.zero)
        {
            stuckTime = stuckProgress = 0f;
            return;
        }
        stuckTime += Time.deltaTime;
        stuckProgress += Flat(transform.position - before).magnitude;
        if (stuckTime < 0.5f) return;
        if (stuckProgress < speed * stuckTime * 0.2f)
        {
            path.Clear();
            repathAt = 0f;
            searchGoalExpires = 0f;
            FlipStrafe(Time.time);
        }
        stuckTime = stuckProgress = 0f;
    }

    static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    static Vector3 Direction(Vector3 offset)
    {
        offset.y = 0f;
        return offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.zero;
    }
}
