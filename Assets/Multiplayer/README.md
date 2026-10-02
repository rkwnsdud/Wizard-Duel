# 1대1 온라인 연결 테스트

`Assets/Multiplayer/NetworkDemo.unity`에서 Netcode for GameObjects로 두 플레이어를 연결한다. 직접 연결(UDP 7777)만 사용하며 Relay는 쓰지 않는다.

## 실행

- **Host**: 서버와 1P를 겸한다. 다른 인스턴스에서 호스트 IP(같은 PC는 `127.0.0.1`)를 입력하고 **Join**하면 2P가 된다.
- **Server**(에디터/개발 빌드): 플레이어가 없는 전용 서버. 클라이언트 두 개가 각각 Join한다.
- 빌드 실행 인자
  - `WizardDuel.exe -batchmode -nographics -server -logFile server.log`: 창 없는 전용 서버
  - `WizardDuel.exe -join 127.0.0.1 -screen-fullscreen 0 -screen-width 640 -screen-height 480`: 실행 즉시 접속

세 번째 접속은 `This match already has two players.`로 거절되고, 나간 플레이어의 슬롯은 다음 접속자가 이어받는다. 창을 클릭해 포커스를 준 쪽만 WASD/마우스 입력을 받는다.

## 구성

- `NetworkDemo`: 연결 UI, 접속 승인. 호스트 여부가 아니라 **빈 슬롯 순서**로 `Player-1`(파랑)/`Player-2`(빨강) 프리팹과 `GridMap` 스폰 위치를 정한다.
- `NetworkDemoPlayer`: 소유자에게만 `PlayerMovement`·`PlayerAim`·`CharacterController`·카메라 추적을 켜고, 상대는 받은 이동량으로 걷기 애니메이션만 재생한다.
- `Assets/Player/Player-1.prefab`: 기존 마법사에 `NetworkObject`, 소유자 권한 `NetworkTransform`(위치·Y 회전), `NetworkDemoPlayer`를 붙인 프리팹. 공격·체력은 아직 동기화하지 않는다.
- `Assets/Player/Player-2.prefab`: `Player-1`의 변형으로 로브 재질(`RobeRed.mat`)과 `slot`만 바꾼다.
- 두 프리팹은 `Assets/DefaultNetworkPrefabs.asset`에 등록되어 있고, 이 목록이 씬의 NetworkManager **Network Prefabs Lists**에 연결되어 있어야 2P가 스폰된다.
