using UnityEngine;
using System.Collections;

/// <summary>
/// HeroInteraction - Gere la collecte d items et les interactions NPC.
/// Separe de HeroController pour la clarte.
/// La collecte ne se declenche QUE si CollectOnArrival = true.
/// </summary>
public class HeroInteraction : MonoBehaviour
{
    public static HeroInteraction Instance { get; private set; }

    private float _cellStep = 1.05f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _cellStep = GridManager.Instance?.CellStep ?? 1.05f;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnGridGenerated>(OnGrid);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnGridGenerated>(OnGrid);
    }

    private void OnGrid(OnGridGenerated e)
    {
        _cellStep = GridManager.Instance?.CellStep ?? _cellStep;
    }

    /// <summary>Convertit une pos grille en pos monde via la grille active (surface/cave).</summary>
    private Vector3 GridToWorld(Vector2Int p)
    {
        var grid = ActiveGrid.Current;
        if (grid != null) return grid.GridToWorld(p.x, p.y);
        return new Vector3(p.x * _cellStep, p.y * _cellStep, 0f);
    }

    // -------------------------------------------------------------------------
    // Verifie et declenche un piege sur la case du heros
    // -------------------------------------------------------------------------

    public TrapInstance CheckTrap(Vector2Int heroPos)
    {
        var wp = (Vector2)GridToWorld(heroPos);
        var hits = Physics2D.OverlapCircleAll(wp, _cellStep * 0.3f);

        foreach (var h in hits)
        {
            var trap = h.GetComponent<TrapInstance>();
            if (trap == null || !trap.IsArmed) continue;

            bool blocked = trap.OnHeroStepOn();
            if (blocked) return trap; // Retourne le piege actif
        }
        return null; // Pas de piege ou desamorce
    }

    // -------------------------------------------------------------------------
    // Verifie si la case ciblee contient un item ramassable
    // -------------------------------------------------------------------------

    public bool HasGroundItemAt(Vector2Int pos)
    {
        var wp = (Vector2)GridToWorld(pos);
        var hits = Physics2D.OverlapCircleAll(wp, _cellStep * 0.3f);
        foreach (var h in hits)
        {
            var g = h.GetComponent<GroundItem>();
            if (g != null && g.ItemData != null) return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Tente de collecter un item a la position du heros
    // Seulement si CollectOnArrival = true
    // -------------------------------------------------------------------------

    public void TryCollect(Vector2Int heroPos)
    {
        var state = HeroState.Instance;
        if (state == null || !state.CollectOnArrival) return;
        state.CollectOnArrival = false;

        var wp = (Vector2)GridToWorld(heroPos);
        var hits = Physics2D.OverlapCircleAll(wp, _cellStep * 0.3f);

        foreach (var h in hits)
        {
            if (h.gameObject == HeroController.Instance?.gameObject) continue;

            var ground = h.GetComponent<GroundItem>();
            if (ground == null || ground.ItemData == null) continue;

            // Verifier meme case exacte via la grille active
            var grid = ActiveGrid.Current;
            Vector2Int itemGrid = grid != null
                ? grid.WorldToGrid(h.transform.position)
                : new Vector2Int(Mathf.RoundToInt(h.transform.position.x / _cellStep),
                                 Mathf.RoundToInt(h.transform.position.y / _cellStep));
            if (itemGrid.x != heroPos.x || itemGrid.y != heroPos.y) continue;

            var item = ground.ItemData;
            bool firstTime = Inventory.Instance != null
                          && !Inventory.Instance.HasDiscovered(item.itemID);

            ground.HideImmediate();
            state.IsUILocked = true;

            ItemPickupUI.Instance?.ShowPickup(item, firstTime, () =>
            {
                Inventory.Instance?.AddItem(item, ground.Quantity);
                Inventory.Instance?.MarkDiscovered(item.itemID);
                ground.Collect();
                state.IsUILocked = false;
            });
            return;
        }
    }
}