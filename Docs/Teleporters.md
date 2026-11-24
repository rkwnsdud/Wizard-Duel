# 텔레포트 타일

전투 맵에 2×2 크기의 텔레포트 타일을 쌍으로 깐다. 한 쌍의 두 타일은 같은 색이고, 타일 가운데에 서면 짝 타일 가운데로 순간 이동한다. 이동이 끝나면 그 쌍의 두 타일이 함께 5초 쿨타임에 들어간다.

## 설정

- 로비 화면 맵 설정의 **텔레포트** 줄에서 0~3쌍을 고른다(`DuelSettings.MaxTeleporterPairs`). 기본값은 1쌍이고, 0이면 타일이 없다.
- 값은 `MapGenerationSettings.TeleporterPairs`로 맵 생성기에 들어가고 `NetworkMapSettings`로 클라이언트에 전해진다. 생성기 자체는 0~9쌍(`TeleporterRegions.MaxPairs`)을 받는다.
- 둘 자리가 모자라면 요청보다 적게 둔다. 배치 규칙은 [맵 생성 문서](../Assets/Map/Generation/README.md)의 텔레포트 단계에 있다.
- 무기 스폰 자리는 타일과 그 둘레 1칸을 피한다(`WeaponSpawnPlacement`).

## 이동 흐름

이동은 서버가 승인하고, 승인된 이동은 소유자 화면이 한다. 이동 동기화가 소유자 권한이기 때문이다.

1. 소유자 화면의 `PlayerTeleporter`가 매 프레임 캐릭터 위치를 본다. 타일 가운데에서 `PlayerTeleporter.TriggerRadius`(0.35m) 안에 들어오고, 전투 중이며 이동할 수 있는 상태면 요청을 보낸다.
2. 서버의 `ArenaTeleporters`가 요청을 받는다. 보낸 클라이언트가 그 플레이어의 소유자인지 확인한 뒤 아래 순서로 버릴 이유를 찾는다.

   | 이유 | 뜻 |
   |---|---|
   | `round` | 요청이 이전 판의 것이다 |
   | `pair` | 없는 쌍 번호다 |
   | `combat` | 전투 중이 아니거나 쓰러졌다(`DuelPlayer.CanUseInventory`) |
   | `cooldown` | 그 쌍이 아직 쿨타임이다 |

   버린 요청은 `[Teleport] Round … dropped reason=…` 로그를 남긴다. 받아 준 요청은 쌍 쿨타임을 시작하고 `[Teleport] Round … side=a->b` 로그를 남긴다.
3. 서버는 그 방의 두 클라이언트에게 쌍마다 다시 쓸 수 있는 서버 시각과 이동 이벤트를 보낸다. 각 화면은 이 값으로 타일 빛을 맞춘다.
4. 이동 이벤트의 플레이어를 이 화면이 소유하면(내 캐릭터, 또는 호스트의 봇) `PlayerTeleporter.Arrive`가 짝 타일 가운데로 옮긴다. 다른 화면은 이동 동기화에서 순간 이동으로 받는다.
5. 도착한 뒤에는 모든 타일 밖으로 한 번 나가야 다시 요청한다. 도착한 타일에서 바로 되돌아가지 않는다.

- 통신은 named message 두 개다. 요청은 `WizardDuel.TeleportRequest.v1`, 상태는 `WizardDuel.TeleportState.v1`이다. `DuelArena.RegisterMessages`가 무기 스폰 통신과 함께 붙인다.
- 판은 `DuelArena`가 전투를 켤 때 `BeginRound`로 시작하고, 전투를 끄거나 판을 정리할 때 `EndRound`로 닫는다. 판이 닫히면 클라이언트는 더 요청하지 않는다.
- 답이 1초 안에 오지 않으면 같은 타일에서 다시 요청할 수 있다. 같은 요청 번호가 다시 오면 서버는 상태만 다시 보낸다.
- 위치나 거리 같은 부정 입력은 검사하지 않는다. 전제는 "악의적인 플레이어는 없다"이다.

## 카메라

이 화면의 카메라가 따라가는 캐릭터가 이동하면 `CameraFollow.ChaseFrom`이 이동 전 자리에서 새 자리까지 카메라를 부드럽게 뒤쫓게 한다. 걸리는 시간은 `DuelCameraSettings`의 `teleportChaseTime`(기본 0.35초)이고, 0이면 바로 옮겨 간다.

