using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MerchantSpawner - Place le marchand ambulant Aldric.
/// _spawnAtLevel contrôle le level de spawn (Inspector).
/// Réserve ET spawne au MÊME level.
/// </summary>
public class MerchantSpawner : MonoBehaviour
{
    public static MerchantSpawner Instance { get; private set; }

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject _merchantPrefab;
    [SerializeField] private GameObject _calèchePrefab;
    [SerializeField] private GameObject _marchandisePrefab;

    [Header("=== Protection ===")]
    [SerializeField, Range(1f, 8f)] private float _protectionRadius = 3f;

    [Header("=== Placement ===")]
    [SerializeField, Range(1, 5)] private int _edgeMargin = 3;
    [SerializeField, Range(2, 10)] private int _borderRange = 8;

    [Header("=== Probabilites de Zone ===")]
    [SerializeField, Range(0f, 1f)] private float _cornerBias = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _edgeBias = 0.35f;
    [SerializeField, Range(0.1f, 0.5f)] private float _cornerZoneRatio = 0.30f;
    [SerializeField, Range(0.1f, 0.4f)] private float _edgeZoneRatio = 0.20f;

    [Header("=== Tailles ===")]
    [SerializeField, Range(0.5f, 3f)] private float _calecheScale = 1.5f;
    [SerializeField, Range(0.2f, 2f)] private float _merchantScale = 0.5f;
    [SerializeField, Range(0.2f, 1.5f)] private float _marchandiseScale = 0.5f;

    [Header("=== Graine ===")]
    [SerializeField] private int _seed = 0;

    [Header("=== Level de spawn ===")]
    [Tooltip("Level d'extension où le marchand apparaît. Avec prologue : 4")]
    [SerializeField] private int _spawnAtLevel = 4;

