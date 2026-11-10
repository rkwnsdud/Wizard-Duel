# Weapon spawn notification

Main's `ArenaWeaponSpawner.ReconcileVisuals` presents one notice when a new
server-confirmed drop ID is first materialized on a client. It uses the existing
reliable, validated snapshots; no client timer, prediction, or additional network
message is involved. Pickup/swap snapshots reuse existing IDs, so they do not
repeat the notice. A host's local reconciliation and its echoed snapshot share
the same visual dictionary. Dedicated servers never create this presentation.
Only actual successful spawns produce a notice; a full/blocked map does not.

`Assets/UI/WeaponSpawnNotification.cs` creates one reusable local overlay:

- Top centre, 440 x 58 at reference resolution 1920 x 1080, 38-unit top margin.
- Existing DoHyeon font, dark translucent background, restrained gold border.
- Fixed text: **마법 무기가 등장했습니다!** No weapon or location details.
- 0.12-second fade-in, 2-second hold, 0.45-second smooth fade-out (unscaled time).
- No raycast/input blocking, no event system or additional audio listener.
- The same 2D chime at every spawn, volume 0.22, low audio priority (180).
- Round end, disconnect and destruction hide the notice and stop its audio.

The font, sound and volume are serialized on
`Assets/Scripts/Battle/WeaponSpawnCatalog.asset`. Timing and layout are in the
notification component. Neither the scene nor the combat/spawn schedule needs
additional setup.

## Sound provenance and regeneration

`Assets/Audio/Notifications/WeaponSpawnChime.wav` is original additive synthesis:
soft sine resonances with a fifth and octave, gradual attack and a short echo
tail. No external samples or recordings were used. The sound and generator are
licensed under **0BSD**, allowing commercial use, modification and redistribution
without attribution. Full terms: `Assets/Audio/Notifications/LICENSE.txt`.

Format: mono 48 kHz, 16-bit PCM, 1.8 seconds. Measured peak 0.349976, RMS 0.068757;
first and last samples are zero. Default playback peak is approximately 0.077
before the listener/master gain. Unity import normalization is disabled.

Regenerate from the repository root with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Docs/WeaponSpawnNotification/GenerateChime.ps1
```

## Integration verification

`NoticeSmokeDriver.cs.txt` is a temporary runtime driver for an **isolated copy**
of the project, not a compiled game script. Copy it into that copy's Assets and
build the existing Main scene using `WeaponSpawnValidation.Build`. Launch a
dedicated server with `-server -spawnSmoke server -smokeDir <empty-directory>`
and two clients with `-join 127.0.0.1 -spawnSmoke a|b -smokeDir <same-directory>`
(one role per client). The processes exit automatically.
The driver sets port **17779** before the lobby starts, leaving a running game
server on the normal port 7777 untouched.

The scenario fixes the first type to Light Orb for reproducibility but preserves
both actual 30-second spawn intervals. It checks no pre-spawn notice, both
clients' exact message and playing audio, fade-in/hold/fade-out, no repeated
notice on pickup or Q exchange, a fresh notice at the next interval, and immediate
round-end cleanup. No test scene is generated or saved.

For interactive review, listen alongside combat audio and check the top-centre
banner at the intended resolution. Adjust `notificationVolume` in the catalog
if needed; the client listener/master volume still applies.
