using UnityEngine;

/// <summary>
/// BeachRecipe — Configuration des îlots plage.
///
/// Chaque entrée BeachIsland définit un îlot :
///   - chunk d'apparition
///   - côté d'ancrage (gauche/droite/centre)
///   - taille et forme (rayon Perlin, seuil)
///   - distance min aux montagnes et aux bords
///
/// Inspector :
///   Assets → Create → MinesweeperRPG → Beach Recipe
/// </summary>
[CreateAssetMenu(fileName = "BeachRecipe",
    menuName = "MinesweeperRPG/Beach Recipe")]
public class BeachRecipe : ScriptableObject
{
    [System.Serializable]
    public class BeachIsland
    {
        [Header("Position")]
        [Tooltip("Chunk (niveau) où l'îlot apparaît")]
        public int spawnAtLevel = 0;

        [Tooltip("Position horizontale : Left, Right, Center, Random")]
        public IslandAnchor anchor = IslandAnchor.Left;

        [Tooltip("Décalage Y dans le chunk (0 = bas, 0.5 = milieu, 1 = haut)")]
        [Range(0f, 1f)] public float yPositionInChunk = 0.3f;

        [Header("Forme")]
        [Tooltip("Rayon horizontal max de l'îlot (en cases)")]
        [Range(3, 16)] public int radiusX = 7;

        [Tooltip("Rayon vertical max de l'îlot (en cases) — 16=1 chunk, 48=3 chunks")]
        [Range(2, 60)] public int radiusY = 24;

        [Tooltip("Seuil Perlin pour considérer une case comme plage (0.3-0.5)")]
        [Range(0.2f, 0.6f)] public float perlinThreshold = 0.38f;

        [Tooltip("Échelle Perlin — plus petit = formes plus grandes et douces")]
        [Range(0.05f, 0.3f)] public float perlinScale = 0.12f;

        [Header("Contraintes")]
        [Tooltip("Distance min aux bords horizontaux de la grille")]
        [Range(0, 5)] public int marginFromEdge = 1;

        [Tooltip("Distance min aux montagnes")]
        [Range(2, 10)] public int marginFromMountain = 5;

        [Tooltip("L'îlot peut s'étaler sur les chunks voisins")]
        public bool canOverflowChunks = true;

        [Header("Variation")]
        [Tooltip("Variation aléatoire du centre X (-N à +N cases)")]
        [Range(0, 4)] public int centerXVariation = 2;

        [Tooltip("Variation aléatoire du centre Y (-N à +N cases)")]
        [Range(0, 3)] public int centerYVariation = 1;
    }

    public enum IslandAnchor
    {
        Left,      // ancré au bord gauche
        Right,     // ancré au bord droit
        Center,    // centré dans la grille
        Random     // position aléatoire
    }

    [Header("Îlots plage")]
    [SerializeField] private BeachIsland[] _islands;

    [Header("Global")]
    [Tooltip("Seed global (0 = aléatoire à chaque run)")]
    [SerializeField] private int _seed = 0;

    public BeachIsland[] Islands => _islands;
    public int Seed => _seed;
}