    private System.Random _rng;
    private float _cellStep;
    private Vector2Int _origin;
    private List<Vector2Int> _occupiedCells = new();
    private List<GameObject> _spawnedObjects = new();
    private bool _spawned = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridExtending>(OnGridExtendingEvt);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridExtending>(OnGridExtendingEvt);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        _rng = new System.Random(_seed != 0 ? _seed : System.Environment.TickCount);
        _spawned = false;
        ClearSpawned();
        _occupiedCells = new List<Vector2Int>();
    }

    // ── Réserve AVANT PlaceDangers ───────────────────────────────────────────

    private void OnGridExtendingEvt(OnGridExtending evt)
    {
        if (_spawned || evt.Level != _spawnAtLevel) return;

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;

        int borderY = evt.OldHeight;
        int yMin = borderY + _edgeMargin;
        int yMax = Mathf.Min(evt.NewHeight - _edgeMargin, borderY + _borderRange);

        int minX = _edgeMargin;
        int maxX = evt.Width - _edgeMargin - 2;
        int cornerW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _cornerZoneRatio));
        int edgeW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _edgeZoneRatio));

        float roll = (float)_rng.NextDouble();
        float cornerP = _cornerBias;
        float edgeP = Mathf.Min(_edgeBias, 1f - cornerP);

        int x;
        string zone;

        if (roll < cornerP)
        {
            zone = "COIN";
            x = _rng.NextDouble() < 0.5f
                ? _rng.Next(minX, minX + cornerW)
                : _rng.Next(maxX - cornerW, maxX);
        }
        else if (roll < cornerP + edgeP)
        {
            zone = "BORD";
            x = _rng.NextDouble() < 0.5f
                ? _rng.Next(minX, minX + edgeW)
                : _rng.Next(maxX - edgeW, maxX);
        }
        else
        {
            zone = "CENTRE";
            x = _rng.Next(minX + cornerW, maxX - cornerW);
        }

        x = Mathf.Clamp(x, minX, maxX);
        int y = _rng.Next(yMin, yMax);
        _origin = new Vector2Int(x, y);

        Debug.Log($"[MerchantSpawner] Zone={zone} pos={_origin} level={evt.Level}");

        var gm = GridManager.Instance;
        if (gm != null)
        {
            gm.ReserveCircle(_origin.x, _origin.y, _protectionRadius);
            for (int dy = 1; dy <= (int)_protectionRadius; dy++)
                gm.ReserveCircle(_origin.x, _origin.y + dy, _protectionRadius - dy + 1);
        }
    }

    // ── Spawn APRÈS extension ────────────────────────────────────────────────

    private void OnGridExtended(OnGridExtended evt)
    {
        if (_spawned || evt.Level != _spawnAtLevel) return;

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        var gm = GridManager.Instance;
        if (gm == null) return;

        SpawnMerchantAtOrigin(gm.Width, gm.Height);
    }

    // ── Placement ────────────────────────────────────────────────────────────

    private void SpawnMerchantAtOrigin(int gridWidth, int gridHeight)
    {
        Debug.Log($"[MerchantSpawner] Spawn Aldric en {_origin}");
        SpawnCalèche();
        SpawnAccessoires();
        SpawnMerchantNPC();
        _spawned = true;
    }

    private void SpawnCalèche()
    {
        if (_calèchePrefab == null) return;
        var pos = new Vector2Int(_origin.x, _origin.y);
        SpawnObject(_calèchePrefab, pos, "Calecheche", _calecheScale, 4);
        _occupiedCells.Add(pos);
        _occupiedCells.Add(pos + Vector2Int.right);
    }

    private void SpawnAccessoires()
    {
        if (_marchandisePrefab == null) return;

        var slots = new List<(Vector2Int offset, int order)>
        {
            (new Vector2Int(-1,  0), 3),
            (new Vector2Int( 2,  0), 3),
        };

        int n = slots.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (slots[k], slots[n]) = (slots[n], slots[k]); }

        var gm = GridManager.Instance;
        int placed = 0;
        foreach (var (off, ord) in slots)
        {
            if (placed >= 1) break;
            var pos = _origin + off;
            if (_occupiedCells.Contains(pos)) continue;
            if (gm != null && !MinesweeperLogic.IsInBounds(pos.x, pos.y, gm.Width, gm.Height)) continue;
            var cell = gm?.GetCell(pos.x, pos.y);
            if (cell != null && cell.IsDangerous) continue;

            SpawnObject(_marchandisePrefab, pos, "Marchandise_" + placed, _marchandiseScale, ord);
            _occupiedCells.Add(pos);
            placed++;
        }
    }

    private void SpawnMerchantNPC()
    {
        if (_merchantPrefab == null) return;

        var candidates = new List<Vector2Int>
        {
            _origin + new Vector2Int( 0, -1),
            _origin + new Vector2Int( 1, -1),
            _origin + new Vector2Int(-1, -1),
            _origin + new Vector2Int( 0, -2),
        };

        int n = candidates.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (candidates[k], candidates[n]) = (candidates[n], candidates[k]); }

        foreach (var pos in candidates)
        {
            if (_occupiedCells.Contains(pos)) continue;
            if (!MinesweeperLogic.IsInBounds(pos.x, pos.y,
                GridManager.Instance.Width, GridManager.Instance.Height)) continue;

            var go = SpawnObject(_merchantPrefab, pos, "Aldric", _merchantScale, 5);

            var npc = go.GetComponent<MerchantNPC>() ?? go.AddComponent<MerchantNPC>();
            var col = go.GetComponent<Collider2D>() ?? go.AddComponent<BoxCollider2D>();
            if (col is BoxCollider2D box) box.size = Vector2.one * 0.9f;

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(pos.x, pos.y);

            _occupiedCells.Add(pos);
            break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GameObject SpawnObject(GameObject prefab, Vector2Int gridPos, string label, float scale, int order)
    {
        float step = _cellStep > 0 ? _cellStep : 1.05f;
        Vector3 wPos = new Vector3(gridPos.x * step, gridPos.y * step, 0f);

        var go = Instantiate(prefab, wPos, Quaternion.identity, null);
        go.name = label;
        go.transform.localScale = Vector3.one * scale;

        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = order;
        }

        var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
        wo.Initialize(gridPos.x, gridPos.y);

        _spawnedObjects.Add(go);
        return go;
    }

    private void ClearSpawned()
    {
        foreach (var go in _spawnedObjects)
            if (go != null) Destroy(go);
        _spawnedObjects.Clear();
    }
}