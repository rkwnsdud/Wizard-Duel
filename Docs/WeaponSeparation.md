# 무기 분리 (#48)

기존 캐릭터 FBX와 지팡이 FBX는 이미 별도 모델이다. 이번 변경은 Player 안에 저장되어 있던 지팡이 구성(모델, FirePoint, LightningWeapon)을 `Assets/Weapons/Lightning Weapon/LightningStaff.prefab`으로 추출한다. Player는 오른손의 WeaponSocket 아래에 이 프리팹을 중첩해 사용한다. 기존 모델, 장착 위치, 크기, 캐릭터 색상은 유지한다.

공격 모션은 각 `WeaponBase`의 **Attack Animation**에서 지정한다. PlayerAttack은 공통 이동 컨트롤러의 Cast 모션만 해당 클립으로 교체하고 공격 종료 시 원래 컨트롤러를 복원한다. 이동 상태와 파라미터는 유지한다. 기존 공격 클립은 GUID·애니메이션 곡선·이벤트를 유지해 `Assets/Weapons/Common/BaseCast.anim`으로 옮겼으며 오프라인/온라인 번개 무기에 연결했다.

PlayerAttack의 **Base Cast Clip**은 공통 Animator의 Cast 상태에 연결된 원본 클립을 식별하는 교체 기준이다. 실제로 재생할 무기별 모션은 각 무기의 **Attack Animation**에서 지정한다. 기존 **Attack Animation Slot**(`attackAnimationSlot`)은 이 역할을 명확히 하도록 **Base Cast Clip**(`baseCastClip`)으로 이름을 바꿨다. 기존 직렬화 참조는 `FormerlySerializedAs`로 호환되며, Player 프리팹도 동일한 클립 참조를 유지한다. 공통 원본 클립의 파일명은 `BaseCast.anim`, 내부 이름은 `WizardBaseCast`로 정리했다(이전: `StaffCast.anim` / `WizardStaffCast`). 번개·Light Orb도 공유하는 기본 시전 모션이며, 완드 장착 시에는 완드의 Attack Animation으로 교체된다. 클립 GUID와 곡선·이벤트는 유지했다.

새 무기를 추가할 때:

1. 모델, 발사점, `WeaponBase.Fire()` 구현을 독립 무기 프리팹에 구성한다. 차징 무기만 `IChargeableWeapon`을 추가한다.
2. Attack Animation에 현재 Wizard 골격용 클립을 지정한다. 클립에는 `FireMagic`과 `EndAttack` 이벤트를 각각 한 번 넣는다.
3. WeaponSocket 아래에 장착하고 PlayerAttack의 Current Weapon에 연결한다. 런타임에는 먼저 해당 플레이어 아래에 생성한 뒤 `TryEquipWeapon(weapon)`을 호출할 수 있다. 충전·공격 중에는 교체가 거절된다. `TryEquipWeapon(null)`은 무기를 해제한다.

공격은 시작 시 선택된 무기에 고정되며 FireMagic 중복 호출은 한 번만 처리한다. 무기를 해제한 상태에서는 공격을 시작하지 않는다. 독립 지팡이의 오디오 참조는 장착된 플레이어의 AudioSource에서 찾는다.

공격과 런타임 포즈는 실제 `Current Weapon`을 기준으로 결정한다. Basic Wand를 장착하면 기존 Idle·Cast와 Grip을 사용하며 Arcane Bolt를 발사한다. 모델의 Renderer를 켜거나 끄는 것만으로 무기가 바뀌지 않는다. 2차 작업 시작 시 로컬 PlayerTest의 Player에는 Light Orb가 선택되어 있었으며 씬 설정은 변경하지 않았다. 공격 클립이 미설정인 기존 프로토타입 무기는 기존 컨트롤러 모션을 사용한다. 네트워크에서 장비 변경을 전달하는 기능과 무기 선택 UI는 추가하지 않는다.

