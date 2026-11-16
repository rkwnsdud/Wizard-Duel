# 기본 완드 에셋

기본 완드는 온라인 대전에서 판이 시작될 때 손에 쥐는 무기이자 AI 대전의 기본 무기다(무기 id 1, 무기 스폰으로는 나오지 않음). 에셋은 `Assets/Game/Weapons/BasicWand/`에 있다. 이 문서는 완드 모델·자세·소리가 왜 지금처럼 되어 있는지만 정리한다. 무기 코드 구조는 [무기 구조 문서](../WeaponSeparation.md)를 본다.

## 모델과 재질

- `Art/Models/BasicWand_v2.fbx`: Meshy AI로 만든 원본(삼각형 243,156개). 게임에서는 직접 쓰지 않고, 감량본을 만드는 원본으로 둔다.
- `Art/Models/BasicWand_Low.fbx`: `Tools/WeaponMesh/decimate_weapon.py`로 삼각형 10,000개로 줄인 감량본. `Prefabs/BasicWand.prefab`이 이 모델을 쓴다.
- `Art/Materials/BlackWood_Satin.mat`: URP/Lit 재질. 텍스처(`Image_0.jpg`, `Image_2.jpg`, `BlackWood_MetallicSmoothness.png` 등)는 예전 배치 그대로 `Assets/Game/Weapons/LightOrb/Art/Textures/`에 있다.
- 마력탄 VFX는 [VFX 문서](../../Assets/Game/Weapons/BasicWand/Art/VFX/README_KO.md)를 본다.

## 손 메시 보정(그립)

- 완드를 쥐면 오른손 중지 끝에 뾰족한 면이 튀어나왔다. 원인은 중지 끝 삼각형의 정점 하나(같은 위치의 사본 포함 정점 2·2089·2184)가 중지가 아니라 약지 끝마디 가중치를 따라가던 스킨 연결이었다.
- `Art/Grip/Wizard_BasicWandGrip.asset`은 마법사 원본 메시의 복사본이고, 이 세 정점의 가중치만 `RightMiddleDistal` 1.0으로 바꿨다. 정점 위치·UV·노멀·본 구조는 원본과 같다.
- `BasicWandGripSkin`은 완드가 자세를 가진 동안(Idle·Cast와 그 전환)만 캐릭터 메시를 이 보정 메시로 바꾸고, 완드를 내려놓으면 원본 메시로 되돌린다.

## 자세와 애니메이션

- `BasicWandIdlePose`는 완드를 든 동안 캐릭터 Animator 컨트롤러를 `Art/Animations/BasicWandGrip.overrideController`로 바꾼다. 이 Override는 Idle만 `WizardCombatIdle_BasicWandGrip.anim`으로 바꾸고 걷기는 원래 클립을 쓴다.
- 공격 클립은 `Art/Animations/BasicWandAttack.anim`(0.65초)이다. 0.2초에 `FireMagic`, 0.65초에 `EndAttack` 이벤트가 있고, 이 두 이벤트는 공격 흐름에 필요하므로 지우지 않는다.
- 공격 자세는 손목과 완드 파지를 Idle과 같은 상대 위치로 두고 윗팔·아래팔로 조준한다. 손목을 따로 비틀면 손바닥이 크게 돌아가 보여서 이 방식으로 바꿨다. 클립은 `Tools/fix_wand_attack_pose.cjs`가 `Assets/Game/Weapons/Common/Art/BaseCast.anim`과 Idle 클립을 읽어 다시 만든다.
- 여러 무기 스크립트가 손 자세를 고치므로, 자세를 고칠 수 있는 무기는 `PlayerAttack.IsWeaponPoseActive(weapon)`가 참일 때만 고친다. 숨겨진 빛 구슬의 손 보정이 완드 자세를 덮어쓰던 문제가 이 규칙으로 해결됐다.
- `BasicWandGrip_DummyGrip.overrideController`, `BasicWandAttack_DummyGrip.anim`, `WizardStaffCast_BasicWandGrip.anim`, `WizardWalk*_BasicWandGrip.anim`은 예전 테스트 씬용 사본으로, 지금 프리팹과 컨트롤러는 참조하지 않는다.

## 소리

- 발사음은 `Assets/Audio/Clips/BasicWand/ArcaneBolt_Fire.wav`이고, 효과음 라이브러리의 `WandFire`로 재생한다. 출처와 라이선스는 [ArcaneBolt_Fire_License.md](ArcaneBolt_Fire_License.md)에 있다.
- 같은 폴더의 `BasicWandCast.wav`(이 폴더의 `GenerateCastSound.ps1`로 합성)와 `magic-projectile-04.wav`는 지금 쓰지 않는다.
