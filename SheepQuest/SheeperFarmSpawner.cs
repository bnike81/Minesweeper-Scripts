using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ShepherdFarmSpawner - Place la ferme du vieux berger et les moutons.
/// _spawnAtLevel contrôle le chunk de spawn (1=grille initiale, 2+=extension).
/// Avec prologue : _spawnAtLevel=2, ferme au premier chunk d'extension.
/// </summary>
public class ShepherdFarmSpawner : MonoBehaviour
{
    public static ShepherdFarmSpawner Instance { get; private set; }

    [Header("=== Prefabs NPC ===")]
    [SerializeField] private GameObject _shepherdPrefab;
    [SerializeField] private GameObject _sheepPrefab;

    [Header("=== Prefabs Decors Ferme ===")]
    [SerializeField] private GameObject _cabinPrefab;
    [SerializeField] private GameObject _barrelPrefab;
    [SerializeField] private GameObject _haySmallPrefab;
    [SerializeField] private GameObject _hayBigPrefab;

    [Header("--- Ferme : Zone de Recherche ---")]
    [SerializeField] private int _minFarmZoneSize = 4;
    [SerializeField, Range(1, 5)] private int _edgeMargin = 2;
    [SerializeField, Range(1f, 8f)] private float _farmProtectionRadius = 3.5f;

    [Header("--- Ferme : Biais de zone ---")]
    [SerializeField, Range(0f, 1f)] private float _cornerBias = 0.30f;
    [SerializeField, Range(0f, 1f)] private float _edgeBias = 0.35f;
    [SerializeField, Range(0.1f, 0.5f)] private float _cornerZoneRatio = 0.30f;
    [SerializeField, Range(0.1f, 0.4f)] private float _edgeZoneRatio = 0.20f;

    [Header("--- Moutons ---")]
    [SerializeField, Range(2, 10)] private int _sheepMinDistFromFarm = 3;
    [SerializeField, Range(3, 15)] private int _sheepMaxDistFromFarm = 8;
    [SerializeField, Range(2, 8)] private int _sheepMinDistBetween = 4;
    [SerializeField, Range(0.5f, 3f)] private float _sheepProtectionRadius = 1.5f;
    [SerializeField, Range(1, 4)] private int _sheepEnemySafeRadius = 2;
    [SerializeField, Range(30, 150)] private int _sheepPlacementMaxTries = 100;

    [Header("--- Berger ---")]
    [SerializeField, Range(1, 4)] private int _shepherdMaxDist = 2;

    [Header("--- Tailles ---")]
    [SerializeField, Range(0.5f, 3f)] private float _cabinScale = 1.5f;
    [SerializeField, Range(0.2f, 1.5f)] private float _accessoryScale = 0.5f;
    [SerializeField, Range(0.2f, 2f)] private float _shepherdScale = 0.5f;
    [SerializeField, Range(0.2f, 2f)] private float _sheepScale = 0.5f;

    [Header("--- Graine ---")]
    [SerializeField] private int _seed = 0;

    [Header("--- Level de spawn ---")]
    [Tooltip("1=grille initiale, 2+=extension. Prologue:2")]
    [SerializeField] private int _spawnAtLevel = 2;

    // État interne
    private System.Random _rng;
    private float _cellStep;
    private Vector2Int _farmOrigin;
    private List<Vector2Int> _farmCells;
    private List<GameObject> _spawnedObjects = new();
    private List<Vector2Int> _allSheepPositions = new();
    private bool _farmReserved;
    private bool _farmSpawned;

