# 무기 발사·애니메이션 구조

Player는 WeaponSocket 아래의 무기를 `PlayerAttack.CurrentWeapon`으로 참조한다. 장착 위치, 모델, 기존 애니메이션 클립과 GUID를 유지한다.

## 책임과 계약

| 계약/클래스 | 책임 |
| --- | --- |
| `WeaponBase` | 장착 공통 타입, Attack Animation, Attack Cooldown, 오프라인 `Fire()` |
| `IChargeableWeapon` | 선택적 `FireCharged(float)`, 충전 미리보기 표시/해제 |
| `PlayerAttack` | 입력, 충전 시간, 쿨다운, 공격 무기 고정, 애니메이션과 발사 경로 선택 |
| `IAttackAuthority` | 온라인 애니메이션 전달 및 소유자 발사 진입점 |
| `IWeapon` | 온라인 소유자 판정 `Fire(attackId, direction, charge, sink)`와 피해 없는 `Present(shot)` |
| `IShotSink` / `DuelPlayer` | 연출 전파, 소유권 확인, 서버 피해 보고와 체력 동기화 |
| `DuelCombatState` | 서버 체력, 자기 피해 거절, `(공격자 슬롯, attackId, hitIndex)` 중복 제거 |

Basic Wand와 Light Orb는 충전 인수가 없는 `Fire()`를 구현한다. LightningWeapon과 DuelLightningWeapon은 `IChargeableWeapon`을 추가한다. 번개의 `Fire()`를 직접 호출하면 최소 충전인 `FireCharged(0f)`를 실행한다. 충전 시간은 기존 `PlayerAttack.maxChargeTime`을 사용한다.

DuelLightningWeapon의 온라인 `Fire(...)`는 명시적 인터페이스 구현이다. `((IWeapon)weapon).Fire(...)`로 호출하며 오프라인 발사를 경유하지 않는다. 기존 온라인 인터페이스 시그니처와 RPC 페이로드는 유지한다.

## 발사·상태 흐름

- 일반 무기: 버튼 누름 → 공격 애니메이션 → FireMagic → 발사.
- 충전 무기: 버튼 누름 → 충전/미리보기 → 버튼 놓음 → 충전값 확정 → 애니메이션 → FireMagic.
- 오프라인: Authority가 없으면 고정한 무기의 `Fire()` 또는 `FireCharged(ratio)`를 호출한다.
- 온라인: Authority가 있으면 DuelPlayer가 공격 시작 시 고정한 AttackingWeapon의 IWeapon.Fire를 소유자에서만 호출한다. 미지원 무기는 경고 후 발사를 생략하며 오프라인 피해 처리로 우회하지 않는다.

기본 완드의 즉시 공격은 충전을 기다리지 않는다는 의미이며 실제 발사 시점은 기존 Animation Event다. hasFired로 중복 발사를 차단한다. 무기가 없으면 공격하지 않는다.

온라인 애니메이션은 StartAttack → PlayAttackRpc → PlayReplicatedAttack 경로를 사용한다. 원격은 쿨다운 시계 차이로 재생이 누락되지 않도록 기존 쿨다운 우회 재생을 유지한다. 실제 명중은 소유자에서만 수행한다.

온라인 번개는 Trace → sink.Broadcast → sink.Hit 순서를 유지한다. 연출을 먼저 보낸 뒤 서버가 피해를 반영한다. ApplyHealth는 서버 체력의 로컬 반영이며 원격 연출에서 별도 피해를 계산하지 않는다. 기존 장착 패시브, 서버 승인 타격 패시브, 공격 속도 보정과 쿨다운 UI 계약도 유지한다.

CancelAttack은 충전 미리보기, 충전값, 공격 상태와 임시 Cast Override를 정리한다. 비활성화 및 온라인 사망·디스폰에서 호출한다. 원격 입력 컴포넌트는 이미 꺼져 있으므로 사망·디스폰에서 명시적으로 정리한다. 디스폰 후에도 Authority를 유지해 남은 이벤트가 오프라인 발사로 전환되지 않도록 한다. 이미 발사된 투사체 회수나 별도 취소 RPC는 포함하지 않는다.

