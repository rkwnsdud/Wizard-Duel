# 무기 분리 (#48)

기존 캐릭터 FBX와 지팡이 FBX는 이미 별도 모델이다. 이번 변경은 Player 안에 저장되어 있던 지팡이 구성(모델, FirePoint, LightningWeapon)을 `Assets/Weapons/Lightning Weapon/LightningStaff.prefab`으로 추출한다. Player는 오른손의 WeaponSocket 아래에 이 프리팹을 중첩해 사용한다. 기존 모델, 장착 위치, 크기, 캐릭터 색상은 유지한다.

공격 모션은 각 `WeaponBase`의 **Attack Animation**에서 지정한다. PlayerAttack은 공통 이동 컨트롤러의 Cast 모션만 해당 클립으로 교체하고 공격 종료 시 원래 컨트롤러를 복원한다. 이동 상태와 파라미터는 유지한다. 기존 공격 클립은 GUID·애니메이션 곡선·이벤트를 유지해 `Assets/Weapons/Common/StaffCast.anim`으로 옮겼으며 오프라인/온라인 번개 무기에 연결했다.

새 무기를 추가할 때:

1. 모델, 발사점, WeaponBase 구현을 독립 무기 프리팹에 구성한다.
2. Attack Animation에 현재 Wizard 골격용 클립을 지정한다. 클립에는 `FireMagic`과 `EndAttack` 이벤트를 각각 한 번 넣는다.
3. WeaponSocket 아래에 장착하고 PlayerAttack의 Current Weapon에 연결한다. 런타임에는 먼저 해당 플레이어 아래에 생성한 뒤 `TryEquipWeapon(weapon)`을 호출할 수 있다. 충전·공격 중에는 교체가 거절된다. `TryEquipWeapon(null)`은 무기를 해제한다.

공격은 시작 시 선택된 무기에 고정되며 FireMagic 중복 호출은 한 번만 처리한다. 무기를 해제한 상태에서는 공격을 시작하지 않는다. 독립 지팡이의 오디오 참조는 장착된 플레이어의 AudioSource에서 찾는다.

PlayerTest의 기존 Basic Wand는 외형/애니메이션 테스트용이며 기존 전용 Idle·Cast Override와 발사 억제 동작을 유지한다. 공격 클립이 미설정인 기존 프로토타입 무기는 기존 컨트롤러 모션을 사용한다. 네트워크에서 장비 변경을 전달하는 기능과 무기 선택 UI는 추가하지 않는다.

## 검증 방법

2026-11-04 최종 배치 실행: 아래 테스트 9개 모두 통과. 결과는 로컬 `Logs/WeaponSeparation-final-tests.xml`, 실행 로그는 `Logs/WeaponSeparation-final-tests.log`에 있다.

Unity 6000.3.24f1의 EditMode Test Runner에서 `WeaponSeparationTests`와 `DuelLightningWeaponTests`를 실행한다. 새 테스트는 Player/Player-1/Player-2/DuelWizard-1/DuelWizard-2의 무기·소켓·클립·이벤트 참조, PlayerTest의 무기와 완드 참조, 실제 Animator의 공격 종료 이벤트, 발사 중복 방지, 공격 중 교체 제한과 무기 해제를 검사한다. 온라인 두 클라이언트 간 실플레이와 최종 외형은 별도로 확인한다.

`Assets/Editor/WeaponSeparationSetup.cs`는 명시적으로 실행하는 마이그레이션/참조 검증 코드다. 완성 에셋을 사용하기 위해 재실행할 필요는 없다. `Apply`를 다시 실행해도 이미 추출한 지팡이는 재생성하지 않는다.
