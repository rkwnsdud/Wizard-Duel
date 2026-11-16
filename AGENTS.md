# AGENTS.md

Wizard-Duel에서 일하는 에이전트를 위한 작업 안내입니다. 게임 소개, 조작, 실행 방법은 [README.md](README.md)에 있습니다. 이 파일에는 코드를 고칠 때 지켜야 할 규칙과, 알아 두면 시간을 아끼는 함정을 모았습니다.

- 엔진: Unity **6000.3.24f1**, URP, Netcode for GameObjects(NGO), Unity Transport
- 모드: 온라인 1대1(전용 서버), AI 대전(로컬 호스트), 오프라인 테스트 씬(`Assets/Scenes/DuelOfflineTest.unity`)
- 빌드 씬: `Assets/Scenes/Main.unity` 하나

## 1. 어디를 보면 되나

| 하려는 일 | 먼저 볼 곳 |
|---|---|
| 새 무기 추가, 무기 공통 계약 | [Docs/WeaponSeparation.md](Docs/WeaponSeparation.md)의 "새 무기 추가", `Assets/Game/Weapons/Common/Scripts/`(`IWeapon`, `WeaponBase`, `WeaponDefinition`, `AttackInfo`) |
| 무기 정의(id·이름·아이콘·스폰 설정) | `Assets/Game/Weapons/Definitions/*.asset`, 카탈로그 `Assets/Game/Battle/WeaponSpawnCatalog.asset` |
| 공격 흐름(발사·연출·명중) | `Assets/Game/Player/Scripts/PlayerAttack.cs`, `Assets/Game/Battle/Scripts/DuelPlayer.cs` |
| 서버가 명중 보고를 받거나 버리는 규칙 | `Assets/Game/Battle/Scripts/ServerHitReport.cs`, [Docs/ServerHitReportRules.md](Docs/ServerHitReportRules.md) |
| 전투 진행·판·스폰 지점 | `Assets/Game/Battle/Scripts/DuelArena.cs` |
| 필드 무기 스폰·줍기·교체 | `WeaponSpawnServer`/`WeaponSpawnClient`/`ArenaWeaponSpawner`(`Assets/Game/Battle/Scripts/`), [Docs/WeaponSpawning.md](Docs/WeaponSpawning.md) |
| 로비·방·접속 승인 | `Assets/Game/Lobby/Scripts/Network/`(`LobbyNetwork`, `LobbyServer`, `LobbyClient`, `LobbyMessages`, `NetworkDuelConfig`), [Assets/Editor/Multiplayer/README.md](Assets/Editor/Multiplayer/README.md) |
| 로비 거절·오류 문구 | `Assets/Game/Lobby/Scripts/LobbyReason.cs`(`LobbyReason` 코드, `LobbyReasonMessages` 한국어 문구) |
| 시작·로비 화면 흐름 | `Assets/Game/Main/Scripts/MainFlow.cs` |
| 이동·입력·체력·상태이상 | `Assets/Game/Player/Scripts/`(`PlayerMotor`, `PlayerMovement`, `PlayerHealth`, `PlayerStatusEffects`), [Docs/MoveSpeedAndStatusEffects.md](Docs/MoveSpeedAndStatusEffects.md) |
| AI 봇 | `Assets/Game/Battle/Scripts/Bot/`(`DuelBot`, `BotGrid`, `AiDuel`) |
| 맵 생성 | `Assets/Map/Generation/`, [Assets/Map/Generation/README.md](Assets/Map/Generation/README.md) |
| 효과음 | `Assets/Audio/`(`SfxId`, `SfxLibrary`, `Sfx.Play`), 표 `Assets/Audio/Resources/SfxLibrary.asset`, 메뉴 **Tools > Wizard Duel > 효과음 패널** |
| 전용 서버 배포 | [Deploy/Server/README.md](Deploy/Server/README.md), 빌드 진입점 `Assets/Game/Main/Editor/LinuxServerBuild.cs` |
| Unity 밖 보조 스크립트 | [Tools/README.md](Tools/README.md) |

