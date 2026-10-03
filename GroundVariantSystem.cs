using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GroundVariantSystem — Sol procédural pour tous les biomes.
///
/// FORÊT  (4 sprites) : prairie / haute herbe / fougère / prairie fleurie
/// GROTTE (7 sprites) : caillouteux / grosse pierre / sol tassé / champignon
/// PLAGE  (6 sprites) : sable tassé / sable haut / sable graminé ×2 / fougère plage / coquillages ×3
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
    [SerializeField] private Sprite _caveRock1;
    [SerializeField] private Sprite _caveRock2;
    [SerializeField] private Sprite _caveRock3;

    [Header("=== Grotte — Fougère (grosse pierre) ===")]
    [SerializeField] private Sprite _caveBigRock;

    [Header("=== Grotte — Prairie basse (2 terres tassées) ===")]
    [SerializeField] private Sprite _caveEarth1;
    [SerializeField] private Sprite _caveEarth2;

    [Header("=== Grotte — Prairie fleurie (champignons) ===")]
    [SerializeField] private Sprite _caveMushroom;

    // ── PLAGE ─────────────────────────────────────────────────────────────────

    [Header("=== Sol Plage ===")]
    [Tooltip("Sable tassé — centre des zones révélées (rayon restreint)")]
    [SerializeField] private Sprite _beachPackedSand;

    [Tooltip("Sable haut — plus proche des palmiers (rayon agrandi vs forêt)")]
    [SerializeField] private Sprite _beachTallSand;

    [Tooltip("Sable graminé 1 — bords zones révélées, alternés avec graminé 2")]
    [SerializeField] private Sprite _beachGrass1;

    [Tooltip("Sable graminé 2 — bords zones révélées, alternés avec graminé 1")]
    [SerializeField] private Sprite _beachGrass2;

    [Tooltip("Fougère plage — bio-indicateur près des palmiers")]
    [SerializeField] private Sprite _beachFern;

    [Header("=== Plage — Coquillages (1-2 par chunk) ===")]
    [SerializeField] private Sprite _beachShell1;
    [SerializeField] private Sprite _beachShell2;
    [SerializeField] private Sprite _beachShell3;

    // ── PARAMÈTRES FORÊT ──────────────────────────────────────────────────────

    [Header("=== Paramètres Forêt ===")]
    [SerializeField] private int _seed = 0;
    [SerializeField, Range(0.05f, 0.3f)] private float _perlinScale = 0.15f;
    [SerializeField, Range(0f, 1f)] private float _borderTallGrass = 0.55f;
    [SerializeField, Range(0f, 1f)] private float _borderFernInGrass = 0.20f;
    [SerializeField, Range(0f, 1f)] private float _semiTallGrass = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _semiFernInGrass = 0.10f;
    [SerializeField, Range(0f, 1f)] private float _semiFlower = 0.05f;
    [SerializeField, Range(0f, 1f)] private float _openFlower = 0.04f;
    [SerializeField, Range(0f, 1f)] private float _openTallGrass = 0.08f;
    [SerializeField, Range(0.3f, 0.7f)] private float _pathThreshold = 0.55f;

    // ── PARAMÈTRES GROTTE ─────────────────────────────────────────────────────

    [Header("=== Paramètres Grotte ===")]
    [SerializeField, Range(1, 5)] private int _caveRockyBorderDist = 2;

    [Header("=== Grotte — Poids zone rocheuse ===")]
    [SerializeField, Range(0f, 1f)] private float _bRock1 = 0.22f;
    [SerializeField, Range(0f, 1f)] private float _bRock2 = 0.18f;
    [SerializeField, Range(0f, 1f)] private float _bRock3 = 0.15f;
    [SerializeField, Range(0f, 1f)] private float _bBigRock = 0.14f;
    [SerializeField, Range(0f, 1f)] private float _bEarth1 = 0.09f;
    [SerializeField, Range(0f, 1f)] private float _bEarth2 = 0.07f;

    [Header("=== Grotte — Poids zone centrale ===")]
    [SerializeField, Range(0f, 1f)] private float _oEarth1 = 0.38f;
    [SerializeField, Range(0f, 1f)] private float _oEarth2 = 0.28f;
    [SerializeField, Range(0f, 1f)] private float _oMushroom = 0.12f;
    [SerializeField, Range(0f, 1f)] private float _oRock1 = 0.08f;
    [SerializeField, Range(0f, 1f)] private float _oRock2 = 0.07f;
    [SerializeField, Range(0f, 1f)] private float _oRock3 = 0.04f;
    [SerializeField, Range(0f, 1f)] private float _oBigRock = 0.03f;

    // ── PARAMÈTRES PLAGE ──────────────────────────────────────────────────────

    [Header("=== Paramètres Plage ===")]
    [Tooltip("Dist max pour sable graminé + fougère (plus grand que forêt)")]
    [SerializeField, Range(1, 4)] private int _beachGrassBorderDist = 2;
    [Tooltip("Dist max pour sable haut (agrandi vs forêt haute herbes)")]
    [SerializeField, Range(2, 6)] private int _beachTallSandDist = 4;
    [Tooltip("Dist min pour sable tassé centre (plus restreint que prairie)")]
    [SerializeField, Range(3, 8)] private int _beachPackedMinDist = 5;
    [Tooltip("Probabilité coquillage par case (1-2 par chunk ≈ 0.005-0.01)")]
    [SerializeField, Range(0f, 0.03f)] private float _beachShellChance = 0.008f;
    [Tooltip("Proba fougère plage près des palmiers")]
    [SerializeField, Range(0f, 0.5f)] private float _beachFernChance = 0.15f;
    [Tooltip("Proba sable graminé dans zone bord")]
    [SerializeField, Range(0f, 1f)] private float _beachGrassChance = 0.50f;
    [Tooltip("Proba sable haut dans zone intermédiaire")]
    [SerializeField, Range(0f, 1f)] private float _beachTallSandChance = 0.40f;

    // ── ÉTAT INTERNE ──────────────────────────────────────────────────────────

    private System.Random _rng;
    private Vector2 _perlinOffset;
    private readonly Dictionary<(int, int), Sprite> _forestCache = new();
    private readonly Dictionary<(int, int), Sprite> _caveCache = new();
    private readonly Dictionary<(int, int), Sprite> _beachCache = new();

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
        _beachCache.Clear();
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

    // ── API PLAGE ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sprite de sol pour une case de plage.
    /// Même pattern que forêt : distance aux palmiers cachés + Perlin.
    /// </summary>
    public Sprite GetBeachGroundSprite(int x, int y)
    {
        if (_beachCache.TryGetValue((x, y), out var c)) return c;
        var gm = GridManager.Instance;
        if (gm == null || gm.Grid == null) return _beachPackedSand;
        int dist = DistToHiddenBeach(gm.Grid, x, y, gm.Width, gm.Height);
        float perlin = Perlin(x, y);
        float roll = Roll();
        var result = ChooseBeach(dist, perlin, roll, x, y);
        _beachCache[(x, y)] = result;
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
            return S(_caveMushroom, _caveEarth1);
        }
        else
        {
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

    // ── CHOIX PLAGE ──────────────────────────────────────────────────────────

    /// <summary>
    /// Distribution plage :
    ///   dist ≤ beachGrassBorderDist  → sable graminé 1/2 (alternés) + fougère plage
    ///   dist ≤ beachTallSandDist     → sable haut (rayon agrandi)
    ///   dist ≥ beachPackedMinDist    → sable tassé (centre restreint)
    ///   partout                      → coquillages rares (1-2 par chunk)
    /// </summary>
    private Sprite ChooseBeach(int dist, float perlin, float roll, int x, int y)
    {
        // Coquillages — très rares, dispersés partout
        if (roll < _beachShellChance)
        {
            int shellIdx = (x * 31 + y * 17) % 3; // pseudo-random par position
            return shellIdx switch
            {
                0 => S(_beachShell1, _beachPackedSand),
                1 => S(_beachShell2, _beachPackedSand),
                _ => S(_beachShell3, _beachPackedSand)
            };
        }

        // Zone bord — très proche des palmiers
        if (dist <= _beachGrassBorderDist)
        {
            // Fougère plage — bio-indicateur comme en forêt
            if (roll < _beachFernChance)
                return S(_beachFern, _beachGrass1);

            // Sable graminé 1 et 2 alternés (damier par position)
            if (roll < _beachFernChance + _beachGrassChance)
            {
                bool alt = (x + y) % 2 == 0;
                return alt ? S(_beachGrass1, _beachPackedSand)
                           : S(_beachGrass2, _beachPackedSand);
            }

            // Reste : sable haut
            return S(_beachTallSand, _beachPackedSand);
        }

        // Zone intermédiaire — sable haut (rayon agrandi vs forêt)
        if (dist <= _beachTallSandDist)
        {
            if (roll < _beachTallSandChance)
                return S(_beachTallSand, _beachPackedSand);

            // Sable graminé rare en zone intermédiaire
            if (roll < _beachTallSandChance + 0.10f)
            {
                bool alt = (x + y) % 2 == 0;
                return alt ? S(_beachGrass1, _beachPackedSand)
                           : S(_beachGrass2, _beachPackedSand);
            }

            return S(_beachPackedSand);
        }

        // Zone centre — sable tassé dominant (rayon restreint)
        if (perlin > _pathThreshold)
            return S(_beachPackedSand);

        // Sable haut sporadique même au centre
        if (roll < 0.06f)
            return S(_beachTallSand, _beachPackedSand);

        return S(_beachPackedSand);
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

    /// <summary>Distance à la case plage cachée la plus proche (palmier).</summary>
    private int DistToHiddenBeach(Cell[,] grid, int x, int y, int w, int h)
    {
        for (int d = 1; d <= 8; d++) // rayon plus grand que forêt
            for (int dx = -d; dx <= d; dx++)
                for (int dy = -d; dy <= d; dy++)
                {
                    if (Mathf.Abs(dx) != d && Mathf.Abs(dy) != d) continue;
                    int nx = x + dx, ny = y + dy;
                    if (!MinesweeperLogic.IsInBounds(nx, ny, w, h)) continue;
                    if (!grid[nx, ny].IsRevealed && grid[nx, ny].Biome == BiomeType.Beach)
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
        return _prairieSprite ?? _caveEarth1 ?? _beachPackedSand;
    }

    public void ClearCache()
    {
        _forestCache.Clear();
        _caveCache.Clear();
        _beachCache.Clear();
    }
}