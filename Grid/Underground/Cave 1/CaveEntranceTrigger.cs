using UnityEngine;

/// <summary>
/// CaveEntranceTrigger — Déclenche l'entrée dans la grotte.
/// Placé par MountainBuilder sur les faces cave de la montagne.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class CaveEntranceTrigger : MonoBehaviour
{
    private int _gridX, _gridY;
    private bool _isBottom;
    private bool _initialized;

    public void Initialize(int gridX, int gridY, bool isBottom)
    {
        _gridX = gridX;
        _gridY = gridY;
        _isBottom = isBottom;
        _initialized = true;
        Debug.Log($"[CaveEntranceTrigger] Initialisé sur {gameObject.name} → ({gridX},{gridY}) bottom={isBottom}");
    }

    private void OnMouseDown()
    {
        Debug.Log($"[CaveEntranceTrigger] CLIC sur ({_gridX},{_gridY}) " +
                  $"init={_initialized} " +
                  $"CaveManager={CaveManager.Instance != null} " +
                  $"IsInCave={CaveManager.Instance?.IsInCave} " +
                  $"IsIndoor={IndoorManager.Instance?.IsIndoor}");

        if (!_initialized) { Debug.LogWarning("[CaveEntranceTrigger] Pas initialisé !"); return; }
        if (CaveManager.Instance == null) { Debug.LogError("[CaveEntranceTrigger] CaveManager.Instance NULL !"); return; }
        if (CaveManager.Instance.IsInCave) return;
        if (IndoorManager.Instance?.IsIndoor == true) return;

        CaveManager.Instance.EnterCave(_gridX, _gridY);
    }

    private void OnMouseEnter()
    {
        if (_initialized)
            TooltipUI.Show("Entrer dans la grotte");
    }

    private void OnMouseExit() => TooltipUI.Hide();
}