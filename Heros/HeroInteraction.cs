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

    // -------------------------------------------------------------------------
    // Verifie et declenche un piege sur la case du heros
    // -------------------------------------------------------------------------

    public TrapInstance CheckTrap(Vector2Int heroPos)
    {
        var wp = new Vector2(heroPos.x * _cellStep, heroPos.y * _cellStep);
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
        var wp = new Vector2(pos.x * _cellStep, pos.y * _cellStep);
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

        var wp = new Vector2(heroPos.x * _cellStep, heroPos.y * _cellStep);
        var hits = Physics2D.OverlapCircleAll(wp, _cellStep * 0.3f);

        foreach (var h in hits)
        {
            if (h.gameObject == HeroController.Instance?.gameObject) continue;

            var ground = h.GetComponent<GroundItem>();
            if (ground == null || ground.ItemData == null) continue;

            // Verifier meme case exacte
            int ix = Mathf.RoundToInt(h.transform.position.x / _cellStep);
            int iy = Mathf.RoundToInt(h.transform.position.y / _cellStep);
            if (ix != heroPos.x || iy != heroPos.y) continue;

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