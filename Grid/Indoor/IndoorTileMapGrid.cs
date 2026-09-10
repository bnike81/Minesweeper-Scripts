using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// IndoorTilemapGrid — Même système de coordonnées que MainGrid.
///
/// DÉTECTION AUTO DE LA PORTE :
///   Scan le bord bas du tilemap pour trouver le trou dans le mur (case sol sans mur).
///   Pas besoin de régler DoorTilePos à la main.
///
/// COORDONNÉES :
///   Après AlignToOutdoorDoor, l'offset (_ox, _oy) place la porte à la même
///   position monde que la porte de la cabane en MainGrid.
///   GridToWorld(x,y) = (x*cs + _ox, y*cs + _oy)
///   WorldToGrid(w)   = (round((w.x-_ox)/cs), round((w.y-_oy)/cs))
/// </summary>
public class IndoorTilemapGrid : MonoBehaviour, IGridContext
{
    [Header("Tilemaps (assigner dans l'Inspector)")]
    [SerializeField] private Tilemap _background;
    [SerializeField] private Tilemap _wallBackground;
    [SerializeField] private Tilemap _collisions;

    [Header("Porte")]
    [Tooltip("Coordonnées de la porte dans le tilemap (en cellules absolues tilemap).\n" +
             "Laissez (0,0) pour auto-détection du trou dans le bord bas.")]
    [SerializeField] private Vector2Int _doorTilePos;
    [SerializeField, Range(0, 3)] private int _spawnOffset = 1;

    [Header("Correction d'alignement")]
    [Tooltip("Décalage constant entre la porte indoor et la porte outdoor.\n" +
             "Si l'intérieur est décalé de +5 en X et +2 en Y par rapport à la porte,\n" +
             "mettre (5, 2) ici. Le tilemap sera shifté de (-5, -2) cases.")]
    [SerializeField] private Vector2Int _alignCorrection = Vector2Int.zero;

    // ── Données ───────────────────────────────────────────────────────────────
    private float _cs;          // CellStep (= GridManager.CellStep)
    private float _ox, _oy;    // offset monde appliqué par AlignToOutdoorDoor
    private BoundsInt _bounds;
    private Cell[,] _cells;

    // ── IGridContext ──────────────────────────────────────────────────────────
    public int Width => _bounds.size.x;
    public int Height => _bounds.size.y;
    public float CellStep => _cs;
    public float CellSpacing => GridManager.Instance?.CellSpacing ?? 0.05f;
    public float CellSize => GridManager.Instance?.CellSize ?? 1f;

    public bool IsReady { get; private set; }
    public Vector2Int DoorTilePos { get; private set; }
    public Vector2Int SurfaceReturnPos { get; private set; }
    public void SetSurfaceReturnPos(Vector2Int p) => SurfaceReturnPos = p;

    /// <summary>Porte effective (auto-détectée + correction d'alignement).</summary>
    public Vector2Int EffectiveDoor =>
        new Vector2Int(DoorTilePos.x + _alignCorrection.x,
                       DoorTilePos.y + _alignCorrection.y);

    /// <summary>Vérifie si la position grille est la porte de sortie.</summary>
    public bool IsEffectiveDoor(Vector2Int gridPos) =>
        gridPos.x == EffectiveDoor.x && gridPos.y == EffectiveDoor.y;

    /// <summary>Position de spawn = porte effective + offset vers l'intérieur.</summary>
    public Vector3 HeroSpawnWorldPos
    {
        get
        {
            int ex = DoorTilePos.x + _alignCorrection.x;
            int ey = DoorTilePos.y + _alignCorrection.y + _spawnOffset;
            return GridToWorld(ex, ey);
        }
    }
    public Vector2Int HeroSpawnGrid
    {
        get
        {
            int ex = DoorTilePos.x + _alignCorrection.x;
            int ey = DoorTilePos.y + _alignCorrection.y + _spawnOffset;
            return new Vector2Int(ex, ey);
        }
    }

    // ── Coordonnées ───────────────────────────────────────────────────────────

