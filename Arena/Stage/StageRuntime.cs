using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StageRuntime — fait vivre un stage chargé dans ArenaGrid (étape 2).
///
/// RÔLES :
///   1. Crée une vue par case (StageCellView, provisoire).
///   2. Fait apparaître un ennemi quand sa case est révélée — au chargement pour
///      les cases déjà révélées, puis à chaque révélation en jeu. C est la règle
///      « un arbre peint sur un ennemi le rend non révélé ».
///   3. Rafraîchit les vues quand des cases sont révélées ou que les chiffres changent.
///
/// Ajouté automatiquement par CombatArena. Rien à régler dans l Inspector.
///
/// ÉTAPE 3 (à venir) : les vraies vues de biome feront apparaître l ennemi
/// elles-mêmes, comme dans l aventure ; ce composant ne le fera plus que pour
/// les cases sans vraie vue.
/// </summary>
public class StageRuntime : MonoBehaviour
{
    private ArenaGrid _grid;
    private ArenaEnemyPlacer _placer;
    private StageCellView[,] _views;
    private readonly List<GameObject> _spawnedViews = new List<GameObject>();
    private readonly List<EnemyInstance> _spawnedEnemies = new List<EnemyInstance>();
    private bool _subscribed;

    public int SpawnedEnemyCount => _spawnedEnemies.Count;

    /// <summary>Construit les vues et fait apparaître les ennemis déjà visibles.</summary>
    public void Build(ArenaGrid grid, Transform cellsParent, ArenaEnemyPlacer placer, float cellVisualSize)
    {
        Clear();

        _grid = grid;
        _placer = placer;
        _views = new StageCellView[grid.Width, grid.Height];

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                var go = new GameObject("StageCell_" + x + "_" + y);
                go.transform.SetParent(cellsParent, false);
                go.transform.position = grid.GridToWorld(x, y, 0.1f);

                var view = go.AddComponent<StageCellView>();
                view.Setup(grid.GetCell(x, y), cellVisualSize);

                _views[x, y] = view;
                _spawnedViews.Add(go);
            }
        }

        // Ennemis posés sur une case déjà révélée : visibles dès le départ
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
                SpawnEnemyIfRevealed(grid.GetCell(x, y));

        _grid.GridChanged += RefreshAll;
        EventBus.Subscribe<OnCellsRevealed>(HandleCellsRevealed);
        _subscribed = true;
    }

    /// <summary>Détruit vues et ennemis du stage, coupe les abonnements.</summary>
    public void Clear()
    {
        if (_subscribed)
        {
            EventBus.Unsubscribe<OnCellsRevealed>(HandleCellsRevealed);
            _subscribed = false;
        }
        if (_grid != null) _grid.GridChanged -= RefreshAll;

        foreach (var ei in _spawnedEnemies)
            if (ei != null) Destroy(ei.gameObject);
        _spawnedEnemies.Clear();

        foreach (var go in _spawnedViews)
            if (go != null) Destroy(go);
        _spawnedViews.Clear();

        _views = null;
        _grid = null;
    }

    private void OnDestroy()
    {
        Clear();
    }

    // =========================================================================
    // Révélation
    // =========================================================================

    private void HandleCellsRevealed(OnCellsRevealed evt)
    {
        if (_grid == null || _views == null || evt.Cells == null) return;

        foreach (var cell in evt.Cells)
        {
            if (cell == null) continue;
            // Ignorer les cases d une autre grille
            if (_grid.GetCell(cell.X, cell.Y) != cell) continue;

            _views[cell.X, cell.Y].Refresh();
            SpawnEnemyIfRevealed(cell);
        }
    }

    private void SpawnEnemyIfRevealed(Cell cell)
    {
        if (cell == null || !cell.IsRevealed) return;
        if (!(cell.IsEnemy || cell.IsBoss)) return;
        if (HasLivingEnemyAt(cell.X, cell.Y)) return;

        var ei = _placer.PlaceEnemy(cell.Content, cell.X, cell.Y, _grid);
        if (ei != null) _spawnedEnemies.Add(ei);
    }

    private static bool HasLivingEnemyAt(int x, int y)
    {
        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;
            if (ei.GridX == x && ei.GridY == y) return true;
        }
        return false;
    }

    // =========================================================================
    // Rafraîchissement
    // =========================================================================

    private void RefreshAll()
    {
        if (_views == null) return;
        foreach (var view in _views)
            if (view != null) view.Refresh();
    }
}