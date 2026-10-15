# 1대1 온라인 연결 테스트

`Assets/Scenes/NetworkDemo.unity`에서 Netcode for GameObjects로 두 플레이어를 연결한다. 직접 연결(UDP 7777)만 사용하며 Relay는 쓰지 않는다.

## Main 대기실 테스트

**Tools > Wizard Duel > Network Test** 창의 **Mode**를 `Main`으로 두고 **Build**를 누르면 `Assets/Scenes/Main.unity`만 Development 빌드해 `Build/LobbyTest/WizardDuel.exe`에 만든다. **Server**는 창 없는 전용 서버로 UDP 7777을 열고, **Client**는 보이는 창에서 `-join 127.0.0.1` 주소를 설정해 시작 화면을 띄운다. `-join`은 방을 만들거나 참가하지 않으므로, 클라이언트 화면에서 **Create**를 누르거나 방장이 보여 준 6자리 코드로 **Join**한다.

서버는 한 번에 방 하나와 확정 참가자 두 명만 받는다. Main 대기실에서 방을 만들면 현재 `TitleBackdrop.Current`의 전체 설정을 복사하고, 서버는 전달받은 시드를 그대로 저장해 두 참가자에게 동기화한다. 시드는 입력란에서 편집하거나 **새로 뽑기**로 바꿀 수 있으며, 다른 설정을 변경해도 시드는 유지된다. 접속한 두 참가자와 서버는 프로토콜 버전 2를 사용하므로 같은 빌드로 실행해야 한다. 확정 참가자 중 한 명이라도 나가면 방 전체를 닫는다. Main 빌드는 대기실·설정 동기화·맵 미리보기 검증 범위이며 전투는 시작하지 않는다. 빌드와 로그는 NetworkDemo와 분리해 `Build/LobbyTest/`, `Logs/LobbyTest/server.log`, `client1.log`, `client2.log`에 둔다. `Open Logs`에서 같은 로그 폴더를 열 수 있고, `[LobbyPreview]` 로그의 시드·형태·크기·스폰 셀·hash를 비교한다. 상세 비교 기록은 `Logs/LobbyTest/stage3-preview-comparison.json`에 있다.

검사와 인계:

> 아래 Stage 3/4 결과와 최종 상태는 자동 시드 정책을 사용하던 당시의 역사 기록이다. 현재 seed pass-through 동작의 검증 결과로 해석하지 않는다.

