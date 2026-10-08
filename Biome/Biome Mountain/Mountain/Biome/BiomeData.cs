using UnityEngine;

/// <summary>
/// Données d'un biome — sprites, prefabs, ennemis.
/// Un ScriptableObject par biome, référencé dans BiomeDatabase.
/// </summary>
[CreateAssetMenu(fileName = "BiomeData", menuName = "MinesweeperRPG/BiomeData")]
public class BiomeData : ScriptableObject
{
    [Header("=== Identité ===")]
    public BiomeType biomeType;
    public string biomeName;
    public Color biomeColor = Color.white;

    [Header("=== Cell Prefab ===")]
    [Tooltip("Prefab de case pour ce biome")]
    public GameObject cellPrefab;

    [Header("=== Sprites Sol ===")]
    public Sprite groundHidden;   // case cachée
    public Sprite groundRevealed; // case révélée (vide)

    [Header("=== Table d'ennemis ===")]
    public EnemySpawnTable enemySpawnTable;

    [Header("=== Musique / Ambiance ===")]
    public AudioClip ambientMusic;
}