using UnityEngine;

/// <summary>
/// EquipmentData - ScriptableObject pour les equipements.
/// Herite de ItemData et ajoute les stats d'equipement.
/// Creer via Assets -> Create -> MinesweeperRPG -> EquipmentData
/// </summary>
[CreateAssetMenu(fileName = "Equip_", menuName = "MinesweeperRPG/EquipmentData")]
public class EquipmentData : ItemData
{
    [Header("=== Equipement ===")]
    [Tooltip("Slot ou cet equipement se place")]
    public EquipSlot slot;

    [Header("=== Stats ===")]
    [Tooltip("Degats infliges par attaque (arme)")]
    public int attackDamage = 0;

    [Tooltip("Reduction de degats recus (armure/casque)")]
    public int defense = 0;

    [Tooltip("Bonus PV maximum (casque/armure)")]
    public int bonusMaxHP = 0;

    [Tooltip("Multiplicateur XP en % (bague) ex: 10 = +10%")]
    public int xpBonusPercent = 0;

    [Tooltip("Bonus chance de loot en % (bague)")]
    public int lootBonusPercent = 0;

    [Header("=== Visuel Slot Vide ===")]
    [Tooltip("Sprite affiche quand ce slot est vide")]
    public Sprite emptySlotSprite;
}