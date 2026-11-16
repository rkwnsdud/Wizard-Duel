# Weapon spawn notification

Main's `WeaponSpawnClient` (the client half of `ArenaWeaponSpawner`) presents one
notice when a new server-confirmed drop ID is first materialized on a client
(`ReconcileVisuals`). It uses the existing reliable, validated snapshots; no client
timer, prediction, or additional network message is involved. Pickup/swap
snapshots reuse existing IDs, so they do not repeat the notice. The AI duel's
local host uses the same client path. Dedicated servers are not clients, so they
never create this presentation. Only actual successful spawns produce a notice;
a full/blocked map does not.

`Assets/Game/UI/Scripts/WeaponSpawnNotification.cs` creates one reusable local overlay:

- Top centre, 440 x 58 at reference resolution 1920 x 1080, 38-unit top margin.
- DoHyeon font, dark translucent background, restrained gold border.
- Fixed text: **마법 무기가 등장했습니다!** No weapon or location details.
- 0.12-second fade-in, 2-second hold, 0.45-second smooth fade-out (unscaled time).
- No raycast/input blocking, no event system or additional audio listener.
- The same 2D chime at every spawn (`SfxId.WeaponSpawnChime`). A new notice
  restarts the chime instead of stacking it.
- Round end, disconnect and destruction hide the notice and stop its audio.

The font is serialized on `Assets/Game/Battle/WeaponSpawnCatalog.asset`
(`notificationFont`, `DoHyeon-Regular SDF`). The chime clip and its volume (0.25)
are entry `WeaponSpawnChime` in `Assets/Audio/Resources/SfxLibrary.asset`, edited in
**Tools > Wizard Duel > 효과음 패널**. Timing and layout are in the notification
component. Neither the scene nor the combat/spawn schedule needs additional setup.

## Sound provenance and regeneration

`Assets/Audio/Clips/Notifications/WeaponSpawnChime.wav` is original additive synthesis:
soft sine resonances with a fifth and octave, gradual attack and a short echo
tail. No external samples or recordings were used. The sound and generator are
licensed under **0BSD**, allowing commercial use, modification and redistribution
without attribution. Full terms: `Assets/Audio/Clips/Notifications/LICENSE.txt`.

Format: mono 48 kHz, 16-bit PCM, 1.8 seconds; first and last samples are zero.
The generator writes a peak of 0.35. When the sound effects were gathered into the
library, the file's level was matched to the other effects (gain only), so the
committed file now peaks at about 0.58 (RMS about 0.114). Unity import
normalization is disabled.

Regenerate from the repository root with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Docs/WeaponSpawnNotification/GenerateChime.ps1
```

A regenerated file is quieter than the committed one; adjust its level or the
library volume afterwards.

For interactive review, listen alongside combat audio and check the top-centre
banner at the intended resolution.
