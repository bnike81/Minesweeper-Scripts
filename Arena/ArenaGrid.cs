using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ArenaGrid — IGridContext pour le mode arène (test et futur mode Colisée).
///
/// DEUX MODES DE CONSTRUCTION :
///   Build(...)          → arène plate : toutes les cases révélées, aucun terrain
///                         (comportement d origine, inchangé).
///   BuildFromStage(...) → stage peint : cases cachées, terrain (eau, mur,
///                         montagne), ennemis cachés, chiffres du démineur.
///                         Chantier Stage Arena, étape 2.
///
/// EN MODE STAGE, la logique est celle de l aventure :
///   - chiffres calculés par MinesweeperLogic.ComputeAllAdjacencies
///   - révélation en cascade par MinesweeperLogic.FloodFill
///   - RevealCell suit le déroulé de GridManager.RevealCell (sans le placement
///     des dangers au premier clic : le stage est déjà peint)
///
/// PRIORITÉ ActiveGrid.Current :
///   Arena → Indoor → Underground → Surface
/// </summary>
public class ArenaGrid : MonoBehaviour, IGridContext
{
    public static ArenaGrid Instance { get; private set; }

    [Header("Debug — lecture seule")]
    [SerializeField] private int _width;
    [SerializeField] private int _height;
    [SerializeField] private float _cellStep = 1.05f;
    [SerializeField] private Vector2 _worldOffset; // decalage monde pour centrer la grille
    [SerializeField] private bool _isStage;

    private Cell[,] _grid;

    /// <summary>True quand la grille a été construite. Utilisé par ActiveGrid.</summary>
    public bool IsBuilt { get; private set; }

    /// <summary>True si la grille vient d un StageConfig (cases cachées, terrain, chiffres).</summary>
    public bool IsStage => _isStage;

    /// <summary>
    /// Levé quand des chiffres ou des contenus ont changé hors révélation
    /// (ennemi vaincu, ennemi déplacé). Les vues se rafraîchissent dessus.
    /// </summary>
    public event System.Action GridChanged;

    public Cell[,] GetGrid() => _grid;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // =========================================================================
    // Pas de grille
    // =========================================================================

    /// <summary>
    /// Arrondit le pas au pixel (16 px par unité), comme GridManager.GridToWorld.
    /// Exemple : 1.05 → 17 px → 1.0625. Indispensable pour que les sprites de
    /// plusieurs cases (côte, montagne) s alignent comme dans l aventure.
    /// </summary>
    public static float SnapStep(float step)
    {
        return Mathf.RoundToInt(step * 16f) / 16f;
    }

    // =========================================================================
    // Construction — arène plate (comportement d origine)
    // =========================================================================

    /// <summary>
    /// Construit la grille de l arène avec les dimensions voulues.
    /// Toutes les cases sont créées en état Revealed, Content = Empty.
    /// Les ennemis sont ajoutés ensuite via MarkEnemyAt().
    /// </summary>
    public void Build(int width, int height, float cellStep = 1.05f, Vector2 worldOffset = default)
    {
        _width = width;
        _height = height;
        _cellStep = cellStep;
        _worldOffset = worldOffset;
        _isStage = false;
        _grid = new Cell[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = new Cell(x, y);
                cell.Content = CellContent.Empty;
                cell.ForceReveal(); // toutes les cases visibles dès le départ
                _grid[x, y] = cell;
            }
        }

