# 무기 기본 세팅 점검 (#35)

2026-11-04, Unity 6000.3.24f1. 현재 플레이어·무기 구성을 점검하고 아래 누락을 수정했다.

## 수정

- PlayerTest의 Player/Dummy Player에 따로 추가된 LightningWeapon·LightOrbWeapon 4개에 기존 공통 Cast 공격 클립을 연결했다. 씬 참조 검사에서도 이 연결을 확인한다.
- 기본 LightningStaff 프리팹에 ChargePreview, LineRenderer와 LightningPreview 참조를 추가했다. 기존 온라인 지팡이와 같은 프리뷰 설정을 사용한다.
- LightningWeapon은 Inspector의 프리뷰 참조를 유지하고, 미지정이면 자식에서 찾는다. LineRenderer 없이 LightningPreview만 런타임에 추가하던 동작은 제거했다.
- PlayerAttack 비활성화 시 입력 객체 생성 여부와 무관하게 충전·공격·발사 대기 상태를 정리하고 임시 공격 컨트롤러를 복원한다.
- LightOrbProjectile은 기존 이동·반사 계산을 Advance(deltaTime)으로 분리했다. Update는 기존과 같이 Time.deltaTime을 전달하며 반사 횟수·피해량·속도는 변경하지 않았다.

## 실제 Unity 검증

명시적 메뉴 `Tools > Weapons > Review Setup`으로 다시 검사할 수 있다. 검증 코드는 별도 임시 씬과 복제 오브젝트를 사용하며 사용자의 씬을 저장하거나 교체하지 않는다. 결과는 로컬 `Logs/WeaponSetupReview.txt`에 기록한다.

- Player/Player-1/Player-2/DuelWizard-1/DuelWizard-2의 무기·소켓·공격 클립·이벤트 참조 검사 통과.
- PlayerTest 무기 4개의 공격 클립, 무기 내부 FirePoint 및 발사체 참조 검사 통과.
- 기본 지팡이 충전 프리뷰 초기화, 네 모서리 표시와 숨김 검사 통과.
- 입력 객체가 없는 PlayerAttack의 비활성화 시 충전·공격 상태 정리 검사 통과.
- Lightning의 실제 물리 쿼리에서 벽 차단 및 Floor 레이어 제외 검사 통과.
- Orb의 실제 물리 쿼리에서 발사자 콜라이더 무시, 연속 두 번 반사, 방향 전환과 독립 빔 구간 생성 검사 통과.
- 기본 Player 프리팹의 Idle 샘플: 팔꿈치 각도 약 97.3도, FirePoint 높이 약 1.907m. FireMagic 이벤트 시점(0.2초)의 FirePoint 높이는 약 1.405m. 높이는 플레이어 루트 기준이다.

팔 각도는 #35의 초기 90~100도 목표 범위에 들어가므로 이번 점검에서 Idle·공격 곡선이나 무기 장착 Transform을 재제작하지 않았다. 이 수치는 기본 Player의 클립 샘플이며 PlayerTest의 개별 Transform override, 모든 벽 근처의 자세나 최종 외형까지 보장하지 않는다.

이번 검증은 편집 모드에서 수행했다. 전체 Play Mode 회귀 테스트, OS 입력, 두 클라이언트 네트워크 실플레이, 새 Arcane Bolt 구현과의 통합 테스트는 수행하지 않았다. 위 결과는 당시 편집 모드 검증 기록이다.

## 2026-11-05 PR 통합

점검 도구를 현재 씬의 Lightning·Light Orb·Basic Wand 각 2개, 총 6개 무기 구성에 맞췄다. Basic Wand의 투사체 참조와 Player·Wall·Obstacle 충돌 마스크 검사도 추가했다. 이번 통합에서는 도구 실행 없이 Unity 내장 Roslyn으로 런타임 및 에디터 컴파일을 확인했다. 실제 Unity 임포트와 플레이 검증은 별도다.
