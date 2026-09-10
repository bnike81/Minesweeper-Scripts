using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// LootSystem - Gere les drops d'objets dans la grille.
/// - Objets au sol (pieces, pommes, branches) trouves en revelant des cases
/// - Drops ennemis (steack, peau) quand un ennemi est vaincu
/// Tous les taux sont reglables dans l'Inspector.
/// </summary>
public class LootSystem : MonoBehaviour
{
    public static LootSystem Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Inspector - Drops au sol (cases revelee vides/foret)
    // -------------------------------------------------------------------------

    // ── Tables de loot par zone (ScriptableObjects — null = utilise les valeurs surface ci-dessous)
    [Header("═══ Loot par zone (optionnel) ═══")]
    [Tooltip("Laisser vide = utilise les taux ci-dessous (surface)")]
    [SerializeField] private LootTable _surfaceLootTable;
    [SerializeField] private LootTable _undergroundLootTable;
    [SerializeField] private LootTable _indoorLootTable;

    /// <summary>Retourne la table active selon la couche du héros.</summary>
    private LootTable ActiveTable()
    {
        var layer = ActiveGrid.Layer;
        return layer switch
        {
            GridLayer.Underground when _undergroundLootTable != null => _undergroundLootTable,
            GridLayer.Indoor when _indoorLootTable != null => _indoorLootTable,
            _ => _surfaceLootTable  // null = taux hardcodés ci-dessous (comportement actuel)
        };
    }

    [Header("--- Pieces d'or ---")]
    [Tooltip("Chance par case revelee de trouver une piece (0=jamais, 1=toujours)")]
    [SerializeField, Range(0f, 0.2f)] private float _goldChance = 0.03f;
    [Tooltip("Nombre de pieces maximum par case")]
    [SerializeField, Range(1, 5)] private int _goldMaxPerCell = 2;

    [Header("--- Pommes ---")]
    [Tooltip("Chance par case revelee de trouver une pomme")]
    [SerializeField, Range(0f, 0.1f)] private float _appleChance = 0.015f;

    [Header("--- Branches ---")]
    [Tooltip("Chance par case revelee de trouver une branche")]
    [SerializeField, Range(0f, 0.2f)] private float _branchChance = 0.06f;
    [Tooltip("Nombre de branches maximum par case")]
    [SerializeField, Range(1, 3)] private int _branchMaxPerCell = 2;

    // -------------------------------------------------------------------------
    // Inspector - Drops ennemis
    // -------------------------------------------------------------------------

    [Header("--- Loup ---")]
    [SerializeField, Range(0f, 1f)] private float _wolfSteakChance = 0.50f;
    [SerializeField, Range(0f, 1f)] private float _wolfHideChance = 0.15f;

    [Header("--- Ours ---")]
    [SerializeField, Range(0f, 1f)] private float _bearSteakChance = 0.65f;
    [SerializeField, Range(0f, 1f)] private float _bearHideChance = 0.25f;

    [Header("--- Mercenaire ---")]
    [Tooltip("Chance de trouver des pieces sur un mercenaire vaincu")]
    [SerializeField, Range(0f, 1f)] private float _mercGoldChance = 0.70f;
    [SerializeField, Range(1, 5)] private int _mercGoldAmount = 2;

    [Header("--- Bandit ---")]
    [SerializeField, Range(0f, 1f)] private float _banditGoldChance = 0.50f;
    [SerializeField, Range(1, 3)] private int _banditGoldAmount = 1;

    // -------------------------------------------------------------------------
    // Etat interne
    // -------------------------------------------------------------------------

