# 무기 구조와 새 무기 추가

무기는 공통 계약(`Assets/Game/Weapons/Common/Scripts/`)을 구현한 프리팹과, 그 프리팹을 가리키는 무기 정의(`Assets/Game/Weapons/Definitions/`)로 이루어진다. 무기 코드는 Netcode를 모른다. 네트워크 전달은 `DuelPlayer`가 맡고, 무기는 받은 `IShotSink`로 연출과 명중만 넘긴다.

플레이어의 이동 충돌은 루트 `CharacterController`가 맡고, 피격 범위는 자식 `Hitbox`의 트리거 `CapsuleCollider`로 조절한다. 현재 피격 캡슐은 반지름 0.5, 높이 2.2, 중심 Y 1.1이며 `DuelWizard-2`는 기본 프리팹의 Hitbox를 상속한다. 프로젝트 6번 레이어의 이름은 `Hitbox`이며, 무기 대상 검색은 이 레이어와 `QueryTriggerInteraction.Collide`를 사용한다. 이동용 콜라이더와 일반 트리거는 명중 대상에서 제외하고, 벽·장애물 검사는 트리거를 무시한다. 테스트의 피격 대상도 같은 레이어의 트리거로 구성한다.

## 무기 목록

| id | 무기 | 정의 | 무기 컴포넌트 | 스폰 |
|---|---|---|---|---|
| 1 | 기본 완드 | `BasicWand.asset` | `BasicWandWeapon` | 안 함(시작 무기) |
| 2 | 번개 지팡이 | `LightningStaff.asset` | `DuelLightningWeapon` | 함 |
| 3 | 빛 반사 구슬 | `LightOrb.asset` | `LightOrbWeapon` | 함 |
| 4 | 폭발 마법 책 | `ExplosionMagicBook.asset` | `ExplosionMagicWeapon` | 함 |
| 5 | 화염 향로 | `FireCenser.asset` | `FireCenserWeapon` | 함 |

id 0은 "무기 없음"이다. 무기 이름과 시전 클립은 각 무기 폴더(`Assets/Game/Weapons/<무기>/`)의 프리팹과 `Art/`에 있다.

## 공통 계약

| 타입 | 하는 일 |
|---|---|
| `WeaponBase` | 모든 무기의 기반 컴포넌트. 무기 정의(`Definition`, `Id`), 시전 클립(`AttackAnimation`), 재생 속도(`AttackAnimSpeed`), 공격 간격(`AttackCooldown`, 기본 0.65초)을 가진다. 시전 클립에는 `FireMagic`과 `EndAttack` 애니메이션 이벤트가 있어야 한다. |
| `IWeapon` | 발사 계약. `Fire(AttackInfo, direction, charge, IShotSink)`는 공격한 쪽 화면에서 `FireMagic` 때 한 번 불린다. `Present(ShotEvent)`는 두 화면 모두에서 연출만 한다. 명중 판정과 피해는 `Present`에서 하지 않는다. |
| `IShotSink` | 무기가 결과를 넘기는 곳. `Broadcast(ShotEvent)`로 연출을 보내고, `Hit(AttackInfo, hitIndex, target, damage)`로 명중을 보고한다. 연출을 명중보다 먼저 보낸다. |
| `AttackInfo` | 공격 하나의 번호와 쏜 무기 id. 공격을 시작할 때 정하고 그 공격의 연출·명중 보고·적중 효과에 그대로 싣는다. 자세한 규칙은 `AttackInfo.cs` 주석에 있다. |
| `ShotEvent` | 연출 한 건. `Kind`(발사·끝 등)는 무기가 정하고, `Start`·`Point`·`Charge`를 담는다. |
| `IChargeableWeapon` | 충전 중 미리보기(`ShowChargePreview`, `HideChargePreview`). 번개 지팡이가 쓴다. |
| `IWeaponReach` | 충전 정도에 따른 사거리와 탄속. AI 상대가 공격 거리를 고를 때 쓴다. 없으면 AI는 기본 사거리(`DuelBotTuning.fallbackReach`)를 쓴다. |
| `IWeaponEquipPassive` | 장착·해제 때 효과(예: 향로의 이동 속도 증가). |
| `IWeaponHitPassive` | 서버에서 명중이 받아들여진 뒤 효과(예: 향로의 화상). |
| `IWeaponPose` | 매 프레임 캐릭터 뼈를 고치는 무기 자세. 사망 모션 동안 꺼진다. |
| `IWeaponDisplayAnimation` | 로비 화면·바닥 픽업·아이콘 전시 때도 켜 두는 무기 자체 애니메이션. 나머지 컴포넌트는 전시 때 꺼진다. |

