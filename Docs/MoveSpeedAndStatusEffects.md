# 이동 속도 배율과 상태이상

이동 속도·공격 속도 배율과 화상(Burn)·둔화(Slow)가 어떻게 걸리고 풀리는지 정리한다. 무기 패시브를 만드는 방법은 [무기 패시브와 쿨다운](WeaponPassivesAndCooldown.md)을 본다.

## 속도 배율

- `PlayerCombatStats`(`Assets/Game/Player/Scripts/`)가 플레이어마다 이동 속도와 공격 속도 배율을 모은다. `SetMoveSpeedModifier(this, 값)`·`SetAttackSpeedModifier(this, 값)`처럼 효과를 건 컴포넌트를 키로 쓰고, 풀 때는 같은 키로 `Remove…Modifier`를 부른다. 키가 다르면 서로 덮어쓰지 않는다.
- 같은 종류의 배율은 모두 곱하고, 결과와 각 값의 최소는 0.01이다.
- 이동 속도는 `PlayerMotor.MoveSpeed` = 기본 속도(`PlayerMotor.BaseSpeed`, 5) × 이동 속도 배율이다. 이동은 소유자 화면에서 계산하므로 배율도 소유자 화면에서 적용되어야 실제 이동에 반영된다.
- 공격 간격은 무기의 `AttackCooldown` ÷ 공격 속도 배율이다.

## 상태이상

`PlayerStatusEffects`(`DuelWizard-1` 프리팹에 붙어 있고 `DuelWizard-2`가 물려받음)가 서버 권위로 화상과 둔화를 관리한다. 적중 패시브는 `DuelPlayer.ServerInflictStatus`로 상태이상을 건다.

- **화상**: `ServerApplyBurn(공격자 슬롯, 틱 피해, 틱 수, 지속 시간)`. 지속 시간을 틱 수로 나눈 간격마다 피해를 한 번씩 준다. 틱 피해는 `DuelPlayer.ServerApplyStatusTick`을 거쳐 일반 명중과 같은 `ServerHitReport`로 들어가므로, 판이 끝났거나 이미 쓰러진 대상에게는 들어가지 않는다([서버 보고 처리 규칙](ServerHitReportRules.md)). 화상 틱의 공격 번호는 최상위 비트(`StatusEffectState.DotAttackIdFlag`)를 켜서 일반 공격 번호와 겹치지 않게 한다.
- **둔화**: `ServerApplySlow(배율, 지속 시간)`. 서버가 배율을 `NetworkVariable`로 보내고, 각 화면의 `PlayerStatusEffects`가 그 값으로 `PlayerCombatStats`의 이동 속도 배율을 건다. 배율은 0보다 크고 1보다 작아야 한다.
- 같은 종류를 다시 맞으면 쌓지 않고 갱신한다. 화상은 틱을 처음부터 다시 세고, 둔화는 만료 시각을 지금부터 다시 잡는다.
- 대상이 쓰러지거나 판이 끝나면 남은 화상과 둔화를 모두 지운다.
- 화상 중인지(`BurnVisible`)도 동기화하지만, 아직 이 값을 쓰는 화면 표시는 없다.

틱 계산 규칙은 MonoBehaviour 없이 `StatusEffectState`에 있고, `Assets/Tests/Editor/Player/StatusEffectStateTests.cs`가 검사한다.

## 지금 쓰는 곳: 화염 향로

화염 향로 프리팹(`Assets/Game/Weapons/FireCenser/Prefabs/DuelFireCenser.prefab`)에 두 패시브가 붙어 있다.

| 컴포넌트 | 종류 | 프리팹 값 |
|---|---|---|
| `CenserMoveSpeedPassive` | 장착 패시브 | 들고 있는 동안 이동 속도 ×1.5 |
| `CenserFlameHitPassive` | 적중 패시브 | 화상 3 피해 × 3틱(3초), 둔화 ×0.7(1.5초) |

다른 무기에서 화상이나 둔화를 쓰려면 `CenserFlameHitPassive`처럼 `IWeaponHitPassive`를 구현한 컴포넌트를 무기 프리팹에 붙이고 `ServerInflictStatus`를 부른다.
