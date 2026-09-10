using UnityEngine;

/// <summary>
/// LootTable — ScriptableObject de configuration du loot par zone.
///
/// Un LootTable par zone :
///   LootTable_Surface.asset    → forêt (gold, pommes, branches)
///   LootTable_Cave.asset       → grottes (cristaux, minerais, reliques)
///   LootTable_Indoor.asset     → intérieurs (recettes, nourriture)
///
/// Le LootSystem (Core) lit la table de la zone active.
/// Ajouter une nouvelle zone = créer un nouveau .asset, zéro code.
/// </summary>
[CreateAssetMenu(fileName = "LootTable", menuName = "MinesweeperRPG/Loot Table")]
public class LootTable : ScriptableObject
{
    [Header("Monnaie")]
    [Range(0f, 0.5f)] public float goldChance = 0.03f;
    [Range(1, 10)] public int goldMax = 3;

    [Header("Consommables")]
    [Range(0f, 0.3f)] public float food1Chance = 0.04f;
    public ItemID food1Item = ItemID.Pomme;

    [Header("Matériaux")]
    [Range(0f, 0.3f)] public float mat1Chance = 0.03f;
    public ItemID mat1Item = ItemID.Branche;
    [Range(1, 5)] public int mat1Max = 2;

    [Header("Items spéciaux (zone)")]
    [Range(0f, 0.1f)] public float specialChance = 0f;
    public ItemID specialItem = ItemID.None;

    // ── Méthode helper ───────────────────────────────────────────────────────

    /// <summary>Retourne true si la table a des items spéciaux configurés.</summary>
    public bool HasSpecialItems => specialChance > 0f && specialItem != ItemID.None;
}