## 2. 설계에서 지켜야 할 것

- **공격 하나는 `AttackInfo`(공격 번호 + 쏜 무기 id)로 다닌다.**
  - 발사 연출, 명중 보고, 서버 판정, 적중 효과(화상·둔화)는 모두 이 정보로 쏜 무기를 찾아 처리한다(`PlayerAttack.FindWeapon`).
  - "지금 들고 있는 무기"를 보고 공격 이벤트를 처리하는 코드를 새로 만들지 않는다.
- **명중 판정은 공격한 쪽 클라이언트, 체력과 승패는 서버가 정한다.**
  - 서버가 보고를 받을지 버릴지는 `ServerHitReport` 한 곳에서만 판정한다. 규칙을 바꾸면 `Docs/ServerHitReportRules.md`도 같이 고친다.
  - 전제는 "악의적인 플레이어는 없다"이다. 피해량·사거리·공격 간격·이동 속도 같은 부정 입력 검사는 일부러 하지 않는다.
- **무기를 클래스 타입·이름·숫자로 분기하지 않는다.** 무기는 `WeaponDefinition`의 id로 가리킨다. HUD·AI 로비·오프라인 테스트·아이콘 생성기가 모두 같은 정의를 읽는다.
- **온라인·AI·오프라인은 같은 전투 코드(`DuelPlayer`, `PlayerAttack`, `ServerHitReport`)를 쓴다.**
  - AI 대전은 로컬 호스트(`LocalHost` 세션 모드)로 돈다.
  - 오프라인 테스트 씬은 같은 플레이어 프리팹에 무기를 직접 붙인다(스폰·줍기 없음). 모드별 `if`를 늘리지 말고, 로컬 처리만 끼워 넣는다.
- **접속 프로토콜 버전:** 아래가 바뀌면 `LobbyJoinRequest.CurrentVersion`을 1 올리고 그 위 주석에 한 줄 적는다(지금 9).
  - 서버와 클라이언트가 주고받는 메시지 형식, RPC 인자
  - 같은 시드에서 나오는 맵(`MapLayoutGenerator` 튜닝 값)
  - 버전이 다르면 서버가 접속을 거절하므로, 원격 서버도 같은 커밋으로 다시 배포해야 한다.
- **로비 사유는 문장 대신 `LobbyReason` 코드로 보낸다.** 문구는 `LobbyReasonMessages`에서 고친다.
- **네트워크 프리팹 목록**(`Assets/DefaultNetworkPrefabs.asset`)에는 `DuelWizard-1`, `DuelWizard-2`만 있다.
  - 무기는 네트워크 오브젝트가 아니다.
  - 프리팹 해시가 어긋나면 **Tools > Wizard Duel > Repair network prefab hashes**로 고친다.
- **효과음은 `SfxLibrary` 표와 `Sfx.Play(SfxId)`로만 낸다.** 새 소리는 `SfxId`에 항목을 넣고, 표에 클립과 볼륨을 연결한다.

## 3. 폴더와 어셈블리

폴더 규칙의 원문은 README의 "프로젝트 구조"입니다. 요약하면 아래와 같습니다.

- **두는 곳**
  - 직접 만든 것: 기능별 PascalCase 폴더 `Assets/Game/<기능>/{Scripts,Prefabs,Art,Editor}`
  - 독립 모듈: `Assets/{Audio,Effects,Input,Map}`
  - 외부 에셋 팩: `Assets/ThirdParty/<팩이름>/`에 원본 구조 그대로. 루트(`Assets/` 바로 아래)에 새 팩이 들어오면 여기로 옮긴다.
  - 효과음 클립: `Assets/Audio/Clips/<무기 폴더 이름 또는 용도>/`
  - 테스트: `Assets/Tests/Editor/<기능>/`, `Assets/Tests/Runtime/`
  - 여러 기능에 걸친 에디터 도구: `Assets/Editor/`
