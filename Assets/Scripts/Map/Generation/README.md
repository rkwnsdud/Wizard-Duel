# 논리 맵 생성

`MapLayoutGenerator`는 오브젝트를 생성하지 않고 격자 데이터만 반환한다.

```csharp
MapLayout map = MapLayoutGenerator.Generate(new MapGenerationSettings
{
    Seed = 7301,
    Size = new Vector2Int(31, 31),
    Shape = MapShape.Donut,
    ObstacleDensity = 0.2f,
    Symmetry = MapSymmetry.Rotate180,
    NoiseStrength = 0.65f,
    NoiseScale = 0.75f
});

string[] layout = map.ToLayout();
Vector2Int blueSpawn = map.SpawnA;
Vector2Int orangeSpawn = map.SpawnB;
MapCell cell = map[10, 5];
float actualDensity = map.ActualObstacleDensity;
```

- `Seed` 기본값은 `7301`, `Shape`은 `Random`, `ObstacleDensity`는 `0.15`, `Symmetry`는 `Rotate180`이다. `None`으로 비대칭 맵을 생성할 수 있다.
- `NoiseStrength`는 `0~1`, 기본값은 `0.5`다. `0`이면 노이즈에 의한 윤곽·중심선 변형을 끄고 기본 형태를 생성한다. 크기와 비율은 여전히 시드에 따라 달라진다.
- `NoiseScale`은 `0.25~2`, 기본값은 `0.75`다. 맵의 반지름에 대한 노이즈 특징의 상대 크기로, 작은 값은 작은 요철을 많이 만들고 큰 값은 넓은 굴곡을 만든다. 강도가 `0`이면 스케일은 결과에 영향을 주지 않는다.
- 부드러운 값 노이즈로 외곽을 변형한다. 길쭉한 맵은 중심선을 굽히고 폭을 변화시키며, 도넛은 외곽과 내부 구멍을 서로 다른 노이즈로 변형한다. 회전 대칭 모드는 변형에도 대칭을 적용한다.
- 작은 스케일에서는 윤곽의 변위도 줄여 가는 목과 고립 영역을 완화한다. 격자화 중 생긴 의도하지 않은 구멍은 메우며, 도넛의 중앙 구멍은 유지한다. 연결성과 스폰 조건을 충족하지 못하는 형태는 다시 생성한다.
- `Size`는 결과 격자의 크기다. 생략하면 원형·사각형·도넛은 각 축 25~41칸, 길쭉한 형태는 긴 축 41~61칸과 짧은 축 13~19칸에서 선택한다. 형태는 격자 안에 들어가며 외곽에 빈 공간이 남을 수 있다.
- 좌표의 `x`는 열, `y`는 행이다. `map[x, y]`와 `layout[y][x]`가 대응한다. 월드 좌표 변환과 실제 블록 생성은 호출자가 처리한다.
- `ToLayout()`은 빈 공간을 공백, 바닥을 `.`, 벽을 `#`, 두 스폰을 `B`와 `O`로 표시한다. 기존 `GridMap` / `BlockFactory`의 문자 규칙과 호환된다. 스폰의 칸 타입은 `Floor`다.
- 외곽과 도넛의 내부 테두리는 벽으로 막힌다. 모든 바닥은 상하좌우로 연결된다. 한 칸 폭의 통로와 우회 이동은 허용하고 직선 시야는 보장하지 않는다.
- 스폰은 2×2 영역이고 둘레 1칸을 더한 4×4 안전 구역(`MapLayout.SpawnClearance`, `InSpawnZone`)은 모두 바닥이다. 외곽 벽도 장애물도 이 구역에 들어오지 않는다.
- 장애물은 칸 단위가 아니라 구조물 단위로 놓는다. 튜닝 값은 `MapLayoutGenerator` 위쪽 상수에 모여 있다.
  - 벽 단계(예산의 60%): 곧은 벽(3~6칸)과 L자 벽을 씨앗으로 놓고, 벽 끝에서 직진하거나 꺾어 2~5칸씩 늘린다. 늘어난 벽은 옆면이 다른 벽에 붙지 않아 두꺼운 덩어리 없이 긴 벽과 L·T자 벽이 생긴다. 늘린 뒤 한 방향 길이가 7칸을 넘으면 직진 대신 꺾는다.
  - 엄폐물 단계(나머지): 둘레 1칸이 비어 있는 자리에 2×2 기둥과 1×2~4 짧은 벽을 따로 놓는다.
  - 공통 규칙: 스폰 안전 구역에는 놓지 않고, 대칭이면 짝을 같이 놓는다. 대각선으로만 맞닿는 벽, 3×3이 모두 벽인 덩어리, 양옆이 벽인 1칸 폭 바닥이 3칸 이상 곧게 이어지는 통로는 만들지 않는다. 바닥 연결이 끊기는 구조물은 되돌린다.
