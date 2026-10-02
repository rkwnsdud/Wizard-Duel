# 1대1 온라인 연결 테스트

`Assets/Multiplayer/NetworkDemo.unity`에서 Netcode for GameObjects로 두 플레이어를 연결한다. 직접 연결(UDP 7777)만 사용하며 Relay는 쓰지 않는다.

## 실행

- **Tools > Wizard Duel > Network Test** 창에서 명령어 없이 테스트한다.
  - **Build**: 이 씬만 Development 빌드로 `Build/NetworkTest/`에 만든다. 빌드 뒤 에셋이 바뀌면 `(assets changed since)`가 붙으니 다시 누른다.
  - **Server**: 창 없는 전용 서버. **Client**: 640x480 창으로 띄워 바로 `127.0.0.1`에 접속하며, 누를 때마다 하나씩 늘어난다. **1v1**: 서버와 클라이언트 두 개.
  - 목록의 **Kill**은 강제 종료다. **Close All**로 모두 닫고, 에디터를 끄면 함께 닫힌다. 로그는 **Open Logs**(`Logs/NetworkTest/`).
- 게임 화면: 서버 IP(같은 PC는 `127.0.0.1`)를 입력하고 **Join**한다. 호스트 모드는 없고 서버는 항상 전용 서버다. **Server** 버튼(에디터/개발 빌드)은 그 인스턴스를 전용 서버로 띄운다.
- 빌드 실행 인자
  - `WizardDuel.exe -batchmode -nographics -server -logFile server.log`: 창 없는 전용 서버
  - `WizardDuel.exe -join 127.0.0.1 -screen-fullscreen 0 -screen-width 640 -screen-height 480`: 실행 즉시 접속

세 번째 접속은 `This match already has two players.`로 거절된다. 한 명이라도 나가면(강제 종료 포함) 방이 닫혀 남은 플레이어는 `A player left, so the room was closed.`를 받고 메뉴로 돌아간다. 서버는 새 방을 다시 연다. 강제 종료는 UnityTransport의 Disconnect Timeout(5초) 뒤에 감지된다. 창을 클릭해 포커스를 준 쪽만 WASD/마우스 입력을 받는다.

> 방을 닫는 이유: Windows에서 강제 종료된 상대에게 계속 보내면 Unity Transport 2.7.4의 수신 버퍼가 새어, 몇 초 뒤 서버가 아무 패킷도 받지 못한다. 연결을 끊고 소켓을 새로 만들어야 회복된다.

## 구성

- `NetworkDemo`: 연결 UI, 접속 승인, 방 닫기. **빈 슬롯 순서**로 `Player-1`(파랑)/`Player-2`(빨강) 프리팹과 `GridMap` 스폰 위치를 정한다.
- `NetworkDemoPlayer`: 소유자에게만 `PlayerMovement`·`PlayerAim`·`CharacterController`·카메라 추적을 켜고, 상대는 받은 이동량으로 걷기 애니메이션만 재생한다.
- `Assets/Player/Player-1.prefab`: 기존 마법사에 `NetworkObject`, 소유자 권한 `NetworkTransform`(위치·Y 회전), `NetworkDemoPlayer`를 붙인 프리팹. 공격·체력은 아직 동기화하지 않는다.
- `Assets/Player/Player-2.prefab`: `Player-1`의 변형으로 로브 재질(`RobeRed.mat`)과 `slot`만 바꾼다.
- 두 프리팹은 `Assets/DefaultNetworkPrefabs.asset`에 등록되어 있고, 이 목록이 씬의 NetworkManager **Network Prefabs Lists**에 연결되어 있어야 2P가 스폰된다.
