using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// MainGrid — Contenu visuel et comportements spécifiques à Grid 0 (surface, altitude 0).
///
/// RÔLE : tout ce qui touche à l'affichage et au contenu propre à la surface.
///   • Spawn et gestion des vues (ForestCellView, MountainCellView) via _cellViews[,]
///   • Queue de spawn étalée sur plusieurs frames (SpawnJob)
///   • Colonnes d'arbres (RefreshTreeColumn, FlushDirtyTreeColumns)
///   • Nettoyage visuel (ClearVisuals)
///   • ForceRevealArcher — comportement surface uniquement
///
/// COMMUNICATION AVEC GridManager :
///   GridManager publie des callbacks statiques (OnCellViewRefreshRequested, etc.)
///   MainGrid s'y abonne et met à jour ses vues en réponse.
///
/// SETUP : GameObject "MainGrid" en scène avec ce script.
///          Assigner _cellPrefab et _biomeDatabase dans l'Inspector.
/// </summary>
public class MainGrid : MonoBehaviour
{
    public static MainGrid Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Préfabs")]
    [Tooltip("Préfab d'une cellule visuelle (fallback si biome inconnu)")]
    [SerializeField] private GameObject _cellPrefab;
    [SerializeField] private BiomeDatabase _biomeDatabase;

    // ── Vues ──────────────────────────────────────────────────────────────────

    private ICellView[,] _cellViews;

    public ICellView GetCellView(int x, int y)
    {
        var gm = GridManager.Instance;
        if (gm == null || _cellViews == null) return null;
        if (x < 0 || y < 0 || x >= gm.Width || y >= gm.Height) return null;
        return _cellViews[x, y];
    }

    // ── Queue de spawn ────────────────────────────────────────────────────────

    private struct SpawnJob
    {
        public int yStart, yEnd, level;
        public bool isFull; // true = OnGridGenerated, false = OnGridExtended
    }

    private readonly Queue<SpawnJob> _spawnQueue = new Queue<SpawnJob>();
    private SpawnJob _currentJob;
    private bool _isSpawning;
    private int _spawnX, _spawnY;
    private const int _spawnPerUpdate = 8;

    public bool IsSpawning => _isSpawning || _spawnQueue.Count > 0;

    // ── Colonnes d'arbres sales ───────────────────────────────────────────────

    private static readonly HashSet<int> _dirtyTreeColumns = new HashSet<int>();

    // ── Archer ───────────────────────────────────────────────────────────────

