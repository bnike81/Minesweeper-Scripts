using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// TurnManager — Cycle tour par tour.
///
/// ETATS :
///   Inactive  — mode libre, pas de tour par tour
///   HeroTurn  — le joueur peut agir (un clic = une action)
///   EnemyTurn — chaque ennemi actif joue en sequence
///
/// ACTIVATION :
///   Quand un ennemi melee est revele ET le heros est dans son perceptionRadius.
///   Necessite GameModeConfig.CombatEnabled = true.
///
/// DESACTIVATION :
///   Apres le tour des ennemis, si aucun ennemi revele n a le heros
///   dans son perceptionRadius → retour Inactive.
///
/// FIX v5 :
///   1. DelayedActivationCheck — si le heros est adjacent (Chebyshev ≤ 1)
///      a un ennemi actif, enemyTurnFirst=true → l ennemi attaque immediatement.
///   2. OnHeroActionDone — verrouille IsUILocked IMMEDIATEMENT avant de lancer
///      EnemyTurnSequence, pour empecher le heros de double-agir dans la fenetre
///      entre SetLocked(false) de HeroCombat et le debut de EnemyTurnSequence.
///
/// SETUP : Creer un GO "TurnManager" dans la scene Core.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    // =========================================================================
    // Etat
    // =========================================================================

    [Header("=== Debug ===")]
    [SerializeField] private bool _logTurns = true;

    private TurnState _state = TurnState.Inactive;
    public TurnState State => _state;

    /// <summary>True quand le TurnManager gere le combat.</summary>
    public bool IsActive => _state != TurnState.Inactive;

    /// <summary>True quand c est au heros de jouer.</summary>
    public bool IsHeroTurn => _state == TurnState.HeroTurn;

    // Liste des ennemis actifs ce tour (reveles, melee, heros dans perception)
    private readonly List<EnemyInstance> _activeEnemies = new List<EnemyInstance>();

    // Ennemis qui etaient actifs au tour precedent (pour detecter perte de vue individuelle)
    private readonly HashSet<EnemyInstance> _previousActive = new HashSet<EnemyInstance>();

    // =========================================================================
    // Init
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[TurnManager] Awake — Instance OK");
    }

    private void OnEnable()
    {
        EventBus.Subscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Subscribe<OnHeroActionComplete>(OnHeroActionDone);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<OnCellsRevealed>(OnCellsRevealed);
        EventBus.Unsubscribe<OnHeroActionComplete>(OnHeroActionDone);
    }

    // =========================================================================
    // Activation — sur revelation d ennemi
    // =========================================================================

    private Coroutine _pendingActivation;

    private void OnCellsRevealed(OnCellsRevealed evt)
    {
        // Ne rien faire si combat desactive dans GameModeConfig
        var gmc = GameModeConfig.Instance;
        if (gmc != null && !gmc.CombatEnabled) return;
        if (_state != TurnState.Inactive) return;

        // Attendre 1 frame pour que les EnemyInstance soient spawnes
        if (_pendingActivation == null)
            _pendingActivation = StartCoroutine(DelayedActivationCheck());
    }

    /// <summary>
    /// FIX v5 : verifie si le heros est adjacent (Chebyshev ≤ 1) a un ennemi actif.
    /// Si oui, enemyTurnFirst=true → l ennemi attaque immediatement apres le "!".
    /// Sinon, le heros joue en premier (comportement classique de decouverte).
    /// </summary>
    private IEnumerator DelayedActivationCheck()
    {
        yield return null; // laisser Awake() des ennemis s executer

        _pendingActivation = null;
        if (_state != TurnState.Inactive) yield break;

        Log("Check activation → EnemyInstance.All=" + EnemyInstance.All.Count
            + ", EnemyDB=" + (EnemyDatabase.Instance != null)
            + ", Hero=" + (HeroController.Instance != null));

        if (!HasActiveEnemy())
        {
            Log("Check activation → aucun ennemi melee actif");
            yield break;
        }

        // ── v5 : verifier si le heros est adjacent a un ennemi actif ──
        bool heroAdjacent = IsHeroAdjacentToActiveEnemy();
        if (heroAdjacent)
            Log("Heros ADJACENT a un ennemi actif → ennemis jouent d abord");

        yield return StartCoroutine(
            ActivateCombatWithIndicators(enemyTurnFirst: heroAdjacent));
    }

    /// <summary>
    /// Affiche "!" sur les ennemis actifs, attend la fin de l animation,
    /// puis passe en HeroTurn ou EnemyTurn.
    /// enemyTurnFirst=true : les ennemis agissent immediatement (reactivation ou adjacent).
    /// enemyTurnFirst=false : le heros joue d abord (decouverte a distance).
    /// </summary>
    private IEnumerator ActivateCombatWithIndicators(bool enemyTurnFirst = false)
    {
        Log("Combat active — " + _activeEnemies.Count + " ennemi(s) melee en vue"
            + (enemyTurnFirst ? " (ennemis jouent d abord)" : ""));

        // Initialiser le tracking : les ennemis actifs maintenant sont "connus"
        _previousActive.Clear();
        foreach (var ei in _activeEnemies)
            if (ei != null && !ei.IsDead) _previousActive.Add(ei);

        // Lancer les "!" et les animations alert en parallele sur tous les ennemis
        var coroutines = new List<Coroutine>();
        foreach (var ei in _activeEnemies)
        {
            if (ei == null || ei.IsDead) continue;
            var beh = ei.GetComponent<EnemyBehaviour>();
            if (beh != null)
            {
                // "!" texte au dessus
                coroutines.Add(StartCoroutine(beh.ShowAlertIndicatorAndWait()));
                // Animation alert propre a l ennemi (loup alert, etc.)
                coroutines.Add(StartCoroutine(beh.PlayAlertPose()));
            }
        }

        // Attendre que toutes les animations soient terminees
        foreach (var c in coroutines)
            yield return c;

        if (enemyTurnFirst)
        {
            // Ennemis jouent en premier (adjacent a la decouverte, ou reactivation)
            yield return StartCoroutine(EnemyTurnSequence());
        }
        else
        {
            // Decouverte a distance : le heros choisit son action
            SetState(TurnState.HeroTurn);
        }
    }

    // =========================================================================
    // Cycle de tours
    // =========================================================================

    private bool _enemyTurnRunning;

    /// <summary>
    /// FIX v5 : verrouille IsUILocked IMMEDIATEMENT avant de lancer
    /// EnemyTurnSequence. Cela empeche le heros de cliquer pendant
    /// la fenetre entre SetLocked(false) de HeroCombat et le debut
    /// effectif de EnemyTurnSequence.
    /// </summary>
    private void OnHeroActionDone(OnHeroActionComplete evt)
    {
        // En mode Inactive : verifier si le combat doit se reactiver
        if (_state == TurnState.Inactive)
        {
            var gmc = GameModeConfig.Instance;
            if (gmc != null && !gmc.CombatEnabled) return;
            if (_pendingActivation != null) return; // deja en cours

            if (HasActiveEnemy())
            {
                Log("Reactivation combat — " + _activeEnemies.Count + " ennemi(s) en vue");
                StartCoroutine(ActivateCombatWithIndicators(enemyTurnFirst: true));
            }
            return;
        }

        if (_state != TurnState.HeroTurn) return;
        if (_enemyTurnRunning) return; // empecher les tours en double

        // ── v5 : verrouiller IMMEDIATEMENT ──
        // Empeche le heros de double-agir entre SetLocked(false) de HeroCombat
        // et le yield dans EnemyTurnSequence qui pose aussi IsUILocked=true.
        var hs = HeroState.Instance;
        if (hs != null) hs.IsUILocked = true;

        Log("Action hero terminee → lancement tour ennemi");

        // Lancer le tour ennemi
        StartCoroutine(EnemyTurnSequence());
    }

    private IEnumerator EnemyTurnSequence()
    {
        _enemyTurnRunning = true;
        SetState(TurnState.EnemyTurn);

        // Filet de securite : deverrouiller apres 15s si crash
        var safety = StartCoroutine(EnsureUnlockAfterDelay(15f));

        // Verrouiller le heros (redondant avec OnHeroActionDone mais securite)
        var hs = HeroState.Instance;
        if (hs != null) hs.IsUILocked = true;

        // Collecter les ennemis actifs MAINTENANT
        RefreshActiveEnemies();

        // --- Detecter les ennemis qui viennent de perdre la vue (per-enemy "?") ---
        var lostSight = new List<EnemyInstance>();
        foreach (var prev in _previousActive)
        {
            if (prev == null || prev.IsDead) continue;
            if (!_activeEnemies.Contains(prev))
                lostSight.Add(prev);
        }

        // Afficher "?" individuellement sur ceux qui ont perdu la vue
        if (lostSight.Count > 0)
        {
            Log("Perte de vue individuelle — " + lostSight.Count + " ennemi(s)");
            var lostCoroutines = new List<Coroutine>();
            foreach (var ei in lostSight)
            {
                var beh = ei.GetComponent<EnemyBehaviour>();
                if (beh != null)
                    lostCoroutines.Add(StartCoroutine(
                        beh.ShowLostIndicatorAndWait()));
            }
            foreach (var c in lostCoroutines)
                yield return c;
        }

        // Mettre a jour _previousActive avec la liste actuelle
        _previousActive.Clear();
        foreach (var ei in _activeEnemies)
            if (ei != null && !ei.IsDead) _previousActive.Add(ei);

        Log("Tour ennemi — " + _activeEnemies.Count + " ennemi(s) actif(s)");

        // Si plus aucun ennemi actif apres le "?" → retour Inactive directement
        if (_activeEnemies.Count == 0)
        {
            Log("Plus d ennemi en vue — retour mode libre");
            if (safety != null) StopCoroutine(safety);
            if (hs != null) hs.IsUILocked = false;
            _enemyTurnRunning = false;
            EventBus.Publish(new OnEnemyTurnComplete());
            SetState(TurnState.Inactive);
            yield break;
        }

        // Chaque ennemi actif agit en sequence
        for (int i = 0; i < _activeEnemies.Count; i++)
        {
            var ei = _activeEnemies[i];
            if (ei == null || ei.IsDead) continue;

            var beh = ei.GetComponent<EnemyBehaviour>();
            if (beh != null)
            {
                yield return StartCoroutine(beh.TakeTurn());
            }

            // Petit delai entre chaque ennemi pour la lisibilite
            if (i < _activeEnemies.Count - 1)
                yield return WaitCache.Get(0.15f);
        }

        // Annuler le filet de securite — tour termine normalement
        if (safety != null) StopCoroutine(safety);

        // Deverrouiller le heros — TOUJOURS, meme si erreur
        if (hs != null) hs.IsUILocked = false;
        _enemyTurnRunning = false;

        // ── FIX v6 : diagnostic complet apres tour ennemi ──
        var hc = HeroCombat.Instance;
        Log("Fin tour ennemi — IsUILocked=" + (hs != null ? hs.IsUILocked.ToString() : "null")
            + " IsMoving=" + (hs != null ? hs.IsMoving.ToString() : "null")
            + " IsCalculating=" + (hs != null ? hs.IsCalculating.ToString() : "null")
            + " IsJumping=" + (hs != null ? hs.IsJumping.ToString() : "null")
            + " HeroCombat.IsAnimating=" + (hc != null ? hc.IsAnimating.ToString() : "null")
            + " HeroCombat.IsInCombat=" + (hc != null ? hc.IsInCombat.ToString() : "null")
            + " CanMove=" + (hs != null ? hs.CanMove.ToString() : "null"));

        // Publier fin de tour ennemi (regen stamina)
        EventBus.Publish(new OnEnemyTurnComplete());

        // Notifier le roaming qu un tour de combat est termine
        EnemyRoamingSystem.Instance?.NotifyCombatTurnComplete();

        // Verifier si on reste en combat (2e refresh apres les mouvements ennemis)
        RefreshActiveEnemies();

        // Detecter ceux qui ont perdu la vue APRES leur tour (ex: hero a bouge)
        var lostAfterTurn = new List<EnemyInstance>();
        foreach (var prev in _previousActive)
        {
            if (prev == null || prev.IsDead) continue;
            if (!_activeEnemies.Contains(prev))
                lostAfterTurn.Add(prev);
        }
        if (lostAfterTurn.Count > 0)
        {
            Log("Perte de vue post-tour — " + lostAfterTurn.Count + " ennemi(s)");
            var postCoroutines = new List<Coroutine>();
            foreach (var ei in lostAfterTurn)
            {
                var beh = ei.GetComponent<EnemyBehaviour>();
                if (beh != null)
                    postCoroutines.Add(StartCoroutine(
                        beh.ShowLostIndicatorAndWait()));
            }
            foreach (var c in postCoroutines)
                yield return c;
        }

        // Mettre a jour pour le prochain tour
        _previousActive.Clear();
        foreach (var ei in _activeEnemies)
            if (ei != null && !ei.IsDead) _previousActive.Add(ei);

        if (_activeEnemies.Count == 0)
        {
            Log("Plus d ennemi en vue — retour mode libre");
            SetState(TurnState.Inactive);
        }
        else
        {
            SetState(TurnState.HeroTurn);
        }
    }

    // =========================================================================
    // Gestion des ennemis actifs
    // =========================================================================

    /// <summary>
    /// Rafraichit la liste des ennemis actifs :
    /// reveles, vivants, melee, heros dans leur perceptionRadius.
    /// </summary>
    private void RefreshActiveEnemies()
    {
        _activeEnemies.Clear();

        var hero = HeroController.Instance;
        if (hero == null) { Log("RefreshActive → Hero null"); return; }
        var heroPos = hero.GridPosition;

        foreach (var ei in EnemyInstance.All)
        {
            if (ei == null || ei.IsDead) continue;

            // Verifier que l ennemi est melee
            var data = EnemyDatabase.Instance?.Get(ei.EnemyType);
            if (data == null)
            {
                Log("RefreshActive → " + ei.EnemyType + " pas dans EnemyDatabase");
                continue;
            }
            if (data.behavior != EnemyBehaviorType.Melee)
            {
                Log("RefreshActive → " + data.displayName + " pas Melee (" + data.behavior + ")");
                continue;
            }

            // Verifier que le heros est dans le perception radius (Manhattan)
            int dist = Mathf.Abs(heroPos.x - ei.GridX)
                     + Mathf.Abs(heroPos.y - ei.GridY);
            if (dist > data.perceptionRadius)
            {
                Log("RefreshActive → " + data.displayName + " hors perception (dist="
                    + dist + ", radius=" + data.perceptionRadius + ")");
                continue;
            }

            Log("RefreshActive → " + data.displayName + " ACTIF (dist=" + dist + ")");
            _activeEnemies.Add(ei);
        }
    }

    /// <summary>Y a-t-il au moins un ennemi melee actif ?</summary>
    private bool HasActiveEnemy()
    {
        RefreshActiveEnemies();
        return _activeEnemies.Count > 0;
    }

    /// <summary>
    /// v5 : verifie si le heros est adjacent (Chebyshev ≤ 1) a un ennemi actif.
    /// Utilise pour decider si les ennemis attaquent en premier a la decouverte.
    /// </summary>
    private bool IsHeroAdjacentToActiveEnemy()
    {
        var hero = HeroController.Instance;
        if (hero == null) return false;
        var heroPos = hero.GridPosition;

        foreach (var ei in _activeEnemies)
        {
            if (ei == null || ei.IsDead) continue;
            int chebyDist = Mathf.Max(
                Mathf.Abs(heroPos.x - ei.GridX),
                Mathf.Abs(heroPos.y - ei.GridY));
            if (chebyDist <= 1) return true;
        }
        return false;
    }

    // =========================================================================
    // Changement d etat
    // =========================================================================

    private void SetState(TurnState newState)
    {
        if (_state == newState) return;
        var old = _state;
        _state = newState;

        Log("Etat: " + old + " → " + newState);

        EventBus.Publish(new OnTurnStateChanged
        { OldState = old, NewState = newState });
    }

    // =========================================================================
    // Utilitaire
    // =========================================================================

    /// <summary>
    /// Filet de securite : si apres 15s le tour ennemi n est pas fini,
    /// force le deverrouillage du heros.
    /// </summary>
    private IEnumerator EnsureUnlockAfterDelay(float maxWait)
    {
        yield return new WaitForSeconds(maxWait);
        if (_enemyTurnRunning)
        {
            Debug.LogWarning("[TurnManager] Filet de securite : tour ennemi force a finir apres " + maxWait + "s");
            var hs = HeroState.Instance;
            if (hs != null) hs.IsUILocked = false;
            _enemyTurnRunning = false;
            SetState(TurnState.HeroTurn);
        }
    }

    private void Log(string msg)
    {
        if (_logTurns) Debug.Log("[TurnManager] " + msg);
    }
}