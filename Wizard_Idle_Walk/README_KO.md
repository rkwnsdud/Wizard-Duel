# Wizard — 전투 Idle + 걷기

## 파일
- Wizard_Animated.blend: 기존 작업과 새 걷기 액션을 보존한 Blender 프로젝트
- Wizard_Animated.fbx: Unity용 전투 Idle 및 걷기 2개 클립, 텍스처 포함
- textures/: 기존 텍스처 3장
- Wizard_Walk_Preview.gif: 앞·뒤 걷기 미리보기

## 애니메이션
- Wizard_Combat_Idle_Grip: 1–91프레임, 30fps, 약 3초. 기존 전투 대기와 오른손 쥐기 유지.
- Wizard_Walk_InPlace: 1–33프레임, 30fps, 약 1.067초. 제자리 반복 걷기. 오른손은 지팡이를 쥐는 자세를 유지하고 왼팔과 몸통은 작게 움직임.
- Blender에는 Original_Idle_11 및 RightHand_Grip_Test도 보존. FBX에는 게임용 Idle/Walk만 내보냄.
- 지팡이 모델은 추가하지 않음. Unity에서 별도 장착 필요.
- 이동은 게임 코드에서 처리. 제작 스케일의 보폭 기준 약 0.45m/s를 출발점으로 발 미끄러짐에 맞춰 이동 속도/재생 속도를 조정할 수 있음.

## 보존 및 기존 수정
손가락 리깅 완료본과 비교해 메시 좌표, UV, 스킨 웨이트, 본 구조가 동일함. 총 39개 본(기존 24개 + 오른손 손가락 15개). 기존 흰 천 웨이트 수정과 손가락 쥐기 자세를 유지함.

## 확인 결과 및 한계
- 걷기 시작/끝 본 위치 오차 0: 반복 연결 확인.
- 전체 33프레임의 발 가중치 영역 검사에서 바닥 아래로 내려가는 정점 없음(최저 약 0.000335m).
- 기존 Idle 본 위치 차이 약 0.00000003m로 수치 오차 범위.
- FBX 재가져오기: 본 39개와 Idle/Walk 2개 확인. 본 위치 최대 왕복 오차 약 0.00000205m.
- 앞·뒤 미리보기로 로브 움직임을 확인. 모든 리타게팅 자세에서의 관통 방지를 보장하는 검사는 아님.
- 이 폴더 작업 시점에는 Unity Editor 실행 검증을 하지 못함. Unity 적용 결과는 아래 "Unity 프로젝트에서" 참고.
- 이번 추가 작업에 달리기·공격 클립은 포함하지 않음.

## Unity 프로젝트에서
- `Wizard_Animated.fbx`와 같은 파일이 `Assets/Game/Player/Art/Models/WizardAnimated/Wizard_Animated.fbx`에 사본으로 들어가 있음. 이 폴더는 Blender 원본 보관용이며 Unity가 읽지 않음.
- 리그는 Generic으로 가져옴. Humanoid 매핑도 가능했지만, 직접 만든 Transform 클립과 오른손 쥐기 자세를 그대로 쓰려고 Generic을 씀. 리그 종류를 바꾸려면 클립과 컨트롤러도 함께 다시 만들어야 함.
- 게임용 Idle·걷기 클립은 `Assets/Game/Player/Art/Animations/WizardAnimated/`, 무기 공격 클립은 `Assets/Game/Weapons/` 아래에 있음.
