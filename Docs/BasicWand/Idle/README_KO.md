# Basic Wand 전용 Idle 파지

## 최신 팔 방향 복원

완드 방향에 맞추던 아래팔의 축 비틀림을 제거했다. 팔을 내린 자세는 유지하고 원본 아래팔의 축 프레임을 보존한다. 이후 손목에 남아 있던 약 17도 굽힘까지 제거해, 최신 자세는 팔꿈치와 손목 굽힘 모두 약 0도인 일자 정렬이다. Player와 Dummy 모두 손등이 바깥, 접힌 손가락 끝이 몸 안쪽을 향한다. 손가락 파지·완드 로컬 Transform·수정된 스킨은 유지했다. 최신 팔 자세와 이미지·검사 결과는 [Arm/README_KO.md](Arm/README_KO.md)를 참고한다. 아래의 이전 제작 단계에 적힌 손목 25.47도 등의 수치는 최신 값이 아니다.

## 승인된 중지 돌출 수정

이후 조사에서 중지 끝면의 정점 3개가 약지 끝마디에 잘못 연결된 것을 확인했다. 사용자의 승인에 따라 원본 FBX를 보존한 메시 복사본 `Grip/Wizard_BasicWandGrip.asset`을 만들고, Player와 Dummy Player의 Wizard에 `BasicWandGripSkin`을 연결했다. 복사본은 세 정점의 가중치만 바꾸며 본 계층·Transform·외형 정점·UV·재질은 원본과 동일하다.

편집 모드에서는 표시 중인 Basic Wand에 적용하고, 재생 중에는 전용 컨트롤러의 Idle 상태에서만 사용한다. 완드를 숨기거나 비활성화하거나 이동·공격 상태로 넘어가면 원본 메시를 복원한다. 더미의 완드 비활성 상태는 유지했다. 더미에는 편집 모드에서 완드를 숨겨도 수정한 손을 유지하는 미리보기를 추가했고, 실제 재생 중의 무기·Idle 조건은 그대로다.

원인, 적용 파일과 양쪽 비교 이미지는 [Review/FINDINGS_KO.md](Review/FINDINGS_KO.md), 상세 검증은 [Review/applied-validation.txt](Review/applied-validation.txt)에 있다. 아래의 손가락 회전·완드 Transform 값은 이번 가중치 수정 이전 포즈의 기록이며 이번 작업에서 다시 바꾸지 않았다.

## 적용 범위와 계층

`Assets/Scenes/PlayerTest.unity`의 Player와 Dummy Player에 적용했다. 공용 `Assets/Player/Player.prefab`에는 BasicWand가 없으며, 이번에도 공용 프리팹은 수정하지 않았다.

실제 계층은 `Player/Wizard/WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm/RightForeArm/RightHand/WeaponSocket/BasicWand`이다. Animator는 Wizard에 있으며, Wizard_Animated 모델은 Generic(`animationType: 2`), 본 최적화 비활성화 상태다. 오른손에는 Thumb/Index/Middle/Ring/Little 각각 Proximal, Intermediate, Distal 본이 있다.

## 참고 사진 해석

1. 첫 사진: 엄지가 손잡이 윗면을 길게 받치고, 검지와 나머지 손가락이 아래에서 감싼다. 손잡이가 손바닥 안에 놓이며 끝마디만으로 집지 않는다.
2. 두 번째 사진: 검지가 엄지보다 앞쪽에 있고 손가락들이 서로 다른 깊이로 굽혀진다. 손가락 사이의 작은 틈과 엄지의 대향 접촉을 참고했다.
3. 세 번째 사진: 손잡이가 엄지·검지 사이에서 손바닥 아래쪽으로 비스듬히 지나간다. 엄지는 반대편 지지점이며 중지·약지·소지는 아래쪽을 감싼다.
4. 네 번째 사진: 엄지는 비교적 길게 펴서 측면을 누르고 나머지 손가락은 손잡이 둘레를 감싼다. 주먹을 과도하게 조이는 형태는 아니다.

사진 속 팔 방향은 적용하지 않았다. 로우폴리 장갑과 실제 BasicWand 손잡이의 크기에 맞춰 접촉을 계산했다. 동일 회전값을 모든 손가락에 더하는 기존 방식은 사용하지 않는다.

