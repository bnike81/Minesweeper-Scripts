using UnityEngine;

/// <summary>
/// CaveEntranceTrigger — Déclenche l'entrée dans la grotte.
/// Placé par MountainBuilder sur les faces cave de la montagne.
///
/// caveIndex = 0 → cave montagne 1, caveIndex = 1 → cave montagne 2, etc.
/// Initialisé par MountainBuilder.AttachCaveEntrance.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class CaveEntranceTrigger : MonoBehaviour
{
    private int _gridX, _gridY;
    private bool _isBottom;
    private int _caveIndex;
    private bool _initialized;

    public void Initialize(int gridX, int gridY, bool isBottom, int caveIndex = 0)
    {
        _gridX = gridX;
        _gridY = gridY;
        _isBottom = isBottom;
        _caveIndex = caveIndex;
        _initialized = true;
        Debug.Log($"[CaveEntranceTrigger] Init ({gridX},{gridY}) bottom={isBottom} cave={caveIndex}");
    }

    private void OnMouseDown()
    {
        if (!_initialized) return;
        if (CaveManager.Instance == null) return;
        if (CaveManager.Instance.IsInCave) return;
        if (IndoorManager.Instance?.IsIndoor == true) return;

        Debug.Log($"[CaveEntranceTrigger] CLIC ({_gridX},{_gridY}) cave={_caveIndex}");
        CaveManager.Instance.EnterCave(_gridX, _gridY);
    }

    private void OnMouseEnter()
    {
        if (_initialized) TooltipUI.Show("Entrer dans la grotte");
    }

    private void OnMouseExit() => TooltipUI.Hide();
}