using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// CaveManager — Transitions surface ↔ grottes.
///
/// MAPPING DIRECT :
///   Chaque portail surface connaît sa position cave exacte.
///   Chaque portail cave connaît sa position surface exacte.
///   Pas de comparaison Y — mapping strict 1:1.
///
/// GÉNÉRATION ADDITIVE :
///   GenerateCave(caveIndex) génère une cave sur l'UndergroundGrid
///   SANS effacer les caves précédentes. Appelé par MountainBuilder
///   quand la montagne se génère en surface.
///
/// SORTIE :
///   Le héros revient à la position surface du portail qu'il utilise.
/// </summary>
public class CaveManager : MonoBehaviour
{
    public static CaveManager Instance { get; private set; }

    [Header("Contenu Underground")]
    [SerializeField] private GameObject _undergroundContent;

    [Header("Références")]
    [SerializeField] private CaveGenerator _caveGenerator;
    [SerializeField] private UndergroundGrid _undergroundGrid;
    [SerializeField] private UndergroundFog _fog;

    // ── CaveSpawners ──────────────────────────────────────────────────────────

    private readonly List<CaveSpawner> _spawners = new();

    public void RegisterSpawner(CaveSpawner s)
    {
        if (s != null && !_spawners.Contains(s))
        {
            // Warning si un spawner avec le même index existe déjà
            foreach (var existing in _spawners)
                if (existing.CaveIndex == s.CaveIndex)
                    Debug.LogWarning($"[CaveManager] ⚠ CaveSpawner doublon index={s.CaveIndex} ! " +
                                    $"Vérifier Inspector : chaque montagne doit avoir un Cave Index unique.");
            _spawners.Add(s);
        }
    }
    public void UnregisterSpawner(CaveSpawner s) => _spawners.Remove(s);
    public CaveSpawner GetSpawner(int ci)
    {
        foreach (var s in _spawners) if (s.CaveIndex == ci) return s;
        return null;
    }

    // ── Portails : mapping direct surface ↔ cave ──────────────────────────────

    public struct PortalLink
    {
        public int surfaceX, surfaceY;     // position surface (main grid)
        public int caveX, caveY;           // position cave (underground grid)
        public int caveIndex;
    }

    private readonly List<PortalLink> _portalLinks = new();
    private readonly HashSet<int> _generatedCaves = new();
    private readonly Dictionary<int, CaveLayout> _caveLayouts = new();

    private PortalLink _activeLink;

    public bool IsInCave { get; private set; }
    public int ActiveCaveIndex => _activeLink.caveIndex;