- **루트에 그대로 두는 것:** `Assets/DefaultNetworkPrefabs.asset`, `Assets/Scenes/`, `Assets/Settings/`, `Assets/TextMesh Pro/`. 패키지나 배포 스크립트가 이 경로를 고정으로 쓴다.
- **어셈블리**(`Assembly-CSharp`는 없다)

| 어셈블리 | 위치 | 참조 |
|---|---|---|
| `WizardDuel.MapGeneration` | `Assets/Map/Generation/` | 없음 |
| `WizardDuel.Audio`, `WizardDuel.Effects` | `Assets/Audio/Scripts/`, `Assets/Effects/` | 없음 |
| `WizardDuel.Input` | `Assets/Input/` | Input System |
| `WizardDuel.Map` | `Assets/Map/Scripts/` | MapGeneration, Netcode |
| `WizardDuel.Game` | `Assets/Game/` 전체 | 위 모듈 전부 + Netcode·Transport·TMP·UGUI·URP |
| 에디터 전용 | `Assets/Editor/`(`WizardDuel.Editor`), `Assets/Audio/Editor/`, `Assets/Map/Editor/`, `Assets/Game/{Main,Player,Weapons/LightOrb}/Editor/` | 각자 필요한 것 |
| 테스트 | `Assets/Tests/Editor/`(`WizardDuel.EditorTests`), `Assets/Tests/Editor/MapGeneration/`, `Assets/Tests/Runtime/`(`WizardDuel.RuntimeTests`) | `WizardDuel.EditorTests`는 `autoReferenced: false` |

- **함정**
  - **`Assets/Game/<기능>/` 아래에 `Editor` 폴더를 새로 만들어도 에디터 전용이 되지 않는다.** asmdef 범위 안에서는 `Editor` 폴더 이름이 특별하지 않아 `WizardDuel.Game`(런타임)에 들어가고, `UnityEditor`를 쓰면 플레이어 빌드가 깨진다. 에디터 전용 asmdef(`includePlatforms: ["Editor"]`)를 같이 만들거나 `Assets/Editor/`에 둔다.
  - 테스트가 새 어셈블리의 타입을 쓰면 `WizardDuel.EditorTests.asmdef`의 references에 그 어셈블리를 더한다.
  - 리플렉션·`Type.GetType`의 어셈블리 이름은 `"DuelPlayer, WizardDuel.Game"`, `"TestSuites, WizardDuel.EditorTests"`처럼 새 이름을 쓴다.
  - 씬·프리팹 YAML에 `Assembly-CSharp` 이름이 남아 있는 곳이 있다(`m_EditorClassIdentifier`, UnityEvent의 `m_TargetAssemblyTypeName`). Unity가 대상 오브젝트로 찾아서 동작에는 문제가 없다. 다시 직렬화해도 바뀌지 않으므로 일괄 수정하지 않는다.
  - `PlayerInputActions.cs`는 `Assets/Input/PlayerInputActions.inputactions`가 생성하는 래퍼다. 직접 고치지 않는다.
- 폴더를 새로 만들거나 옮기면 README의 구조 트리도 같이 고친다.

## 4. Unity 작업 규칙

- **에디터가 프로젝트를 열고 있으면 씬·프리팹·ScriptableObject YAML을 디스크에서 직접 고치지 않는다.**
  - 에디터 API(`SerializedObject`, `PrefabUtility`, `EditorSceneManager.SaveScene`, `AssetDatabase.SaveAssetIfDirty`)로 고친다.
  - 옮기기·지우기는 `AssetDatabase.MoveAsset`/`DeleteAsset`으로 한다. 그래야 `.meta`와 GUID가 유지된다.
  - 셸로 옮기면 `.meta`를 반드시 같이 옮기고 `AssetDatabase.Refresh()`를 부른다.
  - git 병합처럼 많은 파일이 바뀌는 작업은 에디터를 닫고 하면 충돌 표시가 남은 파일을 불러오는 일을 피할 수 있다.
