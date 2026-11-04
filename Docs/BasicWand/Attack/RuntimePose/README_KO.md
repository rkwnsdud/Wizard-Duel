# 플레이 중 팔 비틀림 수정

2026-11-02. 사용자가 실행한 플레이에서 BasicWandGrip / WizardCombatIdle_BasicWandGrip 재생을 확인했다. 아래팔 로컬 회전은 클립의 `(0, -0.040209, 0, 0.999191)` 대신 약 `(0, 0.994874, 0, 0.101120)`으로 바뀌어 있었다.

원인은 기존 LightOrbWeapon의 LateUpdate 손바닥 보정이다. PlayerTest의 currentWeapon이 Light Orb를 계속 가리키므로, 숨겨진 Orb의 보정이 완드 Animator 평가 이후 아래팔·손목·손가락을 덮어썼다. 클립 직접 샘플링과 Animator 단독 평가만으로는 드러나지 않는 충돌이었다.

## 수정

- PlayerAttack.cs: `IsWeaponPoseActive`로 선택된 WeaponBase와 표시 중인 완드의 자세 소유권을 함께 판단한다.
- LightOrbWeapon.cs: 런타임 손 보정의 적용 조건에서 이 판단을 사용한다. 완드가 자세를 소유하면 보정을 복원하고 적용을 중단한다. 완드를 숨기면 기존 Light Orb 보정이 다시 허용된다.
- BasicWandAnimatorReview.cs 및 meta: 명시적 메뉴 `Tools > Basic Wand > Review Animator Pose`. 사용자가 이미 실행한 플레이에서는 상태를 읽기만 하며, 편집 모드에서는 임시 복제본의 Animator/PlayableGraph 평가 및 기존 Orb 보정 충돌 재현, 소유권·복원 검사와 이미지 출력을 수행한다. 자동 Play Mode 또는 새 씬은 사용하지 않는다.

공격/Idle 클립, 공격 이벤트 타이밍, 기존 무기 애니메이션 및 파지·모델·씬 에셋은 이번 수정에서 변경하지 않았다.

## 검증

[live-review.txt](live-review.txt)에 수정 전 런타임 상태를 기록했다. [animator-review.txt](animator-review.txt)에는 Animator 평가, 기존 보정 재현, 완드 표시 시 Orb 차단, 완드 숨김 시 Orb 소유권 복구, 팔·손목·검지·중지 회전 오차 0도를 기록했다. Unity 컴파일과 편집 모드 검사 통과. 사용자 플레이는 검사 후 종료했으며 자동 재실행하지 않았다. 수정 후 실제 플레이의 최종 확인은 사용자에게 맡긴다.

- [수정 전 충돌 재현](before-orb-overlay.png)
- [수정 후 완드 Idle](after-wand-idle.png)
