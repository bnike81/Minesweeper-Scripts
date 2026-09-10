using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CampSpawner - Place le campement abandonne au niveau 2.
/// Zone centrale du niveau 2. Compose de tente, feu de camp, accessoire.
/// </summary>
public class CampSpawner : MonoBehaviour
{
    public static CampSpawner Instance { get; private set; }

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject _tentePrefab;
    [SerializeField] private GameObject _feuCampPrefab;
    [SerializeField] private GameObject _accessoirePrefab;

    [Header("=== Protection ===")]
    [Tooltip("Rayon sans danger autour du campement")]
    [SerializeField, Range(1f, 8f)] private float _protectionRadius = 3.5f;

    [Header("=== Placement Zone ===")]
    [Tooltip("Marge depuis les bords de la grille")]
    [SerializeField, Range(1, 5)] private int _edgeMargin = 3;
    [Tooltip("Demi-hauteur de la zone centrale niveau 2")]
    [SerializeField, Range(2, 8)] private int _centralRange = 5;

    [Header("=== Probabilites de Zone ===")]
    [SerializeField, Range(0f, 1f)] private float _cornerBias = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _edgeBias = 0.35f;
    [SerializeField, Range(0.1f, 0.5f)] private float _cornerZoneRatio = 0.30f;
    [SerializeField, Range(0.1f, 0.4f)] private float _edgeZoneRatio = 0.20f;

    [Header("=== Tailles ===")]
    [SerializeField, Range(0.5f, 3f)] private float _tenteScale = 1.2f;
    [SerializeField, Range(0.5f, 2f)] private float _feuCampScale = 0.8f;
    [SerializeField, Range(0.2f, 1.5f)] private float _accessoireScale = 0.5f;

    [Header("=== Graine ===")]
    [SerializeField] private int _seed = 0;