        IsBuilt = true;
        Debug.Log($"<color=#00AAFF>[ArenaGrid]</color> Built {width}×{height} cellStep={cellStep}");
    }

    // =========================================================================
    // Construction — stage peint
    // =========================================================================

    /// <summary>
    /// Construit la grille depuis un StageConfig.
    /// L appelant doit avoir vérifié le stage avec StageConfig.Validate.
    /// </summary>
    public void BuildFromStage(StageConfig stage, float cellStep, Vector2 worldOffset = default)
    {
        stage.EnsureCells();

        _width = stage.width;
        _height = stage.height;
        _cellStep = cellStep;
        _worldOffset = worldOffset;
        _isStage = true;
        _grid = new Cell[_width, _height];

        int hiddenCount = 0, obstacleCount = 0, enemyCount = 0, hiddenEnemyCount = 0, trapCount = 0;

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                var sc = stage.Get(x, y);
                var biome = sc.biome == BiomeType.None ? stage.defaultBiome : sc.biome;
                var cell = new Cell(x, y, biome);
                TerrainRules.Apply(cell, sc.terrain);

                if (cell.IsObstacle)
                {
                    // Obstacle : jamais de contenu. La case est marquée révélée pour que
                    // HeroController.MoveToAndAttack l écarte de ses cases d approche
                    // (il n écarte que les cases bloquées ET révélées).
                    cell.Content = CellContent.Empty;
                    cell.ForceReveal();
                    obstacleCount++;
                }
                else
                {
                    bool supported = sc.content == CellContent.Trap
                                  || StageConfig.IsEnemyContent(sc.content);
                    cell.Content = supported ? sc.content : CellContent.Empty;

                    if (sc.hidden) hiddenCount++;
                    else cell.ForceReveal();

                    if (StageConfig.IsEnemyContent(cell.Content))
                    {
                        enemyCount++;
                        if (sc.hidden) hiddenEnemyCount++;
                    }
                    else if (cell.Content == CellContent.Trap) trapCount++;
                }

                _grid[x, y] = cell;
            }
        }

        // La case de départ du héros doit être révélée (Validate l a signalé en avertissement).
        if (IsInBounds(stage.heroStart.x, stage.heroStart.y))
        {
            var heroCell = _grid[stage.heroStart.x, stage.heroStart.y];
            if (!heroCell.IsRevealed) { heroCell.ForceReveal(); hiddenCount--; }
        }

        MinesweeperLogic.ComputeAllAdjacencies(_grid, _width, _height);

        IsBuilt = true;
        Debug.Log($"<color=#00AAFF>[ArenaGrid]</color> Stage '{stage.displayName}' {_width}×{_height} " +
                  $"pas={_cellStep} — cachées={hiddenCount} obstacles={obstacleCount} " +
                  $"ennemis={enemyCount} (dont cachés={hiddenEnemyCount}) pièges={trapCount}");
    }

    // =========================================================================
    // Ennemis — marquage et synchronisation
    // =========================================================================

    /// <summary>
    /// Marque une case comme contenant un type d ennemi donné.
    /// Refuse les obstacles. En mode stage, appeler RecomputeNumbers() ensuite.
    /// </summary>
    public bool MarkEnemyAt(int x, int y, CellContent enemyType)
    {
        if (!IsInBounds(x, y)) return false;
        var cell = _grid[x, y];
        if (cell.IsObstacle) return false;
        cell.Content = enemyType;
        return true;
    }

    /// <summary>Retire tous les ennemis de la grille (avant d appliquer une config d ennemis).</summary>
    public void ClearEnemies()
    {
        if (_grid == null) return;
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
                if (_grid[x, y].IsEnemy || _grid[x, y].IsBoss)
                    _grid[x, y].Content = CellContent.Empty;
    }

    /// <summary>Recalcule les chiffres du démineur (mode stage uniquement) et prévient les vues.</summary>
    public void RecomputeNumbers()
    {
        if (!_isStage || _grid == null) return;
        MinesweeperLogic.ComputeAllAdjacencies(_grid, _width, _height);
        GridChanged?.Invoke();
    }

    /// <summary>
    /// Notifie que l ennemi a cette case est vaincu.
    /// Miroir de GridManager.OnEnemyDefeated et UndergroundGrid.OnEnemyDefeated.
    /// Appele par EnemyInstance.Die via ActiveGrid.Current.
    /// </summary>
    public void OnEnemyDefeated(int x, int y)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;
        cell.Content = CellContent.Empty;
        Debug.Log($"<color=#00AAFF>[ArenaGrid]</color> Ennemi vaincu à ({x},{y})");
        RecomputeNumbers(); // sans effet en arène plate
    }

    /// <summary>
    /// Réaligne le contenu des cases sur la position réelle des ennemis vivants.
    ///
    /// Pourquoi : EnemyBehaviour.MoveTowardHero déplace l EnemyInstance
    /// (SetGridPosition) sans déplacer le contenu de la case, et selon la version
    /// de EnemyInstance.Die la grille d arène n est pas toujours prévenue d une mort.
    /// Sans cette resynchronisation, une case garderait « ennemi » après le départ
    /// ou la mort de celui-ci (case incliquable, chiffres faux).
    ///
    /// Appelé automatiquement par Update dès que la liste des ennemis vivants change.
    /// Les ennemis encore cachés (pas d EnemyInstance) ne sont pas touchés.
    /// </summary>
    public void SyncEnemyContents()
    {
        if (_grid == null) return;

        var alive = new Dictionary<Vector2Int, CellContent>();
        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;
            if (!IsInBounds(ei.GridX, ei.GridY)) continue;
            alive[new Vector2Int(ei.GridX, ei.GridY)] = ei.EnemyType;
        }

        bool changed = false;

        // 1. Case révélée marquée « ennemi » sans ennemi vivant dessus → vider
        for (int x = 0; x < _width; x++)
            for (int y = 0; y < _height; y++)
            {
                var cell = _grid[x, y];
                if (!cell.IsRevealed) continue;
                if (!(cell.IsEnemy || cell.IsBoss)) continue;
                if (alive.ContainsKey(new Vector2Int(x, y))) continue;
                cell.Content = CellContent.Empty;
                changed = true;
            }

        // 2. Ennemi vivant sur une case non marquée → marquer
        foreach (var kv in alive)
        {
            var cell = _grid[kv.Key.x, kv.Key.y];
            if (cell.IsObstacle || cell.Content == kv.Value) continue;
            cell.Content = kv.Value;
            changed = true;
        }

        if (changed) RecomputeNumbers(); // chiffres : mode stage uniquement
    }

    // Signature des ennemis vivants (positions + types) vue au dernier Update.
    private int _enemySignature;

    private void Update()
    {
        if (!IsBuilt) return;

        int signature = 0;
        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;
            // Somme (indépendante de l ordre de la liste) d un code par ennemi
            signature += unchecked(((ei.GridX * 73856093) ^ (ei.GridY * 19349663)
                                   ^ ((int)ei.EnemyType * 83492791)) + 1);
        }

        if (signature == _enemySignature) return;
        _enemySignature = signature;
        SyncEnemyContents();
    }

    /// <summary>Détruit la grille (reset arène).</summary>
    public void Clear()
    {
        _grid = null;
        _width = 0;
        _height = 0;
        _isStage = false;
        IsBuilt = false;
    }

    // =========================================================================
    // IGridContext
    // =========================================================================

    public int Width => _width;
    public int Height => _height;
    public float CellStep => _cellStep;
    public float CellSpacing => 0.05f;
    public float CellSize => 1f;

    public Cell GetCell(int x, int y) => IsInBounds(x, y) ? _grid[x, y] : null;

    public bool IsInBounds(int x, int y) =>
        _grid != null && x >= 0 && x < _width && y >= 0 && y < _height;

    /// <summary>
    /// Bloqué = hors grille, obstacle de terrain, ou danger révélé
    /// (même règle que GridManager.IsBlocked, plus le terrain).
    /// Les cases cachées ne sont pas « bloquées » ici : comme dans l aventure,
    /// c est HeroPathfinder qui refuse de marcher sur une case non révélée.
    /// </summary>
    public bool IsBlocked(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        var cell = _grid[x, y];
        if (cell == null) return true;
        if (cell.IsObstacle) return true;
        return cell.IsDangerous && cell.IsRevealed;
    }

    /// <summary>
    /// Révèle une case. Même déroulé que GridManager.RevealCell :
    ///   danger (ennemi, piège, boss) → révélation simple, pas de cascade
    ///   case vide ou chiffre         → cascade (FloodFill)
    /// </summary>
    public RevealResult RevealCell(int x, int y)
    {
        if (!IsInBounds(x, y)) return RevealResult.AlreadyRevealed;
        var cell = _grid[x, y];
        if (cell == null) return RevealResult.AlreadyRevealed;

        // Déjà révélée : comportement d origine de l arène (EnemyHit si ennemi dessus)
        if (cell.IsRevealed)
            return cell.IsEnemy ? RevealResult.EnemyHit : RevealResult.AlreadyRevealed;

        if (cell.IsFlagged) return RevealResult.Flagged;
        if (cell.IsObstacle) return RevealResult.AlreadyRevealed;

        if (cell.IsDangerous || cell.IsBoss)
        {
            cell.Reveal();
            Debug.Log($"<color=#00AAFF>[ArenaGrid]</color> Révélation ({x},{y}) → {cell.Content}");
            EventBus.Publish(new OnCellsRevealed { Cells = new List<Cell> { cell } });

            if (cell.IsBoss)
            {
                EventBus.Publish(new OnBossRevealed { X = x, Y = y });
                return RevealResult.BossFound;
            }

            EventBus.Publish(new OnXPGained { Amount = cell.XPValue });
            return RevealResult.EnemyHit;
        }

        // Case vide ou chiffre : cascade.
        // FloodFill renvoie un buffer statique réutilisé → on le copie avant de publier.
        var revealed = new List<Cell>(MinesweeperLogic.FloodFill(_grid, x, y, _width, _height));

        int totalXP = 0;
        foreach (var c in revealed)
            if (c.IsEmpty) totalXP += c.XPValue;
        if (totalXP > 0)
            EventBus.Publish(new OnXPGained { Amount = totalXP });

        if (revealed.Count > 0)
        {
            Debug.Log($"<color=#00AAFF>[ArenaGrid]</color> Révélation ({x},{y}) → cascade de {revealed.Count} case(s)");
            EventBus.Publish(new OnCellsRevealed { Cells = revealed });
        }

        return revealed.Count > 0 ? RevealResult.Empty : RevealResult.Number;
    }

    public Vector3 GridToWorld(int gx, int gy, float z = 0f)
    {
        return new Vector3(gx * _cellStep + _worldOffset.x, gy * _cellStep + _worldOffset.y, z);
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt((worldPos.x - _worldOffset.x) / _cellStep),
            Mathf.RoundToInt((worldPos.y - _worldOffset.y) / _cellStep));
    }
}