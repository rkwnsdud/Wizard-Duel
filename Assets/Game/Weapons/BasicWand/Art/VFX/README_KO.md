# Arcane Bolt VFX

## 원본 분석

- 원본: `Assets/AssetsPack/Eric VFX Studio/Free Game VFX/Prefab/FX_Fireball.prefab`
- 구조: 루트 `FX_Fireball` 파티클 아래 `Particle_up (1)`와 그 자식 `Particle_up`, `half`, `ball`, `aura2` 4개, `Ring`, `Glow`가 있다. 파티클 시스템은 총 11개다.
- 구체 및 균열: `ball`의 `firerock 3`(AlphaBlendFlow), 루트의 `firerock 2`(AdditiveFlow). `firerock.png`에 검은 암석과 주황색 균열이 직접 그려져 있다.
- 외곽 흐름과 잔상: `aura2` 계층의 `decal_fire10` 및 `Aura_Flame_001`, `half`의 `Gradient_Beam_007`, `Ring`의 `ring_03a`를 사용한다. `decal_fire10.png`에도 주황색 선이 직접 포함된다.
- 파편: `Particle_up` 2개의 `particle_sprite_droplet`. 중심 발광: `Glow`의 `glow_ball2_grey_3 1`.
- 셰이더: 원본의 `Eric/URP_AdditiveFlow` 및 `Eric/URP_AlphaBlendFlow` 사용. Shader Graph는 사용하지 않는다. 두 셰이더 모두 URP 태그와 투명 렌더링을 사용한다.
- Particle Trail 모듈은 모두 꺼져 있다. 보이는 긴 잔상은 뒤쪽에 배치한 파티클 메시와 흐르는 텍스처로 만든다.

## 완드 전용 복사본

- 프리팹: `ArcaneBoltVFX.prefab`
- 전용 머티리얼: `ArcaneCrackedCore`, `ArcaneInnerShell`, `ArcaneWhiteCore`, `ArcaneCrackLightning`, `ArcaneFlow`, `ArcaneBeam`, `ArcaneRim`, `ArcaneSparks`.
- 전용 URP 셰이더: `ArcaneAdditiveFlow`, `ArcaneAlphaBlendFlow`.
- 원본 텍스처, 메시, 파티클 이동 곡선, UV 흐름은 그대로 참조한다. 텍스처 파일과 알파 채널은 수정하지 않았다. 복제한 셰이더가 따뜻한 텍스처 RGB만 밝기에 따라 진보라 `#7954C9`, 연보라 `#B79AFF`, 백색 `#F8F5FF` 범위로 다시 매핑한다. 머티리얼 색과 파티클 색도 같은 팔레트로 바꿨다.
- 불꽃 파편 `Particle_up` 2개의 초당 방출량은 30→18, 20→12, 수명 범위는 약 0.7–0.8초→0.48–0.55초다. 뒤쪽 메시 파티클은 중심에서의 거리를 68%로 줄였다. 프리팹 루트 크기는 70%다.
- 프로젝트의 Bloom, Exposure, Volume, 원본 셰이더, 다른 무기, Basic Wand 모델과 애니메이션은 변경하지 않았다.

## 직접 확인

Unity에서 `ArcaneBoltVFX` 프리팹을 열고 Particle Effect 미리보기로 확인한다. 밝은 배경과 어두운 배경에서 균열·중심 밝기·크기·파편량·잔상 길이를 확인하고, Console에 셰이더 오류나 누락 참조가 없는지 본다. 새 검증 씬이나 플레이 모드는 사용하지 않았다.
