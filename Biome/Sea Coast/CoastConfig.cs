using UnityEngine;

/// <summary>
/// CoastConfig -- ScriptableObject de configuration d'une cote.
/// Creer : Assets > Create > MinesweeperRPG > Coast Config
/// </summary>
[CreateAssetMenu(fileName = "CoastConfig", menuName = "MinesweeperRPG/Coast Config")]
public class CoastConfig : ScriptableObject
{
    [Header("Placement")]

    [Tooltip("Cote du bord de grille (S = bas, E = droite, W = gauche).\n" +
             "RandomWE = probabilite 50/50 entre W et E.")]
    public CoastSide side = CoastSide.South;

    [Tooltip("Position de depart le long du bord (en cases).\n" +
             "Pour S : X de debut. Pour E/W : Y de debut.")]
    [Min(0)]
    public int startPos = 0;

    [Tooltip("Longueur de la cote le long du bord (en cases).\n" +
             "0 = toute la longueur du bord.")]
    [Min(0)]
    public int length = 0;

    [Header("Profondeur (epaisseur eau)")]

    [Tooltip("Epaisseur minimum de la mer (en cases depuis le bord)")]
    [Range(1, 8)]
    public int minDepth = 2;

    [Tooltip("Epaisseur maximum de la mer")]
    [Range(1, 12)]
    public int maxDepth = 4;

    [Tooltip("Profondeur de depart (0 = random entre min et max)")]
    [Min(0)]
    public int startDepth = 0;

    [Header("Forme")]

    [Tooltip("Chance d'expand (+1 profondeur) a chaque step")]
    [Range(0f, 0.5f)]
    public float expandChance = 0.15f;

    [Tooltip("Chance de retract (-1 profondeur) a chaque step")]
    [Range(0f, 0.5f)]
    public float retractChance = 0.15f;

    [Tooltip("Nombre minimum d'Edge consecutifs entre deux transitions")]
    [Range(1, 10)]
    public int minRunLength = 2;

    [Tooltip("Seed fixe (0 = aleatoire a chaque generation)")]
    public int seed = 0;

    [Header("Grille de generation")]

    [Tooltip("Largeur de la grille utilisee pour le layout cotier.\n" +
             "0 = utiliser la grille du jeu (GridManager). " +
             "Mettre a 20 pour un profil identique au test generator.")]
    [Min(0)]
    public int coastGridWidth = 0;

    [Tooltip("Hauteur de la grille utilisee pour le layout cotier.\n" +
             "0 = utiliser la grille du jeu (GridManager). " +
             "Mettre a 20 pour un profil identique au test generator.")]
    [Min(0)]
    public int coastGridHeight = 0;
}