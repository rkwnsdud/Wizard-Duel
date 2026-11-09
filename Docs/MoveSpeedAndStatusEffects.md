# 이동속도 modifier와 상태이상 매니저 안내

이 문서는 Task B(이동속도 modifier)와 Task C(상태이상 매니저)에서 무엇을 만들었고,
팀원이 Unity에서 무엇을 확인할 수 있는지 적는다. Task D 화염 마도서와 Task E 패시브의 전제 조건이다.

관련 PR: #80 (Task B), #81 (Task C)
관련 이슈: #57 (패시브 인터페이스), #58 (근접형 무기), #60 (스턴), #61 (도트 상태이상), #68 (협업 추적)

## 다른 팀원이 바로 쓸 수 있는 것과 화염 전용인 것

### 바로 쓸 수 있는 공용 기반

- `IWeaponEquipPassive`, `IWeaponHitPassive`: 무기 프리팹 자식으로 붙이고 인터페이스만 구현하면 된다.
  무기당 각 1개 제한은 `PlayerWeaponPassiveController`가 이미 강제한다.
- `PlayerCombatStats` 이동속도·공격속도 modifier:
  `SetMoveSpeedModifier(this, 값)`처럼 자기 컴포넌트를 키로 쓰면 서로 겹치지 않는다. 곱연산이라 여러 효과가 동시에 걸려도 깨지지 않는다.
- 서버 승인 적중 경로: `DuelPlayer.ReportHitRpc`에서 `TryTakeHit`을 통과한 것만
  `NotifyServerHit`으로 넘어가므로 중복이나 사망 후 히트는 이미 걸러진다.

### 지금 화염 전용이라 다른 무기가 못 쓰는 것

- `PlayerStatusEffects`: Burn과 Slow 2개가 하드코딩돼 있다.
  독이나 스턴 같은 걸 붙이려면 이 파일을 직접 고쳐야 한다. 인터페이스가 아니다.
- `DuelPlayer.ServerInflictStatus`: 인자가 화염 전용이다.
  `tickDamage, tickCount, duration, slowMultiplier, slowDuration`으로 박혀 있어서 다른 효과는 못 넣는다.
- `StatusEffectState`: Burn 틱 규칙만 있다. 다른 도트와 공유할 구조가 아니다.

지금 상태는 "공통 기반 + 화염 1호 구현"이 섞여 있다.
효과 2개째가 들어오는 순간 일반화가 필요하다. 방향은 다음 중 하나다.

1. 효과마다 분리: `ServerApplyBurn`처럼 `ServerApplyPoison`, `ServerApplyStun`을 계속 추가한다.
   간단하지만 파일이 비대해진다. 효과 3개까지는 버틴다.
2. 일반화: 효과 종류를 id로 구분하고 틱 피해와 modifier를 테이블로 관리한다.
   `StatusEffectState`를 `effectId -> 상태` 딕셔너리로 바꾸는 방식이다. 3개를 넘기면 이쪽이 맞다.

## Task B: 이동속도 modifier

### 무엇을 했나

공격속도 modifier와 같은 구조로 이동속도 modifier 통로를 `PlayerCombatStats`에 추가했다.
아직 실제 이속 패시브나 슬로우는 붙어 있지 않다. 통로만 만든 상태다.

| 파일 | 역할 |
| --- | --- |
| `Assets/Player/PlayerCombatStats.cs` | `moveSpeedModifiers` 딕셔너리, `MoveSpeedMultiplier`, `GetMoveSpeed`, `Set/RemoveMoveSpeedModifier` |
| `Assets/Player/PlayerMovement.cs` | `Update`에서 `effectiveSpeed`로 이동하고 걷기 애니메이션에도 같은 값 사용 |
| `Assets/Scripts/Battle/DuelPlayer.cs` | 원격 걷기 표시(`LateUpdate`)에도 같은 multiplier 반영. 이동 로직 변경 없음 |

### 동작 규칙

- 패시브가 없으면 `MoveSpeedMultiplier = 1.0`이라서 기존 속도 5와 같다.
- 여러 modifier가 겹치면 곱연산으로 합친다. 예: 이속 패시브 1.1 x 슬로우 0.7.
- 0으로 나누거나 멈추는 것을 막기 위해 하한 `0.01f`을 둔다.
- `PlayerMovement`와 원격 표시가 같은 값을 쓰므로 남 화면에서 걷기 모션이 어긋나지 않는다.

### Unity에서 확인할 수 있는 것

