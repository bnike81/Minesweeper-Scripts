using UnityEngine;

/// <summary>
/// BeachCellView — case du biome Plage.
/// Hérite de CellViewBase (OnPointerClick, Refresh, ICellView inclus).
/// Même architecture que ForestCellView.
/// </summary>
public class BeachCellView : CellViewBase, ITreeLayer
{
    [Header("=== Sol Plage ===")]
    [SerializeField] private Sprite _beachGroundHidden;
    [SerializeField] private Sprite _beachGroundRevealed;

    [Header("=== Palmiers ===")]
    [SerializeField] private SpriteRenderer _treeLayerRenderer;
    [SerializeField] private Sprite _palmTrunkSprite;
    [SerializeField] private Sprite _palmCanopySprite;
    [SerializeField] private Sprite _palmCimeSprite;
    [SerializeField] private Sprite _palmBuissonSprite;

    [Header("=== Canopée Transition ===")]
    [SerializeField] private Sprite _palmCanopyConnectTreeAbove;
    [SerializeField] private Sprite _palmCanopyConnectTreeBelow;

    [Header("=== Spéciaux ===")]
    [SerializeField] private Sprite _trapSprite;

    private ForestCellView.TreeLayerType _treeLayer = ForestCellView.TreeLayerType.Canopy;
    private EnemyInstance _enemyInstance;
    private bool _isCanopyTransition;
    private bool _canopyTransitionAbove;

    // =========================================================================
    // Init — même pattern que ForestCellView
    // =========================================================================

    public override void Initialize(Cell cell)
    {
        if (_beachGroundHidden != null) _hiddenGroundSprite = _beachGroundHidden;
        if (_beachGroundRevealed != null) _groundRevealedSprite = _beachGroundRevealed;
        base.Initialize(cell);
    }

    public void SetTreeLayer(ForestCellView.TreeLayerType layer)
    {
        if (_treeLayer == layer) return;
        _treeLayer = layer;
        if (_cell != null && _cell.State == CellState.Hidden) RefreshTreeLayer();
    }

    public void SetCanopyTransition(bool isTransition, bool isAbove)
    {
        _isCanopyTransition = isTransition;
        _canopyTransitionAbove = isAbove;
        if (_cell != null && _cell.State == CellState.Hidden) RefreshTreeLayer();
    }

    // =========================================================================
    // HIDDEN — sol sable sombre + palmier selon tree layer
    // =========================================================================

    protected override void ShowHidden()
    {
        if (_cell != null && _cell.IsMountainReserved)
        {
            SetBg(null); HideTreeLayer(); HideIcon(); HideNumber(); return;
        }
        SetBg(_hiddenGroundSprite);
        HideIcon();
        HideNumber();
        RefreshTreeLayer();
    }

    // =========================================================================
    // FLAGGED
    // =========================================================================

    protected override void ShowFlagged()
    {
        SetBg(_hiddenGroundSprite);
        HideNumber();
        SetIcon(_flagSprite);
        HideTreeLayer();
    }

    // =========================================================================
    // REVEALED — sol procédural plage + ennemis + chiffres
    // =========================================================================

    protected override void ShowRevealed()
    {
        if (_cell != null && _cell.IsMountainReserved)
        {
            SetBg(null); HideTreeLayer(); HideIcon(); HideNumber(); return;
        }

        HideTreeLayer();

        // Sol procédural plage
        var gvs = GroundVariantSystem.Instance;
        var ground = (gvs != null)
            ? gvs.GetBeachGroundSprite(_cell.X, _cell.Y) ?? _groundRevealedSprite
            : _groundRevealedSprite;
        SetBg(ground);

        switch (_cell.Content)
        {
            case CellContent.Empty:
                HideIcon();
                HideNumber();
                break;

            case CellContent.Number:
                HideIcon();
                ShowNumber(_cell.AdjacentDangerCount);
                break;

            case CellContent.Trap:
                HideNumber();
                SetIcon(_trapSprite);
                break;

            default:
                HideNumber();
                HideIcon();
                SpawnEnemyInstance(_cell.Content);
                break;
        }
    }

    // =========================================================================
    // ENEMY SPAWN — identique à ForestCellView
    // =========================================================================

    private void SpawnEnemyInstance(CellContent enemyType)
    {
        if (_enemyInstance != null) return;

        var db = EnemyDatabase.Instance;
        var data = db?.Get(enemyType);

        GameObject go;
        if (data != null && data.prefab != null)
        {
            go = Instantiate(data.prefab, null);
            go.transform.position = transform.position + new Vector3(0f, 0f, -0.15f);
        }
        else
        {
            go = new GameObject("E");
            go.transform.position = transform.position + new Vector3(0f, 0f, -0.15f);
            var sr2 = go.AddComponent<SpriteRenderer>();
            sr2.sortingLayerName = "CellContent";
            sr2.sortingOrder = 7;
            var col2 = go.AddComponent<BoxCollider2D>();
            col2.size = Vector2.one * 0.9f;
        }

        _enemyInstance = go.GetComponent<EnemyInstance>();
        if (_enemyInstance == null) _enemyInstance = go.AddComponent<EnemyInstance>();
        _enemyInstance.Initialize(enemyType, _cell.X, _cell.Y);
    }

    // =========================================================================
    // TREE LAYER — palmiers avec transition canopée
    // =========================================================================

    private void RefreshTreeLayer()
    {
        if (_treeLayerRenderer == null) return;
        if (_cell?.Biome == BiomeType.Beach)
        {
            _treeLayerRenderer.enabled = true;
            if (_isCanopyTransition && _treeLayer == ForestCellView.TreeLayerType.Canopy)
            {
                _treeLayerRenderer.sprite = _canopyTransitionAbove
                    ? _palmCanopyConnectTreeAbove
                    : _palmCanopyConnectTreeBelow;
            }
            else
            {
                _treeLayerRenderer.sprite = GetPalmSprite(_treeLayer);
            }
        }
        else
            HideTreeLayer();
    }

    private void HideTreeLayer()
    {
        if (_treeLayerRenderer != null) _treeLayerRenderer.enabled = false;
    }

    private Sprite GetPalmSprite(ForestCellView.TreeLayerType t) => t switch
    {
        ForestCellView.TreeLayerType.Trunk => _palmTrunkSprite,
        ForestCellView.TreeLayerType.Canopy => _palmCanopySprite,
        ForestCellView.TreeLayerType.Cime => _palmCimeSprite,
        ForestCellView.TreeLayerType.Buisson => _palmBuissonSprite,
        _ => null
    };

    public Sprite GetCanopyConnectAbove() => _palmCanopyConnectTreeAbove;
    public Sprite GetCanopyConnectBelow() => _palmCanopyConnectTreeBelow;
}