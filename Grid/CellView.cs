using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// CellView — Rendu visuel d'une case.
/// Gère : 4 types d'arbres forêt, chiffres en sprites, icônes ennemis/trésors.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CellView : MonoBehaviour, IPointerClickHandler, ICellView
{
    // ─── Composants ───────────────────────────────────────────────────────────
    [Header("═══ Composants ═══")]
    [Tooltip("SpriteRenderer fond de case — glisse le ROOT Cell_Prefab ici")]
    [SerializeField] private SpriteRenderer _bgRenderer;

    [Tooltip("SpriteRenderer couche arbre — glisse l'enfant TreeLayer ici")]
    [SerializeField] private SpriteRenderer _treeLayerRenderer;

    [Tooltip("SpriteRenderer icône ennemi/trésor/flag — glisse l'enfant Icon ici")]
    [SerializeField] private SpriteRenderer _iconRenderer;

    [Tooltip("SpriteRenderer chiffre adjacence — glisse l'enfant NumberSprite ici")]
    [SerializeField] private SpriteRenderer _numberRenderer;

    // ─── Sprites Sol ──────────────────────────────────────────────────────────
    [Header("═══ Sprites — Sol ═══")]
    [Tooltip("Sol herbe découvert (case révélée vide ou chiffre)")]
    [SerializeField] private Sprite _groundRevealedSprite;

    [Tooltip("Sol sombre sous les arbres (case cachée forêt)")]
    [SerializeField] private Sprite _hiddenGroundSprite;

    [Tooltip("Drapeau")]
    [SerializeField] private Sprite _flagSprite;

    // ─── Sprites Arbres ───────────────────────────────────────────────────────
    [Header("═══ Sprites — Arbres Forêt ═══")]
    [Tooltip("TRONC — case cachée, case en dessous révélée, canopée au-dessus")]
    [SerializeField] private Sprite _treeTrunkSprite;

    [Tooltip("CANOPÉE — case cachée standard, tronc en dessous, autre canopée au-dessus")]
    [SerializeField] private Sprite _treeCanopySprite;

    [Tooltip("CIME — canopée du sommet (case au-dessus = révélée ou bord de grille)")]
    [SerializeField] private Sprite _treeCimeSprite;

    [Tooltip("BUISSON — tronc isolé (case en dessous ET au-dessus révélées)")]
    [SerializeField] private Sprite _treeBuissonSprite;

    // ─── Sprites Chiffres ─────────────────────────────────────────────────────
    [Header("═══ Sprites — Chiffres Adjacence ═══")]
    [Tooltip("Sprite du chiffre 1 (sans fond, par-dessus le biome)")]
    [SerializeField] private Sprite _number1Sprite;
    [SerializeField] private Sprite _number2Sprite;
    [SerializeField] private Sprite _number3Sprite;
    [SerializeField] private Sprite _number4Sprite;
    [SerializeField] private Sprite _number5Sprite;
    [SerializeField] private Sprite _number6Sprite;
    [SerializeField] private Sprite _number7Sprite;
    [SerializeField] private Sprite _number8Sprite;

    // ─── Sprites Ennemis ──────────────────────────────────────────────────────
    [Header("═══ Sprites — Ennemis ═══")]
    [SerializeField] private Sprite _wolfSprite;
    [SerializeField] private Sprite _bearSprite;
    [SerializeField] private Sprite _mercenarySprite;
    [SerializeField] private Sprite _banditSprite;
    [SerializeField] private Sprite _archerSprite;
    [SerializeField] private Sprite _bossSprite;

    // ─── Sprites Trésors ──────────────────────────────────────────────────────
    [Header("═══ Sprites — Trésors ═══")]
    [SerializeField] private Sprite _campfireSprite;
    [SerializeField] private Sprite _flowerSprite;
    [SerializeField] private Sprite _chestSprite;
    [SerializeField] private Sprite _fountainSprite;
    [SerializeField] private Sprite _scrollSprite;
    [SerializeField] private Sprite _trapSprite;
    [SerializeField] private GameObject _trapPrefab;

    // ─── Animation ────────────────────────────────────────────────────────────
    [Header("═══ Animation ═══")]
    [SerializeField] private float _revealAnimDuration = 0.12f;

    [Header("═══ Debug ═══")]
    [SerializeField] private bool _showDebugOnHover = false;

    // ─── Données internes ─────────────────────────────────────────────────────
    // ICellView implementation
    public int X => _cell?.X ?? 0;
    public int Y => _cell?.Y ?? 0;
    public void Refresh()
    {
        if (!_isInitialized || _cell == null) return;
        switch (_cell.State)
        {
            case CellState.Hidden: ShowHidden(); break;
            case CellState.Flagged: ShowFlagged(); break;
            case CellState.Revealed: ShowRevealed(); break;
        }
    }
    public void Initialize(Cell cell)
    {
        _cell = cell;
        _isInitialized = true;
        Refresh();
    }

    private Cell _cell;
    private bool _isInitialized;
    private Vector3 _originalScale;

    // ─── 4 types de rendu arbre ───────────────────────────────────────────────
    public enum TreeLayerType
    {
        Trunk,      // Tronc standard : canopée au-dessus, révélé en dessous
        Canopy,     // Canopée standard : au milieu d'un arbre
        Cime,       // Sommet d'arbre : rien de caché au-dessus
        Buisson     // Isolé : révélé en dessous ET au-dessus
    }
    private TreeLayerType _treeLayer = TreeLayerType.Canopy;

    // ─── API publique pour GridManager ────────────────────────────────────────
    public void SetTreeLayer(TreeLayerType newLayer)
    {
        if (_treeLayer == newLayer) return;
        _treeLayer = newLayer;
        if (_cell != null && _cell.State == CellState.Hidden)
            ShowHidden();
    }

    // ─── Archer auto-reveal ───────────────────────────────────────────────────

    private void OnEnable()
    {
    }

    private void OnDisable()
    {
    }


    private void ShowHidden()
    {
        SetBg(_hiddenGroundSprite);
        HideIcon();
        HideNumber();

        if (_treeLayerRenderer == null) return;

        if (_cell.Biome == BiomeType.Forest)
        {
            _treeLayerRenderer.enabled = true;
            _treeLayerRenderer.sprite = GetTreeSprite(_treeLayer);
        }
        else
        {
            _treeLayerRenderer.enabled = false;
        }
    }

    // ─── FLAGGÉ ───────────────────────────────────────────────────────────────
    private void ShowFlagged()
    {
        SetBg(_hiddenGroundSprite);
        HideTreeLayer();
        HideNumber();
        SetIcon(_flagSprite);
    }

    // ─── RÉVÉLÉ ───────────────────────────────────────────────────────────────
    // ─── Sprite de sol procédural ─────────────────────────────────────────────
    /// <summary>
    /// Retourne le sprite de sol pour cette case.
    /// Si GroundVariantSystem existe, utilise la logique procédurale.
    /// Sinon, fallback sur _groundRevealedSprite.
    /// </summary>
    private Sprite GetGroundSprite()
    {
        var gvs = GroundVariantSystem.Instance;
        if (gvs != null)
            return gvs.GetGroundSprite(_cell.X, _cell.Y) ?? _groundRevealedSprite;
        return _groundRevealedSprite;
    }

    private void ShowRevealed()
    {
        HideTreeLayer();

        switch (_cell.Content)
        {
            case CellContent.Empty:
                SetBg(GetGroundSprite()); HideIcon(); HideNumber();
                break;

            case CellContent.Number:
                // Fond biome procédural visible, chiffre sprite par-dessus
                SetBg(GetGroundSprite());
                HideIcon();
                ShowNumberSprite(_cell.AdjacentDangerCount);
                break;

            case CellContent.Enemy_Wolf:
            case CellContent.Enemy_Bear:
            case CellContent.Enemy_Mercenary:
            case CellContent.Enemy_BanditSword:
            case CellContent.Enemy_BanditArcher:
            case CellContent.Enemy_CoralReef:
            case CellContent.Enemy_Boss:
                SetBg(_groundRevealedSprite); HideNumber();
                SetIcon(GetEnemySprite(_cell.Content));
                SpawnEnemyInstance(_cell.Content);
                break;

            case CellContent.Treasure_Campfire:
            case CellContent.Treasure_Flower:
            case CellContent.Treasure_Chest:
            case CellContent.Treasure_Fountain:
            case CellContent.Treasure_Scroll:
                SetBg(_groundRevealedSprite); HideNumber();
                SetIcon(GetTreasureSprite(_cell.Content));
                break;

            case CellContent.Trap:
                SetBg(_groundRevealedSprite); HideNumber();
                SetIcon(_trapSprite);
                SpawnTrapInstance();
                break;

            default:
                SetBg(_groundRevealedSprite); HideIcon(); HideNumber();
                break;
        }
    }

    // ─── Affichage chiffre en sprite ──────────────────────────────────────────
    private void ShowNumberSprite(int count)
    {
        if (_numberRenderer == null) return;

        var sprite = GetNumberSprite(count);
        if (sprite != null)
        {
            _numberRenderer.enabled = true;
            _numberRenderer.sprite = sprite;
        }
        else
        {
            // Fallback : pas de sprite assigné pour ce chiffre, on cache
            _numberRenderer.enabled = false;
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────
    private void SetBg(Sprite sprite)
    {
        if (_bgRenderer == null) return;
        _bgRenderer.sprite = sprite;
        var c = _bgRenderer.color; c.a = 1f;
        _bgRenderer.color = c;
    }

    private void SetIcon(Sprite sprite)
    {
        if (_iconRenderer == null) return;
        _iconRenderer.enabled = sprite != null;
        _iconRenderer.sprite = sprite;
    }

    private void HideIcon() { if (_iconRenderer != null) _iconRenderer.enabled = false; }
    private void HideNumber() { if (_numberRenderer != null) _numberRenderer.enabled = false; }
    private void HideTreeLayer() { if (_treeLayerRenderer != null) _treeLayerRenderer.enabled = false; }

    // ─── Clics ────────────────────────────────────────────────────────────────
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_cell == null || !_isInitialized) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;

        // Apres apparition du heros, bloquer ouverture directe des cases non revelees
        // Seul le heros peut les decouvrir (en s approchant ou en sautant)
        if (HeroController.Instance != null
            && HeroController.Instance.HasAppeared
            && !_cell.IsRevealed)
        {
            // Bloquer dans tous les cas - HeroController gere la logique
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (_cell.IsRevealed) GridManager.Instance?.TryChordClick(_cell.X, _cell.Y);
            else { GridManager.Instance?.RevealCell(_cell.X, _cell.Y); PlayRevealAnimation(); }
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            GridManager.Instance?.ToggleFlag(_cell.X, _cell.Y);
        }
    }

    // ─── Animation révélation ─────────────────────────────────────────────────
    private void PlayRevealAnimation()
    {
        // Pas d'animation pendant un gros flood fill — évite 500 coroutines simultanées
        var gm = GridManager.Instance;
        if (gm != null && gm.IsPublishingBatch)
        {
            transform.localScale = _originalScale;
            return;
        }
        StopAllCoroutines();
        StartCoroutine(RevealAnimRoutine());
    }

    private System.Collections.IEnumerator RevealAnimRoutine()
    {
        float elapsed = 0f;
        Vector3 start = _originalScale * 0.75f;
        transform.localScale = start;
        while (elapsed < _revealAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _revealAnimDuration);
            transform.localScale = Vector3.Lerp(start, _originalScale, t);
            yield return null;
        }
        transform.localScale = _originalScale;
    }

    private void OnMouseEnter()
    {
        if (_showDebugOnHover && _cell != null) { }
    }

    // ─── Lookups sprites ──────────────────────────────────────────────────────
    private Sprite GetTreeSprite(TreeLayerType t) => t switch
    {
        TreeLayerType.Trunk => _treeTrunkSprite,
        TreeLayerType.Canopy => _treeCanopySprite,
        TreeLayerType.Cime => _treeCimeSprite,
        TreeLayerType.Buisson => _treeBuissonSprite,
        _ => _treeCanopySprite
    };

    private Sprite GetNumberSprite(int n) => n switch
    {
        1 => _number1Sprite,
        2 => _number2Sprite,
        3 => _number3Sprite,
        4 => _number4Sprite,
        5 => _number5Sprite,
        6 => _number6Sprite,
        7 => _number7Sprite,
        8 => _number8Sprite,
        _ => null
    };

    private Sprite GetEnemySprite(CellContent c) => c switch
    {
        CellContent.Enemy_Wolf => _wolfSprite,
        CellContent.Enemy_Bear => _bearSprite,
        CellContent.Enemy_Mercenary => _mercenarySprite,
        CellContent.Enemy_BanditSword => _banditSprite,
        CellContent.Enemy_BanditArcher => _archerSprite,
        CellContent.Enemy_Boss => _bossSprite,
        _ => null
    };

    private Sprite GetTreasureSprite(CellContent c) => c switch
    {
        CellContent.Treasure_Campfire => _campfireSprite,
        CellContent.Treasure_Flower => _flowerSprite,
        CellContent.Treasure_Chest => _chestSprite,
        CellContent.Treasure_Fountain => _fountainSprite,
        CellContent.Treasure_Scroll => _scrollSprite,
        _ => null
    };
    // -------------------------------------------------------------------------
    // Spawn EnemyInstance sur la case revelee
    // -------------------------------------------------------------------------

    private EnemyInstance _enemyInstance;

    private void SpawnTrapInstance()
    {
        if (_trapPrefab == null) return;
        if (_cell == null) return;

        var gm = GridManager.Instance;
        float cs = gm?.CellStep ?? 1.05f;
        var pos = new UnityEngine.Vector2Int(_cell.X, _cell.Y);

        // Spawner a la RACINE (pas enfant de CellView)
        // pour que le collider soit en world space et detectable
        var go = Instantiate(_trapPrefab, null);
        var trap = go.GetComponent<TrapInstance>();
        if (trap != null)
            trap.Initialize(pos, cs);
    }

    private void SpawnEnemyInstance(CellContent enemyType)
    {
        if (_enemyInstance != null) return;

        HideIcon();
        if (_iconRenderer != null) _iconRenderer.enabled = false;

        // Utiliser EnemyDatabase si disponible
        var db = EnemyDatabase.Instance;
        var data = db?.Get(enemyType);

        GameObject go;

        if (data != null && data.prefab != null)
        {
            // Spawner le prefab configure avec tous ses sprites
            go = Instantiate(data.prefab, null);
            go.transform.position = transform.position + new Vector3(0f, 0f, -0.15f);
        }
        else
        {
            // Fallback : creation dynamique
            go = new GameObject("E");
            go.transform.position = transform.position + new Vector3(0f, 0f, -0.15f);

            var sr2 = go.AddComponent<SpriteRenderer>();
            sr2.sprite = GetEnemySprite(enemyType);
            sr2.sortingLayerName = "CellContent";
            sr2.sortingOrder = 7;

            var col2 = go.AddComponent<BoxCollider2D>();
            col2.size = Vector2.one * 0.9f;
        }

        _enemyInstance = go.GetComponent<EnemyInstance>();
        if (_enemyInstance == null) _enemyInstance = go.AddComponent<EnemyInstance>();
        _enemyInstance.Initialize(enemyType, _cell.X, _cell.Y);
    }
}