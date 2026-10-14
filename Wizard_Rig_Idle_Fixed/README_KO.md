# Wizard — 오른손 손가락 리깅 추가

Blender 5.2.2 LTS에서 기존 흰 천 웨이트 및 전투 Idle 수정본을 이어서 작업했습니다.

## 포함 파일
- Wizard_Combat_Idle.blend: 손가락 리깅, 기본 쥐기 Idle, 손 펴기/쥐기 테스트 클립. 색상 텍스처 내장.
- Wizard_Combat_Idle.fbx: 메시·39개 본·웨이트와 기본 쥐기 Idle 1개. 색상 텍스처 내장.
- textures/: 원본 색상/Metallic/Roughness PNG 3개.
- Wizard_RightHand_Comparison.png 및 preview/: 실제 모델의 열린 손/굽히는 중/기본 쥐기 렌더.

## 변경 사항
기존 RightHand 아래에 엄지·검지·중지·약지·새끼손가락 각각 3개 본을 추가했습니다. 기존 24개 본은 보존했고 총 39개 본입니다.

본 이름은 RightThumb/Index/Middle/Ring/Little + Proximal/Intermediate/Distal 조합입니다. 각 Proximal의 부모는 RightHand이며, Intermediate → Distal 순서로 연결됩니다. Blender의 Right Hand Fingers 본 컬렉션에서 확인할 수 있습니다.

오른손 영역의 140개 정점 웨이트를 손바닥/손가락 관절 사이에 재배분했습니다. 변경 정점은 최대 3개 본 영향을 사용하며 가중치 합은 1입니다. 다른 영역의 웨이트는 이전 수정본과 완전히 동일합니다. 뒤 흰 천 웨이트 수정, 오른팔 약 97도, 왼팔을 내린 자세와 호흡 동작은 유지했습니다.

정점 추가/삭제, 메시 절단/형상 수정, 텍스처 변경, 새 지팡이 추가는 하지 않았습니다. 손가락은 원본의 낮은 면 수를 유지하므로 가까이서 보면 관절이 각집니다.

## Blender 액션
- Wizard_Combat_Idle_Grip: 기본 선택. 30fps, 1~91프레임, 3초 반복. 손가락은 기본 쥐기 자세 유지.
- RightHand_Grip_Test: 1~16프레임 열린 손, 46~61프레임 쥔 손, 91프레임 다시 열린 손. 몸의 Idle은 유지됩니다. 테스트용이며 FBX에는 포함하지 않았습니다.
- Original_Idle_11: 원본 Idle 보존. 추가 손가락 본은 중립 자세입니다.

FBX에는 Wizard_Combat_Idle_Grip 씬의 활성 Idle만 내보냈습니다. 손가락 15개 본이 추가되어 기존 Unity Avatar를 재사용하기보다 새 FBX의 본 매핑을 확인해야 합니다. 자동 매핑이 누락되면 오른손의 5개 손가락에 위의 Proximal/Intermediate/Distal 본을 대응시켜 주세요.

## 검증
- 이전 수정본과 정점 좌표, 면 구성, UV 동일.
- 기존 24개 본의 부모 관계 및 Rest 행렬 동일.
- 손 밖 웨이트 동일.
- 새 Idle 시작/끝 정점 위치 차이 0.
- 손 펴기/쥐기 중간 자세 렌더 확인.
- FBX 재가져오기 후 39개 본 계층·웨이트·UV 유지. 웨이트와 UV 오차 0, 자세 최대 좌표 오차 약 0.0000012m.

## 남은 확인
실제 지팡이 모델이 없으므로 특정 손잡이 굵기·장착 위치에 맞춘 완성 그립이 아니라 기본 쥐기 자세입니다. 지팡이 모델로 접촉/관통을 맞추는 작업이 남아 있습니다.

Unity Editor에서 Humanoid Avatar 생성 및 기존 게임 애니메이션과의 리타게팅은 직접 실행하지 못했습니다. 실제 걷기/달리기 클립도 제공되지 않아 해당 클립 검증은 미완료입니다. 다른 애니메이션에 손가락 곡선이 없을 때의 손 자세는 Unity Animator 설정에 따라 달라질 수 있으니 게임에서 확인해야 합니다.
