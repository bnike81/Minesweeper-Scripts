using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// CaveManager — Transitions surface ↔ grotte.
///
/// Même logique que IndoorManager :
///   - Contenu underground caché au démarrage
///   - Montré quand on entre, caché quand on sort
///   - Pas de chargement/déchargement de scène
///
/// FLOW :
///   1. MountainBuilder → RegisterEntrance(gridX, gridY, isBottom)
///   2. CaveEntranceTrigger.OnMouseDown → EnterCave(gridX, gridY)
///   3. CaveGenerator.Generate() si pas encore généré
///   4. SceneLoader.EnterUnderground() → cache MainGrid
///   5. Contenu underground affiché + héros téléporté
///   6. Héros atteint sortie → ExitCave()
///   7. SceneLoader.ExitUnderground() → réaffiche MainGrid
/// </summary>
public class CaveManager : MonoBehaviour
{
    public static CaveManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Contenu Underground")]
    [Tooltip("GO parent du contenu visible de la grotte.\n" +
             "Contient UndergroundVisuals + UndergroundFog.\n" +
             "Caché au démarrage, affiché quand on entre.")]
    [SerializeField] private GameObject _undergroundContent;

    [Header("Références")]
    [SerializeField] private CaveGenerator _caveGenerator;
    [SerializeField] private UndergroundGrid _undergroundGrid;
    [SerializeField] private UndergroundFog _fog;

    // ── Portails ──────────────────────────────────────────────────────────────

    public struct CaveEntrance
    {
        public int gridX, gridY;
        public bool isBottom;
    }

    private readonly List<CaveEntrance> _entrances = new();
    private CaveEntrance _activeEntry;

    // ── État ──────────────────────────────────────────────────────────────────

    public bool IsInCave { get; private set; }

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Découverte auto (CaveManager est dans Core, le contenu dans Underground)
        if (_caveGenerator == null)
            _caveGenerator = GetComponentInChildren<CaveGenerator>(true);
        if (_undergroundGrid == null)
            _undergroundGrid = FindObjectOfType<UndergroundGrid>(true);
        if (_fog == null)
            _fog = FindObjectOfType<UndergroundFog>(true);

        // Trouver le contenu underground s'il n'est pas assigné
        if (_undergroundContent == null)
        {
            // Chercher un GO nommé "UndergroundContent" dans toutes les scènes
            var visuals = FindObjectOfType<UndergroundVisuals>(true);
            if (visuals != null)
                _undergroundContent = visuals.transform.parent?.gameObject ?? visuals.gameObject;
        }