    private static readonly List<Cell> _archerRevealBuffer = new List<Cell>(1);

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        GridManager.OnCellViewRefreshRequested += RefreshCellViewAt;
        GridManager.OnCellViewFlushRequested += FlushDirtyTreeColumns;
        GridManager.OnCellViewRefreshNeighboursRequested += RefreshCellAndNeighbours;
    }

    private void OnDisable()
    {
        GridManager.OnCellViewRefreshRequested -= RefreshCellViewAt;
        GridManager.OnCellViewFlushRequested -= FlushDirtyTreeColumns;
        GridManager.OnCellViewRefreshNeighboursRequested -= RefreshCellAndNeighbours;
    }

    // =========================================================================
    // SPAWN (déclenché par GridManager.InitializeGrid / ExtendGrid)
    // =========================================================================

    /// <summary>
    /// Démarre le spawn des vues pour une zone de la grille.
    /// Appelé par GridManager après avoir initialisé / étendu les données de Cell[,].
    /// </summary>
    public void StartSpawning(int yStart, int yEnd, bool isFull, int level)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;

        // Vérifier que le prefab est disponible (dans MainGrid OU dans GridManager)
        var prefab = _cellPrefab ?? gm.CellPrefab;
        if (prefab == null)
        {
            Debug.LogError("[MainGrid] ❌ Cell Prefab non assigné !\n" +
                           "→ Assigner dans l'Inspector de GridManager (champ Cell Prefab)\n" +
                           "  OU dans l'Inspector de MainGrid.");
            return;
        }

        // ── Allocation _cellViews ──────────────────────────────────────────────
        // RÈGLE : allouer uniquement si null (après ClearVisuals ou premier lancement).
        //         NE JAMAIS recréer sur une extension : cela détruirait les vues
        //         des niveaux précédents (bug : level 2+ n'avait plus de visuels).
        if (_cellViews == null)
        {
            // Pré-allouer pour la hauteur max afin d'éviter tout resize futur
            int maxH = yEnd + 10 * (GameManager.Instance?.GridHeightExtensionPerLevel ?? 16);
            _cellViews = new ICellView[gm.Width, Mathf.Max(yEnd, maxH)];
            Debug.Log($"[MainGrid] _cellViews alloué [{gm.Width}×{maxH}]");
        }

        _spawnQueue.Enqueue(new SpawnJob
        {
            yStart = yStart,
            yEnd = yEnd,
            isFull = isFull,
            level = level
        });
    }

    // ── Loop de spawn (Update) ────────────────────────────────────────────────

    private void Update()
    {
        if (!_isSpawning)
        {
            if (_spawnQueue.Count == 0) return;
            _currentJob = _spawnQueue.Dequeue();
            _spawnX = 0;
            _spawnY = _currentJob.yStart;
            _isSpawning = true;
        }

        var gm = GridManager.Instance;
        if (gm == null) { _isSpawning = false; return; }

        int count = 0;
        while (count < _spawnPerUpdate)
        {
            if (_spawnX >= gm.Width)
            {
                // Job terminé
                _isSpawning = false;

                // Rafraîchir toutes les colonnes d'arbres
                for (int x2 = 0; x2 < gm.Width; x2++)
                    RefreshTreeColumn(x2);

                // Publier l'événement de fin de spawn
                if (_currentJob.isFull)
                {
                    Debug.Log("[MainGrid] OnGridGenerated publié");
                    EventBus.Publish(new OnGridGenerated
                    {
                        Width = gm.Width,
                        Height = gm.Height
                    });
                }
                else
                {
                    EventBus.Publish(new OnGridExtended
                    {
                        NewHeight = gm.Height,
                        Level = _currentJob.level
                    });
                }
                break;
            }

            SpawnOneCellView(gm, _spawnX, _spawnY, _currentJob.yStart);
            count++;

            _spawnY++;
            if (_spawnY >= _currentJob.yEnd)
            {
                _spawnY = _currentJob.yStart;
                _spawnX++;
            }
        }
    }

    // ── Spawn d'une case ─────────────────────────────────────────────────────

    private void SpawnOneCellView(GridManager gm, int x, int y, int yStart)
    {
        // Préfab : priorité MainGrid._cellPrefab, fallback GridManager.CellPrefab
        var basePrefab = _cellPrefab ?? gm.CellPrefab;
        if (basePrefab == null) return;

        float cellStep = gm.CellStep;
        int stepPixels = Mathf.RoundToInt(cellStep * 16f);
        float wx = (x * stepPixels) / 16f;
        float wy = (y * stepPixels) / 16f;

        var cell = gm.Grid[x, y];
        // BiomeDatabase : priorité MainGrid, fallback GridManager
        var biomeDb = _biomeDatabase ?? gm.BiomeDatabaseAsset;
        var prefab = biomeDb != null
            ? biomeDb.GetCellPrefab(cell.Biome) ?? basePrefab
            : basePrefab;

        var go = Instantiate(prefab, new Vector3(wx, wy, 0f), Quaternion.identity, transform);

        var baseView = go.GetComponent<CellViewBase>();
        if (baseView != null)
        {
            baseView.Initialize(cell);
            _cellViews[x, y] = baseView;
        }
        else
        {
            var view = go.GetComponent<CellView>();
            if (view != null)
            {
                view.Initialize(cell);
                var treeLayer = (y == yStart)
                    ? CellView.TreeLayerType.Trunk
                    : CellView.TreeLayerType.Canopy;
                view.SetTreeLayer(treeLayer);
                _cellViews[x, y] = view;
            }
        }

        // Enregistrer dans CellViewCuller + batching matériau
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            CellViewCuller.Instance?.RegisterCell(x, y, sr);
            SpriteBatchingSetup.Instance?.ApplySharedMaterial(sr);
        }
    }

    // =========================================================================
    // REFRESH — abonnements callbacks GridManager
    // =========================================================================

    private void RefreshCellViewAt(int x, int y)
    {
        if (_cellViews == null) return;
        var gm = GridManager.Instance;
        if (gm == null || x < 0 || x >= gm.Width || y < 0 || y >= gm.Height) return;
        _cellViews[x, y]?.Refresh();
        _dirtyTreeColumns.Add(x);
    }

    private void RefreshCellAndNeighbours(int cx, int cy)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                RefreshCellViewAt(cx + dx, cy + dy);
        FlushDirtyTreeColumns();
    }

    // ── Colonnes d'arbres ─────────────────────────────────────────────────────

    private void FlushDirtyTreeColumns()
    {
        foreach (int x in _dirtyTreeColumns)
            RefreshTreeColumn(x);
        _dirtyTreeColumns.Clear();
    }

    private void RefreshTreeColumn(int x)
    {
        if (_cellViews == null) return;
        var gm = GridManager.Instance;
        if (gm == null) return;

        for (int y = 0; y < gm.Height; y++)
        {
            var cell = gm.Grid[x, y];
            var view = _cellViews[x, y];
            if (view == null || cell == null) continue;
            if (cell.IsRevealed || cell.Biome != BiomeType.Forest) continue;

            bool belowRevealed = (y == 0) || gm.Grid[x, y - 1].IsRevealed;
            bool aboveRevealed = (y == gm.Height - 1) || gm.Grid[x, y + 1].IsRevealed;

            ForestCellView.TreeLayerType type;
            if (belowRevealed && aboveRevealed) type = ForestCellView.TreeLayerType.Buisson;
            else if (belowRevealed) type = ForestCellView.TreeLayerType.Trunk;
            else if (aboveRevealed) type = ForestCellView.TreeLayerType.Cime;
            else type = ForestCellView.TreeLayerType.Canopy;

            (view as ForestCellView)?.SetTreeLayer(type);
        }
    }

    // ── Refresh global ────────────────────────────────────────────────────────

    public void RefreshAllCellViews()
    {
        if (_cellViews == null) return;
        var gm = GridManager.Instance;
        if (gm == null) return;
        for (int x = 0; x < gm.Width; x++)
            for (int y = 0; y < gm.Height; y++)
                _cellViews[x, y]?.Refresh();
    }

    public IEnumerator RefreshAllCellViewsAsync(int perFrame = 50)
    {
        if (_cellViews == null) yield break;
        var gm = GridManager.Instance;
        if (gm == null) yield break;
        int count = 0;
        for (int x = 0; x < gm.Width; x++)
            for (int y = 0; y < gm.Height; y++)
            {
                _cellViews[x, y]?.Refresh();
                if (++count % perFrame == 0) yield return null;
            }
    }

    // =========================================================================
    // NETTOYAGE VISUEL
    // =========================================================================

    /// <summary>
    /// Détruit toutes les cell views et réinitialise l'état.
    /// Appelé par GridManager avant une nouvelle partie (GenerateGrid).
    /// Le null sur _cellViews force StartSpawning à réallouer proprement.
    /// </summary>
    public void ClearVisuals()
    {
        // Annuler les jobs de spawn en cours
        _spawnQueue.Clear();
        _isSpawning = false;

        // Détruire les cell views (enfants de MainGrid)
        CellViewCuller.Instance?.ResetCache();
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        // Réinitialiser le tableau — StartSpawning le réallouera avec la bonne taille
        _cellViews = null;
    }

    // =========================================================================
    // ARCHER — révélation forcée depuis surface
    // =========================================================================

    /// <summary>
    /// Révèle de force la case d'un archer caché.
    /// Appelé par ArcherWatcher quand le héros entre dans la portée d'un archer.
    /// Flux : ForceRevealArcher → ForceReveal + RefreshCellViewAt
    ///        → ForestCellView.ShowRevealed → SpawnEnemyInstance → Initialize
    ///        → SubscribeArcherEvents → StartBehaviour → CheckArcherTrigger → attaque
    /// </summary>
    public void ForceRevealArcher(int x, int y)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;

        var cell = gm.GetCell(x, y);
        if (cell == null || cell.IsRevealed) return;

        cell.ForceReveal();
        RefreshCellViewAt(x, y);
        FlushDirtyTreeColumns();

        _archerRevealBuffer.Clear();
        _archerRevealBuffer.Add(cell);
        EventBus.Publish(new OnCellsRevealed
        {
            Cells = _archerRevealBuffer,
            IsSummary = false
        });

        Debug.Log($"[MainGrid] Archer révélé de force en ({x},{y})");
    }
}