- Unity Test Runner의 EditMode에서 `LobbySessionTests`, `NetworkLobbySnapshotTests`, `LobbyNetworkSettingsTests`, `NetworkMapSettingsTests`, `LobbyInputTests`를 실행한다.
- `Logs/LobbyTest/stage3-validation.txt`에는 412/412 통과, Main Development 빌드 성공, 실제 UI 미리보기 hash 일치가 기록되어 있다. 이는 Stage 3의 이전 검증 결과이며, Stage 4 전체 EditMode 결과는 아래에 기록한다.
- `Logs/LobbyTest/stage4-validation.txt`에는 방 종료·서버 재시작·오프라인 참가 취소와 최종 Main 실제 UI 시나리오, 호스트·게스트 preview hash 대조를 기록했다. Unity Test Runner에서 `Assembly-CSharp-Editor`와 `WizardDuel.MapGeneration.EditorTests`를 포함해 EditMode 전체를 실행한다. `stage4-complete.xml` 결과는 422/422 통과, 실패·건너뜀 0이다. 여기에 DynamicUpdate에서 가상 키보드 `QueueStateEvent`로 실행한 `LobbyInputTests` 6개가 포함된다. 이것은 물리 키보드 Esc 입력을 검증한 결과가 아니다.
- `Logs/NetworkTest/LobbyRegression/`의 Stage 4 NetworkDemo 로그는 seed 7301 fingerprint `002A0CBA456D3E38F8B6FBA8B63A246EFBDDB462163C2CEED7DB6AE16EEBD1E6` 일치, 1명 준비 중 미스폰, 2명 준비 후 두 플레이어의 스폰 위치·회전 일치, 3번째 참가 거절, 게스트 강제 종료 후 방 닫힘과 서버 재수신을 확인한다. 첫 Demo Development 빌드는 성공(0 errors, 485 warnings)했으며 기존 Sentis/`ConvGeneric.compute` 셰이더 경고가 포함됐다. 후속 증분 빌드 `stage4-networkdemo-incremental-build.json`은 성공, 0 errors, 0 warnings이다.
- 같은 NetworkDemo 런타임 로그에는 서버와 client1/client2에서 각각 2회 `LightningPreview requires a PreviewVisual LineRenderer reference.` 오류가 있다. 기존 프리팹의 `LightningWeapon.Awake`가 참조 없는 `LightningPreview`를 `AddComponent`하는 경로에서 나온다. 무기·플레이어·Demo 파일은 기준 브랜치와 diff가 없음을 독립 검토했지만 기준 바이너리는 재실행하지 않아 기준 빌드에서도 재현된다고 단정하지 않는다. 이 오류는 현재 대기실 변경 범위 밖이며 그대로 기록한다.
- 최종 상태: Stage 4 EditMode 422/422(실패·건너뜀 0), Main 및 NetworkDemo 증분 Development 빌드, Main 실제 Create/Join/Leave UI 확인이 완료됐다. Main 첫 빌드는 0 errors, 485 기존 셰이더 warnings였고 최종 증분 빌드는 0 errors, 0 warnings였다. 실제 UI에서 코드 206204의 호스트·게스트 자동 시드 357524259와 Circle(36,26), spawnA (32,10), spawnB (3,15), hash C5CCBDE2가 일치했고, 두 실행 모두 playerobjects=0/mapgenerated=false였다. 호스트 Leave 후 게스트가 시작 화면으로 돌아오고 서버 UDP 7777 재수신 및 같은 호스트의 새 방 202405 생성도 확인했다. 최종 바이너리에는 임시 InputDiagnostics/ValidationRunner가 없다. 가상 키보드 테스트는 통과했지만 IMGUI Esc 주입은 raw InputSystem 이벤트를 만들지 않으므로 물리 키보드 Esc 동작은 확인하지 않았다.
- PR #37은 기존 UI 변경 PR이며 현재 Open, base는 `main`이다. 이 후속 네트워크 브랜치의 비교 기준은 `issue-19-lobby`이며, `codex/issue-19-lobby-network`는 `af9704f7e4b7593eb38265841276f314daec4a67`에서 분기했다. PR #37 병합 뒤에는 분기점부터 현재 HEAD까지 이 브랜치의 커밋만 최신 `main`으로 옮겨 다시 검증한다.

## 실행

- **Tools > Wizard Duel > Network Test** 창에서 **Mode: NetworkDemo**를 선택해 명령어 없이 테스트한다.
  - **Build**: 이 씬만 Development 빌드로 `Build/NetworkTest/`에 만든다. 빌드 뒤 에셋이 바뀌면 `(assets changed since)`가 붙으니 다시 누른다.
  - **Server**: 창 없는 전용 서버. **Client**: 1280x720 창으로 띄워 바로 `127.0.0.1`에 접속하며, 누를 때마다 하나씩 늘어난다. **1v1**: 서버와 클라이언트 두 개.
  - 목록의 **Kill**은 강제 종료다. **Close All**로 모두 닫고, 에디터를 끄면 함께 닫힌다. 로그는 **Open Logs**(`Logs/NetworkTest/`).
- 게임 화면: 서버 IP(같은 PC는 `127.0.0.1`)를 입력하고 **Join**한다. 호스트 모드는 없고 서버는 항상 전용 서버다. **Server** 버튼(에디터/개발 빌드)은 그 인스턴스를 전용 서버로 띄운다.
- 빌드 실행 인자
  - `WizardDuel.exe -batchmode -nographics -server -logFile server.log`: 창 없는 전용 서버
  - 서버에 `-mapSeed 1234`를 추가하면 Inspector의 시드 대신 해당 시드로 맵을 만든다. 클라이언트의 로컬 시드 설정은 사용하지 않는다.
  - `WizardDuel.exe -join 127.0.0.1 -screen-fullscreen 0 -screen-width 1280 -screen-height 720`: 실행 즉시 접속

