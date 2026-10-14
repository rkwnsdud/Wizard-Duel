# Player 마법사 모델 교체 결과

검증일: 2026-10-14 (KST), Unity 6000.3.24f1.

`Wizard_Idle_Walk/Wizard_Animated.fbx`를 사용하여 Player 외형과 전투 Idle·걷기를 교체하고 기존 공격을 새 골격용 별도 클립으로 이전했다. 달리기는 추가하지 않았다. 원본 FBX/Blender/텍스처/README, 기존 공격·걷기 클립, 플레이어·무기 런타임 스크립트는 수정하지 않았다.

## 변경 파일

| 위치 | 변경 |
| --- | --- |
| `Assets/Player/Player.prefab` | 직속 `Wizard` 아래 새 모델, 유효한 Generic Avatar, 기존 오른손 소켓 재부착 |
| `Assets/Player/Player-2.prefab` | 제거된 기존 렌더러의 빨강 재질 override를 새 렌더러의 `Wizard_Red`로 이전 |
| `Assets/Player/Animations/WizardLocomotion.controller` | 기존 상태·전환·파라미터를 유지하고 Motion 참조와 걷기 재생 배율만 변경 |
| `Assets/Player/Animations/WizardAnimated/` | 새 Idle, 전진, 후진, 좌/우 이동, 별도 공격 클립 및 각각의 `.meta` |
| `Assets/Player/Models/WizardAnimated/` | FBX와 텍스처 사본, Generic Avatar, URP 재질, Metallic/Smoothness 패킹 텍스처, 빨강 변형 텍스처 및 `.meta` |
| `Tools/WizardMigration/` | 작업에 사용한 생성·검증 코드. 게임에 임포트되지 않도록 Assets 밖에 보관 |
| `Docs/PlayerModelMigration/` | 검증 수치, Play Mode 결과, Humanoid 시험 결과와 렌더 이미지 |

`Player-1.prefab`과 관련 씬·다른 프리팹은 수정하지 않았다. `Player.prefab.meta` 및 GUID `a1cc0d0fb37c35449bd995841e69ab7a`를 보존했다. Unity가 자동 초기화한 TMP 폰트 2개의 글리프/아틀라스 변경은 별도 백업한 뒤 원래 상태로 복원했다. 커밋·푸시·머지는 하지 않았다.

## 유지한 구조와 참조

```text
Player (기존 이동/조준/공격/체력/CharacterController/AudioSource)
└─ Wizard (기존 Animator, WizardAnimationEvent)
   └─ WizardModel (새 FBX 인스턴스, 스케일 약 1.228572)
      ├─ char1 (URP SkinnedMeshRenderer)
      └─ Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm/RightForeArm/RightHand
         └─ WeaponSocket
            └─ LightningStaff (기존 LightningWeapon)
               ├─ Model (기존 지팡이 메시)
               └─ FirePoint (기존 오브젝트)
```

Player 루트와 루트 Transform, 이동·조준·공격·체력·CharacterController·AudioSource의 serialized fileID와 값이 원본과 같다. `currentWeapon`, `LightningWeapon.firePoint`, `boltPrefab`, `audioSource`, `thunderSound` 참조와 `WizardAnimationEvent`를 보존했다. Wizard의 기존 GameObject/Transform/Animator ID도 유지된다. 삭제된 이전 스킨/본 대신 새 FBX 계층을 연결했으며 새 FBX의 본 이름·부모 관계·웨이트는 바꾸지 않았다.

지팡이는 새 손가락 관절 위치에 맞춰 축과 그립 위치를 조정하고 길이를 약 1.75m로 맞췄다. 기존 FirePoint를 지팡이 상단 수정 중심으로 옮겼다. 실제 발사 방향은 변경하지 않은 `LightningWeapon.GetAimDirection()`이 계산하는 Player의 수평 전방이다. 0.2초 발사 자세에서 지팡이/FirePoint 축과 조준 전방의 내적은 1.000000이었다.

