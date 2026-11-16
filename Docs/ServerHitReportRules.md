# 서버 명중 보고 처리 규칙

서버 경로는 `DuelPlayer.ReportHitRpc`(무기 명중 보고)와 `DuelPlayer.ServerApplyStatusTick`(서버 상태이상 도트 틱)이 `ServerHitReport.TryApply`를 부르고, 그 안의 `Evaluate`가 받을지 버릴지 결정하는 흐름이다.

`DuelCombatState.TryTakeHit`은 테스트용 얇은 입구다. 같은 `ServerHitReport.TryApply` 판정을 `TryApplyTargetHit`을 통해 부르지만, 대상 단위 상태이상 틱 조건으로 검사하며 방·전투 시작·무기 이력 검사는 하지 않고 판 종료 플래그도 전달하지 않는다. 실제 서버 수신 경로가 아니고 네트워크 체력 동기화나 로그도 처리하지 않는다.

## 기본 전제

- **쏜 쪽 판정 신뢰**: 악의적인 플레이어가 없다는 전제에서 피해량, 명중 여부, 사거리, 공격 간격은 공격한 쪽(`ReportHitRpc` 송신자) 판정을 그대로 사용한다. 서버가 적중 여부를 다시 계산해 깎거나 보정하지 않는다.
- **무기 기준 적중 효과**: 보고가 받아들여지면 `PlayerWeaponPassiveController.NotifyServerHit`가 공격자의 지금 손에 든 무기가 아니라 보고에 실린 `AttackInfo(attackId, weaponId)`의 `weapon` 기준으로 적중 효과를 건다.
- **이동 무검사·무로그**: 서버는 플레이어 이동(`DuelMoveSync`)이나 이동 속도를 검사하지 않으며, 이동 거절·경고 로그도 남기지 않는다.
- **버릴 때 후속 처리**: `DuelPlayer`가 `ServerHitReport`까지 전달한 보고를 거절하면 연결을 끊거나 클라이언트에 거절 알림 RPC를 보내지 않고, 서버 콘솔에 이유와 공격 신원(`attacker=`, `target=`, `attack=`, `weapon=`, `hit=`, `damage=`)을 한 줄 로그로 남긴다. 판정에 도달하기 전의 무로그 반환은 아래 별도 절을 따른다.
- **동시 마지막 일격**: 두 플레이어가 거의 동시에 마지막 일격을 보고하면 서버가 먼저 처리한 쪽의 보고만 `Accepted`로 받아 대상을 쓰러뜨리고 판을 끝낸다(`DuelArena.EndCombat` / `ServerDisableCombat`). 직후 도착한 상대의 마지막 일격 보고는 이미 끝난 판의 보고이므로 `RoundEnded`로 버린다.

---

## 판정 순서와 규칙 목록

`ServerHitReport.Evaluate`는 다음 순서로 검사한다. 처음 걸린 거절 이유(`HitReportDecision`)를 돌려주며, 거절하면 대상 체력과 타격 기록을 건드리지 않는다.

| 검사 | 무기 보고 순서 | 상태이상 틱 순서 | 판정 결과 |
|---|---:|---:|---|
| 공격자 측 판 종료 | 1 | 검사 안 함 | `RoundEnded` |
| 공격자 전투 입력 비활성 | 2 | 검사 안 함 | `CombatNotStarted` |
| 대상 측 판 종료(틱 전용 선검사) | 검사 안 함 | 1 | `RoundEnded` |
| 대상 없음 / 대상 combat 없음 | 3 | 2 | `MissingTarget` |
| 같은 Match가 아님 | 4 | 검사 안 함 | `DifferentMatch` |
| 대상 측 판 종료(같은 Match 확인 뒤) | 5 | 위에서 검사 | `RoundEnded` |
| 공격자 슬롯과 대상 슬롯이 같음 | 6 | 3 | `SelfHit` |
| 대상 체력 0 이하 | 7 | 4 | `TargetAlreadyDefeated` |
| 피해가 유한 양수가 아님 | 8 | 5 | `InvalidDamage` |
| 이번 판에 든 적 없는 무기 | 9 | 검사 안 함 | `WeaponNotHeld` |
| 기록되지 않은 공격 번호·무기 조합 | 10 | 검사 안 함 | `UnknownAttack` |
| 대상이 같은 공격자·공격·타격을 이미 받음 | 11 | 6 | `DuplicateHit` |
| 모두 통과 | 12 | 7 | `Accepted` |

