using UnityEngine;

/// <summary>Move-speed passive that lives on the fire censer prefab.</summary>
[DisallowMultipleComponent]
public sealed class CenserMoveSpeedPassive : MonoBehaviour, IWeaponEquipPassive
{
    [SerializeField, Min(0.01f), Tooltip("Move speed multiplier while the censer is equipped. 1.1 = +10%.")]
    private float moveSpeedMultiplier = 1.1f;

    public void OnEquipped(PlayerCombatStats player)
    {
        if (player == null) return;
        player.SetMoveSpeedModifier(this, moveSpeedMultiplier);
    }

    public void OnUnequipped(PlayerCombatStats player)
    {
        if (player == null) return;
        player.RemoveMoveSpeedModifier(this);
    }
}
