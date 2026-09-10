using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// CookingSlot - Slot drag and drop pour l'interface de cuisson.
/// Accepte uniquement les items compatibles (steack cru ou branches).
/// </summary>
public class CookingSlot : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum SlotType { Input, Fuel, Output }

    [Header("=== Type ===")]
    [SerializeField] private SlotType _type;

    [Header("=== References ===")]
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _quantityText;
    [SerializeField] private Image _background;

    [Header("=== Couleurs ===")]
    [SerializeField] private Color _emptyColor = new Color(0.2f, 0.2f, 0.15f, 0.9f);
    [SerializeField] private Color _filledColor = new Color(0.35f, 0.3f, 0.1f, 0.95f);
    [SerializeField] private Color _hoverColor = new Color(0.5f, 0.45f, 0.1f, 1f);

    [Header("=== Items Acceptes ===")]
    [SerializeField] private ItemData _acceptedItem;

    private int _quantity = 0;
    private ItemData _item;

    public int Quantity => _quantity;
    public ItemData Item => _item;

    private Canvas _canvas;
    private GameObject _dragGhost;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (_background != null) _background.raycastTarget = true;
        var img = GetComponent<Image>();
        if (img == null) img = gameObject.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = true;
    }

    /// <summary>Definit directement l item et la quantite (pour le slot output)</summary>
    public void SetItem(ItemData item, int quantity)
    {
        _item = item;
        _quantity = quantity;
        RefreshVisual();
    }

    public void AddQuantity(int amount)
    {
        _quantity += amount;
        if (_item == null) _item = _acceptedItem;
        RefreshVisual();
    }

    public void RemoveQuantity(int amount)
    {
        _quantity = Mathf.Max(0, _quantity - amount);
        if (_quantity == 0) _item = null;
        RefreshVisual();
    }

    public void Clear()
    {
        _quantity = 0;
        _item = null;
        RefreshVisual();
    }

    public void RefreshVisual()
    {
        bool hasItem = _quantity > 0 && _item != null;

        if (_background != null)
            _background.color = hasItem ? _filledColor : _emptyColor;

        if (_iconImage != null)
        {
            _iconImage.enabled = hasItem;
            if (hasItem && _item?.icon != null)
                _iconImage.sprite = _item.icon;
        }

        if (_quantityText != null)
        {
            _quantityText.enabled = hasItem && _quantity > 1;
            if (hasItem) _quantityText.text = "x" + _quantity;
        }
    }

    // -------------------------------------------------------------------------
    // Drag and Drop
    // -------------------------------------------------------------------------

    public void OnDrop(PointerEventData eventData)
    {
        if (_type == SlotType.Output) return;

        // Source : SlotUI (depuis l inventaire)
        var sourceSlot = eventData.pointerDrag?.GetComponent<SlotUI>();
        if (sourceSlot != null)
        {
            var slotData = sourceSlot.SlotData;
            if (slotData == null || slotData.IsEmpty) return;

            var item = slotData.item;
            if (_acceptedItem != null && item.itemID != _acceptedItem.itemID)
            {
                EventBus.Publish(new OnNotification
                {
                    Message = "Cet item ne va pas ici !",
                    Type = NotificationType.Warning
                });
                return;
            }

            int qty = slotData.quantity;
            _item = item;
            _quantity += qty;
            Inventory.Instance?.RemoveItem(item.itemID, qty);
            RefreshVisual();
            return;
        }

        // Source : DraggableItem (fallback)
        var dragged = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (dragged == null) return;
        var dItem = dragged.ItemData;
        if (dItem == null) return;
        if (_acceptedItem != null && dItem.itemID != _acceptedItem.itemID) return;

        _item = dItem;
        _quantity += dragged.Quantity;
        Inventory.Instance?.RemoveItem(dItem.itemID, dragged.Quantity);
        RefreshVisual();
        dragged.OnDropped();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_background != null && _type != SlotType.Output)
            _background.color = _hoverColor;

        string hint = _type switch
        {
            SlotType.Input => "Glisse un steack cru ici",
            SlotType.Fuel => "Glisse des branches ici",
            SlotType.Output => "Resultat de la cuisson",
            _ => ""
        };
        TooltipUI.Show(hint);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        RefreshVisual();
        TooltipUI.Hide();
    }

    // -------------------------------------------------------------------------
    // Drag depuis ce slot vers l inventaire
    // -------------------------------------------------------------------------

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_quantity <= 0 || _item == null) return;
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null) return;

        _dragGhost = new GameObject("CookDragGhost");
        _dragGhost.transform.SetParent(_canvas.transform, false);

        var rect = _dragGhost.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(24f, 24f);

        var img = _dragGhost.AddComponent<Image>();
        img.sprite = _item?.icon;
        img.raycastTarget = false;

        var cg = _dragGhost.AddComponent<CanvasGroup>();
        cg.alpha = 0.8f;
        cg.blocksRaycasts = false;

        // CookingDraggable garde reference au slot source
        var di = _dragGhost.AddComponent<CookingDraggable>();
        di.Setup(_item, _quantity, this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragGhost == null || _canvas == null) return;
        var rect = _dragGhost.GetComponent<RectTransform>();
        if (rect == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            eventData.position, _canvas.worldCamera, out var pos);
        rect.anchoredPosition = pos;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Chercher manuellement si on est au dessus d un SlotUI
        // (le panel du feu peut bloquer les raycast standards)
        bool dropped = false;
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (var result in results)
        {
            var slot = result.gameObject.GetComponent<SlotUI>();
            if (slot == null) slot = result.gameObject.GetComponentInParent<SlotUI>();
            if (slot != null && _item != null)
            {
                Inventory.Instance?.AddItem(_item, _quantity);
                Clear();
                dropped = true;
                break;
            }
        }

        if (_dragGhost != null) Destroy(_dragGhost);
        _dragGhost = null;
    }
}