    public Vector2Int FarmOrigin => _farmOrigin;
    public Vector3 FarmWorldPos => new Vector3(
        (_farmOrigin.x + 1) * (_cellStep > 0 ? _cellStep : 1.05f),
        (_farmOrigin.y + 1) * (_cellStep > 0 ? _cellStep : 1.05f), 0f);

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridDataReady>(OnGridDataReady);
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtending>(OnGridExtending);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridDataReady>(OnGridDataReady);
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Unsubscribe<OnGridExtending>(OnGridExtending);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        int seed = _seed != 0 ? _seed : UnityEngine.Random.Range(1, 99999);
        _rng = new System.Random(seed);
        _farmCells = new List<Vector2Int>();
        _allSheepPositions = new List<Vector2Int>();
        _farmReserved = false;
        _farmSpawned = false;
        ClearSpawned();
    }

    // =========================================================================
    // EVENTS GRILLE
    // =========================================================================

    private void OnGridDataReady(OnGridDataReady evt)
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        if (_spawnAtLevel > 1) return;

        Vector2Int? origin = FindFarmZone(evt.Width, evt.Height);
        _farmOrigin = origin ?? new Vector2Int(evt.Width / 2 - 2, evt.Height / 2 - 2);

        int cx = _farmOrigin.x + _minFarmZoneSize / 2;
        int cy = _farmOrigin.y + _minFarmZoneSize / 2;
        GridManager.Instance?.ReserveCircle(cx, cy, _farmProtectionRadius);
    }

    private void OnGridGenerated(OnGridGenerated evt)
    {
        ClearSpawned();
        _farmCells = new List<Vector2Int>();
        _allSheepPositions = new List<Vector2Int>();
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;

        if (_spawnAtLevel > 1)
        {
            Debug.Log($"[ShepherdFarm] Ferme retardée au level {_spawnAtLevel}");
            return;
        }

        SpawnFarmAtOrigin(GridManager.Instance?.Width ?? evt.Width,
                          GridManager.Instance?.Height ?? evt.Height);
    }

    private void OnGridExtending(OnGridExtending evt)
    {
        _cellStep = GridManager.Instance?.CellStep ?? _cellStep;
        var gm = GridManager.Instance;
        if (gm == null) return;

        // ── Level ferme : réserver zone + placer moutons batch 1 AVANT ennemis ──
        if (_spawnAtLevel > 1 && evt.Level == _spawnAtLevel && !_farmReserved)
        {
            Vector2Int? origin = FindFarmZone(gm.Width, gm.Height,
                evt.OldHeight, evt.NewHeight);
            _farmOrigin = origin ??
                new Vector2Int(gm.Width / 2 - 2, evt.OldHeight + 2);

            int cx = _farmOrigin.x + _minFarmZoneSize / 2;
            int cy = _farmOrigin.y + _minFarmZoneSize / 2;
            gm.ReserveCircle(cx, cy, _farmProtectionRadius);
            _farmReserved = true;

            Debug.Log($"[ShepherdFarm] Zone ferme réservée level={evt.Level} " +
                      $"origin={_farmOrigin}");

            // Batch 1 : dans tout le chunk ferme + léger débordement prologue
            int sheepCount = QuestManager.Instance?.SheepOnLevel1 ?? 2;
            int yLo = Mathf.Max(_edgeMargin, evt.OldHeight - 4);
            int yHi = evt.NewHeight - _edgeMargin;
            PlaceSheepBeforeEnemies(sheepCount, 0, gm, yLo, yHi);
        }

        // ── Level suivant : moutons batch 2 dans leur chunk ────────────────
        if (evt.Level == _spawnAtLevel + 1)
        {
            int count = QuestManager.Instance?.SheepOnLevel2 ?? 2;
            int offset = QuestManager.Instance?.SheepOnLevel1 ?? 2;
            int yLo = evt.OldHeight + _edgeMargin;
            int yHi = evt.NewHeight - _edgeMargin;
            PlaceSheepBeforeEnemies(count, offset, gm, yLo, yHi);
            Debug.Log($"[ShepherdFarm] Moutons batch 2: {count} au level={evt.Level}");
        }
    }

    private void OnGridExtended(OnGridExtended evt)
    {
        // Spawn la ferme VISUELLE (cabane, berger, accessoires)
        // Les moutons sont déjà placés dans OnGridExtending
        if (_spawnAtLevel > 1 && evt.Level == _spawnAtLevel && !_farmSpawned)
        {
            var gm = GridManager.Instance;
            if (gm == null) return;

            SpawnFarmAtOrigin(gm.Width, gm.Height);
            _farmSpawned = true;
            Debug.Log($"[ShepherdFarm] ✅ Ferme level={evt.Level} origin={_farmOrigin}");
        }
    }

    // =========================================================================
    // SPAWN FERME
    // =========================================================================

    private void SpawnFarmAtOrigin(int gridWidth, int gridHeight)
    {
        Vector2Int cabinPos = new Vector2Int(
            _farmOrigin.x + 1,
            _farmOrigin.y + 2);
        SpawnDecorWithOrder(_cabinPrefab, cabinPos, "Cabane", orderInLayer: 4);
        _farmCells.Add(cabinPos);
        _farmCells.Add(cabinPos + Vector2Int.right);

        Vector2Int doorPos = new Vector2Int(cabinPos.x, cabinPos.y - 1);
        AttachCabinDoor(cabinPos, doorPos);

        SpawnFarmAccessories(cabinPos);

        Vector2Int shepherdPos = GetShepherdPosition(cabinPos);
        SpawnShepherd(shepherdPos);
    }

    // =========================================================================
    // ACCESSOIRES
    // =========================================================================

    private void SpawnFarmAccessories(Vector2Int cabinPos)
    {
        var allSlots = new List<(Vector2Int offset, int order, bool hayOnly)>
        {
            (new Vector2Int(-1,  0), 3, true),
            (new Vector2Int( 2,  0), 3, true),
            (new Vector2Int(-2,  0), 3, false),
            (new Vector2Int( 3,  0), 3, false),
            (new Vector2Int(-1, -1), 6, false),
            (new Vector2Int( 1, -1), 6, false),
            (new Vector2Int(-1, -2), 6, false),
            (new Vector2Int( 0, -2), 6, false),
            (new Vector2Int( 1, -2), 6, false),
        };
        ShuffleList(allSlots);

        var gm = GridManager.Instance;
        var validSlots = new List<(Vector2Int pos, int order, bool hayOnly)>();
        foreach (var (off, ord, hayOnly) in allSlots)
        {
            var pos = cabinPos + off;
            if (_farmCells.Contains(pos)) continue;
            if (gm != null && !IsValidCell(pos, gm.Width, gm.Height)) continue;
            validSlots.Add((pos, ord, hayOnly));
        }

        bool barrelPlaced = false;
        foreach (var (pos, ord, hayOnly) in validSlots)
        {
            if (!barrelPlaced && !hayOnly && _barrelPrefab != null)
            {
                SpawnDecorWithOrder(_barrelPrefab, pos, "Tonneau", ord);
                _farmCells.Add(pos);
                barrelPlaced = true;
                continue;
            }

            if (_farmCells.Count >= 6) break;

            var hayPrefab = (_rng.Next(2) == 0) ? _haySmallPrefab : _hayBigPrefab;
            if (hayPrefab != null)
            {
                SpawnDecorWithOrder(hayPrefab, pos,
                    "Paille_" + (_farmCells.Count - 2), ord);
                _farmCells.Add(pos);
            }
        }
    }

    // =========================================================================
    // BERGER
    // =========================================================================

    private Vector2Int GetShepherdPosition(Vector2Int cabinPos)
    {
        var candidates = new List<Vector2Int>
        {
            cabinPos + new Vector2Int(0, -1),
            cabinPos + new Vector2Int(1, -1),
            cabinPos + new Vector2Int(-1, 0),
            cabinPos + new Vector2Int(2, 0),
            cabinPos + new Vector2Int(0, -2),
            cabinPos + new Vector2Int(1, -2),
        };
        ShuffleList(candidates);

        var gm = GridManager.Instance;
        foreach (var pos in candidates)
        {
            if (_farmCells.Contains(pos)) continue;
            if (gm != null && !IsValidCell(pos, gm.Width, gm.Height)) continue;
            return pos;
        }
        return cabinPos + new Vector2Int(0, -1);
    }

    private void SpawnShepherd(Vector2Int pos)
    {
        if (_shepherdPrefab == null) return;
        var go = Instantiate(_shepherdPrefab, GridToWorld(pos), Quaternion.identity, null);
        go.name = "VieuxBerger";
        go.transform.localScale = Vector3.one * _shepherdScale;

        var npc = go.GetComponent<NPC>() ?? go.AddComponent<NPC>();
        npc.Initialize(NPC.NPCType.Shepherd, 0);
        EnsureCollider(go);

        var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
        wo.Initialize(pos.x, pos.y);

        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = 5;
        }

        _spawnedObjects.Add(go);
        _farmCells.Add(pos);
    }

    // =========================================================================
    // MOUTONS
    // =========================================================================

    /// <summary>
    /// Place les moutons AVANT PlaceDangers avec fallback progressif.
    /// Passe 1 : contraintes complètes (distance ferme + entre moutons).
    /// Passe 2 : distance ferme réduite.
    /// Passe 3 : n'importe où dans la zone Y.
    /// Garantit que TOUS les moutons sont placés.
    /// </summary>
    private void PlaceSheepBeforeEnemies(int count, int indexOffset,
                                          GridManager gm, int yLo, int yHi)
    {
        if (_sheepPrefab == null || gm == null) return;

        int placed = 0;

        // Passe 1 : contraintes normales
        placed += TryPlaceSheep(count - placed, indexOffset + placed, gm, yLo, yHi,
            _sheepMinDistFromFarm, _sheepMaxDistFromFarm, _sheepMinDistBetween);

        // Passe 2 : distance ferme réduite (min 3)
        if (placed < count)
            placed += TryPlaceSheep(count - placed, indexOffset + placed, gm, yLo, yHi,
                3, _sheepMaxDistFromFarm + 4, Mathf.Max(2, _sheepMinDistBetween - 1));

        // Passe 3 : n'importe où dans la zone
        if (placed < count)
            placed += TryPlaceSheep(count - placed, indexOffset + placed, gm, yLo, yHi,
                1, 99, 2);

        Debug.Log($"[ShepherdFarm] ✅ Moutons: {placed}/{count} offset={indexOffset} " +
                  $"Y=[{yLo},{yHi}]");
    }

    private int TryPlaceSheep(int count, int indexOffset, GridManager gm,
                               int yLo, int yHi,
                               int minDistFarm, int maxDistFarm, int minDistBetween)
    {
        int placed = 0;

        for (int attempt = 0; attempt < 400 && placed < count; attempt++)
        {
            int sx = _rng.Next(_edgeMargin, gm.Width - _edgeMargin);
            int sy = _rng.Next(yLo, yHi + 1);

            int distFarm = Mathf.Max(
                Mathf.Abs(sx - _farmOrigin.x),
                Mathf.Abs(sy - _farmOrigin.y));
            if (distFarm < minDistFarm || distFarm > maxDistFarm) continue;

            var pos = new Vector2Int(sx, sy);
            if (_farmCells != null && _farmCells.Contains(pos)) continue;

            bool tooClose = false;
            foreach (var sp in _allSheepPositions)
                if (Mathf.Max(Mathf.Abs(sp.x - sx), Mathf.Abs(sp.y - sy))
                    < minDistBetween)
                { tooClose = true; break; }
            if (tooClose) continue;

            var cell = gm.GetCell(sx, sy);
            if (cell == null) continue;
            if (cell.IsMountainReserved) continue;

            var go = Instantiate(_sheepPrefab, GridToWorld(pos),
                Quaternion.identity, null);
            go.name = "Sheep_" + (placed + indexOffset);
            go.transform.localScale = Vector3.one * _sheepScale;

            var npc = go.GetComponent<NPC>();
            if (npc != null) npc.Initialize(NPC.NPCType.Sheep, placed + indexOffset);
            EnsureCollider(go);

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(sx, sy);

            gm.ReserveCircle(sx, sy, _sheepProtectionRadius);
            _spawnedObjects.Add(go);
            _allSheepPositions.Add(pos);
            placed++;

            Debug.Log($"[ShepherdFarm] Sheep_{placed - 1 + indexOffset} ({sx},{sy}) " +
                      $"dist={distFarm} Y=[{yLo},{yHi}]");
        }

        return placed;
    }

    /// <summary>
    /// Place les moutons LIBREMENT dans un chunk (batch 2).
    /// Pas de contrainte de distance à la ferme — juste dans [yLo, yHi].
    /// </summary>
    private void PlaceSheepFree(int count, int indexOffset,
                                 GridManager gm, int yLo, int yHi)
    {
        if (_sheepPrefab == null || gm == null) return;

        int placed = 0;

        for (int attempt = 0; attempt < 500 && placed < count; attempt++)
        {
            int sx = _rng.Next(_edgeMargin, gm.Width - _edgeMargin);
            int sy = _rng.Next(yLo, yHi);

            var pos = new Vector2Int(sx, sy);

            bool tooClose = false;
            foreach (var sp in _allSheepPositions)
                if (Mathf.Max(Mathf.Abs(sp.x - sx), Mathf.Abs(sp.y - sy))
                    < _sheepMinDistBetween)
                { tooClose = true; break; }
            if (tooClose) continue;

            var cell = gm.GetCell(sx, sy);
            if (cell == null) continue;
            if (cell.IsMountainReserved) continue;

            var go = Instantiate(_sheepPrefab, GridToWorld(pos),
                Quaternion.identity, null);
            go.name = "Sheep_" + (placed + indexOffset);
            go.transform.localScale = Vector3.one * _sheepScale;

            var npc = go.GetComponent<NPC>();
            if (npc != null) npc.Initialize(NPC.NPCType.Sheep, placed + indexOffset);
            EnsureCollider(go);

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(sx, sy);

            gm.ReserveCircle(sx, sy, _sheepProtectionRadius);
            _spawnedObjects.Add(go);
            _allSheepPositions.Add(pos);
            placed++;

            Debug.Log($"[ShepherdFarm] Sheep_{placed - 1 + indexOffset} placé ({sx},{sy}) Y=[{yLo},{yHi}]");
        }

        Debug.Log($"[ShepherdFarm] ✅ Batch 2: {placed}/{count}");
    }

    private void SpawnSheepInZone(int count, int gridWidth, int gridHeight,
                                  int yMin, int indexOffset)
    {
        if (_sheepPrefab == null) return;

        int placed = 0;
        int yMax = gridHeight - _edgeMargin;

        for (int attempt = 0; attempt < _sheepPlacementMaxTries && placed < count; attempt++)
        {
            int sx = _rng.Next(_edgeMargin, gridWidth - _edgeMargin);
            int sy = _rng.Next(Mathf.Max(yMin, _edgeMargin), yMax);
            var pos = new Vector2Int(sx, sy);

            if (_farmCells != null && _farmCells.Contains(pos)) continue;

            int distFarm = Mathf.Max(
                Mathf.Abs(pos.x - _farmOrigin.x),
                Mathf.Abs(pos.y - _farmOrigin.y));
            if (distFarm < _sheepMinDistFromFarm) continue;

            bool tooClose = false;
            foreach (var other in _allSheepPositions)
            {
                int d = Mathf.Max(Mathf.Abs(pos.x - other.x), Mathf.Abs(pos.y - other.y));
                if (d < _sheepMinDistBetween) { tooClose = true; break; }
            }
            if (tooClose) continue;

            // Vérifier bounds + montagne (PAS reserved — les moutons vont dans la zone protégée)
            if (!IsValidCell(pos, gridWidth, gridHeight, false)) continue;

            // Vérifier que la case n'a pas d'ennemi
            var cellCheck = GridManager.Instance?.GetCell(pos.x, pos.y);
            if (cellCheck != null && cellCheck.IsDangerous) continue;

            bool nearEnemy = false;
            var gm = GridManager.Instance;
            if (gm != null)
            {
                for (int ex = -_sheepEnemySafeRadius; ex <= _sheepEnemySafeRadius && !nearEnemy; ex++)
                    for (int ey = -_sheepEnemySafeRadius; ey <= _sheepEnemySafeRadius && !nearEnemy; ey++)
                    {
                        var n = new Vector2Int(pos.x + ex, pos.y + ey);
                        if (!MinesweeperLogic.IsInBounds(n.x, n.y, gridWidth, gridHeight)) continue;
                        var nc = gm.GetCell(n.x, n.y);
                        if (nc != null && nc.IsEnemy) nearEnemy = true;
                    }
            }
            if (nearEnemy) continue;

            var go = Instantiate(_sheepPrefab, GridToWorld(pos), Quaternion.identity, null);
            go.name = "Sheep_" + (placed + indexOffset);
            go.transform.localScale = Vector3.one * _sheepScale;

            var npc = go.GetComponent<NPC>();
            if (npc != null) npc.Initialize(NPC.NPCType.Sheep, placed + indexOffset);
            EnsureCollider(go);

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(pos.x, pos.y);

            GridManager.Instance?.ReserveCircle(pos.x, pos.y, _sheepProtectionRadius);

            _spawnedObjects.Add(go);
            _allSheepPositions.Add(pos);
            placed++;
        }

        Debug.Log($"[ShepherdFarm] SheepInZone: {placed}/{count} Y=[{yMin},{yMax}]");
    }

    // =========================================================================
    // FIND FARM ZONE
    // =========================================================================

    private Vector2Int? FindFarmZone(int gridWidth, int gridHeight,
                                      int constrainYMin = -1,
                                      int constrainYMax = -1)
    {
        int minX = _edgeMargin;
        int maxX = gridWidth - _edgeMargin - _minFarmZoneSize;
        int minY = (constrainYMin >= 0) ? constrainYMin + _edgeMargin : _edgeMargin;
        int maxY = (constrainYMax > 0)
            ? constrainYMax - _edgeMargin - _minFarmZoneSize
            : gridHeight - _edgeMargin - _minFarmZoneSize;

        if (maxX <= minX || maxY <= minY)
            return new Vector2Int(gridWidth / 2 - 2, minY + 2);

        int cornerW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _cornerZoneRatio));
        int cornerH = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) * _cornerZoneRatio));
        int edgeW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _edgeZoneRatio));
        int edgeH = Mathf.Max(1, Mathf.RoundToInt((maxY - minY) * _edgeZoneRatio));

        float roll = (float)_rng.NextDouble();
        int x, y;

        if (roll < _cornerBias)
        {
            int corner = _rng.Next(4);
            x = (corner == 0 || corner == 2) ? _rng.Next(minX, minX + cornerW) : _rng.Next(maxX - cornerW, maxX);
            y = (corner == 0 || corner == 1) ? _rng.Next(minY, minY + cornerH) : _rng.Next(maxY - cornerH, maxY);
        }
        else if (roll < _cornerBias + _edgeBias)
        {
            int side = _rng.Next(4);
            switch (side)
            {
                case 0: x = _rng.Next(minX, maxX); y = _rng.Next(minY, minY + edgeH); break;
                case 1: x = _rng.Next(minX, maxX); y = _rng.Next(maxY - edgeH, maxY); break;
                case 2: x = _rng.Next(minX, minX + edgeW); y = _rng.Next(minY, maxY); break;
                default: x = _rng.Next(maxX - edgeW, maxX); y = _rng.Next(minY, maxY); break;
            }
        }
        else
        {
            x = _rng.Next(minX + cornerW, maxX - cornerW);
            y = _rng.Next(minY + cornerH, maxY - cornerH);
        }

        x = Mathf.Clamp(x, minX, maxX);
        y = Mathf.Clamp(y, minY, maxY);
        return new Vector2Int(x, y);
    }

    // =========================================================================
    // DOOR
    // =========================================================================

    private void AttachCabinDoor(Vector2Int cabinPos, Vector2Int doorPos)
    {
        GameObject cabinGO = null;
        foreach (var go in _spawnedObjects)
        {
            if (go != null && go.name == "Cabane") { cabinGO = go; break; }
        }
        if (cabinGO == null) return;

        if (cabinGO.GetComponent<BoxCollider2D>() == null)
            cabinGO.AddComponent<BoxCollider2D>();

        if (cabinGO.GetComponent<BuildingDoorTrigger>() == null)
        {
            var trigger = cabinGO.AddComponent<BuildingDoorTrigger>();
            trigger.Initialize(doorPos.x, doorPos.y, "Cabane du Berger");
        }

        IndoorManager.Instance?.RegisterBuilding(doorPos.x, doorPos.y, "Cabane du Berger");
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private bool IsValidCell(Vector2Int pos, int w, int h, bool checkDanger = false)
    {
        if (!MinesweeperLogic.IsInBounds(pos.x, pos.y, w, h)) return false;
        var gm = GridManager.Instance;
        if (gm == null) return true;
        var cell = gm.GetCell(pos.x, pos.y);
        if (cell == null) return false;
        if (cell.IsMountainReserved) return false;
        if (checkDanger && cell.IsDangerous) return false;
        return true;
    }

    private Vector3 GridToWorld(Vector2Int pos)
    {
        float step = _cellStep > 0 ? _cellStep : 1.05f;
        return new Vector3(pos.x * step, pos.y * step, 0f);
    }

    private void SpawnDecorWithOrder(GameObject prefab, Vector2Int pos, string label, int orderInLayer)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab, GridToWorld(pos), Quaternion.identity, null);
        go.name = label;
        go.transform.localScale = (label == "Cabane")
            ? Vector3.one * _cabinScale
            : Vector3.one * _accessoryScale;

        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = orderInLayer;
        }

        var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
        wo.Initialize(pos.x, pos.y);

        _spawnedObjects.Add(go);
    }

    private void EnsureCollider(GameObject go)
    {
        if (go.GetComponent<Collider2D>() == null)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.size = Vector2.one * 0.9f;
        }
    }

    private void ClearSpawned()
    {
        foreach (var go in _spawnedObjects) if (go != null) Destroy(go);
        _spawnedObjects.Clear();
    }

    private void ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
    }
}