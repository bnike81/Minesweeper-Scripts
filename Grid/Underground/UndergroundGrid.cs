using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// UndergroundGrid — IGridContext pour la grotte souterraine.
///
/// ALIGNEMENT MONDE :
///   _worldOriginY = (entryPortalY - layout.EntryLocalY) * cellStep
///   → GridToWorld(EntryPos.x, EntryPos.y).y == entryPortalY * cellStep
///   → La case portail en cave = même position monde que la face cave en surface.
///   → Aucun scroll requis pour la transition.
/// </summary>
public class UndergroundGrid : MonoBehaviour, IGridContext
{
    public static UndergroundGrid Instance { get; private set; }

    [Header("Position monde")]
    [Tooltip("Calculé automatiquement — ne pas modifier dans l'Inspector.")]
    private float _worldOriginY = 0f; // calculé dans GenerateFromLayout

    private int _width, _height;
    private float _cellStep;
    private Cell[,] _grid;
    private bool[,] _isFloor;

    private Vector2Int _entryPos, _exitPos;
    public Vector2Int EntryPos => _entryPos;
    public Vector2Int ExitPos => _exitPos;

    public bool IsGenerated { get; private set; }

    public Cell[,] GetGrid() => _grid;

    public void OnEnemyDefeated(int x, int y)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        cell.Content = CellContent.Empty;
        MinesweeperLogic.ComputeAllAdjacencies(_grid, _width, _height);
        // Rafraîchir uniquement les cases sol (chiffres adjacents) — pas les murs
        UndergroundCellView.RefreshFloorNumbers();
        Debug.Log($"[UndergroundGrid] Ennemi vaincu à ({x},{y})");
    }

    // ── IGridContext ──────────────────────────────────────────────────────────

    public int Width => _width;
    public int Height => _height;
    public float CellStep => _cellStep;
    public float CellSpacing => GridManager.Instance?.CellSpacing ?? 0.05f;
    public float CellSize => GridManager.Instance?.CellSize ?? 1f;

    public Cell GetCell(int x, int y) => IsInBounds(x, y) ? _grid[x, y] : null;

    public bool IsInBounds(int x, int y) =>
        _grid != null && x >= 0 && x < _width && y >= 0 && y < _height;

    public bool IsFloor(int x, int y) =>
        IsInBounds(x, y) && _isFloor != null && _isFloor[x, y];

    public bool IsBlocked(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        // Mur de contour ou roche = bloqué
        if (_isFloor != null && !_isFloor[x, y]) return true;
        // Ennemi révélé vivant = bloqué (héros s'arrête adjacent)
        var cell = _grid?[x, y];
        if (cell != null && cell.IsRevealed && cell.IsEnemy) return true;
        // Cases sol non révélées = PAS bloquées en cave (pas d'arbres)
        return false;
    }

    public RevealResult RevealCell(int x, int y)
    {
        if (!IsInBounds(x, y)) return RevealResult.AlreadyRevealed;
        if (_isFloor != null && !_isFloor[x, y]) return RevealResult.AlreadyRevealed;
        var cell = _grid[x, y];
        if (cell == null || cell.IsRevealed) return RevealResult.AlreadyRevealed;
        cell.ForceReveal();
        if (cell.IsEnemy) return RevealResult.EnemyHit;
        if (!cell.IsDangerous) FloodReveal(x, y);
        return RevealResult.Empty;
    }

    // ── Coordonnées ──────────────────────────────────────────────────────────

    public Vector3 GridToWorld(int gx, int gy, float z = 0f) =>
        new Vector3(gx * _cellStep, _worldOriginY + gy * _cellStep, z);

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        float step = _cellStep > 0f ? _cellStep : 1.063f;
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / step),
            Mathf.RoundToInt((worldPos.y - _worldOriginY) / step));
    }

    public Vector3 EntryWorldPos => GridToWorld(_entryPos.x, _entryPos.y);
    public Vector3 ExitWorldPos => GridToWorld(_exitPos.x, _exitPos.y);

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _cellStep = GridManager.Instance?.CellStep ?? 1.063f;
    }

    // =========================================================================
    // GÉNÉRATION
    //
    // entryPortalY : Y de la face cave ENTRÉE en MainGrid (world coord).
    // Permet de calculer _worldOriginY pour aligner les deux grilles.
    // =========================================================================

    public void GenerateFromLayout(CaveLayout layout, int entryPortalY, int yOffset = 0)
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.063f;
        _width = layout.Width;
        _height = layout.Height;
        _grid = new Cell[_width, _height];
        _isFloor = layout.GetFloorMap();

        // _worldOriginY = -cellStep → grid Y=N → world Y = (N-1)*cellStep
        // EntryLocalY ≈ entryPortalY → grid portal à Y=entryPortalY
        // Hero spawn à Y+1 → world = entryPortalY * cellStep ✓
        _worldOriginY = -_cellStep;

        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                var cell = new Cell(x, y, BiomeType.Cave);
                _grid[x, y] = cell;
                if (!_isFloor[x, y]) cell.ForceReveal();
            }

        _entryPos = layout.EntryPos;
        _exitPos = layout.ExitPos;
        IsGenerated = true;

        Debug.Log($"[UndergroundGrid] ✅ {_width}×{_height} " +
                  $"Entry={_entryPos} Exit={_exitPos} worldOriginY={_worldOriginY:F2}");
    }

    public void GenerateFromLayout(CaveLayout layout)
        => GenerateFromLayout(layout, layout.EntryLocalY);

    /// <summary>
    /// Ajoute une cave sur la grille existante SANS effacer.
    /// La grille est étendue si nécessaire.
    /// </summary>
    public void MergeFromLayout(CaveLayout layout, int entryPortalY)
    {
        if (_grid == null)
        {
            GenerateFromLayout(layout, entryPortalY);
            return;
        }

        // Étendre la grille si la nouvelle cave est plus haute
        int neededH = layout.Height;
        if (neededH > _height)
        {
            var oldGrid = _grid;
            var oldFloor = _isFloor;
            int oldH = _height;
            _height = neededH;
            _grid = new Cell[_width, _height];
            _isFloor = new bool[_width, _height];

            for (int x = 0; x < _width; x++)
                for (int y = 0; y < oldH; y++)
                {
                    _grid[x, y] = oldGrid[x, y];
                    _isFloor[x, y] = oldFloor[x, y];
                }

            for (int x = 0; x < _width; x++)
                for (int y = oldH; y < _height; y++)
                {
                    _grid[x, y] = new Cell(x, y, BiomeType.Cave);
                    _grid[x, y].ForceReveal();
                }
        }

        // Merger les nouvelles cases
        var newFloor = layout.GetFloorMap();
        int w = Mathf.Min(layout.Width, _width);
        int h = Mathf.Min(layout.Height, _height);

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (newFloor[x, y])
                {
                    bool wasFloor = _isFloor[x, y];
                    _isFloor[x, y] = true;

                    // Si cette case était mur (ForceRevealed) et devient sol,
                    // la remplacer par une cell fraîche non révélée.
                    // Sinon les ennemis placés dessus seraient déjà "révélés".
                    if (!wasFloor || _grid[x, y] == null)
                        _grid[x, y] = new Cell(x, y, BiomeType.Cave);
                }

                if (_grid[x, y] == null)
                {
                    var cell = new Cell(x, y, BiomeType.Cave);
                    _grid[x, y] = cell;
                    if (!_isFloor[x, y]) cell.ForceReveal();
                }
            }

        Debug.Log($"[UndergroundGrid] ✅ Merge {layout.Width}×{layout.Height} " +
                  $"Entry={layout.EntryPos} Exit={layout.ExitPos} gridH={_height}");
    }

    // =========================================================================
    // DANGERS
    // =========================================================================

    public void PlaceDangers(int safeX, int safeY, CaveConfig config,
                             int caveMinY = 0, int caveMaxY = -1)
    {
        if (_grid == null || config == null) return;

        // Limiter au Y de la cave active (-1 = toute la grille)
        int yLo = caveMinY;
        int yHi = caveMaxY > 0 ? caveMaxY : _height;

        var excluded = new HashSet<(int, int)>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                excluded.Add((safeX + dx, safeY + dy));
        excluded.Add((_entryPos.x, _entryPos.y));
        excluded.Add((_exitPos.x, _exitPos.y));

        var available = new List<Cell>();
        for (int x = 0; x < _width; x++)
            for (int y = yLo; y < yHi; y++)
            {
                if (y < 0 || y >= _height) continue;
                if (!_isFloor[x, y]) continue;
                if (excluded.Contains((x, y))) continue;
                var cell = _grid[x, y];
                if (cell != null && cell.Content == CellContent.Empty)
                    available.Add(cell);
            }

        for (int i = available.Count - 1; i > 0; i--)
        { int j = Random.Range(0, i + 1); (available[i], available[j]) = (available[j], available[i]); }

        CellContent[] caveEnemies = {
            CellContent.Enemy_Spider, CellContent.Enemy_Spider, CellContent.Enemy_Spider,
            CellContent.Enemy_Bat,    CellContent.Enemy_Bat,    CellContent.Enemy_Bat,
            CellContent.Enemy_GoblinLance, CellContent.Enemy_GoblinLance,
            CellContent.Enemy_GoblinMasse, CellContent.Enemy_GoblinMasse,
        };

        int enemyCount = Mathf.RoundToInt(available.Count * config.dangerRatio);
        int placed = 0;
        for (int i = 0; i < available.Count && placed < enemyCount; i++)
        {
            if (available[i].Content != CellContent.Empty) continue;
            available[i].Content = caveEnemies[Random.Range(0, caveEnemies.Length)];
            placed++;
        }

        MinesweeperLogic.ComputeAllAdjacencies(_grid, _width, _height);
        Debug.Log($"[UndergroundGrid] {placed} ennemis (Y {yLo}-{yHi}, ratio {config.dangerRatio:P0})");
    }

    // Surcharge compatibilité
    public void PlaceDangers(int safeX, int safeY, CaveConfig config)
        => PlaceDangers(safeX, safeY, config, 0, -1);

    private readonly HashSet<int> _dangersPlacedCaves = new();
    public bool DangersPlaced => _dangersPlacedCaves.Count > 0;
    public bool DangersPlacedForCave(int caveIndex) => _dangersPlacedCaves.Contains(caveIndex);
    public void MarkDangersPlaced(int caveIndex) => _dangersPlacedCaves.Add(caveIndex);

    public void Clear()
    {
        _grid = null; _isFloor = null;
        _width = 0; _height = 0;
        IsGenerated = false; _dangersPlacedCaves.Clear();
        _worldOriginY = 0f;
    }

    // =========================================================================
    // FLOOD REVEAL
    // =========================================================================

    private void FloodReveal(int startX, int startY)
    {
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = p.x + dx, ny = p.y + dy;
                    if (!IsInBounds(nx, ny) || IsBlocked(nx, ny)) continue;
                    var nb = _grid[nx, ny];
                    if (nb == null || nb.IsRevealed) continue;
                    if (nb.IsEnemy || nb.IsBoss || nb.IsTrap) continue;
                    nb.ForceReveal();
                    if (nb.AdjacentDangerCount == 0 && nb.Content == CellContent.Empty)
                        queue.Enqueue(new Vector2Int(nx, ny));
                }
        }
    }

    // =========================================================================
    // HELPERS PORTAILS
    // =========================================================================

    public bool IsExitPortal(int x, int y) => x == _exitPos.x && y == _exitPos.y;
    public bool IsEntryPortal(int x, int y) => x == _entryPos.x && y == _entryPos.y;
}