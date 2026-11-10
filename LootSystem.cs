using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// LootSystem — Gère les drops d'objets dans la grille.
///
/// ITEMS AU SOL : chaque biome a sa propre LootTable (ScriptableObject).
///   Quand une case vide est révélée, on lit cell.Biome → LootTable du biome
///   → roll chaque entrée de la table indépendamment.
///
/// DROPS ENNEMIS : configurés par ennemi (steak, peau, pièces).
///
/// Tous les taux sont réglables dans l'Inspector.
/// </summary>
public class LootSystem : MonoBehaviour
{
    public static LootSystem Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Inspector — Tables de loot par biome
    // -------------------------------------------------------------------------

    [Header("═══ Loot par biome ═══")]
    [Tooltip("Table de loot pour les cases Forest")]
    [SerializeField] private LootTable _forestLootTable;

    [Tooltip("Table de loot pour les cases Cave")]
    [SerializeField] private LootTable _caveLootTable;

    [Tooltip("Table de loot pour les cases Beach")]
    [SerializeField] private LootTable _beachLootTable;

    /// <summary>Retourne la LootTable correspondant au biome de la case.</summary>
    private LootTable GetTableForBiome(BiomeType biome)
    {
        return biome switch
        {
            BiomeType.Forest => _forestLootTable,
            BiomeType.Cave => _caveLootTable,
            BiomeType.Beach => _beachLootTable,
            _ => _forestLootTable   // fallback forêt
        };
    }

    // -------------------------------------------------------------------------
    // Inspector — Drops ennemis
    // -------------------------------------------------------------------------

    [Header("--- Loup ---")]
    [SerializeField, Range(0f, 1f)] private float _wolfSteakChance = 0.50f;
    [SerializeField, Range(0f, 1f)] private float _wolfHideChance = 0.15f;

    [Header("--- Ours ---")]
    [SerializeField, Range(0f, 1f)] private float _bearSteakChance = 0.65f;
    [SerializeField, Range(0f, 1f)] private float _bearHideChance = 0.25f;

    [Header("--- Mercenaire ---")]
    [Tooltip("Chance de trouver des pièces sur un mercenaire vaincu")]
    [SerializeField, Range(0f, 1f)] private float _mercGoldChance = 0.70f;
    [SerializeField, Range(1, 5)] private int _mercGoldAmount = 2;

    [Header("--- Bandit ---")]
    [SerializeField, Range(0f, 1f)] private float _banditGoldChance = 0.50f;
    [SerializeField, Range(1, 3)] private int _banditGoldAmount = 1;

    // -------------------------------------------------------------------------
    // État interne
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
    // Drop au sol — par biome
    // -------------------------------------------------------------------------

    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        if (_rng == null) _rng = new System.Random();
        var gm = GridManager.Instance;
        if (gm == null) return;

        foreach (var c in evt.Cells)
        {
            if (c.Content != CellContent.Empty && c.Content != CellContent.Number)
                continue;

            var key = (c.X, c.Y);
            if (_droppedCells.Contains(key)) continue;
            _droppedCells.Add(key);
            if (IsCellOccupied(c.X, c.Y)) continue;
            if (HasGroundItemOnCell(c.X, c.Y)) continue;

            // ── Trouver la LootTable du biome de cette case ──────────────
            var table = GetTableForBiome(c.Biome);
            if (table == null || !table.HasEntries) continue;

            // Position monde : cave utilise son propre offset Y
            Vector3 worldPos;
            if (c.Biome == BiomeType.Cave && UndergroundGrid.Instance != null)
                worldPos = UndergroundGrid.Instance.GridToWorld(c.X, c.Y);
            else
                worldPos = new Vector3(c.X * gm.CellStep, c.Y * gm.CellStep, 0f);

            // Roll chaque entrée indépendamment — un seul drop par case
            bool dropped = false;
            foreach (var entry in table.entries)
            {
                if (dropped) break;
                if (entry.item == ItemID.None) continue;
                if (Roll(entry.chance))
                {
                    int qty = entry.maxPerCell > 1
                        ? _rng.Next(1, entry.maxPerCell + 1)
                        : 1;
                    GiveItem(entry.item, qty, worldPos, c.Biome);
                    dropped = true;
                }
            }
        }
    }

    // Cache des cases déjà droppées pour éviter les doublons (flood fill)
    private HashSet<(int, int)> _droppedCells = new HashSet<(int, int)>();

    // Registres statiques — mis à jour par WorldObject et GroundItem eux-mêmes
    // Évite FindObjectsOfType (scan toute la scène) appelé pour chaque case
    private static readonly HashSet<(int, int)> _occupiedCells = new HashSet<(int, int)>();
    private static readonly HashSet<(int, int)> _groundItemCells = new HashSet<(int, int)>();

    // Appelé par WorldObject.Initialize()
    public static void RegisterOccupied(int x, int y) => _occupiedCells.Add((x, y));
    public static void UnregisterOccupied(int x, int y) => _occupiedCells.Remove((x, y));

    // Appelé par GroundItem quand il est posé/ramassé
    public static void RegisterGroundItem(int x, int y) => _groundItemCells.Add((x, y));
    public static void UnregisterGroundItem(int x, int y) => _groundItemCells.Remove((x, y));

    private bool IsCellOccupied(int x, int y) => _occupiedCells.Contains((x, y));
    private bool HasGroundItemOnCell(int x, int y) => _groundItemCells.Contains((x, y));

    // -------------------------------------------------------------------------
    // Drop ennemi — appelé depuis CellView après révélation ennemi
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

    private void GiveItem(ItemID id, int quantity, Vector3 worldPos,
                          BiomeType biome = BiomeType.Forest)
    {
        if (Inventory.Instance == null) return;

        // Si Database pas encore init, on force l'initialisation
        if (Inventory.Database == null)
            Inventory.Instance.ForceInitDatabase();

        if (Inventory.Database == null)
        {
            Debug.LogWarning("[LootSystem] Database introuvable — vérifie que ItemDatabase est assigné sur le GameObject Inventory");
            return;
        }

        var itemData = Inventory.Database.Get(id);
        if (itemData == null) return;

        SpawnGroundItem(itemData, quantity, worldPos, biome);

        Debug.Log("<color=#FFD700>[LootSystem]</color> Drop au sol : "
            + quantity + "x " + itemData.displayName
            + $" (biome={biome})");
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

    private readonly Queue<GroundItem> _groundItemPool = new Queue<GroundItem>(64);

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

    private void SpawnGroundItem(ItemData itemData, int quantity, Vector3 worldPos,
                                BiomeType biome = BiomeType.Forest)
    {
        var item = GetOrCreateGroundItem();
        // Dé-parenter si recyclé du pool (un item cave pourrait encore être sous _undergroundContent)
        item.transform.SetParent(null, false);
        item.transform.position = worldPos + new Vector3(0f, 0f, -0.2f);
        item.transform.localScale = Vector3.one * 0.6f;

        bool isCave = biome == BiomeType.Cave;

        var sr = item.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.sprite = itemData.icon;
            sr.sortingLayerName = "CellContent";
            // Cave : sortingOrder 7 → SOUS le fog (ordre 8) → filtré naturellement
            sr.sortingOrder = isCave ? 7 : 8;
        }

        // Cave : parenter sous _undergroundContent → auto-hide quand on sort
        if (isCave && CaveManager.Instance != null)
        {
            item.transform.SetParent(CaveManager.Instance.UndergroundContentRoot, true);
        }

        item.Initialize(itemData, quantity);
        item.gameObject.SetActive(true);
    }
}