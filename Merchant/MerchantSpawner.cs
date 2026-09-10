using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MerchantSpawner - Place le marchand ambulant Aldric
/// a la frontiere niveau 2/3 (cases hautes du niveau 2 ou basses du niveau 3).
/// Meme logique de placement que ShepherdFarmSpawner.
/// </summary>
public class MerchantSpawner : MonoBehaviour
{
    public static MerchantSpawner Instance { get; private set; }

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject _merchantPrefab;
    [SerializeField] private GameObject _calèchePrefab;
    [SerializeField] private GameObject _marchandisePrefab;

    [Header("=== Protection ===")]
    [Tooltip("Rayon sans danger autour du marchand")]
    [SerializeField, Range(1f, 8f)] private float _protectionRadius = 3f;

    [Header("=== Placement ===")]
    [Tooltip("Marge depuis les bords de la grille")]
    [SerializeField, Range(1, 5)] private int _edgeMargin = 3;
    [Tooltip("Cases de marge depuis la frontiere niveau 2/3")]
    [SerializeField, Range(2, 10)] private int _borderRange = 8;

    [Header("=== Probabilites de Zone ===")]
    [Tooltip("Chance que le marchand soit dans un coin de la zone")]
    [SerializeField, Range(0f, 1f)] private float _cornerBias = 0.35f;
    [Tooltip("Chance que le marchand soit sur un bord de la zone")]
    [SerializeField, Range(0f, 1f)] private float _edgeBias = 0.35f;
    // Reste = centre de la zone

    [Tooltip("Taille de la zone coin en ratio de la largeur")]
    [SerializeField, Range(0.1f, 0.5f)] private float _cornerZoneRatio = 0.30f;
    [Tooltip("Taille de la zone bord en ratio de la largeur")]
    [SerializeField, Range(0.1f, 0.4f)] private float _edgeZoneRatio = 0.20f;

    [Header("=== Tailles ===")]
    [SerializeField, Range(0.5f, 3f)] private float _calecheScale = 1.5f;
    [SerializeField, Range(0.2f, 2f)] private float _merchantScale = 0.5f;
    [SerializeField, Range(0.2f, 1.5f)] private float _marchandiseScale = 0.5f;

    [Header("=== Graine ===")]
    [SerializeField] private int _seed = 0;

    private System.Random _rng;
    private float _cellStep;
    private Vector2Int _origin;
    private List<Vector2Int> _occupiedCells = new();
    private List<GameObject> _spawnedObjects = new();
    private bool _spawned = false;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridDataReady>(OnGridDataReady);
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtending>(OnGridExtendingEvt);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridDataReady>(OnGridDataReady);
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
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

    // -------------------------------------------------------------------------
    // Le marchand apparait quand la grille s'etend au niveau 3
    // -------------------------------------------------------------------------

    private void OnGridDataReady(OnGridDataReady evt) { }

    private void OnGridGenerated(OnGridGenerated evt)
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
    }

    /// <summary>
    /// Recu AVANT le placement des dangers de la nouvelle zone.
    /// On calcule la position du marchand et on reserve le cercle ici.
    /// </summary>
    private void OnGridExtendingEvt(OnGridExtending evt)
    {
        if (_spawned) return;
        if (evt.Level < 3) return;

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;

        // Calculer la position
        // Aldric se place dans les premieres cases du niveau 3
        // (juste au dessus de la frontiere = dans la zone des nouveaux dangers)
        int borderY = evt.OldHeight;
        int yMin = borderY + _edgeMargin;
        int yMax = Mathf.Min(evt.NewHeight - _edgeMargin, borderY + _borderRange);

        // Tirage selon biais coin/bord/centre sur l'axe X
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

        Debug.Log("<color=#FFD700>[MerchantSpawner]</color> Zone=" + zone
            + " pos=(" + x + "," + y + ") roll=" + roll.ToString("F2"));

        // Reserver le cercle MAINTENANT avant que les dangers soient places
        // On etend le rayon vers le haut pour couvrir les cases Y+1 a Y+radius
        var gm = GridManager.Instance;
        if (gm != null)
        {
            // Cercle principal sur la position d'Aldric
            gm.ReserveCircle(_origin.x, _origin.y, _protectionRadius);
            // Cercles supplementaires vers le haut pour couvrir la zone des nouveaux dangers
            for (int dy = 1; dy <= (int)_protectionRadius; dy++)
                gm.ReserveCircle(_origin.x, _origin.y + dy, _protectionRadius - dy + 1);

            Debug.Log("<color=#FFD700>[MerchantSpawner]</color> Zone reservee pour Aldric en "
                + _origin + " rayon=" + _protectionRadius
                + " | ReservedCells=" + gm.ReservedCount);
        }
    }

    private void OnGridExtended(OnGridExtended evt)
    {
        if (_spawned) return;
        if (evt.Level < 3) return;

        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
        var gm = GridManager.Instance;
        if (gm == null) return;

        // _origin est deja calcule dans OnGridExtendingEvt
        SpawnMerchantAtOrigin(gm.Width, gm.Height);
    }

    // -------------------------------------------------------------------------
    // Placement
    // -------------------------------------------------------------------------

    private void SpawnMerchantAtOrigin(int gridWidth, int gridHeight)
    {
        // _origin est deja calcule et reserve dans OnGridExtendingEvt
        Debug.Log("<color=#FFD700>[MerchantSpawner]</color> Spawn Aldric en " + _origin);
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

        // Slots proches de la calèche uniquement
        // Pas devant Aldric (Y-1), pas trop loin
        var slots = new List<(Vector2Int offset, int order)>
        {
            (new Vector2Int(-1,  0), 3),  // lateral gauche direct
            (new Vector2Int( 2,  0), 3),  // lateral droite direct
        };

        // Melanger
        int n = slots.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (slots[k], slots[n]) = (slots[n], slots[k]); }

        var gm = GridManager.Instance;
        int placed = 0;
        foreach (var (off, ord) in slots)
        {
            if (placed >= 1) break; // 1 seul accessoire
            var pos = _origin + off;
            if (_occupiedCells.Contains(pos)) continue;
            if (gm != null && !MinesweeperLogic.IsInBounds(pos.x, pos.y, gm.Width, gm.Height)) continue;

            // Verifier que la case n'a pas de danger
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

        // Devant la calèche (Y-1)
        var candidates = new List<Vector2Int>
        {
            _origin + new Vector2Int( 0, -1),
            _origin + new Vector2Int( 1, -1),
            _origin + new Vector2Int(-1, -1),
            _origin + new Vector2Int( 0, -2),
        };

        // Melanger
        int n = candidates.Count;
        while (n > 1) { n--; int k = _rng.Next(n + 1); (candidates[k], candidates[n]) = (candidates[n], candidates[k]); }

        foreach (var pos in candidates)
        {
            if (_occupiedCells.Contains(pos)) continue;
            if (!MinesweeperLogic.IsInBounds(pos.x, pos.y,
                GridManager.Instance.Width, GridManager.Instance.Height)) continue;

            var go = SpawnObject(_merchantPrefab, pos, "Aldric", _merchantScale, 5);

            // Ajouter composants
            var npc = go.GetComponent<MerchantNPC>() ?? go.AddComponent<MerchantNPC>();
            var col = go.GetComponent<Collider2D>() ?? go.AddComponent<BoxCollider2D>();
            if (col is BoxCollider2D box) box.size = Vector2.one * 0.9f;

            var wo = go.GetComponent<WorldObject>() ?? go.AddComponent<WorldObject>();
            wo.Initialize(pos.x, pos.y);

            _occupiedCells.Add(pos);
            break;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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