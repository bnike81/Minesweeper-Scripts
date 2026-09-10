using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// UndergroundGrid — IGridContext pour la grotte souterraine.
///
/// Même système de coordonnées que GridManager (CellStep identique).
/// Positionnée à Y = _worldOriginY (par défaut -500) pour ne pas
/// chevaucher MainGrid.
///
/// Reçoit le layout depuis CaveLayout.GenerateFromLayout().
/// IsBlocked() utilise _isFloor[,] pour distinguer sol et mur.
///
/// Minesweeper : même mécanique qu'en surface (cases cachées, dangers, ennemis).
/// </summary>
public class UndergroundGrid : MonoBehaviour, IGridContext
{
    public static UndergroundGrid Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Position monde")]
    [Tooltip("Offset Y pour la grotte (évite chevauchement avec la surface)")]
    [SerializeField] private float _worldOriginY = 0f;

    // ── Données ───────────────────────────────────────────────────────────────

    private int _width, _height;
    private float _cellStep;
    private Cell[,] _grid;
    private bool[,] _isFloor;  // true = sol marchable, false = mur

    // Portails
    private Vector2Int _entryPos, _exitPos;
    public Vector2Int EntryPos => _entryPos;
    public Vector2Int ExitPos => _exitPos;

    public bool IsGenerated { get; private set; }

    /// <summary>Accès au tableau de cellules.</summary>
    public Cell[,] GetGrid() => _grid;

    /// <summary>
    /// Appelé quand un ennemi est vaincu dans la grotte.
    /// Vide la case, recalcule les adjacences, rafraîchit les visuels.
    /// Même logique que GridManager.OnEnemyDefeated.
    /// </summary>
    public void OnEnemyDefeated(int x, int y)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;

        cell.Content = CellContent.Empty;

        // Recalculer les nombres adjacents
        MinesweeperLogic.ComputeAllAdjacencies(_grid, _width, _height);

        // Rafraîchir les visuels
        UndergroundCellView.RefreshAllViews();

