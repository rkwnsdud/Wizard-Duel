# 네트워크 테스트 창과 온라인 흐름

이 폴더의 `NetworkTestWindow.cs`(에디터 어셈블리 `WizardDuel.Editor`)는 게임 본편 `Assets/Scenes/Main.unity`를 빌드해 전용 서버와 클라이언트를 한 번에 띄우는 에디터 창이다. 연결은 Netcode for GameObjects와 Unity Transport의 직접 연결(UDP 7777)만 쓰고 Relay는 쓰지 않는다.

## Network Test 창

메뉴 **Tools > Wizard Duel > Network Test**로 연다.

| 버튼 | 하는 일 |
|---|---|
| **Build** | 바뀐 씬을 저장할지 물은 뒤 `Assets/Scenes/Main.unity` 한 씬을 Windows 64비트 Development 빌드로 `Build/LobbyTest/WizardDuel.exe`에 만든다. 띄운 프로세스가 있거나 플레이 모드 중이면 exe가 잠겨 있어 누를 수 없다. |
| **Server** | `-batchmode -nographics -server`로 창 없는 전용 서버를 띄운다. 서버는 하나만 띄울 수 있다. |
| **Client** | `-join 127.0.0.1`과 1280x720 창 모드로 클라이언트를 하나 더 띄운다. 이름은 `client1`, `client2` 순으로 붙는다. |
| **1v1** | 서버 하나와 클라이언트 둘을 함께 띄운다. |
| **Remote Client (limeskin.kr)** | `-join limeskin.kr`로 클라이언트를 띄워 리눅스 전용 서버([배포 문서](../../../Deploy/Server/README.md))에 접속한다. 이름은 `remote1`부터 붙는다. |
| **Kill** / **Close All** | 띄운 프로세스를 하나씩 또는 모두 강제 종료한다. 에디터를 끄면 띄운 프로세스도 모두 닫는다. |
| **Open Logs** | `Logs/LobbyTest/`를 연다. 로그는 프로세스 이름별로 `server.log`, `client1.log`, `remote1.log`처럼 남는다. |

- 빌드한 뒤 `Assets` 아래 파일이 바뀌면 빌드 시각 옆에 `(assets changed since)`가 붙는다. 이때는 다시 빌드한다.
- 플레이 모드에 들어가 도메인을 다시 불러와도, 띄운 프로세스 목록은 PID로 다시 찾아 이어서 보여 준다.
- `-join`은 접속할 주소만 정한다. 클라이언트 화면에서 방을 만들거나 방장이 알려 준 6자리 코드로 참가한다.
- 서버와 클라이언트는 같은 커밋으로 빌드해야 한다. 접속 요청에 실린 프로토콜 버전(`LobbyJoinRequest.CurrentVersion`)이 서버와 다르면 접속을 거절한다. 원격 서버에 붙을 때도 마찬가지다.
- 창을 클릭해 포커스를 준 클라이언트만 키보드·마우스 입력을 받는다.

## 온라인 흐름

1. **방 만들기와 참가**: 서버 하나가 방을 여러 개 연다(`LobbyNetwork`의 **Max Rooms**, 기본 50개). 방은 숫자 6자리 코드로 구분하고 방마다 두 명까지 들어온다. 방이 가득 찬 서버에 새 방을 만들면 "서버에 방이 가득 찼어요. 잠시 후 다시 시도해 주세요.", 두 명이 들어간 방에 참가하면 "이미 두 명이 참가한 대기실이에요."가 뜬다.
2. **로비 화면**: 방장이 맵 설정을 바꾸면 서버가 검사해 두 참가자에게 같은 설정을 보낸다. 두 명이 모이면 방장이 게임을 시작할 수 있다.
3. **전투 준비**: 시작이 승인되면 서버와 두 클라이언트가 같은 설정으로 전투 맵을 만든다. 각 클라이언트는 맵 지문(논리 레이아웃과 두 스폰 위치·회전의 SHA-256)을 서버에 보내고, 서버 지문과 다르면 그 클라이언트의 연결을 끊는다("맵이 서버와 달라서 연결이 끊겼어요."). 전용 서버는 지문, 스폰 위치, 무기 스폰 자리, 맵 배치만 기억하고 자기 맵은 곧바로 지운다. 그래서 여러 방이 같은 월드 좌표를 써도 서로의 맵에 닿지 않는다.
4. **전투**: 두 클라이언트의 지문이 모두 맞으면 방장은 `DuelWizard-1`, 참가자는 `DuelWizard-2`로 소환된다. 플레이어 오브젝트는 그 방의 두 클라이언트에게만 보이므로 다른 방의 플레이어·공격·체력은 전달되지 않는다. 소환 효과가 끝나면 이동·조준·공격 입력이 켜지고 무기 스폰이 시작된다.
5. **결과와 복귀**: 한쪽이 쓰러지면 결과를 보여 주고, `resultDuration`(기본 5초) 뒤 같은 방의 로비 화면으로 돌아간다.
6. **방 닫기**: 두 참가자 중 한 명이라도 나가면(강제 종료 포함) 그 방만 닫고 남은 사람을 내보낸다. 다른 방과 서버는 그대로 돈다. 강제 종료는 Unity Transport의 Disconnect Timeout(5초) 뒤에 감지된다.

> Unity Transport 2.7.4에서는 Windows에서 강제 종료된 상대에게 계속 보내면 수신 버퍼가 새어, 몇 초 뒤 서버가 아무 패킷도 받지 못했다. 2.7.5에서 고쳐졌으므로 서버는 소켓을 다시 만들지 않고 방만 닫는다. Transport 버전을 2.7.5 아래로 내리지 않는다.

전투 판정은 공격한 클라이언트가 명중을 판정해 `ReportHitRpc`로 보고하고, 서버가 `ServerHitReport`에서 받을지 버릴지 정한 뒤 체력을 깎는다. 받는 보고와 버리는 보고의 규칙은 [서버 보고 처리 규칙](../../../Docs/ServerHitReportRules.md)에 있다.

## 로그로 확인하기

`Logs/LobbyTest/`의 로그에서 다음 줄을 찾으면 흐름을 따라갈 수 있다.

- `[Lobby] Listening UDP=… maxrooms=…`: 서버가 열렸다. 리눅스 배포 스크립트도 이 줄을 기다린다.
- `[Lobby] Connecting`, `[Lobby] Joined code=…`: 클라이언트가 접속해 방에 들어갔다.
- `[Arena] Ready … fingerprint=…`, `[Arena] Spawn 0/1`: 각 프로세스가 만든 전투 맵의 지문과 스폰 위치. 서버와 두 클라이언트의 값이 같아야 한다.
- `[Lobby] Client … map ready (n/2)`, `[Lobby] Players spawned`: 지문이 맞은 클라이언트 수와 소환.
- `[WeaponSpawn] Round …`: 판마다 고른 스폰 자리 수와 떨어진 무기.
- `[Combat] Hit … health=`: 서버가 받은 명중과 남은 체력. `[Combat] Hit dropped reason=…`, `[Combat] DoT dropped reason=…`은 버린 보고와 이유다.
- `[Lobby] Player defeated`, `[Lobby] Closed`: 승부가 난 판과 닫힌 방.

로비와 연결 규칙의 테스트는 `Assets/Tests/Editor/Lobby/`에 있다. 플레이 모드가 필요한 테스트(`PlayMode` 범주)는 **Tools > Tests > 전체 테스트 실행 (플레이 모드 포함)**에서만 돌고, 나머지는 **가벼운 테스트 실행**에도 포함된다.