PlayerTest의 Player와 Dummy Player에 별도로 추가된 LightningWeapon·LightOrbWeapon 컴포넌트 4개에도 `BaseCast.anim`을 명시적으로 연결했다. 두 무기는 기존 공통 시전 모션을 계속 사용한다. 각 무기의 Attack Animation을 다른 클립으로 바꾸면 무기별 모션을 지정할 수 있다.

## 검증 방법

2026-11-04 무기 분리 당시 최종 배치 실행: 아래 테스트 9개 모두 통과. 결과는 로컬 `Logs/WeaponSeparation-final-tests.xml`, 실행 로그는 `Logs/WeaponSeparation-final-tests.log`에 있다. 이 기록은 아래 2026-11-05 API 리팩터링의 실행 검증 결과가 아니다.

Unity 6000.3.24f1의 EditMode Test Runner에서 `WeaponSeparationTests`와 `DuelLightningWeaponTests`를 실행한다. 새 테스트는 Player/Player-1/Player-2/DuelWizard-1/DuelWizard-2의 무기·소켓·클립·이벤트 참조, PlayerTest의 무기와 완드 참조, 실제 Animator의 공격 종료 이벤트, 발사 중복 방지, 공격 중 교체 제한과 무기 해제를 검사한다. 온라인 두 클라이언트 간 실플레이와 최종 외형은 별도로 확인한다.

`Assets/Editor/WeaponSeparationSetup.cs`는 명시적으로 실행하는 마이그레이션/참조 검증 코드다. 완성 에셋을 사용하기 위해 재실행할 필요는 없다. `Apply`를 다시 실행해도 이미 추출한 지팡이는 재생성하지 않는다.

## 2026-11-05: 발사·차징 계약 1차 리팩터링

이번 단계는 발사 API와 온라인 확장 경계를 정리한다. Basic Wand와 Light Orb의 온라인 공격 구현, 완드 애니메이션 구조 정리, 씬 생성, 자동 Play 모드 테스트는 포함하지 않는다.

### 책임과 계약

| 계약/클래스 | 책임 |
| --- | --- |
| `WeaponBase` | 장착 참조의 공통 타입, 기존 Attack Animation, 로컬/오프라인 `Fire()` |
| `IChargeableWeapon` | 선택적인 `FireCharged(float)`, `ShowChargePreview(float)`, `HideChargePreview()` |
| `PlayerAttack` | 입력, 충전 시간, 공격 시작 시 무기·충전값 고정, 애니메이션 이벤트, 발사 경로 선택, 취소 |
| `IAttackAuthority` | 온라인 애니메이션 전달 및 소유자 발사 진입점. 존재하면 오프라인으로 우회하지 않음 |
| `IWeapon` | 온라인 소유자 명중 판정 `Fire(attackId, direction, charge, sink)`와 피해 없는 `Present(shot)` |
| `IShotSink` / `DuelPlayer` | 연출 전파, 서버에 피해 보고, 소유권·전투 상태 확인, RPC 및 체력 동기화 |
| `DuelCombatState` | 서버 체력, 자기 피해 거절, `(공격자 슬롯, attackId, hitIndex)` 중복 제거 |

`WeaponBase`에서 `UsesCharge` 및 충전 미리보기 메서드를 제거했다. Basic Wand와 Light Orb는 `Fire()`만 구현한다. `LightningWeapon`과 `DuelLightningWeapon`은 `IChargeableWeapon`을 구현하며, 직접 `Fire()`를 호출하면 `FireCharged(0f)`로 최소 충전 공격을 한다. 충전 시간은 기존 `PlayerAttack.maxChargeTime` 값을 그대로 사용한다.

`DuelLightningWeapon`의 온라인 `Fire(...)`는 명시적 인터페이스 구현이다. `((IWeapon)weapon).Fire(...)`로 호출하며 기존 인터페이스 시그니처는 유지했다. 오프라인 `Fire()`/`FireCharged()`와 온라인 발사를 구분하기 위한 변경이다. 온라인 구현은 오프라인 발사 메서드를 호출하지 않는다.