패시브는 [무기 패시브와 쿨다운](WeaponPassivesAndCooldown.md), 상태이상은 [이동 속도 배율과 상태이상](MoveSpeedAndStatusEffects.md)에 정리되어 있다.

## 공격 한 번의 흐름

1. 공격한 쪽 `PlayerAttack`이 공격 번호를 정하고 시전 애니메이션을 시작한다. `DuelPlayer`가 `PlayAttackRpc`로 상대 화면에도 같은 공격 시작을 알린다.
2. 애니메이션의 `FireMagic` 이벤트에서 `DuelPlayer`가 `PlayerAttack.FindWeapon(weaponId)`로 쏜 무기를 찾아 `IWeapon.Fire`를 부른다.
3. 무기가 `sink.Broadcast`를 부르면 내 화면에서 바로 `Present`하고, `ShotRpc`로 상대 화면에서도 같은 연출을 한다.
4. 무기가 `sink.Hit`을 부르면 `ReportHitRpc`로 서버에 보고한다. 서버는 `ServerHitReport`에서 받을지 정하고([서버 보고 처리 규칙](ServerHitReportRules.md)), 받으면 체력을 깎고 쏜 무기의 `IWeaponHitPassive`를 실행한다.

이벤트는 지금 든 무기가 아니라 `AttackInfo`의 무기 id로 찾은 무기가 처리한다. 쏜 뒤 무기를 바꿔도 예전 무기는 꺼진 채 남아 있으므로, 늦게 온 연출과 명중도 쏜 무기 기준으로 처리된다.

## 무기 교체와 자세

- `PlayerAttack.TryEquipWeapon`은 충전·시전 중에는 무기를 바꾸지 않는다. 바닥 무기 줍기와 교체는 서버가 확인한 뒤 `PlayerWeaponInventory`가 무기 소켓 아래에 무기 인스턴스를 만들어 반영한다([무기 스폰](WeaponSpawning.md)).
- 손 자세를 고치는 무기는 `PlayerAttack.IsWeaponPoseActive(this)`가 참일 때만 고친다. 숨겨진 무기의 자세 보정이 들고 있는 무기의 자세를 덮어쓰지 않게 하려는 규칙이다.

## 새 무기 추가

