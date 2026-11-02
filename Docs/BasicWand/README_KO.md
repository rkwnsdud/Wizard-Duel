# Basic Wand 외형 장착

2026-10-30, Unity 6000.3.24f1 / URP 17.3.0.

## 원본과 임포트

- 사용 파일: Downloads/Meshy_AI_Black_Wood_Wand_v2_20261029_texture.fbx. Unity 기본 ModelImporter로 임포트했다.
- 원본과 프로젝트 사본 SHA256: `8862D23B9E7D2D1242368048596C52447691B5F716E22FABB141E6DB9DF09690`.
- Black_Wood_Wand_v2.glb도 분석했다. FBX와 GLB의 색상 및 노멀 텍스처 바이트가 동일하다. 기존 프로젝트의 GLB 임포터는 Light Orb 전용이므로 완드에는 사용하지 않았다.
- Downloads의 FBX ZIP은 Concept_Design 버전이다. 내부 FBX와 색상/금속성/노멀/거칠기 이미지 구성을 확인했지만, 요청에 명시된 최신 v2를 사용했다.
- 메시 1개, 머티리얼 1개, 243,156 삼각형. Unity 임포트 정점 139,215개. 메시 단순화나 텍스처 디자인 수정 없음.
- FBX 내장 이미지 4개를 바이트 그대로 추출했다. 색상은 8192² sRGB, 노멀/거칠기/금속성은 4096² linear로 설정했다. 압축 및 해상도 축소 없이 원본을 보존했다.
- `BlackWood_Satin`: URP/Lit, 흰색 tint, 노멀 강도 0.65, 양면 렌더링. GLB의 원본 PBR 설정을 기준으로 구성했다. URP용 파생 맵은 R=금속성, A=1−거칠기이며, 색상 디자인을 수정한 것이 아니다.

## 생성 에셋

모두 `Assets/Weapons/Basic Wand/` 아래에 있다.

- `Models/BasicWand_v2.fbx`
- `Textures/Image_0.jpg`, `Image_2.jpg`, `texture_0_roughness.png`, `texture_0_metallic.png`
- `Textures/BlackWood_MetallicSmoothness.png`
- `Materials/BlackWood_Satin.mat`
- `Prefabs/BasicWand.prefab`

## PlayerTest 장착

Player와 Dummy Player 두 인스턴스의 기존 오른손 소켓에 추가했다.

```text
Player 또는 Dummy Player
└─ Wizard/WizardModel/Armature/.../RightHand
   └─ WeaponSocket
      ├─ 기존 무기 (기존 오브젝트·컴포넌트·참조 유지)
      └─ BasicWand
         ├─ Model
         └─ FirePoint
```

완드 길이는 0.40m. 뒤쪽 끝에서 18% 지점을 그립 원점으로 잡았다. 기본 대기 클립의 첫 프레임을 임시 복제 오브젝트에 샘플링하여 손가락 관절 중심에 배치하고 캐릭터 전방으로 향하도록 했다. 씬 본 Transform이나 애니메이션에는 샘플링 결과를 저장하지 않았다.

WeaponSocket 기준 위치 `(-0.001954, 0.140302, 0.042540)`, 회전 Euler `(284.1836, 206.9942, 168.2317)`, 균일 스케일 `0.813953`. 스케일은 부모의 확대를 상쇄한다. FirePoint는 끝에서 약 3mm 앞이며 로컬 +Z가 발사 방향이다. 런타임 조준 보정 없이 기존 손 본을 따라가므로 다른 애니메이션 자세에서 방향은 달라질 수 있다.

## 보존 범위

기존 파일 중 변경한 것은 `Assets/Scenes/PlayerTest.unity`뿐이다. 기존 Lightning/Light Orb의 겹치는 렌더러만 해당 씬 인스턴스에서 끄는 override를 추가했다. 기존 프리팹·무기 모델·스크립트·공격 참조·애니메이션·Animation Event·다른 씬은 수정하지 않았다.

BasicWand는 렌더링 오브젝트와 FirePoint만 있는 외형 프리팹이다. Arcane Bolt, 무기 교체, 시작 무기 로직, 공격 중 이동 제한은 구현하지 않았다. 기존 currentWeapon과 발사점 참조는 그대로이므로 기존 공격 동작은 기존 무기 기준이다.

## 검증 및 직접 확인

Unity 배치 편집 모드에서 임포트, 컴파일, 씬 저장과 프리팹 참조 검증을 수행했다. `verification.txt`에 장착 부모, 40cm 길이, FirePoint 끝 위치, URP 재질/텍스처 참조 확인 결과를 기록했다. 새 검증 씬을 만들거나 Play Mode를 실행하지 않았다. 실제 렌더링 외형은 검증하지 않았다.

Unity에서 PlayerTest를 열어 다음을 확인한다.

1. 두 캐릭터의 오른손에 완드가 표시되고 기존 무기가 겹치지 않는지.
2. 기본 대기 자세에서 손잡이와 손가락의 접촉, 관통 및 간격. 뼈 위치 기반 배치이므로 최종 외형에 따라 BasicWand 인스턴스 Transform을 미세 조정할 수 있다.
3. 검은 나무 색상과 광택, 노멀 표현이 씬 조명에서 적절한지.
4. FirePoint의 파란 +Z 축이 완드 끝에서 바깥을 향하는지.
5. 이후 사용자가 직접 재생할 때 기존 손 애니메이션을 따라가는 외형과 조준 방향.

작업용 에디터 코드는 `Tools/BasicWand/BasicWandSetup.cs`에 보관한다. Assets 밖에 있어 게임에 포함되지 않으며 자동 실행되지 않는다. Apply는 초기 생성용이므로 완성된 프로젝트에 재실행하지 않는다.

## Basic Wand 전용 Idle 수정

최신 기본 파지와 팔 자세, Idle 하나만 교체하는 연결, 변경 파일 및 Unity 확인 방법은 [Idle/README_KO.md](Idle/README_KO.md)에 정리했다. 기존 `Grip/`의 이미지와 검증 결과는 이번 수정 전의 기록이다. 이동·공격 복사본은 보존하되 현재 Override Controller에서는 연결하지 않는다.
