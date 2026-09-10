using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// EquipSlotUI - Un slot d'equipement individuel.
/// Survol = tooltip. Clic = desequiper.
/// </summary>
public class EquipSlotUI : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("=== References ===")]
    [SerializeField] private Image _iconImage;
    [SerializeField] private Image _background;

    [Header("=== Couleurs ===")]
    [SerializeField] private Color _emptyColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    [SerializeField] private Color _equippedColor = new Color(0.2f, 0.35f, 0.2f, 0.95f);
    [SerializeField] private Color _hoverColor = new Color(0.4f, 0.4f, 0.15f, 1f);

    [Header("=== Taille Icone ===")]
    [Tooltip("Taille de l icone dans le slot en pixels UI")]
    [SerializeField] private Vector2 _iconSize = new Vector2(28f, 28f);

    [Header("=== Sprite Slot Vide ===")]
    [Tooltip("Sprite affiche quand le slot est vide")]
    [SerializeField] private Sprite _emptySlotSprite;

    private EquipSlot _slot;

    // -------------------------------------------------------------------------

    public void Initialize(EquipSlot slot, Sprite emptySprite = null)
    {
        _slot = slot;
        if (emptySprite != null) _emptySlotSprite = emptySprite;
        Refresh();
    }

    public void Refresh()
    {
        var equipped = Equipment.Instance?.GetEquipped(_slot);
        bool hasItem = equipped != null;

        if (_background != null)
            _background.color = hasItem ? _equippedColor : _emptyColor;

        if (_iconImage != null)
        {
            if (hasItem)
            {
                _iconImage.enabled = true;
                _iconImage.sprite = equipped.icon;
                _iconImage.color = Color.white;
            }
            else if (_emptySlotSprite != null)
            {
                _iconImage.enabled = true;
                _iconImage.sprite = _emptySlotSprite;
                _iconImage.color = new Color(1f, 1f, 1f, 0.4f);
            }
            else
            {
                _iconImage.enabled = false;
            }
            _iconImage.rectTransform.sizeDelta = _iconSize;
        }
    }

    // -------------------------------------------------------------------------

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        var equipped = Equipment.Instance?.GetEquipped(_slot);
        if (equipped != null) Equipment.Instance.Unequip(_slot);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_background != null) _background.color = _hoverColor;

        var equipped = Equipment.Instance?.GetEquipped(_slot);
        if (equipped != null)
            TooltipUI.Show(equipped.displayName + BuildStatsText(equipped));
        else
        {
            string name = _slot switch
            {
                EquipSlot.Weapon => "Arme",
                EquipSlot.Armor => "Armure",
                EquipSlot.Helmet => "Casque",
                EquipSlot.Ring => "Bague",
                _ => "Slot"
            };
            TooltipUI.Show("Slot " + name + " (vide)");
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        var equipped = Equipment.Instance?.GetEquipped(_slot);
        if (_background != null)
            _background.color = equipped != null ? _equippedColor : _emptyColor;
        TooltipUI.Hide();
    }

    // -------------------------------------------------------------------------

    private string BuildStatsText(EquipmentData e)
    {
        string s = "";
        if (e.attackDamage > 0) s += " | ATK +" + e.attackDamage;
        if (e.defense > 0) s += " | DEF +" + e.defense;
        if (e.bonusMaxHP > 0) s += " | PV +" + e.bonusMaxHP;
        if (e.xpBonusPercent > 0) s += " | XP +" + e.xpBonusPercent + "%";
        return s;
    }
}