세 번째 접속은 `This match already has two players.`로 거절된다. 한 명이라도 나가면(강제 종료 포함) 방이 닫혀 남은 플레이어는 `A player left, so the room was closed.`를 받고 메뉴로 돌아간다. 서버는 새 방을 다시 연다. 강제 종료는 UnityTransport의 Disconnect Timeout(5초) 뒤에 감지된다. 창을 클릭해 포커스를 준 쪽만 WASD/마우스 입력을 받는다.

> 방을 닫는 이유: Windows에서 강제 종료된 상대에게 계속 보내면 Unity Transport 2.7.4의 수신 버퍼가 새어, 몇 초 뒤 서버가 아무 패킷도 받지 못한다. 연결을 끊고 소켓을 새로 만들어야 회복된다.

## 구성

- `NetworkDemo`: 연결 UI, 접속 승인, 맵 설정 동기화, 방 닫기. **빈 슬롯 순서**로 `Player-1`(파랑)/`Player-2`(빨강)을 정하고, `ISpawnPointProvider`가 제공하는 두 스폰 위치와 회전에 배치한다.
- `ProceduralMap`: 서버가 Inspector의 생성 설정(또는 `-mapSeed`)으로 맵을 생성한다. `NetworkMapSettings`로 시드, 자동/고정 크기, 형태, 장애물 밀도, 대칭, 노이즈 강도·스케일을 접속한 클라이언트에 전달한다. 씬의 `Generate On Awake`는 꺼 두어 클라이언트가 서버 설정을 받기 전에 맵을 생성하지 않는다.
- 클라이언트는 같은 설정으로 맵을 생성한 뒤 논리 레이아웃과 두 월드 스폰 위치·회전의 SHA-256 지문을 준비 응답에 보낸다. 서버 맵과 두 클라이언트의 지문이 모두 일치한 뒤 두 플레이어를 함께 생성한다. 한 명만 접속한 동안은 스폰하지 않으며, 지문이 다르면 해당 접속을 종료한다.
- `NetworkDemoPlayer`: 소유자에게만 `PlayerMovement`·`PlayerAim`·`PlayerAttack`·`CharacterController`·카메라 추적을 켜고, 상대는 받은 이동량으로 걷기 애니메이션만 재생한다.
- `Assets/Player/Player-1.prefab`: 기존 마법사에 `NetworkObject`, 소유자 권한 `NetworkTransform`(위치·Y 회전), `NetworkDemoPlayer`를 붙인 프리팹. 공격(`PlayerAttack`·`LightningWeapon`)은 `Player.prefab`과 같게 구성했지만 자기 화면에서만 보이고, 체력은 아직 없다.
- `Assets/Player/Player-2.prefab`: `Player-1`의 변형으로 로브 재질(`RobeRed.mat`)과 `slot`만 바꾼다.
- 두 프리팹은 `Assets/DefaultNetworkPrefabs.asset`에 등록되어 있고, 이 목록이 씬의 NetworkManager **Network Prefabs Lists**에 연결되어 있어야 2P가 스폰된다.

## 맵 동기화 검증

- Unity Test Runner의 EditMode에서 `NetworkMapSettingsTests`를 실행하면 서로 다른 시드와 자동/고정 크기의 설정을 실제 Netcode 버퍼에 직렬화한 뒤 논리 레이아웃과 스폰 셀이 보존되는지 확인한다.
- 서버를 `-mapSeed 7301`, `-mapSeed 0`, `-mapSeed -11`로 각각 실행하고 클라이언트 두 개를 접속한다. 세 로그의 `[Map] Ready` 설정·지문과 `[Map] Spawn` 위치·회전이 같은지 비교한다.
- 첫 클라이언트만 접속했을 때 `[Player] Spawned`가 없어야 한다. 두 번째 클라이언트가 준비된 뒤 서버에 `map ready (2/2)`와 두 플레이어의 배치 로그가 생기며, 각 클라이언트의 `[Player] Spawned` 위치·회전은 해당 슬롯의 `[Map] Spawn`과 같아야 한다.
- 세 번째 접속 거절 및 한 명 이탈 후 방 재개 흐름은 동일하다. 재접속한 두 클라이언트도 새로 맵 준비를 완료해야 스폰된다.
