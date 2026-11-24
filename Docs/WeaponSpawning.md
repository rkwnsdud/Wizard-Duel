# Weapon spawning in Main

`DuelArena` starts the server scheduler for each match when combat is enabled
(after the player summon animation). This happens in online matches on the
dedicated server and in AI duels on the local host. The first weapon appears
10 seconds later, then one per 10 seconds (`WeaponSpawnRound.Interval`). Each
match allows at most two uncollected weapons on the ground
(`WeaponSpawnRound.MaxDrops`). An interval is skipped at that limit or if every
site is occupied or obstructed; after a pickup, spawning can resume at the next
scheduled interval. The server never emits a catch-up burst. Ending combat,
clearing the arena, or closing the connection stops the scheduler and removes
all pickup displays.

## Configuration and placement

`Assets/Game/Battle/WeaponSpawnCatalog.asset` lists the weapon definitions
(`Assets/Game/Weapons/Definitions/`). Each definition holds the stable network
ID, the weapon prefab and the pickup display rotation, size and spin. A weapon
can drop only if its definition has `canSpawn` on and its prefab implements
`IWeapon` (`WeaponSpawnCatalog.IsSpawnable`). Basic Wand has `canSpawn` off, so
Lightning Staff, Light Orb, Explosion Magic Book and Fire Censer are eligible.
Both Main players start with Basic Wand through the shared DuelWizard-1 prefab
(DuelWizard-2 inherits it). Pickup display sizes (largest dimension) are set on
the definitions: staff 2.8125m, orb 0.975m, book 1.125m and censer 1.575m.
Tall pickups float higher when necessary to keep their bottom clear of the floor
during bobbing.

The real arena is procedurally generated, so fixed scene coordinates would be
unsafe across seeds. `WeaponSpawnPlacement` analyses the generated layout on the
server: both players must be able to reach each site, the centre must be open
floor, cardinal neighbours must be walkable, and player starting zones are
excluded with a three-cell margin. Teleporter tiles and the one-cell ring
around them are excluded too ([Teleporters.md](Teleporters.md)). Up to 12 sites are spread at least four metres
apart, with walking-distance balance considered. Physics checks then confirm
the actual floor height and reject obstacles (`ArenaWeaponSpawner.FindSites`).
At spawn time, sites within two metres of a player are also skipped. The server
saves each match's sites and layout in `DuelMatch` before removing its temporary
map, so the dedicated server can keep spawning after the map is gone. The saved
layout also lets the server reject pickup requests through walls. Clients
receive the server's positions.

Displays (`FloatingWeaponPickup`) instantiate the actual prefab, disable
combat/collision/audio/extra effects on that copy, centre its renderer bounds
and fit its configured size. The original assets are untouched. The display
centre floats 1.2 metres above the floor, bobs by 0.08 metres over four seconds
and rotates at the definition's spin speed (30 degrees/second). Components that
implement `IWeaponDisplayAnimation`, such as Light Orb's visual spin, stay
enabled.

## Authority and inventory

`ArenaWeaponSpawner` sends reliable named messages over the Main connection
(`WizardDuel.WeaponSnapshot.v1`, `WizardDuel.WeaponRequest.v1`). Its server half
(`WeaponSpawnServer`) owns separate drop IDs, occupied sites, inventory slots,
and timers for each room; snapshots go only to that room's two clients. Its
client half (`WeaponSpawnClient`) applies snapshots and shows pickups. E
(Interact) picks up and Q (SwapWeapon) swaps; clients only send requests and do
not grant themselves weapons. The server validates sender, round, player state,
distance (two metres horizontally, two metres vertically), line of sight,
duplicate weapon type, and request sequence before changing inventory and
broadcasting a snapshot. Simultaneous pickup requests can grant a drop only
once. A full inventory keeps the stored weapon and replaces the equipped weapon.

Snapshots replicate equipped and stored weapons to both clients; only the local
player creates a HUD. `StoredWeaponChanged` updates its icon/name when needed.
Weapon instances are cached per player and weapon ID, so a swapped-out weapon
stays available (disabled) for attacks that were fired before the swap. New
attacks wait for acknowledgement of an inventory request
(`PlayerAttack.InventoryChangePending`); casts already in progress prevent
equipment requests.

New-drop notifications are described in
[WeaponSpawnNotification/README.md](WeaponSpawnNotification/README.md).

## Verification

`Assets/Tests/Editor/Battle/WeaponSpawnTests.cs` runs in the light test suite
(**Tools > Tests > 가벼운 테스트 실행**). It checks the catalog (four spawnable
weapons, Basic Wand excluded, notification font and chime), placement over every
shape and legal density, the round timer, occupancy, duplicate removal, the drop
cap and reset, and that two matches keep their own weapon sites after the server
removes their temporary maps.

For manual checks, use the Network Test window
([Assets/Editor/Multiplayer/README.md](../Assets/Editor/Multiplayer/README.md))
to run a dedicated server and two Main clients: start a round, wait 10 seconds,
approach a weapon and press E, then Q. Confirm the other client's world pickup
disappears and the equipped weapon changes, while each HUD shows only its own
stored weapon. Try simultaneous E, a duplicate weapon, a full inventory, and
ending a round with pickups still on the floor. The server log prints
`[WeaponSpawn] Round …` and `[WeaponSpawn] Round … spawn id=…` lines.