- **씬이 dirty인 채 씬을 바꾸는 코드를 부르면** "Scene(s) Have Been Modified" 확인 창이 뜬다. 그러면 메인 스레드가 멈추고 MCP가 모두 무응답이 된다.
  - 일부러 씬을 고친 게 아니면, 테스트·씬 전환 전에 저장된 씬을 다시 연다.
  - `var s = SceneManager.GetActiveScene(); if (s.isDirty && !string.IsNullOrEmpty(s.path)) EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);`
- **커밋 전에 되돌릴 Unity 잡음**
  - `Assets/Game/UI/Fonts/DoHyeon-Regular SDF.asset`, `Assets/Game/UI/Fonts/GrenzeGotisch-ExtraBold SDF.asset`: 동적 폰트 아틀라스. 플레이·테스트·에디터 종료 때마다 바뀐다.
  - `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/ProjectSettings.asset`: 줄바꿈만 바뀌거나 define·`runInBackground` 줄만 바뀐 경우
  - `git checkout -- <파일>`로 되돌린다.
- 편집 모드에서 `PlayerSettings.runInBackground`를 바꾸지 않는다. 전체 테스트 때만 켜고, 끝나면 되돌린다.
- **Unity MCP RunCommand를 쓸 때**
  - 코드가 `Unity.AI.*` 네임스페이스 안에서 돈다. 이름이 겹치는 타입은 `UnityEngine.UI.Image`처럼 전체 이름을 쓴다.
  - `using System.Reflection`, `System.Diagnostics.Process`/`Stopwatch`, `System.IO.File.Delete`는 막혀 있다. 긴 코드는 `Assets/Editor/Temp*.cs`에 임시로 두고 부른 뒤 `.meta`까지 지운다.
  - 스크립트를 바꾸고 `AssetDatabase.Refresh()`를 부르면 도메인 리로드 동안 연결이 끊긴다. 15초쯤 뒤에 다시 시도한다.
  - 테스트가 남기는 경고 로그 때문에 도구가 "실패"로 표시해도, 결과 요약의 `passed=… failed=…`로 판단한다.

## 5. 테스트

- 메뉴 **Tools > Tests**. 코드로 부를 때는 아래를 쓴다.
  - 가벼운 테스트: `System.Type.GetType("TestSuites, WizardDuel.EditorTests").GetMethod("RunLight").Invoke(null, null)`
    - 플레이 모드에 들어가지 않고 요약 문자열을 바로 돌려준다.
    - 결과는 `Logs/TestResults/light.txt`
    - 기준: `passed=480 failed=0 skipped=1`. 건너뛰는 것은 `AiDuelWeaponTests.LobbyHasAButtonForEveryCatalogWeapon`이다.
  - 전체 테스트: `…GetMethod("StartFull")`
    - 플레이 모드 테스트까지 돌고, 도메인을 여러 번 다시 불러와 오래 걸린다.
    - 진행 중에는 `Logs/TestResults/full.txt.partial`, 끝나면 `full.txt`. 기준은 `passed=528 failed=0 skipped=1`이다.
    - 도는 동안 "Running managed callbacks", "Importing assets" 진행 창이 떠도 정상이다. 누르지 말고 결과 파일만 지켜본다.
    - 에디터가 포커스를 잃으면 프레임이 멈춰 플레이 모드 테스트가 실패할 수 있다. 시작 전 `PlayerSettings.runInBackground = true`로 두고 끝나면 되돌린다. 실패하면 실패한 테스트만 다시 돌려 확인한다.
- 커밋 전에 최소한 컴파일 오류 0과 가벼운 테스트 통과를 확인한다.
- 테스트에서 `AddComponent<NetworkManager>()`로 매니저를 만들면 `NetworkConfig`가 비어 있을 수 있다(플레이 모드 테스트 뒤 NGO가 정적 훅을 비운다). 만든 직후 `if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();`로 보장한다.
- 테스트 코드는 `Assets/Tests/` 밖에 두지 않는다.

## 6. 실행해서 확인하기