1. `Assets/Game/Weapons/<무기>/` 아래에 `Scripts/`, `Prefabs/`, `Art/`를 만든다. 무기 스크립트는 `WizardDuel.Game` 어셈블리에 들어간다.
2. 무기 컴포넌트를 `WeaponBase`를 상속하고 `IWeapon`을 구현해 만든다. 피해는 `sink.Hit`으로만 보고하고, 여러 번 때리거나 여러 대상을 맞히면 `hitIndex`를 바꿔 보고한다. AI 상대가 거리를 맞추게 하려면 `IWeaponReach`도 구현한다.
3. 무기 프리팹에 이 컴포넌트를 붙이고, `FireMagic`·`EndAttack` 이벤트가 있는 시전 클립과 필요한 투사체·연출 프리팹을 연결한다. 장착·적중 패시브는 각각 하나까지만 붙인다. 전시 중에도 돌아야 하는 무기 자체 애니메이션에는 `IWeaponDisplayAnimation`을 구현한다.
4. **Create > Wizard Duel > Weapon Definition**으로 `Assets/Game/Weapons/Definitions/`에 정의를 만든다. 다른 무기와 겹치지 않는 id(1~255), 표시 이름(`displayName`), HUD 이름(`hudDisplayName`), 스폰 여부(`canSpawn`), 전시 회전·크기를 넣는다. **정의의 `prefab`에는 무기 프리팹을, 그 프리팹의 `WeaponBase.definition`에는 같은 정의를 연결**한다. 양쪽 연결이 모두 필요하다.
5. `Assets/Game/Battle/WeaponSpawnCatalog.asset`의 정의 목록에 추가한다. 무기 스폰과 AI 로비 화면의 무기 선택, HUD, 오프라인 테스트의 무기 목록이 이 목록을 읽는다. 스폰 대상은 `canSpawn`이 참이고 프리팹이 `IWeapon`을 구현해야 한다.
6. 아이콘은 정의의 `iconFile`·`iconRotation`을 채운 뒤 **Tools > Wizard Duel > Bake missing weapon HUD icons**로 `Assets/Game/UI/Art/WeaponIcons/`에 굽는다. 이 도구가 정의의 `icon`도 연결한다. **Validate weapon HUD assets**로 정의가 빠짐없는지 확인한다.
7. 효과음이 필요하면 클립과 라이선스를 `Assets/Audio/Clips/<무기 폴더 이름>/`에 둔다(예: `LightningStaff`, `LightOrb`, `ExplosionMagicBook`, `FireCenser`). 공용 소리는 `Assets/Audio/Clips/<용도>/`에 둔다. `Assets/Audio/Scripts/SfxId.cs` 끝에 기존 번호를 바꾸지 않고 새 id를 추가하고, `Assets/Audio/Resources/SfxLibrary.asset`에 해당 id의 클립·볼륨·속도·음 높이 항목을 하나 연결한다. 무기의 `Present`나 후속 연출에서 `Sfx.Play`로 재생해 쏜 무기의 소리가 나게 한다. **Tools > Wizard Duel > 효과음 패널**에서 들어 보고 `SfxLibraryTests.EverySfxId_HasOneEntryWithAClip`으로 누락을 확인한다.
8. 가벼운 테스트를 돌린다. `WeaponIdentityTests`가 id 중복과 정의↔프리팹 연결을 검사한다. 스폰 무기를 늘리면 `WeaponSpawnTests.Catalog_HasFourSpawnableWeapons_AndExcludesBasicWand`의 4종 기대값과 `Round_EnforcesTimerOccupancyDuplicateRemovalCapAndReset`의 `{ 2, 3, 4, 5 }`처럼 스폰 id 집합을 고정한 테스트도 함께 갱신한다. 플레이에서는 로비 선택·오프라인 장착·바닥 전시/줍기·HUD·공격과 소리를 확인한다.

## 무기별 메모

- **번개 지팡이**: 프리팹은 `Art/Models/FixedStaff/LightningStaff_Low.fbx`(`LightningStaff_Fixed.fbx`를 `Tools/WeaponMesh/decimate_weapon.py`로 줄인 것)와 `Staff_Original`·`Crystal_Cyan_Emission` 재질을 쓴다. 수정체는 얼룩진 원래 발광 텍스처 대신 균일한 발광색을 쓴다(`FixedStaff/README_KO.txt`). 번개 시각 효과는 [번개 이펙트](../Assets/Effects/Lightning/README.md)를 쓴다.
- **빛 반사 구슬**: `Assets/Game/Weapons/LightOrb/Editor/LightOrbGlbImporter.cs`가 `Art/OrbVisual/LightOrb.glb`를 메시·재질·광학 데이터 하위 에셋으로 가져온다. 이 임포터를 지우면 구슬 외형이 깨진다. 굴절은 렌더러 기능 `LightOrbRefractionFeature`로 그리며 `Assets/Settings/PC_Renderer.asset`에 들어 있다.
- **기본 완드**: 모델·손 메시 보정·자세는 [기본 완드 에셋](BasicWand/README_KO.md)에 있다.
