using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Inventory - Logique pure des slots d'inventaire.
/// Singleton accessible partout via Inventory.Instance.
/// Publie OnInventoryChanged a chaque modification.
/// </summary>
public class Inventory : MonoBehaviour
{
    public static Inventory Instance { get; private set; }

    // Items decouverts pour la premiere fois (animation Zelda)
    private System.Collections.Generic.HashSet<ItemID> _discovered = new();

    public bool HasDiscovered(ItemID id) => _discovered.Contains(id);
    public void MarkDiscovered(ItemID id) => _discovered.Add(id);

    [Header("=== Configuration ===")]
    [Tooltip("Nombre de slots (2 lignes de 4 = 8)")]
    [SerializeField, Range(4, 16)] private int _slotCount = 8;

    [Header("=== Base de donnees ===")]
    [Tooltip("Glisse l'asset ItemDatabase ici")]
    [SerializeField] private ItemDatabase _database;

    // Appele par LootSystem si Database est null
    public void ForceInitDatabase()
    {
        if (_database != null && Database == null)
        {
            _database.Initialize();
            Database = _database;
        }
    }

    [Header("=== Bourse ===")]
    [SerializeField, ReadOnly] private int _gold = 0;

    public static ItemDatabase Database { get; set; }

    public int Gold => _gold;
    public int SlotCount => _slotCount;

    // Slot : itemData + quantite
    public class Slot
    {
        public ItemData item;
        public int quantity;
        public bool IsEmpty => item == null || quantity <= 0;
    }

    private Slot[] _slots;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_database != null)
        {
            _database.Initialize();
            Database = _database;
        }
        else
            Debug.LogWarning("[Inventory] ItemDatabase non assigne !");

        InitSlots();
    }

    private void OnEnable() { EventBus.Subscribe<OnRunStarted>(OnRunStarted); }
    private void OnDisable() { EventBus.Unsubscribe<OnRunStarted>(OnRunStarted); }

    private void OnRunStarted(OnRunStarted evt)
    {
        InitSlots();
        _gold = 0;
        EventBus.Publish(new OnInventoryChanged());
        EventBus.Publish(new OnGoldChanged { Amount = 0, Total = 0 });
    }

    private void InitSlots()
    {
        _slots = new Slot[_slotCount];
        for (int i = 0; i < _slotCount; i++)
            _slots[i] = new Slot();
    }

    // -------------------------------------------------------------------------
    // Lecture
    // -------------------------------------------------------------------------

    public Slot GetSlot(int index)
    {
        if (index < 0 || index >= _slotCount) return null;
        return _slots[index];
    }

    public Slot[] GetAllSlots() => _slots;

    public int CountItem(ItemID id)
    {
        int total = 0;
        foreach (var slot in _slots)
            if (!slot.IsEmpty && slot.item.itemID == id)
                total += slot.quantity;
        return total;
    }

    // -------------------------------------------------------------------------
    // Ajouter
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ajoute un objet. Retourne true si reussi, false si inventaire plein.
    /// </summary>
    public bool AddItem(ItemData item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return false;

        // Pieces d'or -> vont directement dans la bourse
        if (item.itemType == ItemType.Currency)
        {
            AddGold(quantity * item.sellValue);
            return true;
        }

        int remaining = quantity;

        // 1. Remplir les piles existantes du meme objet
        if (item.stackable)
        {
            foreach (var slot in _slots)
            {
                if (slot.IsEmpty) continue;
                if (slot.item.itemID != item.itemID) continue;
                int space = item.maxStack - slot.quantity;
                if (space <= 0) continue;
                int add = Mathf.Min(space, remaining);
                slot.quantity += add;
                remaining -= add;
                if (remaining <= 0) break;
            }
        }

        // 2. Remplir des slots vides
        while (remaining > 0)
        {
            var emptySlot = FindEmptySlot();
            if (emptySlot == null)
            {
                Debug.LogWarning("[Inventory] Plein ! " + remaining + "x " + item.displayName + " perdu.");
                EventBus.Publish(new OnInventoryChanged());
                return false;
            }
            int add = item.stackable ? Mathf.Min(item.maxStack, remaining) : 1;
            emptySlot.item = item;
            emptySlot.quantity = add;
            remaining -= add;
        }

        Debug.Log("<color=#AAFFAA>[Inventory]</color> +" + quantity + "x " + item.displayName);
        EventBus.Publish(new OnInventoryChanged());
        return true;
    }

    // -------------------------------------------------------------------------
    // Retirer
    // -------------------------------------------------------------------------

    /// <summary>
    /// Retire une quantite d'un objet. Retourne true si reussi.
    /// </summary>
    public bool RemoveItem(ItemID id, int quantity = 1)
    {
        if (CountItem(id) < quantity) return false;

        int remaining = quantity;
        foreach (var slot in _slots)
        {
            if (slot.IsEmpty || slot.item.itemID != id) continue;
            int remove = Mathf.Min(slot.quantity, remaining);
            slot.quantity -= remove;
            if (slot.quantity <= 0) { slot.item = null; slot.quantity = 0; }
            remaining -= remove;
            if (remaining <= 0) break;
        }

        EventBus.Publish(new OnInventoryChanged());
        return true;
    }

    // -------------------------------------------------------------------------
    // Bourse
    // -------------------------------------------------------------------------

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        _gold += amount;
        Debug.Log("<color=#FFD700>[Inventory]</color> +" + amount + " pieces -> total=" + _gold);
        EventBus.Publish(new OnGoldChanged { Amount = amount, Total = _gold });
    }

    public bool SpendGold(int amount)
    {
        if (_gold < amount) return false;
        _gold -= amount;
        EventBus.Publish(new OnGoldChanged { Amount = -amount, Total = _gold });
        return true;
    }

    // -------------------------------------------------------------------------
    // Utiliser un objet (pomme, steack cuit)
    // -------------------------------------------------------------------------

    public bool UseItem(ItemID id)
    {
        // Trouver l'objet
        ItemData data = null;
        foreach (var slot in _slots)
        {
            if (!slot.IsEmpty && slot.item.itemID == id)
            { data = slot.item; break; }
        }
        if (data == null) return false;

        // Appliquer l'effet - PlayerStats.Heal gere tout
        if (data.healAmount > 0)
        {
            PlayerStats.Instance?.Heal(data.healAmount);
            EventBus.Publish(new OnNotification
            {
                Message = "+" + data.healAmount + " PV (" + data.displayName + ")",
                Type = NotificationType.Item
            });
        }

        RemoveItem(id, 1);
        return true;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Slot FindEmptySlot()
    {
        foreach (var slot in _slots)
            if (slot.IsEmpty) return slot;
        return null;
    }

    public bool IsFull()
    {
        foreach (var slot in _slots)
            if (slot.IsEmpty) return false;
        return true;
    }
}