    private System.Random _rng;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnRunStarted>(OnRunStarted);
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
    }

    private void OnRunStarted(OnRunStarted evt)
    {
        _rng = new System.Random();
        _droppedCells = new HashSet<(int, int)>();
    }

    // -------------------------------------------------------------------------
    // Drop au sol - cases vides revelee
    // -------------------------------------------------------------------------

    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        if (_rng == null) _rng = new System.Random();
        var gm = GridManager.Instance;
        if (gm == null) return;

        // Si une LootTable spécifique à la zone est définie, la déléguer (futur)
        // Pour l'instant : pas de loot surface en underground/indoor sans table
        var layer = ActiveGrid.Layer;
        if (layer != GridLayer.Surface && ActiveTable() == null) return;
        int totalXP = 0;

        foreach (var c in evt.Cells)
        {
            if (c.Content != CellContent.Empty && c.Content != CellContent.Number)
                continue;
            if (c.IsEmpty) totalXP += c.XPValue;

            var key = (c.X, c.Y);
            if (_droppedCells.Contains(key)) continue;
            _droppedCells.Add(key);
            if (IsCellOccupied(c.X, c.Y)) continue;
            if (HasGroundItemOnCell(c.X, c.Y)) continue;

            Vector3 worldPos = new Vector3(c.X * gm.CellStep, c.Y * gm.CellStep, 0f);
            if (Roll(_goldChance))
                GiveItem(ItemID.PieceOr, _rng.Next(1, _goldMaxPerCell + 1), worldPos);
            else if (Roll(_appleChance))
                GiveItem(ItemID.Pomme, 1, worldPos);
            else if (Roll(_branchChance))
                GiveItem(ItemID.Branche, _rng.Next(1, _branchMaxPerCell + 1), worldPos);
        }

    }
    // Cache des cases deja droppees pour eviter les doublons (flood fill)
    private HashSet<(int, int)> _droppedCells = new HashSet<(int, int)>();

    // Registres statiques � mis � jour par WorldObject et GroundItem eux-m�mes
    // �vite FindObjectsOfType (scan toute la sc�ne) appel� pour chaque case
    private static readonly HashSet<(int, int)> _occupiedCells = new HashSet<(int, int)>();
    private static readonly HashSet<(int, int)> _groundItemCells = new HashSet<(int, int)>();

    // Appel� par WorldObject.Initialize()
    public static void RegisterOccupied(int x, int y) => _occupiedCells.Add((x, y));
    public static void UnregisterOccupied(int x, int y) => _occupiedCells.Remove((x, y));

    // Appel� par GroundItem quand il est pos�/ramass�
    public static void RegisterGroundItem(int x, int y) => _groundItemCells.Add((x, y));
    public static void UnregisterGroundItem(int x, int y) => _groundItemCells.Remove((x, y));

    private bool IsCellOccupied(int x, int y) => _occupiedCells.Contains((x, y));
    private bool HasGroundItemOnCell(int x, int y) => _groundItemCells.Contains((x, y));

    // -------------------------------------------------------------------------
    // Drop ennemi - appele depuis CellView apres revelation ennemi
    // -------------------------------------------------------------------------

    public void RollEnemyDrop(CellContent enemyType, Vector3 worldPos)
    {
        if (_rng == null) _rng = new System.Random();

        switch (enemyType)
        {
            case CellContent.Enemy_Wolf:
                if (Roll(_wolfSteakChance)) GiveItem(ItemID.SteackCru, 1, worldPos);
                if (Roll(_wolfHideChance)) GiveItem(ItemID.PeauAnimal, 1, worldPos);
                break;

            case CellContent.Enemy_Bear:
                if (Roll(_bearSteakChance)) GiveItem(ItemID.SteackCru, 1, worldPos);
                if (Roll(_bearHideChance)) GiveItem(ItemID.PeauAnimal, 1, worldPos);
                break;

            case CellContent.Enemy_Mercenary:
                if (Roll(_mercGoldChance)) GiveItem(ItemID.PieceOr, _mercGoldAmount, worldPos);
                break;

            case CellContent.Enemy_BanditSword:
                if (Roll(_banditGoldChance)) GiveItem(ItemID.PieceOr, _banditGoldAmount, worldPos);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void GiveItem(ItemID id, int quantity, Vector3 worldPos)
    {
        if (Inventory.Instance == null) return;

        // Si Database pas encore init, on force l'initialisation
        if (Inventory.Database == null)
            Inventory.Instance.ForceInitDatabase();

        if (Inventory.Database == null)
        {
            Debug.LogWarning("[LootSystem] Database introuvable - verifie que ItemDatabase est assigne sur le GameObject Inventory");
            return;
        }

        var itemData = Inventory.Database.Get(id);
        if (itemData == null) return;

        SpawnGroundItem(itemData, quantity, worldPos);

        Debug.Log("<color=#FFD700>[LootSystem]</color> Drop au sol : "
            + quantity + "x " + itemData.displayName);
    }

    private bool Roll(float chance)
    {
        return (float)_rng.NextDouble() < chance;
    }

    // -------------------------------------------------------------------------
    // Spawn objet au sol
    // -------------------------------------------------------------------------

    // ── Pool de GroundItems (réutilisation — évite Instantiate/Destroy répétés) ──
    [Header("Pool GroundItems")]
    [Tooltip("Prefab avec SpriteRenderer + GroundItem + BoxCollider2D (optionnel)")]
    [SerializeField] private GroundItem _groundItemPrefab;

    private readonly System.Collections.Generic.Queue<GroundItem> _groundItemPool
        = new System.Collections.Generic.Queue<GroundItem>(64);

    private GroundItem GetOrCreateGroundItem()
    {
        while (_groundItemPool.Count > 0)
        {
            var existing = _groundItemPool.Dequeue();
            if (existing != null) { existing.gameObject.SetActive(true); return existing; }
        }
        if (_groundItemPrefab != null)
        {
            var go2 = Instantiate(_groundItemPrefab);
            go2.gameObject.SetActive(false);
            return go2;
        }
        // Fallback : création manuelle
        var fallback = new GameObject("GroundItem");
        fallback.AddComponent<SpriteRenderer>();
        var gi = fallback.AddComponent<GroundItem>();
        var col2 = fallback.AddComponent<BoxCollider2D>();
        col2.size = Vector2.one * 0.8f;
        return gi;
    }

    /// <summary>
    /// Appelé par GroundItem quand l'item est ramassé → retourne au pool.
    /// </summary>
    public static void ReturnToPool(GroundItem item)
    {
        if (Instance == null || item == null) return;
        item.gameObject.SetActive(false);
        Instance._groundItemPool.Enqueue(item);
    }

    private void SpawnGroundItem(ItemData itemData, int quantity, Vector3 worldPos)
    {
        var item = GetOrCreateGroundItem();
        item.transform.position = worldPos + new Vector3(0f, 0f, -0.2f);
        item.transform.localScale = Vector3.one * 0.6f;

        var sr = item.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sprite = itemData.icon;
            sr.sortingLayerName = "CellContent";
            sr.sortingOrder = 8;
        }
        item.Initialize(itemData, quantity);
    }
}