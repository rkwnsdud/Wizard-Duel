# LightningEffect — 독립 URP 번개 시각 효과

두 점 사이에 잠깐 번쩍이는 번개 한 줄을 그리는 시각 효과입니다. 게임에서는 번개 지팡이(`DuelLightningWeapon`)가 공격마다 이 프리팹을 하나씩 만들어 씁니다. 피해·조준·소리 같은 게임 규칙은 들어 있지 않습니다.

이 폴더는 `Assets/Effects/`의 어셈블리 `WizardDuel.Effects`(`Assets/Effects/WizardDuel.Effects.asmdef`)에 속하고, 이 어셈블리는 다른 프로젝트 어셈블리를 참조하지 않습니다. 폴더 밖 프로젝트 에셋도 참조하지 않으므로, URP 프로젝트라면 이 폴더를 `.meta`와 함께 그대로 옮겨 쓸 수 있습니다.

## 파일

```text
Lightning/
├── LightningEffect.cs
├── LightningEffect.prefab
├── README.md
├── Materials/
│   └── LightningBeam.mat
├── Shaders/
│   └── LightningBeam.shader
└── Textures/
    └── T_VFX_Zap_Lightning_01.tga
```

- `LightningBeam.mat`: Tint (0.08, 0.35, 1, 1), Intensity 8, Render Queue 3000.
- `LightningBeam.shader`: 셰이더 이름은 `ReusableEffects/Lightning Beam`입니다. `_LightningTex`에 `T_VFX_Zap_Lightning_01.tga`를 씁니다. LUT 프로퍼티는 없습니다. 폴더 밖 의존성은 URP 패키지의 셰이더 include뿐입니다.

## 프리팹 구조

```text
LightningEffect (LightningEffect.cs)
├── StartPoint — local (0, 0, -4)
└── EndPoint   — local (0, 0,  0)
```

재생하면 루트에 MeshFilter/MeshRenderer와 동적 Mesh를 만들어 리본 메시로 번개를 그립니다. 추가 자식 오브젝트는 만들지 않습니다. 기본 배치는 생성 위치가 끝점이고 로컬 -Z 방향 4m가 시작점입니다.

## 동작

- 번개는 본줄기(Main Bolt), 곁가지(Branches), 크게 휘는 호(Major Arcs), 끝점 갈래(Terminal Forks)로 이루어집니다. 각 묶음의 설정은 Inspector의 같은 이름 머리글 아래에 있습니다.
- 첫 프레임에 번개 전체가 나타나고, `Lightning Refresh Interval`마다 모양을 새로 뽑습니다. 수명(`Duration`)의 55% 지점부터 흐려지다가 수명이 끝나면 오브젝트를 지웁니다. 동적 Mesh는 `OnDestroy`에서 지우고, 공유 재질과 텍스처는 건드리지 않습니다.
- 리본 폭은 `Camera.main` 방향을 기준으로 맞춥니다.
- `Time.time`/`Time.deltaTime`을 쓰므로 timeScale의 영향을 받습니다.
- StartPoint, EndPoint, 재질 중 하나라도 비어 있으면 오류 로그를 남기고 바로 지웁니다.

프리팹의 주요 값은 다음과 같습니다. 나머지 값은 Inspector에서 확인합니다.

| 설정 | 프리팹 값 |
|---|---|
| Duration | 0.2초 |
| Lightning Segment Count | 32 |
| Lightning Jitter | 0.3 |
| Lightning Refresh Interval | 0.04초 |
| Main Bolt Width | 0.34 |
| Branch Count | 12 |

## 사용

```csharp
LightningEffect effect = Instantiate(lightningEffectPrefab, start, Quaternion.identity);
effect.SetEndpoints(start, end);   // 월드 시작점·끝점. 재생 중에 불러도 바로 다시 그린다.
effect.SetMainBoltWidth(width);    // 본줄기 폭
effect.SetChargeRatio(charge);     // 0~1. 클수록 큰 호가 많아진다. 벽에 막혀 짧아진 번개는 길이만큼만 늘어난다.
```

번개 지팡이는 발사 지점에 프리팹을 만든 뒤 맞은 지점까지 끝점을 잡고, 충전 정도에 따라 본줄기 폭과 충전값을 넘깁니다(`Assets/Game/Weapons/LightningStaff/Scripts/DuelLightningWeapon.cs`).

## 출처

다른 프로젝트의 총 스킬 연출(`PoliceFocusedFireEffect`)에서 번개 한 줄의 리본 메시 계산과 재질·셰이더·텍스처를 복사해 와 독립 효과로 만든 뒤, 이 게임에 맞게 곁가지·호·갈래와 충전 반영을 더했습니다. 복사할 때 모든 에셋에 새 GUID를 주었고 셰이더는 표시 이름만 바꿨습니다.
