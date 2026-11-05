# Weapon passives and attack cooldowns

This guide describes the current weapon passive and cooldown hooks so weapon, combat, and UI work can be developed against the same contracts.

## Current boundaries

- A weapon is a `WeaponBase` component on a weapon prefab. The player's `PlayerAttack` references the equipped component.
- A weapon may provide at most one equipped passive and at most one hit passive. Either slot can be empty; one weapon may use both.
- Equipped passives are applied and removed by the player root's `PlayerWeaponPassiveController` when the weapon changes.
- Hit passives run on the server only after `DuelCombatState.TryTakeHit` accepts a hit. The callback is therefore deduplicated with the existing `(attacker slot, attack id, hit index)` check.
- `PlayerCombatStats` owns attack-speed modifiers. They multiply together, and attack cooldown is calculated as `weapon base cooldown / attack speed multiplier`.
- No concrete attack-speed passive or timed debuff is included yet. A hit-passive implementation must manage and replicate its concrete status effect. Reapplying the same timed debuff should refresh its duration rather than stack another copy.

## Relevant files and attachment points

| File | Responsibility / location |
| --- | --- |
| `Assets/Weapons/Common/WeaponBase.cs` | Shared weapon component and per-weapon `AttackCooldown` value. Set the value on each weapon prefab. |
| `Assets/Weapons/Common/IWeaponEquipPassive.cs` | Contract for effects active while a weapon is equipped. |
| `Assets/Weapons/Common/IWeaponHitPassive.cs` | Contract for effects triggered by a server-approved hit. |
| `Assets/Player/PlayerCombatStats.cs` | Player-root stats and attack-speed modifier collection. |
| `Assets/Player/PlayerWeaponPassiveController.cs` | Player-root manager that applies/removes equipped passives and dispatches accepted hits. |
| `Assets/Player/PlayerAttack.cs` | Owns local attack input, cooldown timing, and weapon changes. |
| `Assets/Scripts/Battle/DuelPlayer.cs` | Network attack replay and server hit acceptance; invokes the hit-passive callback. |
| `Assets/UI/PlayerAttackCooldownBar.cs` | UI line that displays remaining cooldown through a horizontal `Image.fillAmount`. |
| `Assets/Player/Player.prefab` | Base player prefab with `PlayerCombatStats` and `PlayerWeaponPassiveController` attached. Duel player variants inherit these components. |

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

Keep gameplay state changes server-owned. A timed debuff should be stored on the target's server-side status/effect controller and synchronized to clients for display and client-side behavior. If the same effect is already active, update its expiry time to `now + duration`; do not add another stack. The interface is only the hook: a reusable timed-effect manager and any particular debuff have not been implemented yet.

The current combat model still accepts the attacker's reported target and damage after basic validation; it does not retrace line-of-sight or recompute damage on the server. A hit passive shares that trust boundary. Server-side hit revalidation remains a separate combat security change.

## Cooldown and cooldown UI

Set `Attack Cooldown` on the weapon component in the weapon prefab inspector. The current default is `0.65` seconds to match the existing cast animation length. The cooldown starts when the attack animation starts. The animation still gates attacks until `EndAttack`; after that, any remaining cooldown blocks only another attack, not movement or aiming.

The cooldown duration is captured at attack start. Equipping a different weapon during a cooldown does not shorten or extend that in-progress cooldown; the next attack uses the newly equipped weapon's value and current attack-speed modifiers. A zero cooldown disables the timer gate, while the attack animation still prevents overlap.

For the line below a world-space health bar:

1. Create the fill `Image` as a child of the cooldown line's fixed-size background so the health bar's layout does not move as it shrinks.
2. Add `PlayerAttackCooldownBar` to the line/fill object and assign its `Image` reference. It searches parent objects for `PlayerAttack`; assign that reference manually if the UI is not parented under the player.
3. The script configures the image as a left-origin horizontal fill. It is hidden when ready and shrinks as remaining cooldown decreases.

The base project does not yet contain the character health-bar UI, so the cooldown component is not attached to a health-bar prefab or scene object yet.

## Network weapon switching limitation

`PlayerAttack.TryEquipWeapon` currently performs a local equipment change. It does not synchronize a selected weapon with the server or the other client. The server hit callback therefore sees the weapon currently equipped on its own player object, which is correct for the initial prefab weapon but is not sufficient for a future online pickup/switch system.

Before online switching is used with hit passives, add a server-validated, replicated equipped-weapon identity and make each client activate the matching weapon prefab. Do not trust a client-supplied passive or weapon identity directly in the hit RPC.