    public Vector3 GridToWorld(int gx, int gy, float z = 0f) =>
        new Vector3(gx * _cs + _ox, gy * _cs + _oy, z);

    public Vector2Int WorldToGrid(Vector3 w) =>
        new Vector2Int(
            Mathf.RoundToInt((w.x - _ox) / _cs),
            Mathf.RoundToInt((w.y - _oy) / _cs));

    // ── Grille ────────────────────────────────────────────────────────────────

    public Cell GetCell(int x, int y)
    {
        if (_cells == null || !IsInBounds(x, y)) return null;
        return _cells[x - _bounds.xMin, y - _bounds.yMin];
    }

    public bool IsInBounds(int x, int y) =>
        _cells != null &&
        x >= _bounds.xMin && x < _bounds.xMax &&
        y >= _bounds.yMin && y < _bounds.yMax;

    public bool IsBlocked(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;

        // La porte effective est toujours marchable (sinon le héros ne peut pas y aller)
        var door = EffectiveDoor;
        if (x == door.x && y == door.y) return false;

        var tp = new Vector3Int(x, y, 0);
        if (_wallBackground?.GetTile(tp) != null) return true;
        if (_collisions?.GetTile(tp) != null) return true;
        if (_background?.GetTile(tp) == null) return true;
        return false;
    }

    public RevealResult RevealCell(int x, int y)
    {
        // Utiliser la porte effective (avec correction d'alignement)
        var door = EffectiveDoor;
        if (x == door.x && y == door.y)
        {
            IndoorManager.Instance?.ExitBuilding(x, y);
            return RevealResult.Empty;
        }
        return RevealResult.AlreadyRevealed;
    }

    // =========================================================================
    // ALIGNEMENT sur la porte outdoor
    // =========================================================================

    public void AlignToOutdoorDoor(Vector2Int outdoorDoor)
    {
        _cs = GridManager.Instance?.CellStep ?? 1.063f;

        // S'assurer que la grille est construite (avec auto-détection porte)
        if (_cells == null) BuildGrid();

        // Position monde cible = position outdoor de la porte
        float targetX = outdoorDoor.x * _cs;
        float targetY = outdoorDoor.y * _cs;

        // Porte effective = auto-détectée + correction
        int effDoorX = DoorTilePos.x + _alignCorrection.x;
        int effDoorY = DoorTilePos.y + _alignCorrection.y;

        // On veut : GridToWorld(effDoorX, effDoorY) = (targetX, targetY)
        _ox = targetX - effDoorX * _cs;
        _oy = targetY - effDoorY * _cs;

        // Repositionner le Unity Grid
        var gridTf = _background?.transform.parent;
        if (gridTf != null)
            gridTf.position = new Vector3(_ox, _oy, 0f);

        Debug.Log($"[IndoorTilemapGrid] Aligné : porte auto={DoorTilePos} " +
                  $"correction=({_alignCorrection.x},{_alignCorrection.y}) " +
                  $"effective=({effDoorX},{effDoorY}) " +
                  $"→ world({targetX:F2},{targetY:F2}), offset=({_ox:F2},{_oy:F2})");
        Debug.Log($"[IndoorTilemapGrid] HeroSpawn grid={HeroSpawnGrid} " +
                  $"world={HeroSpawnWorldPos}");
    }

    // =========================================================================
    // CONSTRUCTION DE LA GRILLE
    // =========================================================================

    private void Awake() => BuildIfNeeded();
    private void OnEnable() => BuildIfNeeded();

    private void BuildIfNeeded()
    {
        if (_background != null && _cells == null)
            BuildGrid();
    }