1. PlayerTest 씬에서 Player로 Play해서 걸어 다닌다. 속도가 예전과 같아야 한다. 같으면 정상이다.
2. Hierarchy에서 Player를 누르면 `Player Combat Stats (Script)`가 붙어 있다. 값이 안 보이는 게 정상이다. 내부 딕셔너리라서 숫자가 표시되지 않는다.
3. Test Runner > EditMode에서 기존 3개를 돌린다. 전부 초록불이면 통과다.
   - `DuelCombatStateTests` (5개)
   - `DuelLightningWeaponTests` (7개)
   - `WeaponSeparationTests` (2개)

### 팀원이 하면 안 되는 것 (아직)

- `MagicBook.prefab`에 `WeaponBase`를 붙이지 않는다. 붙이면 온라인 경로에서 `IWeapon`이 없다고 경고만 뜨고 공격이 생략된다.
- `DuelWizard-1/2`에 무기를 교체하지 않는다. 온라인 무기 교체 동기화가 아직 없다.
- 이속 수치를 바꾸지 않는다. 1.1 같은 값은 Task E에서 정한다.

## Task C: 상태이상 매니저

### 무엇을 했나

화상 도트(Burn)와 둔화(Slow)를 서버에서 굴리는 `PlayerStatusEffects`를 만들었다.
실제 화염 마도서 연결은 Task D/E에서 한다. 지금은 매니저와 규칙만 있다.

| 파일 | 역할 |
| --- | --- |
| `Assets/Player/PlayerStatusEffects.cs` | 서버 권위 Burn/Slow 매니저 (`NetworkBehaviour`). `StatusEffectState` 순수 틱 규칙 포함 |
| `Assets/Player/Editor/StatusEffectStateTests.cs` | 틱 규칙 6개 테스트 |
| `Assets/Scripts/Battle/DuelPlayer.cs` | `ServerApplyStatusTick`, `ServerInflictStatus`, 사망 시 정리 연결 |

### 동작 규칙

- 서버만 상태를 들고 틱을 굴린다. 클라이언트는 `TakeDamage`를 직접 부르지 않는다.
- 도트 틱은 `DuelPlayer.ServerApplyStatusTick` 경유로만 `DuelCombatState`에 들어간다.
- 같은 종류를 다시 맞으면 쌓지 않고 갱신한다. Burn은 틱을 처음부터 다시 세고, Slow는 만료를 연장한다.
- 도트용 공격 id는 최상위 비트(`0x80000000`)를 세워서 일반 공격과 겹치지 않게 한다.
- 사망하면 남은 효과를 지운다. 직접 맞은 피해로 쓰러져도, 도트 틱으로 쓰러져도 정리된다.
- Slow는 `NetworkVariable`로 전파돼서 양쪽 클라이언트 이동속도에 반영된다. VFX나 UI 표시는 아직 없다.

### Unity에서 확인할 수 있는 것

1. Test Runner > EditMode에서 `StatusEffectStateTests`를 검색해서 Run All을 누른다. 6개 다 초록불이면 통과다.
   - `Burn_RejectsInvalidValues`: 0 피해, 0 틱, 0 시간은 거절된다.
   - `Burn_TicksAtFixedIntervalThenEnds`: 3초 동안 1초 간격으로 3번 맞고 끝난다.
   - `Burn_ReapplyRefreshesInsteadOfStacking`: 중간에 다시 맞으면 처음부터 다시 센다. 쌓이지 않는다.
   - `Burn_DotIdsDoNotCollideWithPlainAttackIds`: 도트 id는 일반 공격 id와 겹치지 않는다.
   - `Burn_ClearStopsTicks`: 지우면 틱이 멈춘다.
   - `DotTicks_FlowThroughCombatStateWithoutDuplicates`: 틱 3번이면 체력 100에서 91이 된다. 중복 보고는 한 번만 들어간다.
2. 씬에 붙이는 작업은 아직 없다. Task D/E에서 무기 쪽에 붙인다.

### 팀원이 하면 안 되는 것 (아직)

- `PlayerStatusEffects`를 씬에 손으로 붙이지 않는다. Duel 플레이어 프리팹 작업은 Task D에서 같이 한다.
- 클라에서 `TakeDamage`로 도트를 흉내 내지 않는다. 서버 틱만 정답이다.
- VFX나 UI를 붙이지 않는다. `burnVisible` 값만 서버에서 내려준다.

## 다음 순서

1. Task D 화염 마도서 본체: `FireGrimoire`가 `IWeapon.Fire`로 부채꼴 판정을 하고 `Broadcast` 먼저, `Hit` 나중 순서로 보고한다.
2. Task E 패시브 2개: 무기 프리팹 자식으로 `MoveSpeedPassive`와 `FlameHitPassive`를 붙인다. 플레이어에 직접 붙이지 않는다.
3. Task F 에셋 교체와 2클라이언트 테스트: 책, VFX, SFX 교체 후 광역 동시 히트와 도트 갱신을 확인한다.