### 입력과 발사 경로

- 일반 무기: 버튼 누름 → 공격 애니메이션 → `FireMagic` → 발사. 기본 완드의 즉시 공격은 충전을 기다리지 않는다는 의미이며 실제 발사 시점은 기존 Animation Event다.
- 충전 무기: 버튼 누름 → 충전/미리보기 → 버튼 놓음 → 충전값 확정 및 공격 애니메이션 → `FireMagic` → 발사.
- 오프라인: `Authority == null`이면 고정한 무기의 `Fire()` 또는 `IChargeableWeapon.FireCharged(ratio)`를 호출한다.
- 온라인: `Authority != null`이면 `IAttackAuthority.FireMagic(ratio)`만 호출한다. `DuelPlayer`는 `PlayerAttack.AttackingWeapon`의 `IWeapon.Fire(...)`를 소유자에서만 호출한다. 미지원 무기는 경고 후 발사를 건너뛴다. 로컬 피해를 주는 `Fire()`로 우회하지 않는다.

온라인 애니메이션은 기존 `StartAttack` → `PlayAttackRpc` → 원격 `PlayAttack` 경로를 사용한다. 원격 Animation Event는 소유권 검사로 실제 발사 판정을 실행하지 않는다.

온라인 번개는 `Trace` → `sink.Broadcast` → `sink.Hit` 순서를 유지한다. `Broadcast`는 소유자에서 `Present`를 실행하고 `ShotRpc`로 상대 화면에도 연출을 전달한다. `Hit`는 `ReportHitRpc`를 보내며 서버 `DuelCombatState`가 중복을 제거하고 체력을 반영한다. `ApplyHealth`의 `TakeDamage()` 호출은 서버가 결정한 체력의 로컬 반영이며, 클라이언트의 별도 명중 피해 계산이 아니다.

### 상태와 호환성

- 기존 `hasFired`가 한 공격의 Animation Event 중복 발사를 막는다.
- 충전·공격 중 `TryEquipWeapon`은 계속 거절된다. 온라인 발사도 공격 시작 시 고정한 무기를 사용한다.
- `CancelAttack()`은 충전 미리보기, 충전값, 공격 상태, 공격 무기 참조와 임시 Cast Override를 정리한다. 비활성화 및 온라인 사망·디스폰에서 호출한다. 원격 플레이어는 입력 컴포넌트가 이미 꺼져 있으므로 사망·디스폰에서 명시적으로 호출한다.
- 디스폰 시 입력·이동을 끄고 `Authority` 참조는 유지한다. 남은 이벤트가 오프라인 발사로 전환되지 않도록 한다.
- `CancelAttack()`은 이미 발사된 투사체를 회수하거나 독립적인 취소 RPC를 보내는 API가 아니다. 이동 투사체 수명 및 임의 취소의 원격 전파는 후속 온라인 구현에서 처리해야 한다.
- `IWeapon`, `IShotSink`, `ShotEvent`, `IAttackAuthority`의 기존 시그니처와 RPC 페이로드는 유지했다. `DuelCombatState`와 기존 온라인 번개의 판정·피해 계산·연출 본문도 유지했다.
- 1차에서는 `HasActiveWand`, `BasicWandWeapon` 타입 확인 등 기존 미리보기 예외를 유지했다. 아래 2차 작업에서 제거했다. 일반/충전 구분은 계속 인터페이스로 처리한다.

### Basic Wand / Light Orb 온라인 구현에 필요한 후속 작업

두 무기는 아직 `IWeapon`을 구현하지 않으므로 온라인 발사는 지원하지 않는다. 각 무기에 다음 계약을 구현해야 하며 `PlayerAttack`의 무기별 분기는 필요 없다.

