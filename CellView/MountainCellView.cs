using UnityEngine;

/// <summary>
/// MountainCellView — case du biome Montagne.
/// Hérite de CellViewBase. Pas d'arbres ni fougères.
/// Sprites sol montagne (roche, terre...).
/// </summary>
public class MountainCellView : CellViewBase
{
    // =========================================================================
    // Inspector — Montagne
    // =========================================================================

    [Header("=== Sprites Montagne ===")]
    [SerializeField] private Sprite _groundRockSprite;      // sol roche caché
    [SerializeField] private Sprite _groundRockRevSprite;   // sol roche révélé

    // Ennemis montagne — à définir dans CellContent plus tard

    // =========================================================================
    // Init
    // =========================================================================

    public override void Initialize(Cell cell)
    {
        // Utiliser les sprites montagne au lieu des sprites base
        if (_groundRockSprite != null) _hiddenGroundSprite = _groundRockSprite;
        if (_groundRockRevSprite != null) _groundRevealedSprite = _groundRockRevSprite;
        base.Initialize(cell);
    }

    // =========================================================================
    // Override contenu
    // =========================================================================

    // ShowContentIcon — ennemis montagne à implémenter plus tard
}