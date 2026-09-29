# LightningEffect — 독립 URP 번개 시각 효과

**이 `Assets/ReusableEffects/Lightning/` 폴더만 다른 URP 프로젝트로 옮겨도 됩니다: YES.** 폴더 밖의 프로젝트 에셋은 참조하지 않습니다. 대상 프로젝트에 URP가 설치·활성화되어 있어야 합니다. 기준 환경은 Unity 6000.5.2f1 / URP 17.5.0입니다.

## 전체 파일 목록

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
    ├── T_VFX_Zap_Lightning_01.tga
    └── T_VFX_Zap_LUT_03.tga
```

위 파일과 하위 폴더마다 `.meta`가 함께 있습니다. 상위 `Assets/ReusableEffects.meta`와 `Assets/ReusableEffects/Lightning.meta`도 생성했습니다. 원본 에셋과 GUID가 겹치지 않도록 모든 복사본에 새 GUID를 부여했습니다. 텍스처 데이터와 Import 설정은 원본을 유지합니다.

## Prefab 구조

```text
LightningEffect (LightningEffect.cs)
├── StartPoint — local (0, 0, -4)
└── EndPoint   — local (0, 0,  0)
```

Play 시 루트에 MeshFilter/MeshRenderer와 동적 Mesh를 생성합니다. 추가 자식 GameObject는 만들지 않습니다. 기본 배치는 생성 위치가 끝점이고, 로컬 -Z 방향 4m가 시작점입니다. 이 배치는 독립 사용을 위한 기본값이며 원본의 총 배치를 재현하지 않습니다.

## 원본 조사 및 가져온 계산

원본 호출 흐름은 `PlayerJobSkillController.ExecuteSkillLocally` → `PoliceSkill.Execute` → `PoliceFocusedFireEffect.Initialize`입니다. 원본은 LineRenderer가 아닌 Mesh Ribbon을 런타임 생성하며, 총 6개의 FirePoint부터 공통 목표점까지 각각 그립니다. 목표점은 조준 Raycast로 결정됩니다. 원본은 0.5초 준비 후 1.5초 표시합니다.

복제 범위는 `CreateLightningBeam`, `UpdateLightningArcs`, `RebuildLightningRibbon`의 시각 처리입니다. 새 스크립트는 이 클래스들을 참조하거나 호출하지 않습니다.

- 14개 구간/15개 점. 양 끝점을 고정하고 중간점을 `Vector3.Lerp(start, end, t)`로 배치합니다.
- `Random.insideUnitCircle * lightningJitter * Mathf.Sin(t * Mathf.PI)`를 번개 축에 수직인 두 방향으로 더합니다. 끝부분의 흔들림은 작고 가운데는 큽니다.
- `Time.time + lightningRefreshInterval` 방식으로 다음 갱신을 예약합니다. 프레임 경계에서 실행되므로 실제 간격은 0.08초 이상이며 프레임률의 영향을 받습니다.
- 원본의 접선 계산, Camera.main 방향의 리본 폭 계산, 수직/평행 방향 보정, UV와 삼각형 순서를 그대로 유지합니다.
- 30개 정점과 84개 삼각형 인덱스, MarkDynamic/Clear/RecalculateBounds를 유지합니다. MeshRenderer는 공유 Material과 sortingOrder 1을 사용합니다.
- 월드 좌표로 계산한 정점을 루트의 로컬 좌표로 변환하여 위치·회전·스케일이 적용된 인스턴스에서도 끝점과 월드 폭이 맞게 표시합니다. 스케일 0은 사용하지 마세요.

특정 게임 시스템, 총/무기 생성, 조준 판정, 피해, 준비 대기, 오디오는 포함하지 않았습니다. 원본 전체 6줄 연출 중 번개 한 줄의 시각 효과입니다.

## 유지한 설정

| Inspector 설정 | 기본값 |
|---|---|
| Duration | 1.5초 |
| Lightning Segment Count | 14 |
| Lightning Jitter | 0.3 |
| Lightning Refresh Interval | 0.08초 |
| Lightning Beam Width | 0.18 |

Duration 종료 시 GameObject를 Destroy하고 OnDestroy에서 동적 Mesh를 Destroy합니다. 외부 코드가 인스턴스를 일찍 Destroy하는 경우에도 Mesh를 정리합니다. 공유 Material/Texture는 복제하거나 제거하지 않습니다. 원본처럼 Time.time/Time.deltaTime을 사용하므로 timeScale의 영향을 받습니다.

## Material / Shader / Texture와 의존성

- Material: `Materials/LightningBeam.mat` — 원본 PoliceLightningBeam의 복사본. Tint (0.45, 0.9, 1, 1), Intensity 4, Render Queue 3000 유지.
- Shader 파일: `Shaders/LightningBeam.shader`
- Shader 이름: **ReusableEffects/Lightning Beam**. 기존 Shader와 이름 충돌을 피하도록 복사본의 표시 이름만 변경했습니다. 렌더링 계산은 동일합니다.
- `_LightningTex`: `Textures/T_VFX_Zap_Lightning_01.tga`
- `_LUT`: `Textures/T_VFX_Zap_LUT_03.tga`
- 외부 의존성은 Unity 기본 엔진과 URP 패키지의 Core.hlsl 및 그 패키지 include입니다. URP 패키지 파일은 이 폴더에 복사하지 않았습니다.
- 폴더 밖 프로젝트 Asset, Photon/Fusion, XR, Audio, PlayerJobSkillController 의존성: **모두 없음**.

Camera.main 태그가 붙은 일반 PC 카메라를 사용하는 URP 씬에서 확인하세요. 카메라가 없으면 원본과 같은 방향 보정을 사용합니다. Bloom/노출 등 대상 프로젝트의 렌더 설정에 따라 최종 화면 밝기는 달라질 수 있습니다. AudioListener는 필요하지 않습니다.

## 사용

Prefab을 빈 URP Scene에 배치하고 Play하면 기본 두 점 사이에서 자동 재생 후 제거됩니다. 시작/끝 위치와 Duration은 Inspector에서 조절합니다.

```csharp
GameObject effect = Instantiate(lightningEffectPrefab, position, rotation);
```

월드 시작/끝 위치는 생성 직후 또는 재생 도중 지정할 수 있습니다.

```csharp
effect.GetComponent<LightningEffect>().SetEndpoints(startPosition, endPosition);
```

재생 중 SetEndpoints를 호출하면 즉시 형태를 다시 계산합니다. 두 Transform을 직접 이동하면 다음 정기 갱신 시 반영됩니다. 별도의 초기화나 게임 전용 호출은 없습니다.

## 다른 프로젝트로 이동

`Assets/ReusableEffects/Lightning/` 폴더 전체를 **.meta 파일을 포함해** 대상 URP 프로젝트의 Assets 아래로 복사하세요. 또는 이 폴더를 선택해 Export Package하면 됩니다. Include Dependencies를 끄더라도 위 폴더의 모든 파일을 선택하면 충분합니다. 기존 Dreamland 에셋, Scene, Prefab, 이전 PoliceLightningImpact 폴더는 옮기지 않습니다.

## 검증 결과

- AudioModule 없이 Unity CoreModule + .NET Standard만으로 독립 컴파일 통과.
- 모든 직렬화 GUID 참조가 이 폴더 내부 에셋으로 해결됨을 검증.
- 금지된 게임/오디오 클래스 참조 없음 확인.
- 원본과 지그재그 점 계산식 일치 확인.
- 좌표 변환 외 리본/UV/삼각형 계산식 일치 확인.
- 텍스처 파일 해시와 Shader 시각 계산 코드 원본 일치 확인.
- Unity 6000.5.2f1 / URP 17.5.0 임시 프로젝트에 이 폴더만 복사한 뒤 Import 및 Play Mode 검증 통과. 테스트용 코드와 카메라/URP 설정은 임시 프로젝트에만 생성했으며 배포 폴더에 포함하지 않았습니다.
- `AssetDatabase.GetDependencies(prefab, true)`에서 프로젝트 에셋은 이 폴더 내부의 Prefab, 스크립트, Material, Shader, Texture 2개만 확인했습니다. 나머지 Shader include는 Unity/URP 패키지 경로입니다.
- 빈 Scene에 배치한 Prefab의 자동 실행, 14개 구간, 양 끝점 위치, 자식 2개 구조 확인.
- 실제 URP RenderTexture에서 파란 번개 픽셀 검출 및 캡처 이미지 육안 확인.
- 0.08초 이상 프레임 간격으로 반복되는 랜덤 형태 갱신 확인.
- Scene 배치 인스턴스와 Instantiate 인스턴스 모두 기본 1.5초 수명 후 GameObject/동적 Mesh 제거 확인.
- 위치/회전/비균일 스케일이 적용된 인스턴스에서 SetEndpoints를 Start 전과 재생 중 호출하여 월드 끝점 및 수직 방향 보정 확인.
- AudioSource 없음, 최종 Play Mode 실행 중 Console 오류 0건 확인.
- 테스트 환경 참고: 첫 에디터 기동에서 라이선스 IPC 문제와 에디터 Search 초기 인덱싱 예외가 발생했습니다. 라이선스 연결을 해결하고 에디터 초기화 이후 Play Mode에 진입한 최종 실행은 전체 검사를 통과했습니다. 원본 프로젝트나 배포 에셋을 수정하여 우회하지 않았습니다.

기존 경찰 스킬, PlayerJobSkillController, 기존 Prefab/Material/Shader/Texture와 이전에 만든 PoliceLightningImpact는 수정하지 않았습니다.
