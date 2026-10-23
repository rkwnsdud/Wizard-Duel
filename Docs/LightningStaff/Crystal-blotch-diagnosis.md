# Crystal blotch diagnosis — 2026-10-23

Status: diagnosis verified in Unity editor preview; texture repair NOT applied.

## Evidence

- `Crystal-blotch-before.png`: actual prefab/material close-up in an isolated Unity preview scene.
- `Diagnostic-white-base.png`: temporary cloned material with normal keyword disabled and white base texture. Broad dark facets remain.
- `Diagnostic-emission-only.png`: same clone with black base, metallic disabled and specular highlights disabled. Missing emission coincides with broad dark crystal facets. Main cause is incomplete emission coverage, not solely base color or normal lighting.
- `Diagnostic-no-normal.png`: blank render; INVALID diagnostic, do not use as evidence.
- Preview uses existing PlayerTest volume profile; no volume edits.
- No actual PlayerTest play-mode or attack verification was performed.

## Changes

- Extended existing `Assets/Editor/LightningStaffVerification.cs` with temporary cloned-material diagnostic captures. Source material is restored in finally; preview scene is closed.
- Existing Idle/Walk/Cast preview captures and poses report were regenerated.
- Saved a separate baseline and diagnostic captures.
- Built-in image editing produced `Emission-candidate-REJECTED.png`. Rejected because UV island boundaries and existing lightning details changed. Not imported into Assets or assigned to a material.
- No changes to source textures, material, HDR (8,8,8), Bloom, mesh, UV, prefab, FirePoint, gameplay references or animations.

## Remaining

Repair missing crystal emission with a deterministic pixel/UV-constrained mask, preserving existing lit texels and excluding metal; save a separate texture and validate with identical close-up renders before assigning. Generative editing did not meet exact UV preservation constraints.

## Image editing request

Built-in image tool; input 1 adjusted emission atlas, input 2 registered albedo reference. Requested filling only missing black patches within cyan crystal islands with subdued surrounding blue, preserving exact UV positions, boundaries and existing bright lightning details; all metal, blue metal grooves and shaft black, no halos or highlight increase. Output failed these constraints and was rejected.