    public void BuildGrid()
    {
        if (_background == null)
        {
            Debug.LogError("[IndoorTilemapGrid] ❌ Background Tilemap non assigné !");
            return;
        }

        _cs = GridManager.Instance?.CellStep ?? 1.063f;

        // Lire l'offset courant du Grid Unity
        var gridTf = _background.transform.parent;
        if (gridTf != null) { _ox = gridTf.position.x; _oy = gridTf.position.y; }

        // Comprimer les bounds
        _background.CompressBounds();
        _wallBackground?.CompressBounds();
        _collisions?.CompressBounds();

        _bounds = _background.cellBounds;
        void Expand(Tilemap tm)
        {
            if (tm == null) return;
            var b = tm.cellBounds;
            _bounds.xMin = Mathf.Min(_bounds.xMin, b.xMin);
            _bounds.yMin = Mathf.Min(_bounds.yMin, b.yMin);
            _bounds.xMax = Mathf.Max(_bounds.xMax, b.xMax);
            _bounds.yMax = Mathf.Max(_bounds.yMax, b.yMax);
        }
        Expand(_wallBackground);
        Expand(_collisions);

        int w = _bounds.size.x, h = _bounds.size.y;
        if (w <= 0 || h <= 0)
        {
            Debug.LogWarning("[IndoorTilemapGrid] Tilemaps vides !");
            return;
        }

        // Créer les cells
        _cells = new Cell[w, h];
        for (int xi = 0; xi < w; xi++)
            for (int yi = 0; yi < h; yi++)
            {
                var cell = new Cell(xi + _bounds.xMin, yi + _bounds.yMin, BiomeType.None);
                cell.ForceReveal();
                _cells[xi, yi] = cell;
            }

        // ── Auto-détecter la porte ────────────────────────────────────────────
        DoorTilePos = _doorTilePos != Vector2Int.zero
            ? _doorTilePos          // valeur manuelle dans l'Inspector
            : AutoDetectDoor();     // scan automatique du bord bas

        IsReady = true;

        Debug.Log($"[IndoorTilemapGrid] ✅ {w}×{h} cases. " +
                  $"Bounds=({_bounds.xMin},{_bounds.yMin})→({_bounds.xMax},{_bounds.yMax}) " +
                  $"Porte={DoorTilePos} cs={_cs:F3}");
    }

    /// <summary>
    /// Cherche le trou dans le bord bas du tilemap :
    /// case avec Background mais SANS WallBackground ni Collisions.
    /// </summary>
    private Vector2Int AutoDetectDoor()
    {
        int y = _bounds.yMin; // bord bas
        for (int x = _bounds.xMin; x < _bounds.xMax; x++)
        {
            var tp = new Vector3Int(x, y, 0);
            bool hasSol = _background?.GetTile(tp) != null;
            bool hasWall = _wallBackground?.GetTile(tp) != null
                        || _collisions?.GetTile(tp) != null;
            if (hasSol && !hasWall)
            {
                Debug.Log($"[IndoorTilemapGrid] Porte auto-détectée en ({x},{y})");
                return new Vector2Int(x, y);
            }
        }

        // Fallback : centre du bord bas
        int cx = (_bounds.xMin + _bounds.xMax) / 2;
        Debug.LogWarning($"[IndoorTilemapGrid] Aucune porte trouvée, fallback centre ({cx},{y})");
        return new Vector2Int(cx, y);
    }

    // =========================================================================
    // UPDATE — Tooltip porte
    // =========================================================================

    private bool _tooltipShown;

    private void Update()
    {
        if (!IsReady || IndoorManager.Instance == null || !IndoorManager.Instance.IsIndoor)
        {
            if (_tooltipShown) { TooltipUI.Hide(); _tooltipShown = false; }
            return;
        }

        var cam = Camera.main;
        if (cam == null) return;

        // Position monde de la souris
        Vector3 mouseScreen = Input.mousePosition;
        float camDist = Mathf.Abs(cam.transform.position.z);
        Vector3 mouseWorld = cam.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, camDist));

        // Convertir en coordonnées grille indoor
        Vector2Int mouseGrid = WorldToGrid(mouseWorld);

        if (IsEffectiveDoor(mouseGrid))
        {
            if (!_tooltipShown)
            {
                TooltipUI.Show("Sortir de la cabane");
                _tooltipShown = true;
            }
        }
        else if (_tooltipShown)
        {
            TooltipUI.Hide();
            _tooltipShown = false;
        }
    }

    // =========================================================================
    // SHOW / HIDE
    // =========================================================================

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}