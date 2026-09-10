using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ShopItemLine - Une ligne dans la boutique (icone + nom + prix + bouton).
/// </summary>
public class ShopItemLine : MonoBehaviour
{
    [SerializeField] private Image _icon;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _priceText;
    [SerializeField] private TextMeshProUGUI _quantityText;
    [SerializeField] private Button _actionButton;
    [SerializeField] private TextMeshProUGUI _buttonText;

    private ItemData _item;
    private int _price;
    private bool _isBuy;
    private ShopUI _shop;

    public void Initialize(ItemData item, int price, bool isBuy, int qty, ShopUI shop)
    {
        _item = item;
        _price = price;
        _isBuy = isBuy;
        _shop = shop;

        // Recherche automatique si non assignes dans Inspector
        if (_icon == null)
        {
            var images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
                if (img.gameObject.name == "Icon") { _icon = img; break; }
        }

        var allTexts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in allTexts)
        {
            if (_nameText == null && t.gameObject.name == "Name") _nameText = t;
            if (_priceText == null && t.gameObject.name == "Price") _priceText = t;
            if (_quantityText == null && t.gameObject.name == "Qty") _quantityText = t;
            if (_buttonText == null && t.gameObject.name.Contains("Text") && t.GetComponentInParent<Button>() != null)
                _buttonText = t;
        }
        if (_actionButton == null)
            _actionButton = GetComponentInChildren<Button>(true);

        // Appliquer les donnees
        if (_icon != null && item.icon != null)
        {
            _icon.sprite = item.icon;
            _icon.enabled = true;
        }

        if (_nameText != null) _nameText.text = item.displayName;
        if (_priceText != null) _priceText.text = price + " pieces";
        if (_quantityText != null) _quantityText.text = isBuy ? "" : "x" + qty;
        if (_buttonText != null) _buttonText.text = isBuy ? "Acheter" : "Vendre";
        if (_actionButton != null) _actionButton.onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        if (_shop == null || _item == null) return;
        if (_isBuy) _shop.BuyItem(_item, _price);
        else _shop.SellItem(_item, _price);
    }
}