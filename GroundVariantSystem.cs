using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GroundVariantSystem — Sol procédural pour tous les biomes.
///
/// FORÊT  (4 sprites) : prairie / haute herbe / fougère / prairie fleurie
///   Distribués selon la distance aux arbres cachés + Perlin.
///
/// GROTTE (7 sprites) :
///   Haute herbe   → 3 caillouteux  (rock1 / rock2 / rock3)
///   Fougère       → 1 grosse pierre (bigRock)
///   Prairie basse → 2 sol tassé    (earth1 / earth2)
///   Prairie fleurie → 1 champignon  (mushroom)
///   Distribution : distance aux bords de la grotte + Perlin.
/// </summary>
public class GroundVariantSystem : MonoBehaviour
{
    public static GroundVariantSystem Instance { get; private set; }

    // ── FORÊT ─────────────────────────────────────────────────────────────────

    [Header("=== Sol Forêt ===")]
    [SerializeField] private Sprite _prairieSprite;
    [SerializeField] private Sprite _tallGrassSprite;
    [SerializeField] private Sprite _fernSprite;
    [SerializeField] private Sprite _flowerPrairieSprite;

    // ── GROTTE ────────────────────────────────────────────────────────────────

    [Header("=== Grotte — Haute herbe (3 caillouteux) ===")]
    [Tooltip("Sol caillouteux 1 — cailloux dispersés")]
    [SerializeField] private Sprite _caveRock1;
    [Tooltip("Sol caillouteux 2 — formation rocheuse")]
    [SerializeField] private Sprite _caveRock2;
    [Tooltip("Sol caillouteux 3 — accumulation de petits rochers")]
    [SerializeField] private Sprite _caveRock3;

    [Header("=== Grotte — Fougère (grosse pierre) ===")]
    [Tooltip("Sol avec grosse pierre — équivalent fougère, proche des parois")]
    [SerializeField] private Sprite _caveBigRock;

    [Header("=== Grotte — Prairie basse (2 terres tassées) ===")]
    [Tooltip("Sol tassé 1 — terre compactée principale")]
    [SerializeField] private Sprite _caveEarth1;
    [Tooltip("Sol tassé 2 — terre compactée variante")]
    [SerializeField] private Sprite _caveEarth2;

    [Header("=== Grotte — Prairie fleurie (champignons) ===")]
    [Tooltip("Sol champignon — équivalent prairie fleurie, zones ouvertes")]
    [SerializeField] private Sprite _caveMushroom;

    // ── PARAMÈTRES COMMUNS ────────────────────────────────────────────────────

    [Header("=== Procédural (commun) ===")]
    [SerializeField, Range(0.05f, 0.5f)] private float _perlinScale = 0.15f;
    [SerializeField] private int _seed = 0;

    // ── PARAMÈTRES FORÊT ──────────────────────────────────────────────────────

    [Header("=== Forêt — Bord arbre (dist 1) ===")]
    [SerializeField, Range(0f, 1f)] private float _borderTallGrass = 0.60f;
    [SerializeField, Range(0f, 1f)] private float _borderFernInGrass = 0.25f;

    [Header("=== Forêt — Semi-ouvert (dist 2-3) ===")]
    [SerializeField, Range(0f, 1f)] private float _semiTallGrass = 0.45f;
    [SerializeField, Range(0f, 1f)] private float _semiFernInGrass = 0.15f;
    [SerializeField, Range(0f, 1f)] private float _semiFlower = 0.10f;

    [Header("=== Forêt — Ouvert (dist 4+) ===")]
    [SerializeField, Range(0f, 1f)] private float _openFlower = 0.20f;
    [SerializeField, Range(0f, 1f)] private float _openTallGrass = 0.10f;
    [SerializeField, Range(0.3f, 0.7f)] private float _pathThreshold = 0.45f;

    // ── PARAMÈTRES GROTTE ─────────────────────────────────────────────────────

    [Header("=== Grotte — Bande rocheuse (cases depuis le bord) ===")]
    [SerializeField, Range(1, 6)] private int _caveRockyBorderDist = 2;

