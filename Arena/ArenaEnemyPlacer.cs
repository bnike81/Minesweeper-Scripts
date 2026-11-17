using UnityEngine;

/// <summary>
/// ArenaEnemyPlacer — Spawn direct d ennemis a des positions explicites.
///
/// Reproduit le pattern de CellView.SpawnEnemyInstance mais sans passer
/// par les cells views : on crée l ennemi, on l instancie, on initialise.
///
/// Les ennemis sont deja marqués IsEnemy sur la grille par CombatArena,
/// donc le TurnManager les detectera naturellement en combat.
/// </summary>
public class ArenaEnemyPlacer : MonoBehaviour
{
    /// <summary>
    /// Place un ennemi a la position grille demandée. Retourne l EnemyInstance.
    /// Ne modifie PAS la grille (IsEnemy doit être mis avant via ArenaGrid.MarkEnemyAt).
    /// </summary>
    public EnemyInstance PlaceEnemy(CellContent enemyType, int gridX, int gridY, ArenaGrid grid)
    {
        var db = EnemyDatabase.Instance;
        var data = db?.Get(enemyType);

        GameObject go;
        // Utiliser GridToWorld pour appliquer l offset de la grille
        Vector3 worldPos = grid.GridToWorld(gridX, gridY, -0.15f);

        if (data != null && data.prefab != null)
        {
            // Spawn le prefab configuré (sprites, animator, behavior)
            go = Instantiate(data.prefab, null);
            go.transform.position = worldPos;
        }
        else
        {
            // Fallback : création dynamique minimale
            go = new GameObject($"Enemy_{enemyType}");
            go.transform.position = worldPos;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = 7;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = Vector2.one * 0.9f;

            Debug.LogWarning($"<color=#FFAA00>[ArenaEnemyPlacer]</color> " +
                             $"Pas de prefab pour {enemyType} dans EnemyDatabase — fallback minimal");
        }

        var ei = go.GetComponent<EnemyInstance>();
        if (ei == null) ei = go.AddComponent<EnemyInstance>();

        ei.Initialize(enemyType, gridX, gridY);

        Debug.Log($"<color=#00AAFF>[ArenaEnemyPlacer]</color> " +
                  $"Placed {enemyType} at ({gridX},{gridY})");

        return ei;
    }
}