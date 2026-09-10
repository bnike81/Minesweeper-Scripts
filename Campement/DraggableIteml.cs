using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// DraggableItem - Composant sur les slots d'inventaire pour le drag and drop.
/// Cree un fantome de l'item pendant le drag.
/// </summary>
public class DraggableItem : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _quantityText;

    private ItemData _itemData;
    private int _quantity;
    private Canvas _canvas;
    private GameObject _dragGhost;
    private RectTransform _ghostRect;
    private bool _wasDropped = false;

    public ItemData ItemData => _itemData;
    public int Quantity => _quantity;

    // -------------------------------------------------------------------------

    public void Setup(ItemData item, int qty)
    {
        _itemData = item;
        _quantity = qty;
        _canvas = GetComponentInParent<Canvas>();

        if (_iconImage != null && item?.icon != null)
        {
            _iconImage.sprite = item.icon;
            _iconImage.enabled = true;
        }
        if (_quantityText != null)
        {
            _quantityText.text = qty > 1 ? "x" + qty : "";
            _quantityText.enabled = qty > 1;
        }
    }

    // -------------------------------------------------------------------------
    // Drag
    // -------------------------------------------------------------------------

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_itemData == null) return;
        _wasDropped = false;

        // Creer fantome
        _dragGhost = new GameObject("DragGhost");
        _dragGhost.transform.SetParent(_canvas.transform, false);
        _ghostRect = _dragGhost.AddComponent<RectTransform>();
        _ghostRect.sizeDelta = new Vector2(32f, 32f);

        var img = _dragGhost.AddComponent<Image>();
        img.sprite = _itemData.icon;
        img.raycastTarget = false;

        var cg = _dragGhost.AddComponent<CanvasGroup>();
        cg.alpha = 0.8f;
        cg.blocksRaycasts = false;

        // Position initiale
        _ghostRect.anchoredPosition = ScreenToCanvas(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_ghostRect == null) return;
        _ghostRect.anchoredPosition = ScreenToCanvas(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_dragGhost != null) Destroy(_dragGhost);
        if (!_wasDropped)
        {
            // Drop raté - rien ne se passe
        }
    }

    public void OnDropped()
    {
        _wasDropped = true;
    }

    // -------------------------------------------------------------------------

    private Vector2 ScreenToCanvas(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            screenPos, _canvas.worldCamera, out var localPos);
        return localPos;
    }
}