- 장애물 밀도는 **장애물 수 / 장애물 배치 전 내부 바닥 수**다. 경계 벽은 장애물 수에서 제외한다. 목표 개수는 비율을 곱한 값의 내림이며, 구조물이 들어갈 자리가 모자라면 실제 밀도가 요청보다 낮아진다. 생성기는 `0~1`을 받지만 로비 화면에서 고를 수 있는 범위는 `0~0.25`(`DuelSettings.MaxObstacleDensity`)다. 이 범위에서 원형·사각형은 요청의 98% 이상, 폭이 좁은 도넛·길쭉한 형태는 0.2에서 94%, 0.25에서 85~87% 정도를 채운다.
- 같은 입력은 같은 결과를 만들고 Unity 전역 난수 상태는 소비하지 않는다. 재시도 중에도 선택된 형태와 격자 크기를 유지한다.
- 잘못된 설정은 `ArgumentException` 계열 예외를 발생시킨다. 주어진 크기 안에서 연결된 바닥과 안전한 두 스폰을 32회 안에 만들 수 없으면 `InvalidOperationException`을 발생시킨다.

Unity Test Runner에서 EditMode의 `WizardDuel.MapGeneration.EditorTests`를 실행해 형태, 노이즈 강도·스케일, 경계, 연결성, 밀도, 대칭, 스폰과 입력 검증을 확인할 수 있다.

## 실제 맵 자동 생성

`Assets/Scripts/Map/ProceduralMap.cs`의 `ProceduralMap`은 논리 맵을 생성한 뒤 `GridMap`과 같은 배치 규칙으로 `BlockFactory`를 통해 실제 블록을 만든다. 한 칸은 1단위이고 맵은 컴포넌트의 로컬 원점을 중심으로 배치된다. 벽 블록(`Assets/map/Walls/Wall-1~4`)은 1×1 돌 두 개를 쌓은 1×2×1 크기다.

1. 맵 오브젝트에 기존 `GridMap` 대신 `ProceduralMap`을 추가한다. `BlockFactory`가 함께 추가된다.
2. `BlockFactory`에 바닥·벽 블록과 두 스폰 블록을 지정한다. 스폰 순서는 파랑(`B`), 주황(`O`)이다.
3. Inspector에서 시드, 형태, 장애물 밀도, 대칭, 노이즈를 설정한다. `Automatic Size`를 끄면 `Size`의 칸 수를 사용한다.
4. 플레이어의 `PlayerSpawn`에 있는 `Map`에 `ProceduralMap`을 지정한다. 기존 `GridMap` 참조도 계속 사용할 수 있다.

Play 시작 시 자동 생성되며, `GeneratedLayout`, `Blocks`, `SpawnPoints`로 생성 결과를 조회할 수 있다. 코드에서 외부 설정으로 생성하거나 다시 생성할 수도 있다.

```csharp
proceduralMap.Generate(new MapGenerationSettings
{
    Seed = 1234,
    Size = new Vector2Int(31, 31),
    Shape = MapShape.Donut,
    ObstacleDensity = 0.2f,
    NoiseStrength = 0.75f,
    NoiseScale = 0.75f
});
```

다시 생성하면 이전에 만든 블록을 비활성화하고 제거한 후 새 블록과 스폰을 배치한다. 논리 생성이 실패하면 기존 블록은 유지한다. 플레이어 위치 갱신은 호출자가 새 `SpawnPoints`를 기준으로 처리한다.
