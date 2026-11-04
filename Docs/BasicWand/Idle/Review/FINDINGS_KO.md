# Player / Dummy Player 손 돌출 조사

Player와 Dummy Player 모두 확인했다. 본 위치·스케일은 원본과 동일하고, 씬의 중지 회전은 완드 전용 Idle 클립과 일치한다.

원인은 중지 끝부분 삼각형의 스킨 가중치 연결이다. 해당 삼각형의 정점 연결은 다음과 같다.

| 메시 정점 | 연결된 본 | 가중치 |
| --- | --- | --- |
| 0 | RightMiddleDistal | 1.0 |
| 1 | RightMiddleDistal | 1.0 |
| 2 | RightRingDistal | 1.0 |

끝점 하나가 중지가 아닌 약지 끝마디를 따라가면서 삼각형이 길게 늘어나 뾰족한 면이 생긴다. 같은 위치의 분리된 정점 사본 2089, 2184에도 같은 연결이 있어 총 3개 정점이 대상이다. 원본 Idle에서도 일부 나타나며, 완드용 굽힘 보정으로 더 드러났다. 처음 회전만의 문제로 판단한 진단은 불완전했다.

원본 메시를 메모리에서 복제하고 3개 정점의 연결만 RightMiddleDistal로 바꿔 비교했다. 정점 위치·UV·노멀·삼각형·본 구조·본 Transform은 바꾸지 않았다. Player와 Dummy Player 양쪽에서 돌출된 면이 사라지는 것을 확인했다.

- [Player 현재](Player-scene-0.png) / [가중치 수정 복사본](Player-weight-preview-0.png)
- [Dummy Player 현재](Dummy-Player-scene-0.png) / [가중치 수정 복사본](Dummy-Player-weight-preview-0.png)
- [본·정점 검사 원문](inspection.txt)

사용자가 완드 전용 복사본 적용을 승인한 뒤 실제 적용했다. 원본 FBX는 그대로 보존했다. 검증 씬 생성과 Play Mode 실행은 하지 않았다.

## 승인 후 적용

- 수정 메시: `Assets/Weapons/Basic Wand/Grip/Wizard_BasicWandGrip.asset`. 원본 char1 메시의 복사본이며, 정점 2·2089·2184의 가중치만 중지 끝마디 1.0으로 수정했다.
- PlayerTest의 Player와 Dummy Player 각각 Wizard 오브젝트에 `BasicWandGripSkin`을 연결했다. 두 캐릭터는 같은 수정 메시를 사용하며 각자의 재질은 그대로 유지한다.
- 편집 모드에서는 표시 중인 완드의 손을 수정본으로 보여준다. 이후 더미에는 아래의 숨김 상태 미리보기 보완을 적용했다. 컴포넌트 비활성화 시에는 원본으로 복원한다.
- 재생 시에는 Basic Wand의 전용 컨트롤러가 적용된 Idle 상태에서만 수정본을 사용한다. 이동·공격과 상태 전환 중에는 원본 메시를 사용한다. 다른 시스템이 지정한 별도 메시도 덮어쓰지 않는다.
- 원본 정점 위치·노멀·탄젠트·색상·UV0~UV7·삼각형·바운드·바인드 포즈는 변경하지 않았다. 본 계층과 Transform, 완드 Transform, 애니메이션과 Event도 이번 수정에서 변경하지 않았다.

최종 적용 이미지: [Player](Player-applied-0.png), [Dummy Player](Dummy-Player-applied-0.png). 검사 결과는 [applied-validation.txt](applied-validation.txt)에 있다.

초기 적용 이미지는 실제 설정을 복제한 임시 오브젝트에서 완드를 표시해 렌더링했다. 원래 씬에서 더미의 완드가 숨겨진 상태는 변경하지 않았다.

변경 파일: `PlayerTest.unity`, 새 메시 및 meta, `BasicWandGripSkin.cs` 및 meta, `BasicWandSkinRepair.cs` 및 meta, 적용 이미지 출력을 추가한 `BasicWandGripReview.cs`, 이 조사 문서와 검증 기록. 임시 자동 실행 스크립트는 적용 후 제거했다.

## 더미의 실제 편집 미리보기 보완

더미의 BasicWand 오브젝트가 비활성화되어 있어, 기존 조건이 더미의 수정 메시를 원본 char1으로 복원하고 있었다. 이제 더미의 `BasicWandGripSkin.previewWhenWandHiddenInEditor`만 켜서 편집 모드에서는 완드를 숨겨도 수정한 손을 유지한다. Player의 설정은 false로 유지하며 메시와 모든 본 Transform이 그대로인 것을 비교했다.

이 옵션은 `UNITY_EDITOR`에서 재생 중이 아닐 때만 적용된다. 실제 재생의 무기 표시·Idle·상태 전환 조건은 바꾸지 않았다. 원본 FBX와 수정 메시의 가중치 데이터도 다시 변경하지 않았다.

- [실제 더미 설정의 정면 미리보기](Dummy-Player-actual-preview-0.png)
- [실제 더미 설정의 측면 미리보기](Dummy-Player-actual-preview-2.png)
- [최신 더미 검증 결과](dummy-preview-validation.txt)

이 이미지는 실제 더미에 적용된 메시를 렌더링했으며, 완드를 강제로 활성화하지 않았다. 완드의 비활성 상태도 그대로 보존했다. 최신 변경은 `BasicWandGripSkin.cs`, 더미 설정을 저장한 `PlayerTest.unity`, `BasicWandDummyPreviewFix.cs` 및 meta, 검사 도구와 문서다. 임시 실행 코드는 제거했고 새 씬과 Play Mode는 사용하지 않았다.
