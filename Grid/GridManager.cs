using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// GridManager — Base de données et logique des grilles.
/// 
/// RÔLE (après séparation MainGrid) :
///   • Données : Cell[,], dimensions, génération Minesweeper, placement dangers
///   • Logique : RevealCell, ToggleFlag, TryChordClick, ExtendGrid
///   • Interface : IGridContext (GetCell, IsInBounds, IsBlocked, GridToWorld)
///   • Communication : publie des callbacks statiques (OnCellViewRefreshRequested)
///                     que MainGrid consomme pour gérer les vues
///
/// CE QUI A ÉTÉ DÉPLACÉ DANS MainGrid :
///   SpawnOneCellView, SpawnCellViews, UpdateCellView, RefreshTreeColumn,
///   RefreshAllCellViews, ClearVisuals, _cellViews[,], _cellPrefab, _biomeDatabase
///
public class GridManager : MonoBehaviour, IGridContext
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static GridManager Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("═══ Paramètres Grille ═══")]
    [Header("═══ Visuels (assignés ici, utilisés par MainGrid) ═══")]
    [Tooltip("Préfab d'une cellule — MainGrid l'utilise pour spawner les vues")]
    [SerializeField] private GameObject _cellPrefab;
    [SerializeField] private BiomeDatabase _biomeDatabase;

    /// <summary>Accessible par MainGrid comme fallback si ses propres champs sont vides.</summary>
    public GameObject CellPrefab => _cellPrefab;
    public BiomeDatabase BiomeDatabaseAsset => _biomeDatabase;

    [Tooltip("Taille d'une cellule en unités Unity")]
    [SerializeField] private float _cellSize = 1f;

    [Tooltip("Espacement entre les cellules")]
    [SerializeField] private float _cellSpacing = 0f; // 0 = alignement pixel parfait

    [Header("═══ Génération ═══")]
    [Tooltip("Pourcentage de dangers au niveau 1 (0.0 - 1.0)")]
    [Range(0.08f, 0.30f)]
    [SerializeField] private float _baseDangerRatio = 0.12f;

    [Tooltip("Augmentation du ratio de dangers par niveau")]
    [Range(0f, 0.03f)]
    [SerializeField] private float _dangerRatioIncreasePerLevel = 0.01f;

    [Tooltip("Graine aléatoire (0 = aléatoire)")]
    [SerializeField] private int _randomSeed = 0;

    [Header("═══ Contenu par défaut ═══")]
    [Tooltip("Pourcentage de trésors sur la grille")]
    [Range(0.01f, 0.08f)]
    [SerializeField] private float _treasureRatio = 0.03f;

    [Tooltip("Pourcentage de pièges sur la grille")]
    [Range(0f, 0.04f)]
    [SerializeField] private float _trapRatio = 0.01f;

    [Header("=== Enemy Database ===")]
    [Tooltip("Glisse l asset EnemyDatabase ici")]
    [SerializeField] private EnemyDatabase _enemyDatabase;
    [Tooltip("Table de spawn ennemis par niveau - Create->MinesweeperRPG->Enemy Spawn Table")]
    [SerializeField] private EnemySpawnTable _spawnTable;

    [Header("═══ État Courant (Lecture seule) ═══")]
    [SerializeField, ReadOnly] private int _currentWidth;
    [SerializeField, ReadOnly] private int _currentHeight;
    [SerializeField, ReadOnly] private int _currentLevel;
    [SerializeField, ReadOnly] private int _totalDangers;
    [SerializeField, ReadOnly] private int _revealedSafeCount;
    [SerializeField, ReadOnly] private bool _firstClickDone;

    // ─── Données internes ─────────────────────────────────────────────────────
    private Cell[,] _grid;
    private System.Random _rng;

    // ─── Propriétés publiques ─────────────────────────────────────────────────
    public int Width => _currentWidth;
    public int Height => _currentHeight;
    public int CurrentLevel => _currentLevel;
    public Cell[,] Grid => _grid;
    public float CellStep => _cellSize + _cellSpacing;
    public float CellSize => _cellSize;
    public float CellSpacing => _cellSpacing;

    /// <summary>IGridContext — bounds check public.</summary>
    public bool IsInBounds(int x, int y) =>
        MinesweeperLogic.IsInBounds(x, y, _currentWidth, _currentHeight);

    /// <summary>IGridContext — case bloquée si ennemi révélé dessus.</summary>
    public bool IsBlocked(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        var cell = GetCell(x, y);
        return cell != null && cell.IsDangerous && cell.IsRevealed;
    }
    public ICellView GetCellView(int x, int y) =>
        MainGrid.Instance?.GetCellView(x, y);

    /// <summary>
    /// Convertit une position grille en position monde sans dérive flottante.
    /// Utilise le calcul entier en pixels pour un alignement parfait.
    /// </summary>
    public Vector3 GridToWorld(int gx, int gy, float z = 0f)
    {
        // Calcul entier pixel-perfect — indépendant des vues (maintenant dans MainGrid)
        int stepPixels = Mathf.RoundToInt((_cellSize + _cellSpacing) * 16f);
        return new Vector3((gx * stepPixels) / 16f, (gy * stepPixels) / 16f, z);
    }

    /// <summary>IGridContext — convertit position monde en coordonnées grille surface.</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        float step = _cellSize + _cellSpacing;
        if (step <= 0f) step = 1.05f;
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / step),
            Mathf.RoundToInt(worldPos.y / step));
    }

    // Cases reservees par des systemes exterieurs (ferme, PNJ...)
    // Ces cases sont exclues du placement des dangers ET gardent le biome Prairie
    private HashSet<(int, int)> _reservedCells = new HashSet<(int, int)>();

    /// <summary>
    /// Reserve une zone CIRCULAIRE autour d'un centre.
    /// Les cases reservees sont exclues du placement des dangers.
    /// Le biome reste Forest visuellement - seuls les dangers sont exclus.
    /// </summary>
    public void ReserveCircle(int centerX, int centerY, float radius)
    {
        int r = Mathf.CeilToInt(radius);
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                // Cercle : distance euclidienne
                if (dx * dx + dy * dy > radius * radius) continue;

                int nx = centerX + dx;
                int ny = centerY + dy;
                if (!MinesweeperLogic.IsInBounds(nx, ny, _currentWidth, _currentHeight)) continue;

                _reservedCells.Add((nx, ny));
                // IMPORTANT : on ne change PAS le biome
                // La foret reste visuellement foret -> le secteur reste cache
            }
        }

    }

    // Garde l'ancienne API pour compatibilite
    public void ReserveZone(int originX, int originY, int sizeX, int sizeY, int buffer)
    {
        int cx = originX + sizeX / 2;
        int cy = originY + sizeY / 2;
        ReserveCircle(cx, cy, buffer + sizeX * 0.5f);
    }

    public void ClearReservedZones() => _reservedCells.Clear();
    public int ReservedCount => _reservedCells.Count;

    public bool IsCellReserved(int x, int y) => _reservedCells.Contains((x, y));
    public bool FirstClickDone => _firstClickDone;

    // ─── Callbacks visuels (abonnés par MainGrid) ─────────────────────────────
    /// <summary>MainGrid s'abonne pour rafraîchir la vue d'une case.</summary>
    public static event System.Action<int, int> OnCellViewRefreshRequested;
    /// <summary>MainGrid s'abonne pour vider les colonnes d'arbres sales.</summary>
    public static event System.Action OnCellViewFlushRequested;
    /// <summary>MainGrid s'abonne pour rafraîchir une case et ses voisines (premier clic).</summary>
    public static event System.Action<int, int> OnCellViewRefreshNeighboursRequested;

    private void RequestViewRefresh(int x, int y) =>
        OnCellViewRefreshRequested?.Invoke(x, y);
    private void RequestViewFlush() =>
        OnCellViewFlushRequested?.Invoke();
    private void RequestViewRefreshNeighbours(int cx, int cy) =>
        OnCellViewRefreshNeighboursRequested?.Invoke(cx, cy);

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    private void Awake()
    {
        // Charger EnemyDatabase pour que CellView puisse spawner les prefabs
        if (_enemyDatabase != null && EnemyDatabase.Instance == null)
        {
            // OnEnable du ScriptableObject l assigne automatiquement
            // mais on force si necessaire
            _ = _enemyDatabase;
        }

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnLevelUp>(OnLevelUp);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnLevelUp>(OnLevelUp);
    }

    // ─── Handlers EventBus ────────────────────────────────────────────────────
    private void OnRunStarted(OnRunStarted evt) => InitializeGrid();

    private bool _isExtending = false;

    private void OnLevelUp(OnLevelUp evt)
    {
        if (evt.NewLevel > _currentLevel)
            StartCoroutine(ExtendGridSequence(_currentLevel + 1, evt.NewLevel));
    }

    // Étale les extensions niveau par niveau — une par frame
    // Évite le freeze d'un OnLevelUp massif (ex: niveau 1→10 en un clic)
    private System.Collections.IEnumerator ExtendGridSequence(int fromLevel, int toLevel)
    {
        if (_isExtending) yield break;
        _isExtending = true;
        for (int lvl = fromLevel; lvl <= toLevel; lvl++)
        {
            yield return null;
            ExtendGrid(lvl);
            int waitFrames = 0;
            while (MainGrid.Instance?.IsSpawning == true)
            { yield return null; waitFrames++; }
            var fog = FogOfWar.Instance;
            if (fog != null)
            {
                waitFrames = 0;
                while (fog.IsBuilding)
                { yield return null; waitFrames++; }
            }
        }
        _isExtending = false;
        Debug.Log("[DBG] ExtendSequence DONE");
    }

    // ─── Publication étalée des cases révélées ────────────────────────────────
    private const int _revealBatchSize = 25;
    // Compteur au lieu de bool — évite la condition de course
    // entre deux batches successifs (yield return null entre chaque)
    private int _batchesInFlight = 0;
    public bool IsPublishingBatch => _batchesInFlight > 0;
    // Buffer réutilisable pour éviter new List à chaque batch
    private static readonly System.Collections.Generic.List<Cell> _batchBuffer
        = new System.Collections.Generic.List<Cell>(25);
    // Buffer de copie pour PublishRevealedBatches — évite new List(revealed)
    private static readonly System.Collections.Generic.List<Cell> _revealedCopy
        = new System.Collections.Generic.List<Cell>(512);

    private System.Collections.IEnumerator PublishRevealedBatches(
        System.Collections.Generic.List<Cell> cells)
    {
        _batchesInFlight++;
        int total = cells.Count;
        int i = 0;
        while (i < total)
        {
            int end = Mathf.Min(i + _revealBatchSize, total);
            // Réutiliser le buffer au lieu de GetRange (qui alloue une nouvelle liste)
            _batchBuffer.Clear();
            for (int j = i; j < end; j++)
                _batchBuffer.Add(cells[j]);
            EventBus.Publish(new OnCellsRevealed { Cells = _batchBuffer });
            i = end;
            if (i < total)
                yield return null;
        }
        _batchesInFlight--;
        // Vérification archers une seule fois après tout le batch
        EventBus.Publish(new OnCellsRevealed { Cells = cells });
    }

    // ─── Initialisation ───────────────────────────────────────────────────────
    public void InitializeGrid()
    {
        var gm = GameManager.Instance;
        _currentWidth = gm.StartGridWidth;
        _currentHeight = gm.StartGridHeight;
        _currentLevel = 1;
        _firstClickDone = false;

        _rng = _randomSeed != 0
            ? new System.Random(_randomSeed)
            : new System.Random();

        int maxLevel = 10;
        int maxHeight = gm.StartGridHeight + gm.GridHeightExtensionPerLevel * maxLevel;
        _grid = new Cell[_currentWidth, maxHeight];
        // _cellViews géré par MainGrid

        // Génération directe — fiable, pas de coroutine
        GenerateGrid();
    }

    // ─── Génération de la grille ──────────────────────────────────────────────
    private void GenerateGrid()
    {
        // Mode Debug — appliquer les paramètres dev
        var gmc = GameModeConfig.Instance;
        if (gmc != null && gmc.IsDebug)
        {
            _baseDangerRatio = gmc.DebugDangerRatio;
            Debug.Log($"[GridManager] Debug: dangerRatio={_baseDangerRatio}");
        }

        MainGrid.Instance?.ClearVisuals();

        // Nettoyer les enfants de GridManager (EnemyInstances, TrapInstances spawnés ici)
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        ClearReservedZones();

        // Tableaux pré-alloués dans InitializeGrid — ne pas recréer
        // Réinitialiser seulement la zone active
        for (int x = 0; x < _currentWidth; x++)
            for (int y = 0; y < _currentHeight; y++)
            {
                _grid[x, y] = new Cell(x, y, BiomeType.Forest);
                // _cellViews[x, y] = null; → géré par MainGrid
            }

        //Debug.Log($"<color=#00FF88>[GridManager]</color> Grille créée {_currentWidth}×{_currentHeight}");

        // Publie OnGridDataReady : ShepherdFarmSpawner peut maintenant
        // reserver des zones (biome Prairie + exclusion dangers)
        EventBus.Publish(new OnGridDataReady { Width = _currentWidth, Height = _currentHeight });

        // 2. Instancier les visuels (apres reservations de zones)
        // OnGridGenerated publié à la fin de SpawnInitialCellViewsAsync
        MainGrid.Instance?.StartSpawning(0, _currentHeight, true, _currentLevel);
    }

    // ─── Placement des dangers (après premier clic) ───────────────────────────
    // Buffer statique réutilisable pour les exclusions de PlaceDangers
    private static readonly HashSet<(int, int)> _excludedBuffer = new HashSet<(int, int)>();

    public void PlaceDangers(int safeX, int safeY)
    {
        // Cases exclues : la case cliquée et ses 8 voisines
        _excludedBuffer.Clear();
        _excludedBuffer.Add((safeX, safeY));
        var excludedPositions = _excludedBuffer;
        foreach (var (dx, dy) in GetAllDirections())
        {
            int nx = safeX + dx, ny = safeY + dy;
            if (MinesweeperLogic.IsInBounds(nx, ny, _currentWidth, _currentHeight))
                excludedPositions.Add((nx, ny));
        }

        // Ajouter les cases reservees (ferme, PNJ) aux exclusions des dangers
        foreach (var pos in _reservedCells)
            excludedPositions.Add(pos);

        float dangerRatio = _baseDangerRatio + (_currentLevel - 1) * _dangerRatioIncreasePerLevel;
        int totalCells = _currentWidth * _currentHeight;
        int dangerCount = Mathf.RoundToInt(totalCells * dangerRatio);

        // Placer les trésors d'abord
        int treasureCount = Mathf.RoundToInt(totalCells * _treasureRatio);
        int trapCount = Mathf.RoundToInt(totalCells * _trapRatio);

        // Construire la liste disponible UNE FOIS pour tous les placements
        var available = GetAvailableCells(excludedPositions);
        Shuffle(available);
        PlaceContentFromList(treasureCount, available, PlaceTreasure);
        PlaceContentFromList(trapCount, available, cell => cell.Content = CellContent.Trap);
        PlaceEnemiesRandom(dangerCount, excludedPositions);

        // Calcul des adjacences — obligatoire mais on évite CountTotal séparé
        MinesweeperLogic.ComputeAllAdjacencies(_grid, _currentWidth, _currentHeight);

        // Compter les dangers en même temps que l'adjacence — pas de double boucle
        _totalDangers = 0;
        for (int cx = 0; cx < _currentWidth; cx++)
            for (int cy = 0; cy < _currentHeight; cy++)
                if (_grid[cx, cy] != null && _grid[cx, cy].CountsAsAdjacentDanger)
                    _totalDangers++;
        _revealedSafeCount = 0;

        //Debug.Log($"<color=#00FF88>[GridManager]</color> Dangers placés: {_totalDangers} ({dangerRatio:P0})");
    }

    private void PlaceEnemiesRandom(int count, HashSet<(int, int)> excluded)
    {
        var availableCells = GetAvailableCells(excluded);
        Shuffle(availableCells);

        // Utiliser EnemySpawnTable si disponible
        var spawnConfig = _spawnTable?.GetConfig(_currentLevel);

        // Fallback legacy si pas de SpawnTable assignee
        var legacyTypes = new[]
        {
            CellContent.Enemy_Wolf,
            CellContent.Enemy_Wolf,
            CellContent.Enemy_Wolf,
            CellContent.Enemy_Bear,
        };

        int placed = 0;
        foreach (var cell in availableCells)
        {
            if (placed >= count) break;
            if (cell.Content != CellContent.Empty) continue;

            CellContent type;
            if (spawnConfig != null)
            {
                // Rouler jusqu a obtenir un type non-trap
                // (les traps sont places par PlaceDangers separement)
                int attempts = 0;
                do
                {
                    type = spawnConfig.Roll();
                    attempts++;
                } while (type == CellContent.Trap && attempts < 10);

                if (type == CellContent.Trap)
                    type = CellContent.Enemy_Wolf; // Ultime fallback
            }
            else
            {
                type = legacyTypes[_rng.Next(legacyTypes.Length)];
            }

            cell.Content = type;
            placed++;
            //Debug.Log("[GridManager] Place " + type + " en (" + cell.X + "," + cell.Y + ")");
        }

        if (spawnConfig == null)
            Debug.LogWarning("[GridManager] Aucune EnemySpawnTable assignee - utilisation du fallback");

    }

    private void PlaceContentRandom(int count, HashSet<(int, int)> excluded, System.Action<Cell> placer)
    {
        var available = GetAvailableCells(excluded);
        Shuffle(available);
        PlaceContentFromList(count, available, placer);
    }

    private void PlaceContentFromList(int count, System.Collections.Generic.List<Cell> available,
        System.Action<Cell> placer)
    {
        int placed = 0;
        foreach (var cell in available)
        {
            if (placed >= count) break;
            if (cell.Content != CellContent.Empty) continue;
            placer(cell);
            placed++;
        }
    }

    private static readonly CellContent[] _treasureTypes =
    {
        CellContent.Treasure_Campfire,
        CellContent.Treasure_Campfire,
        CellContent.Treasure_Flower,
        CellContent.Treasure_Chest,
        CellContent.Treasure_Scroll
    };

    private void PlaceTreasure(Cell cell)
    {
        cell.Content = _treasureTypes[_rng.Next(_treasureTypes.Length)];
    }

    // ─── Révélation d'une case ────────────────────────────────────────────────
    public RevealResult RevealCell(int x, int y)
    {
        // Grille pas encore prête (InitializeAsync en cours)
        if (_grid == null) return RevealResult.AlreadyRevealed;
        if (!MinesweeperLogic.IsInBounds(x, y, _currentWidth, _currentHeight))
            return RevealResult.AlreadyRevealed;

        var cell = _grid[x, y];
        if (cell == null) return RevealResult.AlreadyRevealed;
        if (cell.IsRevealed) return RevealResult.AlreadyRevealed;
        if (cell.IsFlagged) return RevealResult.Flagged;
        if (cell.IsMountainReserved) return RevealResult.AlreadyRevealed;

        // Premier clic : placement des dangers MAINTENANT
        if (!_firstClickDone)
        {
            _firstClickDone = true;
            PlaceDangers(x, y);
            // Rafraîchir seulement les voisins du clic — pas toute la grille
            // Les autres cases sont encore Hidden et ne changent pas
            RequestViewRefreshNeighbours(x, y);
            EventBus.Publish(new OnFirstClick { X = x, Y = y });
        }

        // Vérification du contenu
        if (cell.IsDangerous || cell.IsBoss)
        {
            cell.Reveal();
            RequestViewRefresh(x, y);
            EventBus.Publish(new OnCellsRevealed { Cells = new System.Collections.Generic.List<Cell> { cell } });

            if (cell.IsBoss)
            {
                EventBus.Publish(new OnBossRevealed { X = x, Y = y });
                return RevealResult.BossFound;
            }

            // Les degats sont geres par EnemyInstance via son animation d'attaque
            // GridManager ne publie plus OnPlayerDamaged pour les ennemis
            EventBus.Publish(new OnXPGained { Amount = cell.XPValue });
            return RevealResult.EnemyHit;
        }

        if (cell.IsTrap)
        {
            cell.Reveal();
            RequestViewRefresh(x, y);
            EventBus.Publish(new OnCellsRevealed { Cells = new System.Collections.Generic.List<Cell> { cell } });
            // Les degats sont geres par TrapInstance quand le heros marche dessus
            // Ne plus appliquer de degats ici

            // TrapInstance spawne automatiquement par CellView.ShowRevealed
            // Revele zone autour du piege
            var cascadeFromTrap = MinesweeperLogic.FloodFill(_grid, x, y, _currentWidth, _currentHeight);
            foreach (var c in cascadeFromTrap) RequestViewRefresh(c.X, c.Y);
            return RevealResult.TrapTriggered;
        }

        if (cell.IsTreasure)
        {
            cell.Reveal();
            RequestViewRefresh(x, y);
            EventBus.Publish(new OnCellsRevealed { Cells = new System.Collections.Generic.List<Cell> { cell } });
            EventBus.Publish(new OnItemCollected { ItemId = EnumNameCache.Get(cell.Content) });
            EventBus.Publish(new OnXPGained { Amount = cell.XPValue });
            return RevealResult.TreasureFound;
        }

        // Case vide ou chiffre : flood fill
        var revealed = MinesweeperLogic.FloodFill(_grid, x, y, _currentWidth, _currentHeight);
        int totalXP = 0;
        foreach (var c in revealed)
        {
            RequestViewRefresh(c.X, c.Y);
            _revealedSafeCount++;
            if (c.IsEmpty) totalXP += c.XPValue;
        }
        if (totalXP > 0)
            EventBus.Publish(new OnXPGained { Amount = totalXP });
        RequestViewFlush();
        // Étaler OnCellsRevealed sur plusieurs frames pour fog/PNJ/loot/archers
        if (revealed.Count > 0)
            _revealedCopy.Clear();
        _revealedCopy.AddRange(revealed);
        StartCoroutine(PublishRevealedBatches(_revealedCopy));

        return revealed.Count > 0 ? RevealResult.Empty : RevealResult.Number;
    }

    // ─── Pose / Enlève un flag ────────────────────────────────────────────────
    public void ToggleFlag(int x, int y)
    {
        if (!MinesweeperLogic.IsInBounds(x, y, _currentWidth, _currentHeight)) return;
        var cell = _grid[x, y];
        if (cell.IsRevealed) return;

        cell.ToggleFlag();
        RequestViewRefresh(x, y);
        EventBus.Publish(new OnCellFlagged { X = x, Y = y, IsFlagged = cell.IsFlagged });
    }

    // ─── Chord Click ──────────────────────────────────────────────────────────
    public void TryChordClick(int x, int y)
    {
        var chordRevealed = MinesweeperLogic.TryChordClick(_grid, x, y, _currentWidth, _currentHeight);
        if (chordRevealed == null) return;

        foreach (var cell in chordRevealed)
        {
            RequestViewRefresh(cell.X, cell.Y);
            if (cell.IsDangerous)
                EventBus.Publish(new OnPlayerDamaged { Damage = cell.DamageOnReveal, CurrentHP = 0, MaxHP = 0 });
        }
    }

    // ─── Extension de grille (Level Up) ───────────────────────────────────────
    public void ExtendGrid(int newLevel)
    {
        int oldHeight = _currentHeight;
        _currentHeight += GameManager.Instance.GridHeightExtensionPerLevel;
        _currentLevel = newLevel;

        // Pas de recopie — on réutilise les tableaux pré-alloués
        // Initialiser seulement les nouvelles cellules
        for (int x = 0; x < _currentWidth; x++)
            for (int y = oldHeight; y < _currentHeight; y++)
                _grid[x, y] = new Cell(x, y, BiomeType.Forest);

        // Publie avant placement dangers - permet aux spawners de reserver leurs zones
        EventBus.Publish(new OnGridExtending
        {
            OldHeight = oldHeight,
            NewHeight = _currentHeight,
            Level = newLevel,
            Width = _currentWidth
        });

        // Placer les dangers (les zones reservees sont deja dans _reservedCells)
        // Réutiliser le buffer statique au lieu de new HashSet
        _excludedBuffer.Clear();
        PlaceEnemiesInRange(oldHeight, _currentHeight, _excludedBuffer);
        int adjStart = Mathf.Max(0, oldHeight - 1);
        MinesweeperLogic.ComputeAdjacenciesInRange(_grid, _currentWidth, _currentHeight,
            adjStart, _currentHeight);
        MainGrid.Instance?.StartSpawning(oldHeight, _currentHeight, false, newLevel);
    }

    private void PlaceEnemiesInRange(int yStart, int yEnd, HashSet<(int, int)> excluded)
    {
        // Ajouter les cases reservees (ferme, PNJ) aux exclusions des dangers
        foreach (var pos in _reservedCells)
            excluded.Add(pos);

        float dangerRatio = _baseDangerRatio + (_currentLevel - 1) * _dangerRatioIncreasePerLevel;
        int cellCount = _currentWidth * (yEnd - yStart);
        int dangerCount = Mathf.RoundToInt(cellCount * dangerRatio);

        _availableCellsBuffer.Clear();
        var available = _availableCellsBuffer;
        for (int x = 0; x < _currentWidth; x++)
            for (int y = yStart; y < yEnd; y++)
                if (!excluded.Contains((x, y))) available.Add(_grid[x, y]);

        var spawnConfig = _spawnTable?.GetConfig(_currentLevel);
        var legacyTypes = new[] { CellContent.Enemy_Wolf };

        Shuffle(available);
        int placed = 0;
        foreach (var cell in available)
        {
            if (placed >= dangerCount) break;
            if (cell.Content != CellContent.Empty) continue;

            CellContent type;
            if (spawnConfig != null)
            {
                int attempts = 0;
                do { type = spawnConfig.Roll(); attempts++; }
                while (type == CellContent.Trap && attempts < 10);
                if (type == CellContent.Trap) type = CellContent.Enemy_Wolf;
            }
            else
                type = legacyTypes[_rng.Next(legacyTypes.Length)];

            cell.Content = type;
            placed++;
        }
    }

    // ─── Visuals ──────────────────────────────────────────────────────────────
    // ─── Spawn étalé via Update — Queue pour gérer les niveaux dans l'ordre ─


    // ─── Utilitaires ──────────────────────────────────────────────────────────
    /// <summary>
    /// Appele par EnemyInstance quand un ennemi est vaincu.
    /// Retire l'ennemi de la grille et recalcule les adjacences.
    /// </summary>
    public void OnEnemyDefeated(int x, int y)
    {
        var cell = GetCell(x, y);
        if (cell == null) return;

        // Vide la case - l'ennemi n'est plus la
        cell.Content = CellContent.Empty;

        // Recalcule toutes les adjacences
        MinesweeperLogic.ComputeAllAdjacencies(_grid, _currentWidth, _currentHeight);

        // Rafraichit les visuels des cases autour
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                RequestViewRefresh(x + dx, y + dy);

        //Debug.Log("<color=#00FF88>[GridManager]</color> Ennemi vaincu en (" + x + "," + y + ") - adjacences mises a jour");
    }

    public Cell GetCell(int x, int y)
    {
        if (!MinesweeperLogic.IsInBounds(x, y, _currentWidth, _currentHeight)) return null;
        return _grid[x, y];
    }

    private static readonly List<Cell> _availableCellsBuffer = new List<Cell>(512);

    private List<Cell> GetAvailableCells(HashSet<(int, int)> excluded)
    {
        _availableCellsBuffer.Clear();
        var list = _availableCellsBuffer;
        for (int x = 0; x < _currentWidth; x++)
            for (int y = 0; y < _currentHeight; y++)
                if (!excluded.Contains((x, y)))
                    list.Add(_grid[x, y]);
        return list;
    }

    private void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = _rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    private static (int dx, int dy)[] GetAllDirections() => new[]
    {
        (-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1)
    };

    // ─── Debug Gizmos ─────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (_grid == null) return;
        float step = _cellSize + _cellSpacing;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.1f);
        Gizmos.DrawWireCube(
            new Vector3(_currentWidth * step * 0.5f, _currentHeight * step * 0.5f, 0),
            new Vector3(_currentWidth * step, _currentHeight * step, 0)
        );
    }
}

// ReadOnlyAttribute défini dans Core/ReadOnlyAttribute.cs