## 모양과 빛

- 프리팹은 `Assets/Map/Blocks/Teleporters/Teleporter.prefab`이다. 메뉴 **Tools > Wizard Duel > Rebuild Teleporter Tile Assets**(`TeleporterAssetsBuilder`)가 메시·머티리얼·프리팹을 다시 만든다. 모양은 웹 시안 A안 "룬 석판"이다.
- `BlockFactory`의 `Teleporter Block`에 이 프리팹을 지정한다. 비워 두면 경고를 남기고 타일 칸에 바닥만 깐다. 타일 칸에는 늘 바닥 블록이 따로 깔리므로 프리팹에는 콜라이더가 없다.
- 타일은 일반 머티리얼(Teleporter Stone, Teleporter Trim)과 하이라이트 머티리얼(Teleporter Glow)로 이뤄진다. `TeleporterBlock`은 자식 렌더러에서 하이라이트 머티리얼을 쓰는 슬롯만 찾아 쌍 색과 상태에 맞춰 칠한다. 모델을 바꿔도 빛낼 부분에 같은 하이라이트 머티리얼만 쓰면 된다.
- 하이라이트 머티리얼은 `_EMISSION`을 켜 두고 GI 플래그를 실시간 발광으로 둔다. GI 플래그에 발광이 없으면 에디터에서 렌더할 때 URP가 `_EMISSION`을 꺼 타일이 빛나지 않는다.
- 쌍 색은 쌍 번호 순서로 보라 `#9d6bff`, 초록 `#3ee08f`, 분홍 `#ff5cc6`이다.

| 상태 | 빛 | 값(`TeleporterGlowSettings`) |
|---|---|---|
| 쓸 수 있음 | 은은하게 빛나며 안쪽 무늬가 돈다 | 세기 0.6, 오르내림 ±15%(2.5초), 회전 15도/초 |
| 밟음 | 크게 번쩍인 뒤 쿨타임 빛으로 줄어든다 | 세기 6.5에서 0.6초 동안 |
| 쿨타임 | 어둡고 무늬가 멈춘다 | 세기 0.04, 바탕색 밝기 30% |
| 쿨타임 끝 | 쓸 수 있는 빛으로 돌아온다 | 0.4초 |

## AI 봇

봇의 길찾기(`BotGrid`)는 타일 칸에 비용을 더해 돌아가고, 곧게 걸어갈 수 있는지 볼 때도(`HasClearPath`) 타일을 지나는 선은 막힌 것으로 본다. 쏘기와 시야(`HasClearLine`)는 타일을 막지 않는다. 그래도 타일 가운데를 밟아 이동되면 길과 끼임 판정을 비우고 새 자리에서 다시 길을 찾는다.

## 오프라인 테스트 씬

`DuelOfflineTest` 씬의 맵 레이아웃에 숫자 `1`로 타일 한 쌍이 들어 있다. 오프라인 테스트는 전투를 켤 때 자기 자신을 판 키로 `BeginRound`를 부르고, 다시 시작할 때 판을 닫는다.

## 손으로 쓴 레이아웃

`GridMap` 레이아웃에서는 쌍 번호를 `1`부터 숫자로 적는다. 같은 숫자는 2×2 타일 정확히 두 개여야 하고 숫자는 `1`부터 빠짐없이 쓴다(`TeleporterRegions.Parse`). 숫자 칸에는 바닥이 깔린다.

## 테스트

가벼운 테스트에 들어 있다.

- `TeleporterPlacementTests`: 배치 규칙, 대칭 짝, 쌍 수와 상관없이 같은 나머지 맵, 레이아웃 읽기
- `TeleporterFieldTests`: 타일 가운데 좌표와 판정 범위, 상태별 빛, 하이라이트 슬롯만 칠하기, 발광 키워드
- `TeleporterRulesTests`: 쌍 쿨타임, 무기 자리 제외, 봇 길찾기, 카메라 뒤쫓기
- 맵 설정 직렬화·검증 테스트(`NetworkMapSettingsTests`, `NetworkLobbySnapshotTests`)의 쌍 수 항목
