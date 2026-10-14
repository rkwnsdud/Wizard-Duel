# Wizard migration / verification source

These two C# files are the one-off Unity automation used for this model replacement. They are kept outside `Assets` so the migration and temporary Play Mode probes are not part of the game. The completed prefabs/assets are already applied; do not run migration again to use them.

The Korean outcome report and saved evidence are in `Docs/PlayerModelMigration/README_KO.md`. Raw Unity logs remain locally in the ignored `Logs/WizardMigration/` directory.

To repeat verification, with Unity closed:

1. Copy the two `.cs` files into a temporary `Assets/WizardMigrationEditor/` folder. They are guarded by `UNITY_EDITOR`. The Play Mode helper MonoBehaviours intentionally compile in the editor's version of Assembly-CSharp.
2. Use Unity 6000.3.24f1 with this project. Run `-batchmode -quit -projectPath "<project>" -executeMethod WizardMigrationValidation.Validate -logFile "<project>/Logs/WizardMigration/verify.log"` for asset, reference, binding, event-payload, geometry and rendering checks.
3. Run `-batchmode -projectPath "<project>" -executeMethod WizardMigrationValidation.PlayValidate -logFile "<project>/Logs/WizardMigration/play.log"` for actual input/state/event/projectile checks. **Omit `-quit`**: the verifier exits Unity after leaving Play Mode. It uses a cloned InputSettings object and temporary synthetic devices. It records success in `play-pass.txt` and exits 0; check the log and report if it exits 1. If the original prefab snapshot is absent, the baseline PreviewVisual comparison is explicitly skipped.
4. `WizardMigration.RenderSequences` renders six back-view samples per supported motion. No source model, prefab or controller is edited by these verification methods.
5. Remove only the temporary helper directory and its `.meta` after Unity exits. Do not delete source assets or user input folders. Unity may reset dynamic TMP font atlases on startup; compare changes against a pre-run backup before cleaning unrelated changes.

Generation methods (`Prepare`, `Apply`, `RefineAndValidate`, `ProbeHumanoid`) are preserved for audit, not intended as an automatic startup task. `Prepare` needs the original Player skeleton and refuses an already migrated Player. `Apply` reuses existing assets and skips rebuilding an existing WizardModel. `RefineAndValidate` includes final lateral-foot height correction and red-material color adjustment. Full regeneration requires reviewing the original source prefab snapshot and final report first; do not replace a user's current prefab with a snapshot without checking their changes.

Humanoid probe mapping succeeded (`isValid=true`, `isHuman=true`). Production uses Generic to preserve explicit transform clips and the authored right-hand grip. Do not simply switch the FBX importer to Humanoid while leaving the generated generic clips/controller unchanged.
