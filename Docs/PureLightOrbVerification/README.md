# Pure Light Orb — retained evidence

Cleanup: 2026-10-28 (KST). These are historical Unity renders, not a guarantee that the current scene matches every image.

- `ThirdLayer20261027`: layered crystal appearance and shader report.
- `ForearmInwardRoll20261028`: hand pose with the 20-degree inward correction; the subsequent additional 7-degree correction is not pictured. Hand-detail images temporarily hide the orb and use an inspection light.
- `CoreAttackFlash20261028`: centre-only flash peak. The later 0.32-second timing and increased intensity are not verified by this still image.

Intermediate renders and the temporary capture script were removed. No historical report should be treated as a new play-mode validation.

## Required project files

- `Assets/Editor/LightOrbGlbImporter.cs`: imports the GLB into meshes, material and optical-data subassets. Required; do not remove.
- `Assets/Weapons/Light Orb/OrbVisual/`: model, visual prefab, crystal/metal shaders, spin and refraction feature. Required at runtime.
- Weapon component: `LightOrbWeapon`; projectile: `LightOrbProjectile`; display name: `Light Orb`. Original asset GUIDs and imported subasset identifiers are retained for reference compatibility. Historical recovery scenes and reports retain their original names.
- The temporary `PureLightRefractionInstall` menu was removed after confirming the renderer features are serialized in the renderer assets.
- `Assets/Settings/PC_Renderer.asset` and `Mobile_Renderer.asset`: already contain the refraction feature.
- `Assets/Scenes/PureLightOrbVerification.unity`: retained manual preview scene; not enabled in build settings.

Removed the old `PureLightOrbSetup` automatic scene rebuild/play-mode workflow and `PureLightOpticalCapture` temporary diagnostic workflow. Reopening Unity no longer invokes those workflows.
