using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// LootTable — ScriptableObject de configuration du loot par biome.
///
/// Un LootTable par biome :
///   LootTable_Forest.asset  → pomme, branche, pièce d'or, fleur rouge
///   LootTable_Cave.asset    → silex, cristal, champignon, pièce d'or
///   LootTable_Beach.asset   → noix de coco, fibre de palme, pièce d'or
///
/// Créer : Assets → Create → MinesweeperRPG → Loot Table
/// Ajouter un biome = créer un nouveau .asset + l'assigner dans LootSystem.
/// </summary>
[CreateAssetMenu(fileName = "LootTable", menuName = "MinesweeperRPG/Loot Table")]
public class LootTable : ScriptableObject
{
    [Tooltip("Liste des items pouvant apparaître au sol dans ce biome")]
    public List<LootEntry> entries = new();

    /// <summary>Retourne true si la table contient au moins un item.</summary>
    public bool HasEntries => entries != null && entries.Count > 0;
}

/// <summary>
/// Une entrée de loot : quel item, quelle chance, combien max par case.
/// </summary>
[System.Serializable]
public class LootEntry
{
    [Tooltip("L'item à faire apparaître")]
    public ItemID item;

    [Tooltip("Chance par case révélée (0 = jamais, 0.05 = 5%)")]
    [Range(0f, 0.5f)]
    public float chance = 0.03f;

    [Tooltip("Quantité max par case (1 = toujours 1, 3 = entre 1 et 3)")]
    [Range(1, 5)]
    public int maxPerCell = 1;
}