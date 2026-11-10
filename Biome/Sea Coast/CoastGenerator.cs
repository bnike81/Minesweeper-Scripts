using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// CoastGenerator -- Orchestre la generation cotiere en runtime.
///
/// Aligne sur le pattern MountainSpawner :
///   - EventBus : OnRunStarted (clear), OnGridExtended (trigger)
///   - Coroutine : attend grille + fog prets avant spawn
///   - GridManager.CellStep pour pixel-perfect
///   - Seed random pour variete des generations
///
/// Placer sur un GO separe de CoastTestGenerator dans la hierarchie.
/// </summary>
public class CoastGenerator : MonoBehaviour
{
    /// <summary>
    /// Registre de toutes les instances actives.
    /// Permet de retrouver n'importe quel CoastGenerator et de verifier
    /// si une cellule est eau pour n'importe quelle cote.
    /// </summary>
    private static readonly List<CoastGenerator> _allInstances = new List<CoastGenerator>(4);
    public static IReadOnlyList<CoastGenerator> AllInstances => _allInstances;

    [Header("Configuration")]
    [SerializeField] private CoastConfig _config;
    [SerializeField] private CoastSpriteSet _spriteSet;

    [Header("Declenchement")]
    [Tooltip("Level d'extension de grille qui declenche la generation.\n" +
             "0 = generation initiale (premier OnGridExtended).\n" +
             "Pour les cotes verticales (W/E), startPos est auto-calcule\n" +
             "pour demarrer au chunk correspondant a ce level.")]
    [SerializeField] private int _spawnAtLevel = 0;

    [Header("Rendu")]
    [SerializeField] private string _sortingLayer = "Grid";
    [SerializeField] private int _sortingOrder = 2;

    [Header("Animation")]
    [SerializeField, Range(0.1f, 5f)] private float _frameDuration = 0.8f;

    private CoastLayout _layout;
    private readonly List<GameObject> _coastObjects = new List<GameObject>(256);
    private Transform _coastContainer;
    private bool _generated = false;