## 본과 완드 변경

- 원본 WizardCombatIdle을 복사한 `WizardCombatIdle_BasicWandGrip.anim`만 갱신했다. 어깨·위팔·왼팔·몸통·하체 및 다른 속성의 원본 곡선은 유지했다.
- RightForeArm은 기존 위팔 방향을 따라 약 4.84도만 굽힌다. 아래팔의 축 회전으로 손의 비틀림을 흡수하고, 손목은 자연스러운 파지와 완드 하향 방향을 연결하는 범위에서 굽힌다. 최종 손목 길이 방향의 꺾임은 약 25.47도다.
- 검지·중지·약지·소지는 원본 관절의 옆 회전과 벌어짐을 보존하고 굽힘 축의 회전만 개별 조정했다. 엄지는 밑마디의 대향 회전만 작게 조정하고 중간·끝마디 회전은 원본을 보존했다. 손잡이 메시의 축 방향 두께와 스킨 가중치로 변형한 장갑 정점·삼각형의 변·내부 표본을 사용한다. 손가락의 자연스러운 파지 공간을 먼저 구하고 완드의 위치·축을 거기에 맞춘다. 초기 수치 최적화에서 발생했던 큰 옆 비틀림은 제거했다. 본 위치·길이·부모 구조는 바꾸지 않았다.
- 최종 각 손가락의 원본 대비 로컬 회전 보정값과 표면 간격 계산은 [fit-report.txt](fit-report.txt)에 있다. 회전은 원본 quaternion에 로컬 보정을 곱한 값이며, Inspector의 Euler를 단순히 더한 값과는 다를 수 있다. 보고서의 검지~소지 삼중 값은 차례대로 밑마디·중간마디·끝마디의 로컬 Z 굽힘 보정각(도)이다. 엄지 opposition은 밑마디의 로컬 Euler 보정각이다.

| 손가락 | 밑마디 Z | 중간마디 Z | 끝마디 Z |
| --- | ---: | ---: | ---: |
| 검지 | -12 | +1.5 | +6.5 |
| 중지 | +7.5 | +24.5 | +18.5 |
| 약지 | +8 | +25 | +16.5 |
| 소지 | +14.5 | +1 | +9 |

엄지는 밑마디에 (-0.5, -3.5, -6)도만 보정하고 나머지 두 마디는 원본을 유지했다.
- 손가락·손목·아래팔의 회전은 Idle 전체 구간에서 일정하게 유지한다. 시작과 끝 값 및 접선이 같아 루프 경계에서 포즈가 튀지 않는다. 다른 Idle 곡선과 클립 길이·루프 설정·Event는 원본 그대로다.

BasicWand 씬 인스턴스의 WeaponSocket 기준 Transform:

| 항목 | 작업 전 | 작업 후 |
| --- | --- | --- |
| Position | (-0.020, 0.145, 0.026) | (-0.006879, 0.129964, 0.051121) |
| Rotation quaternion (x,y,z,w) | (0, 0.707107, 0.707107, 0) | (0.077292, 0.877672, 0.151386, -0.448107) |
| Rotation Euler | quaternion 기준 | (340.427, 234.094, 0) |
| Scale | 기존 씬 인스턴스 값 | 제작 도구에서 수정하지 않음 |

작업 중 Unity에서 수동 조정된 한 완드 인스턴스의 스케일 (1, 1, 1)은 그대로 보존했다. 다른 완드 인스턴스는 기존 스케일을 유지한다. 제작 도구는 localScale에 값을 쓰지 않는다.

완드 끝은 캐릭터 아래·바깥쪽으로 향한다. 최종 수직 하향 기준 각도는 약 36.60도다. BasicWand 프리팹과 그 안의 Model/FirePoint Transform, 메시·텍스처·재질은 수정하지 않았다. WeaponSocket과 기존 Lightning/Light Orb의 Transform·연결도 유지한다.

## 안전한 애니메이션 연결

`BasicWandGrip.overrideController`는 WizardLocomotion.controller를 기반으로 **WizardCombatIdle 하나만** 교체한다. 기존 이동 4개 및 WizardStaffCast 복사본은 삭제하지 않고 연결만 해제했다. 따라서 이동·공격은 원본 클립과 원본 Animation Event를 사용한다.

