using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// BiomeDatabase � registre central de tous les biomes.
/// ScriptableObject � cr�er dans Assets/MinesweeperRPG/
/// et � assigner dans l'Inspector de GridManager.
///
/// Usage :
///   _biomeDatabase.Get(BiomeType.Forest)
///   _biomeDatabase.GetCellPrefab(BiomeType.Mountain)
/// </summary>
[CreateAssetMenu(fileName = "BiomeDatabase", menuName = "MinesweeperRPG/BiomeDatabase")]
public class BiomeDatabase : ScriptableObject
{
    [Header("=== Biomes enregistr�s ===")]
    public List<BiomeData> biomes = new();

    // =========================================================================
    // API
    // =========================================================================

    public BiomeData Get(BiomeType type)
    {
        foreach (var b in biomes)
            if (b != null && b.biomeType == type)
                return b;
        Debug.LogWarning("[BiomeDatabase] Biome non trouv�: " + type);
        return null;
    }

    public GameObject GetCellPrefab(BiomeType type)
    {
        return Get(type)?.cellPrefab;
    }

    public EnemySpawnTable GetEnemyTable(BiomeType type)
    {
        return Get(type)?.enemySpawnTable;
    }

    public Sprite GetGroundHidden(BiomeType type)
    {
        return Get(type)?.groundHidden;
    }

    public Sprite GetGroundRevealed(BiomeType type)
    {
        return Get(type)?.groundRevealed;
    }

    /// <summary>
    /// Retourne tous les prefabs de cases enregistrés.
    /// Utilisé par CellViewPool.Prewarm() au démarrage.
    /// </summary>
    public IEnumerable<GameObject> GetAllCellPrefabs()
    {
        foreach (var b in biomes)
            if (b?.cellPrefab != null) yield return b.cellPrefab;
    }
}