## Rig 선택과 공격 호환성

별도 임시 FBX로 Humanoid 시험 임포트를 수행했다. Hips, Spine02→Spine, Spine01→Chest, Spine→UpperChest, 목/머리, 양쪽 팔다리와 오른손 15개 손가락 관절을 명시적으로 매핑했다. 결과는 `Avatar.isValid=true`, `isHuman=true`이며 새 Idle/Walk도 `humanMotion=true`로 변환됐다. 왼손 손가락은 원본에 없으며 Humanoid의 선택 항목이다. 전체 매핑은 [humanoid.txt](humanoid.txt)에 있다.

따라서 **새 모델은 Humanoid 매핑이 가능하다**. 이번 Player는 원본의 본별 움직임과 오른손 그립을 직접 보존하기 위해 Generic을 선택했다. 기존 `Wizard_StaffCast.anim`은 `humanMotion=false`이고, 13개 Transform 경로가 `metarig/...` 아래를 가리킨다. 새 `Armature/Hips/...` 계층과 일치하지 않아 Avatar만 Humanoid로 바꾸어도 기존 공격이 자동 리타게팅되지는 않는다.

새 `WizardStaffCast.anim`은 기존 공격을 60fps로 샘플링하여 대응 본의 움직임을 이전했다. 팔 방향은 새 골격에 맞춰 보정하고 오른손은 발사 시점에 지팡이가 전방을 향하도록 맞췄다. 공격 시작/끝은 새 전투 Idle로 연결하고, 손가락의 쥐기 자세를 함께 기록했다. 원본 공격의 직접 재사용 또는 완전한 동일 포즈 복제는 아니다.

Generic 유지의 영향: 새 클립은 이 본 계층에 묶인다. 다른 Humanoid 캐릭터와 자동으로 공유하려면 공격까지 Humanoid 근육 클립으로 다시 변환하고 그립·자세를 재검증해야 한다. 현재 플레이어에는 빈 Root Motion 노드로 만든 유효한 `WizardPlayerAvatar`를 지정했고 `Animator.applyRootMotion=false`를 유지했다.

## 상태 머신과 애니메이션

`MoveSpeed`, `MoveX`, `MoveZ`, `Attack`의 이름·타입과 Idle/Walk/Cast의 전환 조건·시간·우선순위를 보존했다.

| 역할 | 새 연결 | 길이 / 처리 |
| --- | --- | --- |
| Idle | `WizardCombatIdle` | 제공된 전투 Idle, 3초 루프 |
| 전진 | `WizardWalkForward` | 제공된 Walk, 1.066667초 루프 |
| 후진 | `WizardWalkBackward` | 제공된 제자리 Walk의 시간 역재생을 별도 클립으로 저장 |
| 좌/우 | `WizardWalkLeft` / `WizardWalkRight` | 기존 측면 걷기를 새 골격으로 이전, 각각 0.8초; 새 그립 유지 |
| 공격 | `WizardStaffCast` | 기존 공격을 이전한 별도 0.65초 클립 |

방향별 걷기 재생 속도는 1.35배다. Idle은 1배다. 런타임에 본 경로를 바꾸거나 리타게팅하는 새 스크립트는 필요하지 않다. FBX의 일정한 채널은 양 끝 키만 남겼으며 움직이는 키는 유지했다.

공격 이벤트는 원본과 시간·함수명·문자열·정수·실수·오브젝트 인자·messageOptions가 동일하다.

| 시간 | 함수 | 인자 |
| --- | --- | --- |
| 0.2초 | `FireMagic` | 문자열 비어 있음, float/int 0, Object null, RequireReceiver |
| 0.65초 | `EndAttack` | 문자열 비어 있음, float/int 0, Object null, RequireReceiver |

## 재질과 색상 구분