씬 Animator에는 원본 WizardLocomotion.controller를 저장했다. 두 BasicWand 씬 인스턴스에만 `BasicWandIdlePose` 컴포넌트를 추가했다. 실행 시 완드 모델 Renderer가 활성화되면 Idle 전용 컨트롤러를 적용하고, 모델을 숨기거나 완드/컴포넌트를 비활성화하면 원본 컨트롤러로 복원한다. 다른 시스템이 설치한 별도의 컨트롤러는 덮어쓰지 않는다. 처음부터 비활성화된 완드도 다른 무기의 컨트롤러에 영향을 주지 않는다.

이 프로젝트의 BasicWand는 아직 외형 오브젝트이며 WeaponBase 구현이나 무기 교체 API는 없다. 이후 무기 교체에서는 **BasicWand 오브젝트 또는 모델 Renderer를 함께 비활성화**해야 한다. currentWeapon 필드만 바꾸고 완드를 계속 표시하면 이 컴포넌트는 완드가 장착된 상태로 판단한다. 기존 공격/이동 로직이나 발사점 참조는 변경하지 않았다.

## 변경 파일

- `Assets/Scenes/PlayerTest.unity`
- `Assets/Weapons/Basic Wand/Animations/WizardCombatIdle_BasicWandGrip.anim`
- `Assets/Weapons/Basic Wand/Animations/BasicWandGrip.overrideController`
- `Assets/Weapons/Basic Wand/BasicWandIdlePose.cs` 및 `.meta` (신규)
- `Assets/Editor/BasicWandIdleSetup.cs` 및 `.meta` (신규, 편집 모드 제작·검사 도구)
- `Assets/Editor/BasicWandGripSetup.cs` (기존 Apply/Validate가 Idle 전용 도구를 호출하도록 변경)
- `Tools/BasicWand/BasicWandGripSetup.cs` (위와 동일한 안전한 호출로 갱신)
- `Docs/BasicWand/README_KO.md`, 이 문서와 같은 폴더의 fit-report/validation/protection/scene-scope 검증 기록

## 검증 및 Unity에서 확인할 사항

Unity 편집 모드에서 컴파일·클립 곡선 비교·컨트롤러 복원을 검사했다. [validation.txt](validation.txt)를 참고한다. 새 검증 씬 생성이나 Play Mode 실행은 하지 않았다.

손잡이 두께 검사는 실제 메시의 반지름 외피와 변형한 장갑 표면의 표본을 이용한 기하 검사이며, 전체 삼각형 충돌이나 최종 렌더 외형 판정을 대신하지 않는다. 실제 파지의 자연스러움, 손가락 사이 접촉, 손바닥 안착과 로브 관통은 사용자가 Unity에서 확인해야 한다.

1. PlayerTest의 Player와 Dummy Player 오른손을 앞·뒤·옆에서 확대해 엄지 패드와 각 손가락이 손잡이를 받치는지 확인한다.
2. Animation 창에서 `WizardCombatIdle_BasicWandGrip`을 미리보기하고 0초·중간·마지막 프레임의 손목과 손가락을 확인한다. 일반 씬 Animator는 안전한 기본 컨트롤러를 저장하므로 원본 Idle과 전용 Idle을 구분해서 선택한다.
3. 완드 끝과 손잡이가 장갑·소매·허벅지 쪽 로브를 뚫지 않는지, 소지와 약지의 간격이 자연스러운지 확인한다.
4. 사용자가 이후 직접 재생할 때 Idle 진입/이탈과 공격/이동 전환을 확인한다. 이번에는 전환이나 공격 클립을 수정하지 않았다.
5. 완드를 숨기거나 비활성화했을 때 원본 컨트롤러가 복원되는지 확인한다.

`Tools > Basic Wand > Apply Idle Only`는 현재 PlayerTest의 두 인스턴스에 다시 적용하고 씬을 저장한다. 수동으로 포즈를 더 다듬은 뒤에는 무심코 재실행하지 않는다. `Validate Idle Only`는 편집 모드에서 검사하며 Play Mode를 실행하지 않는다. 이전 `Docs/BasicWand/Grip/`의 이미지와 검증 파일은 이번 수정 전의 기록이다.
