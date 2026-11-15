# Weapon spawning in Main

`DuelArena` starts the server scheduler for each match when combat is enabled
(after the existing player summon animation). The first weapon appears 10 seconds later, then one
per 10 seconds. Each match allows at most two uncollected weapons on the ground.
An interval is skipped at that limit or if every site is occupied or obstructed;
after a pickup, spawning can resume at the next scheduled interval.
the server never emits a catch-up burst. Ending combat, clearing the arena, or
closing the connection stops the scheduler and removes all pickup displays.

## Configuration and placement

`Assets/Scripts/Battle/WeaponSpawnCatalog.asset` maps stable network IDs to the
existing online weapon prefabs and per-weapon display rotation, size and spin.
Basic Wand is excluded both by configuration and by a runtime type check.
Lightning Staff, Light Orb, Explosion Magic Book and Fire Censer are eligible.
Both Main players start with Basic Wand through the shared DuelWizard-1 prefab
(DuelWizard-2 inherits it). Pickup display sizes are 1.5 times their initial
values: orb 0.975m, book 1.125m and censer 1.575m maximum dimension.
The staff is enlarged a further 1.5 times to 2.8125m for visibility. Tall pickups
float higher when necessary to keep their bottom clear of the floor during bobbing.
The secondary-slot HUD is also enlarged 1.5 times, preserving its bottom margin.

The real arena is procedurally generated, so fixed scene coordinates would be
unsafe across seeds. `WeaponSpawnPlacement` analyses the generated layout on the
server: both players must be able to reach each site, the centre must be open
floor, cardinal neighbours must be walkable, and player starting zones are
excluded with a three-cell margin. Up to 12 sites are spread at least four metres
apart, with walking-distance balance considered. Physics checks then confirm
the actual floor height and reject obstacles. At spawn time, sites within two
metres of a player are also skipped. The server saves each match's sites in
`DuelMatch` before removing its temporary map. The saved layout also lets the
server reject pickup requests through walls. Clients receive the server's positions.

Displays instantiate the actual prefab, disable combat/collision/audio/extra
effects on that copy, centre its renderer bounds and fit its configured size.
The original assets are untouched. The display centre floats 1.2 metres above
the floor, bobs by 0.08 metres over four seconds and rotates 30 degrees/second.
Light Orb's visual spin component remains enabled for its existing refraction.

## Authority and inventory

`ArenaWeaponSpawner` uses the existing Main connection's reliable named messages.
The server owns separate drop IDs, occupied sites, inventory slots, and timers
for each room. Snapshots go only to that room's two clients. E and Q send
requests; clients do not grant themselves weapons. The server validates sender,
round, player state, distance (two metres), line of sight, duplicate weapon type,
and request sequence before changing inventory and broadcasting a snapshot.
Simultaneous pickup requests can grant a drop only once. A full inventory keeps
the stored weapon and replaces the equipped weapon, matching existing behaviour.

Snapshots replicate equipped and stored weapons to both clients; only the local
player creates a HUD. `StoredWeaponChanged` updates its icon/name when needed.
Weapon instances are cached per player/type and swaps use existing equipment
APIs. New attacks wait for acknowledgement of an inventory request; casts already
in progress prevent local equipment requests. Offline pickup behaviour remains.

## Verification

`WeaponSpawnValidation.Validate` can be run through Unity's `-executeMethod` in
an isolated project copy. It tests 60 layouts (four shapes, five seeds and three
legal obstacle densities), placement safety/spacing, the four eligible weapons,
10-second timing, occupancy, duplicate removal, site reuse, cleanup and Main's
catalog reference. It also checks that two matches retain their own weapon sites
after the server removes their temporary maps. It opens Main without saving it.

For the current multi-room change, the runtime and editor C# projects compile
with zero errors. Unity execution validation was attempted in an isolated copy,
but licensing initialization failed before the validation method could run.
The new room persistence checks and a live server/two-client match still need
to run in a Unity session with a working license.

The original single-room implementation was verified with Unity 6000.3.24f1:
all 60 layouts passed (619 sites), Windows x64 Development build passed, and a
dedicated server plus two clients passed the integration scenario twice.
The scenario checks matching drop records, competing pickup requests (exactly
one winner), acknowledgement release, stored-weapon events and HUD text, Q swap
replication on both clients, one local HUD per client, and cleanup with a second
drop still present. The first drop's 30-second timer runs normally; its type is
fixed to Light Orb only in the temporary driver, and the second timer is advanced
to exercise cleanup. See `WeaponSpawnVerification/Results.txt` for evidence.

The temporary driver is retained as `.cs.txt` in that directory for reproduction:
copy it into **an isolated project copy's** Assets, build Main, then launch one
process with `-server -spawnSmoke server -smokeDir <empty-directory>` and two with
`-join 127.0.0.1 -spawnSmoke a|b -smokeDir <same-directory>` (choose one role per
client). The archived driver refers to the original single-round spawner and
requires updating for the current room-scoped server state. It is not compiled
into this project.
Hidden execution did not produce a screenshot despite the capture request;
visual scale, rotation and appearance still merit an interactive check.

For manual checks, run the existing dedicated server and two Main clients:
start a round, wait 10 seconds, approach a weapon and press E, then Q. Confirm
the other client's world pickup disappears and the equipped weapon changes,
while each HUD shows only its own stored weapon. Try simultaneous E, a duplicate
weapon, a full inventory, and ending a round with pickups still on the floor.

Known source asset issue: Fire Censer has missing material references on two
renderers. Its source is deliberately unchanged; it can appear magenta, and its
HUD entry currently has no generated icon. Repair the original material links
before judging that weapon's final presentation.
