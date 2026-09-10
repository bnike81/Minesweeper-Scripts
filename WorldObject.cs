using UnityEngine;

/// <summary>
/// WorldObject - Cache l'objet sur les cases non revelees.
/// Visible seulement quand le joueur decouvre la case correspondante.
/// </summary>
public class WorldObject : MonoBehaviour
{
    [SerializeField, ReadOnly] private int _gridX;
    [SerializeField, ReadOnly] private int _gridY;
    [SerializeField] private bool _alwaysVisible = false;

    private SpriteRenderer[] _renderers;
    private Collider2D[] _colliders;
    private bool _initialized = false;
    private bool _revealed = false;

    // -------------------------------------------------------------------------

    public void Initialize(int gridX, int gridY)
    {
        _gridX = gridX;
        _gridY = gridY;
        // S'enregistrer dans LootSystem pour �viter FindObjectsOfType
        LootSystem.RegisterOccupied(_gridX, _gridY);
        _initialized = true;

        _renderers = GetComponentsInChildren<SpriteRenderer>(true);
        _colliders = GetComponents<Collider2D>();

        if (_alwaysVisible) return;

        // Cache immediatement
        ApplyVisibility(false);

        // Abonne apres initialisation pour avoir les bonnes coordonnees
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);

        // Verifie si la case est deja revelee (relance de partie)
        var cell = GridManager.Instance?.GetCell(_gridX, _gridY);
        if (cell != null && cell.IsRevealed)
        {
            _revealed = true;
            ApplyVisibility(true);
        }
    }

    private void OnDestroy()
    {
        LootSystem.UnregisterOccupied(_gridX, _gridY);
        if (_initialized && !_alwaysVisible)
            EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
    }

    // -------------------------------------------------------------------------

    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        if (_revealed) return;
        if (evt.IsSummary) return; // déjà traité batch par batch
        foreach (var c in evt.Cells)
        {
            if (c.X == _gridX && c.Y == _gridY)
            {
                _revealed = true;
                ApplyVisibility(true);
                return;
            }
        }
    }

    // -------------------------------------------------------------------------

    private void ApplyVisibility(bool visible)
    {
        foreach (var sr in _renderers)
            if (sr != null) sr.enabled = visible;

        foreach (var col in _colliders)
            if (col != null) col.enabled = visible;
    }
}