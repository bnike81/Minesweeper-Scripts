using UnityEngine;

/// <summary>
/// CaveSpriteSet — Sprites de la grotte organisés par type.
///
/// Créer : Assets → Create → MinesweeperRPG → Cave Sprite Set
/// Assigner chaque sprite dans l'Inspector.
///
/// CONVENTION DE NOMMAGE (bitmask cardinal) :
///   N=1, E=2, S=4, W=8 (bits indiquant où il y a un MUR)
///   Ex: edgeN = mur au nord → bord haut du sol
///       cornerNE = mur au nord ET est → coin extérieur haut-droite
///       innerNE = sol partout sauf diagonale NE → coin intérieur
/// </summary>
[CreateAssetMenu(fileName = "CaveSpriteSet", menuName = "MinesweeperRPG/Cave Sprite Set")]
public class CaveSpriteSet : ScriptableObject
{
    [Header("═══ Case cachée (non révélée) ═══")]
    public Sprite hiddenRock;

    [Header("═══ Sol (centre, pas de mur adjacent) ═══")]
    [Tooltip("Sol par défaut (GroundVariantSystem gère les variantes)")]
    public Sprite floor;

    [Header("═══ Bords (mur sur 1 côté) ═══")]
    public Sprite edgeN;   // mur au nord
    public Sprite edgeE;   // mur à l'est
    public Sprite edgeS;   // mur au sud
    public Sprite edgeW;   // mur à l'ouest

    [Header("═══ Coins extérieurs (mur sur 2 côtés adjacents) ═══")]
    public Sprite cornerNE; // mur nord + est
    public Sprite cornerNW; // mur nord + ouest
    public Sprite cornerSE; // mur sud + est
    public Sprite cornerSW; // mur sud + ouest

    [Header("═══ Coins intérieurs (sol partout sauf 1 diagonale) ═══")]
    [Tooltip("Sol sur les 4 côtés cardinaux, mur en diagonale uniquement")]
    public Sprite innerNE;  // mur diagonale nord-est
    public Sprite innerNW;  // mur diagonale nord-ouest
    public Sprite innerSE;  // mur diagonale sud-est
    public Sprite innerSW;  // mur diagonale sud-ouest

    [Header("═══ Angles 45° ═══")]
    [Tooltip("Transition en diagonale entre sol et mur")]
    public Sprite angle45_NE;
    public Sprite angle45_NW;
    public Sprite angle45_SE;
    public Sprite angle45_SW;

    [Header("═══ Couloirs (mur sur 2 côtés opposés) ═══")]
    public Sprite corridorH;  // murs nord + sud → couloir horizontal
    public Sprite corridorV;  // murs est + ouest → couloir vertical

    [Header("═══ Impasses (mur sur 3 côtés) ═══")]
    public Sprite deadEndN;   // ouvert au nord seulement
    public Sprite deadEndE;
    public Sprite deadEndS;
    public Sprite deadEndW;

    [Header("═══ Mur plein ═══")]
    public Sprite wallFull;   // entouré de murs (roche solide)

    [Header("═══ Chiffres adjacents (mêmes sprites que MainGrid) ═══")]
    [Tooltip("Glisser les mêmes sprites 1-8 que dans le prefab CellView de MainGrid")]
    public Sprite number1;
    public Sprite number2;
    public Sprite number3;
    public Sprite number4;
    public Sprite number5;
    public Sprite number6;
    public Sprite number7;
    public Sprite number8;

    [Header("═══ Drapeau ═══")]
    public Sprite flag;

    /// <summary>Retourne le sprite du nombre (1-8).</summary>
    public Sprite GetNumberSprite(int n) => n switch
    {
        1 => number1,
        2 => number2,
        3 => number3,
        4 => number4,
        5 => number5,
        6 => number6,
        7 => number7,
        8 => number8,
        _ => null
    };

    [Header("═══ Portails ═══")]
    public Sprite portalEntry;
    public Sprite portalExit;
}