새 텍스처를 URP/Lit 재질에 연결했다. Base Map은 sRGB, Metallic/Roughness는 선형으로 처리했다. URP용 패킹은 R=Metallic, A=1−Roughness다. 원본 PNG 파일 바이트는 모두 보존했다. Player와 Player-1은 남색, Player-2는 남색 영역을 붉은색으로 변환한 별도 텍스처를 쓴다. 금색·피부·흰 천과 지팡이 재질은 유지된다.

## 실제 Unity 검증

- FBX 임포트 및 스크립트 컴파일 성공. Player/Player-1/Player-2마다 Animator 1개, 스킨 렌더러 1개, 유효한 Generic Avatar를 확인했다.
- 세 프리팹의 루트 기능 컴포넌트, 직속 Wizard, 새 오른손 아래 기존 소켓, 실제 무기·FirePoint·오디오 참조 및 변형 프리팹 상속을 검사했다.
- 모든 새 애니메이션의 Transform 바인딩이 실제 Wizard 하위 경로로 해석된다.
- Play Mode에서 가상 키보드 입력으로 W/S/A/D와 4개 대각선 이동을 실행했다. 각 방향에서 0.5초 동안 수평 이동거리 2.500m, 즉 기존 5m/s였다. 대각선도 같은 속도다.
- 실제 가상 마우스 입력으로 전방/오른쪽 조준, 버튼 누름 중 차징, 버튼 해제 후 공격을 검사했다.
- Idle→Cast 및 Walk→Cast에서 각각 1회 공격. 각 공격에서 FireMagic 1회, EndAttack 1회, 실제 LightningBolt 1개 생성. 총 2회 공격에 번개 2개이며 중복 발사 없음.
- 실제 번개의 시작점이 FirePoint와 일치하고, 방향이 Player의 조준 전방과 일치했다. 공격 중 이동 정지 및 이후 Idle/Walk 복귀를 확인했다.
- 단일 클립을 샘플링해 정면/후면/손 클로즈업을 렌더했다. 전진·좌/우·공격의 각 6개 후면 프레임에서 뒤 흰 천과 로브의 큰 붕괴나 심한 관통은 관찰되지 않았다. 저폴리 손가락과 소매의 각진 변형 및 일부 접촉은 남아 있다.

수치와 판정 원문: [편집 모드 검증](validation.txt), [Play Mode 검증](play-pass.txt), [최종 참조·GUID·컴파일 검사](final-checks.txt).

## 걷기 속도와 남는 한계

모델 높이는 약 1.75m에서 2.15m가 되도록 1.228572배 적용했다. README의 제작 기준 0.45m/s를 이 스케일로 환산하면 약 0.553m/s, 1.35배 재생을 적용하면 약 0.746m/s다. 이는 제작자의 대략적인 보폭 기준이며 발끝의 접지 구간 순간 속도와는 다르다.

5m/s를 맞추기 위해 걷기를 약 11배로 가속하지 않았다. 제공된 보폭으로는 자연스러운 걷기 재생 속도와 5m/s를 동시에 만족시키기 어렵다. **발 미끄러짐은 여전히 크게 남아 있다.** 별도의 달리기나 보폭 재제작 없이 해소됐다고 볼 수 없다.

아래는 각 클립 120구간에서 발끝 본이 낮고 진행 방향 반대로 움직이는 구간을 추려, 코드 이동 5m/s를 더한 수평 속도 추정치다. 발바닥 전체의 접촉/마찰 시뮬레이션이나 실제 장면의 IK 측정은 아니다.

| 방향 | 1.35배에서 추정 미끄러짐 평균 | 샘플 메시 최저 높이 |
| --- | --- | --- |
| 전진/후진 | 약 3.772m/s | 약 0.0054m |
| 좌 | 약 3.462m/s | 약 0.0050m |
| 우 | 약 3.462m/s | 약 0.0050m |

