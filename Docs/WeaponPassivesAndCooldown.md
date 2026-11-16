# Weapon passives and attack cooldowns

This guide describes the current weapon passive and cooldown hooks so weapon, combat, and UI work can be developed against the same contracts.

## Current boundaries

- A weapon is a `WeaponBase` component on a weapon prefab. The player's `PlayerAttack` references the equipped component.
- A weapon may provide at most one equipped passive and at most one hit passive. Either slot can be empty; one weapon may use both.
- Equipped passives are applied and removed by the player root's `PlayerWeaponPassiveController` when the weapon changes.
- Hit passives run on the server only after `ServerHitReport` accepts a hit report (see [ServerHitReportRules.md](ServerHitReportRules.md)). The callback is therefore deduplicated with the `(attacker slot, attack id, hit index)` check, and is not called for rejected, duplicate, self, post-defeat or post-round hits.
- The hit passive is chosen from the weapon that started the attack (`AttackInfo.WeaponId`), not from the weapon the attacker holds now. Switching weapons after firing does not change which passive applies.
- `PlayerCombatStats` owns attack-speed and move-speed modifiers. Each kind multiplies together (floor `0.01`), and attack cooldown is calculated as `weapon base cooldown / attack speed multiplier`.
- Current passives: the Fire Censer's `CenserMoveSpeedPassive` (equipped, move speed) and `CenserFlameHitPassive` (hit, applies Burn). Timed debuffs such as Burn and Slow are stored and replicated by `PlayerStatusEffects`; see [MoveSpeedAndStatusEffects.md](MoveSpeedAndStatusEffects.md). No attack-speed passive exists yet.

## Relevant files and attachment points

| File | Responsibility / location |
| --- | --- |
| `Assets/Game/Weapons/Common/Scripts/WeaponBase.cs` | Shared weapon component and per-weapon `AttackCooldown` value. Set the value on each weapon prefab. |
| `Assets/Game/Weapons/Common/Scripts/IWeaponEquipPassive.cs` | Contract for effects active while a weapon is equipped. |
| `Assets/Game/Weapons/Common/Scripts/IWeaponHitPassive.cs` | Contract for effects triggered by a server-approved hit. |
| `Assets/Game/Player/Scripts/PlayerCombatStats.cs` | Player-root stats and attack-/move-speed modifier collection. |
| `Assets/Game/Player/Scripts/PlayerWeaponPassiveController.cs` | Player-root manager that applies/removes equipped passives and dispatches accepted hits (`NotifyServerHit`). |
| `Assets/Game/Player/Scripts/PlayerAttack.cs` | Owns local attack input, cooldown timing, and weapon changes. |
| `Assets/Game/Battle/Scripts/DuelPlayer.cs` | Network attack replay and hit-report RPC; calls `ServerHitReport` and then the hit-passive callback. |
| `Assets/Game/UI/Scripts/PlayerAttackCooldownBar.cs` | UI line that displays remaining cooldown through a horizontal `Image.fillAmount`. |
| `Assets/Game/Player/Prefabs/DuelWizard-1.prefab` | Combat player prefab with `PlayerCombatStats`, `PlayerWeaponPassiveController` and `PlayerAttackCooldownBar` attached. `DuelWizard-2` is its variant and inherits them. |

Passive components belong on the weapon prefab, on the same GameObject as `WeaponBase` or one of its children. Do not put passive implementations on the player prefab: the weapon owns the effect definition, while the player manager owns its lifetime.

## Equipped passive implementation

Implement `IWeaponEquipPassive` on a `MonoBehaviour` attached to the weapon prefab. Use the component instance as the modifier key so removing or replacing that passive removes only its own modifier.

```csharp
using UnityEngine;

public sealed class AttackSpeedPassive : MonoBehaviour, IWeaponEquipPassive
{
    [SerializeField, Min(0.01f)] private float attackSpeedMultiplier = 1.2f;

    public void OnEquipped(PlayerCombatStats player)
    {
        player.SetAttackSpeedModifier(this, attackSpeedMultiplier);
    }

    public void OnUnequipped(PlayerCombatStats player)
    {
        player.RemoveAttackSpeedModifier(this);
    }
}
```

`1.2` means attacks can start 20% faster: the base cooldown is divided by the modifier. The concrete value is a weapon-balance choice and is not set by this framework.

The manager calls `OnUnequipped` before disabling the previous weapon and `OnEquipped` after selecting the replacement. The same calls are used for `TryEquipWeapon(null)` and later re-equipping. Keep `OnUnequipped` safe to call during player teardown.

## Hit passive implementation and online rules

Implement `IWeaponHitPassive` on a component on the weapon prefab. `OnServerHit(attacker, target)` runs from the server's accepted-hit path. It is not called for rejected, duplicate, self, or post-defeat hits.

Keep gameplay state changes server-owned. Apply timed debuffs through the target's `PlayerStatusEffects`, which stores them on the server and synchronizes them to clients. Reapplying the same effect refreshes its duration instead of adding another stack.

The current combat model still accepts the attacker's reported target and damage after basic validation; it does not retrace line-of-sight or recompute damage on the server. A hit passive shares that trust boundary. Server-side hit revalidation remains a separate combat security change.

## Cooldown and cooldown UI

Set `Attack Cooldown` on the weapon component in the weapon prefab inspector. The current default is `0.65` seconds to match the existing cast animation length. The cooldown starts when the attack animation starts. The animation still gates attacks until `EndAttack`; after that, any remaining cooldown blocks only another attack, not movement or aiming.

The cooldown duration is captured at attack start. Equipping a different weapon during a cooldown does not shorten or extend that in-progress cooldown; the next attack uses the newly equipped weapon's value and current attack-speed modifiers. A zero cooldown disables the timer gate, while the attack animation still prevents overlap.

For the line below the world-space health bar (already set up on `DuelWizard-1`):

1. Create the fill `Image` as a child of the cooldown line's fixed-size background so the health bar's layout does not move as it shrinks.
2. Add `PlayerAttackCooldownBar` to the line/fill object and assign its `Image` reference. It searches parent objects for `PlayerAttack`; assign that reference manually if the UI is not parented under the player.
3. The script configures the image as a left-origin horizontal fill. It is hidden when ready and shrinks as remaining cooldown decreases.

## Online weapon switching

Pickups and swaps are server-approved and replicated as weapon ids, so every screen holds the same weapon instances (see [WeaponSpawning.md](WeaponSpawning.md)). The hit report carries the attack's weapon id; the server accepts it only for a weapon that player has held during the current round (`WeaponNotHeld` otherwise), then runs that weapon's hit passive.