        Debug.Log($"[UndergroundGrid] Ennemi vaincu à ({x},{y}) — nombres actualisés");
    }

    // ── IGridContext ──────────────────────────────────────────────────────────

    public int Width => _width;
    public int Height => _height;
    public float CellStep => _cellStep;
    public float CellSpacing => GridManager.Instance?.CellSpacing ?? 0.05f;
    public float CellSize => GridManager.Instance?.CellSize ?? 1f;

    public Cell GetCell(int x, int y) =>
        IsInBounds(x, y) ? _grid[x, y] : null;

    public bool IsInBounds(int x, int y) =>
        _grid != null && x >= 0 && x < _width && y >= 0 && y < _height;

    public bool IsBlocked(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        if (_isFloor != null) return !_isFloor[x, y];
        return false;
    }

    public RevealResult RevealCell(int x, int y)
    {
        if (!IsInBounds(x, y)) return RevealResult.AlreadyRevealed;
        if (IsBlocked(x, y)) return RevealResult.AlreadyRevealed;

        var cell = _grid[x, y];
        if (cell == null) return RevealResult.AlreadyRevealed;
        if (cell.IsRevealed) return RevealResult.AlreadyRevealed;

        cell.ForceReveal();

        // Portail de sortie → signaler à CaveManager
        if (x == _exitPos.x && y == _exitPos.y)
            return RevealResult.Empty; // géré par le click handler

        // Révélation en cascade pour les cases vides adjacentes
        if (!cell.IsDangerous && !cell.IsEnemy)
            FloodReveal(x, y);

        return RevealResult.Empty;
    }

    public Vector3 GridToWorld(int gx, int gy, float z = 0f) =>
        new Vector3(gx * _cellStep, _worldOriginY + gy * _cellStep, z);

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        float step = _cellStep > 0f ? _cellStep : 1.063f;
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / step),
            Mathf.RoundToInt((worldPos.y - _worldOriginY) / step));
    }

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
    // GÉNÉRATION DEPUIS CAVELAYOUT (WFC)
    // =========================================================================

    /// <summary>
    /// Crée les cells à partir du layout généré par CaveLayout.
    /// Sol = case jouable (minesweeper). Mur = bloqué et pré-révélé.
    /// </summary>
    public void GenerateFromLayout(CaveLayout layout)
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.063f;
        _width = layout.Width;
        _height = layout.Height;
        _grid = new Cell[_width, _height];
        _isFloor = layout.GetFloorMap();

        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                var cell = new Cell(x, y, BiomeType.Cave);
                _grid[x, y] = cell;

                // Murs : pré-révélés (pas interactifs)
                if (!_isFloor[x, y])
                    cell.ForceReveal();
            }

        _entryPos = layout.EntryPos;
        _exitPos = layout.ExitPos;
        IsGenerated = true;

        Debug.Log($"[UndergroundGrid] ✅ Grille {_width}×{_height} créée. " +
                  $"Entrée={_entryPos}, Sortie={_exitPos}");
    }

    // =========================================================================
    // PLACEMENT DES DANGERS (minesweeper)
    // =========================================================================

    /// <summary>
    /// Place les ennemis et pièges sur les cases sol de la grotte.
    /// Appelé après le premier clic du joueur (comme GridManager.PlaceDangers).
    /// </summary>
    /// <summary>
    /// Place les ennemis de grotte sur les cases sol.
    /// 4 types : Spider, Bat, GoblinLance, GoblinMasse.
    /// Pas de traps pour l'instant.
    /// </summary>
    public void PlaceDangers(int safeX, int safeY, CaveConfig config)
    {
        if (_grid == null || config == null) return;

        // Cases exclues : zone sûre (clic + 8 voisins) + portails
        var excluded = new System.Collections.Generic.HashSet<(int, int)>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                excluded.Add((safeX + dx, safeY + dy));
        excluded.Add((_entryPos.x, _entryPos.y));
        excluded.Add((_exitPos.x, _exitPos.y));

        // Cases sol disponibles
        var available = new System.Collections.Generic.List<Cell>();
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                if (!_isFloor[x, y]) continue;
                if (excluded.Contains((x, y))) continue;
                var cell = _grid[x, y];
                if (cell != null && cell.Content == CellContent.Empty)
                    available.Add(cell);
            }

        // Mélanger (Fisher-Yates)
        for (int i = available.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (available[i], available[j]) = (available[j], available[i]);
        }

        // Nombre d'ennemis à placer
        int totalFloor = available.Count;
        int enemyCount = Mathf.RoundToInt(totalFloor * config.dangerRatio);

        // Types d'ennemis grotte (sélection aléatoire pondérée)
        CellContent[] caveEnemies = {
            CellContent.Enemy_Spider,       // 30%
            CellContent.Enemy_Spider,
            CellContent.Enemy_Spider,
            CellContent.Enemy_Bat,           // 30%
            CellContent.Enemy_Bat,
            CellContent.Enemy_Bat,
            CellContent.Enemy_GoblinLance,   // 20%
            CellContent.Enemy_GoblinLance,
            CellContent.Enemy_GoblinMasse,   // 20%
            CellContent.Enemy_GoblinMasse,
        };

        int placed = 0;
        for (int i = 0; i < available.Count && placed < enemyCount; i++)
        {
            if (available[i].Content != CellContent.Empty) continue;

            CellContent type = caveEnemies[Random.Range(0, caveEnemies.Length)];
            available[i].Content = type;
            placed++;
        }

        // Calculer les adjacences (nombres 1-8 pour chaque case)
        MinesweeperLogic.ComputeAllAdjacencies(_grid, _width, _height);

        _dangersPlaced = true;
        Debug.Log($"[UndergroundGrid] ✅ {placed} ennemis placés sur {totalFloor} cases sol " +
                  $"(ratio {config.dangerRatio:P0})");
    }

    private bool _dangersPlaced;
    public bool DangersPlaced => _dangersPlaced;

    /// <summary>Réinitialise la grotte (nouveau run).</summary>
    public void Clear()
    {
        _grid = null;
        _isFloor = null;
        _width = 0;
        _height = 0;
        IsGenerated = false;
        _dangersPlaced = false;
    }

    // =========================================================================
    // FLOOD REVEAL (cases vides adjacentes)
    // =========================================================================

    /// <summary>
    /// Révélation en cascade (minesweeper) :
    ///   - Cases vides (adj=0) → révéler + continuer la cascade
    ///   - Cases nombres (adj>0) → révéler mais STOP cascade
    ///   - Cases ennemis/pièges → NE PAS révéler (le joueur doit cliquer dessus)
    /// </summary>
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

                    var neighbor = _grid[nx, ny];
                    if (neighbor == null || neighbor.IsRevealed) continue;

                    // ENNEMIS et PIÈGES : ne PAS révéler en cascade !
                    if (neighbor.IsEnemy || neighbor.IsBoss || neighbor.IsTrap)
                        continue;

                    // Révéler cette case
                    neighbor.ForceReveal();

                    // Continuer la cascade SEULEMENT si case vide (adj=0)
                    if (neighbor.AdjacentDangerCount == 0 && neighbor.Content == CellContent.Empty)
                        queue.Enqueue(new Vector2Int(nx, ny));
                }
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>Position monde de l'entrée de la grotte.</summary>
    public Vector3 EntryWorldPos => GridToWorld(_entryPos.x, _entryPos.y);

    /// <summary>Position monde de la sortie de la grotte.</summary>
    public Vector3 ExitWorldPos => GridToWorld(_exitPos.x, _exitPos.y);

    /// <summary>Vérifie si une case est le portail de sortie.</summary>
    public bool IsExitPortal(int x, int y) =>
        x >= _exitPos.x && x < _exitPos.x + 2 &&
        y >= _exitPos.y - 1 && y <= _exitPos.y;

    /// <summary>Vérifie si une case est le portail d'entrée.</summary>
    public bool IsEntryPortal(int x, int y) =>
        x >= _entryPos.x && x < _entryPos.x + 2 &&
        y >= _entryPos.y && y <= _entryPos.y + 1;
}