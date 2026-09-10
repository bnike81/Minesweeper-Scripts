using UnityEngine;

[CreateAssetMenu(fileName = "MountainRecipe",
                 menuName = "MinesweeperRPG/Mountain/Recipe")]
public class MountainRecipe : ScriptableObject
{
    [Header("=== Identité ===")]
    public string recipeName = "Montagne 1";

    // =========================================================================
    // BASE
    // =========================================================================

    [Header("=== Base ===")]
    [Tooltip("Nombre de paires de faces (largeur de la base)")]
    [Range(1, 3)] public int pairCount = 2;
    [Tooltip("Permettre une cassure entre les paires")]
    public bool allowBreaks = true;
    [Tooltip("Probabilité de cassure si permise")]
    [Range(0f, 1f)] public float breakChance = 0.7f;

    // =========================================================================
    // MID — Colonnes verticales
    // =========================================================================

    [Header("=== Mid — Colonnes ===")]
    [Tooltip("Hauteur totale des colonnes (en cases 16x16)")]
    [Range(9, 40)] public int midTotalHeight = 18;

    [Header("=== Épaisseur ===")]
    [Tooltip("Épaisseur minimale entre les deux colonnes (en cases)")]
    [Range(4, 12)] public int thicknessMin = 6;
    [Tooltip("Épaisseur maximale entre les deux colonnes (en cases)")]
    [Range(4, 14)] public int thicknessMax = 8;

    [Header("=== Positionnement Horizontal ===")]
    [Tooltip("Côté de référence : false = gauche, true = droite")]
    public bool referenceFromRight = false;
    [Tooltip("Position X du côté référence à la base (distance du bord grille)")]
    [Range(0, 14)] public int xAtBase = 3;
    [Tooltip("Position X au 1er tronçon (1/4 hauteur)")]
    [Range(0, 14)] public int xAtTier1 = 2;
    [Tooltip("Position X au 2ème tronçon (2/4 hauteur)")]
    [Range(0, 14)] public int xAtTier2 = 1;
    [Tooltip("Position X au 3ème tronçon (3/4 hauteur)")]
    [Range(0, 14)] public int xAtTier3 = 2;
    [Tooltip("Position X au top")]
    [Range(0, 14)] public int xAtTop = 3;

    /// <summary>
    /// Retourne le X cible du côté référence à une hauteur donnée (0.0 à 1.0).
    /// Interpole entre les 5 points de référence.
    /// </summary>
    public int GetTargetX(float heightRatio)
    {
        if (heightRatio <= 0f) return xAtBase;
        if (heightRatio <= 0.25f) return Mathf.RoundToInt(Mathf.Lerp(xAtBase, xAtTier1, heightRatio / 0.25f));
        if (heightRatio <= 0.5f) return Mathf.RoundToInt(Mathf.Lerp(xAtTier1, xAtTier2, (heightRatio - 0.25f) / 0.25f));
        if (heightRatio <= 0.75f) return Mathf.RoundToInt(Mathf.Lerp(xAtTier2, xAtTier3, (heightRatio - 0.5f) / 0.25f));
        if (heightRatio < 1f) return Mathf.RoundToInt(Mathf.Lerp(xAtTier3, xAtTop, (heightRatio - 0.75f) / 0.25f));
        return xAtTop;
    }

    [Header("=== Mid — Tiers 1 (bas) ===")]
    public bool tier1ExpandLeft = true;
    public bool tier1ExpandRight = true;
    public bool tier1RetractLeft = false;
    public bool tier1RetractRight = false;

    [Header("=== Mid — Tiers 2 (milieu) ===")]
    public bool tier2ExpandLeft = true;
    public bool tier2ExpandRight = true;
    public bool tier2RetractLeft = true;
    public bool tier2RetractRight = true;

    [Header("=== Mid — Tiers 3 (haut) ===")]
    public bool tier3ExpandLeft = false;
    public bool tier3ExpandRight = false;
    public bool tier3RetractLeft = true;
    public bool tier3RetractRight = true;

    // =========================================================================
    // EXTENSION FACE
    // =========================================================================

    [Header("=== Extension Face ===")]
    [Tooltip("Activer l'extension face")]
    public bool hasExtension = true;
    [Tooltip("false = droite, true = gauche")]
    public bool extensionOnLeft = false;
    [Tooltip("Tier de hauteur : 1 = bas, 2 = milieu, 3 = haut, 4 = top")]
    [Range(1, 4)] public int extensionTier = 3;
    [Tooltip("L'extension contient un portail cave")]
    public bool extensionHasCave = true;

    // =========================================================================
    // TOP
    // =========================================================================

    [Header("=== Top ===")]
    [Tooltip("Cassure possible dans le bord top")]
    public bool allowTopBreak = true;
    [Range(0f, 1f)] public float topBreakChance = 0.35f;

    // =========================================================================
    // SPAWN
    // =========================================================================

    [Header("=== Spawn ===")]
    public int spawnAtLevel = 4;
    [Range(0f, 1f)] public float spawnChance = 1f;

    // =========================================================================
    // HELPERS
    // =========================================================================

    public int GetTier(int built, int startY)
    {
        int h = midTotalHeight / 3;
        if (built < h) return 0;
        if (built < h * 2) return 1;
        return 2;
    }

    public bool CanExpandSecret(int tier) => tier switch
    {
        0 => tier1ExpandLeft,
        1 => tier2ExpandLeft,
        _ => tier3ExpandLeft
    };

    public bool CanExpandBiome(int tier) => tier switch
    {
        0 => tier1ExpandRight,
        1 => tier2ExpandRight,
        _ => tier3ExpandRight
    };

    public bool CanRetractSecret(int tier) => tier switch
    {
        0 => tier1RetractLeft,
        1 => tier2RetractLeft,
        _ => tier3RetractLeft
    };

    public bool CanRetractBiome(int tier) => tier switch
    {
        0 => tier1RetractRight,
        1 => tier2RetractRight,
        _ => tier3RetractRight
    };
}