예를 들어 대상이 다른 Match이면서 판도 끝났다면 무기 보고는 `DifferentMatch`가 먼저다. 아래 번호는 사유별 설명 번호이며 검사 순서는 위 표를 따른다.

### 1. 판 종료 뒤 보고 (`RoundEnded`)

- **조건**:
  - 무기 명중 보고(`!IsStatusTick`): 공격자 측 판이 끝났거나(`AttackerRoundEnded == true`, `DuelMatch.CombatEnded` 또는 `DuelPlayer.combatEnded`), 같은 방 대상 측 판이 끝났을 때(`TargetRoundEnded == true`, `SameMatch` 확인 뒤 검사).
  - 상태이상 틱(`IsStatusTick`): 대상 측 판이 끝난 상태(`TargetRoundEnded == true`).
- **처리**: 버림 (`HitReportDecision.RoundEnded`). 상태이상 틱이면 대상의 남은 상태이상도 `ClearServerStatusEffects()`로 즉시 비워 매 프레임 반복 로그가 나지 않게 한다.
- **서버 로그**:
  - `[Combat] Hit dropped reason=RoundEnded attacker=<슬롯> target=<슬롯|missing|invalid> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
  - `[Combat] DoT dropped reason=RoundEnded attacker=<슬롯> target=<대상슬롯> attack=<도트번호> weapon=0 hit=<틱번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 승부가 난 뒤에도 1초 안팎 날아가던 투사체·폭발 화염 공(`ExplosionFireball`)이 연출상 끝까지 날아가며 지연 보고를 보내거나, 판 종료 직전에 보낸 보고가 네트워크 지연으로 `EndCombat` / `ServerDisableCombat` 이후 서버에 도착할 때 생긴다. `PlayerStatusEffects`에서도 `DuelPlayer.IsServerCombatEnded`를 보고 미리 상태를 정리해 틱이 계속 돌지 않게 막는다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`, `ServerHitReportTests.StatusTick_IgnoresAttackerCombatAndMatch_RejectsRoundEndedDuplicatesAndDefeated`, `ServerHitReportTests.SimultaneousLastHits_FirstProcessedWinsAndLaterReportIsDropped`

### 2. 전투 시작 전 보고 (`CombatNotStarted`)

- **조건**: 무기 명중 보고(`!IsStatusTick`)에서 판은 끝나지 않았지만 공격자의 `combatEnabled.Value`가 거짓(`AttackerCombatEnabled == false`)일 때.
- **처리**: 버림 (`HitReportDecision.CombatNotStarted`). (상태이상 틱은 공격자의 `combatEnabled`를 보지 않는다. 오프라인 테스트 더미나 공격자가 먼저 쓰러진 뒤에도 이미 붙은 화상은 계속 틱한다.)
- **서버 로그**:
  - `[Combat] Hit dropped reason=CombatNotStarted attacker=<슬롯> target=<대상> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 대기·카운트다운 중이거나 방 정리·전투 비활성화 전환 직후에 어긋난 보고가 들어올 때 생긴다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`, `ServerHitReportTests.StatusTick_IgnoresAttackerCombatAndMatch_RejectsRoundEndedDuplicatesAndDefeated`

### 3. 대상 없음 · 이전 판 오브젝트 디스폰 (`MissingTarget`)

- **조건**: `target.TryGet(...)` 실패, 대상이 아직 스폰되지 않았거나 이미 디스폰됨, 또는 대상 `DuelPlayer` / `DuelCombatState`가 없음(`!HasTarget || TargetCombat == null`).
- **처리**: 버림 (`HitReportDecision.MissingTarget`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=MissingTarget attacker=<슬롯> target=missing(netId=<netId>) attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>` (네트워크 오브젝트는 있으나 `DuelPlayer`가 없으면 `invalid(netId=<netId>)`, 대상 `combat`이 없으면 `<슬롯>`)
  - `[Combat] DoT dropped reason=MissingTarget attacker=<슬롯> target=<대상슬롯> attack=<도트번호> weapon=0 hit=<틱번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 판이 새로 시작되면서 이전 판의 플레이어 `NetworkObject`가 디스폰되었거나 상대가 방을 나간 직후 지연 보고가 도착했을 때 생긴다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`

