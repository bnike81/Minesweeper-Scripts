using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

/// <summary>
/// SlotUI - Visuel d'un slot d'inventaire.
/// Prefab : Image (fond) + Image (icone) + TextMeshPro (quantite)
/// </summary>
public class SlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("=== References ===")]
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _quantityText;
    [SerializeField] private Image _background;

    [Header("=== Couleurs ===")]
    [SerializeField] private Color _emptyColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    [SerializeField] private Color _filledColor = new Color(0.3f, 0.3f, 0.2f, 0.9f);

    [Header("=== Taille Icone ===")]
    [Tooltip("Taille de l'icone dans le slot en pixels UI")]
    [SerializeField] private Vector2 _iconSize = new Vector2(16f, 16f);

    private int _slotIndex;
    private Inventory.Slot _slotData;
    private Canvas _canvas;
    private GameObject _dragGhost;

    public Inventory.Slot SlotData => _slotData;

    private void Awake()
    {
        // S assurer que le slot recoit les raycast pour le drop
        var img = GetComponent<Image>();
        if (img == null) img = gameObject.AddComponent<Image>();
        img.raycastTarget = true;
        img.color = Color.clear; // Transparent mais raycastable
    }

    public void Initialize(int index)
    {
        _slotIndex = index;
        _canvas = GetComponentInParent<Canvas>();
        Refresh(null);
    }

    public void Refresh(Inventory.Slot slot)
    {
        _slotData = slot;
        bool empty = slot == null || slot.IsEmpty;

        if (_background != null)
            _background.color = empty ? _emptyColor : _filledColor;

        if (_iconImage != null)
        {
            _iconImage.enabled = !empty;
            if (!empty && slot.item?.icon != null)
            {
                _iconImage.sprite = slot.item.icon;
                // Forcer la taille definie dans Inspector, pas la taille native
                _iconImage.rectTransform.sizeDelta = _iconSize;
            }
        }

        if (_quantityText != null)
        {
            if (!empty && slot.item != null && slot.item.stackable && slot.quantity > 1)
            {
                _quantityText.enabled = true;
                _quantityText.text = slot.quantity.ToString();
            }
            else
            {
                _quantityText.enabled = false;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Hover tooltip
    // -------------------------------------------------------------------------

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (_slotData == null || _slotData.IsEmpty) return;

        var item = _slotData.item;

        // Objet consommable -> utiliser directement
        if (item.itemType == ItemType.Consumable && item.healAmount > 0)
        {
            Inventory.Instance?.UseItem(item.itemID);
            return;
        }

        // Equipement -> equiper
        if (item is EquipmentData equip)
        {
            Equipment.Instance?.Equip(equip);
            Inventory.Instance?.RemoveItem(item.itemID, 1);
            return;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_slotData == null || _slotData.IsEmpty) return;
        TooltipUI.Show(_slotData.item.displayName);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Hide();
    }

    // -------------------------------------------------------------------------
    // Recevoir un drop depuis CookingSlot
    // -------------------------------------------------------------------------

    public void OnDrop(PointerEventData eventData)
    {
        var go = eventData.pointerDrag;
        if (go == null) return;

        // Cas 1 : drag depuis CookingSlot via CookingDraggable
        var cookDrag = go.GetComponent<CookingDraggable>();
        if (cookDrag != null && cookDrag.ItemData != null)
        {
            Inventory.Instance?.AddItem(cookDrag.ItemData, cookDrag.Quantity);
            cookDrag.OnDropped();
            return;
        }

        // Cas 2 : drag depuis un autre SlotUI via DraggableItem (futur)
        var dragged = go.GetComponent<DraggableItem>();
        if (dragged != null && dragged.ItemData != null)
        {
            Inventory.Instance?.AddItem(dragged.ItemData, dragged.Quantity);
            dragged.OnDropped();
        }
    }

    // -------------------------------------------------------------------------
    // Drag and Drop vers feu de camp
    // -------------------------------------------------------------------------

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_slotData == null || _slotData.IsEmpty) return;
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null) return;

        _dragGhost = new GameObject("DragGhost");
        _dragGhost.transform.SetParent(_canvas.transform, false);

        var rect = _dragGhost.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(24f, 24f);

        var img = _dragGhost.AddComponent<Image>();
        img.sprite = _slotData.item?.icon;
        img.raycastTarget = false;

        var cg = _dragGhost.AddComponent<CanvasGroup>();
        cg.alpha = 0.8f;
        cg.blocksRaycasts = false;

        // DraggableItem permet a CookingSlot de lire l'item
        var di = _dragGhost.AddComponent<DraggableItem>();
        di.Setup(_slotData.item, _slotData.quantity);
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
        if (_dragGhost != null) Destroy(_dragGhost);
        _dragGhost = null;
    }
}