    public CoastLayout Layout => _layout;
    public bool IsGenerated => _layout != null;

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (!_allInstances.Contains(this))
            _allInstances.Add(this);
    }

    private void OnDestroy()
    {
        _allInstances.Remove(this);
    }

    /// <summary>
    /// Verifie si une cellule est eau pour N'IMPORTE QUELLE cote active.
    /// Utile pour les systemes qui doivent checker toutes les cotes.
    /// </summary>
    public static bool IsCoastWaterAny(int x, int y)
    {
        for (int i = 0; i < _allInstances.Count; i++)
            if (_allInstances[i].IsCoastWater(x, y))
                return true;
        return false;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
    }

    // =========================================================================
    // EVENTBUS
    // =========================================================================

    private void OnRunStarted(OnRunStarted e)
    {
        _generated = false;
        Clear();
    }

    /// <summary>Grille initiale generee — declencher si spawnAtLevel == 0.</summary>
    private void OnGridGenerated(OnGridGenerated e)
    {
        if (_config == null || _spriteSet == null) return;
        if (_spawnAtLevel == 0 && !_generated)
        {
            StartCoroutine(SpawnCoastNextFrame());
        }
    }

    /// <summary>Extension de grille — declencher si level correspond.</summary>
    private void OnGridExtended(OnGridExtended evt)
    {
        if (_config == null || _spriteSet == null) return;
        if (evt.Level == _spawnAtLevel && !_generated)
        {
            Debug.Log("[CoastGenerator:" + gameObject.name + "] ✓ MATCH level " + evt.Level + " — lancement generation coast !");
            StartCoroutine(SpawnCoastNextFrame());
        }
    }

    // =========================================================================
    // GENERATION RUNTIME (EventBus)
    // =========================================================================

    private IEnumerator SpawnCoastNextFrame()
    {
        // Attendre que la grille et le fog soient prets
        yield return null;
        var gm = GridManager.Instance;
        if (gm == null) yield break;
        while (MainGrid.Instance?.IsSpawning == true) yield return null;
        var fog = FogOfWar.Instance;
        if (fog != null) while (fog.IsBuilding) yield return null;
        yield return null;

        if (_config == null || _spriteSet == null) yield break;

        _generated = true;
        GenerateCoast(gm);
    }

    private void GenerateCoast(GridManager gm)
    {
        Clear();

        _coastContainer = new GameObject("_CoastContent").transform;
        _coastContainer.SetParent(transform);
        _coastContainer.localPosition = Vector3.zero;

        CoastAnimator.SetFrameDuration(_frameDuration);
        CoastAnimator.ResetGlobalTimer();

        // ---------------------------------------------------------------
        // DIMENSIONS — meme logique que CoastTestGenerator :
        //   coastGridWidth  > 0 → utilise comme largeur (limite la profondeur)
        //   coastGridHeight > 0 → utilise comme longueur de la cote
        //   sinon → grille reelle (gm.Width / gm.Height)
        // ---------------------------------------------------------------
        int gw = _config.coastGridWidth > 0 ? _config.coastGridWidth : gm.Width;

        bool isVertical = _config.side == CoastSide.West
                       || _config.side == CoastSide.East
                       || _config.side == CoastSide.RandomWE;

        int startPosOverride = -1;
        int lengthOverride = -1;

        // Determiner la longueur desiree
        int desiredLength = 0;
        if (_config.length > 0)
            desiredLength = _config.length;
        else if (isVertical && _config.coastGridHeight > 0)
            desiredLength = _config.coastGridHeight;

        if (_spawnAtLevel > 0 && isVertical)
        {
            var gameManager = GameManager.Instance;
            if (gameManager != null)
            {
                int initH = gameManager.StartGridHeight;
                int ext = gameManager.GridHeightExtensionPerLevel;

                // Position de depart calculee depuis le level
                int startFromLevel = initH + (_spawnAtLevel - 2) * ext;
                startFromLevel = Mathf.Max(0, startFromLevel);

                int gh_real = gm.Height;

                // Si la longueur desiree ne rentre pas, reculer le startPos
                if (desiredLength > 0 && startFromLevel + desiredLength > gh_real)
                {
                    int adjusted = Mathf.Max(0, gh_real - desiredLength);
                    Debug.Log("[CoastGenerator:" + gameObject.name +
                              "] Ajustement startPos: " + startFromLevel + " → " + adjusted +
                              " pour accueillir length=" + desiredLength +
                              " dans grille h=" + gh_real);
                    startFromLevel = adjusted;
                }

                startPosOverride = startFromLevel;

                if (desiredLength > 0)
                    lengthOverride = desiredLength;
            }
        }
        else if (desiredLength > 0)
        {
            // Pas d'auto-position, mais longueur specifiee
            lengthOverride = desiredLength;
        }

        // Hauteur de grille : toujours la grille reelle (les tableaux doivent couvrir
        // toutes les cellules accessibles), le layout ne remplit que la zone start..start+len
        int gh = gm.Height;

        // Diagnostic complet
        Debug.Log("[CoastGenerator:" + gameObject.name + "] CONFIG=" + _config.name +
                  " side=" + _config.side +
                  " minDepth=" + _config.minDepth +
                  " maxDepth=" + _config.maxDepth +
                  " expand=" + _config.expandChance +
                  " retract=" + _config.retractChance +
                  " seed=" + _config.seed +
                  " coastGridW=" + _config.coastGridWidth +
                  " coastGridH=" + _config.coastGridHeight +
                  " | effectiveGrid=" + gw + "x" + gh +
                  " startPos=" + startPosOverride +
                  " length=" + lengthOverride);

        _layout = CoastLayout.CreateAndGenerate(_config, gw, gh, startPosOverride, lengthOverride);

        MarkCellsAsCoastWater(gm);
        SpawnPieces(gm);

        LogPieceCounts();
    }

    /// <summary>
    /// Marque les cellules de la grille comme coast water.
    /// Suit le pattern IsMountainReserved : bloque spawn, reveal, mouvement.
    /// </summary>
    private void MarkCellsAsCoastWater(GridManager gm)
    {
        if (gm == null || _layout == null) return;

        int marked = 0;
        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                if (!_layout.IsWater[x, y]) continue;
                var cell = gm.GetCell(x, y);
                if (cell != null)
                {
                    cell.IsCoastWater = true;
                    marked++;
                }
            }

        Debug.Log("[CoastGenerator:" + gameObject.name + "] " + marked + " cellules marquees IsCoastWater");
    }

    // =========================================================================
    // GENERATION MANUELLE (compatibilite externe)
    // =========================================================================

    /// <summary>
    /// Generation manuelle externe (pour compatibilite).
    /// En runtime, preferer le declenchement automatique via EventBus.
    /// </summary>
    public void Generate(int gridWidth, int gridHeight, float cellStep)
    {
        Clear();

        if (_config == null)
        {
            Debug.LogWarning("[CoastGenerator] Pas de CoastConfig assigne !");
            return;
        }
        if (_spriteSet == null)
        {
            Debug.LogWarning("[CoastGenerator] Pas de CoastSpriteSet assigne !");
            return;
        }

        _coastContainer = new GameObject("_CoastContent").transform;
        _coastContainer.SetParent(transform);
        _coastContainer.localPosition = Vector3.zero;

        CoastAnimator.SetFrameDuration(_frameDuration);
        CoastAnimator.ResetGlobalTimer();

        // Dimensions : config overrides > parametres
        int gw = _config.coastGridWidth > 0 ? _config.coastGridWidth : gridWidth;
        int gh = _config.coastGridHeight > 0 ? _config.coastGridHeight : gridHeight;

        // Meme chemin que CoastTestGenerator — factory partagee
        _layout = CoastLayout.CreateAndGenerate(_config, gw, gh);

        MarkCellsAsCoastWater(GridManager.Instance);
        SpawnPieces(GridManager.Instance);

        _generated = true;
        LogPieceCounts();
    }

    public void Generate(CoastConfig config, int gridWidth, int gridHeight, float cellStep)
    {
        _config = config;
        Generate(gridWidth, gridHeight, cellStep);
    }

    // =========================================================================
    // CLEAR
    // =========================================================================

    public void Clear()
    {
        foreach (var go in _coastObjects)
        {
            if (go == null) continue;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        _coastObjects.Clear();

        if (_coastContainer != null)
        {
            if (Application.isPlaying) Destroy(_coastContainer.gameObject);
            else DestroyImmediate(_coastContainer.gameObject);
            _coastContainer = null;
        }

        // Filet de securite : detruire tout enfant orphelin (reference perdue apres domain reload)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith("_CoastContent"))
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        _layout = null;
    }

    // =========================================================================
    // QUERY
    // =========================================================================

    public bool IsCoastWater(int x, int y)
    {
        if (_layout == null) return false;
        if (x < 0 || x >= _layout.GridWidth || y < 0 || y >= _layout.GridHeight) return false;
        return _layout.IsWater[x, y];
    }

    // =========================================================================
    // SPAWN
    // =========================================================================

    private void SpawnPieces(GridManager gm)
    {
        // Detecter les types sans sprites pour avertir
        var missingTypes = new HashSet<CoastCellType>();

        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                var type = _layout.CellTypes[x, y];
                if (type == CoastCellType.None) continue;

                var frames = _spriteSet.GetFrames(type);

                // Verifier sprites valides (pas juste le tableau, le contenu aussi)
                bool hasValidSprites = frames != null && frames.Length > 0
                                       && frames[0] != null;

                if (!hasValidSprites)
                {
                    missingTypes.Add(type);
                    continue;
                }

                // Positionnement identique a la grille — via GridManager.GridToWorld
                Vector3 pos = gm != null
                    ? gm.GridToWorld(x, y)
                    : FallbackGridToWorld(x, y);

                var go = new GameObject("Coast_" + type + "_" + x + "_" + y);
                go.transform.SetParent(_coastContainer);
                go.transform.position = pos;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = frames[0];
                sr.sortingLayerName = _sortingLayer;
                sr.sortingOrder = _sortingOrder;

                var anim = go.AddComponent<CoastAnimator>();
                anim.Setup(frames, sr);

                _coastObjects.Add(go);
            }

        // Avertir des types sans sprites
        if (missingTypes.Count > 0)
        {
            var sb = new System.Text.StringBuilder(
                "[CoastGenerator:" + gameObject.name + "] SPRITES MANQUANTS pour : ");
            foreach (var t in missingTypes)
                sb.Append(t).Append(", ");
            sb.Length -= 2;
            sb.Append(" — ces pieces sont dans le Layout mais invisibles en jeu !");
            Debug.LogWarning(sb.ToString());
        }
    }

    /// <summary>Fallback pixel-perfect quand GridManager n'est pas disponible.</summary>
    private static Vector3 FallbackGridToWorld(int x, int y)
    {
        int stepPx = Mathf.RoundToInt(1.0625f * 16f); // 17
        return new Vector3((x * stepPx) / 16f, (y * stepPx) / 16f, 0f);
    }

    /// <summary>Log identique a CoastTestGenerator pour comparaison facile.</summary>
    private void LogPieceCounts()
    {
        if (_layout == null) return;

        // Depths
        var sb = new System.Text.StringBuilder("[CoastGenerator:" + gameObject.name + "] Depths: ");
        for (int i = 0; i < _layout.Depths.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(_layout.Depths[i]);
        }
        Debug.Log(sb.ToString());

        // Piece counts
        int waters = 0, edges = 0, corners = 0, inners = 0;
        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                var t = _layout.CellTypes[x, y];
                if (t == CoastCellType.Water) waters++;
                else if (t >= CoastCellType.EdgeS && t <= CoastCellType.EdgeW) edges++;
                else if (t >= CoastCellType.CornerNW && t <= CoastCellType.CornerSE) corners++;
                else if (t >= CoastCellType.InnerNW && t <= CoastCellType.InnerSE) inners++;
            }

        Debug.Log("[CoastGenerator:" + gameObject.name + "] Pieces: Water=" + waters +
                  " Edge=" + edges + " Corner=" + corners + " Inner=" + inners +
                  " GO=" + _coastObjects.Count);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_layout == null) return;

        var gm = GridManager.Instance;
        float cs = gm != null ? gm.CellStep : 1f;
        int stepPx = Mathf.RoundToInt(cs * 16f);

        for (int x = 0; x < _layout.GridWidth; x++)
            for (int y = 0; y < _layout.GridHeight; y++)
            {
                if (!_layout.IsWater[x, y]) continue;

                var type = _layout.CellTypes[x, y];
                switch (type)
                {
                    case CoastCellType.Water:
                        Gizmos.color = new Color(0f, 0.3f, 1f, 0.4f); break;
                    case CoastCellType.EdgeS:
                    case CoastCellType.EdgeN:
                    case CoastCellType.EdgeE:
                    case CoastCellType.EdgeW:
                        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f); break;
                    case CoastCellType.CornerNW:
                    case CoastCellType.CornerNE:
                    case CoastCellType.CornerSW:
                    case CoastCellType.CornerSE:
                        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f); break;
                    case CoastCellType.InnerNW:
                    case CoastCellType.InnerNE:
                    case CoastCellType.InnerSW:
                    case CoastCellType.InnerSE:
                        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f); break;
                    default:
                        Gizmos.color = Color.clear; break;
                }

                Vector3 center = new Vector3((x * stepPx) / 16f, (y * stepPx) / 16f, 0f);
                Gizmos.DrawCube(center, Vector3.one * cs * 0.9f);
            }
    }
#endif
}