    [Header("=== Grotte — Poids zone rocheuse (proche parois) ===")]
    [SerializeField, Range(0f, 1f)] private float _bRock1 = 0.30f;
    [SerializeField, Range(0f, 1f)] private float _bRock2 = 0.22f;
    [SerializeField, Range(0f, 1f)] private float _bRock3 = 0.18f;
    [SerializeField, Range(0f, 1f)] private float _bBigRock = 0.14f;
    [SerializeField, Range(0f, 1f)] private float _bEarth1 = 0.09f;
    [SerializeField, Range(0f, 1f)] private float _bEarth2 = 0.07f;
    // Reste (0.00 après arrondi) → mushroom (ne pousse pas trop près des murs)

    [Header("=== Grotte — Poids zone centrale (loin des parois) ===")]
    [SerializeField, Range(0f, 1f)] private float _oEarth1 = 0.38f;
    [SerializeField, Range(0f, 1f)] private float _oEarth2 = 0.28f;
    [SerializeField, Range(0f, 1f)] private float _oMushroom = 0.12f;
    [SerializeField, Range(0f, 1f)] private float _oRock1 = 0.08f;
    [SerializeField, Range(0f, 1f)] private float _oRock2 = 0.07f;
    [SerializeField, Range(0f, 1f)] private float _oRock3 = 0.04f;
    [SerializeField, Range(0f, 1f)] private float _oBigRock = 0.03f;
    // earth1 + earth2 + mushroom + rock1..3 + bigRock doit ≤ 1.0

    // ── ÉTAT INTERNE ──────────────────────────────────────────────────────────

