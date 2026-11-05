/// <summary>Provides a passive that is active only while its weapon is equipped.</summary>
public interface IWeaponEquipPassive
{
    void OnEquipped(PlayerCombatStats player);
    void OnUnequipped(PlayerCombatStats player);
}