### 4. 다른 방(매치) 플레이어 보고 (`DifferentMatch`)

- **조건**: 무기 명중 보고(`!IsStatusTick`)에서 `player.Match != Match`(`SameMatch == false`)일 때.
- **처리**: 버림 (`HitReportDecision.DifferentMatch`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=DifferentMatch attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 서버 한 프로세스에 여러 `DuelMatch` 방이 있을 때 잘못된 대상 참조나 이전 방 객체와 섞인 보고를 막는다. 오프라인 테스트(`DuelOfflineTest`)와 `AiDuel`은 양쪽 모두 같은 `Match`(오프라인 더미·플레이어는 둘 다 `null == null`)이므로 정상 통과한다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`

### 5. 자기 자신 타격 (`SelfHit`)

- **조건**: `AttackerSlot == TargetCombat.Slot`.
- **처리**: 버림 (`HitReportDecision.SelfHit`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=SelfHit attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 범위 공격이나 근접·폭발 콜라이더 겹침으로 쏜 본인 `NetworkObject`가 보고 대상에 섞여 들어올 때 걸러낸다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`

### 6. 이미 쓰러진 대상에 대한 보고 (`TargetAlreadyDefeated`)

- **조건**: `TargetCombat.IsDefeated == true`.
- **처리**: 버림 (`HitReportDecision.TargetAlreadyDefeated`). 상태이상 틱이면 남은 상태이상도 `ClearServerStatusEffects()`로 비운다.
- **서버 로그**:
  - `[Combat] Hit dropped reason=TargetAlreadyDefeated attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
  - `[Combat] DoT dropped reason=TargetAlreadyDefeated attacker=<슬롯> target=<대상슬롯> attack=<도트번호> weapon=0 hit=<틱번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 대상이 다른 공격이나 도트로 막 쓰러진 직후, 이미 날아가던 지연 투사체·범위 폭발 피해 또는 도트 틱이 뒤늦게 도착할 때 생긴다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`, `ServerHitReportTests.StatusTick_IgnoresAttackerCombatAndMatch_RejectsRoundEndedDuplicatesAndDefeated`

### 7. 잘못된 피해량 (`InvalidDamage`)

- **조건**: `Damage`가 `NaN`이거나 무한대이거나 `Damage <= 0f`.
- **처리**: 버림 (`HitReportDecision.InvalidDamage`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=InvalidDamage attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 체력 변동이 없는 0 피해 보고나 부동소수점 오류 값이 서버 상태에 들어가는 것을 막는다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`

### 8. 이번 판에 든 적 없는 무기 id (`WeaponNotHeld`)

- **조건**: 무기 명중 보고(`!IsStatusTick`)에서 서버 `ServerAttackHistory.HasHeldWeapon(weaponId)`가 거짓(`weaponId == 0` 포함)일 때.
- **처리**: 버림 (`HitReportDecision.WeaponNotHeld`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=WeaponNotHeld attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
- **왜 적용했는지 (정상 클라이언트 동작 포함)**: 서버는 스폰(`OnNetworkSpawn`), 전투 시작(`ServerEnableCombat`), 무기 교체·획득(`EquippedWeaponChanged`) 때마다 해당 플레이어가 이번 판에 든 무기 id를 `heldWeapons`에 누적한다. 따라서 **이전 무기로 쏜 뒤 다른 무기로 바꾼 후 뒤늦게 도착한 명중 보고**도 `heldWeapons`에 있으므로 정상 통과한다. 반면 든 적 없는 무기 id가 보고에 실리면 `NotifyServerHit → PlayerAttack.FindWeapon`이 카탈로그에서 엉뚱한 무기를 즉석 생성하기 전에 차단한다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`, `ServerHitReportTests.WeaponHit_AcceptsDelayedProjectileAfterWeaponSwap_AndMultiVictimOrMultiTickHits`, `ServerHitReportTests.ServerAttackHistory_TracksRoundWeaponsAndAttacks_RejectsUnknownOrMismatched_AndClears`

### 9. 서버에 시작 기록이 없는 공격 (`UnknownAttack`)

- **조건**: 무기 명중 보고(`!IsStatusTick`)에서 `ServerAttackHistory.HasStartedAttack(new AttackInfo(attackId, weaponId))`가 거짓(`attackId == 0`, 미등록 공격 번호, 또는 시작 시 등록한 무기 id와 보고의 `weaponId` 불일치)일 때.
- **처리**: 버림 (`HitReportDecision.UnknownAttack`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=UnknownAttack attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
- **왜 적용했는지 (정상 클라이언트 동작 포함)**: 온라인 원격 소유자는 `IAttackAuthority.StartAttack → PlayAttackRpc` 순서로 NGO `Reliable` RPC를 보내므로 서버는 `ReportHitRpc`보다 먼저 `PlayAttackRpc`에서 `(attackId, weaponId)`를 기록한다. 호스트 소유(`DuelOfflineTest` 플레이어·`AiDuel` 봇)도 `StartAttack`에서 `if (IsServer)`로 같은 기록을 남긴다. 덕분에 판 경계·미등록·변조 공격 보고는 거르고, 정상 클라이언트의 지연 투사체나 오프라인·AI 봇 공격은 모두 통과한다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`, `ServerHitReportTests.WeaponHit_AcceptsDelayedProjectileAfterWeaponSwap_AndMultiVictimOrMultiTickHits`, `ServerHitReportTests.ServerAttackHistory_TracksRoundWeaponsAndAttacks_RejectsUnknownOrMismatched_AndClears`

### 10. 같은 공격의 같은 타격 중복 (`DuplicateHit`)

- **조건**: `TargetCombat.HasHit(AttackerSlot, AttackId, HitIndex)`가 참일 때.
- **처리**: 버림 (`HitReportDecision.DuplicateHit`).
- **서버 로그**:
  - `[Combat] Hit dropped reason=DuplicateHit attacker=<슬롯> target=<대상슬롯> attack=<번호> weapon=<무기id> hit=<번호> damage=<피해>`
  - `[Combat] DoT dropped reason=DuplicateHit attacker=<슬롯> target=<대상슬롯> attack=<도트번호> weapon=0 hit=<틱번호> damage=<피해>`
- **정상 클라이언트에서 생기는 이유**: 같은 공격(`attackId`)의 동일 타격(`hitIndex`)이 중복 보고되거나, 다단 히트·범위 공격 인덱스가 겹칠 때 한 번만 피해를 반영한다. 반대로 범위 공격의 서로 다른 대상이나 서로 다른 `hitIndex`(예: 0, 1, 2)는 각각 `Accepted`로 받는다.
- **테스트**: `ServerHitReportTests.Evaluate_ReturnsExactRejectionReasonsWithoutMutatingStateOnReject`, `ServerHitReportTests.WeaponHit_AcceptsDelayedProjectileAfterWeaponSwap_AndMultiVictimOrMultiTickHits`, `ServerHitReportTests.StatusTick_IgnoresAttackerCombatAndMatch_RejectsRoundEndedDuplicatesAndDefeated`

### 11. 정상 수락 (`Accepted`)

- **조건**: 위 1~10 거절 규칙을 모두 통과했을 때.
- **처리**: 받음 (`HitReportDecision.Accepted`). 대상 체력을 `Damage`만큼 깎고(최소 0), `(AttackerSlot, AttackId, HitIndex)`를 기록하며, 무기 명중 보고라면 `weaponId` 기준으로 `NotifyServerHit` 적중 효과를 적용한다. 체력이 0이 되면 대상의 상태이상을 지우고 패배(`defeatedNow = true`)를 처리한다.
- **서버 로그**:
  - `[Combat] Hit slot=<대상슬롯> attacker=<공격자슬롯> id=<번호> weapon=<무기id> hit=<번호> health=<남은체력>`
  - `[Combat] DoT slot=<대상슬롯> attacker=<공격자슬롯> id=<도트번호> tick=<틱번호> health=<남은체력>`
- **정상 클라이언트 포함 경로**: 일반 직격·즉시 명중, 무기 교체 뒤 지연 명중(폭발 화염 공 등), 범위 공격의 여러 대상·다단 타격, 상태이상 도트 틱, 로컬 호스트(`DuelOfflineTest`)와 AI 대전(`AiDuel`) 보고.
- **테스트**: `ServerHitReportTests.WeaponHit_AcceptsDelayedProjectileAfterWeaponSwap_AndMultiVictimOrMultiTickHits`, `ServerHitReportTests.StatusTick_IgnoresAttackerCombatAndMatch_RejectsRoundEndedDuplicatesAndDefeated`, `ServerHitReportTests.SimultaneousLastHits_FirstProcessedWinsAndLaterReportIsDropped`, `ServerHitReportTests.FormatDropLog_ReturnsSingleLineWithReasonAndAttackIdentity`

## 판정 전에 로그 없이 반환하는 경로

다음은 `ServerHitReport`의 거절 로그를 만들기 전 단계다. 모든 무시·정리가 서버의 `dropped` 로그로 남는 것은 아니다.

- `DuelPlayer.ReportHitRpc`와 `ServerApplyStatusTick`은 서버가 아니거나 아직 스폰되지 않았으면 즉시 반환한다. `IShotSink.Hit`도 스폰된 소유자가 아니거나 대상에 `DuelPlayer`가 없으면 보고 RPC를 보내지 않는다.
- `DuelPlayer`의 공격 시작·발사 입구는 스폰·소유권·전투 입력·사망 조건으로 먼저 반환할 수 있다. `RecordServerStartedAttack`은 서버/스폰 여부, 판 종료, 입력 비활성, 사망 조건에서 공격 기록을 건너뛴다. 이 반환에는 보고 거절 로그가 없다. 실제 무기를 찾지 못한 발사와 패시브 적용은 별도 경고를 남긴다.
- `PlayerStatusEffects.Update`는 서버가 아니거나 미스폰이면 반환한다. 대상이 이미 쓰러졌거나 판이 끝났으면 남은 화상·둔화를 지우고 반환해, 보통 다음 틱이 `ServerApplyStatusTick`까지 도달하지 않는다. 틱 시간이 아니거나 화상이 없을 때도 보고를 만들지 않는다.
- `ServerApplyBurn`·`ServerApplySlow`는 서버/스폰 여부나 대상의 사망·판 종료 조건에서 로그 없이 반환한다. 화상의 피해·횟수·기간이 `<= 0`이거나, 둔화 배율이 `<= 0` 또는 `>= 1`이거나 기간이 `<= 0`이면 적용하지 않는다. `DuelPlayer.ServerInflictStatus` 역시 서버가 아니거나 대상이 없으면 반환하고, 양수가 아닌 틱 횟수·둔화 기간의 적용 호출은 건너뛴다.

반대로 실제 `ServerApplyStatusTick` 호출이 판정까지 도달해 `RoundEnded`나 `TargetAlreadyDefeated`가 나오면 `DoT dropped` 로그를 남기고 남은 상태이상을 정리한다.