    private System.Random _rng;
    private Vector2 _perlinOffset;
    private readonly Dictionary<(int, int), Sprite> _forestCache = new();
    private readonly Dictionary<(int, int), Sprite> _caveCache = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    private void OnEnable() => EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    private void OnDisable() => EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);

    private void OnRunStarted(OnRunStarted e)
    {
        int seed = _seed != 0 ? _seed : UnityEngine.Random.Range(1, 99999);
        _rng = new System.Random(seed);
        _perlinOffset = new Vector2((float)_rng.NextDouble() * 1000f,
                                    (float)_rng.NextDouble() * 1000f);
        _forestCache.Clear();
        _caveCache.Clear();
        Debug.Log("<color=#88FF44>[GroundVariant]</color> seed=" + seed);
    }

    // ── API FORÊT ─────────────────────────────────────────────────────────────

    public Sprite GetGroundSprite(int x, int y)
    {
        if (_forestCache.TryGetValue((x, y), out var c)) return c;
        var gm = GridManager.Instance;
        if (gm == null || gm.Grid == null) return _prairieSprite;
        int dist = DistToHiddenForest(gm.Grid, x, y, gm.Width, gm.Height);
        float perlin = Perlin(x, y);
        float roll = Roll();
        var result = ChooseForest(dist, perlin, roll);
        _forestCache[(x, y)] = result;
        return result;
    }

    // ── API GROTTE ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sprite de sol pour une case de grotte.
    /// caveWidth / caveHeight : dimensions totales de la grotte en cases
    /// (nécessaires pour calculer la distance aux bords).
    /// </summary>
    public Sprite GetCaveGroundSprite(int x, int y, int caveWidth, int caveHeight)
    {
        if (_caveCache.TryGetValue((x, y), out var c)) return c;
        int distEdge = Mathf.Min(x, y, caveWidth - 1 - x, caveHeight - 1 - y);
        float perlin = Perlin(x, y);
        float roll = Roll();
        var result = ChooseCave(distEdge, perlin, roll);
        _caveCache[(x, y)] = result;
        return result;
    }

    // ── CHOIX FORÊT ──────────────────────────────────────────────────────────

    private Sprite ChooseForest(int dist, float perlin, float roll)
    {
        if (dist <= 1)
        {
            if (roll < _borderTallGrass) return S(_tallGrassSprite);
            if (roll < _borderTallGrass + _borderFernInGrass) return S(_fernSprite, _tallGrassSprite);
            return _prairieSprite;
        }
        if (dist <= 3)
        {
            if (roll < _semiTallGrass) return S(_tallGrassSprite);
            if (roll < _semiTallGrass + _semiFernInGrass) return S(_fernSprite, _tallGrassSprite);
            if (roll < _semiTallGrass + _semiFernInGrass + _semiFlower) return S(_flowerPrairieSprite);
            return _prairieSprite;
        }
        if (perlin > _pathThreshold) return _prairieSprite;
        if (roll < _openFlower) return S(_flowerPrairieSprite);
        if (roll < _openFlower + _openTallGrass) return S(_tallGrassSprite);
        return _prairieSprite;
    }

    // ── CHOIX GROTTE ─────────────────────────────────────────────────────────

    private Sprite ChooseCave(int distEdge, float perlin, float roll)
    {
        if (distEdge <= _caveRockyBorderDist)
        {
            // Zone rocheuse — proche des parois
            // Grosse pierre apparaît surtout ici (comme la fougère près des arbres)
            float t = 0f;
            float r1 = t += _bRock1;
            float r2 = t += _bRock2;
            float r3 = t += _bRock3;
            float r4 = t += _bBigRock;
            float r5 = t += _bEarth1;
            float r6 = t += _bEarth2;

            if (roll < r1) return S(_caveRock1, _caveEarth1);
            if (roll < r2) return S(_caveRock2, _caveRock1);
            if (roll < r3) return S(_caveRock3, _caveRock1);
            if (roll < r4) return S(_caveBigRock, _caveRock1);
            if (roll < r5) return S(_caveEarth1);
            if (roll < r6) return S(_caveEarth2, _caveEarth1);
            // Reste : champignon (rare près des bords, humidité des parois)
            return S(_caveMushroom, _caveEarth1);
        }
        else
        {
            // Zone centrale — sol tassé dominant, champignons, cailloux rares
            float t = 0f;
            float e1 = t += _oEarth1;
            float e2 = t += _oEarth2;
            float mu = t += _oMushroom;
            float r1 = t += _oRock1;
            float r2 = t += _oRock2;
            float r3 = t += _oRock3;
            float r4 = t += _oBigRock;

            if (roll < e1) return perlin > 0.5f ? S(_caveEarth1) : S(_caveEarth2, _caveEarth1);
            if (roll < e2) return perlin > 0.5f ? S(_caveEarth2) : S(_caveEarth1);
            if (roll < mu) return S(_caveMushroom, _caveEarth1);
            if (roll < r1) return S(_caveRock1, _caveEarth1);
            if (roll < r2) return S(_caveRock2, _caveRock1);
            if (roll < r3) return S(_caveRock3, _caveRock1);
            if (roll < r4) return S(_caveBigRock, _caveRock2);
            return S(_caveEarth1);
        }
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────

    private int DistToHiddenForest(Cell[,] grid, int x, int y, int w, int h)
    {
        for (int d = 1; d <= 6; d++)
            for (int dx = -d; dx <= d; dx++)
                for (int dy = -d; dy <= d; dy++)
                {
                    if (Mathf.Abs(dx) != d && Mathf.Abs(dy) != d) continue;
                    int nx = x + dx, ny = y + dy;
                    if (!MinesweeperLogic.IsInBounds(nx, ny, w, h)) continue;
                    if (!grid[nx, ny].IsRevealed && grid[nx, ny].Biome == BiomeType.Forest)
                        return d;
                }
        return 99;
    }

    private float Perlin(int x, int y) =>
        Mathf.PerlinNoise((_perlinOffset.x + x) * _perlinScale,
                          (_perlinOffset.y + y) * _perlinScale);

    private float Roll() => _rng != null ? (float)_rng.NextDouble() : UnityEngine.Random.value;

    private Sprite S(Sprite a, Sprite b = null)
    {
        if (a != null) return a;
        if (b != null) return b;
        return _prairieSprite ?? _caveEarth1;
    }

    public void ClearCache() { _forestCache.Clear(); _caveCache.Clear(); }
}