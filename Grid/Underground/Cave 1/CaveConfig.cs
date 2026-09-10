using UnityEngine;

/// <summary>
/// CaveConfig — Paramètres de génération de la grotte (ScriptableObject).
///
/// Créer : Assets → Create → MinesweeperRPG → Cave Config
/// Assigner dans CaveGenerator Inspector.
/// </summary>
[CreateAssetMenu(fileName = "CaveConfig", menuName = "MinesweeperRPG/Cave Config")]
public class CaveConfig : ScriptableObject
{
    [Header("═══ Dimensions ═══")]
    [Tooltip("Largeur de la grotte (identique à MainGrid.Width)")]
    public int width = 16;
    [Tooltip("Hauteur de la grotte (3 à 4 niveaux de 16)")]
    [Range(32, 80)] public int height = 48;

    [Header("═══ Couloirs ═══")]
    [Tooltip("Épaisseur minimale des couloirs (en cases)")]
    [Range(2, 4)] public int corridorMinWidth = 2;
    [Tooltip("Épaisseur maximale des couloirs")]
    [Range(2, 4)] public int corridorMaxWidth = 3;

    [Header("═══ Salles ═══")]
    [Tooltip("Nombre de petites salles (4-6 cases)")]
    [Range(1, 6)] public int smallRoomCount = 3;
    [Tooltip("Taille min d'une petite salle")]
    [Range(3, 6)] public int smallRoomMin = 4;
    [Tooltip("Taille max d'une petite salle")]
    [Range(4, 8)] public int smallRoomMax = 6;

    [Header("Grande salle (village goblin)")]
    [Tooltip("Toujours présente au centre de la grotte")]
    public bool alwaysLargeRoom = true;
    [Tooltip("Largeur de la grande salle")]
    [Range(6, 10)] public int largeRoomWidth = 8;
    [Tooltip("Hauteur de la grande salle")]
    [Range(6, 10)] public int largeRoomHeight = 6;

    [Header("═══ Impasses ═══")]
    [Tooltip("Nombre d'impasses pour cacher du loot")]
    [Range(0, 6)] public int deadEndCount = 3;
    [Tooltip("Longueur d'une impasse (en cases)")]
    [Range(3, 8)] public int deadEndLength = 5;

    [Header("═══ Portails (entrée/sortie) ═══")]
    [Tooltip("Largeur du portail (en cases)")]
    [Range(1, 4)] public int portalWidth = 1;

    [Header("═══ Dangers (minesweeper) ═══")]
    [Tooltip("Ratio de cases dangereuses (ennemis + pièges)")]
    [Range(0.05f, 0.30f)] public float dangerRatio = 0.12f;
    [Tooltip("Ratio de pièges parmi les dangers")]
    [Range(0f, 0.2f)] public float trapRatio = 0.03f;
    [Tooltip("SpawnTable spécifique grotte (Spider, Bat, Goblins)")]
    public EnemySpawnTable caveSpawnTable;

    [Header("═══ Seed ═══")]
    [Tooltip("Seed pour la génération (-1 = aléatoire)")]
    public int seed = -1;
}