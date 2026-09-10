using UnityEngine;

/// <summary>
/// BuildingDoorTrigger — Composant à poser sur le sprite d'un bâtiment.
/// Fonctionne exactement comme CaveEntranceTrigger mais pour les intérieurs.
///
/// SETUP AUTOMATIQUE via SheeperFarmSpawner (ou tout autre spawner) :
///   Ajouter ce composant sur le GO de la cabane avec un BoxCollider2D.
///   Appeler Initialize(gridX, gridY) avec la case "porte" en Grid 0.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class BuildingDoorTrigger : MonoBehaviour
{
    private int _gridX, _gridY;
    private string _buildingName = "Bâtiment";

    public void Initialize(int gridX, int gridY, string buildingName = "Bâtiment")
    {
        _gridX = gridX;
        _gridY = gridY;
        _buildingName = buildingName;

        var col = GetComponent<BoxCollider2D>();
        float cs = GridManager.Instance?.CellSize ?? 1f;
        // Diviser par localScale pour obtenir la bonne taille en espace local.
        // La cabane a _cabinScale=1.5f → sans correction le collider serait 1.5× trop grand.
        float scl = Mathf.Max(0.1f, transform.localScale.x);
        col.size = new Vector2((cs * 2f) / scl, cs / scl);
        col.isTrigger = false;
    }

    // ── Tooltip ───────────────────────────────────────────────────────────────

    private void OnMouseEnter()
    {
        // Si déjà en intérieur sur une porte de sortie → "Sortir"
        if (IndoorManager.Instance?.IsIndoor == true)
            TooltipUI.Show($"Sortir de {_buildingName}");
        else
            TooltipUI.Show($"Entrer dans {_buildingName}");
    }

    private void OnMouseExit() =>
        TooltipUI.Hide();

    // ── Clic → entrée bâtiment ────────────────────────────────────────────────

    private void OnMouseDown()
    {
        if (IndoorManager.Instance == null) return;
        if (CaveManager.Instance?.IsInCave == true) return;

        var hero = HeroController.Instance;
        if (hero == null) return;

        int dist = Mathf.Max(
            Mathf.Abs(hero.GridPosition.x - _gridX),
            Mathf.Abs(hero.GridPosition.y - _gridY));

        if (dist <= 2)
            IndoorManager.Instance.EnterBuilding(_gridX, _gridY);
        else
            hero.ApproachCaveEntrance(_gridX, _gridY);
    }

    private void OnDestroy() => TooltipUI.Hide();
}