using UnityEngine;

/// <summary>
/// ItemData - ScriptableObject definissant un objet du jeu.
/// Creer via Assets -> Create -> MinesweeperRPG -> ItemData
/// </summary>
[CreateAssetMenu(fileName = "Item_", menuName = "MinesweeperRPG/ItemData")]
public class ItemData : ScriptableObject
{
    [Header("=== Identite ===")]
    public ItemID itemID;
    public string displayName;
    [TextArea(2, 4)]
    public string description;
    public ItemType itemType;
    public Rarity rarity = Rarity.Common;

    [Header("=== Visuel ===")]
    public Sprite icon;

    [Header("=== Pile ===")]
    [Tooltip("Peut s'accumuler en pile (pomme x3, branches x5...)")]
    public bool stackable = true;
    [Tooltip("Quantite max par slot")]
    public int maxStack = 99;

    [Header("=== Effets ===")]
    [Tooltip("PV restaures a l'utilisation (0 = aucun soin)")]
    public int healAmount = 0;

    [Tooltip("Valeur en pieces si vendu")]
    public int sellValue = 1;

    [Tooltip("Prix d'achat chez un marchand (0 = non achetable)")]
    public int buyValue = 0;
}