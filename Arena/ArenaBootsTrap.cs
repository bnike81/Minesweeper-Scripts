using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ArenaBootstrap — Charge les scènes dépendantes au démarrage de TestArena.
///
/// La scène TestArena.unity ne contient QUE :
///   - MainCamera (optionnel — Core apporte la sienne)
///   - ArenaRoot (avec CombatArena, ArenaGrid, ArenaEnemyPlacer, ArenaBootstrap)
///
/// Au Play, ArenaBootstrap charge en additive les scènes qui portent :
///   - Core (EventBus, TurnManager, PlayerStats, EnemyDatabase, GameModeConfig)
///   - Hero (HeroController, HeroCombat, HeroState, HeroJump, HeroPathfinder,
///           Equipment, Inventory)
///   - UI (HUDView, HeartBarUI, XPBarUI, notifications)
///
/// ─── ORDRE DE CHARGEMENT (CRITIQUE) ───
///   Core → Hero → UI (dans cet ordre !)
///
///   Pourquoi : HeartBarUI (dans UI) lit MaxHP via PlayerStats + Equipment.
///   Si UI charge avant Hero, Equipment n existe pas encore → MaxHP non buffe
///   → la barre de coeurs ne se construit pas correctement.
///
///   En chargeant Hero d abord, Equipment est pret quand HeartBarUI.Start
///   appelle RefreshFromStats, les coeurs s affichent correctement.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ArenaBootstrap : MonoBehaviour
{
    [Header("Scènes à charger en additive (dans l ordre critique)")]
    [Tooltip("Scène Core : EventBus, TurnManager, PlayerStats, EnemyDatabase. OBLIGATOIRE.")]
    public string coreSceneName = "Core";

    [Tooltip("Scène Hero : HeroController + Equipment + Inventory. OBLIGATOIRE. " +
             "Doit etre chargee AVANT UI pour que HeartBarUI lise un MaxHP correct.")]
    public string heroSceneName = "Hero";

    [Tooltip("Scène UI : HUD, HeartBar, notifications. Chargee en DERNIER.")]
    public string uiSceneName = "UI";

    [Header("Options")]
    [Tooltip("Si vrai, log la progression du chargement.")]
    public bool verbose = true;

    [Tooltip("Si vrai, active automatiquement les Main Camera des scènes " +
             "additives au chargement.")]
    public bool enableAdditiveCameras = true;

    private IEnumerator Start()
    {
        if (verbose) Debug.Log("<color=#00FF88>[ArenaBootstrap]</color> Loading additive scenes...");

        // ── ORDRE CRITIQUE : Core → Hero → UI ──
        // Core : PlayerStats, EventBus, TurnManager, etc.
        yield return LoadSceneIfExists(coreSceneName);

        // Hero : HeroController + Equipment (buffs MaxHP)
        // DOIT etre avant UI pour que HeartBarUI lise un MaxHP buffe
        yield return LoadSceneIfExists(heroSceneName);

        // UI : HeartBarUI, HUD, etc. (depend de PlayerStats + Equipment)
        yield return LoadSceneIfExists(uiSceneName);

        if (verbose) Debug.Log("<color=#00FF88>[ArenaBootstrap]</color> All scenes loaded.");

        // Donner 1 frame pour que les Awake/Start des scènes additives se jouent
        yield return null;

        if (enableAdditiveCameras) EnableAdditiveCameras();

        // ─── v13 FIX : initialiser PlayerStats + refresh HUD ───
        //
        // CAUSE RACINE DECOUVERTE :
        //   PlayerStats est dans la scene Hero (pas Core). Son Awake ne setup
        //   PAS les stats — elles sont initialisees via ResetStats() declenche
        //   par l event OnRunStarted.
        //
        //   Or GameManager.StartNewRun publie OnRunStarted PENDANT le chargement
        //   de Core, bien AVANT que Hero (et PlayerStats) soit chargee. Donc
        //   PlayerStats n a jamais recu l event, _currentMaxHP est reste a 0,
        //   et HeartBarUI cree 0 coeurs.
        //
        //   Fix : appeler ResetStats() directement une fois Hero charge.
        //   Plus sur que republier OnRunStarted (eviter d autres effets de bord
        //   comme GridManager qui regenererait une grille).
        yield return null;
        yield return null;

        var ps = PlayerStats.Instance;
        if (ps != null)
        {
            ps.ResetStats();
            if (verbose) Debug.Log($"<color=#00FF88>[ArenaBootstrap]</color> " +
                                   $"PlayerStats.ResetStats appele — MaxHP={ps.MaxHP}");
        }
        else
        {
            Debug.LogWarning("<color=#FF8800>[ArenaBootstrap]</color> PlayerStats.Instance null");
        }

        // HeartBarUI ecoute OnHUDRefreshRequest → RebuildHearts avec le MaxHP
        // maintenant correct.
        EventBus.Publish(new OnHUDRefreshRequest());
        if (verbose) Debug.Log("<color=#00FF88>[ArenaBootstrap]</color> HUD refresh publie");

        // ─── Diagnostic complet du HUD ───
        yield return null;
        DiagnosticHUD();

        // CombatArena.Start() se déclenche ensuite et attend lui-même
        // HeroController.Instance et TurnManager.Instance.
    }

    /// <summary>
    /// Log l etat du HUD pour identifier pourquoi la Heart Bar n apparait pas.
    /// </summary>
    private void DiagnosticHUD()
    {
        Debug.Log("<color=#FFFF00>═══════ DIAGNOSTIC HUD ═══════</color>");

        // PlayerStats
        var ps = PlayerStats.Instance;
        if (ps == null)
            Debug.LogError("[Diag] PlayerStats.Instance = NULL !");
        else
            Debug.Log($"[Diag] PlayerStats OK — MaxHP={ps.MaxHP} CurrentHP={ps.CurrentHP} " +
                      $"scene='{ps.gameObject.scene.name}'");

        // Equipment
        var eq = Equipment.Instance;
        if (eq == null)
            Debug.LogWarning("[Diag] Equipment.Instance = NULL (normal si pas encore de systeme)");
        else
            Debug.Log($"[Diag] Equipment OK — Defense={eq.Defense} scene='{eq.gameObject.scene.name}'");

        // HeartBarUI
        var hb = HeartBarUI.Instance;
        if (hb == null)
            Debug.LogError("[Diag] HeartBarUI.Instance = NULL ! " +
                           "→ HeartBarUI n est pas dans la scene UI ou pas actif.");
        else
        {
            Debug.Log($"[Diag] HeartBarUI OK — scene='{hb.gameObject.scene.name}' " +
                      $"active={hb.gameObject.activeInHierarchy}");

            // Verifier les champs via reflection (prives serialises)
            var type = typeof(HeartBarUI);
            var heartPrefab = type.GetField("_heartPrefab",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var heartFull = type.GetField("_heartFull",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (heartPrefab != null)
            {
                var val = heartPrefab.GetValue(hb);
                Debug.Log($"[Diag] HeartBarUI._heartPrefab = {(val == null ? "NULL !" : "OK")}");
            }
            if (heartFull != null)
            {
                var val = heartFull.GetValue(hb);
                Debug.Log($"[Diag] HeartBarUI._heartFull = {(val == null ? "NULL !" : "OK")}");
            }

            // Compter les enfants (cœurs instanciés)
            int childCount = hb.transform.childCount;
            Debug.Log($"[Diag] HeartBarUI a {childCount} enfants (coeurs instancies)");
        }

        // Canvas
        var canvases = FindObjectsOfType<Canvas>(true);
        foreach (var c in canvases)
        {
            Debug.Log($"[Diag] Canvas '{c.name}' scene='{c.gameObject.scene.name}' " +
                      $"active={c.gameObject.activeInHierarchy} renderMode={c.renderMode} " +
                      $"camera={(c.worldCamera != null ? c.worldCamera.name : "null")}");
        }

        Debug.Log("<color=#FFFF00>═══════ FIN DIAGNOSTIC ═══════</color>");
    }

    private IEnumerator LoadSceneIfExists(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) yield break;

        var existing = SceneManager.GetSceneByName(sceneName);
        if (existing.IsValid() && existing.isLoaded)
        {
            if (verbose) Debug.Log($"<color=#00FF88>[ArenaBootstrap]</color> {sceneName} déjà chargée");
            yield break;
        }

        bool inBuild = false;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            var path = SceneUtility.GetScenePathByBuildIndex(i);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                inBuild = true;
                break;
            }
        }

        if (!inBuild)
        {
            Debug.LogWarning($"<color=#FF8800>[ArenaBootstrap]</color> Scène '{sceneName}' " +
                             $"absente des Build Settings — skip");
            yield break;
        }

        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (op != null && !op.isDone)
            yield return null;

        if (verbose) Debug.Log($"<color=#00FF88>[ArenaBootstrap]</color> {sceneName} chargée");
    }

    /// <summary>
    /// Active automatiquement les Main Camera présentes dans les scènes additives
    /// au cas où elles sont décochées par défaut dans l éditeur.
    /// </summary>
    private void EnableAdditiveCameras()
    {
        var myScene = gameObject.scene;
        int enabled = 0;

        var allCams = FindObjectsOfType<Camera>(true);
        foreach (var cam in allCams)
        {
            if (cam == null) continue;
            if (cam.gameObject.scene == myScene) continue;

            bool changed = false;

            if (!cam.gameObject.activeSelf)
            {
                cam.gameObject.SetActive(true);
                changed = true;
            }

            if (!cam.enabled)
            {
                cam.enabled = true;
                changed = true;
            }

            var al = cam.GetComponent<AudioListener>();
            if (al != null && !al.enabled)
            {
                al.enabled = true;
                changed = true;
            }

            if (changed)
            {
                enabled++;
                if (verbose) Debug.Log($"<color=#00FF88>[ArenaBootstrap]</color> Camera activée : " +
                                        $"{cam.name} (scène '{cam.gameObject.scene.name}')");
            }
        }

        if (verbose && enabled == 0)
            Debug.Log("<color=#00FF88>[ArenaBootstrap]</color> Aucune caméra à activer");
    }
}