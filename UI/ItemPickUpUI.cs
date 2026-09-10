using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ItemPickupUI - Panel style Zelda pour la decouverte d'un item.
/// Affiche : nom, description, type, capacites.
/// Le heros leve l'objet en l'air avant d'afficher le panel.
/// </summary>
public class ItemPickupUI : MonoBehaviour
{
    public static ItemPickupUI Instance { get; private set; }

    [Header("=== Panel ===")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Image _itemIcon;
    [SerializeField] private TextMeshProUGUI _itemName;
    [SerializeField] private TextMeshProUGUI _itemType;
    [SerializeField] private TextMeshProUGUI _itemDesc;
    [SerializeField] private TextMeshProUGUI _itemStats;
    [SerializeField] private TextMeshProUGUI _promptText;
    [SerializeField] private Button _confirmButton;

    [Header("=== Animation Heros ===")]
    [Tooltip("Duree de la pose item leve en l'air")]
    [SerializeField, Range(0.5f, 3f)] private float _holdDuration = 1.5f;
    [Tooltip("Hauteur de l'objet au dessus du heros")]
    [SerializeField, Range(0.3f, 1.5f)] private float _itemHeight = 0.8f;

    [Header("=== Sprites Direction Droite ===")]
    [Tooltip("Heros accroupi ramassage droite")]
    [SerializeField] private Sprite _crouchRight;
    [Tooltip("Heros se relevant objet en main droite")]
    [SerializeField] private Sprite _riseRight;
    [Tooltip("Heros tenant objet en l air vers droite")]
    [SerializeField] private Sprite _holdUpRight;

    [Header("=== Sprites Direction Gauche ===")]
    [Tooltip("Heros accroupi ramassage gauche")]
    [SerializeField] private Sprite _crouchLeft;
    [Tooltip("Heros se relevant objet en main gauche")]
    [SerializeField] private Sprite _riseLeft;
    [Tooltip("Heros tenant objet en l air vers gauche")]
    [SerializeField] private Sprite _holdUpLeft;

    [Header("=== Position Objet - Crouch ===")]
    [Tooltip("Offset X objet pendant crouch")]
    [SerializeField] private float _crouchItemOffsetX = 0.2f;
    [Tooltip("Offset Y objet pendant crouch")]
    [SerializeField] private float _crouchItemOffsetY = 0.1f;

    [Header("=== Position Objet - Rise ===")]
    [Tooltip("Offset X objet pendant rise")]
    [SerializeField] private float _riseItemOffsetX = 0.2f;
    [Tooltip("Offset Y objet pendant rise")]
    [SerializeField] private float _riseItemOffsetY = 0.4f;

    [Header("=== Position Objet - Hold Up ===")]
    [Tooltip("Offset X objet en l air")]
    [SerializeField] private float _holdItemOffsetX = 0f;
    [Tooltip("Offset Y objet en l air")]
    [SerializeField] private float _holdItemOffsetY = 0.85f;

    [Header("=== Echelle Objet ===")]
    [Tooltip("Taille de l objet affiche")]
    [SerializeField] private float _itemDisplayScale = 0.5f;

    // -------------------------------------------------------------------------

    private GameObject _floatingItemGO;
    private SpriteRenderer _floatingItemSR;
    private bool _waitingConfirm = false;
    private System.Action _onConfirm;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_panel != null) _panel.SetActive(false);
    }

    private void Start()
    {
        _confirmButton?.onClick.AddListener(OnConfirm);

        // Creer le GO pour l'objet flottant
        _floatingItemGO = new GameObject("FloatingItem");
        _floatingItemSR = _floatingItemGO.AddComponent<SpriteRenderer>();
        _floatingItemSR.sortingLayerName = "CellContent";
        _floatingItemSR.sortingOrder = 20;
        _floatingItemGO.transform.localScale = Vector3.one * _itemDisplayScale;
        _floatingItemGO.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lance l'animation de ramassage et affiche le panel de decouverte.
    /// firstTime = true : animation complete + panel
    /// firstTime = false : juste ramassage rapide
    /// </summary>
    public void ShowPickup(ItemData item, bool firstTime, System.Action onDone = null)
    {
        _onConfirm = onDone;
        StartCoroutine(PickupSequence(item, firstTime));
    }

    // -------------------------------------------------------------------------
    // Sequence
    // -------------------------------------------------------------------------

    private bool _facingRight = true;

    private IEnumerator PickupSequence(ItemData item, bool firstTime)
    {
        var hero = HeroController.Instance;
        var heroSR = hero?.GetComponent<SpriteRenderer>();

        // Determiner direction dominante
        _facingRight = true;
        if (hero != null)
        {
            var dir = hero.LastDir;
            _facingRight = !(dir == Vector2Int.left);
        }

        Sprite spCrouch = _facingRight ? _crouchRight : _crouchLeft;
        Sprite spRise = _facingRight ? _riseRight : _riseLeft;
        Sprite spHold = _facingRight ? _holdUpRight : _holdUpLeft;

        // 1. Accroupi
        if (heroSR != null && spCrouch != null) heroSR.sprite = spCrouch;
        ShowItem(item.icon, _crouchItemOffsetX * (_facingRight ? 1 : -1), _crouchItemOffsetY);
        yield return new WaitForSeconds(0.2f);

        // 2. Se releve
        if (heroSR != null && spRise != null) heroSR.sprite = spRise;
        ShowItem(item.icon, _riseItemOffsetX * (_facingRight ? 1 : -1), _riseItemOffsetY);
        yield return new WaitForSeconds(0.2f);

        if (firstTime)
        {
            // 3. Tient objet en l'air - reste jusqu'a fermeture panel
            if (heroSR != null && spHold != null) heroSR.sprite = spHold;
            ShowItem(item.icon, _holdItemOffsetX * (_facingRight ? 1 : -1), _holdItemOffsetY);

            yield return new WaitForSeconds(0.3f);
            ShowPanel(item);
            _waitingConfirm = true;
            while (_waitingConfirm) yield return null;
        }
        else
        {
            // Ramassage rapide - juste rise avec objet en main
            ShowItem(item.icon, _riseItemOffsetX * (_facingRight ? 1 : -1), _riseItemOffsetY);
            yield return new WaitForSeconds(0.35f);
        }

        HideItem();
        hero?.RestoreIdleSprite();
        _onConfirm?.Invoke();
    }

    private void ShowItem(Sprite icon, float offX, float offY)
    {
        if (_floatingItemSR == null || icon == null) return;
        _floatingItemSR.sprite = icon;
        _floatingItemGO.transform.localScale = Vector3.one * _itemDisplayScale;
        _floatingItemGO.SetActive(true);
        UpdateFloatingPosition(offX, offY);
    }

    private void HideItem()
    {
        if (_floatingItemGO != null) _floatingItemGO.SetActive(false);
    }

    private void UpdateFloatingPosition(float offX = 0f, float offY = 0.85f)
    {
        _currentOffX = offX;
        _currentOffY = offY;
        if (HeroController.Instance == null) return;
        var hp = HeroController.Instance.transform.position;
        _floatingItemGO.transform.position = new Vector3(hp.x + offX, hp.y + offY, hp.z - 0.1f);
    }

    private float _currentOffX, _currentOffY;

    private void Update()
    {
        if (_floatingItemGO != null && _floatingItemGO.activeSelf)
            UpdateFloatingPosition(_currentOffX, _currentOffY);

        // Clic ou touche pour confirmer
        if (_waitingConfirm)
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                OnConfirm();
        }
    }

    // -------------------------------------------------------------------------
    // Panel
    // -------------------------------------------------------------------------

    private void ShowPanel(ItemData item)
    {
        if (_panel == null) return;
        _panel.SetActive(true);

        if (_itemIcon != null && item.icon != null) _itemIcon.sprite = item.icon;
        if (_itemName != null) _itemName.text = item.displayName;
        if (_itemDesc != null) _itemDesc.text = item.description;

        // Type
        string typeStr = item.itemType switch
        {
            ItemType.Consumable => "Consommable",
            ItemType.Material => "Materiau",
            ItemType.Currency => "Monnaie",
            ItemType.QuestItem => "Objet de quete",
            _ => "Objet"
        };
        if (_itemType != null) _itemType.text = typeStr;

        // Stats
        string stats = "";
        if (item.healAmount > 0) stats += "Soin : +" + item.healAmount + " PV\n";
        if (item.sellValue > 0) stats += "Valeur : " + item.sellValue + " pieces\n";

        // Si c'est un equipement
        if (item is EquipmentData equip)
        {
            typeStr = "Equipement - " + equip.slot switch
            {
                EquipSlot.Weapon => "Arme",
                EquipSlot.Armor => "Armure",
                EquipSlot.Helmet => "Casque",
                EquipSlot.Ring => "Bague",
                _ => ""
            };
            if (_itemType != null) _itemType.text = typeStr;

            if (equip.attackDamage > 0) stats += "ATK : +" + equip.attackDamage + "\n";
            if (equip.defense > 0) stats += "DEF : +" + equip.defense + "\n";
            if (equip.bonusMaxHP > 0) stats += "PV max : +" + equip.bonusMaxHP + "\n";
            if (equip.xpBonusPercent > 0) stats += "XP : +" + equip.xpBonusPercent + "%\n";
        }

        if (_itemStats != null) _itemStats.text = stats.TrimEnd();
        if (_promptText != null) _promptText.text = "Appuyer sur une touche...";
    }

    private void OnConfirm()
    {
        _waitingConfirm = false;
        if (_panel != null) _panel.SetActive(false);
        // Securite : deverrouiller le heros si callback pas encore appele
        if (HeroController.Instance != null)
            HeroController.Instance.IsLocked = false;
    }
}