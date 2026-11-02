# Basic Wand 전용 공격

플레이 중 Light Orb의 손 보정이 완드 자세를 덮어쓰던 후속 문제와 수정·전후 이미지는 [RuntimePose/README_KO.md](RuntimePose/README_KO.md)에 기록했다.

기존 PlayerTest에 장착된 완드와 `WizardCombatIdle_BasicWandGrip`의 실제 파지를 기준으로 제작했다. Player.prefab에는 완드가 장착되어 있지 않아 프리팹을 수정하지 않았다. 기존 WizardLocomotion은 Idle/방향 이동 BlendTree/Cast 구조이며 Attack 트리거와 FireMagic/EndAttack 이벤트를 사용한다. 기존 WizardStaffCast는 0.65초, FireMagic 0.20초, EndAttack 0.65초다.

## 모션과 타이밍

`BasicWandAttack.anim`: 0.400초, 비루프, Animator 속도 1. 단순 배속이 아니라 오른쪽 위팔·아래팔·손목의 키포즈를 별도로 제작했다. 몸통·왼팔·하체는 완드 Idle 첫 프레임을 유지한다. 이동 및 조준 방향 잠금은 기존 PlayerAttack.IsAttacking, PlayerMovement, PlayerAim 구조를 사용한다.

| 시점 | 동작 / 이벤트 |
| --- | --- |
| 0.00 | 실제 완드 Idle의 팔·손가락 자세에서 시작, 입력 즉시 공격/이동 잠금 |
| 0.04 | 오른팔을 빠르게 들어 올림 |
| 0.06 | 팔을 앞으로 보내며 손목 스냅 준비 |
| 0.08 | 전방 찌르기, 완드 +Z가 캐릭터 전방과 일치, **FireMagic** |
| 0.11 | 짧은 추가 뻗기와 손목 스냅의 후속 동작 |
| 0.16–0.32 | 팔꿈치를 접고 팔과 완드 방향을 자연스럽게 내림 |
| 0.40 | 완드 Idle 첫 프레임과 같은 팔 자세, **EndAttack**, 이동 잠금 해제 |

캐릭터 전방은 기존 PlayerAim이 공격 직전 정한 수평 조준 방향이다. 손목에 조준 회전이 집중되지 않도록 아래팔 회전과 팔꿈치 굴곡을 함께 사용했다. 손가락 15개 관절은 기존 Idle 파지 회전을 공격 전체에서 일정하게 유지한다. 완드 로컬 위치·회전·스케일 및 소켓을 변경하지 않았다.

Player와 Dummy의 기존 완드 장착 회전이 약 10도 달라 `BasicWandAttack_DummyGrip.anim`에는 손목 조준 보정을 반영했다. 길이·이벤트·나머지 팔 키포즈·손가락 파지는 동일하다. Dummy의 표시/활성 상태를 변경하지 않았다.

## 연결과 기존 무기 분리

- `BasicWandLocomotion.controller`: 원본 컨트롤러의 전용 사본. Cast 진입 블렌드 0.015초, Cast 종료는 normalized time 1에서 지연 없이 Idle로 전환한다. 원본의 0.25초 종료 블렌드는 이 사본에서만 제거했다. 클립의 마지막 자세 자체가 완드 Idle과 맞는다.
- 기존 `BasicWandGrip.overrideController`: 전용 사본을 기반으로 Idle과 Cast 두 클립만 교체. 이동 클립은 원본을 사용한다.
- `BasicWandGrip_DummyGrip.overrideController`: 동일 구조, Dummy의 장착 회전에 맞는 Cast 변형 사용.
- `BasicWandIdlePose`: 완드가 표시되어 있을 때만 해당 전용 컨트롤러를 소유한다. 완드를 숨기거나 비활성화하면 기존 WizardLocomotion으로 복원한다. 외부에서 설치한 다른 컨트롤러를 덮어쓰지 않는다.
- `BasicWandGripSkin`: 완드 Idle/Cast 및 관련 블렌드에서 기존 손 스킨 보정을 유지한다.
- `PlayerAttack`: 완드가 현재 전용 컨트롤러를 소유하면 차징 없이 입력 즉시 공격한다. 공격 시작 때 완드 여부를 저장하고, 완드 공격의 FireMagic은 기존 currentWeapon/Authority 발사를 호출하지 않는다. 완드를 숨긴 상태의 기존 무기 공격은 원래 경로를 사용한다.

BasicWand는 여전히 외형/애니메이션 전용이다. Arcane Bolt, 데미지, 충돌, 공격 효과는 구현하지 않았다. 기존 FireMagic 이벤트 이름과 WizardAnimationEvent 전달 구조는 유지했으며 향후 실제 완드 무기 구현 시 발사 연결이 필요하다. currentWeapon만 변경하고 완드를 계속 표시하면 완드 모드가 유지되므로, 다른 무기를 확인할 때는 완드 모델을 숨기거나 BasicWand를 비활성화한다.

## 변경 파일

신규 에셋 및 각 `.meta`:

- `Assets/Weapons/Basic Wand/Animations/BasicWandAttack.anim`
- `Assets/Weapons/Basic Wand/Animations/BasicWandAttack_DummyGrip.anim`
- `Assets/Weapons/Basic Wand/Animations/BasicWandLocomotion.controller`
- `Assets/Weapons/Basic Wand/Animations/BasicWandGrip_DummyGrip.overrideController`
- `Assets/Editor/BasicWandAttackSetup.cs`

수정:

- `Assets/Weapons/Basic Wand/Animations/BasicWandGrip.overrideController`
- `Assets/Weapons/Basic Wand/BasicWandIdlePose.cs`
- `Assets/Weapons/Basic Wand/BasicWandGripSkin.cs`
- `Assets/Player/PlayerAttack.cs`
- `Assets/Scenes/PlayerTest.unity`: 기존 완드 컨트롤러/손 스킨 연결. 이번 작업 전의 씬 변경 사항은 보존했다.
- 이 폴더의 README, build.txt, build_DummyGrip.txt, validation.txt.

기존 Lightning/Light Orb 무기, 원본 Animator Controller, 원본 공격/Idle/이동 클립, Player.prefab은 수정하지 않았다. 기존에 사용하지 않던 WizardStaffCast_BasicWandGrip 복사본도 그대로 두었다.

## 검증과 수동 확인

Unity 6000.3.24f1 편집 모드에서 생성·컴파일·검증했다. [validation.txt](validation.txt)에 길이·이벤트·81개 시점의 손가락/장착 유지·Idle 경계·발사 방향 검사를 기록했다. 임시 복제본과 IAttackAuthority 대역으로 입력 즉시 잠금, 완드의 기존 무기 발사 차단, EndAttack 해제, 완드를 숨겼을 때 기존 무기의 중복 없는 이벤트 전달도 확인했다. 실제 투사체를 생성하지 않았다.

새 검증 씬을 만들거나 Play Mode를 실행하지 않았다. 최종 시각적 모션과 실제 프레임 단위 이벤트 도달 시각은 Unity에서 직접 확인한다. 이벤트는 클립 로컬 시간 0.08/0.40초이며 실행 중에는 Animator 갱신 주기 단위로 도달한다.

`Tools > Basic Wand > Build Attack`은 현재 PlayerTest의 두 장착 상태를 기준으로 재생성하고 기존 씬의 참조만 저장하는 명시적 편집 도구다. `Validate Attack`은 임시 복제본으로 검사한다. 자동 실행 코드는 없다. 과거 `Apply Idle Only` 도구는 공격 연결 이전의 도구이므로 다시 적용했다면 Build Attack을 실행해 공격 연결을 복구한다.