- **내 PC에서 1대1:** **Tools > Wizard Duel > Network Test** 창에서 Build → 1v1. 빌드는 `Build/LobbyTest/`, 로그는 `Logs/LobbyTest/`에 생긴다.
- **직접 띄우기**
  - 서버: `WizardDuel.exe -batchmode -nographics -server -logFile server.log`
  - 클라이언트: `WizardDuel.exe -join 127.0.0.1 -screen-fullscreen 0 -screen-width 960 -screen-height 540`
  - 띄운 프로세스는 PID를 적어 두고 그 PID로만 끈다. 이름으로 끄면 다른 사람이 띄운 게임 창까지 꺼진다.
- **로그에서 볼 줄:** `[Lobby] Listening`, `[Lobby] Approval … reason=…`, `[Arena] Ready … fingerprint=`, `[WeaponSpawn] Round …`, `[Combat] Hit …`, `[Combat] … dropped reason=…`
- **화면 캡처:** 창을 숨기면 `ScreenCapture.CaptureScreenshot` 결과가 생기지 않는다. 작은 창 모드로 띄운다.
- **입력 시험:** 실제 키보드 대신 Input System 가상 장치로 넣는다. `Assets/Tests/Editor/PlayModeTestFixture.cs`와 `LobbyInputTests`가 예시다.
- **리눅스 전용 서버 빌드**는 Unity에 Linux Build Support(Dedicated Server) 모듈이 있어야 한다. 없으면 `BuildPipeline.IsBuildTargetSupported`가 false다.
- **이미 알려진 동작(회귀 아님)**
  - 결과 화면 뒤 `[Deferred OnSpawn] RpcMessage` 경고: 이동 RPC가 Unreliable이라 디스폰 뒤에 도착한다.
  - 오프라인 더미가 아주 가까이서 안 맞음: 조준에 총구 위치 보정이 없다.
  - 봇이 양옆이 막힌 한 칸 통로에서 오래 서서 쏨: 옆걸음할 곳이 없으면 제자리에서 쏘는 설계다.
  - 끊긴 참조 경고: `DuelWizard-2`·`LightOrb_Visual` 프리팹 변형의 낡은 덮어쓰기 대상, `PC_Renderer`의 URP 디버그 리소스 슬롯

## 7. Git·문서·소통

- **브랜치:** 기준 브랜치는 `main`이다. `dev`는 더 쓰지 않는다. `main`에서 브랜치를 만들고 `main`으로 PR을 연다.
- **커밋**
  - 사용자가 시킬 때만 커밋·push 한다.
  - 메시지는 `종류(범위): 한글 요약` 형식이다. 종류는 `feat`, `fix`, `refactor`, `perf`, `chore`, `docs`, `test`.
  - 본문에 관련 이슈와 바뀐 동작을 적는다.
- **PR 본문:** "확인 필요" 절과 체크박스(`- [ ]`)를 넣지 않는다. 남은 확인 항목은 PR이 아니라 사용자에게 하는 보고에 쓴다. "간략하게"라고 하면 개요·원인·변경 내용만 쓴다.
- **이슈 본문:** 원인과 목적 위주로 쓰고, 파일·줄 번호 같은 세부는 빼서 다음 작업자가 직접 분석하게 둔다. 말투는 ~합니다체다.
- **언어:** 사용자에게 하는 보고·질문, 문서, 커밋 메시지는 한국어로 쓴다. 새로 쓰는 문장에서 로비는 "대기실" 대신 "로비 화면"이라고 쓴다. 게임 안 문구는 바꾸지 않는다.
- **문서**
  - 코드와 맞아야 한다. 경로·클래스·메뉴·수치를 바꾸면 해당 문서(README, `Docs/`, 폴더 README)도 같이 고친다.
  - README의 표는 첫 칸 폭을 고정한 HTML 표 형식을 유지한다.
  - 다시 쓸 일이 없는 일회성 검증 기록은 `Docs/`에 남기지 않는다.
- **로컬 전용 파일:** `.agents/`와 `Assets/Editor/TempBush*.cs`는 `.git/info/exclude`로 git에서 빠진 로컬 파일이다. 옮기거나 지우지 않는다.