    /// <summary>Retourne le Y min et max de la cave active dans la grille.</summary>
    public (int minY, int maxY) GetCaveYRange(int caveIndex)
    {
        if (_caveLayouts.TryGetValue(caveIndex, out var layout))
        {
            int minY = Mathf.Max(0, layout.EntryLocalY - 5);
            int maxY = layout.ExitLocalY + layout.Height / 2;
            return (minY, maxY);
        }
        return (0, 9999);
    }

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_caveGenerator == null) _caveGenerator = GetComponentInChildren<CaveGenerator>(true);
        if (_undergroundGrid == null) _undergroundGrid = FindObjectOfType<UndergroundGrid>(true);
        if (_fog == null) _fog = FindObjectOfType<UndergroundFog>(true);

        if (_undergroundContent == null)
        {
            var visuals = FindObjectOfType<UndergroundVisuals>(true);
            if (visuals != null)
                _undergroundContent = visuals.transform.parent?.gameObject ?? visuals.gameObject;
        }
        _undergroundContent?.SetActive(false);
    }

    private void OnEnable() => EventBus.Subscribe<OnRunStarted>(OnRunStarted);
    private void OnDisable() => EventBus.Unsubscribe<OnRunStarted>(OnRunStarted);

    private void OnRunStarted(OnRunStarted _)
    {
        _portalLinks.Clear();
        _generatedCaves.Clear();
        _caveLayouts.Clear();
        IsInCave = false;
        _undergroundContent?.SetActive(false);
        _undergroundGrid?.Clear();
    }

    // =========================================================================
    // ENREGISTREMENT DES PORTAILS SURFACE
    //
    // Appelé par MountainBuilder.AttachCaveEntrance quand la montagne spawn.
    // On stocke la position surface — la position cave sera remplie après
    // génération via LinkCavePortals().
    // =========================================================================

    public void RegisterEntrance(int gridX, int gridY, bool isBottom, int caveIndex = 0)
    {
        // Éviter les doublons
        foreach (var l in _portalLinks)
            if (l.surfaceX == gridX && l.surfaceY == gridY) return;

        _portalLinks.Add(new PortalLink
        {
            surfaceX = gridX,
            surfaceY = gridY,
            caveX = -1,
            caveY = -1,  // pas encore connu
            caveIndex = caveIndex
        });
        Debug.Log($"[CaveManager] Portail surface : ({gridX},{gridY}) cave={caveIndex}");
    }

    public void RegisterEntrance(int gridX, int gridY, bool isBottom)
        => RegisterEntrance(gridX, gridY, isBottom, 0);

    // =========================================================================
    // GÉNÉRATION ADDITIVE
    //
    // Appelé par MountainBuilder QUAND la montagne se génère.
    // Génère la cave correspondante sur l'UndergroundGrid SANS effacer
    // les caves précédentes.
    // =========================================================================

    public void GenerateCave(int caveIndex)
    {
        if (_generatedCaves.Contains(caveIndex)) return;

        // Recherche CaveGenerator si null
        if (_caveGenerator == null) _caveGenerator = GetComponentInChildren<CaveGenerator>(true);
        if (_caveGenerator == null) _caveGenerator = FindObjectOfType<CaveGenerator>(true);
        if (_caveGenerator == null)
        {
            Debug.LogError($"[CaveManager] ❌ CaveGenerator null pour cave={caveIndex}");
            return;
        }

        var spawner = GetSpawner(caveIndex);
        if (spawner != null && spawner.Config != null)
        {
            _caveGenerator._config = spawner.Config;
            Debug.Log($"[CaveManager] Config={spawner.Config.name} pour cave={caveIndex}");
        }
        else
        {
            Debug.LogWarning($"[CaveManager] ⚠ Pas de CaveSpawner/Config pour cave={caveIndex}, " +
                             $"utilise config existante={_caveGenerator._config?.name ?? "null"}");
        }

        var portals = new List<PortalLink>();
        foreach (var l in _portalLinks)
            if (l.caveIndex == caveIndex) portals.Add(l);

        Debug.Log($"[CaveManager] GenerateCave({caveIndex}) : {portals.Count} portails, " +
                  $"total links={_portalLinks.Count}");

        if (portals.Count == 0)
        {
            Debug.LogWarning($"[CaveManager] Aucun portail pour cave={caveIndex}");
            // Lister tous les portals pour diagnostic
            foreach (var l in _portalLinks)
                Debug.Log($"  → link: surface=({l.surfaceX},{l.surfaceY}) cave=({l.caveX},{l.caveY}) ci={l.caveIndex}");
            return;
        }

        bool wasActive = _undergroundContent != null && _undergroundContent.activeSelf;
        _undergroundContent?.SetActive(true);

        int entryX = portals[0].surfaceX, entryY = portals[0].surfaceY;
        int exitX = entryX, exitY = entryY;
        bool isTwoWay = true;

        if (portals.Count >= 2)
        {
            exitX = portals[1].surfaceX;
            exitY = portals[1].surfaceY;
            isTwoWay = false;
        }

        Transform cellParent = null;
        if (spawner != null)
        {
            spawner.EnsureContainers();
            cellParent = spawner.CellsContainer;
        }

        bool additive = _generatedCaves.Count > 0;
        _caveGenerator.Generate(entryX, entryY, exitX, exitY,
                                isTwoWay, additive, cellParent);

        if (_caveGenerator.Layout != null)
        {
            _caveLayouts[caveIndex] = _caveGenerator.Layout;
            LinkCavePortals(caveIndex, _caveGenerator.Layout, portals);
            _generatedCaves.Add(caveIndex);
            Debug.Log($"[CaveManager] ✅ Cave {caveIndex} générée additive={additive} " +
                      $"Entry={_caveGenerator.Layout.EntryPos} Exit={_caveGenerator.Layout.ExitPos}");
        }
        else
        {
            Debug.LogError($"[CaveManager] ❌ Cave {caveIndex} Layout null après Generate ! " +
                           $"config={_caveGenerator._config?.name ?? "null"}");
        }

        if (!wasActive) _undergroundContent?.SetActive(false);
    }

    private void LinkCavePortals(int ci, CaveLayout layout, List<PortalLink> portals)
    {
        if (portals.Count == 1)
        {
            var p = portals[0];
            p.caveX = layout.EntryPos.x;
            p.caveY = layout.EntryPos.y;
            UpdateLink(p);
        }
        else if (portals.Count >= 2)
        {
            var lo = portals[0];
            var hi = portals[1];
            if (hi.surfaceY < lo.surfaceY) { var tmp = lo; lo = hi; hi = tmp; }

            lo.caveX = layout.EntryPos.x; lo.caveY = layout.EntryPos.y;
            hi.caveX = layout.ExitPos.x; hi.caveY = layout.ExitPos.y;

            UpdateLink(lo);
            UpdateLink(hi);
        }
    }

    private void UpdateLink(PortalLink updated)
    {
        for (int i = 0; i < _portalLinks.Count; i++)
        {
            if (_portalLinks[i].surfaceX == updated.surfaceX &&
                _portalLinks[i].surfaceY == updated.surfaceY)
            {
                _portalLinks[i] = updated;
                Debug.Log($"[CaveManager] Link : surface({updated.surfaceX},{updated.surfaceY}) " +
                          $"↔ cave({updated.caveX},{updated.caveY}) ci={updated.caveIndex}");
                return;
            }
        }
    }

    // =========================================================================
    // ENTRÉE DANS LA GROTTE
    // =========================================================================

    public void EnterCave(int entranceGridX, int entranceGridY)
    {
        if (IsInCave) return;
        if (IndoorManager.Instance?.IsIndoor == true) return;

        // Trouver le PortalLink du portail surface cliqué
        PortalLink? found = null;
        foreach (var l in _portalLinks)
        {
            if (Mathf.Abs(l.surfaceX - entranceGridX) <= 1 &&
                Mathf.Abs(l.surfaceY - entranceGridY) <= 2)
            { found = l; break; }
        }
        if (!found.HasValue) return;

        int ci = found.Value.caveIndex;

        // Générer si pas encore fait OU si les liens sont incomplets
        if (!_generatedCaves.Contains(ci))
        {
            GenerateCave(ci);
        }

        // Re-lire le link APRÈS génération
        found = null;
        foreach (var l in _portalLinks)
        {
            if (Mathf.Abs(l.surfaceX - entranceGridX) <= 1 &&
                Mathf.Abs(l.surfaceY - entranceGridY) <= 2 &&
                l.caveIndex == ci)
            { found = l; break; }
        }

        // Si toujours pas lié → forcer régénération (nettoyage + retry)
        if (found.HasValue && found.Value.caveX < 0)
        {
            Debug.LogWarning($"[CaveManager] ⚠ Portail non lié, régénération cave={ci}...");
            _generatedCaves.Remove(ci);
            GenerateCave(ci);

            // Re-lire une dernière fois
            found = null;
            foreach (var l in _portalLinks)
            {
                if (Mathf.Abs(l.surfaceX - entranceGridX) <= 1 &&
                    Mathf.Abs(l.surfaceY - entranceGridY) <= 2 &&
                    l.caveIndex == ci)
                { found = l; break; }
            }
        }

        if (!found.HasValue || found.Value.caveX < 0)
        {
            Debug.LogError($"[CaveManager] ❌ Portail cave non lié ! " +
                           $"surface=({entranceGridX},{entranceGridY}) cave={ci} " +
                           $"caveX={found?.caveX ?? -99} portals={_portalLinks.Count} " +
                           $"generated={_generatedCaves.Contains(ci)}");
            return;
        }

        _activeLink = found.Value;

        _undergroundContent?.SetActive(true);

        SceneLoader.Instance?.EnterUnderground();
        EventBus.Subscribe<OnZoneTransitionComplete>(OnCaveReady);

        IsInCave = true;
        Debug.Log($"[CaveManager] ► cave={ci} " +
                  $"surface=({_activeLink.surfaceX},{_activeLink.surfaceY}) " +
                  $"→ cave=({_activeLink.caveX},{_activeLink.caveY})");
    }

    private void OnCaveReady(OnZoneTransitionComplete e)
    {
        EventBus.Unsubscribe<OnZoneTransitionComplete>(OnCaveReady);
        if (e.Zone != SceneLoader.ZoneScene.Underground) return;

        int ci = _activeLink.caveIndex;

        // Spawn hero au bon portail
        var spawnGrid = new Vector2Int(_activeLink.caveX, _activeLink.caveY + 1);
        var spawnWorld = _undergroundGrid.GridToWorld(spawnGrid.x, spawnGrid.y);
        HeroController.Instance?.TeleportToCave(spawnGrid.x, spawnGrid.y, spawnWorld);

        // ── Isoler la cave active ─────────────────────────────────────────────
        // Activer le CaveSpawner actif, désactiver les autres.
        // Les cells, ennemis et items de chaque cave sont enfants de leur spawner.
        foreach (var spawner in _spawners)
        {
            if (spawner == null) continue;
            bool isActive = spawner.CaveIndex == ci;
            spawner.CellsContainer?.gameObject.SetActive(isActive);
            spawner.EnemiesContainer?.gameObject.SetActive(isActive);
            spawner.ItemsContainer?.gameObject.SetActive(isActive);
        }

        _fog?.UpdateVision(spawnGrid.x, spawnGrid.y);
        TooltipUI.Hide();

        Debug.Log($"[CaveManager] ► Spawn cave={ci} grid=({spawnGrid.x},{spawnGrid.y})");
    }

    // =========================================================================
    // SORTIE DE LA GROTTE
    //
    // Le héros revient à la position surface du portail qu'il utilise.
    // Mapping direct : on cherche le PortalLink dont la cave pos correspond.
    // =========================================================================

    public void ExitCave(int portalCaveX, int portalCaveY)
    {
        if (!IsInCave) return;

        // Trouver le PortalLink dont la position cave correspond
        PortalLink? exitLink = null;
        int bestDist = int.MaxValue;
        foreach (var l in _portalLinks)
        {
            if (l.caveX < 0) continue; // pas encore lié
            int d = Mathf.Abs(l.caveX - portalCaveX) + Mathf.Abs(l.caveY - portalCaveY);
            if (d < bestDist) { bestDist = d; exitLink = l; }
        }

        if (!exitLink.HasValue) return;

        _activeLink = exitLink.Value;

        _undergroundContent?.SetActive(false);
        SceneLoader.Instance?.ExitUnderground();
        EventBus.Subscribe<OnZoneTransitionComplete>(OnReturnToSurface);

        IsInCave = false;
        Debug.Log($"[CaveManager] ◄ cave=({portalCaveX},{portalCaveY}) " +
                  $"→ surface=({_activeLink.surfaceX},{_activeLink.surfaceY})");
    }

    private void OnReturnToSurface(OnZoneTransitionComplete e)
    {
        EventBus.Unsubscribe<OnZoneTransitionComplete>(OnReturnToSurface);
        if (e.Zone != SceneLoader.ZoneScene.MainGrid) return;

        // Retour direct à la position surface du portail utilisé
        float cs = GridManager.Instance?.CellStep ?? 1.063f;
        var world = new Vector3(_activeLink.surfaceX * cs, _activeLink.surfaceY * cs, 0f);
        HeroController.Instance?.TeleportToCave(
            _activeLink.surfaceX, _activeLink.surfaceY, world);

        Debug.Log($"[CaveManager] ◄ Retour surface ({_activeLink.surfaceX},{_activeLink.surfaceY})");
    }

    // =========================================================================
    // UPDATE — Fog
    // =========================================================================

    private void Update()
    {
        if (!IsInCave || _fog == null || _undergroundGrid == null) return;
        var hero = HeroController.Instance;
        if (hero == null) return;
        var heroGrid = _undergroundGrid.WorldToGrid(hero.transform.position);
        _fog.UpdateVision(heroGrid.x, heroGrid.y);
    }
}