        // Cacher le contenu au démarrage
        if (_undergroundContent != null)
        {
            _undergroundContent.SetActive(false);
            Debug.Log("[CaveManager] Contenu underground caché au démarrage.");
        }
        else
        {
            Debug.LogWarning("[CaveManager] ⚠️ Underground Content non trouvé ! " +
                             "Assigner dans Inspector ou créer un GO 'UndergroundContent'.");
        }
    }

    private void OnEnable() => EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    private void OnDisable() => EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);

    private void OnRunStarted(OnRunStarted _)
    {
        _entrances.Clear();
        IsInCave = false;
        _undergroundContent?.SetActive(false);
        _undergroundGrid?.Clear();
    }

    // =========================================================================
    // ENREGISTREMENT DES PORTAILS
    // =========================================================================

    public void RegisterEntrance(int gridX, int gridY, bool isBottom)
    {
        // Éviter doublons
        foreach (var e in _entrances)
            if (e.gridX == gridX && e.gridY == gridY) return;

        _entrances.Add(new CaveEntrance { gridX = gridX, gridY = gridY, isBottom = isBottom });
        Debug.Log($"[CaveManager] Portail enregistré : ({gridX},{gridY}) bottom={isBottom}");
    }

    // =========================================================================
    // ENTRÉE DANS LA GROTTE
    // =========================================================================

    public void EnterCave(int entranceGridX, int entranceGridY)
    {
        if (IsInCave) return;
        if (IndoorManager.Instance?.IsIndoor == true) return;

        // Trouver l'entrée correspondante
        CaveEntrance? found = null;
        foreach (var e in _entrances)
        {
            if (Mathf.Abs(e.gridX - entranceGridX) <= 1 &&
                Mathf.Abs(e.gridY - entranceGridY) <= 2)
            { found = e; break; }
        }
        if (!found.HasValue && _entrances.Count > 0) found = _entrances[0];
        if (!found.HasValue)
        {
            Debug.LogWarning($"[CaveManager] Aucun portail trouvé près de ({entranceGridX},{entranceGridY})");
            return;
        }
        _activeEntry = found.Value;

        // Trouver la sortie (autre portail)
        int exitX = _activeEntry.gridX;
        foreach (var e in _entrances)
        {
            if (e.gridX != _activeEntry.gridX || e.gridY != _activeEntry.gridY)
            { exitX = e.gridX; break; }
        }

        // Activer le contenu AVANT la génération
        // (les CellView ont besoin d'un GO actif pour que Awake/SetBg fonctionnent)
        _undergroundContent?.SetActive(true);

        // Générer la grotte (une seule fois)
        if (!_undergroundGrid.IsGenerated)
            _caveGenerator?.Generate(_activeEntry.gridX, exitX);

        // Transition (cache MainGrid)
        SceneLoader.Instance?.EnterUnderground();
        EventBus.Subscribe<OnZoneTransitionComplete>(OnCaveReady);

        IsInCave = true;
        Debug.Log($"[CaveManager] ► Entrée grotte depuis ({_activeEntry.gridX},{_activeEntry.gridY})");
    }

    private void OnCaveReady(OnZoneTransitionComplete e)
    {
        EventBus.Unsubscribe<OnZoneTransitionComplete>(OnCaveReady);
        if (e.Zone != SceneLoader.ZoneScene.Underground) return;

        // Contenu déjà activé dans EnterCave (avant génération)

        // Téléporter le héros à l'entrée
        var spawnGrid = _undergroundGrid.EntryPos + Vector2Int.up;
        var spawnWorld = _undergroundGrid.GridToWorld(spawnGrid.x, spawnGrid.y);
        HeroController.Instance?.TeleportToCave(spawnGrid.x, spawnGrid.y, spawnWorld);

        // Fog initial autour du héros
        _fog?.UpdateVision(spawnGrid.x, spawnGrid.y);

        TooltipUI.Hide();

        Debug.Log($"[CaveManager] ► Dans la grotte — spawn {spawnGrid} world={spawnWorld}");
    }

    // =========================================================================
    // SORTIE DE LA GROTTE
    // =========================================================================

    public void ExitCave(int x = 0, int y = 0)
    {
        if (!IsInCave) return;

        // Cacher le contenu underground
        _undergroundContent?.SetActive(false);

        // Transition retour surface
        SceneLoader.Instance?.ExitUnderground();
        EventBus.Subscribe<OnZoneTransitionComplete>(OnReturnToSurface);

        IsInCave = false;
        Debug.Log("[CaveManager] ◄ Sortie grotte...");
    }

    private void OnReturnToSurface(OnZoneTransitionComplete e)
    {
        EventBus.Unsubscribe<OnZoneTransitionComplete>(OnReturnToSurface);
        if (e.Zone != SceneLoader.ZoneScene.MainGrid) return;

        // Téléporter le héros au portail de sortie en surface
        if (_entrances.Count > 0)
        {
            // Utiliser le dernier portail enregistré (ou le portail opposé)
            var exit = _entrances[_entrances.Count > 1 ? 1 : 0];
            float cs = GridManager.Instance?.CellStep ?? 1.063f;
            var world = new Vector3(exit.gridX * cs, exit.gridY * cs, 0f);
            HeroController.Instance?.TeleportToCave(exit.gridX, exit.gridY, world);
        }

        Debug.Log("[CaveManager] ◄ Retour surface");
    }

    // =========================================================================
    // UPDATE — Fog suit le héros
    // =========================================================================

    private void Update()
    {
        if (!IsInCave || _fog == null || _undergroundGrid == null) return;

        var hero = HeroController.Instance;
        if (hero == null) return;

        // Mettre à jour le fog autour de la position du héros
        var heroGrid = _undergroundGrid.WorldToGrid(hero.transform.position);
        _fog.UpdateVision(heroGrid.x, heroGrid.y);
    }
}