1. `IWeapon.Fire(...)`: 소유자에서만 충돌을 판정한다. 공격 ID와 sink를 투사체 수명 동안 유지하고 피해는 `sink.Hit`으로 보고한다. 원격 연출 코드와 분리한다.
2. `IWeapon.Present(...)`: 소유자와 상대 모두에서 연출만 생성·갱신한다. 현재 오프라인 투사체를 그대로 생성해 충돌/피해 Update를 중복 실행하면 안 된다.
3. Arcane Bolt: 시작 위치·방향·속도·동기화된 생성 시각 또는 경과 시간, 충돌/종료 이벤트가 필요하다. 지연 수신 시 경과 시간에 맞게 위치를 보정하고 종료된 투사체가 뒤늦게 다시 생성되지 않도록 처리한다. 현재 로컬 `Initialize(owner)`와 벽 `SphereCast`를 기반으로 소유자 충돌 경로를 확장한다. 캐릭터 피해 구현도 아직 필요하다.
4. Light Orb: 소유자가 결정한 반사 지점·구간 순서·진행 시각과 종료를 전달해야 한다. 원격에서는 전달된 경로를 재생하고 별도의 반사/명중 판정을 하지 않는다. 기존 `LightOrbProjectile.TakeDamage()`는 오프라인 전용으로 두거나 명확히 분리한다.
5. `ShotEvent`/`ShotRpc`: 현재는 `AttackId`, `Kind`, `Start`, `Point`, `Charge`만 전달한다. 이동 속도·시각·구간 순서 등 필요한 필드를 해당 무기 구현과 함께 추가한다. `Charge`에 다른 의미의 값을 넣어 우회하지 않는다.
6. 발사 후 라우팅: 현재 `Broadcast`/`ShotRpc`는 장착 중인 `IWeapon`에 연출을 전달하며 즉시 판정 번개에 맞춰져 있다. 이동 투사체 도입 전 공격 ID와 발사 무기/연출 인스턴스를 연결해야 한다. 장비 동기화 시에는 양쪽에서 동일하게 식별할 무기 키도 필요하다. 늦은 반사/종료 이벤트를 새 장착 무기에 전달하면 안 된다.
7. 수명과 중복: 한 발은 `hitIndex = 0`으로 한 번만 보고하고 첫 타격 후 종료한다. 자기 소유자를 제외한다. 실제 다단 타격만 hitIndex를 증가시킨다. 서버의 기존 중복 방지도 유지한다. 사망·취소·장비 교체·디스폰 시 활성 판정과 연출을 종료/유지할 정책 및 오래된 이벤트 처리까지 구현한다.

### 보존 및 이번 검증

수정 전 미커밋 파일과 수정 대상 원본은 로컬 `Logs/WeaponRefactor-backup-20261105-105717/`에 보관했으며 `manifest.json`에 SHA-256이 있다. PlayerTest 씬, Arcane Bolt 프리팹과 투사체 소스는 수정하지 않았다. Basic Wand의 변경은 발사 시그니처뿐이며 `PlayerHealth` 조회, 투사체 생성, `Initialize(owner)` 본문을 유지했다. 기존 `.meta`, 애니메이션·컨트롤러·Grip·모델·VFX 에셋과 직렬화 필드는 변경하지 않았다.

Unity 6000.3.24f1에 포함된 Roslyn과 기존 Bee 응답 파일의 참조/정의로 런타임 `Assembly-CSharp` 및 에디터 `Assembly-CSharp-Editor`를 별도 `Logs/WeaponRefactor-compile/` 출력에 컴파일했다. 새 인터페이스 소스를 포함하고 에디터 컴파일은 새 런타임 출력에 연결했다. 두 컴파일 모두 종료 코드 0, 진단 출력 없음. 기존 테스트 소스의 컴파일 호환성만 확인했으며 테스트를 실행하지 않았다. 이 검증은 Unity 임포트/Netcode 후처리, Play 모드 및 실제 두 클라이언트 검증을 대신하지 않는다.

사용자 수동 확인 항목:

