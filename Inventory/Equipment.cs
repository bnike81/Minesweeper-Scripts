using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Equipment - Gere les 4 slots d'equipement du joueur.
/// Publie OnEquipmentChanged quand un equipement change.
/// </summary>
public class Equipment : MonoBehaviour
{
    public static Equipment Instance { get; private set; }

    // Equipements actuellement portes
    private Dictionary<EquipSlot, EquipmentData> _equipped
        = new Dictionary<EquipSlot, EquipmentData>();

    // -------------------------------------------------------------------------
    // Proprietes calculees
    // -------------------------------------------------------------------------

    public int AttackDamage
    {
        get
        {
            int total = 0;
            foreach (var e in _equipped.Values)
                if (e != null) total += e.attackDamage;
            return total;
        }
    }

    public int Defense
    {
        get
        {
            int total = 0;
            foreach (var e in _equipped.Values)
                if (e != null) total += e.defense;
            return total;
        }
    }

    public int BonusMaxHP
    {
        get
        {
            int total = 0;
            foreach (var e in _equipped.Values)
                if (e != null) total += e.bonusMaxHP;
            return total;
        }
    }

    public float XPMultiplier
    {
        get
        {
            float total = 0f;
            foreach (var e in _equipped.Values)
                if (e != null) total += e.xpBonusPercent;
            return 1f + total / 100f;
        }
    }

    public bool HasWeapon => _equipped.ContainsKey(EquipSlot.Weapon)
                          && _equipped[EquipSlot.Weapon] != null;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    [Header("=== Arme de depart (test) ===")]
    [Tooltip("Arme equipe au demarrage pour tester le combat. Laisse vide en prod.")]
    [SerializeField] private EquipmentData _startWeapon;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Equipe l'arme de depart si definie (pour les tests)
        if (_startWeapon != null)
            Equip(_startWeapon);
    }

    private void OnEnable() { EventBus.Subscribe<OnRunStarted>(OnRunStarted); }
    private void OnDisable() { EventBus.Unsubscribe<OnRunStarted>(OnRunStarted); }

    private void OnRunStarted(OnRunStarted evt)
    {
        _equipped.Clear();
        EventBus.Publish(new OnEquipmentChanged());
    }

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    public EquipmentData GetEquipped(EquipSlot slot)
    {
        _equipped.TryGetValue(slot, out var result);
        return result;
    }

    /// <summary>
    /// Equipe un objet dans son slot.
    /// Si un objet est deja equipe, le remet dans l'inventaire.
    /// </summary>
    public void Equip(EquipmentData data)
    {
        if (data == null) return;

        // Desequiper l'ancien si present
        if (_equipped.TryGetValue(data.slot, out var old) && old != null)
            Unequip(data.slot);

        _equipped[data.slot] = data;

        Debug.Log("<color=#AAFFFF>[Equipment]</color> Equipe : " + data.displayName);
        EventBus.Publish(new OnEquipmentChanged());
        EventBus.Publish(new OnNotification
        {
            Message = data.displayName + " equipe !",
            Type = NotificationType.Item
        });
    }

    /// <summary>
    /// Desequipe un slot et remet l'objet dans l'inventaire.
    /// </summary>
    public void Unequip(EquipSlot slot)
    {
        if (!_equipped.TryGetValue(slot, out var data) || data == null) return;

        _equipped[slot] = null;
        Inventory.Instance?.AddItem(data, 1);

        Debug.Log("<color=#AAFFFF>[Equipment]</color> Desequipe : " + data.displayName);
        EventBus.Publish(new OnEquipmentChanged());
    }
}