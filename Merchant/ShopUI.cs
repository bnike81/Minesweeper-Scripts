using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// ShopUI - Interface boutique du marchand Aldric.
/// S'ouvre au centre de l'ecran avec deux onglets : Acheter / Vendre.
/// </summary>
public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance { get; private set; }

    [Header("=== Panel Principal ===")]
    [SerializeField] private GameObject _shopPanel;
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _goldText;

    [Header("=== Onglets ===")]
    [SerializeField] private Button _btnBuy;
    [SerializeField] private Button _btnSell;
    [SerializeField] private Button _btnClose;

    [Header("=== Liste Items ===")]
    [SerializeField] private Transform _itemListParent;
    [SerializeField] private GameObject _shopItemPrefab;

    [Header("=== Couleurs Onglets ===")]
    [SerializeField] private Color _tabActiveColor = new Color(0.9f, 0.75f, 0.2f);
    [SerializeField] private Color _tabInactiveColor = new Color(0.4f, 0.35f, 0.2f);

    [Header("=== Items en vente ===")]
    [SerializeField] private ItemData _itemPomme;
    [SerializeField] private ItemData _itemSteackCuit;

    [Header("=== Prix achat ===")]
    [SerializeField] private int _prixPomme = 3;
    [SerializeField] private int _prixSteackCuit = 5;

    [Header("=== Prix vente ===")]
    [SerializeField] private int _ventesPeauAnimal = 4;
    [SerializeField] private int _ventesFleur = 2;
    [SerializeField] private int _ventesSteackCru = 1;
    [SerializeField] private int _ventesParchemin = 3;

    private bool _isBuyMode = true;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_shopPanel != null) _shopPanel.SetActive(false);
    }

    private void Start()
    {
        _btnBuy?.onClick.AddListener(() => SetMode(true));
        _btnSell?.onClick.AddListener(() => SetMode(false));
        _btnClose?.onClick.AddListener(CloseShop);
    }

    // -------------------------------------------------------------------------
    // Ouverture / Fermeture
    // -------------------------------------------------------------------------

    public void OpenShop()
    {
        if (_shopPanel == null) return;
        _shopPanel.SetActive(true);
        SetMode(true);
        RefreshGold();
    }

    public void CloseShop()
    {
        if (_shopPanel != null) _shopPanel.SetActive(false);
    }

    private void SetMode(bool buyMode)
    {
        _isBuyMode = buyMode;

        // Couleurs onglets
        if (_btnBuy != null)
            _btnBuy.GetComponent<Image>().color = buyMode ? _tabActiveColor : _tabInactiveColor;
        if (_btnSell != null)
            _btnSell.GetComponent<Image>().color = buyMode ? _tabInactiveColor : _tabActiveColor;

        if (_titleText != null)
            _titleText.text = buyMode
                ? "Aldric - Qu'est-ce qui vous ferait plaisir ?"
                : "Aldric - Voyons ce que vous avez...";

        RefreshList();
    }

    private void RefreshGold()
    {
        if (_goldText != null && Inventory.Instance != null)
            _goldText.text = Inventory.Instance.Gold + " pieces";
    }

    // -------------------------------------------------------------------------
    // Listes
    // -------------------------------------------------------------------------

    private void RefreshList()
    {
        // Vider la liste
        foreach (Transform child in _itemListParent)
            Destroy(child.gameObject);

        if (_isBuyMode)
            BuildBuyList();
        else
            BuildSellList();

        RefreshGold();
    }

    private void BuildBuyList()
    {
        // Pomme
        if (_itemPomme != null)
            SpawnShopItem(_itemPomme, _prixPomme, true);

        // Steack cuit
        if (_itemSteackCuit != null)
            SpawnShopItem(_itemSteackCuit, _prixSteackCuit, true);
    }

    private void BuildSellList()
    {
        var inv = Inventory.Instance;
        if (inv == null) return;

        var db = Inventory.Database;
        if (db == null) return;

        // Affiche seulement les items que le joueur possede
        var sellable = new List<(ItemID id, int price)>
        {
            (ItemID.PeauAnimal, _ventesPeauAnimal),
            (ItemID.Fleur,      _ventesFleur),
            (ItemID.SteackCru,  _ventesSteackCru),
            (ItemID.Parchemin,  _ventesParchemin),
        };

        bool hasAnything = false;
        foreach (var (id, price) in sellable)
        {
            int qty = inv.CountItem(id);
            if (qty <= 0) continue;
            var itemData = db.Get(id);
            if (itemData == null) continue;
            SpawnShopItem(itemData, price, false, qty);
            hasAnything = true;
        }

        if (!hasAnything)
            SpawnEmptyMessage("Aldric : Vous n'avez rien a me vendre pour l'instant !");
    }

    // -------------------------------------------------------------------------
    // Spawn d'une ligne item
    // -------------------------------------------------------------------------

    private void SpawnShopItem(ItemData item, int price, bool isBuy, int qty = 1)
    {
        if (_shopItemPrefab == null) return;
        var go = Instantiate(_shopItemPrefab, _itemListParent);
        var line = go.GetComponent<ShopItemLine>();
        line?.Initialize(item, price, isBuy, qty, this);
    }

    private void SpawnEmptyMessage(string msg)
    {
        if (_shopItemPrefab == null) return;
        var go = Instantiate(_shopItemPrefab, _itemListParent);
        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = msg;
    }

    // -------------------------------------------------------------------------
    // Transactions
    // -------------------------------------------------------------------------

    public void BuyItem(ItemData item, int price)
    {
        var inv = Inventory.Instance;
        if (inv == null) return;

        if (inv.Gold < price)
        {
            EventBus.Publish(new OnNotification
            {
                Message = "Aldric : Pas assez de pieces !",
                Type = NotificationType.Warning
            });
            return;
        }

        if (inv.IsFull())
        {
            EventBus.Publish(new OnNotification
            {
                Message = "Aldric : Votre sac est plein !",
                Type = NotificationType.Warning
            });
            return;
        }

        inv.SpendGold(price);
        inv.AddItem(item, 1);

        EventBus.Publish(new OnNotification
        {
            Message = "Aldric : Voila ! Un(e) " + item.displayName + " pour " + price + " pieces !",
            Type = NotificationType.Item
        });

        RefreshGold();
    }

    public void SellItem(ItemData item, int price)
    {
        var inv = Inventory.Instance;
        if (inv == null) return;

        if (inv.CountItem(item.itemID) <= 0) return;

        inv.RemoveItem(item.itemID, 1);
        inv.AddGold(price);

        EventBus.Publish(new OnNotification
        {
            Message = "Aldric : " + price + " pieces pour votre " + item.displayName + " !",
            Type = NotificationType.Item
        });

        RefreshList();
    }
}