- Unity 임포트 후 Console 컴파일/Netcode 후처리 오류가 없는지 확인한다.
- PlayerTest에서 완드가 차징 없이 기존 이벤트 시점에 한 발을 생성하고 벽에서 사라지는지 확인한다.
- 오프라인 번개의 짧은/최대 충전, 미리보기 종료, 사거리와 피해를 확인한다.
- Light Orb의 비차징 발사, 반사, 반사 후 피해와 자기 피해 제외를 확인한다.
- Idle/Grip, 손 파지, Cast 전후 이동 및 기존 VFX를 확인한다.
- 두 클라이언트에서 양쪽 플레이어가 번개를 발사하고, 양 화면의 공격 애니메이션/연출과 한 번의 체력 감소를 확인한다. 벽 차단·최소/최대 충전도 확인한다.
- 충전/공격 중 교체 거절, 공격 취소·사망·디스폰 이후 잔여 발사 및 미리보기/공격 상태가 남지 않는지 확인한다.
- 온라인에 미지원 무기를 연결하면 경고와 함께 발사가 생략되고 로컬 피해가 발생하지 않는지 확인한다.

## 2026-11-05: 무기 애니메이션 2차 리팩터링

사용자는 1차 이후 Unity에서 Basic Wand 발사/벽 충돌, Lightning 차징/공격, Light Orb 발사/반사, 온라인 번개의 공격 애니메이션/연출/피해 동기화를 확인했다. 이 사용자 확인은 2차 코드의 실행 검증을 의미하지 않는다.

### 장착 무기와 애니메이션 소유권

`PlayerAttack`에서 `HasActiveWand()`, `wandPoses`, `animationOnlyWandAttack`, `currentWeapon is BasicWandWeapon`을 제거했다. 무기가 없으면 공격하지 않으며, 실제 장착 무기를 공격 시작 시 고정해 공격 클립과 발사에 사용한다. `IsWeaponPoseActive(weapon)`도 해당 무기가 실제 장착되어 있는지만 확인한다. 원격 플레이어의 입력 컴포넌트가 꺼져 있다는 이유로 포즈를 해제하지 않는다.

장착 변경은 기존 `TryEquipWeapon`으로 수행한다. `EquippedWeaponChanged` 이벤트는 초기화 및 장착 변경 시 포즈 컴포넌트에 동기적으로 알린다. 이미 활성화된 무기를 장착한 직후 같은 프레임에 공격해도 `OnEnable`/`Update` 순서에만 의존하지 않도록 했다. `PlayerAttack`에는 구체적인 무기/포즈 클래스에 대한 참조가 없다. 새 무기의 포즈 코드도 장착 여부와 이 이벤트를 사용할 수 있다.

`BasicWandIdlePose`는 자신의 `WeaponBase`와 부모 `PlayerAttack`을 기존 계층에서 찾는다. 런타임에서는 자신이 장착된 경우에만 기존 `idleController`를 적용한다. 해제 시 자신이 소유한 컨트롤러만 `baseController`로 복원한다. 이벤트 구독은 활성화/비활성화에 맞춰 연결·해제한다. 에디터에서 명시적으로 호출하는 외형 미리보기의 가시성 조건은 유지하며, 런타임 장착 판단에는 사용하지 않는다.

### 공격 Override와 Grip

공격 시작 시 `PlayerAttack`은 현재 포즈 컨트롤러를 저장한다. 이것이 `AnimatorOverrideController`라면 같은 기반 컨트롤러 위에 기존 Override 매핑을 복사하고, `attackAnimationSlot`에 해당하는 Cast만 장착 무기의 `AttackAnimation`으로 바꾼다. 원본 컨트롤러 에셋은 수정하지 않는다. `EndAttack`/`CancelAttack`은 저장한 컨트롤러를 복원한다. 런타임 `Rebind`, 강제 `Animator.Play`, 클립/곡선 수정은 추가하지 않았다.

읽기 전용 `AnimationSourceController`는 공격용 임시 Override가 적용되어 있는 동안 그 아래의 포즈 컨트롤러를 반환한다. 그 밖에는 현재 Animator Controller를 반환한다. 조회 과정에서 Animator를 변경하지 않는다.

