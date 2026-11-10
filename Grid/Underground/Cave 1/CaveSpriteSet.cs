using UnityEngine;

/// <summary>
/// CaveSpriteSet — Sprites de la grotte organisés par type.
///
/// Créer : Assets → Create → MinesweeperRPG → Cave Sprite Set
/// Assigner chaque sprite dans l'Inspector.
///
/// CONVENTION DE NOMMAGE :
///   Le bitmask cardinal indique où il y a du SOL autour d'un mur.
///   Le sprite est nommé à L'OPPOSÉ du sol (direction du mur vu depuis le sol).
///   Ex: edgeN = sol au SUD du mur → ce mur est le bord NORD du sol
///       cornerNW = sol en diag SE → coin convexe (extérieur) nord-ouest
///       innerNW = sol au S+E → coin concave (intérieur) nord-ouest
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

    [Header("═══ Mur plein (fallback roche) ═══")]
    [Tooltip("Roche profonde — fallback si aucun edge/corner/inner ne correspond")]
    public Sprite wallFull;

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

    // =========================================================================
    // PORTAILS — Bord Bas Cave
    // =========================================================================

    [Header("═══ Portail entrée / sortie ═══")]
    [Tooltip(
        "Sprite 'Bord Bas Cave' (16×16) — même logique d'assemblage qu'un bord bas classique.\n" +
        "Utilisé pour L'ENTRÉE (bas de la grotte, y=0) ET LA SORTIE (haut, y=Height-1).\n" +
        "Ce sprite remplace entièrement les anciens portalEntry / portalExit.\n" +
        "Il se pose sur la case MUR du portail, exactement comme un bord bas ordinaire.")]
    public Sprite bordBasCave;

    // ── Helpers ───────────────────────────────────────────────────────────────

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
}