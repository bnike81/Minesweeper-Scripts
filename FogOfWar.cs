using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// FogOfWar - Brouillard de guerre progressif sur la grille.
/// </summary>
public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Header("=== Activation ===")]
    [SerializeField] private bool _enabled = true;

    [Header("=== Distances ===")]
    [Tooltip("Distance max de visibilite (au dela = noir complet)")]
    [SerializeField, Range(1, 8)] private int _visibilityRange = 4;

    [Header("=== Opacite par Distance ===")]
    [SerializeField, Range(0f, 1f)] private float _fogDist1 = 0.12f;
    [SerializeField, Range(0f, 1f)] private float _fogDist2 = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _fogDist3 = 0.58f;
    [SerializeField, Range(0f, 1f)] private float _fogDist4 = 0.78f;
    [SerializeField, Range(0f, 1f)] private float _fogFull = 0.92f;

    [Header("=== Dégradé ===")]
    [Tooltip("Interpoler les opacités entre distances pour un dégradé doux")]
    [SerializeField] private bool _smoothFog = true;

    [Header("=== Visuel ===")]
    [SerializeField] private Color _fogColor = Color.black;
    [SerializeField] private int _sortingOrder = 8;
    [SerializeField] private string _sortingLayer = "CellContent";

    private Dictionary<(int, int), SpriteRenderer> _overlays = new();
    // Pool d'overlays réutilisables — évite Instantiate/Destroy répétés
    private readonly Stack<SpriteRenderer> _overlayPool = new Stack<SpriteRenderer>(64);
    // HashSet réutilisable pour éviter allocations répétées
    private static readonly HashSet<(int, int)> _toUpdateFog = new HashSet<(int, int)>();
    private bool _isBuilding = false;
    public bool IsBuilding => _isBuilding;
    private Sprite _whiteSprite;
    private float _cellStep = 1.05f;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _whiteSprite = CreateWhiteSprite();
        // Material partagé avec la texture blanche — tous les overlays fog batchent
        _fogMaterial = new Material(Shader.Find("Sprites/Default"));
        _fogMaterial.mainTexture = _whiteSprite.texture;
        _fogMaterial.enableInstancing = true;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Subscribe<OnGridExtended>(OnGridExtended);
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnGridGenerated>(OnGridGenerated);
        EventBus.Unsubscribe<OnGridExtended>(OnGridExtended);
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
    }

    private void OnDestroy()
    {
        // Libérer les ressources créées en code
        if (_fogMaterial != null) Destroy(_fogMaterial);
        if (_whiteSprite != null)
        {
            Destroy(_whiteSprite.texture);
            Destroy(_whiteSprite);
        }
        ClearAll();
    }

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    private void OnRunStarted(OnRunStarted e) { }

    private Coroutine _buildCoroutine;
    // Material partagé pour TOUS les overlays fog — force le batching
    private Material _fogMaterial;

    // Queue des dimensions à builder — traité frame par frame
    private int _pendingFogWidth = 0;
    private int _pendingFogHeight = 0;

    private void OnGridExtended(OnGridExtended e)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;
        _cellStep = gm.CellStep;
        // Mémoriser la taille max — une seule coroutine construira tout
        _pendingFogWidth = Mathf.Max(_pendingFogWidth, gm.Width);
        _pendingFogHeight = Mathf.Max(_pendingFogHeight, gm.Height);
        // Ne lancer qu'une seule coroutine fog à la fois
        if (!_isBuilding)
            _buildCoroutine = StartCoroutine(BuildFogNextFrame(
                _pendingFogWidth, _pendingFogHeight));
        // Sinon la coroutine active relira _pendingFogHeight à la fin
        // → pas d'accumulation de coroutines parallèles
    }

    private void OnGridGenerated(OnGridGenerated e)
    {
        _cellStep = GridManager.Instance?.CellStep ?? _cellStep;
        ClearAll();
        if (_buildCoroutine != null) StopCoroutine(_buildCoroutine);
        _buildCoroutine = StartCoroutine(BuildFogNextFrame(e.Width, e.Height));
    }

    private void OnCellsRevealed(OnCellsRevealed e)
    {
        if (!_enabled) return;
        var gm = GridManager.Instance;
        if (gm == null) return;

        // Mettre à jour les cases révélées + leurs voisines dans le rayon
        int r = _visibilityRange + 1;
        _toUpdateFog.Clear();
        foreach (var c in e.Cells)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    int nx = c.X + dx, ny = c.Y + dy;
                    if (nx >= 0 && ny >= 0 && nx < gm.Width && ny < gm.Height)
                        _toUpdateFog.Add((nx, ny));
                }
        }
        foreach (var (x, y) in _toUpdateFog)
            UpdateCell(x, y, gm);
    }

    // -------------------------------------------------------------------------
    // Construction overlays
    // -------------------------------------------------------------------------

    private const int _fogPerFrame = 16;

    private IEnumerator BuildFogNextFrame(int width, int height)
    {
        if (_isBuilding) yield break;
        _isBuilding = true;
        yield return WaitCache.EndOfFrame;

        _cellStep = GridManager.Instance?.CellStep ?? _cellStep;
        int stepPx = Mathf.RoundToInt(_cellStep * 16f);
        float cellSize = GridManager.Instance?.CellSize ?? (_cellStep - 0.04f);

        // Phase 1 : créer les overlays manquants — étalé sur plusieurs frames
        int created = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_overlays.ContainsKey((x, y))) continue;
                var cell = GridManager.Instance?.GetCell(x, y);
                if (cell != null && cell.IsMountainPlateau) continue;
                float wx = (x * stepPx) / 16f;
                float wy = (y * stepPx) / 16f;
                var sr = GetOrCreateOverlay(wx, wy, cellSize);
                _overlays[(x, y)] = sr;
                created++;
                if (created % _fogPerFrame == 0)
                    yield return null;
            }

        // Phase 2 : rafraîchir les états — étalé sur plusieurs frames
        var gm = GridManager.Instance;
        if (gm != null)
        {
            int refreshed = 0;
            for (int x2 = 0; x2 < width; x2++)
                for (int y2 = 0; y2 < height; y2++)
                {
                    UpdateCell(x2, y2, gm);
                    refreshed++;
                    if (refreshed % 32 == 0)
                        yield return null;
                }
        }

        _isBuilding = false;

        // Si nouvelle extension arrivée pendant le build — couvrir la nouvelle zone
        if (_pendingFogWidth > width || _pendingFogHeight > height)
            _buildCoroutine = StartCoroutine(BuildFogNextFrame(
                _pendingFogWidth, _pendingFogHeight));
    }

    // Création synchrone des overlays manquants — utilisée pendant ExtendGrid rapide
    private void BuildFogSync(int width, int height)
    {
        var gm = GridManager.Instance;
        int stepPx = Mathf.RoundToInt(_cellStep * 16f);
        float cellSize = gm?.CellSize ?? (_cellStep - 0.04f);

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (_overlays.ContainsKey((x, y))) continue;
                float wx = (x * stepPx) / 16f;
                float wy = (y * stepPx) / 16f;
                var sr = GetOrCreateOverlay(wx, wy, cellSize);
                _overlays[(x, y)] = sr;
            }
    }

    private SpriteRenderer GetOrCreateOverlay(float wx, float wy, float cellSize)
    {
        SpriteRenderer sr;
        if (_overlayPool.Count > 0)
        {
            sr = _overlayPool.Pop();
            sr.gameObject.SetActive(true);
            sr.transform.position = new Vector3(wx, wy, -0.05f);
        }
        else
        {
            var go = new GameObject("F");
            go.transform.position = new Vector3(wx, wy, -0.05f);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = _sortingLayer;
            sr.sortingOrder = _sortingOrder;
            sr.sprite = _whiteSprite;
            // Material partagé pour batching du fog
            // Material partagé — tous les overlays fog = 1 seul batch
            if (_fogMaterial != null) sr.sharedMaterial = _fogMaterial;
        }
        sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, _fogFull);
        sr.transform.localScale = new Vector3(cellSize, cellSize, 1f);
        return sr;
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    public void RefreshAll()
    {
        var gm = GridManager.Instance;
        if (gm == null) return;
        for (int x = 0; x < gm.Width; x++)
            for (int y = 0; y < gm.Height; y++)
                UpdateCell(x, y, gm);
    }

    private void UpdateCell(int x, int y, GridManager gm)
    {
        if (!_overlays.TryGetValue((x, y), out var sr) || sr == null) return;

        var cell = gm.GetCell(x, y);
        if (cell != null && cell.IsMountainPlateau)
        {
            sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, 0f);
            return;
        }

        if (cell != null && cell.IsRevealed)
        {
            sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, 0f);
            return;
        }
        if (!_enabled)
        {
            sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, 0f);
            return;
        }
        float alpha;
        if (_smoothFog)
        {
            float exactDist = GetExactDist(x, y, gm);
            alpha = GetSmoothAlpha(exactDist);
        }
        else
        {
            int dist = GetMinDist(x, y, gm);
            alpha = dist switch
            {
                1 => _fogDist1,
                2 => _fogDist2,
                3 => _fogDist3,
                4 => _fogDist4,
                _ => _fogFull
            };
        }
        sr.color = new Color(_fogColor.r, _fogColor.g, _fogColor.b, alpha);
    }
    private int GetMinDist(int x, int y, GridManager gm)
    {
        for (int d = 1; d <= _visibilityRange; d++)
            for (int dx = -d; dx <= d; dx++)
                for (int dy = -d; dy <= d; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != d) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= gm.Width || ny >= gm.Height) continue;
                    var c = gm.GetCell(nx, ny);
                    if (c != null && c.IsRevealed) return d;
                }
        return _visibilityRange + 1;
    }

    private float GetExactDist(int x, int y, GridManager gm)
    {
        // Limité au rayon de visibilité — pas de parcours de toute la grille
        float minDist = _visibilityRange + 1f;
        int r = _visibilityRange + 1;
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= gm.Width || ny >= gm.Height) continue;
                var c = gm.GetCell(nx, ny);
                if (c == null || !c.IsRevealed) continue;
                float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                if (d < minDist) minDist = d;
            }
        return minDist;
    }

    private float GetSmoothAlpha(float dist)
    {
        float[] keys = { 0f, 1f, 2f, 3f, 4f, 5f };
        float[] values = { 0f, _fogDist1, _fogDist2, _fogDist3, _fogDist4, _fogFull };

        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (dist >= keys[i] && dist <= keys[i + 1])
            {
                float t = (dist - keys[i]) / (keys[i + 1] - keys[i]);
                t = Mathf.SmoothStep(0f, 1f, t);
                return Mathf.Lerp(values[i], values[i + 1], t);
            }
        }
        return _fogFull;
    }

    // -------------------------------------------------------------------------
    // API
    // -------------------------------------------------------------------------

    public void SetEnabled(bool value)
    {
        _enabled = value;
        RefreshAll();
    }

    // ── Contrôle depuis GridLayerManager (transition grille) ─────────────────
    /// <summary>Cache tous les overlays fog actifs (à la racine scène).</summary>
    public void HideAll()
    {
        foreach (var sr in _overlays.Values)
            if (sr != null) sr.gameObject.SetActive(false);
    }

    /// <summary>Réaffiche tous les overlays fog.</summary>
    public void ShowAll()
    {
        foreach (var sr in _overlays.Values)
            if (sr != null) sr.gameObject.SetActive(true);
    }

    private void ClearAll()
    {
        _isBuilding = false;
        const int maxPool = 300; // limite la taille du pool
        int kept = 0;
        foreach (var sr in _overlays.Values)
        {
            if (sr == null) continue;
            if (kept < maxPool)
            {
                sr.gameObject.SetActive(false);
                sr.transform.SetParent(transform, false);
                _overlayPool.Push(sr);
                kept++;
            }
            else
            {
                Destroy(sr.gameObject); // détruire l'excédent
            }
        }
        _overlays.Clear();
    }

    private Sprite CreateWhiteSprite()
    {
        var tex = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4),
            new Vector2(0.5f, 0.5f), 4f);
    }
    public float GetFogAlpha(int x, int y)
    {
        if (_overlays != null && _overlays.TryGetValue((x, y), out var sr) && sr != null)
            return sr.color.a;
        return _fogFull;
    }
}