## 장착과 애니메이션 소유권

PlayerAttack에서 HasActiveWand, animationOnlyWandAttack, 구체적인 완드 타입 검사를 제거했다. 실제 장착 무기로 공격과 포즈를 결정하며 Renderer 가시성만 바꿔 무기를 전환하지 않는다.

장착은 TryEquipWeapon으로 처리한다. 충전·공격 중 교체를 거절하고 null은 해제한다. EquippedWeaponChanged는 초기화와 장착 변경을 포즈 컴포넌트에 동기적으로 알린다. 이미 활성화된 무기를 장착한 직후 공격해도 다음 Update까지 포즈 적용을 기다리지 않는다.

BasicWandIdlePose는 자신의 WeaponBase와 부모 PlayerAttack을 기존 계층에서 찾아 장착된 경우에만 기존 Idle 컨트롤러를 적용한다. 해제 시 자신이 소유한 컨트롤러만 복원한다. 명시적 에디터 외형 미리보기의 가시성 조건은 유지한다.

공격 시 현재 포즈 컨트롤러를 저장하고 기존 Override의 Idle/Grip 매핑을 복사한 뒤 Cast 슬롯만 AttackAnimation으로 교체한다. EndAttack/CancelAttack에서 원래 컨트롤러를 복원한다. 원본 클립·컨트롤러 에셋 수정이나 런타임 Rebind/강제 Animator.Play는 추가하지 않는다.

읽기 전용 AnimationSourceController는 공격 Override 아래의 포즈 컨트롤러를 반환한다. BasicWandIdlePose는 공격 컨트롤러를 덮어쓰지 않으며, BasicWandGripSkin은 이 컨트롤러와 장착 무기를 확인해 공격 중에도 기존 Grip을 유지한다. 기존 Idle/Cast 및 이동→Cast 블렌드 조건을 유지한다. Light Orb의 손 포즈도 장착 여부로 결정하며 원격 입력 비활성화와 분리한다.

## 새 무기 추가

1. 독립 프리팹에 모델, 발사점과 WeaponBase.Fire 구현을 구성한다. 충전이 필요하면 IChargeableWeapon을 구현한다.
2. 현재 Wizard 골격용 Attack Animation을 지정하고 FireMagic, EndAttack 이벤트를 각각 한 번 포함한다. 미설정 클립은 기존 컨트롤러 모션을 사용한다.
3. 플레이어의 WeaponSocket 아래에 생성한 뒤 TryEquipWeapon으로 장착한다. Inspector 초기 설정은 Current Weapon을 사용한다.
4. 무기별 Idle/Grip 코드에서 장착 상태, EquippedWeaponChanged, AnimationSourceController를 사용할 수 있다. PlayerAttack에 무기 타입 분기를 추가하지 않는다.
5. 온라인 무기는 IWeapon으로 판정과 연출을 분리한다. 기본 완드 프리팹의 실제 플레이 인스턴스에는 Projectile Prefab, Fire Point, Attack Animation을 연결해야 한다.

## Basic Wand / Light Orb 온라인 후속 작업

두 무기는 아직 IWeapon을 구현하지 않아 온라인 발사를 지원하지 않는다.

