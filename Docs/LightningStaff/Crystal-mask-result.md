# Crystal mask repair result

## Final application — 2026-10-23 10:54 KST

Completed the remaining application in Unity 6000.3.24f1. Compilation succeeded. Existing CrystalFilled output and original adjusted map hashes still match the audit; neither was regenerated or edited.

- Saved `LightningStaff_URP.mat` with Emission Map GUID `bcf5f4dd243f3244ba84b8d98c777744`.
- Actual PlayerTest Player/Wizard/.../WeaponSocket/LightningStaff/Model uses that material. Both edit-mode and runtime inspection confirm URP/Lit, local `_EMISSION=True`, `BakedEmissive`, HDR (8,8,8) and the CrystalFilled map. See `Final-renderer.txt` and `Final-runtime.txt`.
- Final same-condition preview: `Final-applied/Idle-crystal.png` and `Final-applied/Diagnostic-emission-only.png`. Broad upper dark patch reduced; silver rings did not acquire emission. Some dark facets/borders remain intentionally preserved.
- Actual PlayerTest play-mode entry and game-camera render succeeded: `Final-PlayerTest-game-camera.png`. Crystal is visibly blue/luminous but small at gameplay camera distance; detailed blotch assessment relies on the close-up. This is a runtime camera render, not a screenshot of the editor Game-view UI. No attack test was performed.
- Play mode was exited after capture. No scene save, geometry/UV, prefab, FirePoint, gameplay, texture, HDR or Bloom changes were made.
- Unity AI `NoSubscription` errors persist independently; they did not block this compilation/play capture.

Changes in this application step: material map reference; existing editor verification utility (actual-renderer inspection and real PlayerTest play capture without synthetic scene replacement); report and capture files. Earlier interruption history below is retained for traceability and no longer describes the final application status.

## Verified

- Source emission unchanged: SHA256 `30b8aec512f0834067f9c8e7bb0e78c7a5f461a50486dd9aad38d2d7b50a7c39`.
- Separate output: `Assets/Weapons/Lightning Weapon/LightningStaff/NewStaff/LightningStaff_Emission_CrystalFilled.png` (imported GUID `bcf5f4dd243f3244ba84b8d98c777744`).
- Read-only mesh UV extraction, albedo comparison and spatial checks validated UV islands 295, 325 and 166. See `UV-crystal-audit.png`.
- Changed 28,723 originally black texels. No existing nonblack emission texel, overlapping UV texel or texel outside the audited islands was changed. Kept an 8-pixel UV border and excluded non-blue albedo. See `Crystal-pixel-audit.json` and `Crystal-changed-pixels.png`.
- Same prefab pose, camera, lights, HDR (8,8,8), and volume profile used for before/after previews. New map assigned only to temporary cloned material for comparison.
- `Mask-before/Idle-crystal.png` vs `Mask-after/Idle-crystal.png`: broad dark region on upper crystal facet reduced. Some central dark facets and boundaries remain deliberately unmodified.
- `Mask-before/Diagnostic-emission-only.png` vs `Mask-after/Diagnostic-emission-only.png`: added emission confined to crystal in this view; silver rings remain dark, apart from surrounding bloom spill. No added silver emission observed.
- These are Unity editor preview renders, NOT PlayerTest play-mode or attack verification.

## Not applied / interruption

After comparison, the Unity editor process 50864 exited before the material application request was consumed. Editor.log records `Shutdown request received from client editor-50864-gdknjk`, Input System shutdown, and Package Manager shutdown. No claim is made about why the user/editor initiated shutdown. Agent did not terminate Unity.

`LightningStaff_URP.mat` still points to the original adjusted map GUID `1c58335ea2246f744a928827e34e0f6f`. The apply operation and final render did not run. Pending request was removed to prevent deferred execution on next startup.

No source texture, mesh, UV, prefab, FirePoint, gameplay logic, scene or Bloom changes were made. Modified editor verification helper supports read-only UV export, cloned-material comparison and a scoped apply command; the latter has not executed.

Remaining: with Unity running, apply the already-verified output to the existing material and capture a final renderer preview. Do not repeat UV analysis or regenerate the output.