- `BasicWandIdlePose`는 공격용 Override 아래에 자신의 포즈 컨트롤러가 있으면 소유권을 유지하고, 공격 컨트롤러를 덮어쓰지 않는다.
- `BasicWandGripSkin`은 실제 장착 무기와 `AnimationSourceController`를 확인한다. 공격용 Override가 설치되어도 기존 Grip 메시를 유지할 수 있다. 기존 Idle/Cast 및 이동→Cast 전환 조건, 원본/Grip 메시 참조, 에디터 미리보기 조건은 유지했다.
- `LightOrbWeapon`은 실제 장착 여부로 손 포즈를 적용한다. 원격 입력 비활성화와 포즈 적용을 분리했다. 손/손가락 계산, 공격, 반사, 발광 및 피해 로직은 변경하지 않았다.
- 기존 에디터 `BasicWandAnimatorReview`의 가시성 기반 장착 검증을 명시적 장착/해제 검증으로 갱신했다. 이번 작업에서는 이 도구를 실행하지 않았다.

### 보존·검증 범위와 수동 확인

2차 작업 전 원본은 로컬 `Logs/WeaponAnimationRefactor-backup-20261105-113132/`에 보관했다. 수정한 파일은 `PlayerAttack.cs`, `BasicWandIdlePose.cs`, `BasicWandGripSkin.cs`, `LightOrbWeapon.cs`의 포즈 조건, `BasicWandAnimatorReview.cs`, 이 문서다. 새 런타임 클래스/인터페이스나 직렬화 필드는 추가하지 않았다.

`IChargeableWeapon`, `IWeapon`, `IAttackAuthority`, `DuelPlayer`, 서버 피해 처리 및 각 무기의 발사 본문은 1차 상태를 유지했다. Arcane Bolt 진행 상태, 씬/프리팹, 기존 `.meta`/GUID, 모델·본·Grip 메시·VFX·애니메이션/컨트롤러 에셋은 변경하지 않았다. 보존 대상은 작업 시작 시 SHA-256과 비교했다.

Unity 6000.3.24f1 내장 Roslyn으로 런타임 및 에디터 소스를 별도 `Logs/WeaponAnimationRefactor-compile/` 출력에 컴파일했다. 두 컴파일 모두 종료 코드 0, 진단 출력 없음. Unity 임포트/Netcode 후처리 및 실제 Animator 평가, EditMode/PlayMode 테스트와 두 클라이언트 실행은 수행하지 않았다. 테스트 씬 생성 및 Git 커밋도 수행하지 않았다.

실제 Unity에서는 다음 회귀 지점을 확인한다.

1. Basic Wand를 실제 `Current Weapon`으로 선택한 뒤 Idle/Grip, 정지 및 이동 중 공격, Cast→Idle 복귀를 확인한다. 특히 공격 Override가 올라간 동안 손 파지가 풀리지 않는지 확인한다.
2. 완드 모델만 보이는 상태에서 Lightning/Light Orb가 선택되어 있으면 선택된 무기의 모션과 공격이 실행되는지 확인한다. 가시성을 바꾸는 방식의 기존 런타임 미리보기는 장착을 대체하지 않는다.
3. `TryEquipWeapon`으로 완드↔번개↔Light Orb 및 해제를 반복하고 포즈 잔류/간섭을 확인한다. 충전·공격 중 교체 거절은 유지한다. 무기 해제 후에는 모델이 남아 보여도 공격하지 않아야 한다.
4. `FireMagic` 중복 방지와 `EndAttack`, 취소/사망 후 컨트롤러 복원 및 다음 공격을 확인한다. 클립의 이벤트 시점과 전환 블렌드는 실제 Animator 평가로 확인해야 한다.
5. 두 클라이언트에서 번개의 공격 애니메이션·연출·피해가 계속 동기화되고 원격 이벤트가 중복 피해를 만들지 않는지 확인한다.
