using UnityEngine;

/// <summary>
/// ForestCellView — case du biome Forêt.
/// Gère : sol procédural, arbres, chiffres, flag, spawn ennemis.
/// </summary>
public class ForestCellView : CellViewBase, ITreeLayer
{
    [Header("=== Sol Forêt ===")]
    [SerializeField] private Sprite _forestGroundHidden;
    [SerializeField] private Sprite _forestGroundRevealed;

    [Header("=== Arbres ===")]
    [SerializeField] private SpriteRenderer _treeLayerRenderer;
    [SerializeField] private Sprite _treeTrunkSprite;
    [SerializeField] private Sprite _treeCanopySprite;
    [SerializeField] private Sprite _treeCimeSprite;
    [SerializeField] private Sprite _treeBuissonSprite;

    [Header("=== Spéciaux ===")]
    [SerializeField] private Sprite _trapSprite;
    [SerializeField] private GameObject _trapPrefab;

    public enum TreeLayerType { Canopy, Trunk, Cime, Buisson }
    private TreeLayerType _treeLayer = TreeLayerType.Canopy;
    private EnemyInstance _enemyInstance;

    // =========================================================================
    // Init
    // =========================================================================

    public override void Initialize(Cell cell)
    {
        if (_forestGroundHidden != null) _hiddenGroundSprite = _forestGroundHidden;
        if (_forestGroundRevealed != null) _groundRevealedSprite = _forestGroundRevealed;
        base.Initialize(cell);
    }

    public void SetTreeLayer(TreeLayerType layer)
    {
        if (_treeLayer == layer) return;
        _treeLayer = layer;
        if (_cell?.State == CellState.Hidden) RefreshTreeLayer();
    }

    // =========================================================================
    // États
    // =========================================================================

    protected override void ShowHidden()
    {
        // Plateau montagne → rien
        if (_cell != null && _cell.IsMountainReserved)
        {
            SetBg(null);
            HideTreeLayer();
            HideIcon();
            HideNumber();
            return;
        }
        SetBg(_hiddenGroundSprite);
        HideIcon();
        HideNumber();
        RefreshTreeLayer();
    }
    

    protected override void ShowFlagged()
    {
        SetBg(_hiddenGroundSprite);
        HideNumber();
        SetIcon(_flagSprite);
        HideTreeLayer();
    }

    protected override void ShowRevealed()

    {
        if (_cell != null && _cell.IsMountainReserved)
        {
            SetBg(null);
            HideTreeLayer();
            HideIcon();
            HideNumber();
            return;
        }

        HideTreeLayer();

        // Sol procédural
        var gvs = GroundVariantSystem.Instance;
        var ground = (gvs != null)
            ? gvs.GetGroundSprite(_cell.X, _cell.Y) ?? _groundRevealedSprite
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
    // Spawn Ennemi
    // =========================================================================

    private void SpawnEnemyInstance(CellContent enemyType)
    {
        if (_enemyInstance != null) return;

        //Debug.Log("[ForestCellView] SpawnEnemy: " + enemyType);

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
    // Arbres
    // =========================================================================

    private void RefreshTreeLayer()
    {
        if (_treeLayerRenderer == null) return;
        if (_cell?.Biome == BiomeType.Forest)
        {
            _treeLayerRenderer.enabled = true;
            _treeLayerRenderer.sprite = GetTreeSprite(_treeLayer);
        }
        else
            HideTreeLayer();
    }

    private void HideTreeLayer()
    {
        if (_treeLayerRenderer != null)
            _treeLayerRenderer.enabled = false;
    }

    private Sprite GetTreeSprite(TreeLayerType t) => t switch
    {
        TreeLayerType.Trunk => _treeTrunkSprite,
        TreeLayerType.Canopy => _treeCanopySprite,
        TreeLayerType.Cime => _treeCimeSprite,
        TreeLayerType.Buisson => _treeBuissonSprite,
        _ => null
    };
}