    private System.Random _rng;
    private float _cellStep;
    private Vector2Int _origin;
    private List<Vector2Int> _occupied = new();
    private List<GameObject> _spawned = new();
    private bool _hasSpawned = false;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnGridExtending>(OnGridExtending);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnGridExtending>(OnGridExtending);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        _rng = new System.Random(_seed != 0 ? _seed : System.Environment.TickCount);
        _hasSpawned = false;
        ClearSpawned();
        _occupied = new List<Vector2Int>();
    }

    // -------------------------------------------------------------------------
    // Reserve AVANT placement des dangers (niveau 2)
    // -------------------------------------------------------------------------

    private void OnGridExtending(OnGridExtending evt)
    {
        if (_hasSpawned || evt.Level != 2) return;

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;

        int levelH = evt.OldHeight;
        int midY = levelH + (evt.NewHeight - levelH) / 2;
        int yMin = Mathf.Max(levelH + _edgeMargin, midY - _centralRange);
        int yMax = Mathf.Min(evt.NewHeight - _edgeMargin, midY + _centralRange);

        int minX = _edgeMargin;
        int maxX = evt.Width - _edgeMargin - 1;
        int cornerW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _cornerZoneRatio));
        int edgeW = Mathf.Max(1, Mathf.RoundToInt((maxX - minX) * _edgeZoneRatio));

        float roll = (float)_rng.NextDouble();
        float cornerP = _cornerBias;
        float edgeP = Mathf.Min(_edgeBias, 1f - cornerP);

        int x;
        if (roll < cornerP)
            x = _rng.NextDouble() < 0.5
                ? _rng.Next(minX, minX + cornerW)
                : _rng.Next(maxX - cornerW, maxX);
        else if (roll < cornerP + edgeP)
            x = _rng.NextDouble() < 0.5
                ? _rng.Next(minX, minX + edgeW)
                : _rng.Next(maxX - edgeW, maxX);
        else
            x = _rng.Next(minX + cornerW, maxX - cornerW);

        x = Mathf.Clamp(x, minX, maxX);
        int y = _rng.Next(yMin, yMax);
        _origin = new Vector2Int(x, y);

        var gm = GridManager.Instance;
        if (gm != null)
        {
            gm.ReserveCircle(_origin.x, _origin.y, _protectionRadius);
            for (int dy = 1; dy <= (int)_protectionRadius; dy++)
                gm.ReserveCircle(_origin.x, _origin.y + dy, _protectionRadius - dy + 1);
        }

        Debug.Log("<color=#FF8844>[CampSpawner]</color> Camp reserve en " + _origin);
    }

    // -------------------------------------------------------------------------
    // Spawn apres extension niveau 2
    // -------------------------------------------------------------------------

    private void OnGridExtended(OnGridExtended evt)
    {
        if (_hasSpawned || evt.Level != 2) return;

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        _hasSpawned = true;

        SpawnTente();
        SpawnFeu();
        SpawnAccessoires();

        Debug.Log("<color=#FF8844>[CampSpawner]</color> Campement spawne en " + _origin);
    }

    // -------------------------------------------------------------------------
    // Elements
    // -------------------------------------------------------------------------

    private void SpawnTente()
    {
        SpawnElement(_tentePrefab, _origin, "Tente", _tenteScale, 4);
        _occupied.Add(_origin);
        _occupied.Add(_origin + Vector2Int.up); // Y+1 interdit
    }

    private void SpawnFeu()
    {
        // Candidates : feu avec espace vide d'un cote
        var candidates = new List<(Vector2Int feu, Vector2Int vide)>
        {
            (_origin + new Vector2Int( 0, -1), _origin + new Vector2Int(-1, -1)),
            (_origin + new Vector2Int( 0, -1), _origin + new Vector2Int( 1, -1)),
            (_origin + new Vector2Int( 1, -1), _origin + new Vector2Int(-1, -1)),
            (_origin + new Vector2Int(-1, -1), _origin + new Vector2Int( 1, -1)),
        };

        int n = candidates.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (candidates[k], candidates[n]) = (candidates[n], candidates[k]); }

        var gm = GridManager.Instance;
        foreach (var (feuPos, videPos) in candidates)
        {
            if (_occupied.Contains(feuPos)) continue;
            if (gm != null && !MinesweeperLogic.IsInBounds(feuPos.x, feuPos.y, gm.Width, gm.Height)) continue;

            SpawnElement(_feuCampPrefab, feuPos, "FeuDeCamp", _feuCampScale, 5);
            _occupied.Add(feuPos);
            _occupied.Add(videPos);        // Cote vide reserve
            _occupied.Add(feuPos + Vector2Int.down); // Y-2 devant le feu = vide
            break;
        }
    }

    private void SpawnAccessoires()
    {
        if (_accessoirePrefab == null) return;
        var gm = GridManager.Instance;

        var slots = new List<Vector2Int>
        {
            _origin + new Vector2Int(-1,  0),
            _origin + new Vector2Int( 1,  0),
            _origin + new Vector2Int(-1, -1),
            _origin + new Vector2Int( 1, -1),
            _origin + new Vector2Int(-1, -2),
            _origin + new Vector2Int( 1, -2),
        };

        int n = slots.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (slots[k], slots[n]) = (slots[n], slots[k]); }

        int placed = 0;
        foreach (var pos in slots)
        {
            if (placed >= 1) break;
            if (_occupied.Contains(pos)) continue;
            if (gm != null && !MinesweeperLogic.IsInBounds(pos.x, pos.y, gm.Width, gm.Height)) continue;
            var cell = gm?.GetCell(pos.x, pos.y);
            if (cell != null && cell.IsDangerous) continue;

            SpawnElement(_accessoirePrefab, pos, "AccessoireCamp_" + placed, _accessoireScale, 4);
            _occupied.Add(pos);
            placed++;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private GameObject SpawnElement(GameObject prefab, Vector2Int gPos, string label, float scale, int order)
    {
        if (prefab == null) { Debug.LogWarning("[CampSpawner] Prefab manquant : " + label); return null; }

        float step = _cellStep > 0 ? _cellStep : 1.05f;
        var go = Instantiate(prefab, new Vector3(gPos.x * step, gPos.y * step, 0f), Quaternion.identity, null);
        go.name = label;
        go.transform.localScale = Vector3.one * scale;

        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = order;
        }

        var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
        wo.Initialize(gPos.x, gPos.y);

        _spawned.Add(go);
        return go;
    }

    private void ClearSpawned()
    {
        foreach (var go in _spawned) if (go != null) Destroy(go);
        _spawned.Clear();
    }

    public Vector2Int? GetCampfirePosition()
    {
        foreach (var go in _spawned)
            if (go != null && go.name == "FeuDeCamp")
            {
                float s = GridManager.Instance?.CellStep ?? 1.05f;
                return new Vector2Int(
                    Mathf.RoundToInt(go.transform.position.x / s),
                    Mathf.RoundToInt(go.transform.position.y / s));
            }
        return null;
    }

    // -------------------------------------------------------------------------
    // Dialogue decouverte
    // -------------------------------------------------------------------------

    public void OnCampDiscovered()
    {
        StartCoroutine(CampDiscoveryDialogue());
    }

    private IEnumerator CampDiscoveryDialogue()
    {
        yield return new WaitForSeconds(0.5f);
        DialogueBox.Instance?.ShowMessage("Heros",
            "Tiens donc... un campement abandonne.");
        yield return new WaitForSeconds(3f);
        DialogueBox.Instance?.ShowMessage("Heros",
            "Le feu semble encore utilisable.");
        yield return new WaitForSeconds(3f);
        DialogueBox.Instance?.ShowMessage("Heros",
            "Quelqu'un est passé par là... ou quelque chose.");
    }
}