using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// CellViewBase — socle universel de toutes les cases.
/// Gère : état hidden/revealed/flagged, chiffres adjacents, flag, clic.
/// Indépendant du biome — chaque biome hérite de cette classe.
/// </summary>
public class CellViewBase : MonoBehaviour, IPointerClickHandler, ICellView
{
    // =========================================================================
    // Inspector — Base (commun à tous les biomes)
    // =========================================================================

    [Header("=== Renderers ===")]
    [SerializeField] protected SpriteRenderer _bgRenderer;
    [SerializeField] protected SpriteRenderer _iconRenderer;
    [SerializeField] protected SpriteRenderer _numberRenderer;

    [Header("=== Sprites Base ===")]
    [SerializeField] protected Sprite _flagSprite;

    // Sprites sol — définis et assignés par chaque biome dans Initialize()
    protected Sprite _groundRevealedSprite;
    protected Sprite _hiddenGroundSprite;

    [Header("=== Chiffres ===")]
    [SerializeField] private Sprite _number1Sprite;
    [SerializeField] private Sprite _number2Sprite;
    [SerializeField] private Sprite _number3Sprite;
    [SerializeField] private Sprite _number4Sprite;
    [SerializeField] private Sprite _number5Sprite;
    [SerializeField] private Sprite _number6Sprite;
    [SerializeField] private Sprite _number7Sprite;
    [SerializeField] private Sprite _number8Sprite;

    [Header("=== Animation ===")]
    [SerializeField] private float _revealAnimDuration = 0.12f;
    [SerializeField] private bool _showDebugOnHover = false;

    // =========================================================================
    // État interne
    // =========================================================================

    protected Cell _cell;
    private bool _isInitialized;

    public int X => _cell?.X ?? 0;
    public int Y => _cell?.Y ?? 0;

    // =========================================================================
    // Init
    // =========================================================================

    // ─── Archer auto-reveal ───────────────────────────────────────────────────

    private void OnEnable()
    {
    }

    private void OnDisable()
    {
    }



    // ─── Initialisation ───────────────────────────────────────────────────────

    public virtual void Initialize(Cell cell)
    {
        _cell = cell;
        _isInitialized = true;
        Refresh();
    }

    public void Refresh()
    {
        if (_cell != null && _cell.IsMountainReserved)
        {
            SetBg(null); // pas de ground
            HideNumber();
            return;
        }
        if (!_isInitialized || _cell == null) return;
        switch (_cell.State)
        {
            case CellState.Hidden: ShowHidden(); break;
            case CellState.Flagged: ShowFlagged(); break;
            case CellState.Revealed: ShowRevealed(); break;
        }
    }

    // =========================================================================
    // États
    // =========================================================================

    protected virtual void ShowHidden()
    {
        SetBg(_hiddenGroundSprite);
        HideIcon();
        HideNumber();
    }

    protected virtual void ShowFlagged()
    {
        SetBg(_hiddenGroundSprite);
        HideNumber();
        SetIcon(_flagSprite);
    }

    protected virtual void ShowRevealed()
    {
        SetBg(_groundRevealedSprite);

        if (_cell.Content == CellContent.Empty || _cell.Content == CellContent.Number)
        {
            HideIcon();
            ShowNumber(_cell.AdjacentDangerCount);
        }
        else
        {
            HideNumber();
            ShowContentIcon();
        }
    }

    // =========================================================================
    // Chiffres adjacents
    // =========================================================================

    protected void ShowNumber(int count)
    {
        if (count <= 0) { HideNumber(); return; }
        var numSprite = GetNumberSprite(count);
        if (numSprite != null && _numberRenderer != null)
        {
            _numberRenderer.sprite = numSprite;
            _numberRenderer.enabled = true;
        }
        else HideNumber();
    }

    // =========================================================================
    // Icône contenu (override dans les sous-classes pour ennemis/trésors)
    // =========================================================================

    protected virtual void ShowContentIcon()
    {
        HideIcon();
    }

    // =========================================================================
    // Clic
    // =========================================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_cell == null) return;
        var gm = GridManager.Instance;
        if (gm == null) return;

        if (eventData.button == PointerEventData.InputButton.Left)
            gm.RevealCell(_cell.X, _cell.Y);
        else if (eventData.button == PointerEventData.InputButton.Right)
            gm.ToggleFlag(_cell.X, _cell.Y);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    protected void SetBg(Sprite sprite)
    {
        if (_bgRenderer == null) return;
        _bgRenderer.sprite = sprite;
        var c = _bgRenderer.color; c.a = 1f;
        _bgRenderer.color = c;
    }

    protected void SetIcon(Sprite sprite)
    {
        if (_iconRenderer == null) return;
        _iconRenderer.sprite = sprite;
        _iconRenderer.enabled = sprite != null;
    }

    protected void HideIcon()
    {
        if (_iconRenderer != null) _iconRenderer.enabled = false;
    }

    protected void HideNumber()
    {
        if (_numberRenderer != null) _numberRenderer.enabled = false;
    }

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
}