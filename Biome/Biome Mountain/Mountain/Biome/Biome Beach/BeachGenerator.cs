using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// BeachGenerator — Génère les îlots plage et re-spawne les cell views.
///
/// Écoute OnGridExtended, attend que tout soit prêt (montagnes + spawn),
/// peint BiomeType.Beach, détruit les ForestCellView, crée des BeachCellView.
///
/// AUCUNE MODIFICATION de GridManager ou MainGrid nécessaire.
/// </summary>
public class BeachGenerator : MonoBehaviour
{
    public static BeachGenerator Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private BeachRecipe _recipe;

    [Header("Références")]
    [SerializeField] private BiomeDatabase _biomeDatabase;

    private System.Random _rng;
    private Vector2 _perlinOffset;
    private bool _initialized;
    private readonly List<Vector2Int> _changedCells = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
    }

    private void OnRunStarted(OnRunStarted _)
    {
        int seed = (_recipe != null && _recipe.Seed != 0)
            ? _recipe.Seed : Random.Range(1, 99999);
        _rng = new System.Random(seed);
        _perlinOffset = new Vector2(
            (float)_rng.NextDouble() * 1000f,
            (float)_rng.NextDouble() * 1000f);
        _initialized = true;
    }

    // =========================================================================
    // DÉCLENCHEMENT
    // =========================================================================

    /// <summary>Grille initiale générée — vérifier si un îlot est au level 0.</summary>
    private void OnGridGenerated(OnGridGenerated e)
    {
        if (!_initialized || _recipe == null || _recipe.Islands == null) return;

        bool hasIsland = false;
        foreach (var island in _recipe.Islands)
            if (island.spawnAtLevel == 0) { hasIsland = true; break; }

        if (hasIsland)
            StartCoroutine(ApplyBeachDelayed(0));
    }

    /// <summary>Chunk étendu — vérifier si un îlot couvre ce level.</summary>
    private void OnGridExtended(OnGridExtended e)
    {
        if (!_initialized || _recipe == null || _recipe.Islands == null) return;

        int chunkH = GameManager.Instance?.GridHeightExtensionPerLevel ?? 16;

        bool hasIsland = false;
        foreach (var island in _recipe.Islands)
        {
            // L'îlot peut couvrir plusieurs chunks — vérifier si ce level est dans la zone
            int spawnLevel = island.spawnAtLevel;
            int chunksSpan = Mathf.CeilToInt(island.radiusY / (float)chunkH) + 1;

            if (e.Level >= spawnLevel && e.Level <= spawnLevel + chunksSpan)
            { hasIsland = true; break; }
        }

        if (hasIsland)
            StartCoroutine(ApplyBeachDelayed(e.Level));
    }

    private IEnumerator ApplyBeachDelayed(int level)
    {
        // Attendre fin du spawn MainGrid
        while (MainGrid.Instance != null && MainGrid.Instance.IsSpawning)
            yield return null;

        // Attendre que les montagnes soient COMPLÈTEMENT placées
        // MountainSpawner attend aussi IsSpawning puis place les montagnes
        // → 5 frames suffisent pour que Reserve + ReservePlateau aient tourné
        for (int i = 0; i < 5; i++)
            yield return null;

        var gm = GridManager.Instance;
        if (gm == null || gm.Grid == null) yield break;

        int chunkH = GameManager.Instance?.GridHeightExtensionPerLevel ?? 16;

        _changedCells.Clear();
        ApplyBeachBiomes(gm.Grid, gm.Width, gm.Height, level, chunkH);

        if (_changedCells.Count > 0)
        {
            // Remplacer les ennemis forêt par ennemis plage AVANT re-spawn
            ReplaceEnemiesOnBeachCells(gm);
            RespawnBeachCells(gm);
        }
    }

    /// <summary>
    /// Remplace les ennemis forêt par des ennemis plage sur les cells changées.
    /// PlaceEnemiesRandom a déjà placé des loups/ours — on les remplace par des crabes.
    /// </summary>
    private void ReplaceEnemiesOnBeachCells(GridManager gm)
    {
        var biomeDb = _biomeDatabase ?? gm.BiomeDatabaseAsset;
        var beachTable = biomeDb?.GetEnemyTable(BiomeType.Beach);
        int currentLevel = gm.CurrentLevel;
        var beachConfig = beachTable?.GetConfig(currentLevel);

        if (beachConfig == null)
        {
            Debug.LogWarning("[BeachGenerator] ⚠ Pas de BeachSpawnTable pour ce level");
            return;
        }

        int replaced = 0;
        foreach (var pos in _changedCells)
        {
            if (!gm.IsInBounds(pos.x, pos.y)) continue;
            var cell = gm.Grid[pos.x, pos.y];
            if (cell == null) continue;

            bool isEnemy = cell.Content != CellContent.Empty
                        && cell.Content != CellContent.Number
                        && cell.Content != CellContent.Trap;

            if (isEnemy)
            {
                cell.Content = beachConfig.Roll();
                replaced++;
            }
        }

        if (replaced > 0)
            MinesweeperLogic.ComputeAllAdjacencies(gm.Grid, gm.Width, gm.Height);

        Debug.Log($"[BeachGenerator] {replaced} ennemis → beach enemies");
    }

    // =========================================================================
    // RE-SPAWN — remplace ForestCellView par BeachCellView
    // =========================================================================

    private void RespawnBeachCells(GridManager gm)
    {
        var biomeDb = _biomeDatabase ?? gm.BiomeDatabaseAsset;
        if (biomeDb == null)
        {
            Debug.LogError("[BeachGenerator] ❌ BiomeDatabase non trouvée !");
            return;
        }

        var beachPrefab = biomeDb.GetCellPrefab(BiomeType.Beach);
        if (beachPrefab == null)
        {
            Debug.LogError("[BeachGenerator] ❌ Beach prefab non trouvé dans BiomeDatabase !");
            return;
        }

        var mainGrid = MainGrid.Instance;
        float cellStep = gm.CellStep;
        int stepPixels = Mathf.RoundToInt(cellStep * 16f);
        int respawned = 0;
        var dirtyColumns = new HashSet<int>();

        foreach (var pos in _changedCells)
        {
            int x = pos.x, y = pos.y;
            if (!gm.IsInBounds(x, y)) continue;

            var cell = gm.Grid[x, y];
            if (cell == null || cell.Biome != BiomeType.Beach) continue;

            // Détruire l'ancien cell view
            ICellView oldView = mainGrid?.GetCellView(x, y);
            if (oldView != null)
            {
                var oldMB = oldView as MonoBehaviour;
                if (oldMB != null) Destroy(oldMB.gameObject);
            }

            // Créer le BeachCellView
            float wx = (x * stepPixels) / 16f;
            float wy = (y * stepPixels) / 16f;
            Transform parent = mainGrid != null ? mainGrid.transform : transform;
            var go = Instantiate(beachPrefab, new Vector3(wx, wy, 0f),
                                Quaternion.identity, parent);

            // Initialiser
            var beachView = go.GetComponent<BeachCellView>();
            if (beachView != null)
            {
                beachView.Initialize(cell);
                mainGrid?.SetCellView(x, y, beachView);

                // ── Assigner TreeLayer directement ────────────────────────
                bool belowRevealed = (y == 0) || gm.Grid[x, y - 1].IsRevealed;
                bool aboveRevealed = (y >= gm.Height - 1) || gm.Grid[x, y + 1].IsRevealed;

                ForestCellView.TreeLayerType treeType;
                if (belowRevealed && aboveRevealed)
                    treeType = ForestCellView.TreeLayerType.Buisson;
                else if (belowRevealed)
                    treeType = ForestCellView.TreeLayerType.Trunk;
                else if (aboveRevealed)
                    treeType = ForestCellView.TreeLayerType.Cime;
                else
                    treeType = ForestCellView.TreeLayerType.Canopy;

                beachView.SetTreeLayer(treeType);

                // ── Transition canopée plage→forêt ────────────────────────
                if (treeType == ForestCellView.TreeLayerType.Canopy)
                {
                    bool treeAbove = (y + 1 < gm.Height
                        && !gm.Grid[x, y + 1].IsRevealed
                        && gm.Grid[x, y + 1].Biome == BiomeType.Forest);
                    bool treeBelow = (y - 1 >= 0
                        && !gm.Grid[x, y - 1].IsRevealed
                        && gm.Grid[x, y - 1].Biome == BiomeType.Forest);

                    if (treeAbove)
                        beachView.SetCanopyTransition(true, isAbove: true);
                    else if (treeBelow)
                        beachView.SetCanopyTransition(true, isAbove: false);
                }
            }
            else
            {
                // Fallback CellViewBase
                var baseView = go.GetComponent<CellViewBase>();
                if (baseView != null)
                {
                    baseView.Initialize(cell);
                    mainGrid?.SetCellView(x, y, baseView);
                }
            }

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                CellViewCuller.Instance?.RegisterCell(x, y, sr);
                SpriteBatchingSetup.Instance?.ApplySharedMaterial(sr);
            }

            dirtyColumns.Add(x);
            respawned++;
        }

        // Rafraîchir les ForestCellView voisines (transition canopée côté forêt)
        foreach (int x in dirtyColumns)
            mainGrid?.RefreshTreeColumnPublic(x);

        Debug.Log($"[BeachGenerator] ✅ {respawned} cells re-spawnées en Beach");
    }

    // =========================================================================
    // PEINTURE DES BIOMES
    // =========================================================================

    private void ApplyBeachBiomes(Cell[,] grid, int width, int height,
                                  int level, int chunkHeight)
    {
        foreach (var island in _recipe.Islands)
        {
            // L'îlot couvre spawnAtLevel + N chunks selon radiusY
            int spawnLevel = island.spawnAtLevel;
            int chunksSpan = Mathf.CeilToInt(island.radiusY / (float)chunkHeight) + 1;

            if (level >= spawnLevel && level <= spawnLevel + chunksSpan)
                GenerateIsland(grid, width, height, island, island.spawnAtLevel, chunkHeight);
        }
    }

    private void GenerateIsland(Cell[,] grid, int gridW, int gridH,
                                BeachRecipe.BeachIsland island,
                                int level, int chunkHeight)
    {
        int centerX;
        switch (island.anchor)
        {
            case BeachRecipe.IslandAnchor.Left:
                centerX = island.radiusX + island.marginFromEdge;
                break;
            case BeachRecipe.IslandAnchor.Right:
                centerX = gridW - island.radiusX - island.marginFromEdge - 1;
                break;
            case BeachRecipe.IslandAnchor.Center:
                centerX = gridW / 2;
                break;
            default:
                centerX = _rng.Next(island.radiusX + 2, gridW - island.radiusX - 2);
                break;
        }

        if (island.centerXVariation > 0)
            centerX += _rng.Next(-island.centerXVariation, island.centerXVariation + 1);
        centerX = Mathf.Clamp(centerX, island.marginFromEdge + 1,
                              gridW - island.marginFromEdge - 2);

        int chunkBaseY = level * chunkHeight;
        int centerY = chunkBaseY + Mathf.RoundToInt(island.yPositionInChunk * chunkHeight);

        if (island.centerYVariation > 0)
            centerY += _rng.Next(-island.centerYVariation, island.centerYVariation + 1);
        centerY = Mathf.Clamp(centerY, 1, gridH - 2);

        float islandOffX = _perlinOffset.x + level * 137.5f;
        float islandOffY = _perlinOffset.y + level * 89.3f;

        int painted = 0;
        int rx = island.radiusX;
        int ry = island.radiusY;

        for (int dx = -rx; dx <= rx; dx++)
            for (int dy = -ry; dy <= ry; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if (x < 0 || x >= gridW || y < 0 || y >= gridH) continue;

                float distNorm = Mathf.Sqrt(
                    (dx * dx) / (float)(rx * rx) +
                    (dy * dy) / (float)(ry * ry));

                if (distNorm > 1f) continue;

                float perlin = Mathf.PerlinNoise(
                    islandOffX + x * island.perlinScale,
                    islandOffY + y * island.perlinScale);

                float threshold = island.perlinThreshold + distNorm * 0.25f;
                if (perlin < threshold) continue;

                if (x < island.marginFromEdge || x >= gridW - island.marginFromEdge)
                    continue;

                var cell = grid[x, y];
                if (cell == null) continue;
                if (cell.IsMountainReserved) continue;
                if (cell.Biome == BiomeType.Beach) continue;

                if (IsNearMountain(grid, gridW, gridH, x, y, island.marginFromMountain))
                    continue;

                cell.Biome = BiomeType.Beach;
                _changedCells.Add(new Vector2Int(x, y));
                painted++;
            }

        Debug.Log($"[BeachGenerator] Îlot level={level} centre=({centerX},{centerY}) " +
                  $"rayon=({rx},{ry}) peint={painted}");
    }

    private bool IsNearMountain(Cell[,] grid, int w, int h, int x, int y, int margin)
    {
        for (int dx = -margin; dx <= margin; dx++)
            for (int dy = -margin; dy <= margin; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                var c = grid[nx, ny];
                if (c != null && (c.IsMountainReserved || c.IsMountainPlateau))
                    return true;
            }
        return false;
    }
}