- 소유자 투사체는 공격 ID와 sink를 유지하며 sink.Hit으로만 피해를 보고해야 한다. 단일 투사체는 hitIndex 0으로 한 번만 타격하고 자신을 제외한다.
- Present는 양쪽에서 연출만 생성/갱신한다. 오프라인 피해 투사체를 그대로 생성해 원격 충돌/피해를 중복 실행하면 안 된다.
- Arcane Bolt에는 시작 위치·방향·속도·공통 시각 또는 경과 시간, 충돌/종료 이벤트, 지연 수신 보정과 서버 피해 보고 구현이 필요하다.
- Light Orb는 소유자가 결정한 반사 지점·구간 순서·진행 시각과 종료를 전달하고 원격에서 해당 경로를 재생해야 한다.
- ShotEvent/ShotRpc에 시간·속도·경로 정보를 추가해야 한다. Charge에 다른 의미를 넣어 우회하지 않는다.
- 현재 연출 라우팅은 장착 중인 IWeapon을 사용한다. 이동 투사체 도입 전 공격 ID와 발사 무기/연출을 연결해 장비 교체 후 늦은 이벤트를 올바르게 처리해야 한다.
- 사망·취소·교체·디스폰 시 판정/연출 수명 정책과 오래된 이벤트 정리가 필요하다.

## PR 범위와 검증

이번 PR은 Arcane Bolt의 오프라인 벽·플레이어 충돌을 추가한다. 이동 구간을 SphereCastAll로 검색하고 거리순으로 소유자를 제외한 첫 충돌을 처리한다. 플레이어에게 10의 피해를 주고 투사체를 제거하며, 벽·장애물에서도 제거한다. 프리팹의 mask는 Player, Wall, Obstacle 레이어를 포함한다. BasicWandWeapon은 발사 시 소유자의 PlayerHealth를 전달한다. 온라인 Basic Wand 발사는 아직 지원하지 않는다.

공통 Cast 클립은 GUID·곡선·이벤트를 유지한 채 BaseCast.anim / WizardBaseCast로 이름을 변경했다. PlayerAttack의 교체 기준 필드는 baseCastClip으로 정리하고 FormerlySerializedAs로 기존 attackAnimationSlot 참조를 보존한다. 무기별 실제 모션은 각 무기의 Attack Animation에서 지정한다.

PlayerTest의 완드 발사 참조와 무기·캐릭터 활성 상태, 번개 프리팹 및 Light Orb 이동·반사의 검증용 단계 분리, WeaponSetupReview 검증 도구를 포함한다. Basic Wand의 Textures 폴더는 Light Orb 아래로 이동했으며 파일 내용과 GUID를 보존한다. 최신 main의 패시브·쿨다운·맵 변경은 유지한다.

사용자는 1차 로컬 작업에서 완드 발사/벽 충돌, 번개 차징/공격, Light Orb 발사/반사, 온라인 번개의 애니메이션/연출/피해 동기화를 확인했다. 이는 로컬 진행 코드가 포함된 검증이며 이 PR 통합본이나 2차 애니메이션 변경의 실행 검증이 아니다.

Unity 내장 Roslyn을 통한 통합본 컴파일 및 정적 비교 결과는 PR 본문에 기록한다. Unity 임포트/Netcode 후처리, EditMode/PlayMode 실행, 실제 두 클라이언트 검증은 별도다. 자동 테스트 씬은 생성하지 않는다.

수동 확인 항목:

- 완드의 Idle/Grip, 이동→Cast→Idle 전환과 공격 중 손 파지 유지
- 실제 장착 무기 교체/해제 후 포즈 잔류 및 무기 없는 상태의 공격 차단
- FireMagic 한 번 발사, EndAttack/취소/사망 후 복원과 다음 공격
- 충전, 쿨다운·공격 속도 보정·패시브·쿨다운 UI
- 두 클라이언트의 번개 애니메이션/연출 및 한 번의 서버 체력 감소

기존 WeaponSeparationSetup.Apply는 명시적 마이그레이션 도구이며 재실행할 필요가 없다. 기존 테스트 소스의 컴파일 확인과 실제 테스트 통과는 구분한다.

2026-11-05 통합본 검증: Unity 6000.3.24f1 내장 Roslyn과 캐시 참조로 WizardDuel.MapGeneration, Assembly-CSharp, Assembly-CSharp-Editor를 모두 컴파일했다. 종료 코드 0, 진단 출력 없음. Unity 임포트·Netcode 후처리와 EditMode/PlayMode 테스트는 실행하지 않았다.
