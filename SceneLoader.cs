using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SceneLoader — Orchestrateur central des transitions de scènes.
///
/// ARCHITECTURE :
///   Scènes permanentes (jamais déchargées) :
///     Core       → GameManager, EventBus, SceneLoader, AudioManager
///     UI         → HUD, dialogue, inventaire visuel, XP bar
///     Hero       → HeroController, HeroJump, HeroPathFinder, HeroCombat, Camera
///
///   Scènes de zone (chargées/déchargées selon la position du héros) :
///     MainGrid   → GridManager, FogOfWar, MountainBuilder, ShepherdFarmSpawner
///     Underground→ UndergroundGrid, ennemis caverne, CaveManager
///     Indoor     → Tilemaps, meubles, PNJs intérieurs, IndoorManager
///
/// TRANSITIONS :
///   Surface → Grotte  : MainGrid masquée + Underground chargée additivement
///   Surface → Indoor  : MainGrid masquée + Indoor chargée additivement
///   Retour  → Surface : zone alternative déchargée + MainGrid réaffichée
///
/// UTILISATION :
///   SceneLoader.Instance.EnterUnderground(portal);
///   SceneLoader.Instance.ExitUnderground();
///   SceneLoader.Instance.EnterIndoor(building);
///   SceneLoader.Instance.ExitIndoor();
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    // ── Noms de scènes (doivent correspondre exactement aux noms dans Build Settings) ──

    public const string SCENE_CORE = "Core";
    public const string SCENE_UI = "UI";
    public const string SCENE_HERO = "Hero";
    public const string SCENE_MAINGRID = "MainGrid";
    public const string SCENE_UNDERGROUND = "Underground";
    public const string SCENE_INDOOR = "Indoor";

    // ── Scènes permanentes (chargées au démarrage, ne sont jamais déchargées) ─

    private static readonly string[] PERMANENT_SCENES =
    {
        SCENE_CORE, SCENE_UI, SCENE_HERO
    };

    // ── État ──────────────────────────────────────────────────────────────────

    public enum ZoneScene { None, MainGrid, Underground, Indoor }

    public ZoneScene CurrentZone { get; private set; } = ZoneScene.None;
    public bool IsTransitioning { get; private set; }

    // GOs masqués de MainGrid pendant qu'une autre zone est active
    private readonly List<GameObject> _hiddenMainGridRoots = new();
    // GOs dynamiques (spawnés hors MainGrid) également masqués
    private readonly List<GameObject> _hiddenDynamicGOs = new();

    /// <summary>
    /// Masque les objets de surface spawnés dynamiquement hors de la scène MainGrid.
    /// Cela arrive quand la scène active au moment du spawn n'était pas MainGrid.
    /// Après le fix SetActiveScene, ce fallback ne sera plus nécessaire, mais il sécurise.
    /// </summary>
    private void HideDynamicSurfaceObjects()
    {
        _hiddenDynamicGOs.Clear();
        var mainScene = UnityEngine.SceneManagement.SceneManager
                                   .GetSceneByName(SCENE_MAINGRID);

        void HideType<T>() where T : UnityEngine.MonoBehaviour
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (c == null || !c.gameObject.activeSelf) continue;
                if (c.gameObject.scene == mainScene) continue; // déjà géré
                c.gameObject.SetActive(false);
                _hiddenDynamicGOs.Add(c.gameObject);
            }
        }
        HideType<EnemyInstance>();
        HideType<TrapInstance>();
        HideType<SheepBehaviour>();
        HideType<NPC>();
        HideType<GroundItem>();
        HideType<WorldObject>();
    }

    // =========================================================================
    // INIT
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Point d'entrée du jeu.
    /// Appelé depuis Core.Bootstrapper après que les scènes permanentes sont chargées.
    /// </summary>
    public void StartGame()
    {
        StartCoroutine(LoadZoneCoroutine(SCENE_MAINGRID, onComplete: () =>
        {
            // ⚠️ CRITIQUE : définir MainGrid comme scène active
            // Tous les Instantiate(prefab, null) iront dans MainGrid
            // → ennemis, moutons, items au sol, cabin sprites seront dans MainGrid
            // → SetMainGridVisible(false) les trouvera via GetRootGameObjects()
            var mainScene = UnityEngine.SceneManagement.SceneManager
                                       .GetSceneByName(SCENE_MAINGRID);
            if (mainScene.isLoaded)
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(mainScene);

            CurrentZone = ZoneScene.MainGrid;
            Debug.Log("[SceneLoader] MainGrid active + définie comme scène active.");
        }));
    }

    // =========================================================================
    // TRANSITIONS DE ZONE
    // =========================================================================

    // ── Surface → Sous-sol ───────────────────────────────────────────────────

    /// <summary>
    /// Charge Underground additivement et masque les GOs de MainGrid.
    /// Appelé par CaveManager.EnterCave() au lieu de GridLayerManager.SwitchToUnderground().
    /// </summary>
    public void EnterUnderground()
    {
        if (IsTransitioning || CurrentZone == ZoneScene.Underground) return;
        StartCoroutine(EnterUndergroundSimple());
    }

    private IEnumerator EnterUndergroundSimple()
    {
        IsTransitioning = true;
        EventBus.Publish(new OnZoneTransitionStart { From = CurrentZone, To = ZoneScene.Underground });
        SetMainGridVisible(false);
        yield return null;
        CurrentZone = ZoneScene.Underground;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = ZoneScene.Underground });
        Debug.Log("[SceneLoader] ► Zone Underground (simple)");
    }

    /// <summary>
    /// Décharge Underground et réaffiche MainGrid.
    /// Appelé par CaveManager.ExitCave().
    /// </summary>
    public void ExitUnderground()
    {
        if (IsTransitioning || CurrentZone != ZoneScene.Underground) return;
        StartCoroutine(ExitUndergroundSimple());
    }

    private IEnumerator ExitUndergroundSimple()
    {
        IsTransitioning = true;
        EventBus.Publish(new OnZoneTransitionStart { From = CurrentZone, To = ZoneScene.MainGrid });
        SetMainGridVisible(true);
        var mainS = UnityEngine.SceneManagement.SceneManager.GetSceneByName(SCENE_MAINGRID);
        if (mainS.isLoaded)
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(mainS);
        yield return null;
        CurrentZone = ZoneScene.MainGrid;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = ZoneScene.MainGrid });
        Debug.Log("[SceneLoader] ◄ Retour MainGrid depuis Underground (simple)");
    }

    // ── Surface → Intérieur ──────────────────────────────────────────────────

    /// <summary>
    /// Charge Indoor additivement et masque les GOs de MainGrid.
    /// Appelé par IndoorManager.EnterBuilding().
    /// </summary>
    public void EnterIndoor()
    {
        if (IsTransitioning || CurrentZone == ZoneScene.Indoor) return;
        // PAS de LoadSceneAsync — le contenu indoor est dans _indoorContent (CabaneSetup)
        // On cache juste MainGrid et on publie l'événement
        StartCoroutine(EnterIndoorSimple());
    }

    private IEnumerator EnterIndoorSimple()
    {
        IsTransitioning = true;
        EventBus.Publish(new OnZoneTransitionStart { From = CurrentZone, To = ZoneScene.Indoor });

        // Cacher MainGrid (root GOs + objets dynamiques)
        SetMainGridVisible(false);

        yield return null; // 1 frame pour que tout se stabilise

        CurrentZone = ZoneScene.Indoor;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = ZoneScene.Indoor });
        Debug.Log("[SceneLoader] ► Zone Indoor (sans chargement de scène)");
    }

    /// <summary>
    /// Décharge Indoor et réaffiche MainGrid.
    /// Appelé par IndoorManager.ExitBuilding().
    /// </summary>
    public void ExitIndoor()
    {
        if (IsTransitioning || CurrentZone != ZoneScene.Indoor) return;
        // PAS de UnloadSceneAsync — on réaffiche juste MainGrid
        StartCoroutine(ExitIndoorSimple());
    }

    private IEnumerator ExitIndoorSimple()
    {
        IsTransitioning = true;
        EventBus.Publish(new OnZoneTransitionStart { From = CurrentZone, To = ZoneScene.MainGrid });

        // Réafficher MainGrid (root GOs + objets dynamiques + fog)
        SetMainGridVisible(true);

        // Remettre MainGrid comme scène active
        var mainS = UnityEngine.SceneManagement.SceneManager
                                .GetSceneByName(SCENE_MAINGRID);
        if (mainS.isLoaded)
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(mainS);

        yield return null;

        CurrentZone = ZoneScene.MainGrid;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = ZoneScene.MainGrid });
        Debug.Log("[SceneLoader] ◄ Retour MainGrid (sans déchargement de scène)");
    }

    // ── Reset complet (nouveau run) ───────────────────────────────────────────

    /// <summary>
    /// Recharge MainGrid proprement (nouveau run, mort du héros...).
    /// </summary>
    public void ReloadMainGrid()
    {
        if (IsTransitioning) return;
        StartCoroutine(ReloadMainGridCoroutine());
    }

    // =========================================================================
    // COROUTINES DE TRANSITION
    // =========================================================================

    private IEnumerator EnterZoneCoroutine(string zoneName, ZoneScene newZone)
    {
        IsTransitioning = true;
        EventBus.Publish(new OnZoneTransitionStart { From = CurrentZone, To = newZone });

        // 1. Masquer les GOs de MainGrid (sans décharger la scène — le grid state est préservé)
        SetMainGridVisible(false);

        // 2. Charger la nouvelle scène additivement
        yield return LoadZoneCoroutine(zoneName);

        CurrentZone = newZone;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = newZone });
        Debug.Log($"[SceneLoader] ► Zone active : {newZone}");
    }

    private IEnumerator ExitZoneCoroutine(string zoneName, ZoneScene returnZone)
    {
        IsTransitioning = true;
        EventBus.Publish(new OnZoneTransitionStart { From = CurrentZone, To = returnZone });

        // 1. Décharger la scène de zone alternative
        yield return UnloadZoneCoroutine(zoneName);

        // 2. Réafficher MainGrid
        SetMainGridVisible(true);

        // Remettre MainGrid comme scène active (les spawns futurs iront dedans)
        var mainS = UnityEngine.SceneManagement.SceneManager
                                .GetSceneByName(SCENE_MAINGRID);
        if (mainS.isLoaded)
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(mainS);

        CurrentZone = returnZone;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = returnZone });
        Debug.Log($"[SceneLoader] ◄ Retour surface : {returnZone}");
    }

    private IEnumerator ReloadMainGridCoroutine()
    {
        IsTransitioning = true;

        // Décharger les zones additives si actives
        if (CurrentZone == ZoneScene.Underground)
            yield return UnloadZoneCoroutine(SCENE_UNDERGROUND);
        if (CurrentZone == ZoneScene.Indoor)
            yield return UnloadZoneCoroutine(SCENE_INDOOR);

        // Décharger et recharger MainGrid
        if (SceneManager.GetSceneByName(SCENE_MAINGRID).isLoaded)
            yield return SceneManager.UnloadSceneAsync(SCENE_MAINGRID);

        yield return LoadZoneCoroutine(SCENE_MAINGRID);

        _hiddenMainGridRoots.Clear();
        CurrentZone = ZoneScene.MainGrid;
        IsTransitioning = false;
        EventBus.Publish(new OnZoneTransitionComplete { Zone = ZoneScene.MainGrid });
    }

    // =========================================================================
    // HELPERS — CHARGEMENT / DÉCHARGEMENT
    // =========================================================================

    private IEnumerator LoadZoneCoroutine(string sceneName, System.Action onComplete = null)
    {
        if (SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            onComplete?.Invoke();
            yield break;
        }

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        op.allowSceneActivation = true;

        while (!op.isDone)
            yield return null;

        onComplete?.Invoke();
        Debug.Log($"[SceneLoader] Scène chargée : {sceneName}");
    }

    private IEnumerator UnloadZoneCoroutine(string sceneName)
    {
        if (!SceneManager.GetSceneByName(sceneName).isLoaded)
            yield break;

        var op = SceneManager.UnloadSceneAsync(sceneName);
        while (op != null && !op.isDone)
            yield return null;

        Debug.Log($"[SceneLoader] Scène déchargée : {sceneName}");
    }

    // =========================================================================
    // MASQUAGE / AFFICHAGE DE MAINGRID
    // =========================================================================

    /// <summary>
    /// Cache ou réaffiche tous les root GameObjects de la scène MainGrid.
    /// Remplace le rôle de GridLayerManager.HideSurface/ShowSurface dans ce contexte.
    /// Les objets cachés sont restaurés à leur état exact au retour.
    /// </summary>
    private void SetMainGridVisible(bool visible)
    {
        var mainScene = SceneManager.GetSceneByName(SCENE_MAINGRID);
        if (!mainScene.isLoaded) return;

        if (!visible)
        {
            _hiddenMainGridRoots.Clear();

            // 1. Cacher tous les root GOs de la scène MainGrid
            foreach (var go in mainScene.GetRootGameObjects())
            {
                if (go.activeSelf)
                {
                    go.SetActive(false);
                    _hiddenMainGridRoots.Add(go);
                }
            }

            // 2. Fallback : objets spawnés dynamiquement hors MainGrid
            //    (si la scène active n'était pas MainGrid au moment du spawn)
            HideDynamicSurfaceObjects();

            // 3. Overlays FogOfWar (racine scène, pas enfants de FogOfWar)
            FogOfWar.Instance?.HideAll();

            Debug.Log($"[SceneLoader] MainGrid masquée : {_hiddenMainGridRoots.Count} GOs root + " +
                      $"{_hiddenDynamicGOs.Count} GOs dynamiques cachés.");

            // Log les types des objets dynamiques cachés pour debug
            foreach (var go in _hiddenDynamicGOs)
                Debug.Log($"  → caché dynamique : {go.name}");
        }
        else
        {
            // Restaurer les GOs cachés
            int restoredR = 0, restoredD = 0, nullR = 0, nullD = 0;
            foreach (var go in _hiddenMainGridRoots)
            {
                if (go != null) { go.SetActive(true); restoredR++; }
                else nullR++;
            }
            _hiddenMainGridRoots.Clear();

            foreach (var go in _hiddenDynamicGOs)
            {
                if (go != null) { go.SetActive(true); restoredD++; }
                else nullD++;
            }
            _hiddenDynamicGOs.Clear();

            FogOfWar.Instance?.ShowAll();

            Debug.Log($"[SceneLoader] MainGrid réaffichée : " +
                      $"{restoredR} root ({nullR} null) + " +
                      $"{restoredD} dynamiques ({nullD} null)");
        }
    }

    // =========================================================================
    // UTILITAIRES
    // =========================================================================

    /// <summary>
    /// Vérifie si une scène est active dans la hiérarchie.
    /// </summary>
    public bool IsSceneLoaded(string sceneName) =>
        SceneManager.GetSceneByName(sceneName).isLoaded;

    /// <summary>
    /// Déplace un GameObject dans une scène cible (utile pour le héros).
    /// </summary>
    public static void MoveToScene(GameObject go, string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        if (scene.isLoaded)
            SceneManager.MoveGameObjectToScene(go, scene);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EVENTS — transitions de scène
// ─────────────────────────────────────────────────────────────────────────────

public struct OnZoneTransitionStart
{
    public SceneLoader.ZoneScene From, To;
}

public struct OnZoneTransitionComplete
{
    public SceneLoader.ZoneScene Zone;
}