측면 리타게팅 초기 결과의 약 10cm 발 들뜸은 Hips 높이 곡선을 보정하여 줄였다. 걷기 네 방향의 발끝 루프 시작/끝 위치 오차는 출력 정밀도에서 0이었다. 이 보정은 평면 바닥 기준이며 경사·계단에서의 접지는 별도 검증이 필요하다.

## 기존 문제와 미검증 범위

- **기존 오류:** `LightningPreview requires a PreviewVisual LineRenderer reference.` 교체 전 Player의 별도 복사본을 실제 Play Mode에서 생성했을 때도 동일 오류가 1회 발생했다. 원본의 `preview`는 null이고 자동 추가되는 LightningPreview에 PreviewVisual이 없다. 무기/프리뷰 기능을 임의로 확장하지 않았으므로 이 기존 오류는 남는다. 실제 차징 후 발사와 공격 종료는 위 테스트를 통과했다.
- 두 클라이언트를 연결한 네트워크 게임은 실행하지 않았다. Player-1/2의 상속·참조·비활성 입력 컴포넌트 설정·색상은 검사했지만 네트워크 동기화 자체의 종단 간 검증은 아니다.
- 실제 게임 카메라/조명, 모든 전환 중간 포즈, 경사/계단, 다른 오브젝트와의 로브 관통을 전수 검사하지 않았다. 천 물리나 충돌 회피를 새로 추가하지 않았다.
- 마우스/키보드 테스트는 배치 Play Mode의 합성 입력이다. 운영체제의 실제 장치와 창 포커스까지 포함하는 사용자 조작 테스트는 아니다.
- 사운드 참조와 발사 호출 경로는 보존·검사했지만 실제 스피커 청취는 하지 않았다.

## 남은 최소 수동 확인

1. Unity 6000.3.24f1에서 기존 Player 테스트 씬을 열어 실제 카메라 시점으로 WASD·대각선·마우스 조준·차징 후 발사를 확인한다. 마이그레이션을 다시 적용할 필요는 없다.
2. 유지된 5m/s에서 눈에 보이는 발 미끄러짐을 확인하고, 추후 보폭을 다시 제작할지 결정한다. 이번 변경은 달리기를 포함하지 않는다.
3. 기존 멀티플레이 테스트 절차로 Player-1/2를 연결해 남색/빨강 구분과 동기화를 확인한다.
4. 기존 충전 프리뷰를 복구하려면 별도 작업으로 `LightningPreview.previewVisual`을 올바른 LineRenderer에 연결한다.

## 이미지

- [Player 남색](Player-idle.png), [Player-2 빨강](Player-2-idle.png)
- [발사 자세](cast-front.png), [발사 시 손/지팡이](cast-grip.png)
- [전진 후면 연속 프레임](WizardWalkForward-sequence.png), [좌 이동](WizardWalkLeft-sequence.png), [우 이동](WizardWalkRight-sequence.png), [공격](WizardStaffCast-sequence.png)

연속 프레임은 왼쪽 위부터 행 순서로 클립 진행률 0%, 20%, 40%, 60%, 80%, 100%다.

## 중단 및 재개 기록

이전 실행 거부는 자동 승인 검토 서비스의 사용 한도 소진으로 검토 자체를 완료하지 못한 것이었다. 해당 실행은 이루어지지 않았으며 명령 형태를 바꿔 우회하지 않았다. 사용자가 한도 재설정을 알린 뒤 같은 `require_escalated` 승인 절차로 Unity 실행이 승인됐다.

재개 시 Player/컨트롤러는 원본 해시와 같았고 생성 자산만 존재했다. 재생성은 동일 경로의 자산을 `CopySerialized`로 갱신해 GUID를 유지했다. Apply는 이미 `WizardModel`이 있으면 모델을 다시 생성하지 않는다. Prepare는 이미 적용된 Player에서 원래 골격을 찾으려 하지 않고 중단하도록 보호했다. 임시 Humanoid 시험 FBX와 원본 Player 시험 복사본은 검증 후 제거했다.
